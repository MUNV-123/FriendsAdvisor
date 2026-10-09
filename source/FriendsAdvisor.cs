using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Extensions;
using Mirror;
using UnityEngine;

namespace FriendsAdvisor
{
    public static class Bootstrap
    {
        private static bool initialized;
        public static void Initialize()
        {
            try
            {
                if (initialized) return;
                var go = new GameObject("FriendsAdvisor.ReadOnly");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Advisor>();
                initialized = true;
                Debug.Log("[FriendsAdvisor] Loaded v1.3; read-only predictions; F8 panel, F7 markers; draggable UI.");
            }
            catch (Exception ex) { Debug.LogError("[FriendsAdvisor] Bootstrap: " + ex); }
        }
        public static void ObserveCrash(object game, float point)
        {
            try { CrashResults.Store(game as Crash, point); }
            catch (Exception ex) { Debug.LogWarning("[FriendsAdvisor] Crash observation: " + ex.Message); }
        }
        public static void ObserveWheelSpin(object wheel, float angle)
        {
            try { RaceWheelPredictions.ObserveWheelSpin(wheel, angle); }
            catch (Exception ex) { Debug.LogWarning("[FriendsAdvisor] Wheel observation: " + ex.Message); }
        }
        public static void ObserveRouletteBallSpin(object wheel, float angle)
        {
            try { RaceWheelPredictions.ObserveRouletteBallSpin(wheel, angle); }
            catch (Exception ex) { Debug.LogWarning("[FriendsAdvisor] Roulette observation: " + ex.Message); }
        }
    }

    internal static class CrashResults
    {
        private sealed class Result { public WeakReference game; public int round; public float point; }
        private static readonly Dictionary<int, Result> results = new Dictionary<int, Result>();
        public static void Store(Crash game, float point)
        {
            if (!game || !game.isServer) return;
            if (results.Count > 256) results.Clear();
            results[game.GetInstanceID()] = new Result { game = new WeakReference(game), round = Read.Field<int>(game, "gameTurn"), point = point };
        }
        public static bool TryGet(Crash game, out float point)
        {
            Result value;
            point = 0;
            if (!results.TryGetValue(game.GetInstanceID(), out value) || !ReferenceEquals(value.game.Target, game) || value.round != Read.Field<int>(game, "gameTurn")) return false;
            point = value.point;
            return true;
        }
    }

