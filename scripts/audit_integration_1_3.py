#!/usr/bin/env python3
"""Read-only source/evidence audit. Does not run Unity, inference, or mutate Git.

Example final audit:
  python3 scripts/audit_integration_1_3.py --target r8 --verify-git --output results/integration-1.3-audit.json
Review-feedback and algorithm-guided mechanic variants must be explicitly selected:
  --final-receipt mechanic=results/mechanic-state-acceptance-1.3-r8.json
Optional additional LLM material flow (does not replace the canonical material slot):
  --receipt r7=results/material-chat-acceptance-1.3-r7.json
Explicitly scoped later-source material audit (does not claim the canonical three):
  --target r9 --required-actual material --required-actual material-chat
Optional archive: --tgz deliverables/com.worldweaver.toolkit-1.3.0.tgz
Missing final receipts/XML produce INCOMPLETE (exit 2), never a successful aggregate.
Historical failed acceptance runs remain failures; their preservation can pass a provenance audit.
"""
from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = "Packages/com.worldweaver.toolkit"
HEX = re.compile(r"^[0-9a-f]{64}$")
KINDS = {
    "material": ("actual-offline-and-polyhaven-material-acceptance", "MaterialAcceptanceRunner.cs"),
    "npc-conversation": ("actual-local-freeform-and-social-npc-acceptance", "NpcConversationAcceptanceRunner.cs"),
    "mechanic": ("actual-main-chat-proactive-mechanic-draft", "MechanicAcceptanceRunner.cs"),
}
RECEIPT_TYPES = {kind: (slot, runner, "initial") for slot, (kind, runner) in KINDS.items()}
RECEIPT_TYPES.update({
    "actual-main-chat-review-feedback-mechanic-draft": ("mechanic", "MechanicRepairAcceptanceRunner.cs", "review-feedback"),
    "actual-main-chat-explicit-state-mechanic-draft": ("mechanic", "MechanicStateAcceptanceRunner.cs", "explicit-state-guided"),
    "actual-main-chat-polyhaven-search-import-and-selected-renderer": ("material-chat", "MaterialChatAcceptanceRunner.cs", "optional-main-chat"),
})
ACTUAL_SLOTS = tuple(dict.fromkeys(slot for slot, _, _ in RECEIPT_TYPES.values()))
MATERIAL_RETRY_ANALYSIS_KIND = "r11-material-initial-tls-failure-and-separate-successful-retry-analysis"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def pairs_unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON property: " + key)
        result[key] = value
    return result


def read_json(path: Path):
    if path.stat().st_size > 32 * 1024 * 1024:
        raise ValueError("JSON exceeds 32 MiB: " + str(path))
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=pairs_unique)


def relative_path(value: str) -> str:
    if not isinstance(value, str) or not value or "\\" in value or value.startswith("/") or any(p in ("", ".", "..") for p in value.split("/")):
        raise ValueError("Unsafe/unnormalized relative path: " + repr(value))
    return value


def hashes_from_rows(rows):
    result = {}
    for row in rows:
        path = relative_path(row["path"])
        digest = row["sha256"]
        if path in result or not isinstance(digest, str) or not HEX.fullmatch(digest):
            raise ValueError("Duplicate path or invalid SHA256: " + path)
        result[path] = digest
    return result


def digest_tree(files, *, separator="\0", order=None):
    order = list(order) if order is not None else sorted(files)
    return sha("".join(path + separator + files[path] + "\n" for path in order).encode("utf-8"))


def disk_files(folder: Path):
    if folder.is_symlink():
        raise ValueError("Package root is a symlink: " + str(folder))
    result, sizes = {}, {}
    for path in folder.rglob("*"):
        if path.is_symlink():
            raise ValueError("Symlink in package: " + str(path))
        if not path.is_file() or path.name == ".DS_Store":
            continue
        name = path.relative_to(folder).as_posix()
        result[name], sizes[name] = sha(path.read_bytes()), path.stat().st_size
    return result, sizes


def differences(expected, actual):
    return {
        "missing": sorted(expected.keys() - actual.keys()),
        "extra": sorted(actual.keys() - expected.keys()),
        "changed": sorted(key for key in expected.keys() & actual.keys() if expected[key] != actual[key]),
    }


def identical(diff):
    return not any(diff.values())


def choose_final(matches, explicit):
    if explicit:
        matches = [item for item in matches if item["path"] == explicit]
    if len(matches) != 1:
        return None, f"Expected exactly one final receipt, found {len(matches)}; use --final-receipt to resolve variants explicitly"
    if not explicit and matches[0].get("workflowVariant") in ("review-feedback", "explicit-state-guided"):
        return None, "Feedback/algorithm-guided mechanic requires explicit --final-receipt mechanic=PATH; it cannot silently replace the initial workflow"
    return matches[0], None


def target_candidates(receipts, target, kind):
    return [item for item in receipts if item["snapshot"] == target and item["kind"] == kind]


def is_known_analysis_sidecar(path, kind):
    return path.name.endswith("-failure-analysis.json") and kind == MATERIAL_RETRY_ANALYSIS_KIND


def acceptance_outcomes(required, selected, target, *, evidence_ok):
    """Keep explicit subset success separate from the original three-slot claim."""
    by_kind = {item["kind"]: item for item in selected}
    def bound(kind):
        item = by_kind.get(kind, {})
        return item.get("snapshot") == target and item.get("harnessBindingComplete") is True and item.get("completePackageSourceMatchesSnapshot") is True
    canonical_complete = all(bound(kind) for kind in KINDS)
    explicit_pass = evidence_ok and all(bound(kind) and by_kind[kind].get("reportedOutcome") == "passed" for kind in required)
    canonical_pass = evidence_ok and canonical_complete and all(by_kind[kind].get("reportedOutcome") == "passed" for kind in KINDS)
    return {"canonicalThreeSlotsComplete": canonical_complete,
            "allExplicitlyRequiredActualAcceptancesReportedPassed": explicit_pass,
            "allRequiredActualAcceptancesReportedPassed": canonical_pass}


def review_reference(review, root: Path, review_path: Path):
    nested = review.get("receipt") if isinstance(review.get("receipt"), dict) else {}
    filename = review.get("sourceReceipt", nested.get("path", ""))
    expected = review.get("sourceReceiptSha256", nested.get("sha256"))
    declared = Path(filename)
    candidates = [declared] if declared.is_absolute() else [root / declared, review_path.parent / declared]
    located = next((path for path in candidates if path.is_file() and isinstance(expected, str) and sha(path.read_bytes()) == expected), candidates[0])
    bound = isinstance(expected, str) and HEX.fullmatch(expected) is not None and located.is_file() and sha(located.read_bytes()) == expected
    return located, expected, bound


