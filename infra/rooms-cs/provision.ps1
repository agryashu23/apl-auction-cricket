# APL C# room server: ONE-TIME creation of the Lightsail box (idempotent; safe to re-run).
#   powershell -File infra\rooms-cs\provision.ps1
# Creates in ap-south-1, profile "default" ONLY:
#   key pair  apl-rooms-cs-key   (private key saved to ~\.ssh\apl-rooms-cs.pem)
#   instance  apl-rooms-cs       (Ubuntu 24.04, bundle micro_3_1 = 1 GB / 2 vCPU / 40 GB, ~$7/month)
#   static IP apl-rooms-cs-ip    (free while attached)
# then installs Caddy + the systemd unit (bootstrap.sh) and runs deploy.ps1.
$ErrorActionPreference = 'Stop'

$Profile_  = 'default'
$Region    = 'ap-south-1'
$Zone      = 'ap-south-1a'
$Instance  = 'apl-rooms-cs'
$StaticIp  = 'apl-rooms-cs-ip'
$KeyName   = 'apl-rooms-cs-key'
$Bundle    = 'micro_3_1'
$Blueprint = 'ubuntu_24_04'
$KeyFile   = Join-Path $env:USERPROFILE '.ssh\apl-rooms-cs.pem'
$MaxMonthlyUsd = 10

function Aws { & aws.exe @args --profile $Profile_ --region $Region; if ($LASTEXITCODE -ne 0) { throw "aws $($args -join ' ') failed" } }
function AwsQuiet { $ErrorActionPreference = 'Continue'; & aws.exe @args --profile $Profile_ --region $Region 2>$null | Out-Null; return ($LASTEXITCODE -eq 0) }

$account = (Aws sts get-caller-identity --query Account --output text).Trim()
Write-Host "[provision] AWS account $account (profile $Profile_), region $Region"

$price = [double](Aws lightsail get-bundles --query "bundles[?bundleId=='$Bundle'].price | [0]" --output text)
Write-Host "[provision] bundle $Bundle = `$$price/month"
if ($price -gt $MaxMonthlyUsd) { throw "bundle costs `$$price/month (> `$$MaxMonthlyUsd); stopping" }

if (-not (Test-Path $KeyFile)) {
    if (AwsQuiet lightsail get-key-pair --key-pair-name $KeyName) {
        throw "key pair $KeyName exists in AWS but $KeyFile is missing; delete the key pair or restore the file"
    }
    $kp = Aws lightsail create-key-pair --key-pair-name $KeyName --output json | ConvertFrom-Json
    New-Item -ItemType Directory -Force (Split-Path $KeyFile) | Out-Null
    [IO.File]::WriteAllText($KeyFile, $kp.privateKeyBase64)
    & icacls $KeyFile /inheritance:r /grant:r "$($env:USERNAME):R" | Out-Null
    Write-Host "[provision] key pair $KeyName -> $KeyFile"
}

if (-not (AwsQuiet lightsail get-instance --instance-name $Instance)) {
    Aws lightsail create-instances --instance-names $Instance --availability-zone $Zone `
        --blueprint-id $Blueprint --bundle-id $Bundle --key-pair-name $KeyName `
        --tags 'key=project,value=apl-rooms' | Out-Null
    Write-Host "[provision] instance $Instance creating..."
}
do {
    Start-Sleep 5
    $state = (Aws lightsail get-instance-state --instance-name $Instance --query 'state.name' --output text).Trim()
} while ($state -ne 'running')

if (-not (AwsQuiet lightsail get-static-ip --static-ip-name $StaticIp)) {
    Aws lightsail allocate-static-ip --static-ip-name $StaticIp | Out-Null
}
$attached = (Aws lightsail get-static-ip --static-ip-name $StaticIp --query 'staticIp.attachedTo' --output text).Trim()
if ($attached -ne $Instance) {
    Aws lightsail attach-static-ip --static-ip-name $StaticIp --instance-name $Instance | Out-Null
}
$ip = (Aws lightsail get-static-ip --static-ip-name $StaticIp --query 'staticIp.ipAddress' --output text).Trim()
$hostName = ($ip -replace '\.', '-') + '.sslip.io'
Write-Host "[provision] static IP $ip -> $hostName"

# Firewall (22 from this PC only, 80, 443) is applied by deploy.ps1; apply it now too so SSH works.
$myIp = (Invoke-RestMethod 'https://checkip.amazonaws.com').Trim()
$ports = @(
    @{ fromPort = 22;  toPort = 22;  protocol = 'tcp'; cidrs = @("$myIp/32") },
    @{ fromPort = 80;  toPort = 80;  protocol = 'tcp'; cidrs = @('0.0.0.0/0'); ipv6Cidrs = @('::/0') },
    @{ fromPort = 443; toPort = 443; protocol = 'tcp'; cidrs = @('0.0.0.0/0'); ipv6Cidrs = @('::/0') }
)
$portsFile = Join-Path $env:TEMP 'apl-rooms-cs-ports.json'
[IO.File]::WriteAllText($portsFile, (ConvertTo-Json -InputObject $ports -Depth 5 -Compress))
Aws lightsail put-instance-public-ports --instance-name $Instance --port-infos "file://$portsFile" | Out-Null

$ssh = @('-i', $KeyFile, '-o', 'StrictHostKeyChecking=accept-new', '-o', 'ConnectTimeout=10')
$ErrorActionPreference = 'Continue'
for ($i = 0; $i -lt 30; $i++) {
    & ssh @ssh "ubuntu@$ip" 'cloud-init status --wait >/dev/null 2>&1; echo ok'
    if ($LASTEXITCODE -eq 0) { break }
    Start-Sleep 10
}
& scp @ssh (Join-Path $PSScriptRoot 'bootstrap.sh') (Join-Path $PSScriptRoot 'aplrooms.service') "ubuntu@${ip}:/tmp/"
if ($LASTEXITCODE -ne 0) { throw 'scp failed' }
& ssh @ssh "ubuntu@$ip" "sed -i 's/\r$//' /tmp/bootstrap.sh /tmp/aplrooms.service && sudo bash /tmp/bootstrap.sh $hostName"
if ($LASTEXITCODE -ne 0) { throw 'bootstrap failed' }

& (Join-Path $PSScriptRoot 'deploy.ps1')
Write-Host "[provision] done: wss://$hostName/ws"
