using System;
using System.Collections.Generic;
using KoishiWindBot.OCGWrapper;
using WindBot.Game;
using WindBot.Local;

namespace KoishiWindBot.Local
{
    public sealed class RadiantTyphoonLocalDuel : IDisposable
    {
        private readonly LocalDuelNative _native;
        private readonly WindBotLocalRuntime _windBot;
        private readonly LocalDuelRouter _router;

        public RadiantTyphoonLocalDuel(string dataRoot, string cardsDatabase, Action<byte[]> humanGameMessage, Action<string> log)
        {
            LocalDuelRouter routerRef = null;
            _windBot = new WindBotLocalRuntime(
                dataRoot,
                cardsDatabase,
                packet =>
                {
                    if (routerRef != null)
                        routerRef.HandleAiClientPacket(packet);
                });

            LocalDuelNative.Configure(
                dataRoot,
                code =>
                {
                    Card card = Card.Get((int)code);
                    if (card == null)
                        return null;
                    return new CardRecord
                    {
                        Code = (uint)card.Id,
                        Alias = (uint)card.Alias,
                        Setcode = unchecked((ulong)card.Setcode),
                        Type = (uint)card.Type,
                        Level = (uint)card.Level,
                        Attribute = (uint)card.Attribute,
                        Race = (uint)card.Race,
                        Attack = card.Attack,
                        Defense = card.Defense,
                        LScale = (uint)card.LScale,
                        RScale = (uint)card.RScale,
                        LinkMarker = (uint)card.LinkMarker,
                        RuleCode = 0
                    };
                },
                log);

            _native = new LocalDuelNative();
            _router = new LocalDuelRouter(_native, _windBot, humanGameMessage, log);
            routerRef = _router;
        }

        public void Start(IList<int> humanMain, IList<int> humanExtra, int life = 8000, int startHand = 5, int drawCount = 1, int duelRule = 5)
        {
            if (humanMain == null)
                throw new ArgumentNullException("humanMain");
            if (humanExtra == null)
                humanExtra = new List<int>();

            Deck aiDeck = Deck.Load("AI_RadiantTyphoon");
            if (aiDeck == null)
                throw new InvalidOperationException("Could not load AI_RadiantTyphoon.ydk.");

            uint[] seed = LocalDuelNative.CreateSeedSequence();
            _native.Create(seed);
            _native.SetPlayerInfo(0, life, startHand, drawCount);
            _native.SetPlayerInfo(1, life, startHand, drawCount);
            _native.SetRegistry("duel_mode", "single");
            _native.SetRegistry("start_lp", life.ToString());
            _native.SetRegistry("start_hand", startHand.ToString());
            _native.SetRegistry("draw_count", drawCount.ToString());
            _native.SetRegistry("player_name_0", "Player");
            _native.SetRegistry("player_name_1", "WindBot");
            _native.SetRegistry("player_type_0", "0");
            _native.SetRegistry("player_type_1", "1");

            _native.Preload("./script/patches/entry.lua");
            _native.Preload("./script/special.lua");
            _native.Preload("./script/init.lua");

            LoadDeck(humanMain, 0, LocalDuelNative.LocationDeck);
            LoadDeck(humanExtra, 0, LocalDuelNative.LocationExtra);
            foreach (NamedCard card in aiDeck.Cards)
                _native.AddCard((uint)card.Id, 1, 1, LocalDuelNative.LocationDeck);
            foreach (NamedCard card in aiDeck.ExtraCards)
                _native.AddCard((uint)card.Id, 1, 1, LocalDuelNative.LocationExtra);

            _router.SendInitialState(life, duelRule);
            uint options = unchecked((uint)duelRule << 16);
            _native.Start(options);
            _router.Pump();
        }

        public void SubmitHumanResponse(byte[] response)
        {
            _router.SubmitHumanResponse(response);
        }

        public bool IsEnded
        {
            get { return _router.IsEnded; }
        }

        public int WaitingPlayer
        {
            get { return _router.WaitingPlayer; }
        }

        private void LoadDeck(IList<int> cards, byte player, byte location)
        {
            for (int i = cards.Count - 1; i >= 0; --i)
                _native.AddCard((uint)cards[i], player, player, location);
        }

        public void Dispose()
        {
            _router.Dispose();
        }
    }
}
