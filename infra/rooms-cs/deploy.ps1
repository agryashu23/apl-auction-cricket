# APL C# room server: code-only update (re-publish, upload, restart).
#
#   powershell -File infra\rooms-cs\deploy.ps1                 # production timings
#   powershell -File infra\rooms-cs\deploy.ps1 -TestTimings    # harness timings (time x10, takeover 1.5s, retention 4s)
#   powershell -File infra\rooms-cs\deploy.ps1 -SkipPublish    # re-upload last build / just change timings
#
# Uses the AWS CLI profile "default". The box is created once by provision.ps1.
param(
    [switch]$TestTimings,
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'

$Profile_  = 'default'
$Region    = 'ap-south-1'
$Instance  = 'apl-rooms-cs'
$StaticIp  = 'apl-rooms-cs-ip'
$KeyFile   = Join-Path $env:USERPROFILE '.ssh\apl-rooms-cs.pem'
$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Stage     = Join-Path $env:TEMP 'apl-rooms-cs-publish'

function Aws { & aws.exe @args --profile $Profile_ --region $Region; if ($LASTEXITCODE -ne 0) { throw "aws $($args -join ' ') failed" } }

$ip = (Aws lightsail get-static-ip --static-ip-name $StaticIp --query 'staticIp.ipAddress' --output text).Trim()
$hostName = ($ip -replace '\.', '-') + '.sslip.io'
Write-Host "[deploy] host $hostName ($ip)"

# Keep SSH open only to this PC's current public IP (it can change between runs); 80/443 open to all.
$myIp = (Invoke-RestMethod 'https://checkip.amazonaws.com').Trim()
$ports = @(
    @{ fromPort = 22;  toPort = 22;  protocol = 'tcp'; cidrs = @("$myIp/32") },
    @{ fromPort = 80;  toPort = 80;  protocol = 'tcp'; cidrs = @('0.0.0.0/0'); ipv6Cidrs = @('::/0') },
    @{ fromPort = 443; toPort = 443; protocol = 'tcp'; cidrs = @('0.0.0.0/0'); ipv6Cidrs = @('::/0') }
)
$portsFile = Join-Path $env:TEMP 'apl-rooms-cs-ports.json'
[IO.File]::WriteAllText($portsFile, (ConvertTo-Json -InputObject $ports -Depth 5 -Compress))
Aws lightsail put-instance-public-ports --instance-name $Instance --port-infos "file://$portsFile" | Out-Null

if (-not $SkipPublish) {
    if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
    & dotnet publish (Join-Path $Repo 'server-cs\AplRooms') -c Release -r linux-x64 --self-contained true `
        -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $Stage 'app')
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    New-Item -ItemType Directory -Force (Join-Path $Stage 'app\data') | Out-Null
    Copy-Item (Join-Path $Repo 'data\apl_dataset.json') (Join-Path $Stage 'app\data\apl_dataset.json')
    Push-Location (Join-Path $Stage 'app')
    & (Join-Path $env:SystemRoot 'System32\tar.exe') -czf (Join-Path $Stage 'aplrooms.tgz') AplRooms data
    Pop-Location
    if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
}

$envBody = if ($TestTimings) { "APL_ROOMS_TIME_SCALE=10`nAPL_ROOMS_TAKEOVER_SEC=1.5`nAPL_ROOMS_RETENTION_SEC=4`n" } else { "" }
$envFile = Join-Path $Stage 'aplrooms.env'
New-Item -ItemType Directory -Force $Stage | Out-Null
[IO.File]::WriteAllText($envFile, $envBody)

$ssh = @('-i', $KeyFile, '-o', 'StrictHostKeyChecking=accept-new', '-o', 'ConnectTimeout=20')
& scp @ssh (Join-Path $Stage 'aplrooms.tgz') $envFile "ubuntu@${ip}:/tmp/"
if ($LASTEXITCODE -ne 0) { throw 'scp failed' }
$remote = 'set -e; sudo mkdir -p /opt/aplrooms; sudo tar -xzf /tmp/aplrooms.tgz -C /opt/aplrooms; ' +
          'sudo chmod 755 /opt/aplrooms/AplRooms; sudo install -m 644 /tmp/aplrooms.env /etc/aplrooms.env; ' +
          'sudo systemctl restart aplrooms; sleep 3; systemctl is-active aplrooms; curl -s http://127.0.0.1:8787/health; echo'
& ssh @ssh "ubuntu@$ip" $remote
if ($LASTEXITCODE -ne 0) { throw 'remote restart failed' }

Write-Host "[deploy] done: https://$hostName/health   wss://$hostName/ws   (test timings: $([bool]$TestTimings))"
