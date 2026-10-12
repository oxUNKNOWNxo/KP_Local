#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
managed = assets / "SibylSystem" / "WindBotRuntime" / "LocalRuntime"


def read(path: Path) -> str:
    if not path.is_file():
        raise SystemExit(f"Required WindBot surrender target missing: {path}")
    return path.read_text(encoding="utf-8-sig")


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected one {label} anchor, found {count}")
    return text.replace(old, new, 1)


# Keep the established AI watch-mode message/input behavior. Only change its
# exit button while InAI is active so it enters the same surrender confirmation
# path used by a normal duel instead of immediately leaving the local duel.
ocgcore_path = assets / "SibylSystem" / "Ocgcore" / "Ocgcore.cs"
ocgcore = read(ocgcore_path)
watch_old = '                UIHelper.registEvent(toolBar, "exit_", onExit);'
watch_new = '''                if (InAI)
                    UIHelper.registEvent(toolBar, "exit_", onSurrender);
                else
                    UIHelper.registEvent(toolBar, "exit_", onExit);'''
ocgcore = replace_once(ocgcore, watch_old, watch_new, "AI watch exit binding")

forced_old = '''    public override void ES_RMS_ForcedYesNo(messageSystemValue result)
    {
        base.ES_RMS_ForcedYesNo(result);
        if (result.value == "yes")
        {
            surrended = true;'''
forced_new = '''    public override void ES_RMS_ForcedYesNo(messageSystemValue result)
    {
        base.ES_RMS_ForcedYesNo(result);
        if (InAI)
        {
            AIRoom aiRoom = Program.I().aiRoom as AIRoom;
            if (aiRoom != null && aiRoom.HandleWindBotSurrenderPromptResult(result.value == "yes"))
            {
                if (result.value == "yes")
                    surrended = true;
                return;
            }
        }
        if (result.value == "yes")
        {
            surrended = true;'''
ocgcore = replace_once(ocgcore, forced_old, forced_new, "forced yes/no surrender callback")

surrender_old = '''    void onSurrender()
    {
        if (
            Program.I().room.duelEnded == true'''
surrender_new = '''    void onSurrender()
    {
        if (InAI)
        {
            AIRoom aiRoom = Program.I().aiRoom as AIRoom;
            if (aiRoom != null && aiRoom.BeginWindBotSurrenderPrompt())
            {
                RMSshow_yesOrNoForce(
                    InterString.Get("你确定要投降吗？"),
                    new messageSystemValue { value = "yes", hint = "yes" },
                    new messageSystemValue { value = "no", hint = "no" }
                );
            }
            return;
        }

        if (
            Program.I().room.duelEnded == true'''
ocgcore = replace_once(ocgcore, surrender_old, surrender_new, "AI surrender entry")
write(ocgcore_path, ocgcore)

# AIRoom owns the active bridge, so expose two deliberately narrow hooks for
# the stock Ocgcore confirmation callback. The pending bit lives in the bridge
# to ensure unrelated forced yes/no dialogs can never be mistaken for surrender.
airoom_path = assets / "SibylSystem" / "Room" / "AIRoom.cs"
airoom = read(airoom_path)
airoom_anchor = '''    public override void preFrameFunction()
    {'''
airoom_insert = '''    public bool BeginWindBotSurrenderPrompt()
    {
        return windbot != null && windbot.BeginSurrenderPrompt();
    }

    public bool HandleWindBotSurrenderPromptResult(bool confirmed)
    {
        return windbot != null && windbot.HandleSurrenderPromptResult(confirmed);
    }

    public override void preFrameFunction()
    {'''
airoom = replace_once(airoom, airoom_anchor, airoom_insert, "AIRoom surrender proxy")
write(airoom_path, airoom)

# The bridge keeps confirmation state and converts a confirmed surrender into
# a local-duel result message. InAI must already be true when shiftCondition()
# wires the watch toolbar, otherwise exit_ would still bind to onExit.
bridge_path = assets / "SibylSystem" / "KoishiWindBotBridge.cs"
bridge = read(bridge_path)
bridge = replace_once(
    bridge,
    '''    private RadiantTyphoonLocalDuel duel;''',
    '''    private RadiantTyphoonLocalDuel duel;
    private bool surrenderPromptPending;''',
    "bridge surrender state",
)
bridge = replace_once(
    bridge,
    '''    public void Dispose()
    {
        if (duel != null)''',
    '''    public void Dispose()
    {
        surrenderPromptPending = false;
        if (duel != null)''',
    "bridge surrender dispose reset",
)
bridge_anchor = '''    public static void GetAiPregameChoices(string aiDeckName, out int hand, out bool aiWantsFirst)
    {'''
