param(
    [string]$ToolRoot = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))),
    [string]$OriginalAssembly = 'D:\steam\steamapps\common\Gamble With Your Friends\.friends-advisor\original.bak',
    [string]$FixtureParent = (Join-Path ([IO.Path]::GetTempPath()) 'FriendsAdvisorInstallerTests')
)
$ErrorActionPreference = 'Stop'
$expectedOriginal = 'F2147868930F1FC9DF3489EF89339759F7EEB8A92C270BEC094A4ECE6DD2935B'
$v12Hash = '4A8F38C9950ADD6E26B7E43F2A66FA8F234F7F5E89D81EF936EF21E51446A982'
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
$OriginalAssembly = [IO.Path]::GetFullPath($OriginalAssembly)
if ((Get-FileHash -LiteralPath $OriginalAssembly -Algorithm SHA256).Hash -ne $expectedOriginal) { throw '测试必须使用精确受支持的原版程序集；不会修改提供的原文件。' }
$package = Get-Content -LiteralPath (Join-Path $ToolRoot 'advisor-package.json') -Raw | ConvertFrom-Json
if ($package.advisorSha256 -eq $v12Hash) { throw '测试需要已编译的 1.3 工具包，不能用 1.2 工具包测试自身升级。' }
$FixtureParent = [IO.Path]::GetFullPath($FixtureParent)
if (-not (Test-Path -LiteralPath $FixtureParent)) { New-Item -ItemType Directory -Path $FixtureParent | Out-Null }
$fixtureRoot = Join-Path $FixtureParent ('v13_installer_fixture_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$log = Join-Path $fixtureRoot 'test-log.txt'
$script:assertions = 0
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:assertions++
}
function New-Fixture([string]$Name) {
    $path = Join-Path $fixtureRoot $Name
    $managed = Join-Path $path 'Gamble With Your Friends_Data\Managed'
    New-Item -ItemType Directory -Path $managed | Out-Null
    Copy-Item -LiteralPath $OriginalAssembly -Destination (Join-Path $managed 'Assembly-CSharp.dll')
    [IO.File]::WriteAllBytes((Join-Path $path 'Gamble With Your Friends.exe'), [byte[]]@())
    [IO.File]::WriteAllText((Join-Path $managed 'unrelated.txt'), 'keep-me')
    New-Item -ItemType Directory -Path (Join-Path $managed 'unrelated-directory') | Out-Null
    [IO.File]::WriteAllText((Join-Path $managed 'unrelated-directory\nested.txt'), 'keep-nested')
    return $path
}
function Run-Setup([string]$Root, [string]$Command, [string]$Fixture, [bool]$Succeeds = $true) {
    $resolved = [IO.Path]::GetFullPath($Fixture)
    Assert ($resolved.StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) '拒绝向隔离测试根目录以外运行安装器。'
    $tool = Join-Path $Root 'AdvisorSetup.exe'
    $process = New-Object Diagnostics.Process
    $process.StartInfo = New-Object Diagnostics.ProcessStartInfo
    $process.StartInfo.FileName = $tool
    $process.StartInfo.Arguments = $Command + ' "' + $resolved + '"'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
    $process.StartInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
    $null = $process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $output = $stdout.Result + $stderr.Result
    $exitCode = $process.ExitCode
    $process.Dispose()
    Add-Content -LiteralPath $log -Value ($Root + ' / ' + $Command + ' / ' + $Fixture + ' / exit=' + $exitCode + "`r`n" + $output)
    Assert (($exitCode -eq 0) -eq $Succeeds) ('安装器退出码不符：' + $Command + ' / expectedSuccess=' + $Succeeds + "`r`n" + $output)
    return $output
}
function Snapshot([string]$Fixture) {
    $files = Get-ChildItem -LiteralPath $Fixture -Recurse -File | Sort-Object FullName
    return (($files | ForEach-Object { $_.FullName.Substring($Fixture.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join "`n")
}
function Assert-Unrelated([string]$Fixture) {
    $managed = Join-Path $Fixture 'Gamble With Your Friends_Data\Managed'
    Assert ([IO.File]::ReadAllText((Join-Path $managed 'unrelated.txt')) -eq 'keep-me') '无关文件变化。'
    Assert ([IO.File]::ReadAllText((Join-Path $managed 'unrelated-directory\nested.txt')) -eq 'keep-nested') '无关子目录文件变化。'
}
function Assert-Restored([string]$Fixture, [bool]$StateDirectoryRemains = $false) {
    $managed = Join-Path $Fixture 'Gamble With Your Friends_Data\Managed'
    Assert ((Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll') -Algorithm SHA256).Hash -eq $expectedOriginal) '卸载未精确恢复原版。'
    Assert (-not (Test-Path -LiteralPath (Join-Path $managed 'FriendsAdvisor.dll'))) '卸载后助手残留。'
    Assert ((Test-Path -LiteralPath (Join-Path $Fixture '.friends-advisor')) -eq $StateDirectoryRemains) '卸载后状态目录不符。'
    Assert-Unrelated $Fixture
}
function Assert-InstalledHash([string]$Fixture, [string]$Hash) {
    $managed = Join-Path $Fixture 'Gamble With Your Friends_Data\Managed'
    $manifest = Get-Content -LiteralPath (Join-Path $Fixture '.friends-advisor\manifest.json') -Raw | ConvertFrom-Json
    Assert ($manifest.phase -eq 'installed') '安装阶段错误。'
    Assert ($manifest.advisorSha256 -eq $Hash) '安装清单助手哈希错误。'
    Assert ((Get-FileHash -LiteralPath (Join-Path $managed 'FriendsAdvisor.dll') -Algorithm SHA256).Hash -eq $Hash) '已安装助手哈希错误。'
    Assert ((Get-FileHash -LiteralPath (Join-Path $Fixture '.friends-advisor\original.bak') -Algorithm SHA256).Hash -eq $expectedOriginal) '原始备份发生变化。'
    Assert-Unrelated $Fixture
}
function Change-Bytes([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $bytes[0] = $bytes[0] -bxor 1
    [IO.File]::WriteAllBytes($Path, $bytes)
}
function Copy-Package([string]$Name) {
    $root = Join-Path $fixtureRoot ('package-' + $Name)
    New-Item -ItemType Directory -Path $root | Out-Null
    foreach ($relative in @('AdvisorSetup.exe', 'FriendsAdvisor.dll', 'advisor-package.json', 'legacy', 'previous', 'v12')) {
        Copy-Item -LiteralPath (Join-Path $ToolRoot $relative) -Destination $root -Recurse
    }
    return $root
}

$clean = New-Fixture 'clean'
$null = Run-Setup $ToolRoot 'install' $clean
$null = Run-Setup $ToolRoot 'verify' $clean
$null = Run-Setup $ToolRoot 'status' $clean
Assert-InstalledHash $clean $package.advisorSha256
$before = Snapshot $clean
$null = Run-Setup $ToolRoot 'install' $clean
Assert ((Snapshot $clean) -eq $before) '重复安装修改了已安装文件。'
$null = Run-Setup $ToolRoot 'uninstall' $clean
Assert-Restored $clean
$null = Run-Setup $ToolRoot 'uninstall' $clean
Assert-Restored $clean

foreach ($oldDirectory in @('legacy', 'previous', 'v12')) {
    $fixture = New-Fixture ('upgrade-' + $oldDirectory)
    $oldRoot = Join-Path $ToolRoot $oldDirectory
    $null = Run-Setup $oldRoot 'install' $fixture
    $null = Run-Setup $ToolRoot 'verify' $fixture
    $null = Run-Setup $ToolRoot 'status' $fixture
    $null = Run-Setup $ToolRoot 'install' $fixture
    $null = Run-Setup $ToolRoot 'verify' $fixture
    Assert-InstalledHash $fixture $package.advisorSha256
    $before = Snapshot $fixture
    $null = Run-Setup $ToolRoot 'install' $fixture
    Assert ((Snapshot $fixture) -eq $before) '升级后的重复安装修改了文件。'
    $null = Run-Setup $ToolRoot 'uninstall' $fixture
    Assert-Restored $fixture
}

$stateExtra = New-Fixture 'preserve-extra-state'
$null = Run-Setup $ToolRoot 'install' $stateExtra
$extraStateFile = Join-Path $stateExtra '.friends-advisor\user-note.txt'
[IO.File]::WriteAllText($extraStateFile, 'keep-state')
$null = Run-Setup $ToolRoot 'uninstall' $stateExtra
Assert-Restored $stateExtra $true
Assert ([IO.File]::ReadAllText($extraStateFile) -eq 'keep-state') '卸载删除了无关状态文件。'
Assert (-not (Test-Path -LiteralPath (Join-Path $stateExtra '.friends-advisor\manifest.json'))) '卸载后安装清单残留。'
Assert (-not (Test-Path -LiteralPath (Join-Path $stateExtra '.friends-advisor\original.bak'))) '卸载后自有备份残留。'

foreach ($kind in @('payload', 'unknown-manifest-payload', 'backup', 'extra-state')) {
    $fixture = New-Fixture ('reject-installed-' + $kind)
    $null = Run-Setup (Join-Path $ToolRoot 'v12') 'install' $fixture
    $advisor = Join-Path $fixture 'Gamble With Your Friends_Data\Managed\FriendsAdvisor.dll'
    if ($kind -eq 'payload' -or $kind -eq 'unknown-manifest-payload') { Change-Bytes $advisor }
    if ($kind -eq 'unknown-manifest-payload') {
        $manifestPath = Join-Path $fixture '.friends-advisor\manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifest.advisorSha256 = (Get-FileHash -LiteralPath $advisor -Algorithm SHA256).Hash
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json))
    }
    if ($kind -eq 'backup') { Change-Bytes (Join-Path $fixture '.friends-advisor\original.bak') }
    if ($kind -eq 'extra-state') { [IO.File]::WriteAllText((Join-Path $fixture '.friends-advisor\user-note.txt'), 'keep-state') }
    $before = Snapshot $fixture
    $null = Run-Setup $ToolRoot 'install' $fixture $false
    Assert ((Snapshot $fixture) -eq $before) ('被拒绝的升级修改了文件：' + $kind)
    if ($kind -eq 'unknown-manifest-payload') {
        foreach ($command in @('verify', 'status', 'uninstall')) {
            $null = Run-Setup $ToolRoot $command $fixture $false
            Assert ((Snapshot $fixture) -eq $before) ('未知 payload 的命令修改了文件：' + $command)
        }
    }
}

foreach ($kind in @('old-installer', 'old-payload', 'old-package', 'new-payload', 'new-package')) {
    $fixture = New-Fixture ('reject-package-' + $kind)
    $null = Run-Setup (Join-Path $ToolRoot 'v12') 'install' $fixture
    $copy = Copy-Package $kind
    if ($kind -eq 'old-installer') { Change-Bytes (Join-Path $copy 'v12\AdvisorSetup.exe') }
    if ($kind -eq 'old-payload') { Change-Bytes (Join-Path $copy 'v12\FriendsAdvisor.dll') }
    if ($kind -eq 'old-package') {
        $path = Join-Path $copy 'v12\advisor-package.json'
        $bad = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $bad.advisorSha256 = ('0' * 64)
        [IO.File]::WriteAllText($path, ($bad | ConvertTo-Json))
    }
    if ($kind -eq 'new-payload') { Change-Bytes (Join-Path $copy 'FriendsAdvisor.dll') }
    if ($kind -eq 'new-package') {
        $path = Join-Path $copy 'advisor-package.json'
        $bad = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $bad.gameAssemblySha256 = ('0' * 64)
        [IO.File]::WriteAllText($path, ($bad | ConvertTo-Json))
    }
    $before = Snapshot $fixture
    $null = Run-Setup $copy 'install' $fixture $false
    Assert ((Snapshot $fixture) -eq $before) ('工具包预检失败后修改了已有安装：' + $kind)
    $null = Run-Setup $ToolRoot 'verify' $fixture
    Assert-InstalledHash $fixture $v12Hash
}

# Deliberately remove only the isolated package's NEW payload after the old
# installer deletes its manifest. The rollback package remains intact.
if (-not ('FriendsAdvisorUpgradePayloadFault' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
public sealed class FriendsAdvisorUpgradePayloadFault : IDisposable {
    private readonly FileSystemWatcher watcher;
    private readonly string manifest, payload, held;
    public bool WasInjected { get; private set; }
    public string Error { get; private set; }
    public FriendsAdvisorUpgradePayloadFault(string fixture, string payloadPath) {
        manifest = Path.Combine(fixture, ".friends-advisor", "manifest.json");
        payload = payloadPath; held = payloadPath + ".test-held";
        watcher = new FileSystemWatcher(fixture, "manifest.json");
        watcher.IncludeSubdirectories = true;
        watcher.Deleted += (sender, e) => {
            if (!string.Equals(e.FullPath, manifest, StringComparison.OrdinalIgnoreCase) || WasInjected) return;
            try { File.Move(payload, held); WasInjected = true; }
            catch (Exception ex) { Error = ex.Message; }
        };
        watcher.EnableRaisingEvents = true;
    }
    public void Dispose() {
        watcher.EnableRaisingEvents = false; watcher.Dispose();
        if (File.Exists(held)) File.Move(held, payload);
    }
}
'@
}
$rollback = New-Fixture 'rollback-after-old-uninstall'
$null = Run-Setup (Join-Path $ToolRoot 'v12') 'install' $rollback
$copy = Copy-Package 'rollback'
$fault = New-Object FriendsAdvisorUpgradePayloadFault($rollback, (Join-Path $copy 'FriendsAdvisor.dll'))
try {
    $output = Run-Setup $copy 'install' $rollback $false
    Assert $fault.WasInjected ('升级失败注入未执行：' + $fault.Error)
} finally { $fault.Dispose() }
$null = Run-Setup $ToolRoot 'verify' $rollback
Assert-InstalledHash $rollback $v12Hash
$null = Run-Setup $ToolRoot 'install' $rollback
Assert-InstalledHash $rollback $package.advisorSha256
$null = Run-Setup $ToolRoot 'uninstall' $rollback
Assert-Restored $rollback

Write-Output ('PASS: ' + $script:assertions + ' assertions; 1.3 clean/idempotent install; 1.0/1.1/1.2 upgrades; exact restore; unrelated file preservation; tamper refusal; runtime upgrade rollback.')
Write-Output ('Fixtures and complete process output: ' + $fixtureRoot)