def git_bytes(root: Path, revision: str, path: str):
    # All commands are read-only. No shell, network, checkout, writes, or credential access.
    result = subprocess.run(["git", "-C", str(root), "show", revision + ":" + path], capture_output=True, timeout=20)
    return result.stdout if result.returncode == 0 else None


def git_package(root: Path, revision: str):
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Invalid source commit")
    result = subprocess.run(["git", "-C", str(root), "archive", "--format=tar", revision, PACKAGE], capture_output=True, timeout=30)
    if result.returncode:
        raise ValueError("Could not read source commit with git archive: " + revision)
    return tar_hashes(io.BytesIO(result.stdout), prefix=PACKAGE + "/")


def tar_hashes(fileobj, *, prefix="package/"):
    found = {}
    with tarfile.open(fileobj=fileobj, mode="r:*") as archive:
        for member in archive:
            if member.isdir():
                continue
            if not member.isfile() or not member.name.startswith(prefix) or member.size > 64 * 1024 * 1024:
                raise ValueError("Unexpected tar entry: " + member.name)
            name = relative_path(member.name[len(prefix):])
            if PurePosixPath(name).name == ".DS_Store":
                continue
            if name in found:
                raise ValueError("Duplicate tar member: " + name)
            with archive.extractfile(member) as stream:
                found[name] = sha(stream.read())
    return found


def xml_summary(path: Path):
    root = ET.parse(path).getroot()
    cases = list(root.iter("test-case"))
    counts = Counter(case.get("result", "Unknown") for case in cases)
    names = [case.get("fullname", "") for case in cases]
    issues = []
    if not cases or any(not name for name in names) or len(set(names)) != len(names):
        issues.append("Zero cases, missing fullnames, or duplicate fullnames")
    declared = {}
    mapping = {"total": len(cases), "passed": counts["Passed"], "failed": counts["Failed"], "skipped": counts["Skipped"], "inconclusive": counts["Inconclusive"]}
    for key, actual in mapping.items():
        try:
            declared[key] = int(root.attrib[key])
        except (KeyError, ValueError):
            issues.append("Missing/invalid root count: " + key)
            continue
        if declared[key] != actual:
            issues.append(f"Root {key}={declared[key]} differs from actual cases={actual}")
    passed = bool(cases) and counts["Passed"] == len(cases) and root.get("result") == "Passed" and not issues
    return {"sha256": sha(path.read_bytes()), "rootResult": root.get("result"), "declared": declared,
            "actual": dict(counts), "actualTotal": len(cases), "allCasesPassedWithoutSkip": passed,
            "issues": issues, "caseNames": names,
            "bindingScope": "XML filename and companion snapshot manifest; NUnit XML itself contains no package SHA256."}


def receipt_files(receipt):
    source = receipt.get("source")
    if not isinstance(source, dict):
        raise ValueError("Receipt has no package source map")
    if isinstance(source.get("files"), list):
        return hashes_from_rows(source["files"]), source
    raw = source.get("packageFiles", source)
    declared_root = receipt.get("resolvedPackageRoot", source.get("packageRoot", ""))
    if not isinstance(declared_root, str):
        raise ValueError("Receipt package root must be a string")
    root = declared_root.replace("\\", "/").rstrip("/") + "/"
    result = {}
    for path, digest in raw.items():
        if not isinstance(digest, str) or not HEX.fullmatch(digest):
            raise ValueError("Invalid receipt source digest: " + path)
        if path.startswith("/") and (not declared_root.startswith("/") or not path.startswith(root)):
            raise ValueError("Absolute receipt source key escapes its declared packageRoot: " + path)
        if path.startswith(root):
            path = path[len(root):]
        path = relative_path(path)
        if path in result:
            raise ValueError("Duplicate normalized receipt source path")
        result[path] = digest
    return result, source


