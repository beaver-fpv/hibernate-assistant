#requires -Version 5.1
[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The 64-bit .NET Framework C# compiler was not found. Build on 64-bit Windows with .NET Framework 4.8 installed.'
}
$source = Join-Path $PSScriptRoot 'src'
$assets = Join-Path $PSScriptRoot 'assets'
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$executable = Join-Path $output 'HibernateAssistant.exe'
$compilerArguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/out:' + $executable),
    ('/win32manifest:' + (Join-Path $source 'app.manifest')),
    ('/win32icon:' + (Join-Path $assets 'app.ico')),
    ('/resource:' + (Join-Path $source 'MainWindow.xaml') + ',MainWindow.xaml'),
    ('/resource:' + (Join-Path $source 'CountdownView.xaml') + ',CountdownView.xaml')
)
foreach ($reference in @('System.dll','System.Core.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')) {
    $compilerArguments += '/reference:' + (Join-Path $framework $reference)
}
$compilerArguments += @(Get-ChildItem -LiteralPath $source -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $executable
