param([string]$Exe,[string]$SaveRoot)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=(Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
if(-not $Exe){$Exe=Join-Path $repoRoot ('dist\NomadDriveSaveConverter-v'+$version+'-win-x64.exe')}
if(-not $SaveRoot){$SaveRoot=Join-Path $repoRoot '.build\smoke-saves'}
if(-not (Test-Path -LiteralPath $SaveRoot)){throw 'Run scripts/create_smoke_fixture.py first. This test requires isolated synthetic fixtures.'}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Exe))
$type=$assembly.GetType('ConverterWindow');$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$form=[Activator]::CreateInstance($type)
function Field([string]$name){$type.GetField($name,$flags).GetValue($form)}
function PumpUntilIdle {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do {
        [Windows.Forms.Application]::DoEvents();[Threading.Thread]::Sleep(10)
        if($watch.Elapsed.TotalSeconds -gt 50){throw ('GUI timeout: '+(Field 'status').Text)}
    }while((Field 'busy'))
}
function PreviewSelected {
    $task=$type.GetMethod('Preview',$flags).Invoke($form,@())
    PumpUntilIdle
    $task.GetAwaiter().GetResult() | Out-Null
}
$before=@(Get-ChildItem -LiteralPath $SaveRoot -Directory).Count
try {
    (Field 'saves').Text=[IO.Path]::GetFullPath($SaveRoot)
    $form.ShowInTaskbar=$false;$form.StartPosition=[Windows.Forms.FormStartPosition]::Manual
    $form.Location=[Drawing.Point]::new(-20000,-20000);$form.Show();PumpUntilIdle
    if(-not (Field 'details').Text.Contains('Converted: 0')){throw ('Automatic startup failed: '+(Field 'details').Text)}
    $grid=Field 'slots';if($grid.Rows.Count -lt 6){throw 'Synthetic worlds are missing'}
    $legacy=@($grid.Rows|Where-Object {$_.Tag['format'] -eq 5})[0]
    $type.GetField('filling',$flags).SetValue($form,$true);$grid.CurrentCell=$legacy.Cells[0]
    $type.GetField('filling',$flags).SetValue($form,$false);PreviewSelected
    if(-not (Field 'details').Text.Contains('Player slots:')){throw 'English player-slot preview is missing'}
    if((Field 'details').Lines.Count -lt 5){throw 'Report line breaks are not displayed'}
    if(-not (Field 'newName').Text.EndsWith('(converted)')){throw 'English suffix is missing'}
    if(-not (Field 'convert').Enabled){throw 'Single-world action is disabled after preview'}
    (Field 'players').SelectedIndex=2;PumpUntilIdle
    if(-not (Field 'details').Text.Contains('Player slots: 3')){throw 'Player limit preview did not update'}
    (Field 'replace').Checked=$false;PumpUntilIdle
    if(-not (Field 'details').Text.Contains('Replaced: 0;')){throw 'Replacement preview did not update'}
    $current=@($grid.Rows|Where-Object {$_.Tag['format'] -eq 16})[0]
    $type.GetField('filling',$flags).SetValue($form,$true);$grid.CurrentCell=$current.Cells[0]
    $type.GetField('filling',$flags).SetValue($form,$false);PreviewSelected
    if((Field 'convert').Text -ne 'Repair multiplayer in a copy'){throw 'Current-format repair action is missing'}
    if((Field 'cleanup').Checked){throw 'Loot cleanup must be off by default'}
    (Field 'cleanup').Checked=$true;PumpUntilIdle
    if(-not (Field 'details').Text.Contains('LEGACY WORLD-LOOT CLEANUP')){throw 'Loot cleanup preview is missing'}
    if(-not (Field 'details').Text.Contains('ground stashes')){throw 'Loot cleanup scope is missing'}
    if((Field 'convert').Text -ne 'Create cleaned copy'){throw 'Loot cleanup copy action is missing'}
    $resources=$assembly.GetManifestResourceNames()
    if('NomadDriveSaveConverter.Mapping' -notin $resources -or 'NomadDriveSaveConverter.Notices' -notin $resources){throw 'Embedded resources are missing'}
    if('NomadDriveSaveConverter.Engine' -in $resources -or $assembly.GetType('EngineProvider')){throw 'The removed executable engine is still present'}
    $type.GetField('busy',$flags).SetValue($form,$true)
    $form.Close()
    if($form.IsDisposed){throw 'The window closed during an active operation'}
    $type.GetField('busy',$flags).SetValue($form,$false)
    $form.Size=[Drawing.Size]::new(880,780);$form.PerformLayout();PumpUntilIdle
    if((Field 'details').Height -lt 45){throw 'Report is hidden at minimum size'}
    $form.Size=[Drawing.Size]::new(1040,890);$form.PerformLayout();PumpUntilIdle
    if(-not (Field 'cleanup').Enabled -or -not (Field 'convert').Enabled){throw 'Cleanup controls remained disabled after preview'}
    $bitmap=[Drawing.Bitmap]::new($form.Width,$form.Height)
    $bitmapPath=Join-Path $repoRoot '.build\gui-preview.png'
    $form.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$form.Width,$form.Height))
    $bitmap.Save($bitmapPath,[Drawing.Imaging.ImageFormat]::Png);$bitmap.Dispose()
    $after=@(Get-ChildItem -LiteralPath $SaveRoot -Directory).Count
    if($after -ne $before){throw 'Read-only GUI smoke test created extra worlds'}
    Write-Output 'PASS: automatic startup, no duplicate worlds, English preview, player limit, current-format repair, opt-in loot cleanup preview, direct .NET implementation and notices.'
} finally {$form.Dispose()}
