<#
.SYNOPSIS
  The motion pipeline: generate clips with Blender, or draw a review sheet of one.

  motion.ps1 gen <script.py> [args...]      run a lib/ or drivers/ script in Blender (headless)
  motion.ps1 preview <clip.fbx> [sheet.py args...]
                                            dump the clip's poses and draw tools/motions/sheet.py's
                                            contact sheet (PNG path is printed last)
  Env: APL_RIG (Mixamo rig), APL_MOTION_OUT (default <repo>\build\motions), APL_MOTION_WORK,
       APL_BLENDER (default %USERPROFILE%\tools\blender-4.2.9-windows-x64\blender.exe).
#>
param(
  [Parameter(Mandatory)][ValidateSet('gen', 'preview')][string]$Cmd,
  [Parameter(ValueFromRemainingArguments)][string[]]$Rest
)
$ErrorActionPreference = 'Continue'
$blender = if ($env:APL_BLENDER) { $env:APL_BLENDER } else { Join-Path $env:USERPROFILE 'tools\blender-4.2.9-windows-x64\blender.exe' }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$work = if ($env:APL_MOTION_WORK) { $env:APL_MOTION_WORK } else { Join-Path $repo 'build\motions\work' }
New-Item -ItemType Directory -Force $work | Out-Null
if (-not (Test-Path $blender)) { throw "Blender not found at $blender (set APL_BLENDER)" }

switch ($Cmd) {
  'gen' {
    if (-not $Rest) { throw 'gen needs a script' }
    # (no 2>&1: Windows PowerShell wraps a native command's stderr in error records)
    & $blender -b --python (Join-Path $here 'run.py') -- @Rest |
      Where-Object { $_ -and $_ -notmatch '^Blender \d|^Read prefs|^Color management|^FBX version|export starting|export finished|^Blender quit' }
  }
  'preview' {
    if (-not $Rest) { throw 'preview needs a clip' }
    $fbx = (Resolve-Path $Rest[0]).Path
    $name = [IO.Path]::GetFileNameWithoutExtension($fbx)
    $json = Join-Path $work "$name.pose.json"
    $png = Join-Path $work "$name.sheet.png"
    & $blender -b --python (Join-Path $here 'dump_pose.py') -- $fbx $json | Where-Object { $_ -match 'dumped|Error|Traceback' }
    $extra = @()
    if ($Rest.Count -gt 1) { $extra = $Rest[1..($Rest.Count - 1)] }
    python (Join-Path $here 'sheet.py') $json $png --count 10 @extra
    Write-Output $png
  }
}
