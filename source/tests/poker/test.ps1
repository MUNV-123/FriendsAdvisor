param(
    [string]$ScratchPath = (Join-Path ([IO.Path]::GetTempPath()) 'FriendsAdvisor-PokerTests'),
    [string]$GameSourcePath
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PokerHarness.csproj'
$absoluteScratch = [IO.Path]::GetFullPath($ScratchPath)
New-Item -ItemType Directory -Force -Path $absoluteScratch | Out-Null
$buildArguments = @('build', $project, '--artifacts-path', $absoluteScratch, '--nologo')
if ($GameSourcePath) {
    # The optional reference is generated in scratch from the user's local game
    # decompilation. Game code is not shipped in this test project or release.
    $gameSource = Get-Content -LiteralPath $GameSourcePath -Raw
    $start = $gameSource.IndexOf('private int EvaluatePokerHand(')
    $end = $gameSource.IndexOf('private string GetHandDescription(', $start)
    if ($start -lt 0 -or $end -le $start) { throw 'The supported game evaluator was not found.' }
    $methods = $gameSource.Substring($start, $end - $start).Replace('SyncList<CardData>', 'List<CardData>')
    $reference = @"
using System;
using System.Collections.Generic;
using System.Linq;
public class ReferenceEvaluator {
    public int Eval(List<CardData> hand) { return EvaluatePokerHand(hand); }
    public int Pay(int rank) { return rank >= 1 ? GetPayoutMultiplier(rank) : 0; }
$methods
}
"@
    $referencePath = Join-Path $absoluteScratch 'ReferenceEvaluator.cs'
    [IO.File]::WriteAllText($referencePath, $reference, [Text.UTF8Encoding]::new($false))
    $buildArguments += "-p:GameOraclePath=$referencePath"
}
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Poker harness compilation failed.' }
$executable = Join-Path $absoluteScratch 'bin\PokerHarness\debug\PokerHarness.dll'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Poker harness output was not found.' }
& dotnet $executable
if ($LASTEXITCODE -ne 0) { throw 'Poker prediction checks failed.' }
