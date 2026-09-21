#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
if not assets.is_dir():
    raise SystemExit(f"Assets directory not found: {assets}")

candidates = []
for path in assets.rglob("*.cs"):
    try:
        text = path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError:
        continue
    if (
        "UpdateClientCoroutine()" in text
        and "UseBundledBasicData()" in text
        and "RetryBasicDataUpdate()" in text
    ):
        candidates.append((path, text))

if len(candidates) != 1:
    raise SystemExit(
        f"Expected exactly one Program source candidate, found {len(candidates)}: "
        f"{[str(p) for p, _ in candidates]}"
    )

path, text = candidates[0]

# Disable only the automatic basic-data synchronization performed at game
# startup. Do not modify the explicit Resource Update path: it remains
# available when the user intentionally invokes it.
#
# Do not depend on the exact declaration of gameStart(). The original C# may
# omit an explicit 'private' modifier although a decompiler later displays it.
startup_matches = list(
    re.finditer(r"(?m)^(?P<indent>[ \t]*)RetryBasicDataUpdate\(\);[ \t]*$", text)
)
if len(startup_matches) != 1:
    raise SystemExit(
        f"Expected exactly one standalone startup RetryBasicDataUpdate call, found {len(startup_matches)}"
    )

m = startup_matches[0]
indent = m.group("indent")
startup_replacement = (
    f"{indent}InitializeBasicDataSyncState();\n"
    f"{indent}UseBundledBasicData();"
)
text = text[: m.start()] + startup_replacement + text[m.end() :]

if re.search(r"(?m)^[ \t]*RetryBasicDataUpdate\(\);[ \t]*$", text):
    raise SystemExit("Startup RetryBasicDataUpdate call is still present after patch")
if "UseBundledBasicData();" not in text:
    raise SystemExit("UseBundledBasicData startup path was not installed")

path.write_text(text, encoding="utf-8")
print(f"Patched: {path}")
print("  - disabled forced basic-data sync at startup")
print("  - left explicit/manual Resource Update behavior unchanged")
