#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <output-directory>" >&2
  exit 2
fi

OUT="$(mkdir -p "$1" && cd "$1" && pwd)"
WORK="${RUNNER_TEMP:-/tmp}/koishipro-windbot-subset"
REPO="https://github.com/purerosefallen/windbot.git"

rm -rf "$WORK"
mkdir -p "$WORK/src"
git -C "$WORK/src" init -q
git -C "$WORK/src" remote add origin "$REPO"
git -C "$WORK/src" config http.version HTTP/1.1
git -C "$WORK/src" fetch --quiet --no-tags --depth=1 origin master
git -C "$WORK/src" checkout --quiet --detach FETCH_HEAD
SHA="$(git -C "$WORK/src" rev-parse HEAD)"

rm -rf "$OUT/src" "$OUT/data"
mkdir -p "$OUT/src" "$OUT/data/Decks" "$OUT/data/Dialogs"

copy_file() {
  local path="$1"
  mkdir -p "$OUT/src/$(dirname "$path")"
  cp "$WORK/src/$path" "$OUT/src/$path"
}

copy_file Logger.cs
for path in   Game/BattlePhase.cs   Game/BattlePhaseAction.cs   Game/ChainInfo.cs   Game/ClientCard.cs   Game/ClientField.cs   Game/Deck.cs   Game/Duel.cs   Game/GameAI.cs   Game/GameBehavior.cs   Game/GamePacketFactory.cs   Game/MainPhase.cs   Game/MainPhaseAction.cs   Game/Room.cs; do
  copy_file "$path"
done

while IFS= read -r path; do
  copy_file "$path"
done < <(find "$WORK/src/Game/AI" -maxdepth 1 -type f -name '*.cs' | sed "s#^$WORK/src/##" | sort)

while IFS= read -r path; do
  copy_file "$path"
done < <(find "$WORK/src/Game/AI/Enums" -maxdepth 1 -type f -name '*.cs' | sed "s#^$WORK/src/##" | sort)

while IFS= read -r path; do
  copy_file "$path"
done < <(find "$WORK/src/Game/AI/Decks" -maxdepth 1 -type f -name '*.cs' | sed "s#^$WORK/src/##" | sort)

for path in   YGOSharp.OCGWrapper/Card.cs   YGOSharp.OCGWrapper/CardsManager.cs   YGOSharp.OCGWrapper/NamedCard.cs   YGOSharp.OCGWrapper/NamedCardsManager.cs; do
  copy_file "$path"
done

while IFS= read -r path; do
  copy_file "$path"
done < <(find "$WORK/src/YGOSharp.OCGWrapper.Enums" -maxdepth 1 -type f -name '*.cs' ! -path '*/Properties/*' | sed "s#^$WORK/src/##" | sort)

while IFS= read -r path; do
  copy_file "$path"
done < <(find "$WORK/src/YGOSharp.Network/Enums" -maxdepth 1 -type f -name '*.cs' | sed "s#^$WORK/src/##" | sort)
copy_file YGOSharp.Network/Utils/BinaryExtensions.cs

while IFS= read -r path; do
  cp "$path" "$OUT/data/Decks/"
done < <(find "$WORK/src/Decks" -maxdepth 1 -type f -name '*.ydk' | sort)
if [ -f "$WORK/src/Dialogs/wof-Kasumisawa-Haruma.json" ]; then
  cp "$WORK/src/Dialogs/wof-Kasumisawa-Haruma.json" "$OUT/data/Dialogs/"
else
  cp "$WORK/src/Dialogs/default.json" "$OUT/data/Dialogs/wof-Kasumisawa-Haruma.json"
