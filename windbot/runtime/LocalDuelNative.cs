using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KoishiWindBot.Local
{
    public sealed class CardRecord
    {
        public uint Code;
        public uint Alias;
        public ulong Setcode;
        public uint Type;
        public uint Level;
        public uint Attribute;
        public uint Race;
        public int Attack;
        public int Defense;
        public uint LScale;
        public uint RScale;
        public uint LinkMarker;
        public uint RuleCode;
    }

    public unsafe sealed class LocalDuelNative : IDisposable
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string NativeLibrary = "__Internal";
#else
        private const string NativeLibrary = "koishi_ocgcore";
#endif
        public const uint ProcessorBufferLength = 0x0fffffff;
        public const uint ProcessorFlag = 0xf0000000;
        public const uint ProcessorWaiting = 0x10000000;
        public const uint ProcessorEnd = 0x20000000;

        public const byte LocationDeck = 0x01;
        public const byte LocationHand = 0x02;
        public const byte LocationMonster = 0x04;
        public const byte LocationSpell = 0x08;
        public const byte LocationGrave = 0x10;
        public const byte LocationRemoved = 0x20;
        public const byte LocationExtra = 0x40;
        public const byte PositionFaceDownDefense = 0x08;

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct NativeCardData
        {
            public uint Code;
            public uint Alias;
            public fixed ushort Setcode[16];
            public uint Type;
            public uint Level;
            public uint Attribute;
            public uint Race;
            public int Attack;
            public int Defense;
            public uint LScale;
            public uint RScale;
            public uint LinkMarker;
            public uint RuleCode;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate IntPtr ScriptReader(IntPtr scriptName, int* length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate uint CardReader(uint code, NativeCardData* data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint MessageHandler(IntPtr duel, uint messageType);

        public delegate CardRecord CardProvider(uint code);

        private static ScriptReader _scriptReaderDelegate = ReadScript;
        private static CardReader _cardReaderDelegate = ReadCard;
        private static MessageHandler _messageHandlerDelegate = OnNativeMessage;
        private static string _dataRoot = ".";
        private static CardProvider _cardProvider;
        private static Action<string> _log;
        private static IntPtr _scriptBuffer = IntPtr.Zero;
        private static int _scriptBufferCapacity;

        private IntPtr _duel;

        public IntPtr Handle { get { return _duel; } }
        public bool IsCreated { get { return _duel != IntPtr.Zero; } }

        public static void Configure(string dataRoot, CardProvider provider, Action<string> log)
        {
            _dataRoot = string.IsNullOrEmpty(dataRoot) ? "." : dataRoot;
            _cardProvider = provider;
            _log = log;
            set_script_reader(_scriptReaderDelegate);
            set_card_reader(_cardReaderDelegate);
            set_message_handler(_messageHandlerDelegate);
        }

        public void Create(uint[] seedSequence)
        {
            if (seedSequence == null || seedSequence.Length != 8)
                throw new ArgumentException("Koishi ocgcore requires exactly 8 seed values.", "seedSequence");
            Dispose();
            _duel = create_duel_v2(seedSequence);
            if (_duel == IntPtr.Zero)
                throw new InvalidOperationException("create_duel_v2 returned a null duel.");
        }

        public static uint[] CreateSeedSequence()
        {
            var random = new Random(unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Millisecond));
            var result = new uint[8];
            var bytes = new byte[4];
            for (int i = 0; i < result.Length; ++i)
            {
                random.NextBytes(bytes);
                result[i] = BitConverter.ToUInt32(bytes, 0);
            }
            return result;
        }

        public void SetPlayerInfo(int player, int life, int startHand, int drawCount)
        {
            EnsureCreated();
            set_player_info(_duel, player, life, startHand, drawCount);
        }

        public void SetRegistry(string key, string value)
        {
            EnsureCreated();
            set_registry_value(_duel, key, value);
        }

        public bool Preload(string path)
        {
            EnsureCreated();
            return preload_script(_duel, path) != 0;
        }

        public void AddCard(uint code, byte owner, byte player, byte location)
        {
            EnsureCreated();
            new_card(_duel, code, owner, player, location, 0, PositionFaceDownDefense);
        }

        public void Start(uint options)
        {
            EnsureCreated();
            start_duel(_duel, options);
        }

        public uint Process()
        {
            EnsureCreated();
            return process(_duel);
        }

        public byte[] GetMessage(int length)
        {
            EnsureCreated();
            if (length <= 0)
                return new byte[0];
            var buffer = new byte[Math.Max(length, 0x2000)];
            int actual = get_message(_duel, buffer);
            if (actual <= 0)
                return new byte[0];
            if (actual == buffer.Length)
                return buffer;
            var result = new byte[actual];
            Buffer.BlockCopy(buffer, 0, result, 0, actual);
            return result;
        }

        public void SetResponse(byte[] response)
        {
            EnsureCreated();
            if (response == null)
                response = new byte[0];
            if (response.Length > 255)
                throw new ArgumentOutOfRangeException("response", "ocgcore response is limited to 255 bytes.");
            var fixedBuffer = new byte[256];
            if (response.Length != 0)
                Buffer.BlockCopy(response, 0, fixedBuffer, 0, response.Length);
            set_responseb(_duel, fixedBuffer);
        }

        public int QueryFieldCount(byte player, byte location)
        {
            EnsureCreated();
            return query_field_count(_duel, player, location);
        }

        public byte[] QueryField(byte player, byte location, uint flags, int useCache)
        {
            EnsureCreated();
            var buffer = new byte[0x20000];
            int length = query_field_card(_duel, player, location, flags, buffer, useCache);
            if (length <= 0)
                return new byte[0];
            var result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }

        public byte[] QueryCard(byte player, byte location, byte sequence, uint flags, int useCache)
        {
            EnsureCreated();
            var buffer = new byte[0x4000];
            int length = query_card(_duel, player, location, sequence, flags, buffer, useCache);
            if (length <= 0)
                return new byte[0];
            var result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }

        public void Dispose()
        {
            if (_duel != IntPtr.Zero)
            {
                end_duel(_duel);
                _duel = IntPtr.Zero;
            }
        }

        private void EnsureCreated()
        {
            if (_duel == IntPtr.Zero)
                throw new InvalidOperationException("The local duel has not been created.");
        }

        private static string ResolveScriptPath(string requested)
        {
            if (string.IsNullOrEmpty(requested))
                return null;

            string normalized = requested.Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);
            if (Path.IsPathRooted(normalized))
                return normalized;

            string[] parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string path = _dataRoot;
            foreach (string part in parts)
            {
                if (part == "..")
                    return null;
                path = Path.Combine(path, part);
            }

            // The bundled WindBotData scripts remain authoritative.  The
            // expansions/script folder is only a fallback for card scripts
            // that are not present in the bundled script snapshot, allowing
            // newly announced cards to be tested before the bundled scripts
            // catch up without overriding an existing standard script.
            if (File.Exists(path))
                return path;

            string expansionPath = ResolveExpansionCardScriptPath(parts);
            if (expansionPath != null && File.Exists(expansionPath))
            {
                _log?.Invoke("[WindBot/Core] using expansion card script: " + expansionPath);
                return expansionPath;
            }

            return path;
        }

        private static string ResolveExpansionCardScriptPath(string[] parts)
        {
            if (parts == null || parts.Length != 2 ||
                !string.Equals(parts[0], "script", StringComparison.OrdinalIgnoreCase))
                return null;

            string fileName = parts[1];
            if (fileName.Length < 6 ||
                (fileName[0] != 'c' && fileName[0] != 'C') ||
                !fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                return null;

            int digitsEnd = fileName.Length - 4;
            for (int i = 1; i < digitsEnd; ++i)
            {
                if (fileName[i] < '0' || fileName[i] > '9')
                    return null;
            }

            return Path.Combine("expansions", "script", fileName);
        }

#if UNITY_IOS && !UNITY_EDITOR
        [AOT.MonoPInvokeCallback(typeof(ScriptReader))]
#endif
        private static unsafe IntPtr ReadScript(IntPtr scriptName, int* length)
        {
            try
            {
                string requested = Marshal.PtrToStringAnsi(scriptName);
                string path = ResolveScriptPath(requested);
                if (path == null || !File.Exists(path))
                {
                    if (length != null)
                        *length = 0;
                    _log?.Invoke("[WindBot/Core] script not found: " + requested);
                    return IntPtr.Zero;
                }

                byte[] bytes = File.ReadAllBytes(path);
                if (_scriptBufferCapacity < bytes.Length)
                {
                    if (_scriptBuffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(_scriptBuffer);
                    _scriptBufferCapacity = Math.Max(bytes.Length, 4096);
                    _scriptBuffer = Marshal.AllocHGlobal(_scriptBufferCapacity);
                }
                Marshal.Copy(bytes, 0, _scriptBuffer, bytes.Length);
                if (length != null)
                    *length = bytes.Length;
                return _scriptBuffer;
            }
            catch (Exception ex)
            {
                if (length != null)
                    *length = 0;
                _log?.Invoke("[WindBot/Core] script reader error: " + ex.Message);
                return IntPtr.Zero;
            }
        }

#if UNITY_IOS && !UNITY_EDITOR
        [AOT.MonoPInvokeCallback(typeof(CardReader))]
#endif
        private static unsafe uint ReadCard(uint code, NativeCardData* output)
        {
            if (output == null)
                return 0;
            *output = default(NativeCardData);
            CardRecord card = _cardProvider == null ? null : _cardProvider(code);
            if (card == null)
                return 0;

            output->Code = card.Code != 0 ? card.Code : code;
            output->Alias = card.Alias;
            ulong setcodes = card.Setcode;
            for (int i = 0; i < 16; ++i)
            {
                output->Setcode[i] = (ushort)(setcodes & 0xffff);
                if (i < 3)
                    setcodes >>= 16;
                else
                    setcodes = 0;
            }
            output->Type = card.Type;
            output->Level = card.Level;
            output->Attribute = card.Attribute;
            output->Race = card.Race;
            output->Attack = card.Attack;
            output->Defense = card.Defense;
            output->LScale = card.LScale;
            output->RScale = card.RScale;
            output->LinkMarker = card.LinkMarker;
            output->RuleCode = card.RuleCode;
            return output->Code;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [AOT.MonoPInvokeCallback(typeof(MessageHandler))]
#endif
        private static uint OnNativeMessage(IntPtr duel, uint messageType)
        {
            try
            {
                var buffer = new byte[4096];
                get_log_message(duel, buffer);
                int zero = Array.IndexOf(buffer, (byte)0);
                int length = zero >= 0 ? zero : buffer.Length;
                string message = System.Text.Encoding.UTF8.GetString(buffer, 0, length);
                if (!string.IsNullOrEmpty(message))
                    _log?.Invoke("[WindBot/Core] " + message);
            }
            catch
            {
            }
            return 0;
        }

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void set_script_reader(ScriptReader reader);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void set_card_reader(CardReader reader);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void set_message_handler(MessageHandler handler);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr create_duel_v2([In] uint[] seedSequence);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void start_duel(IntPtr duel, uint options);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void end_duel(IntPtr duel);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void set_player_info(IntPtr duel, int player, int life, int startHand, int drawCount);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void new_card(IntPtr duel, uint code, byte owner, byte player, byte location, byte sequence, byte position);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint process(IntPtr duel);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int get_message(IntPtr duel, [Out] byte[] buffer);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void get_log_message(IntPtr duel, [Out] byte[] buffer);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void set_responseb(IntPtr duel, [In] byte[] buffer);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int query_field_count(IntPtr duel, byte player, byte location);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int query_field_card(IntPtr duel, byte player, byte location, uint queryFlags, [Out] byte[] buffer, int useCache);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int query_card(IntPtr duel, byte player, byte location, byte sequence, uint queryFlags, [Out] byte[] buffer, int useCache);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int preload_script(IntPtr duel, string scriptName);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void set_registry_value(IntPtr duel, string key, string value);
    }
}
