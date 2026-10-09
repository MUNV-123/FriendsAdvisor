using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private const string DefaultGame = @"D:\steam\steamapps\common\Gamble With Your Friends";
    private const string ExpectedOriginal = "F2147868930F1FC9DF3489EF89339759F7EEB8A92C270BEC094A4ECE6DD2935B";
    private const string GameExe = "Gamble With Your Friends.exe";
    private const string DataDirectory = "Gamble With Your Friends_Data";
    private const string AdvisorFile = "FriendsAdvisor.dll";
    private const string HookType = "SteamManager";
    private const string HookMethod = "Awake";
    private const string BootstrapSignature = "System.Void FriendsAdvisor.Bootstrap::Initialize()";
    private const string ObservationSignature = "System.Void FriendsAdvisor.Bootstrap::ObserveCrash(System.Object,System.Single)";
    private const string LegacyHookDescription = "SteamManager.Awake;Crash.RaiseRoutine(System.Single)";
    private const string PreviousHookDescription = LegacyHookDescription + ";Wheel.RpcSpinWheel;RouletteWheel.RpcSpinBallWheel;DuckRaceDuck.RpcStep;SlotReel.Start;SlotReel.Finish";
    private const string HookDescription = LegacyHookDescription + ";Wheel.RpcSpinWheel;RouletteWheel.RpcSpinBallWheel";
    private sealed record ExtraHook(string Type, string Method, string Observer, string[] Parameters);
    private sealed record OldPackage(string Directory, string InstallerHash, string AdvisorHash);
    private static readonly OldPackage Legacy = new("legacy", "2EC8EE31E9D99B4F406A97062E667A48C59CA4C25BC4DDA0C5F6C08B47F7C515", "F0B031ED62C29C7C9FBA1EED5A24B529210CB063EF497F277B175280B3BE1FE0");
    private static readonly OldPackage Previous = new("previous", "262BDB6FA769DC3FE4868569B02D13267ED9E368DFD835D718706342FD9B8A8D", "621BA40C63CAD5D292678C51A2CDF8B762322489A47B9D477F52354BFC25F84A");
    private static readonly OldPackage Version12 = new("v12", "50965C5E4C7B96F7017E3EB7761C0F2DB842F90E193895F8DC3971862510A0E8", "4A8F38C9950ADD6E26B7E43F2A66FA8F234F7F5E89D81EF936EF21E51446A982");
    private static readonly ExtraHook[] ExtraHooks = {
        new("Wheel", "RpcSpinWheel", "ObserveWheelSpin", new[] { "System.Single", "System.Single" }),
        new("RouletteWheel", "RpcSpinBallWheel", "ObserveRouletteBallSpin", new[] { "System.Single", "System.Single" })
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private sealed class Package
    {
        public int SchemaVersion { get; set; }
        public string GameAssemblySha256 { get; set; } = "";
        public string AdvisorSha256 { get; set; } = "";
    }

    private sealed class Manifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string Phase { get; set; } = "prepared";
        public string OriginalSha256 { get; set; } = ExpectedOriginal;
        public string PatchedSha256 { get; set; } = "";
        public string AdvisorSha256 { get; set; } = "";
        public string Hook { get; set; } = HookDescription;
        public string Bootstrap { get; set; } = BootstrapSignature;
        public string Observation { get; set; } = ObservationSignature;
        public string InstalledUtc { get; set; } = DateTime.UtcNow.ToString("O");
    }

    private sealed class Layout
    {
        public string Game { get; }
        public string Managed { get; }
        public string Assembly { get; }
        public string Advisor { get; }
        public string StateDirectory { get; }
        public string State { get; }
        public string Backup { get; }
        public Layout(string game)
        {
            Game = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Managed = Path.Combine(Game, DataDirectory, "Managed");
            Assembly = Path.Combine(Managed, "Assembly-CSharp.dll");
            Advisor = Path.Combine(Managed, AdvisorFile);
            StateDirectory = Path.Combine(Game, ".friends-advisor");
            State = Path.Combine(StateDirectory, "manifest.json");
            Backup = Path.Combine(StateDirectory, "original.bak");
            if (!File.Exists(Path.Combine(Game, GameExe)) || !File.Exists(Assembly))
                throw new InvalidOperationException("目录不是受支持的游戏安装目录（未找到游戏程序或 Assembly-CSharp.dll）。");
            RejectReparsePoint(Game);
            RejectReparsePoint(Path.Combine(Game, DataDirectory));
            RejectReparsePoint(Managed);
            if (Directory.Exists(StateDirectory)) RejectReparsePoint(StateDirectory);
            RejectReparsePoint(Assembly);
            if (File.Exists(Advisor)) RejectReparsePoint(Advisor);
            if (File.Exists(State)) RejectReparsePoint(State);
            if (File.Exists(Backup)) RejectReparsePoint(Backup);
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length is < 1 or > 2 || !new[] { "install", "uninstall", "status", "verify" }.Contains(args[0].ToLowerInvariant()))
            {
                Console.WriteLine("用法：AdvisorSetup.exe install|uninstall|status|verify [游戏目录]");
                Console.WriteLine("默认目录：" + DefaultGame);
                return 2;
            }
            var layout = new Layout(args.Length == 2 ? args[1] : DefaultGame);
            var existing = File.Exists(layout.State) ? ReadJson<Manifest>(layout.State) : null;
            // 1.2 and 1.3 share the same hooks. Identify 1.2 by its released payload,
            // never by the hook list alone; an unknown payload must not be upgraded as 1.2.
            var oldPackage = existing?.Hook == LegacyHookDescription ? Legacy
                : existing?.Hook == PreviousHookDescription ? Previous
                : existing?.Hook == HookDescription && Equal(existing.AdvisorSha256, Version12.AdvisorHash) ? Version12 : null;
            if (oldPackage != null)
            {
                if (!Equal(existing!.AdvisorSha256, oldPackage.AdvisorHash))
                    throw new InvalidOperationException("安装清单记录的旧版助手哈希不受支持，已停止。");
                if (args[0].ToLowerInvariant() == "install") UpgradeOld(layout, oldPackage);
                else RunOld(args[0].ToLowerInvariant(), layout, oldPackage);
                return 0;
            }
            if (existing?.Hook == HookDescription)
            {
                var package = ReadPackage(AppContext.BaseDirectory, "当前");
                if (!Equal(existing.AdvisorSha256, package.AdvisorSha256))
                    throw new InvalidOperationException("已安装的助手不是此工具包或已知的 1.2 版本，已停止；请使用原工具包恢复。");
            }
            switch (args[0].ToLowerInvariant())
            {
                case "install": Install(layout); break;
                case "uninstall": Uninstall(layout); break;
                case "status": Status(layout); break;
                case "verify": Verify(layout); break;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("失败：" + ex.Message);
            return 1;
        }
    }

    private static void RunOld(string command, Layout l, OldPackage old)
    {
        var legacy = Path.Combine(AppContext.BaseDirectory, old.Directory, "AdvisorSetup.exe");
        RequireHash(legacy, old.InstallerHash, "旧版恢复工具");
        var start = new ProcessStartInfo(legacy) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(l.Game);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法运行旧版恢复工具。");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("旧版恢复工具检查未通过，没有继续更新。");
    }

    private static void UpgradeOld(Layout l, OldPackage old)
    {
        RequireStopped();
        var payload = Path.Combine(AppContext.BaseDirectory, AdvisorFile);
        var package = ReadPackage(AppContext.BaseDirectory, "新版");
        RequireHash(payload, package.AdvisorSha256, "新版助手");
        ValidatePayload(payload);
        var oldDirectory = Path.Combine(AppContext.BaseDirectory, old.Directory);
        var oldManifest = ReadPackage(oldDirectory, "旧版回退");
        if (!Equal(oldManifest.AdvisorSha256, old.AdvisorHash))
            throw new InvalidOperationException("旧版回退工具包清单与已知版本不同，已停止。");
        RequireHash(Path.Combine(oldDirectory, "AdvisorSetup.exe"), old.InstallerHash, "旧版恢复工具");
        RequireHash(Path.Combine(oldDirectory, AdvisorFile), old.AdvisorHash, "旧版回退助手");
        RequireHash(l.Backup, ExpectedOriginal, "更新前原始备份");
        RequireHash(l.Advisor, old.AdvisorHash, "更新前旧版助手");
        // Old installers preserve unrelated state files, but a subsequent install
        // would refuse that directory. Detect this before uninstalling anything.
        if (Directory.EnumerateFileSystemEntries(l.StateDirectory).Any(path =>
            !string.Equals(path, l.State, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, l.Backup, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(".friends-advisor 中存在其他文件，请先将这些文件移出再更新；已有助手和所有文件均保留。");
        // Prepare and validate the complete new patch before uninstalling the old version.
        var stage = StageName(l.Managed, "upgradecheck");
        try { Patch(l.Backup, payload, stage); ValidatePatch(stage); }
        finally { DeleteOwnStage(stage); }
        RunOld("verify", l, old);
        try
        {
            RunOld("uninstall", l, old);
            Install(l);
        }
        catch
        {
            try
            {
                if (File.Exists(l.State))
                {
                    var interrupted = ReadJson<Manifest>(l.State);
                    if (Equal(interrupted.AdvisorSha256, old.AdvisorHash)) RunOld("uninstall", l, old);
                    else if (Equal(interrupted.AdvisorSha256, package.AdvisorSha256)) Uninstall(l);
                    else throw new InvalidOperationException("恢复时发现未知安装清单，拒绝覆盖。");
                }
                RunOld("install", l, old);
                Console.Error.WriteLine("新版更新未完成，已恢复原先可用的旧版助手。");
            }
            catch (Exception restore) { Console.Error.WriteLine("更新恢复未完成：" + restore.Message + "。原工具包保留在 " + old.Directory + " 目录。"); }
            throw;
        }
        Console.WriteLine("已从旧版更新为 1.3，原版游戏备份仍已校验保存。");
    }

    private static void Install(Layout l)
    {
        RequireStopped();
        var payload = Path.Combine(AppContext.BaseDirectory, AdvisorFile);
        var package = ReadPackage(AppContext.BaseDirectory, "工具包");
        RequireHash(payload, package.AdvisorSha256, "工具包 FriendsAdvisor.dll");
        ValidatePayload(payload);
        if (File.Exists(l.State))
        {
            var existing = ReadManifest(l);
            if (!Equal(existing.AdvisorSha256, package.AdvisorSha256))
                throw new InvalidOperationException("已有其他版本的助手，请先使用原工具包卸载。");
            Verify(l);
            Console.WriteLine("此版本已安装，无需重复安装。");
            return;
        }
        if (File.Exists(l.Advisor) || Directory.Exists(l.StateDirectory) || File.Exists(l.StateDirectory))
            throw new InvalidOperationException("发现未记录的 FriendsAdvisor.dll 或 .friends-advisor，已停止，避免覆盖现有文件。");
        RequireHash(l.Assembly, ExpectedOriginal, "游戏程序集（游戏可能已更新或已有其他修改）");

        string assemblyStage = StageName(l.Managed, "assembly");
        string advisorStage = StageName(l.Managed, "advisor");
        bool journalWritten = false;
        bool directoryCreated = false;
        try
        {
            Patch(l.Assembly, payload, assemblyStage);
            var patchedHash = Hash(assemblyStage);
            ValidatePatch(assemblyStage);
            CopyNew(payload, advisorStage);
            RequireHash(advisorStage, package.AdvisorSha256, "助手暂存文件");
            RequireStopped();
            RequireHash(l.Assembly, ExpectedOriginal, "安装前游戏程序集");
            if (File.Exists(l.Advisor) || Directory.Exists(l.StateDirectory) || File.Exists(l.StateDirectory))
                throw new InvalidOperationException("安装目标在准备期间发生变化，已停止。");
            Directory.CreateDirectory(l.StateDirectory);
            directoryCreated = true;
            CopyNew(l.Assembly, l.Backup);
            RequireHash(l.Backup, ExpectedOriginal, "原始备份");
            var manifest = new Manifest { PatchedSha256 = patchedHash, AdvisorSha256 = package.AdvisorSha256 };
            WriteJsonNew(l.State, manifest);
            journalWritten = true;
            File.Move(advisorStage, l.Advisor, false);
            // 替换单个程序集是原子的；prepared 清单允许中断后按哈希恢复。
            File.Replace(assemblyStage, l.Assembly, null);
            manifest.Phase = "installed";
            ReplaceJson(l.State, manifest);
            Verify(l);
            Console.WriteLine("安装完成。启动游戏后可使用 F8 显示或隐藏助手。");
        }
        catch
        {
            if (journalWritten)
            {
                Console.Error.WriteLine("安装未完成，保留了原始备份与恢复清单。请运行 uninstall 恢复；不会覆盖哈希不一致的文件。");
            }
            else if (directoryCreated)
            {
                if (File.Exists(l.Backup) && Equal(Hash(l.Backup), ExpectedOriginal)) File.Delete(l.Backup);
                if (!Directory.EnumerateFileSystemEntries(l.StateDirectory).Any()) Directory.Delete(l.StateDirectory);
            }
            throw;
        }
        finally
        {
            DeleteOwnStage(assemblyStage);
            DeleteOwnStage(advisorStage);
        }
    }

    private static void Uninstall(Layout l)
    {
        RequireStopped();
        if (!File.Exists(l.State))
        {
            if (!File.Exists(l.Advisor) && !Directory.Exists(l.StateDirectory) && Equal(Hash(l.Assembly), ExpectedOriginal))
            {
                Console.WriteLine("助手未安装，游戏程序集与受支持原版一致。");
                return;
            }
            throw new InvalidOperationException("未找到有效安装清单，无法安全判断哪些文件属于助手；没有修改游戏。");
        }
        var m = ReadManifest(l);
        if (m.Phase != "restored" || File.Exists(l.Backup)) RequireHash(l.Backup, m.OriginalSha256, "原始备份");
        var current = Hash(l.Assembly);
        if (!Equal(current, m.OriginalSha256) && !Equal(current, m.PatchedSha256))
            throw new InvalidOperationException("游戏程序集已被更新或再次修改，拒绝用旧备份覆盖。原始备份仍保留在 .friends-advisor/original.bak。");
        if (File.Exists(l.Advisor)) RequireHash(l.Advisor, m.AdvisorSha256, "已安装助手（文件已变化，拒绝删除）");
        if (m.Phase == "restored" && (!Equal(current, m.OriginalSha256) || File.Exists(l.Advisor)))
            throw new InvalidOperationException("已恢复清单与当前文件状态不一致，已停止。");
        RequireStopped();
        if (Equal(current, m.PatchedSha256))
        {
            ValidatePatch(l.Assembly);
            var restoreStage = StageName(l.Managed, "restore");
            try
            {
                CopyNew(l.Backup, restoreStage);
                RequireHash(restoreStage, ExpectedOriginal, "恢复暂存文件");
                RequireHash(l.Assembly, current, "恢复前游戏程序集");
                File.Replace(restoreStage, l.Assembly, null);
            }
            finally { DeleteOwnStage(restoreStage); }
        }
        RequireHash(l.Assembly, ExpectedOriginal, "已恢复游戏程序集");
        if (File.Exists(l.Advisor))
        {
            RequireHash(l.Advisor, m.AdvisorSha256, "卸载前助手");
            File.Delete(l.Advisor);
        }
        m.Phase = "restored";
        ReplaceJson(l.State, m);
        if (File.Exists(l.Backup))
        {
            RequireHash(l.Backup, ExpectedOriginal, "删除前备份");
            File.Delete(l.Backup);
        }
        File.Delete(l.State);
        if (!Directory.EnumerateFileSystemEntries(l.StateDirectory).Any()) Directory.Delete(l.StateDirectory);
        Console.WriteLine("卸载完成。Assembly-CSharp.dll 已按 SHA-256 恢复为原始文件。");
        if (Directory.Exists(l.StateDirectory)) Console.WriteLine(".friends-advisor 中存在其他文件，已保留。");
    }

    private static void Status(Layout l)
    {
        Console.WriteLine("游戏目录：" + l.Game);
        Console.WriteLine("游戏运行中：" + (IsRunning() ? "是" : "否"));
        Console.WriteLine("当前程序集 SHA-256：" + Hash(l.Assembly));
        if (!File.Exists(l.State))
        {
            if (File.Exists(l.Advisor) || Directory.Exists(l.StateDirectory))
                throw new InvalidOperationException("存在未记录的助手文件，状态异常。");
            if (!Equal(Hash(l.Assembly), ExpectedOriginal))
                throw new InvalidOperationException("未安装助手，但游戏程序集不是此工具支持的版本。");
            Console.WriteLine("助手状态：未安装；版本受支持。");
            return;
        }
        var m = ReadManifest(l);
        Console.WriteLine("安装状态：" + (m.Phase == "installed" ? "已安装" : m.Phase == "restored" ? "已恢复，等待清理（运行 uninstall）" : "安装中断，需要卸载恢复"));
        Console.WriteLine("原始备份：" + l.Backup);
        Console.WriteLine("助手 SHA-256：" + m.AdvisorSha256);
        if (m.Phase == "installed") Verify(l);
    }

    private static void Verify(Layout l)
    {
        var m = ReadManifest(l);
        if (m.Phase != "installed") throw new InvalidOperationException("安装尚未完成，请运行 uninstall 恢复后再安装。");
        RequireHash(l.Backup, m.OriginalSha256, "原始备份");
        RequireHash(l.Assembly, m.PatchedSha256, "已安装游戏程序集");
        RequireHash(l.Advisor, m.AdvisorSha256, "已安装助手");
        ValidatePayload(l.Advisor);
        ValidatePatch(l.Assembly);
        // 重建预期补丁并逐字节校验，避免仅相信安装清单中记录的补丁哈希。
        using var expected = new MemoryStream();
        PatchTo(l.Backup, l.Advisor, expected);
        var rebuiltHash = Convert.ToHexString(SHA256.HashData(expected.ToArray()));
        if (!Equal(rebuiltHash, m.PatchedSha256))
            throw new InvalidOperationException("补丁与原始备份重建结果不同；没有修改文件。");
        Console.WriteLine("校验通过：原始备份、助手、启动调用与程序集补丁均一致。");
    }

    private static Manifest ReadManifest(Layout l)
    {
        var m = ReadJson<Manifest>(l.State);
        if (m.SchemaVersion != 1 || m.Phase is not ("prepared" or "installed" or "restored") || !Equal(m.OriginalSha256, ExpectedOriginal)
            || !IsHash(m.PatchedSha256) || Equal(m.PatchedSha256, ExpectedOriginal) || !IsHash(m.AdvisorSha256)
            || m.Hook != HookDescription || m.Bootstrap != BootstrapSignature || m.Observation != ObservationSignature)
            throw new InvalidOperationException("安装清单内容不符合此版本，已停止。");
        return m;
    }

    private static void Patch(string original, string payload, string destination)
    {
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        PatchTo(original, payload, output);
    }

    private static void PatchTo(string original, string payload, Stream output)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(original, new ReaderParameters { InMemory = true });
        using var advisor = AssemblyDefinition.ReadAssembly(payload, new ReaderParameters { InMemory = true });
        var module = assembly.MainModule;
        var target = FindHook(module);
        var crashTarget = FindCrashHook(module);
        if (module.AssemblyReferences.Any(x => x.Name == "FriendsAdvisor"))
            throw new InvalidOperationException("原始程序集已引用 FriendsAdvisor，不能重复修改。");
        if (assembly.Name.HasPublicKey) throw new InvalidOperationException("此工具不能修改强名称签名程序集。");
        var reference = new AssemblyNameReference(advisor.Name.Name, advisor.Name.Version)
        {
            Culture = advisor.Name.Culture,
            PublicKeyToken = advisor.Name.PublicKeyToken
        };
        module.AssemblyReferences.Add(reference);
        var bootstrapType = new TypeReference("FriendsAdvisor", "Bootstrap", module, reference, false);
        // 使用目标游戏的 TypeSystem.Void，避免带入开发机的 System.Runtime 引用。
        var initialize = new MethodReference("Initialize", module.TypeSystem.Void, bootstrapType) { HasThis = false };
        target.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, initialize));
        var observe = new MethodReference("ObserveCrash", module.TypeSystem.Void, bootstrapType) { HasThis = false };
        observe.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        observe.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        crashTarget.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
        crashTarget.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Ldarg_1));
        crashTarget.Body.Instructions.Insert(2, Instruction.Create(OpCodes.Call, observe));
        foreach (var hook in ExtraHooks)
        {
            var method = FindExtraHook(module, hook);
            var observer = CreateObserver(module, bootstrapType, hook);
            method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Ldarg_1));
            method.Body.Instructions.Insert(2, Instruction.Create(OpCodes.Call, observer));
        }
        assembly.Write(output);
    }

    private static MethodReference CreateObserver(ModuleDefinition module, TypeReference bootstrap, ExtraHook hook)
    {
        var method = new MethodReference(hook.Observer, module.TypeSystem.Void, bootstrap) { HasThis = false };
        method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        return method;
    }

    private static MethodDefinition FindExtraHook(ModuleDefinition module, ExtraHook hook)
    {
        var type = module.Types.Single(x => x.FullName == hook.Type);
        var method = type.Methods.Single(x => x.Name == hook.Method && !x.IsStatic && x.ReturnType.FullName == "System.Void"
            && x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(hook.Parameters));
        if (!method.HasBody) throw new InvalidOperationException("观察目标缺少代码。");
        return method;
    }

    private static MethodDefinition FindCrashHook(ModuleDefinition module)
    {
        var type = module.Types.SingleOrDefault(x => x.FullName == "Crash")
            ?? throw new InvalidOperationException("未找到 Crash，游戏版本不受支持。");
        var methods = type.Methods.Where(x => x.Name == "RaiseRoutine" && !x.IsStatic
            && x.ReturnType.FullName == "System.Collections.IEnumerator"
            && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.FullName == "System.Single").ToArray();
        if (methods.Length != 1 || !methods[0].HasBody || methods[0].Body.Instructions.Count == 0)
            throw new InvalidOperationException("Crash.RaiseRoutine 签名不符合受支持版本。");
        return methods[0];
    }

    private static MethodDefinition FindHook(ModuleDefinition module)
    {
        var type = module.Types.SingleOrDefault(x => x.FullName == HookType)
            ?? throw new InvalidOperationException("未找到 SteamManager，游戏版本不受支持。");
        var methods = type.Methods.Where(x => x.Name == HookMethod && !x.HasParameters && x.ReturnType.FullName == "System.Void" && !x.IsStatic).ToArray();
        if (methods.Length != 1 || !methods[0].HasBody || methods[0].Body.Instructions.Count == 0)
            throw new InvalidOperationException("SteamManager.Awake 签名不符合受支持版本。");
        return methods[0];
    }

    private static void ValidatePayload(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到助手 DLL：" + path);
        using var a = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
        if (a.Name.Name != "FriendsAdvisor" || a.Name.HasPublicKey)
            throw new InvalidOperationException("助手 DLL 的程序集名称或签名不正确。");
        var t = a.MainModule.Types.SingleOrDefault(x => x.FullName == "FriendsAdvisor.Bootstrap");
        var methods = t?.Methods.Where(x => x.IsPublic && x.IsStatic && !x.HasParameters && x.Name == "Initialize" && x.ReturnType.FullName == "System.Void").ToArray();
        if (t is null || !t.IsPublic || methods is null || methods.Length != 1)
            throw new InvalidOperationException("助手缺少 public static void FriendsAdvisor.Bootstrap.Initialize()。");
        var observers = t.Methods.Where(x => x.IsPublic && x.IsStatic && x.Name == "ObserveCrash" && x.ReturnType.FullName == "System.Void"
            && x.Parameters.Count == 2 && x.Parameters[0].ParameterType.FullName == "System.Object"
            && x.Parameters[1].ParameterType.FullName == "System.Single").ToArray();
        if (observers.Length != 1)
            throw new InvalidOperationException("助手缺少 public static void FriendsAdvisor.Bootstrap.ObserveCrash(object, float)。");
        foreach (var hook in ExtraHooks)
        {
            var types = new[] { "System.Object", "System.Single" };
            if (t.Methods.Count(x => x.IsPublic && x.IsStatic && x.Name == hook.Observer && x.ReturnType.FullName == "System.Void"
                && x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(types)) != 1)
                throw new InvalidOperationException("助手缺少观察方法 " + hook.Observer);
        }
        if (a.MainModule.AssemblyReferences.Any(x => x.Name is "System.Private.CoreLib" or "System.Runtime"))
            throw new InvalidOperationException("助手错误引用了桌面 .NET 运行时，不能用于 Unity Mono。");
        if (!a.MainModule.AssemblyReferences.Any(x => x.Name == "mscorlib"))
            throw new InvalidOperationException("助手未引用 Unity Mono 的 mscorlib。");
    }

    private static void ValidatePatch(string path)
    {
        using var a = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
        var method = FindHook(a.MainModule);
        var crash = FindCrashHook(a.MainModule);
        var calls = a.MainModule.Types.SelectMany(AllTypes).SelectMany(x => x.Methods).Where(x => x.HasBody)
            .SelectMany(x => x.Body.Instructions).Where(x => x.Operand is MethodReference mr && mr.DeclaringType.FullName == "FriendsAdvisor.Bootstrap").ToArray();
        var first = method.Body.Instructions[0];
        if (calls.Length != 4 || !calls.Any(x => ReferenceEquals(x, first)) || first.OpCode != OpCodes.Call
            || first.Operand is not MethodReference m || m.FullName != BootstrapSignature || m.HasThis || m.HasParameters
            || m.DeclaringType.Scope is not AssemblyNameReference ar || ar.Name != "FriendsAdvisor")
            throw new InvalidOperationException("游戏补丁的启动调用与受支持版本不一致。");
        var crashInstructions = crash.Body.Instructions;
        if (crashInstructions.Count < 4 || crashInstructions[0].OpCode != OpCodes.Ldarg_0 || crashInstructions[1].OpCode != OpCodes.Ldarg_1
            || crashInstructions[2].OpCode != OpCodes.Call || !calls.Any(x => ReferenceEquals(x, crashInstructions[2]))
            || crashInstructions[2].Operand is not MethodReference observer || observer.FullName != ObservationSignature || observer.HasThis
            || observer.DeclaringType.Scope is not AssemblyNameReference observerAssembly || observerAssembly.Name != "FriendsAdvisor")
            throw new InvalidOperationException("游戏补丁的 Crash 观察调用与受支持版本不一致。");
        foreach (var hook in ExtraHooks)
        {
            var target = FindExtraHook(a.MainModule, hook);
            var observerCall = target.Body.Instructions.SingleOrDefault(x => x.Operand is MethodReference mr && mr.DeclaringType.FullName == "FriendsAdvisor.Bootstrap" && mr.Name == hook.Observer);
            if (observerCall == null) throw new InvalidOperationException("缺少观察钩子 " + hook.Observer);
            var observerReference = (MethodReference)observerCall.Operand;
            var expected = CreateObserver(a.MainModule, observerReference.DeclaringType, hook);
            if (observerCall.OpCode != OpCodes.Call || observerReference.FullName != expected.FullName || observerReference.HasThis
                || observerReference.DeclaringType.Scope is not AssemblyNameReference scope || scope.Name != "FriendsAdvisor")
                throw new InvalidOperationException("观察钩子签名不匹配 " + hook.Observer);
            int start = target.Body.Instructions.IndexOf(observerCall) - 2;
            if (start != 0 || target.Body.Instructions[start].OpCode != OpCodes.Ldarg_0
                || target.Body.Instructions[start + 1].OpCode != OpCodes.Ldarg_1)
                throw new InvalidOperationException("观察钩子参数不匹配。");
        }
        if (a.MainModule.AssemblyReferences.Count(x => x.Name == "FriendsAdvisor") != 1)
            throw new InvalidOperationException("FriendsAdvisor 引用数量异常。");
    }

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition t)
    {
        yield return t;
        foreach (var child in t.NestedTypes.SelectMany(AllTypes)) yield return child;
    }

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到校验清单：" + path);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidOperationException("无法读取校验清单。");
    }

    private static Package ReadPackage(string directory, string label)
    {
        var package = ReadJson<Package>(Path.Combine(directory, "advisor-package.json"));
        if (package.SchemaVersion != 1 || !Equal(package.GameAssemblySha256, ExpectedOriginal) || !IsHash(package.AdvisorSha256))
            throw new InvalidOperationException(label + "校验清单不完整或版本不匹配，请重新取得完整工具包。");
        return package;
    }

    private static void WriteJsonNew<T>(string path, T data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, data, JsonOptions);
        stream.Flush(true);
    }

    private static void ReplaceJson<T>(string path, T data)
    {
        var stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteJsonNew(stage, data); File.Replace(stage, path, null); }
        finally { DeleteOwnStage(stage); }
    }

    private static string StageName(string directory, string kind) => Path.Combine(directory, ".FriendsAdvisor." + kind + "." + Guid.NewGuid().ToString("N") + ".tmp");
    private static void DeleteOwnStage(string path) { if (File.Exists(path)) File.Delete(path); }
    private static void CopyNew(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(true);
    }
    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static void RequireHash(string path, string expected, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(label + "不存在：" + path);
        if (!Equal(Hash(path), expected)) throw new InvalidOperationException(label + "的 SHA-256 不匹配，已停止。");
    }
    private static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe));
        try { return processes.Length != 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private static void RequireStopped()
    {
        if (IsRunning()) throw new InvalidOperationException("请先完全退出 Gamble With Your Friends，再安装或卸载助手。");
    }
    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("目标包含链接或重定向路径，已停止：" + path);
    }
}
