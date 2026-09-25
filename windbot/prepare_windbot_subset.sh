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

copy_file Game/AI/Decks/RadiantTyphoonExecutor.cs

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

cp "$WORK/src/Decks/AI_RadiantTyphoon.ydk" "$OUT/data/Decks/"
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


# WindBot keeps its own card database separate from KoishiPro2's YGOSharp
# database. Load expansion CDBs as a fallback as well so cards that are newer
# than the bundled cards.cdb retain their real type/stats in the local duel.
cards_mgr = root / "YGOSharp.OCGWrapper/CardsManager.cs"
text = cards_mgr.read_text(encoding="utf-8")
text = text.replace("using System.Data;\\n", "using System.Data;\\nusing System;\\nusing System.IO;\\n")
old = """        internal static void Init(string databaseFullPath)
        {
            _cards = new Dictionary<int, Card>();

            using (SqliteConnection connection = new SqliteConnection("Data Source=" + databaseFullPath))
            {
                connection.Open();

                using (IDbCommand command = new SqliteCommand("SELECT id, ot, alias, setcode, type, level, race, attribute, atk, def FROM datas", connection))
                {
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            LoadCard(reader);
                        }
                    }
                }
            }
        }
"""
new = """        internal static void Init(string databaseFullPath)
        {
            _cards = new Dictionary<int, Card>();
            LoadDatabase(databaseFullPath);
            LoadExpansionDatabases(databaseFullPath);
        }

        private static void LoadExpansionDatabases(string baseDatabase)
        {
            string expansionDirectory = Path.Combine(Directory.GetCurrentDirectory(), "expansions");
            if (!Directory.Exists(expansionDirectory))
                return;

            string baseFullPath = Path.GetFullPath(baseDatabase);
            foreach (string path in Directory.GetFiles(expansionDirectory, "*.cdb"))
            {
                if (string.Equals(Path.GetFullPath(path), baseFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                LoadDatabase(path);
            }
        }

        private static void LoadDatabase(string databaseFullPath)
        {
            using (SqliteConnection connection = new SqliteConnection("Data Source=" + databaseFullPath))
            {
                connection.Open();

                using (IDbCommand command = new SqliteCommand("SELECT id, ot, alias, setcode, type, level, race, attribute, atk, def FROM datas", connection))
                {
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            LoadCard(reader);
                    }
                }
            }
        }
"""
if old not in text:
    raise SystemExit("CardsManager Init anchor not found")
text = text.replace(old, new, 1)
old = """        private static void LoadCard(IDataRecord reader)
        {
            Card card = new Card(reader);
            _cards.Add(card.Id, card);
        }
"""
new = """        private static void LoadCard(IDataRecord reader)
        {
            Card card = new Card(reader);
            if (!_cards.ContainsKey(card.Id))
                _cards.Add(card.Id, card);
        }
"""
if old not in text:
    raise SystemExit("CardsManager LoadCard anchor not found")
cards_mgr.write_text(text, encoding="utf-8")

named_mgr = root / "YGOSharp.OCGWrapper/NamedCardsManager.cs"
text = named_mgr.read_text(encoding="utf-8")
old = """                _cards = new Dictionary<int, NamedCard>();

                using (SqliteConnection connection = new SqliteConnection("Data Source=" + databaseFullPath))
                {
                    connection.Open();

                    using (IDbCommand command = new SqliteCommand(
                        "SELECT datas.id, ot, alias, setcode, type, level, race, attribute, atk, def, texts.name, texts.desc"
                        + " FROM datas INNER JOIN texts ON datas.id = texts.id",
                        connection))
                    {
                        using (IDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                LoadCard(reader);
                            }
                        }
                    }
                }
"""
new = """                _cards = new Dictionary<int, NamedCard>();
                LoadDatabase(databaseFullPath);
                LoadExpansionDatabases(databaseFullPath);
"""
if old not in text:
    raise SystemExit("NamedCardsManager Init anchor not found")
