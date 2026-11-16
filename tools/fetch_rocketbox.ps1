# Downloads the Rocketbox avatars APL uses (MIT licence, Microsoft) into
# client-assets/rocketbox/<Name>/{Export,Textures}. Re-runnable: skips files
# already downloaded with the right size.
#
#   powershell -File tools\fetch_rocketbox.ps1
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\client-assets\rocketbox'
$avatars = @(
  'Professions/Business_Male_01', 'Professions/Business_Male_02', 'Professions/Business_Male_03',
  'Professions/Business_Male_04', 'Professions/Business_Male_05', 'Professions/Business_Male_06',
  'Professions/Business_Male_07',
  'Professions/Business_Female_01', 'Professions/Business_Female_02', 'Professions/Business_Female_03',
  'Professions/Business_Female_04',
  'Adults/Male_Adult_01', 'Adults/Male_Adult_02', 'Adults/Male_Adult_03', 'Adults/Male_Adult_04',
  'Adults/Male_Adult_05', 'Adults/Male_Adult_06', 'Adults/Male_Adult_07', 'Adults/Male_Adult_08',
  'Adults/Female_Adult_01', 'Adults/Female_Adult_02',
  # Cricketers (match mode): every male body without headgear, recoloured into team kits.
  'Adults/Male_Adult_09', 'Adults/Male_Adult_10', 'Adults/Male_Adult_11', 'Adults/Male_Adult_12',
  'Adults/Male_Adult_13', 'Adults/Male_Adult_14', 'Adults/Male_Adult_15', 'Adults/Male_Adult_16',
  'Adults/Male_Adult_17', 'Adults/Male_Adult_18', 'Adults/Male_Adult_19', 'Adults/Male_Adult_20',
  'Adults/Male_Adult_21',
  'Professions/Sports_Male_01', 'Professions/Sports_Male_02', 'Professions/Sports_Male_03', 'Professions/Sports_Male_04',
  'Professions/Medical_Male_01', 'Professions/Medical_Male_02', 'Professions/Medical_Male_03',
  'Professions/Medical_Male_04', 'Professions/Medical_Male_05',
  'Professions/Delivery_Male_01', 'Professions/Gardener_Male_01', 'Professions/Security_Male_01', 'Professions/Wood_Male_01',
  'Professions/Pilot_Male_01', 'Professions/Pilot_Male_02', 'Professions/Pilot_Male_03'
)
$h = @{ 'User-Agent' = 'apl-asset-fetch' }
$tree = (Invoke-WebRequest 'https://api.github.com/repos/microsoft/Microsoft-Rocketbox/git/trees/master?recursive=1' -Headers $h -UseBasicParsing).Content | ConvertFrom-Json
$wanted = foreach ($a in $avatars) {
  $prefix = "Assets/Avatars/$a/"
  $tree.tree | Where-Object { $_.type -eq 'blob' -and $_.path.StartsWith($prefix) -and $_.path -notmatch '\.png$' }
}
$total = ($wanted | Measure-Object size -Sum).Sum
Write-Host ("{0} files, {1:N0} MB" -f $wanted.Count, ($total / 1MB))
$done = 0
foreach ($f in $wanted) {
  $rel = $f.path.Substring('Assets/Avatars/'.Length)
  $rel = $rel.Substring($rel.IndexOf('/') + 1)   # drop Professions/ or Adults/
  $dest = Join-Path $root $rel
  New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
  if (-not ((Test-Path $dest) -and (Get-Item $dest).Length -eq $f.size)) {
    $url = 'https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/' + ($f.path -replace ' ', '%20')
    Invoke-WebRequest $url -OutFile $dest -UseBasicParsing -Headers $h
  }
  $done += $f.size
  Write-Host ("{0,5:P0}  {1}" -f ($done / $total), $rel)
}
# Ship the MIT licence alongside the models (attribution requirement).
Invoke-WebRequest 'https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/LICENSE.md' `
  -OutFile (Join-Path $root 'LICENSE.md') -UseBasicParsing -Headers $h
Write-Host 'Done.'
