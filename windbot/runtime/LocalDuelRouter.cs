using System;
using System.Collections.Generic;
using System.IO;
using KoishiWindBot.Network.Enums;
using KoishiWindBot.OCGWrapper.Enums;
using WindBot.Local;

namespace KoishiWindBot.Local
{
    public sealed class LocalDuelRouter : IDisposable
    {
        private const byte MsgRetry = 1;
        private const byte MsgHint = 2;
        private const byte MsgStart = 4;
        private const byte MsgWin = 5;
        private const byte MsgUpdateData = 6;
        private const byte MsgUpdateCard = 7;
        private const byte MsgSelectBattleCmd = 10;
        private const byte MsgSelectIdleCmd = 11;
        private const byte MsgSelectEffectYn = 12;
        private const byte MsgSelectYesNo = 13;
        private const byte MsgSelectOption = 14;
        private const byte MsgSelectCard = 15;
        private const byte MsgSelectChain = 16;
        private const byte MsgSelectPlace = 18;
        private const byte MsgSelectPosition = 19;
        private const byte MsgSelectTribute = 20;
        private const byte MsgSelectCounter = 22;
        private const byte MsgSelectSum = 23;
        private const byte MsgSelectDisfield = 24;
        private const byte MsgSortCard = 25;
        private const byte MsgSelectUnselect = 26;
        private const byte MsgConfirmDecktop = 30;
        private const byte MsgConfirmCards = 31;
        private const byte MsgShuffleDeck = 32;
        private const byte MsgShuffleHand = 33;
        private const byte MsgRefreshDeck = 34;
        private const byte MsgSwapGraveDeck = 35;
        private const byte MsgShuffleSetCard = 36;
        private const byte MsgReverseDeck = 37;
        private const byte MsgDeckTop = 38;
        private const byte MsgShuffleExtra = 39;
        private const byte MsgNewTurn = 40;
        private const byte MsgNewPhase = 41;
        private const byte MsgConfirmExtratop = 42;
        private const byte MsgMove = 50;
        private const byte MsgPosChange = 53;
        private const byte MsgSet = 54;
        private const byte MsgSwap = 55;
        private const byte MsgFieldDisabled = 56;
        private const byte MsgSummoning = 60;
        private const byte MsgSummoned = 61;
        private const byte MsgSpSummoning = 62;
        private const byte MsgSpSummoned = 63;
        private const byte MsgFlipSummoning = 64;
        private const byte MsgFlipSummoned = 65;
        private const byte MsgChaining = 70;
        private const byte MsgChained = 71;
        private const byte MsgChainSolving = 72;
        private const byte MsgChainSolved = 73;
        private const byte MsgChainEnd = 74;
        private const byte MsgChainNegated = 75;
        private const byte MsgChainDisabled = 76;
        private const byte MsgCardSelected = 80;
        private const byte MsgRandomSelected = 81;
        private const byte MsgBecomeTarget = 83;
        private const byte MsgDraw = 90;
        private const byte MsgDamage = 91;
        private const byte MsgRecover = 92;
        private const byte MsgEquip = 93;
        private const byte MsgLpUpdate = 94;
        private const byte MsgUnequip = 95;
        private const byte MsgCardTarget = 96;
        private const byte MsgCancelTarget = 97;
        private const byte MsgPayLpCost = 100;
        private const byte MsgAddCounter = 101;
        private const byte MsgRemoveCounter = 102;
        private const byte MsgAttack = 110;
        private const byte MsgBattle = 111;
        private const byte MsgAttackDisabled = 112;
        private const byte MsgDamageStepStart = 113;
        private const byte MsgDamageStepEnd = 114;
        private const byte MsgMissedEffect = 120;
        private const byte MsgTossCoin = 130;
        private const byte MsgTossDice = 131;
        private const byte MsgRockPaperScissors = 132;
        private const byte MsgHandRes = 133;
        private const byte MsgAnnounceRace = 140;
        private const byte MsgAnnounceAttrib = 141;
        private const byte MsgAnnounceCard = 142;
        private const byte MsgAnnounceNumber = 143;
        private const byte MsgCardHint = 160;
        private const byte MsgPlayerHint = 165;
        private const byte MsgMatchKill = 170;
        private const byte MsgResetTime = 221;

