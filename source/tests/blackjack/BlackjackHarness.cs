using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FriendsAdvisor;

public class GameBase { public bool isPlaying; }
public enum Phase { Waiting, PlayerTurn, DealerTurn, Finished }
public class Blackjack : GameBase
{
    public List<CardData> deck = new List<CardData>();
    public List<CardData> playerHand = new List<CardData>();
    public List<CardData> dealerHand = new List<CardData>();
    public List<CardData> splitHand = new List<CardData>();
    public bool deckInitialized = true, hasSplitThisRound;
    public int numberOfDecks = 1, activeHandIndex;
    public bool[] handCompleted = new bool[2], handDoubled = new bool[2];
    public Phase gameState;
}
public enum Suit { Hearts, Diamonds, Clubs, Spades }
public enum Rank { Ace=1, Two, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King }
public struct CardData
{
    public Suit Suit; public Rank Rank;
    public CardData(Suit suit, Rank rank) { Suit=suit; Rank=rank; }
    public int GetBlackjackValue() { return Rank==Rank.Ace ? 11 : (int)Rank>=11 ? 10 : (int)Rank; }
}
namespace FriendsAdvisor
{
    internal static class Read
    {
        public static int Calls;
        public static List<int> Contexts = new List<int>();
        public static object Value(object target,string name) { return target.GetType().GetField(name).GetValue(target); }
        public static T Field<T>(object target,string name) { return (T)Value(target,name); }
        public static Random PeekRandom(GameBase game,int context=0) { Calls++; Contexts.Add(context); return new Random(7654321^context); }
    }
}
public class NoDrawRandom : Random
{
    public override double NextDouble() { throw new Exception("supplied RNG was unnecessarily consumed"); }
}
public class Harness
{
    private static int checks;
    private static CardData C(int rank) { return new CardData(Suit.Hearts,(Rank)rank); }
    private static List<CardData> Cards(params int[] ranks) { var result=new List<CardData>(); foreach(int rank in ranks)result.Add(C(rank)); return result; }
    private static void Check(bool yes,string message) { checks++; if(!yes)throw new Exception(message); }
    private static string Snapshot(Blackjack g)
    {
        var result=new StringBuilder();
        foreach(var list in new[] {g.deck,g.playerHand,g.dealerHand,g.splitHand})
        {
            foreach(var c in list)result.Append((int)c.Suit).Append('/').Append((int)c.Rank).Append(',');
            result.Append(';');
        }
        result.Append(g.isPlaying).Append(g.deckInitialized).Append(g.hasSplitThisRound).Append(g.activeHandIndex).Append(g.numberOfDecks).Append(g.gameState);
        foreach(bool b in g.handCompleted)result.Append(b);
        foreach(bool b in g.handDoubled)result.Append(b);
        return result.ToString();
    }
    private static string Describe(Blackjack g)
    {
        string before=Snapshot(g), text;
        Check(BlackjackPredictions.TryDescribe(g,new NoDrawRandom(),out text),"recognized Blackjack");
        Check(Snapshot(g)==before,"all original lists, flags, indexes and bool arrays remain unchanged");
        return text;
    }
    public static void Main()
    {
        try { Run(); }
        catch(Exception error) { Console.WriteLine("FAIL: "+error.GetType().Name+": "+error.Message); Environment.Exit(1); }
    }
    private static void Run()
    {
        Check(BlackjackPredictions.HandValue(Cards(1,1,9))==21,"two aces reduced to prevent bust");
        Check(BlackjackPredictions.HandValue(Cards(1,1,13))==12,"both aces reduce around facecard");
        Check(BlackjackPredictions.HandValue(Cards(1,5,1))==17,"multiace soft17");
        Check(BlackjackPredictions.HandValue(Cards(10,6,13))==26,"hard bust");

        var game=new Blackjack {deck=Cards(10,10,6,6,5,2,3,4,13,1,7,8)};
        string text=Describe(game);
        Check(text.Contains("你 10♥、6♥（16点）；庄 10♥、6♥（16点）"),"P,D,P,D initial deal");
        Check(text.Contains("起手后你若要牌：5♥ → 21点"),"player next card after initial deal");
        Check(text.Contains("若你起手后直接停牌，庄家补牌：5♥ → 21点"),"dealer ifstand uses SAME next card");
        Check(text.Contains("在该条件下：你 负"),"conditional known outcome");
        Check(Read.Calls==0,"existing deck consumes no shuffle RNG");

        int[] originalDeal={10,10,6,6,5,2,3,4,13,1,7,8};
        for(int prefix=0;prefix<4;prefix++)
        {
            game=new Blackjack {isPlaying=true,gameState=Phase.Waiting};
            for(int index=0;index<prefix;index++)
                if(index%2==0)game.playerHand.Add(C(originalDeal[index]));else game.dealerHand.Add(C(originalDeal[index]));
            for(int index=prefix;index<originalDeal.Length;index++)game.deck.Add(C(originalDeal[index]));
            text=Describe(game);
            Check(text.Contains("你 10♥、6♥（16点）；庄 10♥、6♥（16点）"),"valid partialdeal prefix "+prefix+" reconstructs known finalhands");
            Check(text.Contains("起手后你若要牌：5♥ → 21点"),"valid prefix "+prefix+" leaves next playerdraw correctly aligned");
        }

        game=new Blackjack {deck=Cards(1,1,13,13,5,2,3,4,13,1,7,8)};
        text=Describe(game);
        Check(text.Contains("起手为Blackjack"),"player21 direct settlement");
        Check(!text.Contains("庄家补牌：") && !text.Contains("共享牌堆"),"natural21 even dealer21 no future draws");

        game=new Blackjack {isPlaying=true,gameState=Phase.Waiting,playerHand=Cards(10),dealerHand=Cards(10),deck=Cards(6,6,5,2,3,4,13,1,7,8)};
        text=Describe(game);
        Check(text.Contains("接下来：你 6♥ → 庄 6♥"),"middeal only draws pending initial prefix");
        Check(text.Contains("你 10♥、6♥（16点）"),"middeal does not replay existing cards");
        Check(game.deck.Count==10 && game.playerHand.Count==1 && game.dealerHand.Count==1,"middeal original cards remain intact");
        game.playerHand.Add(C(6)); game.dealerHand.Add(C(6));
        text=Describe(game);
        Check(text.Contains("等待牌局状态同步"),"waiting prefix4 is not forecast as newdeal");

        game=new Blackjack {isPlaying=true,gameState=Phase.PlayerTurn,playerHand=Cards(10,6),dealerHand=Cards(1,6),deck=Cards(13,2,3,4,5,6,7,8)};
        text=Describe(game);
        Check(text.Contains("下次要牌（如可加倍，也是这张）：K♥ → 26点，爆牌"),"next hit may bust and double takes same card");
        Check(text.Contains("庄家补牌：无需补牌 → 17点"),"dealer stands soft17");
        Check(text.Contains("庄家完整牌（含暗牌）：A♥、6♥"),"dealer hidden card displayed");

        game=new Blackjack {isPlaying=true,gameState=Phase.PlayerTurn,playerHand=Cards(8,8),dealerHand=Cards(10,6),deck=Cards(3,13,5,4,13,1,7,8)};
        text=Describe(game);
        Check(text.Contains("若分牌（需足够余额）：第一手补 3♥ → 11点；第二手补 K♥ → 18点"),"same rank split assigns first drawhand0 then drawhand1");
        Check(text.Contains("是否分牌由你决定"),"split preview does not choose action automatically");
        Check(game.playerHand.Count==2 && game.splitHand.Count==0 && !game.hasSplitThisRound && game.deck[0].Rank==Rank.Three,"split preview changes no original state");
        game.playerHand=Cards(11,12);
        text=Describe(game);
        Check(!text.Contains("若分牌（需足够余额）"),"Jack Queen equal value are NOT same rank");
        game=new Blackjack {deck=Cards(8,10,8,6,3,13,5,4,13,1,7,8)};
        text=Describe(game);
        Check(text.Contains("第一手补 3♥ → 11点；第二手补 K♥ → 18点"),"prebet split preview uses cards after fourinitialdeal");

        game=new Blackjack {isPlaying=true,gameState=Phase.DealerTurn,playerHand=Cards(10,8),dealerHand=Cards(10,6),deck=Cards(2,5,3,4,13,1,7,8)};
        text=Describe(game);
        Check(text.Contains("庄家接下来补牌：2♥ → 18点"),"ongoing dealer future comes from current deck");
        Check(text.Contains("在该条件下：你 平"),"dealer18 equals player18");
        Check(!text.Contains("下次要牌"),"dealer phase doesn't claim player can hit");
        game.dealerHand.Add(C(2)); game.deck.RemoveAt(0);
        text=Describe(game);
        Check(text.Contains("无需补牌 → 18点"),"middealer forecast does not redraw dealt cards");

        game=new Blackjack {isPlaying=true,gameState=Phase.PlayerTurn,hasSplitThisRound=true,playerHand=Cards(8,3),splitHand=Cards(8,13),dealerHand=Cards(10,6),deck=Cards(5,2,3,4,13,1,7,8)};
        text=Describe(game);
        Check(text.Contains("当前操作：第 1 手"),"split current hand0");
        Check(text.Contains("若两手都停牌、不再要牌，庄家补牌：5♥ → 21点"),"split dealer hypothetical requires both stop");
        Check(text.Contains("第1手 负；第2手 负"),"split forecasts perhand condition, no payout");
        Check(!text.Contains("返还") && !text.Contains("倍率"),"split forecast cannot promise ordinary payout");
        game.activeHandIndex=1; game.handCompleted[0]=true; game.deck[0]=C(3);
        text=Describe(game);
        Check(text.Contains("当前操作：第 2 手"),"split current hand1");
        Check(text.Contains("3♥ → 21点"),"split next draw targets active secondhand");
        Check(text.Contains("第一手已结束"),"split previoushand completion");

        game.gameState=Phase.Finished;
        int before=Read.Calls;
        text=Describe(game);
        Check(!text.Contains("共享牌堆") && !text.Contains("下次要牌") && text.Contains("本局正在结算"),"finished state excludes nextturn predictions");
        Check(Read.Calls==before,"finished state needs no RNG");

        game=new Blackjack {isPlaying=true,gameState=Phase.PlayerTurn,playerHand=Cards(10,6),dealerHand=Cards(10,6),deck=new List<CardData>()};
        before=Read.Calls;
        text=Describe(game);
        Check(Read.Calls>before && game.deck.Count==0,"empty-deck refill only constructs copies");
        foreach(int context in Read.Contexts)Check(context==520000,"shuffle context uses rebuilt deck52 not previous0");

        game=new Blackjack {deckInitialized=false,deck=Cards(9,9)};
        before=Read.Calls;
        text=Describe(game);
        Check(Read.Calls>before && !game.deckInitialized && game.deck.Count==2,"initial shuffle doesn't mutate game flags/deck");
        Console.WriteLine("PASS: "+checks+" assertions; dealing prefix, known next cards, soft17, split conditions, phase gates, immutable deck/hands, refill context.");
    }
}