class Auditor:
    def __init__(self, root: Path, verify_git: bool):
        self.root, self.verify_git = root, verify_git
        self.issues, self.pending, self.notes = [], [], []
        self.snapshots, self.expected, self.harness_cache = {}, {}, {}

    def issue(self, context, message):
        self.issues.append({"context": context, "message": message})

    def audit_material_retry_analysis(self, snapshot, path):
        raw = read_json(path)
        record = {"path": str(path), "sha256": sha(path.read_bytes()), "kind": raw.get("kind"), "snapshot": snapshot,
                  "role": "Analysis sidecar, not an actual acceptance run or a final-slot candidate", "acceptanceSlotEligible": False,
                  "linkedActualReceipts": []}
        if raw.get("sourceCommit") != self.snapshots[snapshot]["sourceCommit"]:
            self.issue(str(path), "Material retry analysis source commit differs from its snapshot")
        if raw.get("harnessSha256") != self.snapshots[snapshot]["harnessDeclarations"].get("MaterialAcceptanceRunner.cs"):
            self.issue(str(path), "Material retry analysis harness differs from its snapshot")
        for key in ("initialAttempt", "successfulRetry"):
            link = raw.get(key, {})
            located, digest, bound = review_reference({"sourceReceipt": link.get("receipt", ""), "sourceReceiptSha256": link.get("sha256")}, self.root, path)
            item = {"role": key, "path": str(located), "sha256": digest, "hashMatches": bound}
            record["linkedActualReceipts"].append(item)
            if not bound:
                self.issue(str(path), "Material retry analysis is not bound to unchanged actual receipt: " + key)
                continue
            actual = read_json(located); item["reportedPassed"] = actual.get("passed")
            if actual.get("kind") != KINDS["material"][0] or type(link.get("passed")) is not bool or link["passed"] != actual.get("passed"):
                self.issue(str(path), "Material retry analysis misstates actual receipt kind/outcome: " + key)
        return record

    def load_snapshot(self, path: Path):
        name = re.search(r"-(r\d+)\.json$", path.name).group(1)
        data = read_json(path)
        files = hashes_from_rows(data["files"])
        order = [row["path"] for row in data["files"]]
        if order != sorted(order, key=lambda value: tuple(value.split("/"))):
            self.issue(str(path), "Snapshot rows are not component-sorted as the historical snapshot generator sorts pathlib.Path values")
        claimed_tree = digest_tree(files, order=order)
        if claimed_tree != data.get("treeSha256") or len(files) != data.get("fileCount"):
            self.issue(str(path), "Snapshot self-digest/count mismatch")
        if data.get("canonicalOrdinalNulTreeSha256") is not None and data["canonicalOrdinalNulTreeSha256"] != digest_tree(files):
            self.issue(str(path), "Explicit canonical ordinal tree digest differs from file map")
        for row in data["files"]:
            if type(row.get("bytes")) is not int or row["bytes"] < 0:
                self.issue(str(path), "Missing/invalid file byte count: " + row["path"])
        declared_harnesses = data.get("harnesses")
        if not isinstance(declared_harnesses, dict):
            external = self.root / f"results/integration-1.3-harnesses-{name}.json"
            declared_harnesses = read_json(external).get("harnesses", {}) if external.is_file() else {}
        record = {"manifest": str(path.relative_to(self.root)), "manifestSha256": sha(path.read_bytes()),
                  "sourceCommit": data.get("sourceCommit"), "packageVersion": data.get("packageVersion"), "fileCount": len(files),
                  "declaredTreeSha256": data.get("treeSha256"), "verifiedDeclaredTreeSha256": claimed_tree,
                  "canonicalOrdinalNulTreeSha256": digest_tree(files),
                  "treeOrderingNote": "Snapshot uses component-sorted manifest row order; an ordinal full-path NUL hash can legitimately differ.",
                  "harnessDeclarations": declared_harnesses}
        folder = Path(data["packageSnapshot"])
        folder = folder if folder.is_absolute() else self.root / folder
        record["localImmutableSnapshotAvailable"] = folder.is_dir()
        if folder.is_dir():
            actual, sizes = disk_files(folder)
            record["localSnapshotDifference"] = differences(files, actual)
            if not identical(record["localSnapshotDifference"]):
                self.issue(str(path), "Immutable local snapshot differs from its manifest")
            if any(sizes.get(row["path"]) != row["bytes"] for row in data["files"]):
                self.issue(str(path), "Immutable local snapshot byte-size mismatch")
        else:
            self.notes.append(name + ": private snapshot directory unavailable; public manifest self-hashes still checked")
        if self.verify_git:
            actual = git_package(self.root, data["sourceCommit"])
            record["sourceCommitDifference"] = differences(files, actual)
            if not identical(record["sourceCommitDifference"]):
                self.issue(str(path), "Declared source commit package differs from manifest (git archive read-only)")
        self.snapshots[name], self.expected[name] = record, files

    def harness(self, name, digest):
        cache_key = (name, digest)
        if cache_key in self.harness_cache:
            return self.harness_cache[cache_key]
        result = {"name": name, "sha256": digest, "actualBytesLocated": False}
        if not isinstance(digest, str) or not HEX.fullmatch(digest):
            result["reason"] = "No valid harness SHA256 supplied"; return result
        current = self.root / "scripts" / name
        if current.is_file():
            result["currentScriptSha256"] = sha(current.read_bytes())
            if result["currentScriptSha256"] == digest:
                result.update(actualBytesLocated=True, location="scripts/" + name)
        if not result["actualBytesLocated"]:
            for historic in sorted((self.root / "results/harness-history").glob(Path(name).stem + "-*.cs.txt")):
                if sha(historic.read_bytes()) == digest:
                    result.update(actualBytesLocated=True, location=str(historic.relative_to(self.root)),
                                  byteLocationScope="Archived/reconstructed historical source; reconstruction metadata, when present, is separate.")
                    break
        if not result["actualBytesLocated"] and self.verify_git:
            commits = subprocess.run(["git", "-C", str(self.root), "log", "-50", "--format=%H", "--", "scripts/" + name], capture_output=True, timeout=30, text=True)
            for commit in commits.stdout.splitlines():
                content = git_bytes(self.root, commit, "scripts/" + name)
                if content is not None and sha(content) == digest:
                    result.update(actualBytesLocated=True, location="git:" + commit + ":scripts/" + name); break
        self.harness_cache[cache_key] = result
        return result

    def audit_receipt(self, snapshot, path: Path):
        raw = read_json(path)
        receipt_kind = raw.get("kind")
        if receipt_kind not in RECEIPT_TYPES:
            raise ValueError("Unsupported actual receipt kind: " + str(raw.get("kind")))
        kind, runner, variant = RECEIPT_TYPES[receipt_kind]
        if not isinstance(raw.get("finishedUtc"), str) or not raw["finishedUtc"]:
            self.pending.append("Actual receipt has not finished: " + str(path))
            return {"path": str(path), "sha256": sha(path.read_bytes()), "snapshot": snapshot, "kind": kind, "receiptKind": receipt_kind, "workflowVariant": variant,
                    "reportedOutcome": "in-progress", "harnessBindingComplete": False, "originalReceiptUnmodified": True}
        files, source = receipt_files(raw)
        diff = differences(self.expected[snapshot], files)
        record = {"path": str(path), "sha256": sha(path.read_bytes()), "snapshot": snapshot, "kind": kind, "receiptKind": receipt_kind, "workflowVariant": variant,
                  "sourceDifference": diff, "completePackageSourceMatchesSnapshot": identical(diff),
                  "reportedPassed": raw.get("passed"), "reportedSuccess": raw.get("success"),
                  "failedChecks": [key for key, value in raw.get("checks", {}).items() if value is False],
                  "originalReceiptUnmodified": True, "resultIsForTargetSnapshotOnly": snapshot}
        record["expectedRunnerForReceiptKind"] = runner
        if raw.get("runner") and str(raw["runner"]).rsplit(".", 1)[-1] + ".cs" != runner:
            self.issue(str(path), "Receipt runner name does not match its declared workflow kind")
        if not identical(diff):
            self.issue(str(path), "Receipt package map does not equal the explicitly bound snapshot")
        if "fileCount" in source and source["fileCount"] != len(files):
            self.issue(str(path), "Receipt file count differs from its source map")
        if "treeSha256" in source:
            fmt = source.get("treeFormat", source.get("treeDigestFormat", ""))
            separator = "\t" if "TAB" in fmt else "\0" if "NUL" in fmt else None
            if separator is None:
                self.issue(str(path), "Receipt has unknown tree format")
            else:
                record["receiptTreeSha256Recomputed"] = digest_tree(files, separator=separator)
                if record["receiptTreeSha256Recomputed"] != source["treeSha256"]:
                    self.issue(str(path), "Receipt source tree SHA256 mismatch")
        declared = raw.get("harnessSha256", source.get("harnessSha256"))
        expected = self.snapshots[snapshot]["harnessDeclarations"].get(runner)
        actual = self.harness(runner, declared)
        record["receiptSelfDeclaredHarness"] = actual
        record["snapshotDeclaresThisHarness"] = expected is not None
        if not isinstance(declared, str) or not HEX.fullmatch(declared):
            record["harnessBindingComplete"] = False
            record["harnessBindingGap"] = "Receipt did not supply its harness hash; an external declaration is not a self-binding receipt."
            if expected:
                record["externalHarnessDeclaration"] = self.harness(runner, expected)
        else:
            receipt_only_optional = (kind == "material-chat" or variant == "explicit-state-guided") and expected is None
            record["harnessBindingScope"] = "Receipt-only exact public runner bytes; absent from historical snapshot declaration; no runner-to-source-commit claim." if receipt_only_optional else "Receipt hash, snapshot harness declaration and exact located runner bytes."
            record["harnessBindingComplete"] = actual["actualBytesLocated"] and (expected == declared or receipt_only_optional)
            if (expected != declared and not receipt_only_optional) or not actual["actualBytesLocated"]:
                self.issue(str(path), "Receipt harness differs from companion declaration or exact runner bytes were not located")
        values = [raw[key] for key in ("passed", "success") if key in raw]
        if not values:
            self.issue(str(path), "Completed receipt has no explicit boolean outcome")
        elif any(type(value) is not bool for value in values) or len(set(values)) > 1:
            self.issue(str(path), "Conflicting/nonboolean top-level outcome flags")
        reported_pass = bool(values) and all(values)
        if reported_pass and record["failedChecks"]:
            self.issue(str(path), "Receipt claims PASS while containing false checks")
        record["reportedOutcome"] = "passed" if reported_pass else "failed-or-incomplete"
        if variant == "review-feedback":
            record["reviewFeedbackInputLinks"] = self.feedback_links(raw, path)
        if variant == "explicit-state-guided":
            record["algorithmGuidanceDisclosure"] = self.guidance_disclosure(raw, path)
        for draft in raw.get("drafts", []):
            command = next((action for action in (raw.get("pendingPlan") or {}).get("actions", []) if action.get("name") == draft.get("className") and action.get("tool") == "draft_script"), None)
            if command is None or sha(command["args"]["code"].encode("utf-8")) != draft.get("sourceSha256"):
                self.issue(str(path), "Exported draft hash is not bound to the captured pending source")
            export = Path(draft.get("sourcePath", ""))
            if export.is_file() and sha(export.read_bytes()) != draft.get("sourceSha256"):
                self.issue(str(path), "Existing exported draft bytes changed")
        artifacts = []; record["renderedImageArtifacts"] = artifacts
        def images(value):
            if isinstance(value, dict):
                filename, digest = value.get("path"), value.get("sha256")
                if isinstance(filename, str) and Path(filename).suffix.lower() in (".png", ".jpg", ".jpeg") and isinstance(digest, str) and HEX.fullmatch(digest):
                    actual_path = Path(filename)
                    if not actual_path.is_absolute():
                        actual_path = self.root / actual_path
                    if not actual_path.is_file():
                        alternatives = {path.parent / Path(filename).name, self.root / "results" / Path(filename).name}
                        matches = [candidate for candidate in alternatives if candidate.is_file() and sha(candidate.read_bytes()) == digest]
                        if len(matches) == 1:
                            actual_path = matches[0]
                    valid = actual_path.is_file() and sha(actual_path.read_bytes()) == digest
                    artifacts.append({"declaredPath": filename, "locatedPath": str(actual_path), "sha256": digest, "bytesHashMatches": valid})
                    if not valid:
                        self.issue(str(path), "Rendered image missing or changed: " + filename)
                for key, nested in value.items():
                    if key != "source":
                        images(nested)
            elif isinstance(value, list):
                for nested in value:
                    images(nested)
        images(raw)
        return record

    def guidance_disclosure(self, raw, receipt_path):
        """A guided fresh-chat attempt must not be presented as unassisted first-attempt success."""
        disclosure = raw.get("guidanceDisclosure")
        if not isinstance(disclosure, dict):
            self.issue(str(receipt_path), "Explicit-state mechanic lacks algorithm guidance disclosure")
            return {"valid": False, "notUnassistedFirstAttempt": True}
        prompt = raw.get("userRequest")
        digest = disclosure.get("promptSha256")
        prompt_bound = isinstance(prompt, str) and bool(prompt.strip()) and isinstance(digest, str) and HEX.fullmatch(digest) is not None and sha(prompt.encode("utf-8")) == digest
        count = disclosure.get("priorFailedAttempts")
        valid = (disclosure.get("algorithmGuided") is True and disclosure.get("freshConversation") is True
                 and type(count) is int and count > 0 and prompt_bound
                 and isinstance(disclosure.get("scope"), str) and bool(disclosure["scope"].strip()))
        if not valid:
            self.issue(str(receipt_path), "Explicit-state mechanic algorithm/fresh-chat/prior-failure disclosure or exact prompt binding is missing/invalid")
        return {**disclosure, "valid": valid, "promptHashMatches": prompt_bound, "notUnassistedFirstAttempt": True,
                "independentSemanticReviewStillRequired": True}

    def feedback_links(self, raw, receipt_path):
        """Verify original inputs; a FAIL review is evidence for feedback, not a failed new candidate."""
        links, located = [], {}
        definitions = (("originalReceiptPath", "originalReceiptSha256"), ("reviewReceiptPath", "reviewReceiptSha256"), ("rejectedSourcePath", "rejectedSourceSha256"))
        for path_key, sha_key in definitions:
            filename, digest = raw.get(path_key), raw.get(sha_key)
            if not isinstance(filename, str) or not isinstance(digest, str) or not HEX.fullmatch(digest):
                self.issue(str(receipt_path), "Review-feedback receipt lacks exact input binding: " + path_key)
                links.append({"input": path_key, "available": False, "hashMatches": False}); continue
            candidate = Path(filename)
            if not candidate.is_absolute():
                candidate = receipt_path.parent / candidate
            if not candidate.is_file():
                candidates = {receipt_path.parent / Path(filename).name, self.root / "results" / Path(filename).name}
                matches = [item for item in candidates if item.is_file() and sha(item.read_bytes()) == digest]
                if len(matches) == 1:
                    candidate = matches[0]
            available = candidate.is_file(); matches = available and sha(candidate.read_bytes()) == digest
            links.append({"input": path_key, "declaredPath": filename, "locatedPath": str(candidate), "sha256": digest, "available": available, "hashMatches": matches})
            if available and not matches:
                self.issue(str(receipt_path), "Review-feedback input bytes changed: " + path_key)
            if matches and path_key == "rejectedSourcePath" and raw.get("rejectedSourceBytes") is not None and raw["rejectedSourceBytes"] != candidate.stat().st_size:
                self.issue(str(receipt_path), "Rejected source byte count differs from the exact input file")
            if not available:
                self.notes.append(str(receipt_path) + ": linked input unavailable locally: " + path_key)
            if matches:
                located[path_key] = candidate
        if "reviewReceiptPath" in located:
            review = read_json(located["reviewReceiptPath"])
            if review.get("sourceReceiptSha256") != raw.get("originalReceiptSha256") or review.get("reviewedSourceSha256") != raw.get("rejectedSourceSha256"):
                self.issue(str(receipt_path), "Feedback review does not bind the stated original receipt/source")
            links.append({"reviewAccepted": review.get("accepted"), "oldFailureDoesNotDetermineNewCandidateOutcome": True})
        if "originalReceiptPath" in located:
            original = read_json(located["originalReceiptPath"])
            drafts = original.get("drafts", [])
            if not any(draft.get("sourceSha256") == raw.get("rejectedSourceSha256") for draft in drafts):
                self.issue(str(receipt_path), "Rejected source is not a captured draft in the exact original receipt")
            links.append({"originalProtocolReportedPassed": original.get("passed"), "originalSemanticOutcomeRemainsSeparate": True})
        return links


