"""Build portable prepared review inputs; this does not perform manual validation."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

repository = Path(__file__).resolve().parents[2]
review = repository / "docs" / "authoring-manual-review"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path, help="Destination ZIP (outside the committed source tree recommended)")
args = parser.parse_args()
files = sorted(path for path in (review / "fixtures").rglob("*") if path.is_file())
entries = []
for path in files:
    if path.is_symlink():
        raise ValueError(f"Unexpected linked fixture: {path}")
    data = path.read_bytes()
    data.decode("utf-8")
    if path.suffix == ".json":
        json.loads(data)
    entries.append({"path": path.relative_to(review).as_posix(), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
manifest = {"status": "prepared-review-inputs-not-completed-manual-validation", "files": entries}
args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    payloads = [(path.relative_to(review).as_posix(), path.read_bytes()) for path in files]
    payloads += [(name, (review / name).read_bytes()) for name in ("README.md", "checklist.md", "stack.md")]
    payloads.append(("manifest.json", (json.dumps(manifest, indent=2) + "\n").encode()))
    for name, data in payloads:
        info = zipfile.ZipInfo(name, date_time=(2026, 10, 5, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o100644 << 16
        archive.writestr(info, data)
with zipfile.ZipFile(args.output) as archive:
    assert archive.testzip() is None
    for entry in entries:
        assert hashlib.sha256(archive.read(entry["path"])).hexdigest() == entry["sha256"]
print(json.dumps({"archive": str(args.output), "fixtures": len(entries), "sha256": hashlib.sha256(args.output.read_bytes()).hexdigest(), "status": manifest["status"]}))
