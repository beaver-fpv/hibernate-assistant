#requires -Version 5.1
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) { & (Join-Path $repository 'build.ps1') }
$executable = Join-Path $repository 'dist\HibernateAssistant.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build the application first.' }
$artifacts = Join-Path $repository 'artifacts\self-test'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$report = Join-Path $artifacts 'test-results.txt'
# /self-test injects FakePower and only shows offscreen windows.
# It never calls the real Windows power-changing methods.
$process = Start-Process -FilePath $executable -ArgumentList @('/self-test', ('"' + $report + '"')) -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(90000)) {
        $process.Kill()
        throw 'Self-test timed out after 90 seconds.'
    }
    $exitCode = $process.ExitCode
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
    if ($exitCode -ne 0) { throw ('Self-test failed with exit code ' + $exitCode) }
    if (-not (Test-Path -LiteralPath $report)) { throw 'Self-test did not produce a report.' }
} finally { $process.Dispose() }
