param(
    [string]$Python = 'python',
    [switch]$InstallDependencies,
    [switch]$SkipTests
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot=Join-Path $repoRoot '.build'
$distRoot=Join-Path $repoRoot 'dist'
$version=(Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'VERSION must contain a semantic version such as 1.1.0.'}
function Invoke-Checked([string]$Command,[string[]]$Arguments){
    & $Command @Arguments
    if($LASTEXITCODE -ne 0){throw ('Command failed with exit code '+$LASTEXITCODE+': '+$Command)}
}
Push-Location $repoRoot
try {
    Invoke-Checked $Python @('-c','import sys,struct; assert sys.version_info >= (3,11); assert struct.calcsize("P")==8, "Use 64-bit Python"')
    if($InstallDependencies){Invoke-Checked $Python @('-m','pip','install','-r','requirements-build.txt')}
    Invoke-Checked $Python @('-c','import PyInstaller; import importlib.metadata as m; assert m.version("PyInstaller")=="6.22.2", "Install requirements-build.txt"')
    if(-not $SkipTests){Invoke-Checked $Python @('-m','unittest','discover','-s','tests','-v')}
    New-Item -ItemType Directory -Path $buildRoot,$distRoot -Force | Out-Null
    $engineDir=Join-Path $buildRoot 'engine'
    $pyiWork=Join-Path $buildRoot 'pyinstaller'
    $mapping=(Resolve-Path -LiteralPath 'src\engine\mapping.json').Path
    Invoke-Checked $Python @('-m','PyInstaller','--noconfirm','--onefile','--windowed','--noupx',
        '--name','converter-engine','--add-data',($mapping+';.'),'--distpath',$engineDir,
        '--workpath',$pyiWork,'--specpath',$buildRoot,'src\engine\converter_engine.py')
    $engineFile=Join-Path $engineDir 'converter-engine.exe'
    $engineHash=(Get-FileHash -LiteralPath $engineFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $notices=Join-Path $buildRoot 'THIRD_PARTY_NOTICES.txt'
    Invoke-Checked $Python @('scripts\collect_notices.py',$notices)
    $generated=Join-Path $buildRoot 'BuildInfo.cs'
    $assemblyVersion=$version+'.0'
    @"
using System.Reflection;
[assembly: AssemblyTitle("Nomad Drive Save Converter")]
[assembly: AssemblyProduct("Nomad Drive Save Converter")]
[assembly: AssemblyDescription("Legacy save conversion and multiplayer repair")]
[assembly: AssemblyCompany("robinhugy")]
[assembly: AssemblyCopyright("Copyright (c) 2026 robinhugy")]
[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
internal static class BuildInfo {
    public const string Version = "$version";
    public const string EngineSha256 = "$engineHash";
}
"@ | Set-Content -LiteralPath $generated -Encoding utf8
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if(-not (Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.x C# compiler was not found. Install/enable .NET Framework 4.8.'}
    $packageName='NomadDriveSaveConverter-v'+$version+'-win-x64'
    $packageDir=Join-Path $buildRoot $packageName
    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
    $exe=Join-Path $packageDir 'NomadDriveSaveConverter.exe'
    Invoke-Checked $compiler @('/nologo','/target:winexe','/optimize+','/platform:x64','/codepage:65001',
        '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',
        ('/win32manifest:'+(Join-Path $repoRoot 'src\gui\app.manifest')),
        ('/resource:'+$engineFile+',NomadDriveSaveConverter.Engine'),
        ('/resource:'+$notices+',NomadDriveSaveConverter.Notices'),('/out:'+$exe),
        'src\gui\ConverterWindow.cs','src\gui\EngineProvider.cs',$generated)
    Copy-Item -LiteralPath 'README.md','LICENSE','docs\RELEASE_NOTES.md' -Destination $packageDir -Force
    Copy-Item -LiteralPath $notices -Destination (Join-Path $packageDir 'THIRD_PARTY_NOTICES.txt') -Force
    $packageHash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    ($packageHash+'  NomadDriveSaveConverter.exe') | Set-Content -LiteralPath (Join-Path $packageDir 'SHA256SUMS.txt') -Encoding ascii
    $standalone=Join-Path $distRoot ($packageName+'.exe')
    Copy-Item -LiteralPath $exe -Destination $standalone -Force
    Compress-Archive -LiteralPath $packageDir -DestinationPath (Join-Path $distRoot ($packageName+'.zip')) -Force
    $checksums=@()
    foreach($asset in @($standalone,(Join-Path $distRoot ($packageName+'.zip')))){
        $checksums+=((Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($asset))
    }
    $checksums | Set-Content -LiteralPath (Join-Path $distRoot 'SHA256SUMS.txt') -Encoding ascii
    Invoke-Checked $Python @('scripts\package_source.py')
    Write-Output ('Release files: '+$distRoot)
} finally {Pop-Location}
