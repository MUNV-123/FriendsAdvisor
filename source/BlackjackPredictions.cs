using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FriendsAdvisor
{
    // Only local lists and freshly constructed RNG instances are advanced. Never
    // call the game's DrawCardFromDeck, ShuffleDeck, PlayerHit or DealerPlay.
    internal static class BlackjackPredictions
    {
        private sealed class DeckCopy
        {
            private readonly GameBase game;
            private List<CardData> cards;

            internal DeckCopy(GameBase game, IEnumerable<CardData> cards)
            {
                this.game = game;
                this.cards = new List<CardData>(cards);
            }

            internal DeckCopy Clone() { return new DeckCopy(game, cards); }

            internal CardData Draw()
            {
                if (cards.Count == 0) cards = BuildDeck(game);
                if (cards.Count == 0) throw new InvalidOperationException("21点没有有效牌堆。");
                CardData card = cards[0];
                cards.RemoveAt(0);
                return card;
            }
        }

        internal static bool TryDescribe(GameBase game, Random rng, out string text)
        {
            text = null;
            if (!(game is Blackjack) || rng == null) return false;
            var deck = new DeckCopy(game, Read.Field<List<CardData>>(game, "deck"));
            if (!game.isPlaying)
            {
                if (!Read.Field<bool>(game, "deckInitialized")) deck = new DeckCopy(game, BuildDeck(game));
                var player = new List<CardData>();
                var dealer = new List<CardData>();
                DealInitial(deck, player, dealer, 0);
                text = InitialDescription(deck, player, dealer, false);
                return true;
            }

            string phase = Read.Value(game, "gameState").ToString();
            var playerHand = Hand(game, "playerHand");
            var dealerHand = Hand(game, "dealerHand");
            var splitHand = Hand(game, "splitHand");
            bool split = Read.Field<bool>(game, "hasSplitThisRound");
            int active = Read.Field<int>(game, "activeHandIndex");
            if (phase == "Waiting")
            {
                int dealt = playerHand.Count + dealerHand.Count;
                if (split || dealt > 3 || playerHand.Count != (dealt + 1) / 2 || dealerHand.Count != dealt / 2)
                {
                    text = "正在准备起手牌，等待牌局状态同步。";
                    return true;
                }
                var pending = new List<string>();
                for (int i = dealt; i < 4; i++)
                {
                    CardData card = deck.Draw();
                    if (i % 2 == 0) playerHand.Add(card); else dealerHand.Add(card);
                    pending.Add((i % 2 == 0 ? "你 " : "庄 ") + CardName(card));
                }
                text = "起手发牌中；接下来：" + (pending.Count == 0 ? "起手牌已齐" : string.Join(" → ", pending.ToArray())) + "。\n"
                    + InitialDescription(deck, playerHand, dealerHand, true);
                return true;
            }

            var result = new StringBuilder();
            result.Append("你的牌：").Append(HandDescription(playerHand));
            if (split) result.Append("\n分牌手：").Append(HandDescription(splitHand));
            result.Append("\n庄家完整牌（含暗牌）：").Append(HandDescription(dealerHand)).Append("。");
            if (phase == "Finished")
            {
                result.Append("\n本局正在结算；下一局抽牌预览会在结算后更新。");
                text = result.ToString();
                return true;
            }
            if (phase == "DealerTurn")
            {
                AddDealerForecast(result, deck.Clone(), dealerHand, playerHand, split ? splitHand : null, "庄家接下来补牌");
                text = result.ToString();
                return true;
            }
            if (phase != "PlayerTurn" || active < 0 || active > 1 || (active == 1 && !split))
            {
                result.Append("\n等待可操作的牌局状态。");
                text = result.ToString();
                return true;
            }

            var current = active == 0 ? playerHand : splitHand;
            if (current.Count == 0 || dealerHand.Count < 2)
            {
                result.Append("\n等待起手牌发完。");
                text = result.ToString();
                return true;
            }
            var afterHit = new List<CardData>(current);
            CardData next = deck.Clone().Draw();
            afterHit.Add(next);
            bool[] doubled = Read.Field<bool[]>(game, "handDoubled");
            bool canDouble = current.Count == 2 && doubled != null && doubled.Length > active && !doubled[active];
            result.Append("\n当前操作：").Append(split ? "第 " + (active + 1) + " 手" : "你的手牌").Append("。");
            result.Append("\n下次要牌").Append(canDouble ? "（如可加倍，也是这张）" : "").Append("：")
                .Append(CardName(next)).Append(" → ").Append(ValueDescription(afterHit)).Append("。");
            if (split)
            {
                bool[] completed = Read.Field<bool[]>(game, "handCompleted");
                if (active == 0)
                    result.Append("\n第一手停牌、加倍或要牌后达到21点/爆牌，会切换第二手；庄家在第二手结束后才补牌。");
                else if (completed != null && completed.Length > 0 && completed[0])
                    result.Append("\n第一手已结束；第二手停牌、加倍或要牌后达到21点/爆牌，会进入庄家回合。");
            }
            else AddSplitPreview(result, deck.Clone(), current);
            AddQueue(result, deck.Clone());
            string condition = split && active == 0 ? "若两手都停牌、不再要牌，庄家补牌" : "若你现在停牌，庄家补牌";
            AddDealerForecast(result, deck.Clone(), dealerHand, playerHand, split ? splitHand : null, condition);
            result.Append("\n双方共用同一牌堆；你继续要牌、加倍或分牌会改变庄家之后拿到的牌。");
            text = result.ToString();
            return true;
        }

        private static string InitialDescription(DeckCopy deck, List<CardData> player, List<CardData> dealer, bool dealing)
        {
            var result = new StringBuilder();
            result.Append(dealing ? "起手完成后" : "下局起手").Append("：\n你 ").Append(HandDescription(player))
                .Append("；庄 ").Append(HandDescription(dealer)).Append("（第二张为暗牌）。");
            if (HandValue(player) == 21)
            {
                result.Append("\n你的起手为Blackjack；当前游戏规则直接结算，庄家不再补牌。");
                return result.ToString();
            }
            CardData next = deck.Clone().Draw();
            var afterHit = new List<CardData>(player);
            afterHit.Add(next);
            result.Append("\n起手后你若要牌：").Append(CardName(next)).Append(" → ").Append(ValueDescription(afterHit)).Append("。");
            AddSplitPreview(result, deck.Clone(), player);
            AddQueue(result, deck.Clone());
            AddDealerForecast(result, deck.Clone(), dealer, player, null, "若你起手后直接停牌，庄家补牌");
            result.Append("\n你和庄家共用牌堆；继续要牌、加倍或分牌会改变庄家之后的抽牌。");
            return result.ToString();
        }

        private static void DealInitial(DeckCopy deck, List<CardData> player, List<CardData> dealer, int start)
        {
            for (int i = start; i < 4; i++)
            {
                CardData card = deck.Draw();
                if (i % 2 == 0) player.Add(card); else dealer.Add(card);
            }
        }

        private static void AddQueue(StringBuilder result, DeckCopy deck)
        {
            var queue = new string[6];
            for (int i = 0; i < queue.Length; i++) queue[i] = CardName(deck.Draw());
            result.Append("\n共享牌堆前6张：").Append(string.Join(" → ", queue)).Append("。");
        }

        private static void AddSplitPreview(StringBuilder result, DeckCopy deck, List<CardData> player)
        {
            if (player.Count != 2 || player[0].Rank != player[1].Rank) return;
            CardData first = deck.Draw();
            CardData second = deck.Draw();
            var hand0 = new List<CardData> { player[0], first };
            var hand1 = new List<CardData> { player[1], second };
            result.Append("\n若分牌（需足够余额）：第一手补 ").Append(CardName(first)).Append(" → ").Append(ValueDescription(hand0))
                .Append("；第二手补 ").Append(CardName(second)).Append(" → ").Append(ValueDescription(hand1)).Append("。");
            result.Append("\n这是分牌时的牌序预览，是否分牌由你决定。");
            if (HandValue(hand0) == 21 || HandValue(hand1) == 21)
                result.Append("分牌后起手21点仍等待你的操作。");
        }

        private static void AddDealerForecast(StringBuilder result, DeckCopy deck, List<CardData> dealer, List<CardData> player, List<CardData> split, string label)
        {
            var finalDealer = new List<CardData>(dealer);
            var draws = new List<string>();
            while (HandValue(finalDealer) < 17)
            {
                if (draws.Count >= 32) throw new InvalidOperationException("21点庄家抽牌超过支持范围。");
                CardData card = deck.Draw();
                finalDealer.Add(card);
                draws.Add(CardName(card));
            }
            result.Append("\n").Append(label).Append("：")
                .Append(draws.Count == 0 ? "无需补牌" : string.Join(" → ", draws.ToArray()))
                .Append(" → ").Append(ValueDescription(finalDealer)).Append("。");
            int dealerValue = HandValue(finalDealer);
            if (split != null)
                result.Append("\n在该条件下：第1手 ").Append(Outcome(HandValue(player), dealerValue)).Append("；第2手 ").Append(Outcome(HandValue(split), dealerValue)).Append("。");
            else
                result.Append("\n在该条件下：你 ").Append(Outcome(HandValue(player), dealerValue)).Append("。");
        }

        private static List<CardData> BuildDeck(GameBase game)
        {
            int number = Read.Field<int>(game, "numberOfDecks");
            if (number <= 0 || number > 64) throw new InvalidOperationException("21点牌堆数量超出支持范围。");
            var cards = new List<CardData>(number * 52);
            for (int i = 0; i < number; i++)
                foreach (Suit suit in Enum.GetValues(typeof(Suit)))
                    foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                        if ((int)rank != 0) cards.Add(new CardData(suit, rank));
            Random random = Read.PeekRandom(game, unchecked(cards.Count * 10000));
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = random.Next(0, i + 1);
                CardData saved = cards[i];
                cards[i] = cards[j];
                cards[j] = saved;
            }
            return cards;
        }

        private static List<CardData> Hand(GameBase game, string name)
        {
            return new List<CardData>((IEnumerable<CardData>)Read.Value(game, name));
        }

        internal static int HandValue(IEnumerable<CardData> hand)
        {
            int value = 0, aces = 0;
            foreach (CardData card in hand)
            {
                if (card.Rank == Rank.Ace) { aces++; value += 11; }
                else value += card.GetBlackjackValue();
            }
            while (value > 21 && aces > 0) { value -= 10; aces--; }
            return value;
        }

        private static string HandDescription(List<CardData> hand)
        {
            var cards = new List<string>();
            foreach (CardData card in hand) cards.Add(CardName(card));
            return (cards.Count == 0 ? "无牌" : string.Join("、", cards.ToArray())) + "（" + ValueDescription(hand) + "）";
        }

        private static string ValueDescription(List<CardData> hand)
        {
            int value = HandValue(hand);
            return value.ToString(CultureInfo.InvariantCulture) + "点" + (value > 21 ? "，爆牌" : "");
        }

        private static string Outcome(int player, int dealer)
        {
            if (player > 21) return "爆牌，负";
            if (dealer > 21 || player > dealer) return "胜";
            return player == dealer ? "平" : "负";
        }

        private static string CardName(CardData card)
        {
            string rank = card.Rank == Rank.Ace ? "A" : card.Rank == Rank.Jack ? "J" : card.Rank == Rank.Queen ? "Q" : card.Rank == Rank.King ? "K" : ((int)card.Rank).ToString(CultureInfo.InvariantCulture);
            string suit = card.Suit == Suit.Hearts ? "♥" : card.Suit == Suit.Diamonds ? "♦" : card.Suit == Suit.Clubs ? "♣" : "♠";
            return rank + suit;
        }
    }
}
