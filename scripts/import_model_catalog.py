#!/usr/bin/env python3
"""Build/verify a pinned, data-only Rocketbox catalog; never download the entire model library."""
import argparse
import hashlib
import json
import pathlib
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
PIN = "0943055db6ec570bcef9f2c8b41c9e5467c808f9"
TARGET = ROOT / "Packages/com.worldweaver.toolkit/Editor/Assistant/ModelCatalog.json"


def build(tree):
    assert tree["sha"] == PIN
    entries = []
    for model in tree["tree"]:
        path = model["path"]
        if not (path.startswith("Assets/Avatars/") and path.endswith(".fbx") and not path.endswith("_facial.fbx")):
            continue
        folder = path.rsplit("/Export/", 1)[0]
        name = folder.rsplit("/", 1)[-1]
        files = [model] + [f for f in tree["tree"] if f["path"].startswith(folder + "/Textures/") and f["path"].lower().endswith(("_color.tga", "_color.png", "_color.jpg"))]
        entries.append({"id": "rocketbox:" + name.lower(), "name": name.replace("_", " "),
                        "tags": "realistic human rigged humanoid " + folder.split("/")[2].lower() + " " + name.lower().replace("_", " "),
                        "files": [{"path": f["path"], "sha1": f["sha"], "bytes": f["size"], "relative": f["path"][len(folder) + 1:]} for f in files]})
    assert len(entries) == 115
    return {"version": 1, "repository": "microsoft/Microsoft-Rocketbox", "commit": PIN, "license": "MIT", "models": entries}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", action="store_true", help="Rebuild from the pinned GitHub tree (metadata only).")
    args = parser.parse_args()
    if args.build:
        url = f"https://api.github.com/repos/microsoft/Microsoft-Rocketbox/git/trees/{PIN}?recursive=1"
        request = urllib.request.Request(url, headers={"User-Agent": "WorldWeaver-catalog-builder"})
        with urllib.request.urlopen(request, timeout=30) as response:
            tree = json.load(response)
        TARGET.write_text(json.dumps(build(tree), indent=2) + "\n")
    data = json.loads(TARGET.read_text())
    assert data["commit"] == PIN and data["license"] == "MIT" and len(data["models"]) == 115
    assert len({m["id"] for m in data["models"]}) == 115
    for model in data["models"]:
        assert model["id"].startswith("rocketbox:") and model["files"][0]["path"].endswith(".fbx")
        assert sum(f["bytes"] for f in model["files"]) <= 96 * 1024 ** 2
        for file in model["files"]:
            assert file["path"].startswith("Assets/Avatars/") and ".." not in file["relative"]
            assert len(file["sha1"]) == 40 and all(c in "0123456789abcdef" for c in file["sha1"])
            assert 0 < file["bytes"] <= 32 * 1024 ** 2
    print(f"Verified 115 pinned models, MIT, metadata SHA-256 {hashlib.sha256(TARGET.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    main()
