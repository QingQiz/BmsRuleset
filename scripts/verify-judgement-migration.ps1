[CmdletBinding()]
param(
    [string]$ResultsDirectory,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $repoRoot ('artifacts/hitobject-validation/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$production = Join-Path $repoRoot 'osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj'
$tests = Join-Path $repoRoot 'osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj'
$settings = Join-Path $repoRoot 'osu.Game.Rulesets.BmsRuleset.Tests/judgement-instrumentation.runsettings'

if (-not $SkipBuild) {
    foreach ($project in @($production, $tests)) {
        & dotnet build $project -c Release --no-restore -p:BuildProjectReferences=false -v:quiet
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
}

$patterns = @(
    '.Normal.Gameplay.', '.Normal.Mods.', '.Normal.Replay.', '.Normal.Localisation.', '.Normal.AutoGeneration.',
    'BmsKeySoundCursorTest', 'BmsScoreSelectionModTest', 'BmsAudioVolumeRoutingTest', 'BmsSamplePlaybackTest'
)
$scenes = @(
    'AutoScratchInput', 'Beatoraja', 'CautionAutoplay', 'ChargeNoteExecution', 'ColumnJudgement', 'CoreSchedulingGeneration',
    'DenseReplay', 'GitComparison', 'HcnTailVisuals', 'HellChargeNoteExecution', 'HitExplosionLimit',
    'InitialBackgroundAudio', 'InvertGameplay', 'InvisibleNote', 'JudgementAlgorithm', 'JudgementCheckpoints',
    'JudgementIndexes', 'JudgementLifecycleExecution', 'LandmineKeyboardInput', 'LongNoteBodyTint',
    'LongNoteExecution', 'LongNoteJudgement', 'LongNoteLifecycle', 'MineCulling', 'Mods', 'NoteExecution',
    'PauseRewind', 'ReplayRewind', 'ShortLnAutoplay', 'Skins', 'VisualCulling'
)
$patterns += $scenes | ForEach-Object { 'TestSceneBms' + $_ }
$scope = '(' + (($patterns | ForEach-Object { 'FullyQualifiedName~' + $_ }) -join '|') + ')&FullyQualifiedName!~.Performance.'
$commands = [System.Collections.Generic.List[object]]::new()
$summary = [System.Collections.Generic.List[object]]::new()
$previousComparison = $env:BMS_COMPARISON_RESULTS
$previousNoInline = $env:COMPlus_JitNoInline
$previousDotnetNoInline = $env:DOTNET_JitNoInline
try {
    # Only the instrumented child process changes inlining. Benchmarks never enter this script.
    $env:COMPlus_JitNoInline = $null
    $env:DOTNET_JitNoInline = $null
    $env:BMS_COMPARISON_RESULTS = Join-Path $ResultsDirectory 'comparison/current'
    foreach ($group in @('default', 'instrumentation')) {
        $category = if ($group -eq 'default') { 'TestCategory!=JudgementInstrumentation' } else { 'TestCategory=JudgementInstrumentation' }
        $arguments = @('test', $tests, '-c', 'Release', '--no-build', '--no-restore', '--filter', "$scope&$category",
            '--logger', "trx;LogFileName=$group.trx", '--results-directory', $ResultsDirectory)
        if ($group -eq 'instrumentation') { $arguments += @('--settings', $settings) }
        $commands.Add(@{ Group = $group; Arguments = $arguments })
        $commands | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ResultsDirectory 'commands.json') -Encoding utf8
        Write-Output "Running $group judgement checks..."
        $reportPath = Join-Path $ResultsDirectory "$group.trx"
        if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath }
        & dotnet @arguments *> (Join-Path $ResultsDirectory "$group.log")
        $exitCode = $LASTEXITCODE
        if (-not (Test-Path -LiteralPath $reportPath)) { throw "Missing test results: $reportPath" }
        $report = [xml](Get-Content -LiteralPath $reportPath -Raw)
        $counts = $report.TestRun.ResultSummary.Counters
        $summary.Add(@{ Group = $group; Total = [int]$counts.total; Passed = [int]$counts.passed;
            Failed = [int]$counts.failed; Skipped = [int]$counts.notExecuted; ExitCode = $exitCode })
        $summary | ConvertTo-Json | Set-Content (Join-Path $ResultsDirectory 'summary.json') -Encoding utf8
        if ($exitCode -ne 0 -or [int]$counts.total -eq 0 -or [int]$counts.passed -ne [int]$counts.total) {
            throw "Incomplete $group verification; see $reportPath"
        }
        Write-Output "$group`: $($counts.passed) passed, no failures or skips."
    }
}
finally {
    $env:BMS_COMPARISON_RESULTS = $previousComparison
    $env:COMPlus_JitNoInline = $previousNoInline
    $env:DOTNET_JitNoInline = $previousDotnetNoInline
}
Write-Output "Results: $ResultsDirectory"
