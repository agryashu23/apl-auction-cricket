# Creates / refreshes the Unity client in client/ and builds the hall:
#   1. create the project (first run only)
#   2. sync code + art + voice + data from client-src/, client-assets/, audio/
#   3. link the C# core package and Newtonsoft
#   4. batch: Setup → Animators → Hall scene → screenshots of every shot
#
#   5. optional: Windows player + scripted play-through screenshots (-PlayTest), APK (-Apk)
#
#   powershell -File tools\unity_bootstrap.ps1 [-SkipCapture] [-PlayTest] [-Apk]
param(
  [string]$Version = '6000.3.27f2',
  [switch]$SkipCapture,
  [switch]$PlayTest,
  # With -PlayTest: stop after the first few lots (PASS and SKIP included).
  [switch]$Short,
  # With -PlayTest: play a quick cricket match instead of the auction.
  [switch]$MatchTest,
  # With -MatchTest: play the whole ceremony too (toss, openers, walk-out, intros) and both innings.
  [switch]$PreMatch,
  # With -MatchTest: film every ball of the batter from 5 cameras (-aplbatcap → autoshots/batcap).
  [switch]$BatCapture,
  # With -PlayTest: tour every stadium (screenshots of each ground).
  [switch]$StadiumTour,
  [int]$Overs = 0,
  [switch]$Nudges,
  # With -MatchTest: a run out every runnable ball, both innings (TEC-1 stall repro, -aplrunoutloop).
  [switch]$RunOutLoop,
  [string]$Venue = '',
  # With -PlayTest: line up both sides in their kits (screenshots).
  [switch]$CastTour,
  # With -PlayTest: pose every batting clip on the striker and photograph it (the motion reviewer's gallery).
  [switch]$ShotGallery,
  # With -ShotGallery: 'all', or clip names separated by commas ('lhb:' prefix for the left-handed set).
  [string]$GalleryClips = 'all',
  [switch]$Apk
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
$unity = "C:\Program Files\Unity\Hub\Editor\$Version\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "Unity $Version not installed at $unity" }
$client = Join-Path $repo 'client'
$logs = Join-Path $repo 'build\unity'
New-Item -ItemType Directory -Force $logs | Out-Null

function RunUnity([string]$name, [string[]]$extra) {
  $log = Join-Path $logs "$name.log"
  $argv = @('-batchmode', '-projectPath', $client, '-logFile', $log) + $extra
  Write-Host "Unity: $name ..."
  $p = Start-Process -FilePath $unity -ArgumentList $argv -Wait -PassThru -NoNewWindow
  $errors = Select-String -Path $log -Pattern 'error CS\d+|Exception|\[APL\]' | Select-Object -Last 15
  $errors | ForEach-Object { Write-Host "   $($_.Line.Trim())" }
  if ($p.ExitCode -ne 0) { throw "Unity step '$name' failed (exit $($p.ExitCode)); see $log" }
}

# 1. Project
if (-not (Test-Path (Join-Path $client 'ProjectSettings'))) {
  Write-Host 'Creating Unity project...'
  $p = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-quit', '-createProject', $client, '-logFile', (Join-Path $logs 'create.log')) -Wait -PassThru -NoNewWindow
  if ($p.ExitCode -ne 0) { throw 'Project creation failed; see build\unity\create.log' }
}

# Unity players can only load a component whose class name matches its file name
# (the editor tolerates a mismatch, then the built scene fails as "corrupted").
Get-ChildItem (Join-Path $repo 'client-src\Assets\Apl') -Recurse -Filter *.cs | ForEach-Object {
  $file = $_
  Select-String -Path $file.FullName -Pattern 'class (\w+)\s*:\s*(MonoBehaviour|ScriptableObject)\b' | ForEach-Object {
    $cls = $_.Matches[0].Groups[1].Value
    if ($cls -ne $file.BaseName) { throw "$($file.Name) declares component $cls; move it to $cls.cs" }
  }
}

# 2. Sync content (robocopy /MIR keeps it exactly in step; exit codes < 8 are success)
function Mirror([string]$from, [string]$to) {
  New-Item -ItemType Directory -Force $to | Out-Null
  robocopy $from $to /MIR /NFL /NDL /NJH /NJS /NP /XF *.meta | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "copy failed: $from" }
}
Mirror (Join-Path $repo 'client-src\Assets\Apl') (Join-Path $client 'Assets\Apl')
foreach ($r in 'Data', 'Crests', 'Ui', 'Sfx', 'Portraits', 'Music') { Mirror (Join-Path $repo "client-src\Assets\Resources\$r") (Join-Path $client "Assets\Resources\$r") }
Mirror (Join-Path $repo 'client-assets\rocketbox') (Join-Path $client 'Assets\ThirdParty\Rocketbox')
Mirror (Join-Path $repo 'client-assets\mixamo') (Join-Path $client 'Assets\ThirdParty\Mixamo')
# Match mode: batting / bowling / fielding clips (FBX only; metadata is in Resources/Data/cricket).
Mirror (Join-Path $repo 'client-assets\cricket') (Join-Path $client 'Assets\ThirdParty\Cricket')
# The one T-pose avatar every cricket clip is retargeted through (tools/motions/export_tpose_rig.py).
Mirror (Join-Path $repo 'client-assets\cricket_rig') (Join-Path $client 'Assets\ThirdParty\CricketRig')
# Match gear: helmet, pads, gloves, keeper kit and the bat (tools/gear/make_gear.py); read at runtime as Resources/Gear/gear.bytes + bat.png.
Mirror (Join-Path $repo 'client-assets\gear') (Join-Path $client 'Assets\Resources\Gear')
foreach ($v in 'arjun', 'kajal', 'comm_en') { if (Test-Path (Join-Path $repo "audio\voice\$v")) { Mirror (Join-Path $repo "audio\voice\$v") (Join-Path $client "Assets\Resources\Voice\$v") } }
# Match commentary: which clips say what (tools/gen_commentary.py).
if (Test-Path (Join-Path $repo 'audio\voice\commentary.json')) { Copy-Item (Join-Path $repo 'audio\voice\commentary.json') (Join-Path $client 'Assets\Resources\Data\commentary.json') -Force }
Copy-Item (Join-Path $repo 'data\apl_dataset.json') (Join-Path $client 'Assets\Resources\Data\apl_dataset.json') -Force
# Default room server (git-ignored, one line: the host infra/rooms-cs/provision.ps1 prints). Missing = localhost.
$roomServer = Join-Path $repo 'client-src\Assets\Resources\room_server.txt'
$roomServerOut = Join-Path $client 'Assets\Resources\room_server.txt'
if (Test-Path $roomServer) { Copy-Item $roomServer $roomServerOut -Force }
elseif (Test-Path $roomServerOut) { Remove-Item $roomServerOut -Force }

