using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace FriendsAdvisor
{
    // Observations only copy the game's chosen animation targets. Simulated poses
    // are matrices; no Transform, game field, coroutine or live RNG is changed.
    internal static class RaceWheelPredictions
    {
        private sealed class Spin
        {
            public WeakReference wheel, game;
            public int round;
            public float angle;
            public string result, reason;
            public bool hasBall;
        }
        private static readonly Dictionary<int, Spin> spins = new Dictionary<int, Spin>();
        private static readonly PropertyInfo estimatedValue = typeof(GameBase).GetProperty("EstimatedValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool TryDescribe(GameBase game, System.Random rng, out string text)
        {
            text = null;
            if (!(game is Roulette) && !(game is WheelOfFortune)) return false;
            var wheel = Read.Field<Wheel>(game, "wheel");
            if (!wheel) throw new InvalidOperationException("缺少转盘组件");
            string result, reason;
            if (game.isPlaying)
            {
                Spin spin;
                if (!spins.TryGetValue(wheel.GetInstanceID(), out spin) || !ReferenceEquals(spin.wheel.Target, wheel) ||
                    !ReferenceEquals(spin.game.Target, game) || spin.round != Read.Field<int>(game, "gameTurn"))
                { text = "本轮已经开始，助手未取得旋转目标。\n等待下一轮后显示下注前落点。"; return true; }
                if (wheel is RouletteWheel && !spin.hasBall)
                { text = "等待本轮滚球目标；暂不提供确定落点。"; return true; }
                result = spin.result; reason = spin.reason;
            }
            else
            {
                if (Read.Field<bool>(wheel, "_isSpinning"))
                { text = "转盘尚未停稳，等待下一轮。"; return true; }
                float angle; float? ballAngle;
                Angles(wheel, rng, out angle, out ballAngle);
                TryResult(game, wheel, angle, ballAngle, out result, out reason);
            }
            if (result == null) { text = "转盘落点暂不能确定：" + reason; return true; }
            text = game is Roulette ? DescribeRoulette(game, result) : DescribeFortune(game, result);
            return true;
        }

        internal static void ObserveWheelSpin(object value, float angle)
        {
            var wheel = value as Wheel;
            if (!wheel || !wheel.isServer) return;
            var game = FindGame(wheel);
            if (!game || !game.isPlaying || !(game is Roulette) && !(game is WheelOfFortune)) return;
            if (spins.Count > 256) spins.Clear();
            var spin = new Spin { wheel = new WeakReference(wheel), game = new WeakReference(game), round = Read.Field<int>(game, "gameTurn"), angle = angle };
            if (!(wheel is RouletteWheel)) TryResult(game, wheel, angle, null, out spin.result, out spin.reason);
            spins[wheel.GetInstanceID()] = spin;
        }

        internal static void ObserveRouletteBallSpin(object value, float angle)
        {
            var wheel = value as RouletteWheel;
            if (!wheel || !wheel.isServer) return;
            Spin spin;
            if (!spins.TryGetValue(wheel.GetInstanceID(), out spin) || !ReferenceEquals(spin.wheel.Target, wheel)) return;
            var game = spin.game.Target as GameBase;
            if (!game || !game.isPlaying || spin.round != Read.Field<int>(game, "gameTurn")) return;
            spin.hasBall = true;
            TryResult(game, wheel, spin.angle, angle, out spin.result, out spin.reason);
        }

        private static GameBase FindGame(Wheel wheel)
        {
            var game = wheel.GetComponentInParent<GameBase>();
            if (game && (game is Roulette || game is WheelOfFortune) && ReferenceEquals(Read.Value(game, "wheel"), wheel)) return game;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<GameBase>(FindObjectsSortMode.None))
                if ((candidate is Roulette || candidate is WheelOfFortune) && ReferenceEquals(Read.Value(candidate, "wheel"), wheel)) return candidate;
            return null;
        }

        private static void Angles(Wheel wheel, System.Random rng, out float angle, out float? ballAngle)
        {
            // RouletteWheel makes two integer draws. The ball's direction does not
            // follow spinDirection, while the normal wheel makes one double draw.
            bool roulette = wheel is RouletteWheel;
            float fraction = roulette ? (float)rng.Next(0, 37) * 9.72973f : (float)(rng.NextDouble() * 360.0);
            angle = (float)Read.Field<int>(wheel, "minTurnAmount") * 360f + fraction;
            ballAngle = roulette ? (float?)((float)Read.Field<int>(wheel, "minTurnAmount") * 360f + (float)rng.Next(0, 37) * 9.72973f) : null;
            if (Read.Field<bool>(wheel, "spinDirection")) angle *= -1f;
        }

        private static bool TryResult(GameBase game, Wheel wheel, float angle, float? ballAngle, out string result, out string reason)
        {
            result = null; reason = null;
            var wheelTransform = Read.Field<Transform>(wheel, "wheelTransform");
            var resultsParent = Read.Field<Transform>(wheel, "resultsParent");
            var selector = Read.Field<Transform>(wheel, "resultSelector");
            if (!wheelTransform || !resultsParent || !selector) { reason = "缺少转盘姿态或指针"; return false; }
            var results = Read.Field<WheelResult[]>(wheel, "_results");
            if (results == null || results.Length == 0) results = resultsParent.GetComponentsInChildren<WheelResult>();
            if (results.Length < 2) { reason = "转盘分区不完整"; return false; }
            if (!Inside(wheelTransform, game.transform) || !Inside(selector, game.transform))
            { reason = "指针或转盘位于机器外部，无法保证姿态稳定"; return false; }
            Transform ballWheel = null, ball = null;
            if (wheel is RouletteWheel)
            {
                if (!ballAngle.HasValue) { reason = "缺少滚球旋转目标"; return false; }
                ballWheel = Read.Field<Transform>(wheel, "ballWheel");
                ball = Read.Field<Transform>(wheel, "ball");
                if (!ballWheel || !ball || !Inside(ballWheel, game.transform) || !Inside(ball, game.transform))
                { reason = "滚球层级不匹配"; return false; }
            }
            Vector3 pointer = FinalMatrix(selector, game.transform, wheelTransform, angle, ballWheel, ballAngle.GetValueOrDefault(), ball, 0).MultiplyPoint3x4(Vector3.zero);
            float nearest = float.MaxValue, second = float.MaxValue;
            int winner = -1;
            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i] || !Inside(results[i].transform, wheelTransform)) { reason = "转盘分区层级不匹配"; return false; }
                Vector3 position = FinalMatrix(results[i].transform, game.transform, wheelTransform, angle, ballWheel, ballAngle.GetValueOrDefault(), ball, 0).MultiplyPoint3x4(Vector3.zero);
                float distance = (position - pointer).sqrMagnitude;
                if (float.IsNaN(distance) || float.IsInfinity(distance)) { reason = "转盘坐标无效"; return false; }
                if (distance < nearest) { second = nearest; nearest = distance; winner = i; }
                else if (distance < second) second = distance;
            }
            // Refuse boundaries instead of assigning a certain number to a tie or
            // a gap small enough for float/tween end-frame rounding to decide.
            if (winner < 0 || second - nearest <= Math.Max(0.0000001f, second * 0.00001f))
            { reason = "落点接近分界，等待实际停转确认"; return false; }
            result = results[winner].result;
            if (string.IsNullOrEmpty(result)) { result = null; reason = "落点没有有效结果"; return false; }
            return true;
        }

        private static bool Inside(Transform value, Transform parent) { return value == parent || value.IsChildOf(parent); }

        private static Matrix4x4 FinalMatrix(Transform value, Transform stableRoot, Transform wheel, float angle, Transform ballWheel, float ballAngle, Transform ball, int depth)
        {
            if (depth > 128) throw new InvalidOperationException("转盘层级过深");
            if (value == stableRoot) return stableRoot.localToWorldMatrix;
            if (!value || !value.parent) throw new InvalidOperationException("转盘层级已变化");
            Quaternion rotation = value == wheel ? Quaternion.Euler(0f, 0f, -angle) : value == ballWheel ? Quaternion.Euler(0f, 0f, -ballAngle) : value.localRotation;
            // The roulette ball first lifts, then drops to this exact local pose.
            Vector3 position = value == ball ? new Vector3(0f, 0.9f, 0f) : value.localPosition;
            return FinalMatrix(value.parent, stableRoot, wheel, angle, ballWheel, ballAngle, ball, depth + 1) * Matrix4x4.TRS(position, rotation, value.localScale);
        }

        private static string DescribeRoulette(GameBase game, string result)
        {
            int number;
            if (!int.TryParse(result, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number < 0 || number > 36)
                return "转盘落点：" + result + "\n无法识别为 0–36 的轮盘号码。";
            string advice = game.isPlaying ? "中奖单号：" : "应押单号：";
            string payout = "单号基础返还 " + (36.0 * (double)estimatedValue.GetValue(game, null)).ToString("0.####", CultureInfo.InvariantCulture) + "x（含本金，未计玩家增益）。";
            if (number == 0) return "<color=#85efae>本轮目标号码：<b>0（绿色）</b></color>\n" + advice + "0；" + payout + "\n0 不属于红黑、单双、大小、列或打。";
            bool red = Array.IndexOf(new[] { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 }, number) >= 0;
            return "<color=#85efae>本轮目标号码：<b>" + number + "（" + (red ? "红色" : "黑色") + "）</b></color>\n" + advice + number + "；" + payout + "\n其他中奖项：" + (red ? "红" : "黑") + "、" + (number % 2 == 0 ? "双" : "单") + "、" + (number <= 18 ? "小（1–18）" : "大（19–36）") + "。\n第 " + ((number - 1) / 12 + 1) + " 打（12 数组），第 " + ((number - 1) % 3 + 1) + " 列。";
        }

        private static string DescribeFortune(GameBase game, string result)
        {
            if (result == "Spin") return "<color=#ffdc84>本次落点：再转一次（Spin）。</color>\n游戏会自动续转，助手随后显示新落点。\n本次尚未决定最终返还。";
            decimal value;
            if (!decimal.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return "本轮目标落点：" + result + "\n无法识别返还倍率；游戏按 0x 处理。";
            double factor = (double)value * (double)estimatedValue.GetValue(game, null);
            string outcome = factor > 1.0 ? "盈利" : factor == 1.0 ? "回本" : factor > 0.0 ? "未回本" : "未中奖";
            return "本轮目标落点：<b>" + result + "x</b>\n基础返还：" + factor.ToString("0.####", CultureInfo.InvariantCulture) + "x（含本金）→ " + outcome + "。\n玩家增益可能改变实际返还。";
        }

    }
}