def self_test():
    """Small negative controls for the auditor itself; no project/Unity/Git access."""
    passed = []
    for value in ("../outside", "/absolute", "a/../b", "a\\b"):
        try:
            relative_path(value)
        except ValueError:
            continue
        raise AssertionError("Traversal/absolute source path was accepted")
    passed.append("unsafe_source_paths_rejected")
    try:
        hashes_from_rows([{"path": "same", "sha256": "a" * 64}, {"path": "same", "sha256": "b" * 64}])
    except ValueError:
        passed.append("duplicate_source_manifest_rejected")
    else:
        raise AssertionError("Duplicate manifest rows accepted")
    with tempfile.TemporaryDirectory(prefix="worldweaver-audit-selftest-") as temporary:
        folder = Path(temporary); xml = folder / "counts.xml"
        xml.write_text('<test-run result="Passed" total="2" passed="2" failed="0" skipped="0" inconclusive="0"><test-case fullname="one" result="Passed" /></test-run>')
        result = xml_summary(xml)
        assert result["issues"] and not result["allCasesPassedWithoutSkip"]
        passed.append("declared_xml_count_cannot_hide_missing_cases")
        xml.write_text('<test-run result="Passed" total="1" passed="0" failed="0" skipped="1" inconclusive="0"><test-case fullname="one" result="Skipped" /></test-run>')
        assert not xml_summary(xml)["allCasesPassedWithoutSkip"]
        passed.append("skipped_xml_case_cannot_be_pass")
        (folder / "scripts").mkdir(); runner = folder / "scripts/MaterialAcceptanceRunner.cs"; runner.write_text("// fixture runner\n")
        runner_hash = sha(runner.read_bytes()); receipt = folder / "receipt.json"
        data = {"kind": KINDS["material"][0], "finishedUtc": "2026-11-01T00:00:00Z", "source": {"file": "a" * 64},
                "harnessSha256": runner_hash, "passed": True, "checks": {"actual_online_import": False}}
        receipt.write_text(json.dumps(data))
        auditor = Auditor(folder, False); auditor.expected["r9"] = {"file": "a" * 64}; auditor.snapshots["r9"] = {"harnessDeclarations": {runner.name: runner_hash}}
        auditor.audit_receipt("r9", receipt)
        assert any("claims PASS" in issue["message"] for issue in auditor.issues)
        passed.append("true_aggregate_with_false_check_rejected")
        data["passed"] = False; receipt.write_text(json.dumps(data)); auditor.issues.clear(); result = auditor.audit_receipt("r9", receipt)
        assert not auditor.issues and result["reportedOutcome"] == "failed-or-incomplete"
        passed.append("honest_failed_receipt_preserved_without_relabeling")
        data["source"]["file"] = "b" * 64; receipt.write_text(json.dumps(data)); auditor.audit_receipt("r9", receipt)
        assert any("source" in issue["message"] or "package" in issue["message"] for issue in auditor.issues)
        passed.append("receipt_source_drift_rejected")
        packed = io.BytesIO()
        with tarfile.open(fileobj=packed, mode="w") as archive:
            entry = tarfile.TarInfo("package/../escape"); entry.size = 1; archive.addfile(entry, io.BytesIO(b"x"))
        packed.seek(0)
        try:
            tar_hashes(packed)
        except ValueError:
            passed.append("tgz_traversal_rejected_without_extraction")
        else:
            raise AssertionError("Unsafe tar member accepted")
        original_runner = folder / "scripts/MechanicAcceptanceRunner.cs"; original_runner.write_text("// initial mechanic runner\n")
        repair_runner = folder / "scripts/MechanicRepairAcceptanceRunner.cs"; repair_runner.write_text("// feedback mechanic runner\n")
        rejected = folder / "rejected.cs.txt"; rejected.write_text("// rejected draft\n"); rejected_sha = sha(rejected.read_bytes())
        original = folder / "original.json"; original.write_text(json.dumps({"passed": True, "drafts": [{"sourceSha256": rejected_sha}]})); original_sha = sha(original.read_bytes())
        review = folder / "review.json"; review.write_text(json.dumps({"accepted": False, "sourceReceiptSha256": original_sha, "reviewedSourceSha256": rejected_sha})); review_sha = sha(review.read_bytes())
        repair_data = {"kind": "actual-main-chat-review-feedback-mechanic-draft", "runner": "MechanicRepairAcceptanceRunner", "finishedUtc": "2026-11-01T00:00:00Z",
                       "source": {"file": "a" * 64}, "passed": True, "checks": {"candidate_retained": True},
                       "originalReceiptPath": str(original), "originalReceiptSha256": original_sha,
                       "reviewReceiptPath": str(review), "reviewReceiptSha256": review_sha,
                       "rejectedSourcePath": str(rejected), "rejectedSourceSha256": rejected_sha,
                       "harnessSha256": sha(original_runner.read_bytes())}
        auditor = Auditor(folder, False); auditor.expected["r9"] = {"file": "a" * 64}
        auditor.snapshots["r9"] = {"harnessDeclarations": {original_runner.name: sha(original_runner.read_bytes()), repair_runner.name: sha(repair_runner.read_bytes())}}
        receipt.write_text(json.dumps(repair_data)); result = auditor.audit_receipt("r9", receipt)
        assert not result["harnessBindingComplete"] and any("harness" in issue["message"] for issue in auditor.issues)
        passed.append("feedback_variant_cannot_borrow_original_runner_hash")
        repair_data["harnessSha256"] = sha(repair_runner.read_bytes()); receipt.write_text(json.dumps(repair_data)); auditor.issues.clear()
        result = auditor.audit_receipt("r9", receipt)
        assert not auditor.issues and result["reportedOutcome"] == "passed" and any(link.get("reviewAccepted") is False for link in result["reviewFeedbackInputLinks"])
        passed.append("prior_semantic_fail_is_preserved_without_rejecting_new_feedback_candidate")
        original.write_text("tampered original receipt"); auditor.issues.clear(); auditor.audit_receipt("r9", receipt)
        assert any("input bytes changed" in issue["message"] for issue in auditor.issues)
        passed.append("changed_feedback_input_receipt_rejected")
        review.write_text("tampered review"); auditor.issues.clear(); auditor.audit_receipt("r9", receipt)
        assert any("reviewReceiptPath" in issue["message"] for issue in auditor.issues)
        passed.append("changed_feedback_review_input_rejected")
        try:
            receipt_files({"source": {"packageRoot": "/fixture", "packageFiles": {"/fixture-other/file": "a" * 64}}})
        except ValueError:
            passed.append("absolute_source_keys_cannot_escape_declared_package_root")
        else:
            raise AssertionError("Absolute source prefix confusion accepted")
        chat_runner = folder / "scripts/MaterialChatAcceptanceRunner.cs"; chat_runner.write_text("// optional main-chat material runner\n")
        optional_data = {"kind": "actual-main-chat-polyhaven-search-import-and-selected-renderer", "finishedUtc": "2026-11-01T00:00:00Z",
                         "source": {"packageRoot": "/fixture", "packageFiles": {"/fixture/file": "a" * 64}}, "harnessSha256": sha(chat_runner.read_bytes()), "passed": False, "checks": {}}
        receipt.write_text(json.dumps(optional_data)); auditor.issues.clear(); result = auditor.audit_receipt("r9", receipt)
        assert not auditor.issues and result["kind"] == "material-chat" and not result["snapshotDeclaresThisHarness"] and "Receipt-only" in result["harnessBindingScope"]
        passed.append("optional_historical_material_chat_binding_does_not_invent_snapshot_declaration")
        initial = {"path": "initial.json", "workflowVariant": "initial"}; feedback = {"path": "feedback.json", "workflowVariant": "review-feedback"}
        assert choose_final([initial, feedback], None)[0] is None
        passed.append("multiple_mechanic_variants_never_auto_select_canonical_or_best_result")
        assert choose_final([feedback], None)[0] is None and choose_final([initial, feedback], "feedback.json")[0] == feedback
        passed.append("feedback_final_slot_requires_explicit_selection")
        bound_review = {"receipt": {"path": "original.json", "sha256": sha(original.read_bytes())}, "overallSemanticVerdict": "FAIL"}
        assert review_reference(bound_review, folder, folder / "semantic.json")[2]
        passed.append("nested_receipt_manual_review_schema_is_hash_bound_without_relabeling_verdict")
        state_runner = folder / "scripts/MechanicStateAcceptanceRunner.cs"; state_runner.write_text("// guided fresh-chat mechanic runner\n")
        prompt = "Создай куб: состояние isAmber=false, F меняет состояние и цвет."
        state_data = {"kind": "actual-main-chat-explicit-state-mechanic-draft", "runner": "MechanicStateAcceptanceRunner",
                      "finishedUtc": "2026-11-01T00:00:00Z", "source": {"file": "a" * 64}, "passed": True,
                      "userRequest": prompt, "harnessSha256": sha(original_runner.read_bytes()),
                      "guidanceDisclosure": {"algorithmGuided": True, "freshConversation": True, "priorFailedAttempts": 3,
                                             "promptSha256": sha(prompt.encode("utf-8")), "scope": "Explicit algorithm supplied after failed attempts."}}
        receipt.write_text(json.dumps(state_data)); auditor.issues.clear(); result = auditor.audit_receipt("r9", receipt)
        assert not result["harnessBindingComplete"] and any("harness" in issue["message"] for issue in auditor.issues)
        passed.append("explicit_state_variant_cannot_borrow_older_runner_hash")
        state_data["harnessSha256"] = sha(state_runner.read_bytes()); receipt.write_text(json.dumps(state_data)); auditor.issues.clear()
        result = auditor.audit_receipt("r9", receipt)
        assert not auditor.issues and result["harnessBindingComplete"] and not result["snapshotDeclaresThisHarness"] and "no runner-to-source-commit claim" in result["harnessBindingScope"]
        assert result["algorithmGuidanceDisclosure"]["valid"] and result["algorithmGuidanceDisclosure"]["notUnassistedFirstAttempt"]
        passed.append("external_guided_runner_is_exact_receipt_only_binding_without_snapshot_rewrite")
        disclosure = state_data.pop("guidanceDisclosure"); receipt.write_text(json.dumps(state_data)); auditor.issues.clear(); auditor.audit_receipt("r9", receipt)
        assert any("guidance disclosure" in issue["message"] for issue in auditor.issues)
        passed.append("explicit_state_variant_cannot_omit_algorithm_guidance_disclosure")
        state_data["guidanceDisclosure"] = disclosure; state_data["userRequest"] += "changed"; receipt.write_text(json.dumps(state_data)); auditor.issues.clear()
        result = auditor.audit_receipt("r9", receipt)
        assert not result["algorithmGuidanceDisclosure"]["promptHashMatches"] and auditor.issues
        passed.append("guided_prompt_hash_cannot_hide_changed_algorithm_instructions")
        guided = {"path": "guided.json", "workflowVariant": "explicit-state-guided"}
        assert choose_final([guided], None)[0] is None and choose_final([initial, guided], "guided.json")[0] == guided
        passed.append("guided_fresh_chat_variant_requires_explicit_final_selection")
        subset = ["material", "material-chat"]
        selected = [{"kind": kind, "snapshot": "r9", "harnessBindingComplete": True,
                     "completePackageSourceMatchesSnapshot": True, "reportedOutcome": "passed"} for kind in subset]
        outcomes = acceptance_outcomes(subset, selected, "r9", evidence_ok=True)
        assert outcomes["allExplicitlyRequiredActualAcceptancesReportedPassed"] and not outcomes["canonicalThreeSlotsComplete"] and not outcomes["allRequiredActualAcceptancesReportedPassed"]
        passed.append("explicit_material_subset_cannot_claim_canonical_three_success")
        chosen, missing_error = choose_final(target_candidates(selected[:1], "r9", "material-chat"), None)
        outcomes = acceptance_outcomes(subset, selected[:1], "r9", evidence_ok=missing_error is None)
        assert chosen is None and missing_error and not outcomes["allExplicitlyRequiredActualAcceptancesReportedPassed"]
        passed.append("missing_explicitly_required_slot_remains_incomplete")
        historical = [{**selected[1], "snapshot": "r8"}]
        assert not target_candidates(historical, "r9", "material-chat")
        assert not acceptance_outcomes(subset, selected[:1] + historical, "r9", evidence_ok=True)["allExplicitlyRequiredActualAcceptancesReportedPassed"]
        passed.append("historical_receipt_cannot_fill_later_source_subset_slot")
        optional_data["source"]["packageFiles"]["/fixture/file"] = "b" * 64
        receipt.write_text(json.dumps(optional_data)); auditor.issues.clear(); result = auditor.audit_receipt("r9", receipt)
        assert not result["completePackageSourceMatchesSnapshot"] and any("source map" in issue["message"] or "package map" in issue["message"] for issue in auditor.issues)
        passed.append("relabeling_old_receipt_as_later_snapshot_is_rejected_by_exact_source_map")
        canonical = [{"kind": kind, "snapshot": "r9", "harnessBindingComplete": True,
                      "completePackageSourceMatchesSnapshot": True, "reportedOutcome": "passed"} for kind in KINDS]
        outcomes = acceptance_outcomes(list(KINDS), canonical, "r9", evidence_ok=True)
        assert outcomes["canonicalThreeSlotsComplete"] and outcomes["allRequiredActualAcceptancesReportedPassed"] and outcomes["allExplicitlyRequiredActualAcceptancesReportedPassed"]
        passed.append("default_canonical_three_retains_original_complete_pass_contract")
        analysis = folder / "material-acceptance-1.3-r9-failure-analysis.json"
        assert is_known_analysis_sidecar(analysis, MATERIAL_RETRY_ANALYSIS_KIND)
        assert not is_known_analysis_sidecar(analysis, KINDS["material"][0]) and not is_known_analysis_sidecar(analysis, "unknown-kind")
        passed.append("real_or_unknown_receipt_kind_cannot_be_hidden_as_analysis_by_filename")
        first = folder / "first-material.json"; first.write_text(json.dumps({"kind": KINDS["material"][0], "passed": False}))
        retry = folder / "retry-material.json"; retry.write_text(json.dumps({"kind": KINDS["material"][0], "passed": True}))
        analysis.write_text(json.dumps({"kind": MATERIAL_RETRY_ANALYSIS_KIND, "sourceCommit": "f" * 40, "harnessSha256": runner_hash,
                                        "initialAttempt": {"receipt": str(first), "sha256": sha(first.read_bytes()), "passed": False},
                                        "successfulRetry": {"receipt": str(retry), "sha256": sha(retry.read_bytes()), "passed": True}}))
        auditor = Auditor(folder, False); auditor.snapshots["r9"] = {"sourceCommit": "f" * 40, "harnessDeclarations": {"MaterialAcceptanceRunner.cs": runner_hash}}
        result = auditor.audit_material_retry_analysis("r9", analysis)
        assert not auditor.issues and result["acceptanceSlotEligible"] is False and [v["reportedPassed"] for v in result["linkedActualReceipts"]] == [False, True]
        passed.append("known_retry_analysis_preserves_failed_and_passed_actual_links_without_filling_slot")
        first.write_text("changed old failure receipt"); auditor.audit_material_retry_analysis("r9", analysis)
        assert any("unchanged actual receipt" in issue["message"] for issue in auditor.issues)
        passed.append("retry_analysis_cannot_hide_changed_original_failure_receipt")
    print(json.dumps({"auditorSelfTestsPassed": len(passed), "tests": passed}, indent=2))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--target", default="r8", help="Required final snapshot label, default r8")
    parser.add_argument("--receipt", action="append", default=[], metavar="SNAPSHOT=PATH")
    parser.add_argument("--required-actual", action="append", choices=ACTUAL_SLOTS, help="Explicit required actual slot; repeat to select a subset. Omit to require the original canonical three. Historical receipts never fill a later snapshot slot.")
    parser.add_argument("--final-receipt", action="append", default=[], metavar="KIND=PATH", help="Explicitly choose a final target receipt when several model/run variants exist; all variants stay audited")
    parser.add_argument("--semantic-review", action="append", type=Path, default=[], help="Additional manual review JSON; checked separately from automatic outcomes")
    parser.add_argument("--tgz", type=Path)
    parser.add_argument("--verify-git", action="store_true", help="Also read source commit archives and historical runner bytes; never mutates Git")
    parser.add_argument("--output", type=Path, help="Write an audit report only when explicitly supplied")
    parser.add_argument("--self-test", action="store_true", help="Run isolated auditor tamper/negative controls only")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    required_actual = list(args.required_actual) if args.required_actual is not None else list(KINDS)
    if len(set(required_actual)) != len(required_actual):
        parser.error("Duplicate --required-actual slot")
    root = args.root.resolve(); audit = Auditor(root, args.verify_git)
    report = {"schemaVersion": 1, "kind": "independent-integration-source-and-proof-audit", "checkedUtc": datetime.now(timezone.utc).isoformat(),
              "examinedRoot": str(root), "auditorSource": {"path": str(Path(__file__).resolve()), "sha256": sha(Path(__file__).read_bytes())},
              "targetSnapshot": args.target, "scope": "Byte/source/protocol integrity, not independent semantic validation of model answers or a benchmark.",
              "snapshots": audit.snapshots, "issues": audit.issues, "pendingEvidence": audit.pending, "notes": audit.notes,
              "actualAcceptanceProfile": "canonical-three" if set(required_actual) == set(KINDS) else "explicit-subset",
              "requiredActualSlots": required_actual, "canonicalThreeSlotsComplete": False,
              "auditStatus": "incomplete", "provenanceIntegrityVerified": False,
              "allRequiredActualAcceptancesReportedPassed": False, "allExplicitlyRequiredActualAcceptancesReportedPassed": False}
    try:
        for path in sorted((root / "results").glob("integration-1.3-snapshot-r*.json")):
            audit.load_snapshot(path)
        if args.target not in audit.snapshots:
            raise ValueError("Required target snapshot not found: " + args.target)
        expected = audit.expected[args.target]
        current, _ = disk_files(root / PACKAGE)
        report["currentPackageDifferenceFromTarget"] = differences(expected, current)
        report["currentPackageEqualsTarget"] = identical(report["currentPackageDifferenceFromTarget"])
        if not report["currentPackageEqualsTarget"]:
            audit.issue(PACKAGE, "Current package differs from frozen target snapshot")
        report["currentPackageVersion"] = read_json(root / PACKAGE / "package.json").get("version")
        if report["currentPackageVersion"] != audit.snapshots[args.target]["packageVersion"]:
            audit.issue(PACKAGE, "Package version differs from target manifest")
        report["currentPackageCanonicalOrdinalNulTreeSha256"] = digest_tree(current)
        report["targetHarnesses"] = []
        for name, digest in audit.snapshots[args.target]["harnessDeclarations"].items():
            item = audit.harness(name, digest); report["targetHarnesses"].append(item)
            if not item["actualBytesLocated"] or item.get("currentScriptSha256") != digest:
                audit.issue(name, "Current target harness missing/changed")
        declared_runners = set(audit.snapshots[args.target]["harnessDeclarations"])
        required_runners = {runner for _, runner in KINDS.values()}
        known_runners = {runner for _, runner, _ in RECEIPT_TYPES.values()}
        if not required_runners <= declared_runners:
            audit.issue(args.target, "Target does not declare all three canonical acceptance harnesses")
        if declared_runners - known_runners:
            audit.issue(args.target, "Target declares unsupported acceptance runner names: " + ", ".join(sorted(declared_runners - known_runners)))
        report["optionalTargetHarnessesDoNotAddRequiredSlots"] = sorted(declared_runners - required_runners)
        xmls = {}; report["xmlRuns"] = xmls
        for path in sorted((root / "results").glob("integration-1.3-*.xml")):
            try:
                result = xml_summary(path); result["path"] = str(path.relative_to(root)); xmls[path.name] = result
                for issue in result["issues"]:
                    audit.issue(str(path), issue)
            except ET.ParseError:
                audit.pending.append("XML is not complete yet: " + path.name)
        required_xml = [f"integration-1.3-{mode}-{args.target}.xml" for mode in ("edit", "play")]
        for name in required_xml:
            if name not in xmls:
                audit.pending.append("Required final XML missing/incomplete: " + name)
        subset = xmls.get(f"integration-1.3-http-{args.target}.xml")
        edit = xmls.get(required_xml[0])
        if subset and edit:
            names = set(subset["caseNames"])
            report["httpFocusedSubsetOfFullEdit"] = names <= set(edit["caseNames"])
            report["httpFocusedCountIsNotAddedToFullEdit"] = True
            if not report["httpFocusedSubsetOfFullEdit"]:
                audit.issue("HTTP focused XML", "Focused cases are not a subset of full EditMode")
        for item in xmls.values():
            item.pop("caseNames", None)
        specs = {}; analysis_sidecars = []; report["nonAcceptanceAnalysisSidecars"] = analysis_sidecars
        for kind in KINDS:
            for path in (root / "results").glob(kind + "-acceptance-1.3-r*.json"):
                match = re.search(r"-(r\d+)(?:-[^.]+)?\.json$", path.name)
                if match:
                    if path.name.endswith("-failure-analysis.json") and is_known_analysis_sidecar(path, read_json(path).get("kind")):
                        analysis_sidecars.append(audit.audit_material_retry_analysis(match.group(1), path))
                        continue
                    specs[str(path.resolve())] = match.group(1)
        for value in args.receipt:
            label, filename = value.split("=", 1); path = Path(filename); path = path if path.is_absolute() else root / path
            specs[str(path.resolve())] = label
        final_choices = {}
        for value in args.final_receipt:
            kind, filename = value.split("=", 1)
            if kind not in required_actual or kind in final_choices:
                raise ValueError("Unknown/duplicate/nonrequired --final-receipt kind for this profile: " + kind)
            path = Path(filename); path = path if path.is_absolute() else root / path
            final_choices[kind] = str(path.resolve()); specs[str(path.resolve())] = args.target
        receipts = []; report["actualReceipts"] = receipts
        for filename, label in sorted(specs.items()):
            if label not in audit.snapshots:
                audit.issue(filename, "No explicitly bound snapshot: " + label); continue
            receipts.append(audit.audit_receipt(label, Path(filename)))
        reviews = []; report["manualSemanticReviews"] = reviews
        review_paths = set((root / "results").glob("*semantic-review-1.3-r*.json"))
        review_paths.update(path if path.is_absolute() else root / path for path in args.semantic_review)
        for path in sorted(review_paths):
            review = read_json(path)
            source, expected_sha, bound = review_reference(review, root, path)
            reviews.append({"path": str(path), "sha256": sha(path.read_bytes()), "sourceReceipt": str(source),
                            "sourceReceiptHashMatches": bound, "accepted": review.get("accepted"),
                            "verdict": review.get("overallSemanticVerdict", review.get("verdict")),
                            "scope": review.get("scope", review.get("method")),
                            "automatedAuditDidNotRejudgeSemantics": True})
            if not bound:
                audit.issue(str(path), "Manual review is not bound to the exact existing receipt bytes")
        report["manualReviewsDoNotRewriteAutomaticOutcome"] = True
        report["automaticAcceptanceFlagExcludesManualSemanticJudgment"] = True
        selected_final = []; report["finalAcceptanceSelection"] = selected_final
        for kind in required_actual:
            matches = target_candidates(receipts, args.target, kind)
            explicit = final_choices.get(kind)
            chosen, selection_error = choose_final(matches, explicit)
            if selection_error:
                audit.pending.append(kind + ": " + selection_error)
            else:
                selected_final.append({"kind": kind, "path": chosen["path"], "reportedOutcome": chosen["reportedOutcome"],
                                       "snapshot": chosen["snapshot"], "harnessBindingComplete": chosen["harnessBindingComplete"],
                                       "completePackageSourceMatchesSnapshot": chosen.get("completePackageSourceMatchesSnapshot", False),
                                       "receiptKind": chosen.get("receiptKind"), "workflowVariant": chosen.get("workflowVariant"),
                                       "selection": "explicit CLI" if explicit else "only candidate"})
                if not chosen["harnessBindingComplete"]:
                    audit.pending.append("Final receipt lacks a complete harness binding: " + kind)
        if args.tgz:
            path = args.tgz if args.tgz.is_absolute() else root / args.tgz
            with path.open("rb") as stream:
                packed = tar_hashes(stream)
            report["tgz"] = {"path": str(path), "sha256": sha(path.read_bytes()), "difference": differences(expected, packed)}
            if not identical(report["tgz"]["difference"]):
                audit.issue(str(path), "TGZ package bytes differ from frozen target")
        report["engineeringXmlReportedPassed"] = all(name in xmls and xmls[name]["allCasesPassedWithoutSkip"] for name in required_xml)
        report.update(acceptance_outcomes(required_actual, selected_final, args.target, evidence_ok=not audit.issues and not audit.pending))
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError, tarfile.TarError) as error:
        audit.issue("audit execution", str(error))
    report["auditStatus"] = "failed" if audit.issues else "incomplete" if audit.pending else "verified"
    report["provenanceIntegrityVerified"] = not audit.issues and not audit.pending
    report["interpretation"] = "A verified provenance audit may contain faithfully preserved failed historical/model acceptances. It must not be presented as product acceptance."
    if args.output:
        output = args.output if args.output.is_absolute() else root / args.output
        if output.is_file() and read_json(output).get("kind") != report["kind"]:
            raise ValueError("Refusing to overwrite a file that is not an audit report: " + str(output))
        if output.resolve().is_relative_to((root / PACKAGE).resolve()) or output.resolve().is_relative_to((root / "scripts").resolve()):
            raise ValueError("Audit output cannot overwrite package/script sources")
        output.parent.mkdir(parents=True, exist_ok=True)
        temporary = output.with_suffix(output.suffix + ".tmp")
        temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"); temporary.replace(output)
    print(json.dumps({"auditStatus": report["auditStatus"], "currentPackageEqualsTarget": report.get("currentPackageEqualsTarget"),
                      "engineeringXmlReportedPassed": report.get("engineeringXmlReportedPassed", False),
                      "allRequiredActualAcceptancesReportedPassed": report["allRequiredActualAcceptancesReportedPassed"],
                      "actualAcceptanceProfile": report["actualAcceptanceProfile"], "requiredActualSlots": required_actual,
                      "canonicalThreeSlotsComplete": report["canonicalThreeSlotsComplete"],
                      "allExplicitlyRequiredActualAcceptancesReportedPassed": report["allExplicitlyRequiredActualAcceptancesReportedPassed"],
                      "issues": audit.issues, "pendingEvidence": audit.pending, "report": str(args.output) if args.output else None}, ensure_ascii=False, indent=2))
    return 1 if audit.issues else 2 if audit.pending else 0


if __name__ == "__main__":
    sys.exit(main())
