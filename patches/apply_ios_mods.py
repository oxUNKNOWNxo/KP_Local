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


def method_span(src: str, signature: str):
    start = src.find(signature)
    if start < 0:
        raise RuntimeError(f"Method signature not found: {signature}")
    brace = src.find("{", start)
    if brace < 0:
        raise RuntimeError(f"Method body not found: {signature}")

    depth = 0
    i = brace
    in_str = False
    verbatim = False
    escape = False
    in_char = False
    line_comment = False
    block_comment = False

    while i < len(src):
        c = src[i]
        n = src[i + 1] if i + 1 < len(src) else ""

        if line_comment:
            if c == "\n":
                line_comment = False
        elif block_comment:
            if c == "*" and n == "/":
                block_comment = False
                i += 1
        elif in_str:
            if verbatim:
                if c == '"' and n == '"':
                    i += 1
                elif c == '"':
                    in_str = False
                    verbatim = False
            else:
                if escape:
                    escape = False
                elif c == "\\":
                    escape = True
                elif c == '"':
                    in_str = False
        elif in_char:
            if escape:
                escape = False
            elif c == "\\":
                escape = True
            elif c == "'":
                in_char = False
        else:
            if c == "/" and n == "/":
                line_comment = True
                i += 1
            elif c == "/" and n == "*":
                block_comment = True
                i += 1
            elif c == "@" and n == '"':
                in_str = True
                verbatim = True
                i += 1
            elif c == '"':
                in_str = True
            elif c == "'":
                in_char = True
            elif c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    return start, i + 1
        i += 1

    raise RuntimeError(f"Unterminated method: {signature}")


# Stop the automatic online basic-data synchronization at every launch.
# UseBundledBasicData() is the application's own safe path for allowing local
# data to be used without making the first online sync mandatory.
s, e = method_span(text, "private void gameStart()")
block = text[s:e]
match = re.search(r"(?m)^(\s*)RetryBasicDataUpdate\(\);\s*$", block)
if match is None or len(re.findall(r"RetryBasicDataUpdate\(\);", block)) != 1:
    raise SystemExit("Expected exactly one startup RetryBasicDataUpdate call")
indent = match.group(1)
block = (
    block[: match.start()]
    + indent
    + "InitializeBasicDataSyncState();\n"
    + indent
    + "UseBundledBasicData();"
    + block[match.end() :]
)
text = text[:s] + block + text[e:]

# Keep cards.cdb local/custom even when the user explicitly runs Resource
# Update. lflist.conf and strings.conf continue to use the original online
# updater, transaction, validation, rollback and hot-reload code.
s, e = method_span(text, "private IEnumerator UpdateClientCoroutine()")
block = text[s:e]
start_marker = (
    'string url = "https://cdntx2.moecube.com/koishipro/content/cards.cdb";'
)
next_marker = (
    'string url2 = "https://cdntx2.moecube.com/koishipro/content/lflist.conf";'
)
a = block.find(start_marker)
b = block.find(next_marker)
if a < 0 or b < 0 or b <= a:
    raise SystemExit("Could not identify cards.cdb update section")

line_start = block.rfind("\n", 0, a) + 1
next_line_start = block.rfind("\n", 0, b) + 1
indent2 = block[line_start:a]
replacement = (
    f"{indent2}// Keep the user-provided/local cards.cdb. Online resource updates must not overwrite it.\n"
    f"{indent2}cardsDbDownloadSucceeded = true;\n"
    f'{indent2}SetBasicDataProgress(BasicDataUpdateState.Checking, "卡片库", 1f / 3f);\n'
)
block = block[:line_start] + replacement + block[next_line_start:]
text = text[:s] + block + text[e:]

path.write_text(text, encoding="utf-8")
print(f"Patched: {path}")
print("  - disabled forced basic-data sync at startup")
print("  - protected local cards.cdb from manual online resource updates")
