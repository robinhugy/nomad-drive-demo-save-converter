param([string]$Python = 'python', [switch]$SkipTests)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot=Join-Path $repoRoot '.build'
$distRoot=Join-Path $repoRoot 'dist'
$version=(Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'VERSION must contain a semantic version.'}
function Invoke-Checked([string]$Command,[string[]]$Arguments){
    & $Command @Arguments
    if($LASTEXITCODE -ne 0){throw ('Command failed with exit code '+$LASTEXITCODE+': '+$Command)}
}
Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Path $buildRoot,$distRoot -Force | Out-Null
    $generated=Join-Path $buildRoot 'BuildInfo.cs'
    @"
using System.Reflection;
[assembly: AssemblyTitle("Nomad Drive Save Converter")]
[assembly: AssemblyProduct("Nomad Drive Save Converter")]
[assembly: AssemblyDescription("Demo to Early Access save migration and selected player limits")]
[assembly: AssemblyCompany("robinhugy")]
[assembly: AssemblyCopyright("Copyright (c) 2026 robinhugy")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
internal static class BuildInfo { public const string Version = "$version"; }
"@ | Set-Content -LiteralPath $generated -Encoding utf8
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if(-not (Test-Path -LiteralPath $compiler)){throw 'Enable .NET Framework 4.8 to build this app.'}
    $mapping=(Resolve-Path -LiteralPath 'src\engine\mapping.json').Path
    $earlyMapping=(Resolve-Path -LiteralPath 'src\engine\early-access.json').Path
    $icon=(Resolve-Path -LiteralPath 'src\gui\assets\app-icon.ico').Path
    $notices=Join-Path $buildRoot 'THIRD_PARTY_NOTICES.txt'
    $license=Get-Content -LiteralPath 'LICENSE' -Raw
    @"
Nomad Drive Save Converter: licenses and notices

$license
The application uses the Windows .NET Framework supplied by Microsoft.
No Python interpreter, PyInstaller bootloader, game assembly or personal save is bundled.
Game names and compatibility identifiers belong to their respective owners.
"@ | Set-Content -LiteralPath $notices -Encoding utf8
    $engineSources=@(Get-ChildItem -LiteralPath 'src\engine' -Filter '*.cs' -File | ForEach-Object {$_.FullName})
    $packageName='NomadDriveSaveConverter-v'+$version+'-win-x64'
    $packageDir=Join-Path $buildRoot $packageName
    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
    $exe=Join-Path $packageDir 'NomadDriveSaveConverter.exe'
    $common=@('/nologo','/optimize+','/platform:x64','/codepage:65001','/r:System.Core.dll','/r:System.Web.Extensions.dll',('/resource:'+$mapping+',NomadDriveSaveConverter.Mapping'),('/resource:'+$earlyMapping+',NomadDriveSaveConverter.EarlyAccessMapping'),$generated)+$engineSources
    Invoke-Checked $compiler (@('/target:winexe','/r:System.Windows.Forms.dll','/r:System.Drawing.dll',('/win32manifest:'+(Join-Path $repoRoot 'src\gui\app.manifest')),('/win32icon:'+$icon),('/resource:'+$icon+',NomadDriveSaveConverter.Icon'),('/resource:'+$notices+',NomadDriveSaveConverter.Notices'),('/out:'+$exe),'src\gui\ConverterWindow.cs')+$common)
    if(-not $SkipTests){
        Invoke-Checked $Python @('-m','unittest','discover','-s','tests','-v')
        $harness=Join-Path $buildRoot 'native-tests.exe'
        Invoke-Checked $compiler (@('/target:exe','/define:TEST_HARNESS',('/out:'+$harness),'tests\NativeHarness.cs')+$common)
        Invoke-Checked $Python @('tests\run_native_tests.py',$harness,$exe)
        Invoke-Checked $Python @('tests\run_selection_tests.py',$harness)
        Invoke-Checked $Python @('tests\run_workflow_ui.py')
    }
    Copy-Item -LiteralPath 'README.md','LICENSE','docs\RELEASE_NOTES.md' -Destination $packageDir -Force
    Copy-Item -LiteralPath $notices -Destination (Join-Path $packageDir 'THIRD_PARTY_NOTICES.txt') -Force
    $packageHash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    ($packageHash+'  NomadDriveSaveConverter.exe') | Set-Content -LiteralPath (Join-Path $packageDir 'SHA256SUMS.txt') -Encoding ascii
    Copy-Item -LiteralPath $exe -Destination (Join-Path $distRoot ($packageName+'.exe')) -Force
    Compress-Archive -LiteralPath $packageDir -DestinationPath (Join-Path $distRoot ($packageName+'.zip')) -Force
    Invoke-Checked $Python @('scripts\package_source.py')
    Write-Output ('Release files: '+$distRoot)
} finally {Pop-Location}
