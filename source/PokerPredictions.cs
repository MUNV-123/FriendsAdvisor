using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace FriendsAdvisor
{
    // Poker keeps its deck between rounds. All removals, replacement branches and
    // empty-deck shuffles below operate on local copies; no gameplay method is called.
    internal static class PokerPredictions
    {
        private static readonly PropertyInfo estimatedValue = typeof(GameBase).GetProperty("EstimatedValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal sealed class Plan
        {
            internal int KeepMask;
            internal int KeptCount;
            internal int HandRank;
            internal List<CardData> FinalHand;
        }

        private sealed class DeckCopy
        {
            private readonly GameBase game;
            private List<CardData> cards;

            internal DeckCopy(GameBase game, IEnumerable<CardData> cards)
            {
                this.game = game;
                this.cards = new List<CardData>(cards);
            }

            internal CardData Draw()
            {
                if (cards.Count == 0) cards = BuildDeck(game);
                if (cards.Count == 0) throw new InvalidOperationException("扑克没有有效牌堆。");
                CardData card = cards[0];
                cards.RemoveAt(0);
                return card;
            }
        }

        internal static bool TryDescribe(GameBase game, Random rng, out string text)
        {
            text = null;
            if (!(game is Poker) || rng == null) return false;
            string phase = Read.Value(game, "gameState").ToString();
            if (game.isPlaying && phase == "Dealing")
            {
                text = "正在发起手牌；发牌完成后显示当前选择及最佳换牌方案。";
                return true;
            }
            if (phase == "Replacing")
            {
                text = "已确认选牌，正在换牌；下一局预览将在本局结束后更新。";
                return true;
            }
            if (phase == "Finished")
            {
                text = "本局正在结算；下一局预览会在机器重置后更新。";
                return true;
            }
            bool preBet = !game.isPlaying && phase == "Waiting";
            bool playerTurn = game.isPlaying && phase == "PlayerTurn";
            if (!preBet && !playerTurn)
            {
                text = "等待可操作的扑克状态。";
                return true;
            }

            var hand = CopyHand(game, "playerHand");
            if ((preBet && hand.Count != 0) || (playerTurn && hand.Count != 5))
            {
                text = "等待手牌与机器状态同步。";
                return true;
            }
            var storedDeck = Read.Field<List<CardData>>(game, "deck");
            if (storedDeck == null) throw new InvalidOperationException("扑克牌堆尚未初始化。");
            bool initialized = Read.Field<bool>(game, "deckInitialized");
            if (playerTurn && !initialized)
            {
                text = "等待牌堆与机器状态同步。";
                return true;
            }
            bool initialize = preBet && !initialized;
            bool stateDependentShuffle = initialize || storedDeck.Count < (preBet ? 10 : 5);
            var deck = new DeckCopy(game, initialize ? BuildDeck(game) : storedDeck);
            if (preBet) for (int i = 0; i < 5; i++) hand.Add(deck.Draw());

            // Every legal retention uses a prefix of this same replacement queue.
            // Build it once so every branch starts at exactly the same deck state.
            var replacements = new List<CardData>();
            for (int i = 0; i < 5; i++) replacements.Add(deck.Draw());
            List<Plan> plans = EnumeratePlans(hand, replacements);
            double houseValue = (double)estimatedValue.GetValue(game, null);
            if (double.IsNaN(houseValue) || double.IsInfinity(houseValue) || houseValue < 0)
                throw new InvalidOperationException("扑克基础返还参数无效。");
            Plan best = BestPlan(plans, houseValue);
            var result = new StringBuilder();
            result.Append(preBet ? "下局起手：" : "当前手牌：").Append(IndexedCards(hand)).Append("。");
            if (playerTurn)
            {
                var keep = CopyHand(game, "cardsToKeep");
                int selectedMask = 0;
                for (int i = 0; i < hand.Count; i++) if (keep.Contains(hand[i])) selectedMask |= 1 << i;
                var selected = MakePlan(hand, replacements, selectedMask);
                result.Append("\n当前保留：").Append(Selection(hand, selectedMask, true)).Append("。");
                result.Append("\n若现在确认：").Append(Cards(selected.FinalHand)).Append(" → ")
                    .Append(HandName(selected.HandRank)).Append("；基础返还 x")
                    .Append(Format(PayoutFactor(selected.HandRank) * houseValue)).Append("。");
            }
            result.Append("\n最佳方案（共 ").Append(plans.Count).Append(" 种合法保留选择）：")
                .Append(HandName(best.HandRank)).Append("。");
            result.Append("\n应保留：").Append(Selection(hand, best.KeepMask, true))
                .Append("；换掉：").Append(Selection(hand, best.KeepMask, false)).Append("。");
            result.Append("\n按此确认后的牌：").Append(Cards(best.FinalHand)).Append("。");
            double factor = PayoutFactor(best.HandRank) * houseValue;
            result.Append("\n最佳基础返还 x").Append(Format(factor)).Append("（含本金，未计玩家增益）；")
                .Append(factor > 1 ? "高于本金" : factor == 1 ? "等于本金" : "低于本金").Append("。");
            if (HasIdenticalCards(hand)) result.Append("\n完全相同的花色点数会一起保留；选其中一张即可，重复点击会取消该牌值。");
            result.Append("\n需要实际选牌并确认；一对虽被游戏判赢，基础返还仍可能低于本金。");
            if (stateDependentShuffle)
                result.Append("\n本次预览含初始化或空牌堆重洗；牌序以当前种子、天数和机器位置为准。");
            text = result.ToString();
            return true;
        }

        private static List<CardData> BuildDeck(GameBase game)
        {
            int number = Read.Field<int>(game, "numberOfDecks");
            if (number <= 0 || number > 64) throw new InvalidOperationException("扑克牌堆数量超出支持范围。");
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

        private static List<CardData> CopyHand(GameBase game, string name)
        {
            return new List<CardData>((IEnumerable<CardData>)Read.Value(game, name));
        }

        internal static List<Plan> EnumeratePlans(IList<CardData> hand, IList<CardData> replacements)
        {
            if (hand.Count != 5) throw new ArgumentException("扑克需要五张手牌。");
            var plans = new List<Plan>();
            for (int mask = 0; mask < 32; mask++)
            {
                bool legal = true;
                for (int i = 0; i < 5 && legal; i++)
                    for (int j = i + 1; j < 5; j++)
                        if (hand[i].Equals(hand[j]) && ((mask >> i) & 1) != ((mask >> j) & 1)) { legal = false; break; }
                if (legal) plans.Add(MakePlan(hand, replacements, mask));
            }
            return plans;
        }

        internal static Plan MakePlan(IList<CardData> hand, IList<CardData> replacements, int keepMask)
        {
            if (hand.Count != 5 || keepMask < 0 || keepMask > 31) throw new ArgumentException("扑克保留方案无效。");
            var final = new List<CardData>();
            for (int i = 0; i < hand.Count; i++) if ((keepMask & (1 << i)) != 0) final.Add(hand[i]);
            int kept = final.Count;
            if (replacements.Count < 5 - kept) throw new ArgumentException("扑克补牌队列不足。");
            for (int i = 0; i < 5 - kept; i++) final.Add(replacements[i]);
            return new Plan { KeepMask = keepMask, KeptCount = kept, FinalHand = final, HandRank = Evaluate(final) };
        }

        internal static Plan BestPlan(IList<Plan> plans, double houseValue)
        {
            Plan best = null;
            foreach (Plan plan in plans)
            {
                double value = PayoutFactor(plan.HandRank) * houseValue;
                double bestValue = best == null ? -1 : PayoutFactor(best.HandRank) * houseValue;
                if (best == null || value > bestValue || (value == bestValue && plan.KeptCount > best.KeptCount)) best = plan;
            }
            if (best == null) throw new ArgumentException("扑克没有合法保留方案。");
            return best;
        }

        // Match this game's category precedence and exact group sizes. In multi-
        // deck play it has no five-of-a-kind category; do not normalize that case.
        internal static int Evaluate(IList<CardData> hand)
        {
            if (hand.Count != 5) return 0;
            var ranks = new List<int>();
            var counts = new Dictionary<int, int>();
            bool flush = true;
            foreach (CardData card in hand)
            {
                int rank = (int)card.Rank;
                ranks.Add(rank);
                int count;
                counts.TryGetValue(rank, out count);
                counts[rank] = count + 1;
                if (card.Suit != hand[0].Suit) flush = false;
            }
            ranks.Sort();
            bool straight = Consecutive(ranks);
            if (!straight && ranks[0] == 1)
            {
                var aceHigh = new List<int>(ranks);
                aceHigh[0] = 14;
                aceHigh.Sort();
                straight = Consecutive(aceHigh);
            }
            int pairs = 0;
            bool three = false, four = false;
            foreach (int count in counts.Values)
            {
                if (count == 2) pairs++;
                if (count == 3) three = true;
                if (count == 4) four = true;
            }
            if (straight && flush) return 8;
            if (four) return 7;
            if (counts.Count == 2 && three && pairs == 1) return 6;
            if (flush) return 5;
            if (straight) return 4;
            if (three) return 3;
            if (pairs == 2) return 2;
            if (pairs >= 1) return 1;
            return 0;
        }

        private static bool Consecutive(IList<int> ranks)
        {
            for (int i = 1; i < ranks.Count; i++) if (ranks[i] != ranks[i - 1] + 1) return false;
            return true;
        }

        internal static int PayoutFactor(int rank)
        {
            switch (rank)
            {
                case 8: return 50;
                case 7: return 25;
                case 6: return 9;
                case 5: return 6;
                case 4: return 4;
                case 3: return 3;
                case 2: return 2;
                case 1: return 1;
                default: return 0; // High card is gated to zero before payout.
            }
        }

        private static string HandName(int rank)
        {
            switch (rank)
            {
                case 8: return "同花顺";
                case 7: return "四条";
                case 6: return "葫芦";
                case 5: return "同花";
                case 4: return "顺子";
                case 3: return "三条";
                case 2: return "两对";
                case 1: return "一对";
                default: return "高牌（未中奖）";
            }
        }

        private static bool HasIdenticalCards(IList<CardData> hand)
        {
            for (int i = 0; i < hand.Count; i++) for (int j = i + 1; j < hand.Count; j++) if (hand[i].Equals(hand[j])) return true;
            return false;
        }

        private static string Selection(IList<CardData> hand, int mask, bool retained)
        {
            var items = new List<string>();
            for (int i = 0; i < hand.Count; i++)
                if (((mask & (1 << i)) != 0) == retained) items.Add("第" + (i + 1) + "张 " + CardName(hand[i]));
            return items.Count == 0 ? "无" : string.Join("、", items.ToArray());
        }

        private static string IndexedCards(IList<CardData> hand)
        {
            var cards = new List<string>();
            for (int i = 0; i < hand.Count; i++) cards.Add((i + 1) + ":" + CardName(hand[i]));
            return string.Join("、", cards.ToArray());
        }

        private static string Cards(IList<CardData> hand)
        {
            var cards = new List<string>();
            foreach (CardData card in hand) cards.Add(CardName(card));
            return string.Join("、", cards.ToArray());
        }

        private static string CardName(CardData card)
        {
            string rank = card.Rank == Rank.Ace ? "A" : card.Rank == Rank.Jack ? "J" : card.Rank == Rank.Queen ? "Q" : card.Rank == Rank.King ? "K" : ((int)card.Rank).ToString(CultureInfo.InvariantCulture);
            string suit = card.Suit == Suit.Hearts ? "♥" : card.Suit == Suit.Diamonds ? "♦" : card.Suit == Suit.Clubs ? "♣" : "♠";
            return rank + suit;
        }

        private static string Format(double value) { return value.ToString("0.####", CultureInfo.InvariantCulture); }
    }
}
