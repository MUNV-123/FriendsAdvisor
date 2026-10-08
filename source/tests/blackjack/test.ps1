param([string]$ScratchPath = (Join-Path ([IO.Path]::GetTempPath()) 'FriendsAdvisor-BlackjackTests'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'BlackjackHarness.csproj'
$absoluteScratch = [IO.Path]::GetFullPath($ScratchPath)
New-Item -ItemType Directory -Force -Path $absoluteScratch | Out-Null
& dotnet build $project --artifacts-path $absoluteScratch --nologo
if ($LASTEXITCODE -ne 0) { throw 'Blackjack harness compilation failed.' }
$executable = Join-Path $absoluteScratch 'bin\BlackjackHarness\debug\BlackjackHarness.dll'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Blackjack harness output was not found.' }
& dotnet $executable
if ($LASTEXITCODE -ne 0) { throw 'Blackjack prediction checks failed.' }
