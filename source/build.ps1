param([string]$GamePath = 'D:\steam\steamapps\common\Gamble With Your Friends')
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GamePath 'Gamble With Your Friends_Data\Managed'
$outputDir = Split-Path -Parent $PSScriptRoot
$sdkList = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { throw '需要 .NET SDK 来重新编译源码。' }
$lastSdk = $sdkList | Select-Object -Last 1
if ($lastSdk -notmatch '^([^ ]+) \[(.+)\]') { throw '找不到 .NET SDK。' }
$compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
$refs = @('mscorlib','System','System.Core','netstandard','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.TextRenderingModule','UnityEngine.InputLegacyModule','UnityEngine.PhysicsModule','UnityEngine.UI','Mirror','Assembly-CSharp')
$arguments = @($compiler, '/nologo', '/target:library', '/optimize+', '/nostdlib+', '/langversion:7.3', ('/out:' + (Join-Path $outputDir 'FriendsAdvisor.dll')))
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $managed ($ref + '.dll')) }
$arguments += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Host 'FriendsAdvisor.dll 编译完成。重新编译后需要刷新安装包中的 SHA256 校验值。'
