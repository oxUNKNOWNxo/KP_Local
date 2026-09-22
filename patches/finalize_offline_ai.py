#!/usr/bin/env python3
from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
if not assets.is_dir():
    raise SystemExit(f"Assets directory not found: {assets}")

patch_root = Path(__file__).resolve().parent
bootstrap_source = patch_root / "ai" / "AIBootstrap.cs"
bootstrap_target = assets / "SibylSystem" / "AIBootstrap.cs"
if not bootstrap_source.is_file():
    raise SystemExit(f"AI bootstrap source missing: {bootstrap_source}")
shutil.copyfile(bootstrap_source, bootstrap_target)

core_path = assets / "SibylSystem" / "coreWrapper.cs"
core = core_path.read_text(encoding="utf-8-sig")
old_ptr = '''        private IntPtr getPtrString(string path)
        {
            IntPtr ptrFileName = Marshal.AllocHGlobal(path.Length + 1);
            byte[] s = System.Text.Encoding.UTF8.GetBytes(path);
            Marshal.Copy(s, 0, ptrFileName, s.Length);
            return ptrFileName;
        }'''
new_ptr = '''        private IntPtr getPtrString(string path)
        {
            byte[] s = System.Text.Encoding.UTF8.GetBytes(path);
            IntPtr ptrFileName = Marshal.AllocHGlobal(s.Length + 1);
            Marshal.Copy(s, 0, ptrFileName, s.Length);
            Marshal.WriteByte(ptrFileName, s.Length, 0);
            return ptrFileName;
        }'''
if old_ptr not in core:
    raise SystemExit("Could not locate legacy UTF-8 pointer helper")
core = core.replace(old_ptr, new_ptr, 1)
core_path.write_text(core, encoding="utf-8")

airoom_path = assets / "SibylSystem" / "Room" / "AIRoom.cs"
airoom = airoom_path.read_text(encoding="utf-8-sig")
marker = '''    void printFile()
    {
        Directory.CreateDirectory("deck");
        Directory.CreateDirectory("ai");
        Directory.CreateDirectory("ai/ydk");'''
replacement = '''    void printFile()
    {
        Directory.CreateDirectory("deck");
        Directory.CreateDirectory("ai");
        Directory.CreateDirectory("ai/ydk");
        AIBootstrap.EnsureInstalled();'''
if marker not in airoom:
    raise SystemExit("Could not locate AIRoom data bootstrap insertion point")
airoom = airoom.replace(marker, replacement, 1)
airoom_path.write_text(airoom, encoding="utf-8")

precy_path = assets / "SibylSystem" / "precy.cs"
precy = precy_path.read_text(encoding="utf-8-sig")
precy = precy.replace(
    'Program.I().cardDescription.RMSshow_none(InterString.Get("游戏内部出错，请重试，文件名中不能包含中文。"));',
    'Program.I().cardDescription.RMSshow_none("AIスクリプトまたはデッキの読み込みに失敗しました。");'
)
precy_path.write_text(precy, encoding="utf-8")

print("Finalized offline AI integration:")
print(f"  - installed {bootstrap_target}")
print("  - made UTF-8 native paths NUL-terminated and byte-safe")
print("  - enabled first-use bundled AI data bootstrap")
print("  - replaced obsolete non-ASCII filename warning")