fi
cp "$WORK/src/LICENSE" "$OUT/WindBot-LICENSE.txt"
printf '%s\n' "$SHA" > "$OUT/windbot-revision.txt"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if compgen -G "$SCRIPT_DIR/runtime/*.cs" > /dev/null; then
  mkdir -p "$OUT/src/LocalRuntime"
  cp "$SCRIPT_DIR"/runtime/*.cs "$OUT/src/LocalRuntime/"
fi

python3 - "$OUT/src" <<'PY'
from pathlib import Path
import sys

root = Path(sys.argv[1])
replacements = [
    ("YGOSharp.OCGWrapper.Enums", "KoishiWindBot.OCGWrapper.Enums"),
    ("YGOSharp.OCGWrapper", "KoishiWindBot.OCGWrapper"),
    ("YGOSharp.Network.Utils", "KoishiWindBot.Network.Utils"),
    ("YGOSharp.Network.Enums", "KoishiWindBot.Network.Enums"),
    ("YGOSharp.Network", "KoishiWindBot.Network"),
]
for path in root.rglob("*.cs"):
    text = path.read_text(encoding="utf-8-sig")
    for old, new in replacements:
        text = text.replace(old, new)
    path.write_text(text, encoding="utf-8")

# Expose the same pre-duel choices WindBot normally returns through the
# server protocol, so KoishiPro2 can reuse its local RPS UI without a room.
behavior = root / "Game/GameBehavior.cs"
behavior_text = behavior.read_text(encoding="utf-8")
behavior_anchor = "        public int GetLocalPlayer(int player)\n"
if behavior_anchor not in behavior_text:
    raise SystemExit("GameBehavior pregame-choice anchor not found")
behavior_methods = """        public int ChooseRockPaperScissors()
        {
            return _ai.OnRockPaperScissors();
        }

        public bool ChooseFirst()
        {
            return _ai.OnSelectHand();
        }

"""
behavior_text = behavior_text.replace(behavior_anchor, behavior_methods + behavior_anchor, 1)
behavior.write_text(behavior_text, encoding="utf-8")


import re

deck_sources = sorted((root / "Game/AI/Decks").glob("*.cs"))
deck_data_root = root.parent / "data" / "Decks"
pattern = re.compile(
    r'\[Deck\(\s*"([^"]+)"'
    r'(?:\s*,\s*"([^"]*)")?'
    r'(?:\s*,\s*"([^"]*)")?'
    r'\s*\)\]\s*'
    r'(?:(?:public|internal|sealed|abstract|partial)\s+)*'
    r'class\s+([A-Za-z_][A-Za-z0-9_]*)',
    re.S,
)

entries = []
seen_names = set()
for source in deck_sources:
    source_text = source.read_text(encoding="utf-8-sig")
    for match in pattern.finditer(source_text):
        name, deck_file, level, class_name = match.groups()
        deck_file = deck_file or name
        level = level or "Normal"
        if name in seen_names:
            raise SystemExit("Duplicate WindBot deck name: " + name)
        if not (deck_data_root / (deck_file + ".ydk")).is_file():
            print("Skipping executor without bundled YDK: " + name + " -> " + deck_file)
            continue
        seen_names.add(name)
        entries.append((name, deck_file, level, class_name))

if len(entries) < 2:
    raise SystemExit("Expected multiple WindBot AI decks, found " + str(len(entries)))

entries.sort(key=lambda item: item[0].lower())

def cs(value):
    return value.replace("\\", "\\\\").replace('"', '\\"')

deck_mgr = root / "Game/AI/DecksManager.cs"
text = deck_mgr.read_text(encoding="utf-8")
text = text.replace("using System.Reflection;\n", "")
start = text.index("        public static void Init()")
end = text.index("\n        public static Executor Instantiate", start)
register_lines = []
for name, deck_file, level, class_name in entries:
    register_lines.append(
        '            _decks.Add("' + cs(name) + '", new DeckInstance("' +
        cs(deck_file) + '", typeof(WindBot.Game.AI.Decks.' + class_name + '), "' +
        cs(level) + '"));'
    )
replacement = """        public static void Init()
        {
            _decks = new Dictionary<string, DeckInstance>();
            _rand = new Random();

""" + "\n".join(register_lines) + """

            _list = new List<DeckInstance>();
            _list.AddRange(_decks.Values);
            Logger.WriteLine("Decks initialized, explicit iOS registry: " + _decks.Count);
        }
"""
text = text[:start] + replacement + text[end:]

instantiate_start = text.index("        public static Executor Instantiate")
instantiate_end = text.index("\n        public static bool HasDeck", instantiate_start)
factory_lines = []
for index, (name, deck_file, level, class_name) in enumerate(entries):
    prefix = "if" if index == 0 else "else if"
    factory_lines.append(
        "            " + prefix + " (infos.Type == typeof(WindBot.Game.AI.Decks." +
        class_name + "))\n                executor = new WindBot.Game.AI.Decks." +
        class_name + "(ai, duel);"
    )
instantiate = """        public static Executor Instantiate(GameAI ai, Duel duel)
        {
            if (_decks == null)
                Init();

            DeckInstance infos;
            string deck = ai.Game.Deck;

            if (deck != null && _decks.ContainsKey(deck))
                infos = _decks[deck];
            else
            {
                do
                {
                    infos = _list[_rand.Next(_list.Count)];
                }
                while (infos.Level != "Normal");
            }

            Executor executor;
""" + "\n".join(factory_lines) + """
            else
                throw new NotSupportedException("Executor is not registered for the iOS build: " + infos.Type.FullName);

            executor.Deck = infos.Deck;
            return executor;
        }

        public static string[] GetDeckNames()
        {
            if (_decks == null)
                Init();
            List<string> names = new List<string>(_decks.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        public static string GetDeckFile(string name)
        {
            if (_decks == null)
                Init();
            DeckInstance infos;
            return name != null && _decks.TryGetValue(name, out infos) ? infos.Deck : null;
        }

        public static string GetDeckLevel(string name)
        {
            if (_decks == null)
                Init();
            DeckInstance infos;
            return name != null && _decks.TryGetValue(name, out infos) ? infos.Level : null;
        }
"""
text = text[:instantiate_start] + instantiate + text[instantiate_end:]
text = text.replace(
"""        public static bool HasDeck(string name)
        {
            return _decks.ContainsKey(name);
        }""",
"""        public static bool HasDeck(string name)
        {
            if (_decks == null)
                Init();
            return name != null && _decks.ContainsKey(name);
        }"""
)
deck_mgr.write_text(text, encoding="utf-8")

catalog = root.parent / "data" / "deck-catalog.tsv"
catalog.write_text(
    "name\tfile\tlevel\texecutor\n" +
    "".join(name + "\t" + deck_file + "\t" + level + "\t" + class_name + "\n"
            for name, deck_file, level, class_name in entries),
    encoding="utf-8",
)
print("Generated explicit iOS WindBot registry: " + str(len(entries)) + " decks")
deck_mgr.write_text(text, encoding="utf-8")
PY

mkdir -p "$OUT/src/Local"

cat > "$OUT/src/Local/ProgramShim.cs" <<'CS'
using System;
using System.IO;

namespace WindBot
{
    public static class Program
    {
        public static readonly Random Rand = new Random();
        public static bool ServerMode = false;
        public static string DataRoot = ".";

        public static FileStream ReadFile(string directory, string name, string extension)
        {
            if (string.IsNullOrEmpty(name))
                name = "default";
            string path = Path.Combine(DataRoot, directory, name + "." + extension);
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }

    public static class Config
    {
        public static bool GetBool(string key, bool defaultValue = false)
        {
            return defaultValue;
        }
    }
}
CS

cat > "$OUT/src/Local/YGOClientShim.cs" <<'CS'
using System;
using System.IO;
using KoishiWindBot.Network.Enums;

namespace KoishiWindBot.Network
{
    public sealed class YGOClient
    {
        public Action<byte[]> Sent;
        public bool IsConnected { get; private set; } = true;

        public void Close()
        {
            IsConnected = false;
        }

        public void Send(BinaryWriter writer)
        {
            writer.Flush();
            MemoryStream stream = writer.BaseStream as MemoryStream;
            if (stream == null)
                throw new InvalidOperationException("Local WindBot packets must use a MemoryStream.");
            Sent?.Invoke(stream.ToArray());
        }

        public void Send(CtosMessage message)
        {
            Sent?.Invoke(new byte[] { (byte)message });
        }

        public void Send(CtosMessage message, byte value)
        {
            Sent?.Invoke(new byte[] { (byte)message, value });
        }

        public void Send(CtosMessage message, int value)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((byte)message);
                writer.Write(value);
                writer.Flush();
                Sent?.Invoke(stream.ToArray());
            }
        }
    }
}
CS

cat > "$OUT/src/Local/GameClientShim.cs" <<'CS'
using System;
using System.IO;
using KoishiWindBot.Network;
using KoishiWindBot.Network.Enums;

namespace WindBot.Game
{
    public sealed class GameClient
    {
        public YGOClient Connection { get; private set; }
        public string Username = "WindBot";
        public string Deck = "RadiantTyphoon";
        public string DeckFile = "AI_RadiantTyphoon";
        public string DeckCode;
        public string Dialog = "wof-Kasumisawa-Haruma";
        public int Hand;
        public bool Debug;
        public bool _chat;
        public string ExecutorName { get; private set; }
        public string CurrentSTOCMessage { get; private set; }

        public GameClient(YGOClient connection)
            : this(connection, "RadiantTyphoon", "AI_RadiantTyphoon")
        {
        }

        public GameClient(YGOClient connection, string deck, string deckFile)
        {
            Connection = connection;
            Deck = deck;
            DeckFile = deckFile;
        }

        internal void SetDeckContext(string executorName)
        {
            ExecutorName = executorName;
        }

        internal void SetCurrentSTOCMessage(string message)
        {
            CurrentSTOCMessage = message;
        }

        public void Surrender()
        {
            Connection.Send(CtosMessage.Surrender);
        }

        public void Chat(string message)
        {
            // Offline proof build keeps bot chat disabled.
        }
    }
}
CS

cat > "$OUT/src/Local/WindBotLocalRuntime.cs" <<'CS'
using System;
using System.IO;
using KoishiWindBot.Network;
using WindBot.Game;
using WindBot.Game.AI;
using KoishiWindBot.OCGWrapper;

namespace WindBot.Local
{
    public sealed class WindBotLocalRuntime
    {
        private readonly YGOClient _connection;
        private readonly GameClient _client;
        private readonly GameBehavior _behavior;

        public WindBotLocalRuntime(string dataRoot, string cardsDatabase, Action<byte[]> onClientPacket)
            : this(dataRoot, cardsDatabase, "RadiantTyphoon", "AI_RadiantTyphoon", onClientPacket)
        {
        }

        public WindBotLocalRuntime(
            string dataRoot,
            string cardsDatabase,
            string aiDeckName,
            string aiDeckFile,
            Action<byte[]> onClientPacket)
        {
            Program.DataRoot = dataRoot;
            CardsManager.Init(cardsDatabase);
            NamedCardsManager.Init(cardsDatabase);
            DecksManager.Init();

            _connection = new YGOClient();
            _connection.Sent = onClientPacket;
            _client = new GameClient(_connection, aiDeckName, aiDeckFile);
            _behavior = new GameBehavior(_client);
        }

        public void FeedServerPacket(byte[] packet)
        {
            using (MemoryStream stream = new MemoryStream(packet, false))
            using (BinaryReader reader = new BinaryReader(stream))
                _behavior.OnPacket(reader);
        }

        public int ChooseRockPaperScissors()
        {
            return _behavior.ChooseRockPaperScissors();
        }

        public bool ChooseFirst()
        {
            return _behavior.ChooseFirst();
        }

        public string ExecutorName
        {
            get { return _client.ExecutorName; }
        }
    }
}
CS

echo "Prepared WindBot subset at $OUT"
find "$OUT/src" -type f -name '*.cs' | sort
