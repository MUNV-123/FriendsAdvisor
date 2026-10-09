using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FriendsAdvisor;

public class GameBase
{
    public bool isPlaying;
    public double HouseValue = 0.9;
    protected double EstimatedValue { get { return HouseValue; } }
}
public enum Phase { Waiting, Dealing, PlayerTurn, Replacing, Finished }
public class Poker : GameBase
{
    public List<CardData> deck = new List<CardData>();
    public List<CardData> playerHand = new List<CardData>();
    public List<CardData> cardsToKeep = new List<CardData>();
    public bool deckInitialized = true;
    public int numberOfDecks = 1;
    public Phase gameState;
}
public enum Suit { Hearts, Diamonds, Clubs, Spades }
public enum Rank { Ace = 1, Two, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King }
public struct CardData
{
    public Suit Suit;
    public Rank Rank;
    public CardData(Suit suit, Rank rank) { Suit = suit; Rank = rank; }
}
namespace FriendsAdvisor
{
    internal static class Read
    {
        public static int Calls;
        public static readonly List<int> Contexts = new List<int>();
        public static object Value(object target, string name) { return target.GetType().GetField(name).GetValue(target); }
        public static T Field<T>(object target, string name) { return (T)Value(target, name); }
        public static Random PeekRandom(GameBase game, int context = 0)
        {
            Calls++;
            Contexts.Add(context);
            return new Random(7654321 ^ context);
        }
    }
}
public class NoDrawRandom : Random
{
    public override double NextDouble() { throw new Exception("supplied RNG was unnecessarily consumed"); }
    public override int Next() { throw new Exception("supplied RNG was unnecessarily consumed"); }
    public override int Next(int maxValue) { throw new Exception("supplied RNG was unnecessarily consumed"); }
    public override int Next(int minValue, int maxValue) { throw new Exception("supplied RNG was unnecessarily consumed"); }
}
public static class Harness
{
    private static int checks;
#if GAME_SOURCE_ORACLE
    private static readonly ReferenceEvaluator evaluator = new ReferenceEvaluator();
#endif
    private static CardData C(int rank, int suit = 0) { return new CardData((Suit)suit, (Rank)rank); }
    private static List<CardData> H(params int[] ranks)
    {
        var result = new List<CardData>();
        for (int i = 0; i < ranks.Length; i++) result.Add(C(ranks[i], i % 4));
        return result;
    }
    private static void Check(bool yes, string label) { checks++; if (!yes) throw new Exception(label); }
    private static bool Same(IList<CardData> a, IList<CardData> b) { return a.SequenceEqual(b); }
    private static string Cards(IList<CardData> cards)
    {
        return string.Join(",", cards.Select(c => (int)c.Suit + "/" + (int)c.Rank).ToArray());
    }
    private static string State(Poker game)
    {
        return Cards(game.deck) + ";" + Cards(game.playerHand) + ";" + Cards(game.cardsToKeep)
            + ";" + game.isPlaying + ";" + game.gameState + ";" + game.deckInitialized + ";" + game.numberOfDecks + ";" + game.HouseValue;
    }
    private static string Describe(Poker game)
    {
        string state = State(game), text;
        Check(PokerPredictions.TryDescribe(game, new NoDrawRandom(), out text), "recognizes Poker");
        Check(State(game) == state, "original deck, hand, selections, phase and configuration remain unchanged");
        return text;
    }
    private static List<CardData> BuildActualDeck(int count)
    {
        var deck = new List<CardData>();
        for (int i = 0; i < count; i++)
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                    if ((int)rank != 0) deck.Add(new CardData(suit, rank));
        var random = new Random(7654321 ^ (deck.Count * 10000));
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            CardData saved = deck[i]; deck[i] = deck[j]; deck[j] = saved;
        }
        return deck;
    }
    private static List<CardData> ReplaceLikeGame(IList<CardData> hand, IList<CardData> replacements, int mask)
    {
        var keptValues = new List<CardData>();
        for (int i = 0; i < hand.Count; i++) if ((mask & (1 << i)) != 0) keptValues.Add(hand[i]);
        var result = new List<CardData>(hand);
        for (int i = result.Count - 1; i >= 0; i--) if (!keptValues.Contains(result[i])) result.RemoveAt(i);
        int needed = 5 - result.Count;
        for (int i = 0; i < needed; i++) result.Add(replacements[i]);
        return result;
    }
    private static int Oracle(IList<CardData> hand)
    {
#if GAME_SOURCE_ORACLE
        return evaluator.Eval(new List<CardData>(hand));
#else
        if (hand.Count != 5) return 0;
        var ranks = hand.Select(c => (int)c.Rank).Distinct().OrderBy(x => x).ToArray();
        bool flush = hand.All(c => c.Suit == hand[0].Suit);
        bool straight = ranks.Length == 5 &&
            (ranks[4] - ranks[0] == 4 || ranks.SequenceEqual(new[] { 1, 10, 11, 12, 13 }));
        var groups = hand.GroupBy(c => c.Rank).Select(g => g.Count()).OrderByDescending(x => x).ToArray();
        if (straight && flush) return 8;
        if (groups.Contains(4)) return 7;
        if (groups.SequenceEqual(new[] { 3, 2 })) return 6;
        if (flush) return 5;
        if (straight) return 4;
        if (groups.Contains(3)) return 3;
        if (groups.Count(x => x == 2) == 2) return 2;
        if (groups.Contains(2)) return 1;
        return 0;
#endif
    }
    private static void EvaluatorTests()
    {
        int[][] ranks = { new[] { 1, 2, 3, 4, 5 }, new[] { 10, 11, 12, 13, 1 }, new[] { 8, 8, 8, 8, 2 }, new[] { 6, 6, 6, 3, 3 }, new[] { 2, 5, 7, 9, 13 }, new[] { 9, 10, 11, 12, 13 }, new[] { 4, 4, 4, 8, 2 }, new[] { 2, 2, 13, 13, 3 }, new[] { 3, 3, 4, 8, 13 }, new[] { 2, 4, 6, 8, 11 } };
        bool[] flush = { true, true, false, false, true, false, false, false, false, false };
        int[] expected = { 8, 8, 7, 6, 5, 4, 3, 2, 1, 0 };
        for (int i = 0; i < ranks.Length; i++)
        {
            var hand = H(ranks[i]);
            if (flush[i]) for (int j = 0; j < hand.Count; j++) hand[j] = C(ranks[i][j]);
            Check(PokerPredictions.Evaluate(hand) == expected[i], "known category including Ace-low and Ace-high straight " + i);
        }
        Check(PokerPredictions.Evaluate(H(13, 13, 13, 13, 13)) == 0, "actual multi-deck five-kind anomaly is high card");
        Check(PokerPredictions.Evaluate(new List<CardData> { C(13), C(13), C(13), C(13), C(13) }) == 5, "five identical values is flush rather than four-kind");
        Check(PokerPredictions.Evaluate(H(1, 2, 3, 4)) == 0, "incomplete hand never scored as finished");
        int[] pays = { 0, 1, 2, 3, 4, 6, 9, 25, 50 };
        for (int i = 0; i < pays.Length; i++)
        {
            Check(PokerPredictions.PayoutFactor(i) == pays[i], "actual rank payout " + i);
#if GAME_SOURCE_ORACLE
            Check(evaluator.Pay(i) == PokerPredictions.PayoutFactor(i), "extracted game's payout gate and factor " + i);
#endif
        }
        // Every rank multiset is tested with both flush and nonflush suits. The
        // oracle is independent; optional source extraction uses the actual game.
        for (int a = 1; a <= 13; a++) for (int b = a; b <= 13; b++) for (int c = b; c <= 13; c++)
            for (int d = c; d <= 13; d++) for (int e = d; e <= 13; e++)
            {
                var hand = H(a, b, c, d, e);
                Check(PokerPredictions.Evaluate(hand) == Oracle(hand), "nonflush rank multiset parity");
                hand = new List<CardData> { C(a), C(b), C(c), C(d), C(e) };
                Check(PokerPredictions.Evaluate(hand) == Oracle(hand), "flush rank multiset parity");
            }
    }
    private static void BranchTests()
    {
        var random = new Random(271828);
        for (int sample = 0; sample < 200; sample++)
        {
            var hand = new List<CardData>();
            var stream = new List<CardData>();
            for (int i = 0; i < 5; i++)
            {
                hand.Add(C(random.Next(1, 14), random.Next(0, 4)));
                stream.Add(C(random.Next(1, 14), random.Next(0, 4)));
            }
            string state = Cards(hand) + ";" + Cards(stream);
            var plans = PokerPredictions.EnumeratePlans(hand, stream);
            Check(plans.Count == (1 << hand.Distinct().Count()), "one branch per distinct CardData value subset");
            foreach (var plan in plans)
            {
                var expected = ReplaceLikeGame(hand, stream, plan.KeepMask);
                Check(Same(plan.FinalHand, expected), "kept order followed by draw queue matches backward removal");
                Check(plan.HandRank == Oracle(expected), "every reachable branch final score matches oracle");
                Check(plan.KeptCount == 5 - System.Numerics.BitOperations.PopCount((uint)(31 ^ plan.KeepMask)), "retained count matches actual positions");
            }
            var best = PokerPredictions.BestPlan(plans, 0.9);
            Check(plans.All(p => PokerPredictions.PayoutFactor(p.HandRank) <= PokerPredictions.PayoutFactor(best.HandRank)), "best branch maximizes return over all legal choices");
            var zero = PokerPredictions.BestPlan(plans, 0);
            Check(zero.KeepMask == 31, "equal zero returns prefer no unnecessary replacement");
            Check(Cards(hand) + ";" + Cards(stream) == state, "all branch analysis leaves source lists unchanged");
        }
        var duplicateHand = new List<CardData> { C(8), C(8), C(2, 2), C(4, 1), C(7, 3) };
        var duplicatePlans = PokerPredictions.EnumeratePlans(duplicateHand, H(8, 8, 11, 8, 10));
        Check(duplicatePlans.Count == 16, "two identical cards reduce possible keeps from 32 to 16");
        Check(duplicatePlans.All(p => (p.KeepMask & 1) == ((p.KeepMask >> 1) & 1)), "cannot retain only one of identical CardData values");
        var allSame = new List<CardData> { C(8), C(8), C(8), C(8), C(8) };
        Check(PokerPredictions.EnumeratePlans(allSame, H(2, 3, 4, 5, 6)).Count == 2, "five identical cards have only keep all and keep none choices");
    }
    private static void StateTests()
    {
        string text;
        Check(!PokerPredictions.TryDescribe(new GameBase(), new NoDrawRandom(), out text), "other machines ignored");
        Check(!PokerPredictions.TryDescribe(new Poker(), null, out text), "requires caller-validated generator");
        var game = new Poker { deck = new List<CardData> { C(2), C(3), C(4), C(5), C(9), C(6), C(7, 1), C(8, 2), C(10, 3), C(11, 1) } };
        int calls = Read.Calls;
        text = Describe(game);
        Check(text.Contains("下局起手：1:2♥、2:3♥、3:4♥、4:5♥、5:9♥"), "prebet reads next five persistent deck cards");
        Check(text.Contains("应保留：第1张 2♥、第2张 3♥、第3张 4♥、第4张 5♥；换掉：第5张 9♥"), "known optimal replacement retains four low hearts");
        Check(text.Contains("按此确认后的牌：2♥、3♥、4♥、5♥、6♥"), "replacements append after retained cards");
        Check(text.Contains("最佳基础返还 x45") && text.Contains("高于本金"), "actual EstimatedValue is included in optimal return");
        Check(Read.Calls == calls, "full existing deck needs no new shuffle");
        game = new Poker { isPlaying = true, gameState = Phase.PlayerTurn, playerHand = H(3, 3, 6, 8, 12), cardsToKeep = H(3, 3, 6, 8, 12), deck = H(2, 4, 7, 9, 11) };
        text = Describe(game);
        Check(text.Contains("若现在确认：3♥、3♦、6♣、8♠、Q♥ → 一对；基础返还 x0.9"), "current selection can be game Win while below stake");
        Check(text.Contains("一对虽被游戏判赢，基础返还仍可能低于本金"), "pair is not called profitable solely because tagged Win");
        game.cardsToKeep.Clear();
        text = Describe(game);
        Check(text.Contains("当前保留：无") && text.Contains("若现在确认：2♥、4♦、7♣、9♠、J♥"), "selection refresh predicts discard all without using prior selection");
        foreach (Phase phase in new[] { Phase.Dealing, Phase.Replacing, Phase.Finished })
        {
            game.gameState = phase; game.deck.Clear(); calls = Read.Calls;
            text = Describe(game);
            Check(!text.Contains("下局起手") && !text.Contains("最佳方案") && !text.Contains("若现在确认"), "animation and settlement gate " + phase);
            Check(Read.Calls == calls, "animation/settlement does not request shuffle " + phase);
        }
        game = new Poker { gameState = Phase.Finished, isPlaying = false };
        Check(Describe(game).Contains("机器重置"), "phase Finished still blocks preview if playing flag already false");
        game = new Poker { gameState = Phase.Waiting, isPlaying = true };
        Check(Describe(game).Contains("等待可操作"), "playing/Waiting mismatch is rejected");
        game = new Poker { gameState = Phase.PlayerTurn, isPlaying = true, playerHand = H(3, 4, 5, 6) };
        Check(Describe(game).Contains("状态同步"), "partial hand is not mistaken for complete player turn");
        game.playerHand.Add(C(7)); game.deckInitialized = false;
        Check(Describe(game).Contains("牌堆与机器状态同步"), "uninitialized active deck rejected");
        game = new Poker { deck = H(1, 2, 3, 4, 5, 6, 7, 8, 9, 10), playerHand = H(2) };
        Check(Describe(game).Contains("状态同步"), "stale waiting hand is not silently replayed");

        for (int count = 1; count <= 2; count++)
        {
            var fresh = BuildActualDeck(count);
            game = new Poker { deckInitialized = false, numberOfDecks = count, deck = H(13, 13) };
            calls = Read.Calls;
            text = Describe(game);
            Check(Read.Calls == calls + 1, "first preview initializes independent deck once");
            Check(Read.Contexts[Read.Contexts.Count - 1] == count * 520000, "shuffle context includes full rebuilt multi-deck count");
            Check(text.Contains("1:" + CardName(fresh[0])) && text.Contains("5:" + CardName(fresh[4])), "actual enum order and Fisher-Yates first deal match");
            Check(text == Describe(game), "repeated preview is idempotent, including initial shuffle");
            for (int remaining = 0; remaining < 10; remaining++)
            {
                var tail = new List<CardData>();
                for (int i = 0; i < remaining; i++) tail.Add(C((i % 13) + 1, i % 4));
                var actual = new List<CardData>(tail);
                while (actual.Count < 10) actual.AddRange(fresh);
                game = new Poker { deck = tail, numberOfDecks = count };
                calls = Read.Calls;
                text = Describe(game);
                Check(Read.Calls == calls + 1, "refill can occur during initial deal or replacement queue " + remaining);
                Check(Read.Contexts[Read.Contexts.Count - 1] == count * 520000, "refill uses constructed deck size, not exhausted remaining size");
                for (int i = 0; i < 5; i++) Check(text.Contains((i + 1) + ":" + CardName(actual[i])), "refill initial queue parity at slot " + i);
                var expectedHand = actual.GetRange(0, 5);
                var expectedQueue = actual.GetRange(5, 5);
                var best = PokerPredictions.BestPlan(PokerPredictions.EnumeratePlans(expectedHand, expectedQueue), 0.9);
                string expectedFinal = string.Join("、", best.FinalHand.Select(CardName).ToArray());
                Check(text.Contains("按此确认后的牌：" + expectedFinal), "refill best final hand parity");
            }
        }
        game = new Poker { isPlaying = true, gameState = Phase.PlayerTurn, playerHand = new List<CardData> { C(8), C(8), C(2, 2), C(4, 1), C(7, 3) }, cardsToKeep = new List<CardData> { C(8) }, deck = H(8, 8, 11, 8, 10) };
        text = Describe(game);
        Check(text.Contains("共 16 种") && text.Contains("当前保留：第1张 8♥、第2张 8♥"), "active duplicate-value selection retains both instances");
        Check(text.Contains("若现在确认：8♥、8♥、8♥、8♦、J♣ → 四条"), "duplicate keep current branch uses only three replacement draws");
        Check(text.Contains("选其中一张即可"), "duplicate-value interaction caveat shown");
    }
    private static string CardName(CardData card)
    {
        string rank = card.Rank == Rank.Ace ? "A" : card.Rank == Rank.Jack ? "J" : card.Rank == Rank.Queen ? "Q" : card.Rank == Rank.King ? "K" : ((int)card.Rank).ToString();
        return rank + new[] { "♥", "♦", "♣", "♠" }[(int)card.Suit];
    }
    public static void Main()
    {
        try
        {
            EvaluatorTests(); BranchTests(); StateTests();
#if GAME_SOURCE_ORACLE
            Console.WriteLine("PASS: " + checks + " assertions; includes actual game-source evaluator/payout parity.");
#else
            Console.WriteLine("PASS: " + checks + " assertions; independent category oracle, legal keeps, best return, immutable state, lifecycle gates and mid-deal/mid-replacement multi-deck refill.");
#endif
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error.GetType().Name + ": " + error.Message); Environment.Exit(1); }
    }
}