# 3. Packages: APL core (local), Newtonsoft
$manifestPath = Join-Path $client 'Packages\manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$deps = $manifest.dependencies
$core = 'file:' + ((Join-Path $repo 'packages\AuctionCore.Net\src\AuctionCore') -replace '\\', '/')
$deps | Add-Member -NotePropertyName 'com.apl.core' -NotePropertyValue $core -Force
$deps | Add-Member -NotePropertyName 'com.unity.nuget.newtonsoft-json' -NotePropertyValue '3.2.1' -Force
# uGUI + URP ship with the editor (BuiltInPackages); a blank batchmode project has neither.
$builtIn = Join-Path (Split-Path $unity) 'Data\Resources\PackageManager\BuiltInPackages'
foreach ($pkg in 'com.unity.ugui', 'com.unity.render-pipelines.universal') {
  $ver = (Get-Content (Join-Path $builtIn "$pkg\package.json") -Raw | ConvertFrom-Json).version
  $deps | Add-Member -NotePropertyName $pkg -NotePropertyValue $ver -Force
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))

# 4. Build
RunUnity 'build-hall' @('-quit', '-executeMethod', 'Apl.Client.EditorTools.Pipeline.BuildEverything')
if (-not $SkipCapture) {
  RunUnity 'capture' @('-quit', '-executeMethod', 'Apl.Client.EditorTools.Pipeline.Capture')
  Write-Host "Screenshots: $(Join-Path $logs 'screens')"
}
if ($PlayTest) {
  RunUnity 'win' @('-quit', '-buildTarget', 'Win64', '-executeMethod', 'Apl.Client.EditorTools.Pipeline.BuildWindows')
  $shots = if ($ShotGallery) { Join-Path $logs 'gallery' } else { Join-Path $logs 'autoshots' }
  if ($ShotGallery -and (Test-Path $shots)) { Remove-Item -Recurse -Force $shots }
  if (Test-Path $shots) { Remove-Item -Recurse -Force $shots }
  Write-Host 'Play-through (menus, ceremony, auction at 12x, results)...'
  $p = Start-Process -FilePath (Join-Path $logs 'win\APL.exe') -ArgumentList (@('-aplshots', $shots) + $(if ($ShotGallery) { @('-aplgallery', $GalleryClips) } elseif ($CastTour) { @('-aplcast') } elseif ($StadiumTour) { @('-aplstadiums') } elseif ($MatchTest) { @('-aplmatchtest') + $(if ($PreMatch) { @('-aplprematch') } else { @() }) + $(if ($BatCapture) { @('-aplbatcap') } else { @() }) + $(if ($Overs -gt 0) { @('-aplovers', "$Overs") } else { @() }) + $(if ($Nudges) { @('-aplnudges') } else { @() }) + $(if ($RunOutLoop) { @('-aplrunoutloop') } else { @() }) + $(if ($Venue) { @('-aplvenue', $Venue) } else { @() }) } elseif ($Short) { @('-aplshort') } else { @() }) + @('-screen-width', $(if ($ShotGallery) { '1280' } else { '1600' }), '-screen-height', $(if ($ShotGallery) { '720' } else { '900' }), '-screen-fullscreen', '0', '-logFile', (Join-Path $logs 'win-player.log'))) -Wait -PassThru
  Get-Content (Join-Path $shots 'autoshot.txt') | Select-String 'DONE|Exception|Error|MISSING' | Select-Object -First 20 | ForEach-Object { Write-Host "   $($_.Line)" }
  Write-Host "Play-through exit $($p.ExitCode); shots in $shots"
  if ($ShotGallery) { python -I (Join-Path $repo 'tools\motions\gallery_sheet.py') $shots }
}
if ($Apk) {
  RunUnity 'apk' @('-quit', '-buildTarget', 'Android', '-executeMethod', 'Apl.Client.EditorTools.Pipeline.BuildAndroid')
  Write-Host "APK: $(Join-Path $logs 'APL.apk')"
}