        private const uint QueryCode = 0x1;
        private const uint QueryPosition = 0x2;
        private const byte PositionFaceDown = 0x0a;
        private const byte PositionFaceUp = 0x05;
        private const byte PositionReveal = 0x80;

        private readonly LocalDuelNative _native;
        private readonly WindBotLocalRuntime _ai;
        private readonly Action<byte[]> _humanGameMessage;
        private readonly Action<string> _log;
        private readonly int _humanPlayer;
        private readonly int _aiPlayer;

        private byte[] _pendingAiResponse;
        private int _waitingPlayer = -1;
        private int _lastResponsePlayer = -1;
        private bool _ended;
        private bool _pumping;

        public bool IsEnded { get { return _ended; } }
        public int WaitingPlayer { get { return _waitingPlayer; } }

        public LocalDuelRouter(LocalDuelNative native, WindBotLocalRuntime ai, Action<byte[]> humanGameMessage, Action<string> log, bool humanFirst)
        {
            _native = native ?? throw new ArgumentNullException("native");
            _ai = ai ?? throw new ArgumentNullException("ai");
            _humanGameMessage = humanGameMessage ?? throw new ArgumentNullException("humanGameMessage");
            _log = log;
            _humanPlayer = humanFirst ? 0 : 1;
            _aiPlayer = 1 - _humanPlayer;
        }

        public void SendInitialState(int life, int duelRule)
        {
            int humanDeck = _native.QueryFieldCount(0, LocalDuelNative.LocationDeck);
            int humanExtra = _native.QueryFieldCount(0, LocalDuelNative.LocationExtra);
            int aiDeck = _native.QueryFieldCount(1, LocalDuelNative.LocationDeck);
            int aiExtra = _native.QueryFieldCount(1, LocalDuelNative.LocationExtra);

            byte[] human = BuildStartPacket((byte)_humanPlayer, duelRule, life, humanDeck, humanExtra, aiDeck, aiExtra);
            byte[] bot = BuildStartPacket((byte)_aiPlayer, duelRule, life, humanDeck, humanExtra, aiDeck, aiExtra);
            SendToPlayer(_humanPlayer, human);
            SendToPlayer(_aiPlayer, bot);
            RefreshExtra(0);
            RefreshExtra(1);
        }

        public void Pump()
        {
            if (_ended || _pumping)
                return;

            _pumping = true;
            try
            {
                uint engineFlag = 0;
                while (!_ended)
                {
                    if (engineFlag == LocalDuelNative.ProcessorEnd)
                    {
                        _ended = true;
                        break;
                    }

                    uint result = _native.Process();
                    int length = (int)(result & LocalDuelNative.ProcessorBufferLength);
                    engineFlag = result & LocalDuelNative.ProcessorFlag;

                    if (length <= 0)
                    {
                        // Match Koishi's SingleDuel::Process(): PROCESSOR_WAITING
                        // by itself is an internal ocgcore yield, not proof that
                        // a player response is required. Real response waits
                        // always arrive with a selection message and Analyze()
                        // stops the pump there. Continuing here is essential
                        // for effects such as Radiant Typhoon Chant, which
                        // insert an empty PROCESSOR_WAIT while resolving.
                        continue;
                    }

                    byte[] message = _native.GetMessage(length);
                    bool wait = Analyze(message);
                    if (!wait)
                        continue;

                    if (_waitingPlayer == _aiPlayer && _pendingAiResponse != null)
                    {
                        byte[] response = _pendingAiResponse;
                        _pendingAiResponse = null;
                        _waitingPlayer = -1;
                        _native.SetResponse(response);
                        continue;
                    }
                    break;
                }
            }
            finally
            {
                _pumping = false;
            }
        }

        public void SubmitHumanResponse(byte[] response)
        {
            if (_ended)
                return;
            if (_waitingPlayer != _humanPlayer)
                throw new InvalidOperationException("The duel is not waiting for the human player.");
            _waitingPlayer = -1;
            _native.SetResponse(response ?? new byte[0]);
            Pump();
        }

