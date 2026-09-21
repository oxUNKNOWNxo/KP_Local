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

# Stop the automatic online basic-data synchronization at every launch.
# Do not depend on the exact source declaration of gameStart(): the original
# source may omit an explicit 'private' modifier even though decompiled C#
# displays it. In Program.cs the standalone startup call is unique.
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

# Keep cards.cdb local/custom even when the user explicitly runs Resource
# Update. lflist.conf and strings.conf continue to use the original online
# updater, transaction, validation, rollback and hot-reload code.
# The URL declarations are unique in Program.cs, so this also avoids depending
# on whether UpdateClientCoroutine() has an explicit access modifier.
start_marker = 'string url = "https://cdntx2.moecube.com/koishipro/content/cards.cdb";'
next_marker = 'string url2 = "https://cdntx2.moecube.com/koishipro/content/lflist.conf";'
a = text.find(start_marker)
b = text.find(next_marker)
if a < 0 or b < 0 or b <= a:
    raise SystemExit("Could not identify cards.cdb update section")
if text.find(start_marker, a + 1) >= 0 or text.find(next_marker, b + 1) >= 0:
    raise SystemExit("Expected unique cards.cdb/lflist update markers")

line_start = text.rfind("\n", 0, a) + 1
next_line_start = text.rfind("\n", 0, b) + 1
indent2 = text[line_start:a]
replacement = (
    f"{indent2}// Keep the user-provided/local cards.cdb. Online resource updates must not overwrite it.\n"
    f"{indent2}cardsDbDownloadSucceeded = true;\n"
    f'{indent2}SetBasicDataProgress(BasicDataUpdateState.Checking, "卡片库", 1f / 3f);\n'
)
text = text[:line_start] + replacement + text[next_line_start:]

# Sanity checks before writing anything.
if re.search(r"(?m)^[ \t]*RetryBasicDataUpdate\(\);[ \t]*$", text):
    raise SystemExit("Startup RetryBasicDataUpdate call is still present after patch")
if start_marker in text:
    raise SystemExit("cards.cdb online URL is still present after patch")
if next_marker not in text:
    raise SystemExit("lflist.conf updater was unexpectedly removed")
if 'https://cdntx2.moecube.com/koishipro/content/strings.conf' not in text:
    raise SystemExit("strings.conf updater was unexpectedly removed")

path.write_text(text, encoding="utf-8")
print(f"Patched: {path}")
print("  - disabled forced basic-data sync at startup")
print("  - protected local cards.cdb from manual online resource updates")
