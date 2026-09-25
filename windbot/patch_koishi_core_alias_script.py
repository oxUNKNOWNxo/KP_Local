#!/usr/bin/env python3
from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: patch_koishi_core_alias_script.py <ygopro-core-root>")

root = Path(sys.argv[1])
path = root / "interpreter.cpp"
text = path.read_text(encoding="utf-8-sig")

old = """	load_card_script(pcard->data.get_original_code());
"""
new = """	// Match current EDOPro script selection semantics: nearby aliases are
	// alternate printings and share the alias script, while distant aliases
	// (anime/original variants) keep their own card script.
	if(pcard->data.alias && (pcard->data.alias < pcard->data.code + 10)
			&& (pcard->data.code < pcard->data.alias + 10))
		load_card_script(pcard->data.alias);
	else
		load_card_script(pcard->data.code);
"""

if old not in text:
    if "nearby aliases are" in text:
        print("Koishi core alias script-selection patch already applied.")
        raise SystemExit(0)
    raise SystemExit("Koishi core register_card script-selection anchor not found")

path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("Patched Koishi core alias script selection to current EDOPro semantics.")
