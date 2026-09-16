[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Cases,
    [Parameter(Mandatory)][string] $Output,
    [string] $Assembly = (Join-Path $PSScriptRoot '../osu.Game.Rulesets.BmsRuleset.Tests/bin/Release/net8.0/osu.Game.Rulesets.BmsRuleset.Tests.dll')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$caseFile = (Resolve-Path -LiteralPath $Cases).Path
$assemblyFile = (Resolve-Path -LiteralPath $Assembly).Path
$outputRoot = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputRoot) {
    throw 'Output must be a new directory to preserve previous measurements.'
}

function Get-Setting($Item, [string] $Name, $Default) {
    $property = $Item.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

$spec = Get-Content -LiteralPath $caseFile -Raw | ConvertFrom-Json
if ($spec.version -ne 1 -or @($spec.cases).Count -eq 0) { throw 'Expected a version 1 manifest with at least one case.' }
$plans = @()
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($case in $spec.cases) {
    if ($case.name -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_-]*$' -or !$names.Add($case.name)) { throw 'Case names must be unique path-safe identifiers.' }
    $chart = [string]$case.chart
    if (![IO.Path]::IsPathRooted($chart)) { $chart = Join-Path (Split-Path $caseFile) $chart }
    $chart = (Resolve-Path -LiteralPath $chart).Path
    $repeat = Get-Setting $case 'repeat' 1
    if ($repeat -lt 1 -or $repeat -gt 20 -or $repeat -ne [int]$repeat) { throw 'repeat must be an integer from 1 to 20.' }
    $threshold = Get-Setting $case 'maxUpdateP99Ms' $null
    if ($null -ne $threshold -and (!([double]::IsFinite([double]$threshold)) -or $threshold -le 0)) { throw 'maxUpdateP99Ms must be positive and finite.' }
    $captureArgs = @('--gameplay-diagnostic', '--filter', 'gameplay', '--chart', $chart)
    foreach ($setting in @(
        @('start', '--start', 0), @('duration', '--duration', 30),
        @('scrollSpeed', '--scroll-speed', 8), @('referenceBpm', '--reference-bpm', 'MainBpm'),
        @('updateHz', '--update-hz', 0), @('drawHz', '--draw-hz', 0),
        @('skin', '--skin', 'Argon'), @('longNoteMode', '--long-note-mode', 'Undefined')
    )) {
        $value = Get-Setting $case $setting[0] $setting[2]
        $captureArgs += $setting[1], [Convert]::ToString($value, [Globalization.CultureInfo]::InvariantCulture)
    }
    if (Get-Setting $case 'headless' $false) { $captureArgs += '--headless' }
    if (Get-Setting $case 'audioOutput' $false) { $captureArgs += '--audio-output' }
    if (Get-Setting $case 'invert' $false) { $captureArgs += '--invert' }
    $invertSeed = Get-Setting $case 'invertRandomSeed' $null
    if ($null -ne $invertSeed) {
        if (!(Get-Setting $case 'invert' $false)) { throw 'invertRandomSeed requires invert: true.' }
        $captureArgs += '--invert-random-seed', [Convert]::ToString($invertSeed, [Globalization.CultureInfo]::InvariantCulture)
    }
    $plans += [pscustomobject]@{ Name = $case.name; Repeat = $repeat; Arguments = $captureArgs; Threshold = $threshold }
}

New-Item -ItemType Directory -Path $outputRoot | Out-Null
Copy-Item -LiteralPath $caseFile -Destination (Join-Path $outputRoot 'cases.json')
$reports = [Collections.Generic.List[object]]::new()
$failed = $false
foreach ($plan in $plans) {
    for ($run = 1; $run -le $plan.Repeat; $run++) {
        $destination = Join-Path $outputRoot ($plan.Name + '/run-' + $run.ToString('00'))
        New-Item -ItemType Directory -Path $destination | Out-Null
        # Each process exits before the next case starts so captures do not compete for CPU/audio/GPU.
        $captureArgs = $plan.Arguments + @('--output', (Join-Path $destination 'capture'))
        & dotnet $assemblyFile @captureArgs *> (Join-Path $destination 'runner.log')
        $code = $LASTEXITCODE
        $reportPath = Join-Path $destination 'capture/summary.json'
        $summary = if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json } else { $null }
        $passed = $code -eq 0 -and $null -ne $summary -and $summary.Success
        $p99 = if ($null -ne $summary) { $summary.Summary.UpdateP99Ms } else { $null }
        if ($null -ne $plan.Threshold -and $null -ne $p99 -and $p99 -gt $plan.Threshold) { $passed = $false }
        if (!$passed) { $failed = $true }
        $reports.Add([pscustomobject]@{
            Case = $plan.Name; Run = $run; Passed = $passed; ExitCode = $code
            UpdateP99Ms = $p99; MaxUpdateP99Ms = $plan.Threshold; Report = $reportPath
        })
        $reports | ConvertTo-Json -Depth 10 -AsArray | Set-Content -LiteralPath (Join-Path $outputRoot 'matrix.json') -Encoding utf8
        Write-Host ($plan.Name + ' run ' + $run + ': passed=' + $passed + ', update P99=' + $p99 + ' ms')
    }
}
if ($failed) { exit 1 }