    internal static class Read
    {
        private static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
        public static object Value(object target, string name)
        {
            if (target == null) throw new InvalidOperationException("缺少对象: " + name);
            string key = target.GetType().FullName + ":" + name;
            FieldInfo field;
            if (!fields.TryGetValue(key, out field))
            {
                for (Type t = target.GetType(); t != null; t = t.BaseType)
                {
                    field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field != null) break;
                }
                if (field == null) throw new MissingFieldException(key);
                fields[key] = field;
            }
            return field.GetValue(target);
        }
        public static T Field<T>(object target, string name) { return (T)Value(target, name); }
        public static object Call(object target, string name, params object[] args)
        {
            string key = target.GetType().FullName + ":" + name;
            MethodInfo method;
            if (!methods.TryGetValue(key, out method))
            {
                for (Type t = target.GetType(); t != null; t = t.BaseType)
                {
                    method = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (method != null) break;
                }
                if (method == null) throw new MissingMethodException(key);
                methods[key] = method;
            }
            return method.Invoke(target, args);
        }
        public static System.Random PeekRandom(GameBase game, int context = 0)
        {
            // GetSeededRandom constructs a fresh generator. Guard its fallback so
            // the tool never advances Unity.Random or the shared seeded generator.
            if (!NetworkServer.active || !game.isServer) throw new InvalidOperationException("需要房主本地数据");
            var seed = NetworkSingleton<SeededRandomManager>.Instance;
            var manager = NetworkSingleton<GameManager>.Instance;
            if (!seed || !manager || Field<System.Random>(seed, "_random") == null)
                throw new InvalidOperationException("等待存档随机种子初始化");
            if (manager.state != GameState.Game && manager.state != GameState.Test)
                throw new InvalidOperationException("进入赌场后显示本轮预测");
            return (System.Random)Call(game, "GetSeededRandom", context);
        }
    }

    public sealed class Advisor : MonoBehaviour
    {
        private sealed class Marker
        {
            public Component target;
            public string text;
            public Color color;
        }
        private readonly List<Marker> markers = new List<Marker>();
        private GameBase[] machines = new GameBase[0];
        private GameBase selected;
        private Camera camera;
        private float nextScan, nextRefresh;
        private bool visible = true, showMarkers = true, guiLogged;
        private string description = "进入游戏后靠近机器，自动显示本轮提示。";
        private string title = "等待进入赌场";
        private GUIStyle box, body, heading, markerStyle;
        private Rect panel = new Rect(12, 12, 480, 240);
        private Vector2 scroll;
        private bool positionDirty;
        private float savePositionAt;
        private const string PositionX = "FriendsAdvisor.WindowX";
        private const string PositionY = "FriendsAdvisor.WindowY";
        private Font font;
        private string lastError;
        private PropertyInfo keyboardCurrent, f8Key, f7Key, keyPressed;

        private void Start()
        {
            try
            {
                if (PlayerPrefs.HasKey(PositionX)) panel.x = PlayerPrefs.GetFloat(PositionX) * Screen.width;
                if (PlayerPrefs.HasKey(PositionY)) panel.y = PlayerPrefs.GetFloat(PositionY) * Screen.height;
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
                Type keyboard = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                Type button = Type.GetType("UnityEngine.InputSystem.Controls.ButtonControl, Unity.InputSystem");
                if (keyboard != null && button != null)
                {
                    keyboardCurrent = keyboard.GetProperty("current");
                    f8Key = keyboard.GetProperty("f8Key");
                    f7Key = keyboard.GetProperty("f7Key");
                    keyPressed = button.GetProperty("wasPressedThisFrame");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[FriendsAdvisor] Font/input: " + ex.Message); }
        }

        private void Update()
        {
            try
            {
                if (positionDirty && Time.unscaledTime >= savePositionAt) SavePosition();
                if (keyboardCurrent != null && keyPressed != null)
                {
                    object keyboard = keyboardCurrent.GetValue(null, null);
                    if (keyboard != null)
                    {
                        if ((bool)keyPressed.GetValue(f8Key.GetValue(keyboard, null), null)) visible = !visible;
                        if ((bool)keyPressed.GetValue(f7Key.GetValue(keyboard, null), null)) showMarkers = !showMarkers;
                    }
                }
                if (!visible) return;
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + 0.1f;
                camera = Camera.main;
                if (NetworkClient.localPlayer)
                {
                    var pi = NetworkClient.localPlayer.GetComponent<PlayerInteract>();
                    if (pi)
                    {
                        var localCam = Read.Value(pi, "_cam") as Camera;
                        if (localCam && localCam.isActiveAndEnabled) camera = localCam;
                    }
                }
                if (!camera)
                {
                    selected = null;
                    markers.Clear();
                    title = "等待进入赌场";
                    description = "进入游戏后靠近机器，自动显示本轮提示。";
                    return;
                }
                if (Time.unscaledTime >= nextScan)
                {
                    nextScan = Time.unscaledTime + 1f;
                    machines = UnityEngine.Object.FindObjectsByType<GameBase>(FindObjectsSortMode.None);
                }
                selected = ChooseMachine();
                markers.Clear();
                if (!selected)
                {
                    title = "等待靠近机器";
                    description = "支持：扫雷、龙塔、Crash、CrossyRoad、HiLo、\n百家乐、轮盘赌、幸运转盘、黑杰克、\n彩色转盘 MoneyWheel、视频扑克 Poker。\n走到机器附近并看向它，提示会自动切换。";
                    return;
                }
                title = selected.GetType().Name + "  ·  " + selected.GameName;
                if (!NetworkServer.active || !selected.isServer)
                {
                    description = "当前为加入他人房间的客户端。\n本地缺少本轮种子或隐藏结果，无法确定预测。\n请自己创建房间后使用。";
                    return;
                }
                var rng = Read.PeekRandom(selected);
                int round = Read.Field<int>(selected, "gameTurn") + 1;
                string mode = selected.isPlaying ? "本轮进行中" : "下注前预览";
                description = "<color=#88cfff>" + mode + "  ·  回合 " + round + "</color>\n" + Describe(selected, rng);
                lastError = null;
            }
            catch (Exception ex)
            {
                markers.Clear();
                Exception error = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                description = "预测暂不可用：" + error.Message;
                if (lastError != error.Message)
                {
                    lastError = error.Message;
                    Debug.LogWarning("[FriendsAdvisor] " + error);
                }
            }
        }

        private GameBase ChooseMachine()
        {
            if (NetworkClient.localPlayer)
            {
                var interact = NetworkClient.localPlayer.GetComponent<PlayerInteract>();
                var component = interact ? interact.TargetInteractable as Component : null;
                if (component)
                {
                    var direct = component.GetComponentInParent<GameBase>();
                    if (direct) return direct;
                    var pad = component.GetComponentInParent<Keypad>();
                    if (pad && pad.casinoGame) return pad.casinoGame;
                    if (component is MinesweeperTile) return Read.Field<Minesweeper>(component, "minesweeperGame");
                    if (component is DragonTowerButton) return Read.Field<DragonTower>(component, "dragonTower");
                }
            }
            GameBase best = null;
            float score = float.MaxValue;
            foreach (var game in machines)
            {
                if (!game || !game.isActiveAndEnabled) continue;
                Vector3 delta = game.transform.position - camera.transform.position;
                float distance = delta.magnitude;
                if (distance > 12f) continue;
                float angle = Vector3.Angle(camera.transform.forward, delta);
                if (angle > 80f) continue;
                float candidate = distance + angle * 0.08f;
                if (candidate < score) { score = candidate; best = game; }
            }
            return best;
        }

        private string Describe(GameBase game, System.Random rng)
        {
            if (game is Minesweeper) return DescribeMines(game, rng);
            if (game is DragonTower) return DescribeDragon(game, rng);
            if (game is Crash) return DescribeCrash(game, rng);
            if (game is CrossyRoad) return DescribeRoad(game);
            string simple;
            if (RaceWheelPredictions.TryDescribe(game, rng, out simple)) return simple;
            if (BlackjackPredictions.TryDescribe(game, rng, out simple)) return simple;
            if (PokerPredictions.TryDescribe(game, rng, out simple)) return simple;
            if (SimplePredictions.TryDescribe(game, rng, out simple)) return simple;
            return "此机器暂未支持确定预测。";
        }

        private string DescribeMines(GameBase game, System.Random rng)
        {
            if (game.isPlaying && Read.Field<bool>(game, "_hasEnded")) return "本轮已经结束，等待下一轮。";
            var tiles = Read.Field<List<MinesweeperTile>>(game, "tiles");
            if (tiles == null || tiles.Count < 2) throw new InvalidOperationException("扫雷格子结构不匹配");
            int count = Mathf.Clamp(Read.Field<int>(game, "_currentMineCount"), 1, tiles.Count - 1);
            var mines = new HashSet<int>();
            if (game.isPlaying) mines.UnionWith(Read.Field<HashSet<int>>(game, "_mineIndexes"));
            else
            {
                int[] order = new int[tiles.Count];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                for (int i = 0; i < count; i++)
                {
                    int j = rng.Next(i, order.Length);
                    int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
                    mines.Add(order[i]);
                }
            }
            if (mines.Count != count) throw new InvalidOperationException("等待雷区生成");
            var revealed = game.isPlaying ? Read.Field<HashSet<int>>(game, "_revealedTiles") : new HashSet<int>();
            for (int i = 0; i < tiles.Count; i++)
            {
                if (revealed.Contains(i)) continue;
                AddMarker(tiles[i], mines.Contains(i) ? "雷" : "安全", mines.Contains(i) ? Bad : Good);
            }
            return "雷数 " + count + "  ·  未翻安全格 " + (tiles.Count - count - revealed.Count) + "\n<color=#85efae>点击绿色“安全”格，避开红色“雷”。</color>\n改动雷数后，下注前预览会立即重新计算。";
        }

        private string DescribeDragon(GameBase game, System.Random rng)
        {
            if (game.isPlaying && Read.Field<bool>(game, "_hasEnded")) return "本轮已经结束，等待下一轮。";
            var floors = Read.Value(game, "floors") as IList;
            if (floors == null || floors.Count == 0) throw new InvalidOperationException("缺少龙塔层数据");
            int current = game.isPlaying ? Read.Field<int>(game, "_currentFloor") : 0;
            var path = new StringBuilder();
            for (int i = 0; i < floors.Count; i++)
            {
                object floor = floors[i];
                int egg = game.isPlaying ? Read.Field<int>(floor, "eggIndex") : rng.Next(0, 4);
                if (egg < 0 || egg >= 4) throw new InvalidOperationException("龙塔危险按钮数据异常");
                var buttons = Read.Value(floor, "buttons") as IList;
                if (buttons == null || buttons.Count != 4) throw new InvalidOperationException("龙塔按钮结构不匹配");
                if (i < current) continue;
                if (path.Length > 0) path.Append(" / ");
                path.Append(i + 1).Append("层避开 ").Append(egg + 1);
                var indices = new HashSet<int>();
                foreach (object value in buttons)
                {
                    var button = value as DragonTowerButton;
                    if (!button || Read.Field<int>(button, "floorIndex") != i) throw new InvalidOperationException("龙塔层编号不匹配");
                    int index = Read.Field<int>(button, "buttonIndex");
                    if (index < 0 || index >= 4 || !indices.Add(index)) throw new InvalidOperationException("龙塔按钮编号不匹配");
                    AddMarker(button, (index == egg ? "危险 " : "安全 ") + (index + 1), index == egg ? Bad : Good);
                }
            }
            return "<color=#85efae>每层选择任意绿色“安全”按钮。</color>\n红色按钮会输；编号直接标在对应按钮上。\n" + path;
        }

        private string DescribeCrash(GameBase game, System.Random rng)
        {
            if (game.isPlaying && Read.Field<bool>(game, "_hasEnded")) return "本轮已兑现或爆炸，等待下一轮。";
            float point = 1.01f;
            if (game.isPlaying && Read.Field<bool>(game, "_hasStarted"))
            {
                if (!CrashResults.TryGet((Crash)game, out point)) return "爆点已选定，但助手未取得本轮实际爆点。\n等待下一轮，当前不提供确定预测。";
            }
            else if ((float)rng.NextDouble() > Read.Field<float>(game, "instantCrashChance"))
                point = (float)Read.Call(game, "GetRandomCrashPoint", (float)rng.NextDouble());
            float now = game.isPlaying ? Read.Field<float>(game, "_multiplier") : 1f;
            if (point <= 1.011f) return "<color=#ff8b93>本轮为瞬间爆炸：爆点 " + point.ToString("0.0000") + "x</color>\n建议换另一台机器；等待不会改变同一台的本轮结果。";
            float early = 1f + (point - 1f) * 0.7f;
            return "确定爆点：<b>" + point.ToString("0.0000") + "x</b>\n当前倍率：" + now.ToString("0.0000") + "x\n" + (game.isPlaying && now >= early ? "<color=#ffdc84>现在立即兑现，已超过建议倍率。</color>" : "建议提早兑现：约 " + early.ToString("0.0000") + "x") + "\n兑现需要手动操作；反应、帧率会影响能否及时兑现。";
        }

        private string DescribeRoad(GameBase game)
        {
            if (game.isPlaying && Read.Field<bool>(game, "_hasEnded")) return "本轮已经结束，等待下一轮。";
            var multipliers = Read.Field<double[]>(game, "stepMultipliers");
            if (multipliers == null || multipliers.Length == 0) throw new InvalidOperationException("缺少步数倍率");
            int maximum = Math.Max(0, Math.Min(Read.Field<int>(game, "maxSteps"), multipliers.Length - 1));
            if (maximum == 0) throw new InvalidOperationException("没有可玩的步数");
            int current = game.isPlaying ? Read.Field<int>(game, "_currentStep") : 0;
            int stop = maximum;
            for (int step = current; step < maximum; step++)
            {
                float chance = game.isPlaying && step == current ? Read.Field<float>(game, "_currentCrashChance") : (float)Read.Call(game, "GetCrashChanceForStep", step);
                if ((float)Read.PeekRandom(game, step * 9999).NextDouble() < chance) { stop = step; break; }
            }
            if (stop == 0) return "<color=#ff8b93>第一步就会失败，建议换另一台机器。</color>\n等待不会改变同一台的本轮结果。";
            string warning = stop == maximum ? "本轮可以安全走到最后一步。" : "<color=#ff8b93>第 " + (stop + 1) + " 步会失败。</color>";
            return "当前已走 " + current + " 步  ·  最大 " + maximum + " 步\n" + warning + "\n<color=#85efae>走到第 " + stop + " 步后兑现：" + multipliers[stop].ToString("0.####") + "x</color>\n" + (current >= stop ? "现在应兑现，不要再前进。" : "还可安全前进 " + (stop - current) + " 步。");
        }

        private static readonly Color Good = new Color(0.3f, 1f, 0.55f);
        private static readonly Color Bad = new Color(1f, 0.3f, 0.4f);
        private void AddMarker(Component target, string text, Color color)
        {
            if (target) markers.Add(new Marker { target = target, text = text, color = color });
        }

        private void OnGUI()
        {
            try
            {
                if (keyboardCurrent == null && Event.current.type == EventType.KeyDown)
                {
                    if (Event.current.keyCode == KeyCode.F8) visible = !visible;
                    if (Event.current.keyCode == KeyCode.F7) showMarkers = !showMarkers;
                }
                if (!visible) return;
                if (box == null)
                {
                    box = new GUIStyle(GUI.skin.box);
                    body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, richText = true, wordWrap = true };
                    body.normal.textColor = Color.white;
                    heading = new GUIStyle(body) { fontSize = 20, fontStyle = FontStyle.Bold };
                    markerStyle = new GUIStyle(GUI.skin.box) { font = font, fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                }
                float width = Math.Min(480f, Screen.width - 24f);
                float height = body.CalcHeight(new GUIContent(description), width - 50f) + 126f;
                height = Math.Min(height, Screen.height - 24f);
                panel.width = width;
                panel.height = height;
                ClampPanel();
                Vector2 previousPosition = panel.position;
                panel = GUI.Window(918274, panel, DrawPanel, GUIContent.none, box);
                ClampPanel();
                if (panel.position != previousPosition)
                {
                    positionDirty = true;
                    savePositionAt = Time.unscaledTime + 0.6f;
                }
                if (showMarkers && camera)
                {
                    foreach (var mark in markers)
                    {
                        if (!mark.target) continue;
                        Vector3 point = camera.WorldToScreenPoint(mark.target.transform.position);
                        if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) continue;
                        markerStyle.normal.textColor = mark.color;
                        GUI.Box(new Rect(point.x - 36, Screen.height - point.y - 14, 72, 28), mark.text, markerStyle);
                    }
                }
                if (!guiLogged && Event.current.type == EventType.Repaint)
                {
                    guiLogged = true;
                    Debug.Log("[FriendsAdvisor] GUI ready v1.3; Chinese font, draggable window and overlay rendered.");
                }
            }
            catch (Exception ex)
            {
                if (lastError != ex.Message) { lastError = ex.Message; Debug.LogError("[FriendsAdvisor] GUI: " + ex); }
            }
        }
        private void DrawPanel(int id)
        {
            GUI.Label(new Rect(14, 8, panel.width - 28, 28), "朋友梭哈吧 · 预测助手", heading);
            GUI.Label(new Rect(14, 38, panel.width - 28, 28), title, body);
            float contentHeight = body.CalcHeight(new GUIContent(description), panel.width - 50);
            scroll = GUI.BeginScrollView(new Rect(14, 70, panel.width - 28, panel.height - 122), scroll,
                new Rect(0, 0, panel.width - 50, contentHeight));
            GUI.Label(new Rect(0, 0, panel.width - 50, contentHeight), description, body);
            GUI.EndScrollView();
            GUI.Label(new Rect(14, panel.height - 48, panel.width - 28, 22), "F8 显示/隐藏   F7 格子标记   当前回合有效", body);
            GUI.Label(new Rect(14, panel.height - 26, panel.width - 28, 22), "按 Esc 显示鼠标，拖动标题栏移动窗口（自动保存）", body);
            if (Cursor.lockState != CursorLockMode.Locked) GUI.DragWindow(new Rect(0, 0, panel.width, 36));
        }
        private void ClampPanel()
        {
            panel.x = Mathf.Clamp(panel.x, 0, Mathf.Max(0, Screen.width - panel.width));
            panel.y = Mathf.Clamp(panel.y, 0, Mathf.Max(0, Screen.height - panel.height));
        }
        private void SavePosition()
        {
            PlayerPrefs.SetFloat(PositionX, panel.x / Mathf.Max(1, Screen.width));
            PlayerPrefs.SetFloat(PositionY, panel.y / Mathf.Max(1, Screen.height));
            PlayerPrefs.Save();
            positionDirty = false;
        }
        private void OnDestroy()
        {
            if (positionDirty) SavePosition();
            if (font) UnityEngine.Object.Destroy(font);
        }
    }
}
