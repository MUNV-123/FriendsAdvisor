using System;
using System.Collections.Generic;
using System.Reflection;
using FriendsAdvisor;

public class GameBase
{
    public bool isPlaying;
    protected double EstimatedValue { get { return 0.9; } }
}
public class Baccarat : GameBase
{
    public List<CardData> deck = new List<CardData>();
    public bool deckInitialized;
    public int numberOfDecks = 1;
    public BaccaratBetType currentBetType;
}
public class HiLoGame : GameBase
{
    public HiLoSlider hiLoSlider = new HiLoSlider();
    public bool _isOver;
}
public class HiLoSlider { public float currentValue = .5f, minTargetValue = 1f, maxTargetValue = 99f; }
public enum Suit { Hearts, Diamonds, Clubs, Spades }
public enum Rank { Ace=1, Two, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King }
public enum BaccaratBetType { Player, Banker, Tie }
public struct CardData
{
    public Suit Suit; public Rank Rank;
    public CardData(Suit suit, Rank rank) { Suit=suit; Rank=rank; }
    public int GetBaccaratValue() { return (int)Rank >= 11 ? 0 : (int)Rank; }
}
namespace FriendsAdvisor
{
    internal static class Read
    {
        public static int Calls;
        public static Random PeekRandom(GameBase game, int context=0) { Calls++; return new Random(7654321 ^ context); }
    }
}
public class FixedRandom : Random
{
    private double value;
    public FixedRandom(double v) { value=v; }
    public override double NextDouble() { return value; }
}
public class Harness
{
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static CardData Card(Rank rank) { return new CardData(Suit.Hearts,rank); }
    public static void Main()
    {
        try { Run(); }
        catch(Exception error) { Console.WriteLine("FAIL: "+error.GetType().Name+": "+error.Message); Environment.Exit(1); }
    }
    public static void Run()
    {
        string text;
        var baccarat = new Baccarat { deckInitialized=true, currentBetType=BaccaratBetType.Banker };
        baccarat.deck.AddRange(new[] { Card(Rank.Ace),Card(Rank.Two),Card(Rank.King),Card(Rank.Three) });
        var original = baccarat.deck.ToArray();
        SimplePredictions.TryDescribe(baccarat,new Random(1),out text);
        Check(text.Contains("下局闲 1 点，庄 5 点"),"known four card deal order");
        Check(text.Contains("当前押庄 / Banker → 中奖"),"known selected bet success");
        Check(text.Contains("x1.8"),"floor return factor");
        Check(Read.Calls==0,"existing deck must not shuffle");
        Check(baccarat.deck.Count==4 && baccarat.deckInitialized,"deck and flags unchanged");
        for(int i=0;i<4;i++) Check(baccarat.deck[i].Rank==original[i].Rank,"deck unchanged at "+i);
        baccarat.isPlaying=true;
        SimplePredictions.TryDescribe(baccarat,new Random(1),out text);
        Check(text.Contains("本局结束后"),"mid-deal waits instead of drawing again");
        Check(Read.Calls==0,"mid-deal must not shuffle");
        baccarat.isPlaying=false;
        baccarat.deck.Clear();
        SimplePredictions.TryDescribe(baccarat,new Random(1),out text);
        Check(Read.Calls==1 && baccarat.deck.Count==0,"empty-deck simulation shuffles clone only");
        baccarat.deck.Add(Card(Rank.Ten));
        baccarat.deck.Add(Card(Rank.Ace));
        SimplePredictions.TryDescribe(baccarat,new Random(1),out text);
        Check(Read.Calls==2 && baccarat.deck.Count==2,"mid-hand refill leaves remaining deck intact");
        baccarat.deckInitialized=false;
        SimplePredictions.TryDescribe(baccarat,new Random(1),out text);
        Check(Read.Calls==3 && !baccarat.deckInitialized && baccarat.deck.Count==2,"initial-deck simulation changes no state");

        var hilo = new HiLoGame();
        double[] edges = {0,.000001,.0099,.01,.0101,.49999999,.5,.50000001,.9899,.99,.9901,.999999999};
        foreach(double edge in edges)
        {
            SimplePredictions.TryDescribe(hilo,new FixedRandom(edge),out text);
            string suggestion=text.Split(new[] {"\n"},StringSplitOptions.None)[2];
            int start=suggestion.IndexOf("门槛设为 ",StringComparison.Ordinal)+"门槛设为 ".Length;
            int end=suggestion.IndexOf("%",start,StringComparison.Ordinal);
            double suggested=double.Parse(suggestion.Substring(start,end-start),System.Globalization.CultureInfo.InvariantCulture);
            float actual=(float)edge;
            bool over=suggestion.Contains("大 / Over");
            Check(over ? actual >= (float)(suggested/100) : actual <= (float)(suggested/100),"HiLo suggestion wins at boundary "+edge);
            Check(hilo.hiLoSlider.currentValue==.5f && !hilo._isOver,"HiLo cannot change slider or direction");
        }
        hilo.isPlaying=true;
        SimplePredictions.TryDescribe(hilo,new FixedRandom(.4),out text);
        Check(!text.Contains("建议押") && text.Contains("已锁定"),"HiLo locked game no advice to change controls");
        Console.WriteLine("PASS: "+assertions+" assertions; known cards, clone immutability, empty/partial deck refill, HiLo float boundaries.");
    }
}
