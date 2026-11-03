#!/usr/bin/env python3
"""Build a deterministic toolkit tarball from an exact, clean committed package.

Other repository files may be dirty. This script never invokes Unity, edits the
package, removes a consumer Library, or changes historical evidence in place.
"""
import argparse
import datetime
import gzip
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import platform
import re
import shutil
import subprocess
import tarfile
import zlib


ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "Packages/com.worldweaver.toolkit"
PREFIX = "Packages/com.worldweaver.toolkit/"
sha = lambda raw: hashlib.sha256(raw).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def inside_root(value):
    path = Path(value)
    path = (path if path.is_absolute() else ROOT / path).resolve()
    require(path != ROOT and ROOT in path.parents, "Artifact path must remain inside the repository: " + str(path))
    return path


def relative(path):
    return path.relative_to(ROOT).as_posix()


def git(*args, input=None):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], input=input)


def preserve(path, raw):
    if path.exists():
        require(path.read_bytes() == raw, "Historical artifact already differs; preserving it: " + relative(path))
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)


def committed_package(commit):
    entries = {}
    for row in git("ls-tree", "-r", "-z", commit, "--", PREFIX.rstrip("/")).split(b"\0"):
        if not row:
            continue
        head, raw_name = row.split(b"\t", 1)
        mode, kind, oid = head.decode("ascii").split()
        name = raw_name.decode("utf-8")
        require(mode in ("100644", "100755") and kind == "blob" and name.startswith(PREFIX), "Non-regular committed package entry: " + name)
        name = name[len(PREFIX):]
        require(PurePosixPath(name).name != ".DS_Store", "A tracked .DS_Store cannot enter the archive.")
        entries[name] = oid
    require(entries, "The source commit contains no toolkit package.")
    objects = sorted(set(entries.values()))
    batch = git("cat-file", "--batch", input=("\n".join(objects) + "\n").encode("ascii"))
    cursor, blobs = 0, {}
    for oid in objects:
        end = batch.index(b"\n", cursor)
        actual, kind, size_text = batch[cursor:end].decode("ascii").split()
        size = int(size_text)
        require(actual == oid and kind == "blob", "Unexpected Git blob response.")
        cursor = end + 1
        blobs[oid] = batch[cursor:cursor + size]
        cursor += size
        require(batch[cursor:cursor + 1] == b"\n", "Truncated Git blob response.")
        cursor += 1
    require(cursor == len(batch), "Unexpected trailing Git blob data.")
    return {name: blobs[oid] for name, oid in entries.items()}


def current_package():
    require(not PACKAGE.is_symlink(), "The package root cannot be a symlink.")
    payloads = {}
    for path in PACKAGE.rglob("*"):
        require(not path.is_symlink(), "Symlinked package input rejected: " + str(path))
        if path.name == ".DS_Store" or "__pycache__" in path.parts or path.suffix == ".pyc":
            continue
        if path.is_file():
            payloads[path.relative_to(PACKAGE).as_posix()] = path.read_bytes()
        else:
            require(path.is_dir(), "Non-regular package input rejected: " + str(path))
    return payloads