bridge_insert = '''    public bool BeginSurrenderPrompt()
    {
        if (duel == null || duel.IsEnded)
            return false;
        surrenderPromptPending = true;
        return true;
    }

    public bool HandleSurrenderPromptResult(bool confirmed)
    {
        if (!surrenderPromptPending)
            return false;

        surrenderPromptPending = false;
        if (confirmed && duel != null && !duel.IsEnded)
            duel.SurrenderHuman();
        return true;
    }

    public static void GetAiPregameChoices(string aiDeckName, out int hand, out bool aiWantsFirst)
    {'''
bridge = replace_once(bridge, bridge_anchor, bridge_insert, "bridge surrender methods")
bridge = replace_once(
    bridge,
    '''        Program.I().ocgcore.handler = Response;
        Program.I().ocgcore.shiftCondition(Ocgcore.Condition.watch);
        Program.I().ocgcore.InAI = true;''',
    '''        Program.I().ocgcore.handler = Response;
        Program.I().ocgcore.InAI = true;
        Program.I().ocgcore.shiftCondition(Ocgcore.Condition.watch);''',
    "InAI toolbar binding order",
)
write(bridge_path, bridge)

# Expose a single local-duel surrender operation. The router emits exactly the
# same MSG_WIN payload shape already used when WindBot itself surrenders, but
# with the AI player as winner. Reason 0x04 is the existing surrender reason.
local_duel_path = managed / "RadiantTyphoonLocalDuel.cs"
local_duel = read(local_duel_path)
local_duel_anchor = '''        public bool IsEnded
        {
            get { return _router.IsEnded; }
        }'''
local_duel_insert = '''        public void SurrenderHuman()
        {
            _router.SurrenderHuman();
        }

        public bool IsEnded
        {
            get { return _router.IsEnded; }
        }'''
local_duel = replace_once(local_duel, local_duel_anchor, local_duel_insert, "local duel surrender method")
write(local_duel_path, local_duel)

router_path = managed / "LocalDuelRouter.cs"
router = read(router_path)
router_anchor = '''        public void HandleAiClientPacket(byte[] packet)
        {'''
router_insert = '''        public void SurrenderHuman()
        {
            if (_ended)
                return;

            _log?.Invoke("[WindBot] Human requested surrender.");
            _pendingAiResponse = null;
            _waitingPlayer = -1;
            byte[] win = new byte[] { MsgWin, (byte)_aiPlayer, 0x04 };
            SendBoth(win);
            _ended = true;
        }

        public void HandleAiClientPacket(byte[] packet)
        {'''
router = replace_once(router, router_anchor, router_insert, "local router human surrender")
write(router_path, router)

# Fail fast if any of the generated integration points disappeared upstream.
checks = {
    ocgcore_path: (
        'UIHelper.registEvent(toolBar, "exit_", onSurrender);',
        "HandleWindBotSurrenderPromptResult",
        "BeginWindBotSurrenderPrompt",
    ),
    airoom_path: (
        "BeginWindBotSurrenderPrompt",
        "HandleWindBotSurrenderPromptResult",
    ),
    bridge_path: (
        "surrenderPromptPending",
        "BeginSurrenderPrompt",
        "HandleSurrenderPromptResult",
        "duel.SurrenderHuman();",
    ),
    local_duel_path: ("public void SurrenderHuman()",),
    router_path: (
        "public void SurrenderHuman()",
        'byte[] win = new byte[] { MsgWin, (byte)_aiPlayer, 0x04 };',
    ),
}
for path, markers in checks.items():
    text = read(path)
    for marker in markers:
        if marker not in text:
            raise SystemExit(f"WindBot surrender marker missing from {path}: {marker}")

print("Prepared WindBot AI surrender integration:")
print("  - AI watch exit now enters the stock surrender confirmation")
print("  - confirmed surrender emits the normal local MSG_WIN surrender result")
print("  - stock defeat/result and replay save UI remain authoritative")