text = text.replace(old, new, 1)
insert_before = """        internal static NamedCard GetCard(int id)
"""
helpers = """        private static void LoadExpansionDatabases(string baseDatabase)
        {
            string expansionDirectory = Path.Combine(Directory.GetCurrentDirectory(), "expansions");
            if (!Directory.Exists(expansionDirectory))
                return;

            string baseFullPath = Path.GetFullPath(baseDatabase);
            foreach (string path in Directory.GetFiles(expansionDirectory, "*.cdb"))
            {
                if (string.Equals(Path.GetFullPath(path), baseFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                LoadDatabase(path);
            }
        }

        private static void LoadDatabase(string databaseFullPath)
        {
            using (SqliteConnection connection = new SqliteConnection("Data Source=" + databaseFullPath))
            {
                connection.Open();

                using (IDbCommand command = new SqliteCommand(
                    "SELECT datas.id, ot, alias, setcode, type, level, race, attribute, atk, def, texts.name, texts.desc"
                    + " FROM datas INNER JOIN texts ON datas.id = texts.id",
                    connection))
                {
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            LoadCard(reader);
                    }
                }
            }
        }

"""
if insert_before not in text:
    raise SystemExit("NamedCardsManager insertion anchor not found")
text = text.replace(insert_before, helpers + insert_before, 1)
old = """        private static void LoadCard(IDataRecord reader)
        {
            NamedCard card = new NamedCard(reader);
            _cards.Add(card.Id, card);
        }
"""
new = """        private static void LoadCard(IDataRecord reader)
        {
            NamedCard card = new NamedCard(reader);
            if (!_cards.ContainsKey(card.Id))
                _cards.Add(card.Id, card);
        }
"""
if old not in text:
    raise SystemExit("NamedCardsManager LoadCard anchor not found")
text = text.replace(old, new, 1)
named_mgr.write_text(text, encoding="utf-8")


deck_mgr = root / "Game/AI/DecksManager.cs"
text = deck_mgr.read_text(encoding="utf-8")
text = text.replace("using System.Reflection;\n", "")
start = text.index("        public static void Init()")
end = text.index("\n        public static Executor Instantiate", start)
replacement = """        public static void Init()
        {
            _decks = new Dictionary<string, DeckInstance>();
            _rand = new Random();

            Type type = typeof(WindBot.Game.AI.Decks.RadiantTyphoonExecutor);
            _decks.Add(
                \"RadiantTyphoon\",
                new DeckInstance(\"AI_RadiantTyphoon\", type, \"Normal\"));

            _list = new List<DeckInstance>();
            _list.AddRange(_decks.Values);
            Logger.WriteLine(\"Decks initialized, explicit iOS proof registry: \" + _decks.Count);
        }
"""
text = text[:start] + replacement + text[end:]
old = """            Executor executor = (Executor)Activator.CreateInstance(infos.Type, ai, duel);
            executor.Deck = infos.Deck;
            return executor;"""
new = """            Executor executor;
            if (infos.Type == typeof(WindBot.Game.AI.Decks.RadiantTyphoonExecutor))
                executor = new WindBot.Game.AI.Decks.RadiantTyphoonExecutor(ai, duel);
            else
                throw new NotSupportedException(\"Executor is not registered for the iOS proof build: \" + infos.Type.FullName);
            executor.Deck = infos.Deck;
            return executor;"""
if old not in text:
    raise SystemExit("DecksManager Instantiate anchor not found")
text = text.replace(old, new)
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
        {
            Connection = connection;
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
        {
            Program.DataRoot = dataRoot;
            CardsManager.Init(cardsDatabase);
            NamedCardsManager.Init(cardsDatabase);
            DecksManager.Init();

            _connection = new YGOClient();
            _connection.Sent = onClientPacket;
            _client = new GameClient(_connection);
            _behavior = new GameBehavior(_client);
        }

        public void FeedServerPacket(byte[] packet)
        {
            using (MemoryStream stream = new MemoryStream(packet, false))
            using (BinaryReader reader = new BinaryReader(stream))
                _behavior.OnPacket(reader);
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
