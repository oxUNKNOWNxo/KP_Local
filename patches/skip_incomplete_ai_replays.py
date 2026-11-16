#!/usr/bin/env python3
"""Reject incomplete local-AI replay files, including during application shutdown.

Keep normal Win/Draw/confirmed-surrender replay saving, user-selected saves,
and online-duel replay semantics unchanged.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
tcp_path = root / "Assets" / "SibylSystem" / "MonoHelpers" / "TcpHelper.cs"
if not tcp_path.is_file():
    raise SystemExit(f"Missing upstream replay implementation: {tcp_path}")
source = tcp_path.read_text(encoding="utf-8-sig")


def replace_exact(old: str, new: str, purpose: str) -> None:
    global source
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{purpose}: expected exactly one anchor, got {count}")
    source = source.replace(old, new, 1)


replace_exact(
    "        internal readonly List<Package> Packets = new List<Package>();",
    "        internal readonly List<Package> Packets = new List<Package>();\n"
    "        internal bool IsLocalAI;\n"
    "        internal bool HasDuelResult;",
    "ReplayRecord completion state",
)

replace_exact(
    "        currentRecord = new ReplayRecord { DuelNumber = recordDuelNumber, StartedAt = recordSessionStart };",
    "        currentRecord = new ReplayRecord\n"
    "        {\n"
    "            DuelNumber = recordDuelNumber,\n"
    "            StartedAt = recordSessionStart,\n"
    "            IsLocalAI = Program.I().ocgcore.InAI\n"
    "        };",
    "Record local-AI session at duel start",
)

replace_exact(
    "        if (record == null) return;\n"
    "        List<Package> packagesInRecord = record.Packets;",
    "        if (record == null) return;\n"
    "        // App shutdown, disconnect and next-duel rollover all call SaveRecord.\n"
    "        // Do not turn an unfinished local AI duel into a replay file.\n"
    "        if (record.IsLocalAI && !record.HasDuelResult)\n"
    "        {\n"
    "            record.Packets.Clear();\n"
    "            return;\n"
    "        }\n"
    "        List<Package> packagesInRecord = record.Packets;",
    "Prevent saving interrupted local AI records",
)

replace_exact(
    "            p.Record = currentRecord;\n"
    "            currentRecord.Packets.Add(p);",
    "            // Also capture AI state if the local-mode flag was set after Start.\n"
    "            if (Program.I().ocgcore.InAI) currentRecord.IsLocalAI = true;\n"
    "            if (p.Fuction == (int)GameMessage.Win) currentRecord.HasDuelResult = true;\n"
    "            p.Record = currentRecord;\n"
    "            currentRecord.Packets.Add(p);",
    "Record duel result before replay autosave",
)

tcp_path.write_text(source, encoding="utf-8")
verify = tcp_path.read_text(encoding="utf-8")
for marker in (
    "internal bool IsLocalAI;",
    "internal bool HasDuelResult;",
    "if (record.IsLocalAI && !record.HasDuelResult)",
    "if (p.Fuction == (int)GameMessage.Win) currentRecord.HasDuelResult = true;",
    "IsLocalAI = Program.I().ocgcore.InAI",
):
    if verify.count(marker) != 1:
        raise SystemExit(f"Local-AI incomplete-replay guard missing or duplicated: {marker}")
print("Local AI replays require GameMessage.Win before any file write.")
print("Normal/online replay behavior and completed AI replay saving remain unchanged.")