        public void HandleAiClientPacket(byte[] packet)
        {
            if (packet == null || packet.Length == 0)
                return;

            CtosMessage type = (CtosMessage)packet[0];
            if (type == CtosMessage.Response)
            {
                int length = packet.Length - 1;
                _pendingAiResponse = new byte[length];
                if (length != 0)
                    Buffer.BlockCopy(packet, 1, _pendingAiResponse, 0, length);
                return;
            }

            if (type == CtosMessage.Surrender)
            {
                _log?.Invoke("[WindBot] AI requested surrender.");
                byte[] win = new byte[] { MsgWin, (byte)_humanPlayer, 0x04 };
                SendBoth(win);
                _ended = true;
                return;
            }

            if (type == CtosMessage.TimeConfirm)
                return;

            _log?.Invoke("[WindBot] Ignored local CTOS packet: " + type);
        }

        private bool Analyze(byte[] data)
        {
            int p = 0;
            while (p < data.Length)
            {
                int start = p;
                byte type = ReadByte(data, ref p);

                switch (type)
                {
                    case MsgResetTime:
                        Skip(data, ref p, 3);
                        break;

                    case MsgUpdateCard:
                    {
                        byte controller = ReadByte(data, ref p);
                        byte location = ReadByte(data, ref p);
                        byte sequence = ReadByte(data, ref p);
                        int cardLength = ReadInt32(data, ref p);
                        uint queryFlags = ReadUInt32(data, ref p);
                        Skip(data, ref p, cardLength - 8);
                        RefreshSingle(controller, location, sequence, queryFlags);
                        break;
                    }

                    case MsgRetry:
                        if (_lastResponsePlayer < 0)
                            throw new InvalidDataException("MSG_RETRY arrived before any player response.");
                        _waitingPlayer = _lastResponsePlayer;
                        SendToPlayer(_waitingPlayer, Slice(data, start, p));
                        return true;

                    case MsgHint:
                    {
                        byte hintType = ReadByte(data, ref p);
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 4);
                        byte[] packet = Slice(data, start, p);
                        if (hintType == 1 || hintType == 2 || hintType == 3 || hintType == 5)
                            SendToPlayer(player, packet);
                        else if (hintType == 4 || hintType == 6 || hintType == 7 || hintType == 8 || hintType == 9)
                            SendToPlayer(1 - player, packet);
                        else if (hintType == 10 || hintType == 11 || hintType == 21 || hintType == 22 || hintType == 23 || hintType == 24)
                            SendBoth(packet);
                        break;
                    }

                    case MsgWin:
                        Skip(data, ref p, 2);
                        SendBoth(Slice(data, start, p));
                        _ended = true;
                        return false;

                    case MsgSelectBattleCmd:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 11);
                        count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 8 + 2);
                        RefreshForDecision();
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectIdleCmd:
                    {
                        byte player = ReadByte(data, ref p);
                        for (int i = 0; i < 5; ++i)
                        {
                            int count = ReadByte(data, ref p);
                            Skip(data, ref p, count * 7);
                        }
                        int specialCount = ReadByte(data, ref p);
                        Skip(data, ref p, specialCount * 11 + 3);
                        RefreshForDecision();
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectEffectYn:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 12);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectYesNo:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 4);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectOption:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 4);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectCard:
                    case MsgSelectTribute:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 3);
                        int count = ReadByte(data, ref p);
                        byte[] packet = null;
                        int bodyStart = start;
                        for (int i = 0; i < count; ++i)
                        {
                            int codeOffset = p;
                            Skip(data, ref p, 4);
                            byte controller = ReadByte(data, ref p);
                            Skip(data, ref p, 3);
                            if (controller != player)
                            {
                                if (packet == null)
                                    packet = (byte[])data.Clone();
                                WriteInt32(packet, codeOffset, 0);
                            }
                        }
                        return SendDecisionAndWait(player, Slice(packet ?? data, bodyStart, p));
                    }

                    case MsgSelectUnselect:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 4);
                        byte[] packet = null;
                        for (int group = 0; group < 2; ++group)
                        {
                            int count = ReadByte(data, ref p);
                            for (int i = 0; i < count; ++i)
                            {
                                int codeOffset = p;
                                Skip(data, ref p, 4);
                                byte controller = ReadByte(data, ref p);
                                Skip(data, ref p, 3);
                                if (controller != player)
                                {
                                    if (packet == null)
                                        packet = (byte[])data.Clone();
                                    WriteInt32(packet, codeOffset, 0);
                                }
                            }
                        }
                        return SendDecisionAndWait(player, Slice(packet ?? data, start, p));
                    }

                    case MsgSelectChain:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, 9 + count * 14);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectPlace:
                    case MsgSelectDisfield:
                    case MsgSelectPosition:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 5);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectCounter:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 4);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 9);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSelectSum:
                    {
                        Skip(data, ref p, 1);
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 6);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 11);
                        count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 11);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgSortCard:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 7);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgConfirmDecktop:
                    case MsgConfirmExtratop:
                    {
                        ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 7);
                        SendBoth(Slice(data, start, p));
                        break;
                    }

                    case MsgConfirmCards:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 1);
                        int count = ReadByte(data, ref p);
                        int firstCard = p;
                        byte firstLocation = count > 0 ? PeekByte(data, firstCard + 5) : (byte)0;
                        Skip(data, ref p, count * 7);
                        byte[] packet = Slice(data, start, p);
                        if (firstLocation != LocalDuelNative.LocationDeck)
                            SendBoth(packet);
                        else
                            SendToPlayer(player, packet);
                        break;
                    }

                    case MsgShuffleDeck:
                        Skip(data, ref p, 1);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgShuffleHand:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        int ids = p;
                        Skip(data, ref p, count * 4);
                        byte[] full = Slice(data, start, p);
                        SendToPlayer(player, full);
                        byte[] hidden = (byte[])full.Clone();
                        for (int i = 0; i < count; ++i)
                            WriteInt32(hidden, (ids - start) + i * 4, 0);
                        SendToPlayer(1 - player, hidden);
                        RefreshHand(player, 0x781fff, 0);
                        break;
                    }

                    case MsgShuffleExtra:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        int ids = p;
                        Skip(data, ref p, count * 4);
                        byte[] full = Slice(data, start, p);
                        SendToPlayer(player, full);
                        byte[] hidden = (byte[])full.Clone();
                        for (int i = 0; i < count; ++i)
                            WriteInt32(hidden, (ids - start) + i * 4, 0);
                        SendToPlayer(1 - player, hidden);
                        RefreshExtra(player);
                        break;
                    }

                    case MsgRefreshDeck:
                        Skip(data, ref p, 1);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgSwapGraveDeck:
                    {
                        byte player = ReadByte(data, ref p);
                        SendBoth(Slice(data, start, p));
                        RefreshGrave(player);
                        break;
                    }

                    case MsgReverseDeck:
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgDeckTop:
                        Skip(data, ref p, 6);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgShuffleSetCard:
                    {
                        byte location = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 8);
                        SendBoth(Slice(data, start, p));
                        if (location == LocalDuelNative.LocationMonster)
                        {
                            RefreshMzone(0, 0x181fff, 0);
                            RefreshMzone(1, 0x181fff, 0);
                        }
                        else
                        {
                            RefreshSzone(0, 0x181fff, 0);
                            RefreshSzone(1, 0x181fff, 0);
                        }
                        break;
                    }

                    case MsgNewTurn:
                        RefreshForDecision();
                        Skip(data, ref p, 1);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgNewPhase:
                        Skip(data, ref p, 2);
                        SendBoth(Slice(data, start, p));
                        RefreshForDecision();
                        break;

                    case MsgMove:
                    {
                        int codeOffset = p;
                        Skip(data, ref p, 4);
                        byte previousController = ReadByte(data, ref p);
                        byte previousLocation = ReadByte(data, ref p);
                        Skip(data, ref p, 2);
                        byte currentController = ReadByte(data, ref p);
                        byte currentLocation = ReadByte(data, ref p);
                        byte currentSequence = ReadByte(data, ref p);
                        byte currentPosition = ReadByte(data, ref p);
                        Skip(data, ref p, 4);
                        byte[] full = Slice(data, start, p);
                        bool hideCode = ShouldHideFacedownCode(currentPosition);
                        if ((currentLocation & 0x0c) != 0)
                            StripRevealFlag(full, (codeOffset - start) + 8);
                        SendToPlayer(currentController, full);

                        byte[] hidden = (byte[])full.Clone();
                        if ((currentLocation & (LocalDuelNative.LocationGrave | 0x80)) == 0 &&
                            (((currentLocation & (LocalDuelNative.LocationDeck | LocalDuelNative.LocationHand)) != 0) || hideCode))
                            WriteInt32(hidden, codeOffset - start, 0);
                        SendToPlayer(1 - currentController, hidden);
                        if (currentLocation != 0 && (currentLocation & 0x80) == 0 &&
                            (currentLocation != previousLocation || currentController != previousController))
                            RefreshSingle(currentController, currentLocation, currentSequence);
                        break;
                    }

                    case MsgPosChange:
                    {
                        Skip(data, ref p, 4);
                        byte controller = ReadByte(data, ref p);
                        byte location = ReadByte(data, ref p);
                        byte sequence = ReadByte(data, ref p);
                        byte previous = ReadByte(data, ref p);
                        byte current = ReadByte(data, ref p);
                        SendBoth(Slice(data, start, p));
                        if ((previous & PositionFaceDown) != 0 && (current & PositionFaceUp) != 0)
                            RefreshSingle(controller, location, sequence);
                        break;
                    }

                    case MsgSet:
                    {
                        byte[] packet = Slice(data, start, Math.Min(data.Length, p + 4));
                        if (packet.Length >= 5)
                            WriteInt32(packet, 1, 0);
                        p += Math.Min(4, data.Length - p);
                        SendBoth(packet);
                        break;
                    }

                    case MsgSwap:
                    {
                        Skip(data, ref p, 4);
                        byte c1 = ReadByte(data, ref p);
                        byte l1 = ReadByte(data, ref p);
                        byte s1 = ReadByte(data, ref p);
                        Skip(data, ref p, 5);
                        byte c2 = ReadByte(data, ref p);
                        byte l2 = ReadByte(data, ref p);
                        byte s2 = ReadByte(data, ref p);
                        Skip(data, ref p, 1);
                        SendBoth(Slice(data, start, p));
                        RefreshSingle(c1, l1, s1);
                        RefreshSingle(c2, l2, s2);
                        break;
                    }

                    case MsgFieldDisabled:
                        Skip(data, ref p, 4);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgSummoning:
                        Skip(data, ref p, 8);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgSummoned:
                    case MsgSpSummoned:
                    case MsgFlipSummoned:
                        SendBoth(Slice(data, start, p));
                        RefreshMzone(0);
                        RefreshMzone(1);
                        RefreshSzone(0);
                        RefreshSzone(1);
                        break;

                    case MsgSpSummoning:
                    {
                        int codeOffset = p;
                        Skip(data, ref p, 4);
                        byte controller = ReadByte(data, ref p);
                        Skip(data, ref p, 2);
                        byte position = ReadByte(data, ref p);
                        byte[] full = Slice(data, start, p);
                        StripRevealFlag(full, codeOffset - start + 4);
                        SendToPlayer(controller, full);
                        byte[] hidden = (byte[])full.Clone();
                        if (ShouldHideFacedownCode(position))
                            WriteInt32(hidden, codeOffset - start, 0);
                        SendToPlayer(1 - controller, hidden);
                        break;
                    }

                    case MsgFlipSummoning:
                    {
                        int baseOffset = p;
                        Skip(data, ref p, 4);
                        byte controller = PeekByte(data, baseOffset + 4);
                        byte location = PeekByte(data, baseOffset + 5);
                        byte sequence = PeekByte(data, baseOffset + 6);
                        RefreshSingle(controller, location, sequence);
                        Skip(data, ref p, 4);
                        SendBoth(Slice(data, start, p));
                        break;
                    }

                    case MsgChaining:
                        Skip(data, ref p, 16);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgChained:
                    case MsgChainSolving:
                    case MsgChainSolved:
                    case MsgChainNegated:
                    case MsgChainDisabled:
                        Skip(data, ref p, 1);
                        SendBoth(Slice(data, start, p));
                        if (type == MsgChained || type == MsgChainSolved)
                            RefreshForDecision();
                        break;

                    case MsgChainEnd:
                        SendBoth(Slice(data, start, p));
                        RefreshForDecision();
                        break;

                    case MsgCardSelected:
                    {
                        ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 4);
                        break;
                    }

                    case MsgRandomSelected:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 4);
                        SendBoth(Slice(data, start, p));
                        break;
                    }

                    case MsgBecomeTarget:
                    {
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count * 4);
                        SendBoth(Slice(data, start, p));
                        break;
                    }

                    case MsgDraw:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        int cards = p;
                        Skip(data, ref p, count * 4);
                        byte[] full = Slice(data, start, p);
                        SendToPlayer(player, full);
                        byte[] hidden = (byte[])full.Clone();
                        for (int i = 0; i < count; ++i)
                        {
                            int off = (cards - start) + i * 4;
                            uint code = ReadUInt32At(hidden, off);
                            if ((code & 0x80000000u) == 0)
                                WriteInt32(hidden, off, 0);
                        }
                        SendToPlayer(1 - player, hidden);
                        break;
                    }

                    case MsgDamage:
                    case MsgRecover:
                    case MsgLpUpdate:
                    case MsgPayLpCost:
                        Skip(data, ref p, 5);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgEquip:
                    case MsgCardTarget:
                    case MsgCancelTarget:
                        Skip(data, ref p, 8);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgUnequip:
                        Skip(data, ref p, 4);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgAddCounter:
                    case MsgRemoveCounter:
                        Skip(data, ref p, 7);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgAttack:
                        Skip(data, ref p, 8);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgBattle:
                        Skip(data, ref p, 26);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgAttackDisabled:
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgDamageStepStart:
                    case MsgDamageStepEnd:
                        SendBoth(Slice(data, start, p));
                        RefreshMzone(0);
                        RefreshMzone(1);
                        break;

                    case MsgMissedEffect:
                    {
                        byte player = PeekByte(data, p);
                        Skip(data, ref p, 8);
                        SendToPlayer(player, Slice(data, start, p));
                        break;
                    }

                    case MsgTossCoin:
                    case MsgTossDice:
                    {
                        ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, count);
                        SendBoth(Slice(data, start, p));
                        break;
                    }

                    case MsgRockPaperScissors:
                    {
                        byte player = ReadByte(data, ref p);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgHandRes:
                        Skip(data, ref p, 1);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgAnnounceRace:
                    case MsgAnnounceAttrib:
                    {
                        byte player = ReadByte(data, ref p);
                        Skip(data, ref p, 5);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgAnnounceCard:
                    case MsgAnnounceNumber:
                    {
                        byte player = ReadByte(data, ref p);
                        int count = ReadByte(data, ref p);
                        Skip(data, ref p, 4 * count);
                        return SendDecisionAndWait(player, Slice(data, start, p));
                    }

                    case MsgCardHint:
                        Skip(data, ref p, 9);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgPlayerHint:
                        Skip(data, ref p, 6);
                        SendBoth(Slice(data, start, p));
                        break;

                    case MsgMatchKill:
                        Skip(data, ref p, 4);
                        break;

                    default:
                        throw new InvalidDataException("Unsupported Koishi ocgcore message in local WindBot router: " + type);
                }
            }
            return false;
        }

        private bool SendDecisionAndWait(int player, byte[] packet)
        {
            _waitingPlayer = player;
            _lastResponsePlayer = player;
            SendToPlayer(player, packet);
            return true;
        }

        private void RefreshForDecision()
        {
            RefreshMzone(0);
            RefreshMzone(1);
            RefreshSzone(0);
            RefreshSzone(1);
            RefreshHand(0);
            RefreshHand(1);
        }

        private void RefreshMzone(int player, uint flags = 0x881fff, int useCache = 1)
        {
            RefreshHiddenField(player, LocalDuelNative.LocationMonster, flags, useCache);
        }

        private void RefreshSzone(int player, uint flags = 0xe81fff, int useCache = 1)
        {
            RefreshHiddenField(player, LocalDuelNative.LocationSpell, flags, useCache);
        }

        private void RefreshHiddenField(int player, byte location, uint flags, int useCache)
        {
            byte[] query = _native.QueryField((byte)player, location, flags | QueryCode | QueryPosition, useCache);
            var hiddenSegments = new List<Tuple<int, int>>();
            int p = 0;
            while (p < query.Length)
            {
                int segment = ReadInt32(query, ref p);
                if (segment <= 0 || p + segment - 4 > query.Length)
                    break;
                if (segment > 4)
                {
                    byte position = GetPosition(query, p, 8);
                    bool hide = ShouldHideFacedownCode(position);
                    StripRevealFlag(query, p + 8);
                    if (hide)
                        hiddenSegments.Add(Tuple.Create(p, segment - 4));
                }
                p += segment - 4;
            }

            SendToPlayer(player, BuildUpdateData((byte)player, location, query));
            byte[] hiddenQuery = (byte[])query.Clone();
            foreach (var segment in hiddenSegments)
                Array.Clear(hiddenQuery, segment.Item1, segment.Item2);
            SendToPlayer(1 - player, BuildUpdateData((byte)player, location, hiddenQuery));
        }

        private void RefreshHand(int player, uint flags = 0x681fff, int useCache = 1)
        {
            byte[] query = _native.QueryField((byte)player, LocalDuelNative.LocationHand, flags | QueryCode | QueryPosition, useCache);
            SendToPlayer(player, BuildUpdateData((byte)player, LocalDuelNative.LocationHand, query));

            byte[] hidden = (byte[])query.Clone();
            int p = 0;
            while (p < hidden.Length)
            {
                int segment = ReadInt32(hidden, ref p);
                if (segment <= 0 || p + segment - 4 > hidden.Length)
                    break;
                if (segment > 4)
                {
                    byte position = GetPosition(hidden, p, 8);
                    if ((position & PositionFaceUp) == 0)
                        Array.Clear(hidden, p, segment - 4);
                }
                p += segment - 4;
            }
            SendToPlayer(1 - player, BuildUpdateData((byte)player, LocalDuelNative.LocationHand, hidden));
        }

        private void RefreshGrave(int player, uint flags = 0x81fff, int useCache = 1)
        {
            byte[] query = _native.QueryField((byte)player, LocalDuelNative.LocationGrave, flags | QueryCode | QueryPosition, useCache);
            SendBoth(BuildUpdateData((byte)player, LocalDuelNative.LocationGrave, query));
        }

        private void RefreshExtra(int player, uint flags = 0xe81fff, int useCache = 1)
        {
            byte[] query = _native.QueryField((byte)player, LocalDuelNative.LocationExtra, flags | QueryCode | QueryPosition, useCache);
            SendToPlayer(player, BuildUpdateData((byte)player, LocalDuelNative.LocationExtra, query));
        }

        private void RefreshSingle(int player, byte location, byte sequence, uint flags = 0xf81fff)
        {
            byte[] query = _native.QueryCard((byte)player, location, sequence, flags | QueryCode | QueryPosition, 0);
            if (query.Length <= 4)
            {
                SendToPlayer(player, BuildUpdateCard((byte)player, location, sequence, query));
                return;
            }

            byte position = GetPosition(query, 0, 12);
            bool hide = (position & PositionFaceDown) != 0;
            if ((location & 0x0c) != 0)
            {
                hide = ShouldHideFacedownCode(position);
                StripRevealFlag(query, 12);
            }
            SendToPlayer(player, BuildUpdateCard((byte)player, location, sequence, query));

            byte[] hidden = (byte[])query.Clone();
            if (hide)
            {
                byte[] minimal = new byte[16];
                WriteInt32(minimal, 0, 16);
                WriteUInt32(minimal, 4, QueryCode | QueryPosition);
                WriteUInt32(minimal, 8, 0);
                Buffer.BlockCopy(hidden, 12, minimal, 12, 4);
                hidden = minimal;
            }
            SendToPlayer(1 - player, BuildUpdateCard((byte)player, location, sequence, hidden));
        }

        private void SendBoth(byte[] gameMessage)
        {
            SendToPlayer(0, (byte[])gameMessage.Clone());
            SendToPlayer(1, (byte[])gameMessage.Clone());
        }

        private void SendToPlayer(int player, byte[] gameMessage)
        {
            if (player == _humanPlayer)
            {
                _humanGameMessage(gameMessage);
                return;
            }
            if (player != _aiPlayer)
                throw new InvalidDataException("Invalid routed player index: " + player);

            byte[] packet = new byte[gameMessage.Length + 1];
            packet[0] = (byte)StocMessage.GameMsg;
            Buffer.BlockCopy(gameMessage, 0, packet, 1, gameMessage.Length);
            _ai.FeedServerPacket(packet);
        }

        private static byte[] BuildStartPacket(byte playerType, int duelRule, int life, int deck0, int extra0, int deck1, int extra1)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(MsgStart);
                writer.Write(playerType);
                writer.Write((byte)duelRule);
                writer.Write(life);
                writer.Write(life);
                writer.Write((ushort)deck0);
                writer.Write((ushort)extra0);
                writer.Write((ushort)deck1);
                writer.Write((ushort)extra1);
                return stream.ToArray();
            }
        }

        private static byte[] BuildUpdateData(byte player, byte location, byte[] query)
        {
            byte[] result = new byte[3 + query.Length];
            result[0] = MsgUpdateData;
            result[1] = player;
            result[2] = location;
            Buffer.BlockCopy(query, 0, result, 3, query.Length);
            return result;
        }

        private static byte[] BuildUpdateCard(byte player, byte location, byte sequence, byte[] query)
        {
            byte[] result = new byte[4 + query.Length];
            result[0] = MsgUpdateCard;
            result[1] = player;
            result[2] = location;
            result[3] = sequence;
            Buffer.BlockCopy(query, 0, result, 4, query.Length);
            return result;
        }

        private static bool ShouldHideFacedownCode(byte position)
        {
            return (position & PositionFaceDown) != 0 && (position & PositionReveal) == 0;
        }

        private static byte GetPosition(byte[] buffer, int segmentBodyStart, int offset)
        {
            int pos = segmentBodyStart + offset;
            if (pos + 4 > buffer.Length)
                return 0;
            uint info = ReadUInt32At(buffer, pos);
            return (byte)(info >> 24);
        }

        private static byte StripRevealFlag(byte[] buffer, int offset)
        {
            if (offset + 4 > buffer.Length)
                return 0;
            uint info = ReadUInt32At(buffer, offset);
            info &= ~(0x80u << 24);
            WriteUInt32(buffer, offset, info);
            return (byte)(info >> 24);
        }

        private static byte ReadByte(byte[] data, ref int p)
        {
            Need(data, p, 1);
            return data[p++];
        }

        private static byte PeekByte(byte[] data, int p)
        {
            Need(data, p, 1);
            return data[p];
        }

        private static int ReadInt32(byte[] data, ref int p)
        {
            Need(data, p, 4);
            int value = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24);
            p += 4;
            return value;
        }

        private static uint ReadUInt32(byte[] data, ref int p)
        {
            uint value = ReadUInt32At(data, p);
            p += 4;
            return value;
        }

        private static uint ReadUInt32At(byte[] data, int p)
        {
            Need(data, p, 4);
            return (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24));
        }

        private static void WriteInt32(byte[] data, int p, int value)
        {
            WriteUInt32(data, p, unchecked((uint)value));
        }

        private static void WriteUInt32(byte[] data, int p, uint value)
        {
            Need(data, p, 4);
            data[p] = (byte)value;
            data[p + 1] = (byte)(value >> 8);
            data[p + 2] = (byte)(value >> 16);
            data[p + 3] = (byte)(value >> 24);
        }

        private static void Skip(byte[] data, ref int p, int count)
        {
            if (count < 0)
                throw new InvalidDataException("Negative packet length.");
            Need(data, p, count);
            p += count;
        }

        private static void Need(byte[] data, int p, int count)
        {
            if (p < 0 || count < 0 || p + count > data.Length)
                throw new EndOfStreamException("Malformed Koishi ocgcore packet.");
        }

        private static byte[] Slice(byte[] data, int start, int end)
        {
            if (end < start)
                throw new InvalidDataException("Invalid packet slice.");
            byte[] result = new byte[end - start];
            Buffer.BlockCopy(data, start, result, 0, result.Length);
            return result;
        }

        public void Dispose()
        {
            _native.Dispose();
        }
    }
}