def archive_bytes(payloads, epoch):
    directories = {"package"}
    for name in payloads:
        directories.update("package/" + str(parent) for parent in PurePosixPath(name).parents if str(parent) != ".")
    output = io.BytesIO()
    with gzip.GzipFile(filename="", fileobj=output, mode="wb", compresslevel=9, mtime=epoch) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
            for name in sorted(directories):
                entry = tarfile.TarInfo(name)
                entry.type, entry.mode = tarfile.DIRTYPE, 0o755
                entry.uid = entry.gid = 0
                entry.uname = entry.gname = ""
                entry.mtime = epoch
                archive.addfile(entry)
            for name, raw in sorted(payloads.items()):
                entry = tarfile.TarInfo("package/" + name)
                entry.size, entry.mode = len(raw), 0o644
                entry.uid = entry.gid = 0
                entry.uname = entry.gname = ""
                entry.mtime = epoch
                archive.addfile(entry, io.BytesIO(raw))
    packed = output.getvalue()
    with tarfile.open(fileobj=io.BytesIO(packed), mode="r:gz") as archive:
        seen, unpacked = set(), {}
        for entry in archive.getmembers():
            require(entry.name not in seen, "Duplicate archive member.")
            seen.add(entry.name)
            parts = PurePosixPath(entry.name)
            require(not parts.is_absolute() and ".." not in parts.parts and parts.parts[0] == "package", "Escaping archive member.")
            require(entry.isfile() or entry.isdir(), "Non-regular archive member.")
            require(entry.uid == entry.gid == 0 and entry.uname == entry.gname == "" and entry.mtime == epoch, "Incorrect archive owner/timestamp.")
            require(entry.mode == (0o755 if entry.isdir() else 0o644), "Incorrect archive permissions.")
            if entry.isfile():
                unpacked[entry.name[len("package/"):]] = archive.extractfile(entry).read()
    require(unpacked == payloads, "Unpacked bytes differ from the committed package.")
    return packed, len(directories)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-commit", required=True, help="Full 40-character frozen package commit")
    parser.add_argument("--snapshot-manifest", required=True)
    parser.add_argument("--revision", required=True, help="Evidence revision, for example r9")
    parser.add_argument("--previous-revision", help="Preserve previous canonical TGZ/proof verbatim with this suffix")
    parser.add_argument("--consumer", help="Create a new, Library-free consumer; must not already exist")
    parser.add_argument("--archive", default="deliverables/com.worldweaver.toolkit-1.3.0.tgz")
    parser.add_argument("--proof", default="results/upm-package-1.3.json")
    parser.add_argument("--verify-only", action="store_true", help="Check existing archive against rebuilt bytes without writing anything")
    args = parser.parse_args()
    require(re.fullmatch(r"[0-9a-f]{40}", args.source_commit) is not None, "Supply a full lowercase source commit.")
    require(re.fullmatch(r"r[1-9][0-9]*", args.revision) is not None, "Invalid evidence revision.")
    require(args.previous_revision is None or re.fullmatch(r"r[1-9][0-9]*", args.previous_revision) is not None, "Invalid previous revision.")
    require(git("rev-parse", "--verify", args.source_commit + "^{commit}").decode().strip() == args.source_commit, "The source commit does not resolve exactly.")
    manifest_path, output, proof_path = map(inside_root, (args.snapshot_manifest, args.archive, args.proof))
    snapshot = json.loads(manifest_path.read_text())
    require(snapshot["sourceCommit"] == args.source_commit, "Snapshot source commit differs from the requested commit.")
    expected = {row["path"]: row for row in snapshot["files"]}
    require(len(expected) == len(snapshot["files"]) == snapshot["fileCount"], "Duplicate or inconsistent snapshot file count.")
    payloads = committed_package(args.source_commit)
    require(payloads == current_package(), "Working package bytes/path set differ from the frozen Git commit; other dirty repository files are allowed.")
    require(subprocess.run(["git", "-C", str(ROOT), "diff", "--quiet", args.source_commit, "--", PREFIX.rstrip("/")]).returncode == 0, "The package has tracked changes relative to the frozen source commit.")
    require(set(payloads) == set(expected), "Git/current and snapshot package path sets differ.")
    require(all(sha(raw) == expected[name]["sha256"] and len(raw) == expected[name]["bytes"] for name, raw in payloads.items()), "Git/current bytes differ from the snapshot manifest.")
    tree = sha("".join(name + "\0" + sha(payloads[name]) + "\n" for name in sorted(payloads)).encode())
    require(tree == snapshot["canonicalOrdinalNulTreeSha256"], "Canonical snapshot tree digest differs.")
    package_info = json.loads(payloads["package.json"])
    require(package_info["name"] == "com.worldweaver.toolkit" and package_info["version"] == snapshot["packageVersion"], "Package identity/version differs from the snapshot.")
    require(sum(map(len, payloads.values())) <= 24 * 1024 * 1024, "Source payload exceeds the bounded clean-audit archive budget.")
    epoch = int(git("show", "-s", "--format=%ct", args.source_commit).strip())
    packed, directory_count = archive_bytes(payloads, epoch)
    require(packed == archive_bytes(payloads, epoch)[0], "Two deterministic builds differed.")
    if args.verify_only:
        require(output.read_bytes() == packed, "Existing archive differs from this exact deterministic rebuild.")
        print(json.dumps({"status": "existing_archive_rebuilt_byte_equal", "sourceCommit": args.source_commit, "sha256": sha(packed), "fileCount": len(payloads)}))
        return
    require(args.consumer is not None, "Supply --consumer for a new build, or use --verify-only.")
    consumer = inside_root(args.consumer)
    require(not consumer.exists(), "The fresh consumer already exists; no Library or consumer assets will be removed.")
    previous = None
    if output.exists() or proof_path.exists():
        require(output.exists() and proof_path.exists() and args.previous_revision is not None, "Existing canonical artifacts require --previous-revision preservation.")
        old_raw = proof_path.read_bytes()
        old = json.loads(old_raw)
        require(old["sourceCommit"] != args.source_commit and sha(output.read_bytes()) == old["sha256"], "Previous canonical archive/proof do not match or are already this revision.")
        old_archive = output.with_name(output.stem + "-" + args.previous_revision + output.suffix)
        old_receipt = proof_path.with_name(proof_path.stem + "-" + args.previous_revision + proof_path.suffix)
        preserve(old_archive, output.read_bytes())
        preserve(old_receipt, old_raw)
        previous = {"revision": args.previous_revision, "sourceCommit": old["sourceCommit"], "archive": relative(old_archive), "sha256": old["sha256"], "receipt": relative(old_receipt), "receiptSha256": sha(old_raw), "receiptPreservedVerbatim": True,
                    "note": "The historical receipt retains its original canonical archive path; the preserved historical TGZ is explicitly located in this record."}
    settings = ROOT / "UnityProject/ProjectSettings"
    require(not settings.is_symlink() and not any(path.is_symlink() for path in settings.rglob("*")), "Symlinked project settings rejected.")
    harness = ROOT / "scripts/CleanInstall13Audit.cs"
    require(harness.is_file(), "The external clean-install audit source is missing.")
    harness_raw = harness.read_bytes()
    (consumer / "Packages").mkdir(parents=True)
    (consumer / "Assets/Editor").mkdir(parents=True)
    shutil.copytree(settings, consumer / "ProjectSettings", ignore=shutil.ignore_patterns(".DS_Store"))
    (consumer / "Assets/Editor/CleanInstall13Audit.cs").write_bytes(harness_raw)
    main_dependencies = json.loads((ROOT / "UnityProject/Packages/manifest.json").read_text())["dependencies"]
    dependencies = {name: version for name, version in main_dependencies.items() if name.startswith("com.unity.modules.")}
    dependencies["com.worldweaver.toolkit"] = "file:" + os.path.relpath(output, consumer / "Packages").replace(os.sep, "/")
    consumer_manifest = consumer / "Packages/manifest.json"
    consumer_manifest.write_text(json.dumps({"dependencies": dependencies}, ensure_ascii=False, indent=2) + "\n")
    require(not (consumer / "Library").exists(), "A supposedly fresh consumer contains a Library.")
    require(current_package() == payloads, "Package sources changed while preparing the archive.")
    receipt = {"kind": "deterministic-upm-archive-verification", "status": "archive_verified_consumer_prepared_not_executed", "recordedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "revision": args.revision, "sourceCommit": args.source_commit, "packageVersion": package_info["version"], "snapshotManifest": relative(manifest_path), "snapshotManifestSha256": sha(manifest_path.read_bytes()),
               "snapshotFileCount": len(payloads), "snapshotTreeSha256": snapshot["treeSha256"], "canonicalOrdinalNulTreeSha256": tree,
               "archive": relative(output), "sha256": sha(packed), "bytes": len(packed), "uncompressedFileBytes": sum(map(len, payloads.values())), "archivedFileCount": len(payloads), "archivedDirectoryCount": directory_count,
               "fixedTimestamp": {"source": "source commit committer Unix timestamp", "epoch": epoch, "utc": datetime.datetime.fromtimestamp(epoch, datetime.timezone.utc).isoformat()},
               "format": {"prefix": "package/", "tar": "POSIX pax", "gzip": "empty filename; level 9; mtime from source commit", "uid": 0, "gid": 0, "uname": "", "gname": "", "fileMode": "0644", "directoryMode": "0755"},
               "builder": {"path": relative(Path(__file__).resolve()), "sha256": sha(Path(__file__).read_bytes()), "python": platform.python_version(), "zlib": zlib.ZLIB_VERSION},
               "verified": {"repeatedBuildByteEqual": True, "allCurrentFilesMatchSnapshotAndGit": True, "unpackedBytesEqualCurrentSnapshotAndGit": True, "canonicalOrdinalNulTreeMatchesSnapshot": True,
                            "noDuplicateOrEscapingMembers": True, "noSymlinkHardlinkOrSpecialFileMembers": True, "noDSStore": True, "allMemberPermissionsOwnersTimestampsVerified": True, "sourceFilesUnchangedAfterPackaging": True},
               "preservedPrevious": previous, "files": [{"path": name, "sha256": sha(raw), "bytes": len(raw)} for name, raw in sorted(payloads.items())],
               "cleanConsumer": {"project": relative(consumer), "manifestSha256": sha(consumer_manifest.read_bytes()), "packageReference": dependencies["com.worldweaver.toolkit"], "libraryExistedAtPreparation": False,
                                 "packageSourceEmbeddedOrLinked": False, "projectSettingsSource": "UnityProject/ProjectSettings", "projectSettingsFiles": [{"path": path.relative_to(consumer / "ProjectSettings").as_posix(), "sha256": sha(path.read_bytes())} for path in sorted((consumer / "ProjectSettings").rglob("*")) if path.is_file()],
                                 "unityStarted": False, "compileOrAssetLoadSuccess": None, "harness": {"path": relative(consumer / "Assets/Editor/CleanInstall13Audit.cs"), "reproducibleSource": relative(harness), "sha256": sha(harness_raw), "publicAndConsumerBytesIdentical": True,
                                 "executeMethod": "CleanInstall13Audit.Run", "requiredEnvironment": {"WW_CLEAN13_ACCEPTANCE": "1", "WW_CLEAN13_OUTPUT": str(ROOT / ("results/clean-install-1.3.0-" + args.revision + ".raw.json")), "WW_CLEAN13_PACKAGE_PROOF": str(proof_path), "WW_CLEAN13_EXPECTED_SOURCE": args.source_commit}, "compilerInvokedByBuilder": False, "unityStarted": False}}}
    output.parent.mkdir(parents=True, exist_ok=True)
    proof_path.parent.mkdir(parents=True, exist_ok=True)
    temp_archive = output.with_name(output.name + ".building")
    temp_proof = proof_path.with_name(proof_path.name + ".building")
    temp_archive.write_bytes(packed)
    temp_proof.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n")
    require(current_package() == payloads and sha(temp_archive.read_bytes()) == sha(packed), "Source/archive changed before publication.")
    os.replace(temp_archive, output)
    os.replace(temp_proof, proof_path)
    print(json.dumps({key: receipt[key] for key in ("status", "revision", "sourceCommit", "archive", "sha256", "bytes", "archivedFileCount", "archivedDirectoryCount", "canonicalOrdinalNulTreeSha256")}, indent=2))
    print("Proof SHA256:", sha(proof_path.read_bytes()))
    print("Harness SHA256:", sha(harness_raw))


if __name__ == "__main__":
    main()
