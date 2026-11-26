"""Fetch the unmodified classic Steve skin using Mojang's published version and file hashes."""
from hashlib import sha1, sha256
from io import BytesIO
import json
from pathlib import Path
from urllib.parse import urlparse
from urllib.request import urlopen
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parent
MANIFEST = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"
MEMBER = "assets/minecraft/textures/entity/steve.png"

def official(url, max_size, expected=None):
    parts = urlparse(url)
    if parts.scheme != "https" or not parts.hostname.endswith(".mojang.com"):
        raise ValueError("Unexpected Minecraft distribution origin")
    with urlopen(url, timeout=25) as response:
        content = response.read(max_size + 1)
    if len(content) > max_size or (expected and sha1(content).hexdigest() != expected):
        raise ValueError("Mojang artifact length or SHA-1 does not match")
    return content

def main():
    versions = json.loads(official(MANIFEST, 2_000_000))["versions"]
    entry = next(v for v in versions if v["id"] == "1.8" and v["type"] == "release")
    meta = json.loads(official(entry["url"], 500_000, entry["sha1"]))
    client = meta["downloads"]["client"]
    payload = official(client["url"], client["size"], client["sha1"])
    with ZipFile(BytesIO(payload)) as archive:
        if archive.getinfo(MEMBER).file_size > 128_000:
            raise ValueError("Unexpected skin size")
        skin = archive.read(MEMBER)
    (ROOT / "skin.png").write_bytes(skin)
    origin = {"game": "Minecraft: Java Edition", "version": meta["id"], "releaseTime": meta["releaseTime"],
              "manifest": MANIFEST, "version_json": entry["url"], "version_sha1": entry["sha1"],
              "client": client["url"], "client_sha1": client["sha1"], "client_bytes": len(payload),
              "member": MEMBER, "skin_sha1": sha1(skin).hexdigest(), "skin_sha256": sha256(skin).hexdigest()}
    (ROOT / "origin.json").write_text(json.dumps(origin, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(origin, ensure_ascii=False))

if __name__ == "__main__":
    main()
