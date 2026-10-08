using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

// Every simulated card, removal, shuffle and RNG call stays in an independent copy.
// The caller must supply a fresh, validated GameBase RNG and only run on the host.
namespace FriendsAdvisor
{
internal static class SimplePredictions
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly PropertyInfo EstimatedValueProperty = typeof(GameBase).GetProperty("EstimatedValue", InstanceFlags);

    internal static bool TryDescribe(GameBase game, System.Random rng, out string text)
    {
        text = null;
        if (game == null || rng == null) return false;
        if (game is Baccarat)
        {
            text = DescribeBaccarat(game);
            return true;
        }
        if (game is HiLoGame)
        {
            text = DescribeHiLo(game, rng);
            return true;
        }
        return false;
    }

    private static string DescribeBaccarat(GameBase game)
    {
        if (game.isPlaying) return "正在发牌或结算。下一局预测将在本局结束后更新。";

        var storedDeck = Field<List<CardData>>(game, "deck");
        var deck = storedDeck == null ? new List<CardData>() : new List<CardData>(storedDeck);
        if (!Field<bool>(game, "deckInitialized")) deck = NewDeck(game);
        var dealt = new CardData[4];
        for (int i = 0; i < dealt.Length; i++)
        {
            if (deck.Count == 0) deck = NewDeck(game);
            if (deck.Count == 0) throw new InvalidOperationException("百家乐没有有效牌堆。");
            dealt[i] = deck[0];
            deck.RemoveAt(0);
        }

        int player = (dealt[0].GetBaccaratValue() + dealt[2].GetBaccaratValue()) % 10;
        int banker = (dealt[1].GetBaccaratValue() + dealt[3].GetBaccaratValue()) % 10;
        BaccaratBetType winner = player > banker ? BaccaratBetType.Player : banker > player ? BaccaratBetType.Banker : BaccaratBetType.Tie;
        BaccaratBetType chosen = Field<BaccaratBetType>(game, "currentBetType");
        double multiplier = (winner == BaccaratBetType.Tie ? 10.0 : 2.0) * EstimatedValue(game);
        return "下局闲 " + player + " 点，庄 " + banker + " 点 → " + BetName(winner) + "。\n"
            + "应押：" + BetName(winner) + "；基础返还 x" + Format(multiplier) + "（含本金，未计玩家增益）。\n"
            + "当前押" + BetName(chosen) + " → " + (chosen == winner ? "中奖" : "未中奖") + "。\n"
            + "闲牌：" + CardName(dealt[0]) + "、" + CardName(dealt[2])
            + "；庄牌：" + CardName(dealt[1]) + "、" + CardName(dealt[3]) + "。";
    }

    private static List<CardData> NewDeck(GameBase game)
    {
        int count = Field<int>(game, "numberOfDecks");
        if (count <= 0 || count > 64) throw new InvalidOperationException("百家乐牌堆数量超出支持范围。");
        var deck = new List<CardData>(count * 52);
        for (int n = 0; n < count; n++)
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                    if ((int)rank != 0) deck.Add(new CardData(suit, rank));

        // PeekRandom validates the host, seed and both managers before invoking
        // GameBase's pure fresh-generator method. No shared generator is consumed.
        var random = Read.PeekRandom(game, unchecked(deck.Count * 10000));
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            CardData card = deck[i];
            deck[i] = deck[j];
            deck[j] = card;
        }
        return deck;
    }

    private static string DescribeHiLo(GameBase game, System.Random rng)
    {
        var slider = Field<HiLoSlider>(game, "hiLoSlider");
        if (slider == null) throw new InvalidOperationException("大小骰子没有有效滑杆。");
        float roll = (float)rng.NextDouble();
        float target = slider.currentValue;
        bool over = Field<bool>(game, "_isOver");
        bool win = over ? roll >= target : roll <= target;
        double chance = over ? 1.0 - (double)target : (double)target;
        double multiplier = chance > 0.0 ? EstimatedValue(game) / chance : double.PositiveInfinity;

        string result = "本局点数：" + ((double)roll * 100.0).ToString("0.000000", CultureInfo.InvariantCulture) + "%。\n"
            + "当前押" + (over ? "大 / Over" : "小 / Under") + "，门槛 "
            + ((double)target * 100.0).ToString("0.###", CultureInfo.InvariantCulture) + "% → " + (win ? "中奖" : "未中奖") + "。";
        if (game.isPlaying) return result + "\n本局已开始，门槛与方向已锁定。";

        float minimum = Field<float>(slider, "minTargetValue");
        float maximum = Field<float>(slider, "maxTargetValue");
        double percentage = (double)roll * 100.0;
        // A full percentage point margin avoids displaying a rounded target that
        // could actually sit on the losing side of the float comparison.
        double underTarget = Math.Min(maximum, Math.Ceiling(percentage) + 1.0);
        underTarget = Math.Max(minimum, underTarget);
        double overTarget = Math.Max(minimum, Math.Floor(percentage) - 1.0);
        overTarget = Math.Min(maximum, overTarget);
        bool underValid = (float)(underTarget / 100.0) >= roll;
        bool overValid = (float)(overTarget / 100.0) <= roll;
        bool recommendOver = overValid && (!underValid || 1.0 - overTarget / 100.0 < underTarget / 100.0);
        if (underValid || overValid)
        {
            double suggested = recommendOver ? overTarget : underTarget;
            result += "\n建议押" + (recommendOver ? "大 / Over" : "小 / Under") + "，门槛设为 "
                + suggested.ToString("0.###", CultureInfo.InvariantCulture) + "%（留有比较余量）。";
        }
        if (win) result += "\n当前基础返还 x" + Format(multiplier) + "（含本金，未计玩家增益）。";
        return result;
    }

    private static double EstimatedValue(GameBase game)
    {
        return (double)EstimatedValueProperty.GetValue(game, null);
    }

    private static T Field<T>(object value, string name)
    {
        for (Type type = value.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null) return (T)field.GetValue(value);
        }
        throw new MissingFieldException(value.GetType().FullName, name);
    }

    private static string BetName(BaccaratBetType type)
    {
        return type == BaccaratBetType.Player ? "闲 / Player" : type == BaccaratBetType.Banker ? "庄 / Banker" : "和 / Tie";
    }

    private static string CardName(CardData card)
    {
        return card.Rank == Rank.Ace ? "A" : card.Rank == Rank.Jack ? "J" : card.Rank == Rank.Queen ? "Q" : card.Rank == Rank.King ? "K" : ((int)card.Rank).ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
}
