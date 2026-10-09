using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FriendsAdvisor;

// A detached model supplies Unity's mathematical conventions and object tree.
// Tests execute the production module without starting Unity or a game session.
namespace UnityEngine
{
    public enum FindObjectsSortMode { None }
    public class Object
    {
        internal static readonly List<Object> All = new List<Object>();
        private static int nextId; private readonly int id = ++nextId;
        public Object() { All.Add(this); }
        public int GetInstanceID() { return id; }
        public static implicit operator bool(Object value) { return !ReferenceEquals(value, null); }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode ignored) where T : Object { return All.OfType<T>().ToArray(); }
    }
    public class Component : Object
    {
        public Transform transform;
        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform item = transform; item != null; item = item.parent)
                foreach (var component in All.OfType<T>()) if (component.transform == item) return component;
            return null;
        }
    }
    public class Transform : Component
    {
        public Transform parent;
        public Vector3 localPosition, localScale = new Vector3(1, 1, 1);
        public Quaternion localRotation = Quaternion.identity;
        public Transform() { transform = this; }
        public bool IsChildOf(Transform ancestor) { for (var item = parent; item != null; item = item.parent) if (item == ancestor) return true; return false; }
        public Matrix4x4 localToWorldMatrix { get { var local = Matrix4x4.TRS(localPosition, localRotation, localScale); return parent == null ? local : parent.localToWorldMatrix * local; } }
        public Vector3 position { get { return localToWorldMatrix.MultiplyPoint3x4(Vector3.zero); } }
        public T[] GetComponentsInChildren<T>() where T : Component { return All.OfType<T>().Where(x => x.transform == this || x.transform.IsChildOf(this)).ToArray(); }
    }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public struct Quaternion
    {
        internal System.Numerics.Quaternion value;
        public static Quaternion identity { get { return new Quaternion { value=System.Numerics.Quaternion.Identity }; } }
        public static Quaternion Euler(float x,float y,float z) { return new Quaternion { value=System.Numerics.Quaternion.CreateFromYawPitchRoll(y*(float)Math.PI/180f,x*(float)Math.PI/180f,z*(float)Math.PI/180f) }; }
    }
    public struct Matrix4x4
    {
        internal System.Numerics.Matrix4x4 value;
        public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)
        {
            return new Matrix4x4 { value=System.Numerics.Matrix4x4.CreateScale(s.x,s.y,s.z)*System.Numerics.Matrix4x4.CreateFromQuaternion(q.value)*System.Numerics.Matrix4x4.CreateTranslation(p.x,p.y,p.z) };
        }
        public Vector3 MultiplyPoint3x4(Vector3 p) { var result=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(p.x,p.y,p.z),value); return new Vector3(result.X,result.Y,result.Z); }
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b) { return new Matrix4x4 { value=b.value*a.value }; }
    }
}
public class GameBase : UnityEngine.Component
{
    public bool isPlaying, isServer=true; public int gameTurn;
    protected double EstimatedValue { get { return .9; } }
    public GameBase() { transform=new UnityEngine.Transform(); }
}
public class Wheel : UnityEngine.Component
{
    public bool isServer=true,_isSpinning,spinDirection; public int minTurnAmount=3;
    public UnityEngine.Transform wheelTransform,resultsParent,resultSelector;
    public WheelResult[] _results;
}
public class RouletteWheel : Wheel { public UnityEngine.Transform ballWheel,ball; }
public class WheelResult : UnityEngine.Component { public string result; }
public class Roulette : GameBase { public Wheel wheel; }
public class WheelOfFortune : GameBase { public Wheel wheel; }
public class MoneyWheel : GameBase { public Wheel wheel; public string _currentBettingOption="Green"; }
namespace FriendsAdvisor
{
    internal static class Read
    {
        public static object Value(object target,string name) { for(var t=target.GetType();t!=null;t=t.BaseType) { var field=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly); if(field!=null)return field.GetValue(target); } throw new MissingFieldException(name); }
        public static T Field<T>(object target,string name) { return (T)Value(target,name); }
    }
}
public class SequenceRandom : Random
{
    private readonly Queue<int> integers; private readonly double value;
    public int IntCalls,DoubleCalls;
    public SequenceRandom(double fraction,params int[] values) { value=fraction; integers=new Queue<int>(values); }
    public override int Next(int min,int max) { IntCalls++; int item=integers.Dequeue(); if(item<min||item>=max)throw new Exception("RNG range mismatch"); return item; }
    public override double NextDouble() { DoubleCalls++; return value; }
}
public static class Harness
{
    private static int count;
    private static void Check(bool value,string reason) { count++; if(!value)throw new Exception(reason); }
    private static UnityEngine.Transform Child(UnityEngine.Transform parent,float x=0,float y=0,float z=0) { return new UnityEngine.Transform { parent=parent,localPosition=new UnityEngine.Vector3(x,y,z) }; }
    private static WheelOfFortune Fortune()
    {
        var game=new WheelOfFortune(); var wheel=new Wheel { transform=Child(game.transform) }; game.wheel=wheel;
        wheel.wheelTransform=Child(wheel.transform); wheel.resultsParent=wheel.wheelTransform; wheel.resultSelector=Child(game.transform,0,1);
        var values=new[] {"0","1","5","Spin"}; wheel._results=new WheelResult[4];
        for(int i=0;i<4;i++) { double angle=i*Math.PI/2; wheel._results[i]=new WheelResult { result=values[i],transform=Child(wheel.wheelTransform,(float)Math.Sin(angle),(float)Math.Cos(angle)) }; }
        return game;
    }
    private static Roulette RouletteGame()
    {
        var game=new Roulette(); var wheel=new RouletteWheel { transform=Child(game.transform) }; game.wheel=wheel;
        wheel.wheelTransform=Child(wheel.transform); wheel.resultsParent=wheel.wheelTransform; wheel.ballWheel=Child(game.transform); wheel.ball=Child(wheel.ballWheel,0,1.25f,.1f); wheel.resultSelector=wheel.ball;
        wheel._results=new WheelResult[37];
        for(int i=0;i<37;i++) { double angle=i*2*Math.PI/37; wheel._results[i]=new WheelResult { result=i.ToString(),transform=Child(wheel.wheelTransform,(float)Math.Sin(angle)*.9f,(float)Math.Cos(angle)*.9f) }; }
        return game;
    }
    private static MoneyWheel MoneyGame()
    {
        var game=new MoneyWheel(); var wheel=new Wheel { transform=Child(game.transform) }; game.wheel=wheel;
        wheel.wheelTransform=Child(wheel.transform); wheel.resultsParent=wheel.wheelTransform; wheel.resultSelector=Child(game.transform,0,1);
        var values=new[] {"Green","Blue","Red","Orange"}; wheel._results=new WheelResult[4];
        for(int i=0;i<4;i++) { double angle=i*Math.PI/2; wheel._results[i]=new WheelResult { result=values[i],transform=Child(wheel.wheelTransform,(float)Math.Sin(angle),(float)Math.Cos(angle)) }; }
        return game;
    }
    private static string Describe(GameBase game,Random rng) { string text; Check(RaceWheelPredictions.TryDescribe(game,rng,out text),"machine handled"); return text; }
    public static void Main()
    {
        try { Run(); Console.WriteLine("PASS: "+count+" assertions; independent RNG draws, both directions, virtual hierarchy transforms, ties, capture phases, round staleness, Roulette/Fortune regression, MoneyWheel colors, selected bets, payout and locked-bet wording."); }
        catch(Exception error) { Console.Error.WriteLine("FAIL: "+error); Environment.Exit(1); }
    }
    private static void Run()
    {
        var fortune=Fortune(); var rng=new SequenceRandom(.5); var original=fortune.wheel.wheelTransform.localRotation.value;
        Check(Describe(fortune,rng).Contains("5x"),"fortune half rotation selects known result");
        Check(rng.DoubleCalls==1&&rng.IntCalls==0,"normal wheel exactly one independent double");
        Check(fortune.wheel.wheelTransform.localRotation.value==original&&!fortune.isPlaying,"preview cannot mutate transform/game");
        Check(Describe(fortune,new SequenceRandom(.25)).Contains("再转一次"),"Spin does not pretend final payout");
        Check(Describe(fortune,new SequenceRandom(.125)).Contains("分界"),"tie refuses certain prediction");
        fortune.wheel.spinDirection=true;
        Check(Describe(fortune,new SequenceRandom(.25)).Contains("1x"),"reversed spin chooses opposite quadrant");
        fortune.wheel.spinDirection=false;
        fortune.transform.localPosition=new UnityEngine.Vector3(4,-2,5); fortune.transform.localRotation=UnityEngine.Quaternion.Euler(0,0,34); fortune.transform.localScale=new UnityEngine.Vector3(1.2f,.8f,2);
        fortune.wheel.wheelTransform.localRotation=UnityEngine.Quaternion.Euler(0,0,67);
        Check(Describe(fortune,new SequenceRandom(.5)).Contains("5x"),"absolute target ignores previous rotation with translated rotated scaled root");
        fortune.wheel.resultSelector.parent=new UnityEngine.Transform();
        Check(Describe(fortune,new SequenceRandom(.5)).Contains("机器外部"),"external pointer cannot claim stable pose");

        var roulette=RouletteGame(); var rw=(RouletteWheel)roulette.wheel; rng=new SequenceRandom(.99,5,3);
        string preview=Describe(roulette,rng);
        Check(preview.Contains("35（黑色）"),"roulette independent wheel and ball result is 35");
        Check(preview.Contains("应押单号：35")&&preview.Contains("32.4x"),"roulette prebet advice uses machine payout factor");
        Check(rng.IntCalls==2&&rng.DoubleCalls==0,"roulette exactly two integer draws");
        Check(rw.ball.localPosition.y==1.25f&&rw.ball.localPosition.z==.1f,"virtual final drop cannot move real ball");
        rw.spinDirection=true;
        Check(Describe(roulette,new SequenceRandom(0,5,3)).Contains("8（黑色）"),"roulette reverse affects wheel only");
        rw.spinDirection=false;
        Check(Describe(roulette,new SequenceRandom(0,0,0)).Contains("0（绿色）"),"zero does not get red/black/even categories");
        roulette.isPlaying=true;
        Check(Describe(roulette,new SequenceRandom(0)).Contains("未取得"),"no mid-spin observation cannot claim result");
        RaceWheelPredictions.ObserveWheelSpin(rw,1080+5*9.72973f);
        Check(Describe(roulette,new SequenceRandom(0)).Contains("等待本轮滚球目标"),"requires both actual rotation targets");
        RaceWheelPredictions.ObserveRouletteBallSpin(rw,1080+3*9.72973f);
        string active=Describe(roulette,new SequenceRandom(0));
        Check(active.Contains("35（黑色）"),"active result copied from actual targets");
        Check(active.Contains("中奖单号：35")&&!active.Contains("应押"),"locked round describes winning number without advice to change bet");
        rw.wheelTransform.localRotation=UnityEngine.Quaternion.Euler(0,0,130); rw.ballWheel.localRotation=UnityEngine.Quaternion.Euler(0,0,-42); rw.ball.localPosition=new UnityEngine.Vector3(0,1.1f,.05f);
        Check(Describe(roulette,new SequenceRandom(0)).Contains("35（黑色）"),"stored result stable while animations move");
        roulette.gameTurn++;
        Check(Describe(roulette,new SequenceRandom(0)).Contains("未取得"),"cannot reuse observation in later round");
        roulette.gameTurn--; rw.isServer=false;
        RaceWheelPredictions.ObserveWheelSpin(rw,1080); RaceWheelPredictions.ObserveRouletteBallSpin(rw,1080);
        Check(Describe(roulette,new SequenceRandom(0)).Contains("35（黑色）"),"client capture cannot overwrite host record");

        var money=MoneyGame(); original=money.wheel.wheelTransform.localRotation.value;
        var colors=new[] {"绿色","橙色","红色","蓝色"};
        var options=new[] {"Green","Orange","Red","Blue"};
        var factors=new[] {"1.8x","9x","4.5x","2.7x"};
        for(int i=0;i<4;i++)
        {
            rng=new SequenceRandom(i*.25); money._currentBettingOption=options[i];
            string text=Describe(money,rng);
            Check(text.Contains("应押颜色："+colors[i]),"money wheel target color "+options[i]);
            Check(text.Contains("基础返还 "+factors[i]),"money wheel uses fixed color multiplier and machine factor "+options[i]);
            Check(text.Contains("当前选择："+colors[i]+" → 中奖（"),"matching selection wins "+options[i]);
            Check(rng.DoubleCalls==1&&rng.IntCalls==0,"money wheel consumes exactly one detached double");
        }
        money._currentBettingOption="Green";
        Check(Describe(money,new SequenceRandom(.5)).Contains("当前选择：绿色 → 未中奖（基础返还 0x）"),"different current selection loses");
        Check(money._currentBettingOption=="Green"&&money.wheel.wheelTransform.localRotation.value==original&&!money.isPlaying,"money wheel preview never changes selected bet, transform or game");
        Check(Describe(money,new SequenceRandom(.125)).Contains("分界"),"money wheel exact section boundary refuses advice");
        money.wheel.spinDirection=true;
        Check(Describe(money,new SequenceRandom(.25)).Contains("应押颜色：蓝色"),"money wheel reverse direction");
        money.wheel.spinDirection=false; money.wheel._isSpinning=true;
        rng=new SequenceRandom(.5);
        Check(Describe(money,rng).Contains("尚未停稳")&&rng.DoubleCalls==0,"money wheel waits without RNG while not playing but spinning");
        money.wheel._isSpinning=false;
        money.transform.localPosition=new UnityEngine.Vector3(-2,5,7); money.transform.localRotation=UnityEngine.Quaternion.Euler(0,0,-13); money.transform.localScale=new UnityEngine.Vector3(.7f,1.6f,2);
        money.wheel.wheelTransform.localRotation=UnityEngine.Quaternion.Euler(0,0,72);
        Check(Describe(money,new SequenceRandom(.5)).Contains("应押颜色：红色"),"money wheel absolute target with scaled and moved root");
        money.isPlaying=true;
        Check(Describe(money,new SequenceRandom(0)).Contains("未取得"),"money wheel cannot guess an unobserved active spin");
        RaceWheelPredictions.ObserveWheelSpin(money.wheel,1260);
        rng=new SequenceRandom(.25); string moneyActive=Describe(money,rng);
        Check(moneyActive.Contains("中奖颜色：红色")&&!moneyActive.Contains("应押"),"money wheel observed active spin uses locked-bet wording");
        Check(moneyActive.Contains("当前选择：绿色 → 未中奖（基础返还 0x）")&&rng.DoubleCalls==0,"money wheel active cache computes selected loss without another random draw");
        money.wheel.wheelTransform.localRotation=UnityEngine.Quaternion.Euler(0,0,211);
        Check(Describe(money,new SequenceRandom(0)).Contains("中奖颜色：红色"),"money wheel active cached result stable through animation");
        money.wheel.isServer=false; RaceWheelPredictions.ObserveWheelSpin(money.wheel,1080);
        Check(Describe(money,new SequenceRandom(0)).Contains("中奖颜色：红色"),"money wheel client observation cannot overwrite host cache");
        money.wheel.isServer=true; money.gameTurn++;
        Check(Describe(money,new SequenceRandom(0)).Contains("未取得"),"money wheel cannot reuse result in later round");
        money.isPlaying=false; money.wheel._results[2].result="Purple";
        Check(Describe(money,new SequenceRandom(.5)).Contains("无法识别下注颜色"),"money wheel unknown result refuses payout advice");
        money.wheel._results[2].result="Red"; money.wheel.resultSelector.parent=new UnityEngine.Transform();
        Check(Describe(money,new SequenceRandom(.5)).Contains("机器外部"),"money wheel external selector refuses stable prediction");

    }
}
