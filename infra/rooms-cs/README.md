# APL C# room server hosting (server-cs/AplRooms)

One Amazon Lightsail box in **ap-south-1** (Mumbai), using the AWS CLI profile **`default`**.

| Resource | Name | Notes |
|---|---|---|
| Instance | `apl-rooms-cs` | Ubuntu 24.04, bundle `micro_3_1` (1 GB RAM, 2 vCPU, 40 GB SSD, 2 TB transfer) |
| Static IP | `apl-rooms-cs-ip` | free while attached |
| Key pair | `apl-rooms-cs-key` | private key at `%USERPROFILE%\.ssh\apl-rooms-cs.pem` |
| Firewall | instance ports | 22 from the deploying PC's IP only, 80 and 443 open |

**Host:** `<static-ip-with-dashes>.sslip.io` (for example `203-0-113-10.sslip.io`). `provision.ps1` prints it when it finishes; find it again with:

```powershell
aws lightsail get-static-ip --static-ip-name apl-rooms-cs-ip --profile default --region ap-south-1 --query staticIp.ipAddress --output text
```

- Health: `https://<host>/health`
- Client URL: `wss://<host>/ws`
- Client builds: put `https://<host>` on one line in `client-src/Assets/Resources/room_server.txt` (git-ignored). The client uses it as the default server; without it the default is `http://localhost:8787`.

## How it runs

- `/opt/aplrooms/AplRooms`: self-contained linux-x64 single-file build. The dataset is at `/opt/aplrooms/data/apl_dataset.json`.
- systemd unit `aplrooms` (`aplrooms.service`) has `Restart=always` and listens on `127.0.0.1:8787`. Its flags are set through `APL_ROOMS_*` env vars.
- `/etc/aplrooms.env` holds timing overrides. It is empty in production. With `deploy.ps1 -TestTimings` it gets `TIME_SCALE=10`, `TAKEOVER_SEC=1.5` and `RETENTION_SEC=4`, which the RoomBots harness needs.
- Caddy serves `<host>` on port 443 with an automatic Let's Encrypt certificate and proxies `/ws` and `/health` to 8787. Every other path returns 404.
- There is a 1 GB swap file.

## First-time creation

```powershell
aws login                                     # if the "default" session has expired (browser sign-in)
powershell -File infra\rooms-cs\provision.ps1  # idempotent: key pair, instance, static IP, firewall, Caddy, unit, deploy
```

`provision.ps1` stops if the bundle costs more than $10 a month.

## Update (code only)

```powershell
powershell -File infra\rooms-cs\deploy.ps1                # publish, upload, restart (production timings)
powershell -File infra\rooms-cs\deploy.ps1 -TestTimings   # same, with harness timings
powershell -File infra\rooms-cs\deploy.ps1 -SkipPublish   # only switch timings / re-upload the last build
```

The deploy also resets the SSH rule to this PC's current public IP. Restarting drops any live rooms, because rooms are kept in memory.

## Verify

```powershell
curl.exe https://<host>/health
powershell -File infra\rooms-cs\deploy.ps1 -SkipPublish -TestTimings
dotnet run --project server-cs\RoomBots -- --url wss://<host>/ws
powershell -File infra\rooms-cs\deploy.ps1 -SkipPublish   # back to production timings
```

## Logs and shell

```powershell
$ip = aws lightsail get-static-ip --static-ip-name apl-rooms-cs-ip --profile default --region ap-south-1 --query staticIp.ipAddress --output text
ssh -i $env:USERPROFILE\.ssh\apl-rooms-cs.pem ubuntu@$ip "journalctl -u aplrooms -n 200 --no-pager"
ssh -i $env:USERPROFILE\.ssh\apl-rooms-cs.pem ubuntu@$ip "journalctl -u aplrooms -f"      # follow
ssh -i $env:USERPROFILE\.ssh\apl-rooms-cs.pem ubuntu@$ip "journalctl -u caddy -n 100 --no-pager"
```

If SSH times out, your public IP has probably changed. Run `deploy.ps1 -SkipPublish` to re-open port 22 for it.

## Cost

- Lightsail `micro_3_1` (with IPv4): **$7/month**, covered by AWS credits. New Lightsail accounts may get the first months free.
- The static IP is free while attached. If it is left **unattached**, it is billed.
- Data transfer is included up to 2 TB a month.
- Nothing else is billed: no domain, load balancer, RDS or NAT. TLS is free (Let's Encrypt on sslip.io).

## Tear down (stops all charges)

```powershell
$p = '--profile','default','--region','ap-south-1'
aws lightsail delete-instance  --instance-name apl-rooms-cs @p
aws lightsail release-static-ip --static-ip-name apl-rooms-cs-ip @p   # release it, or it is billed while unattached
aws lightsail delete-key-pair   --key-pair-name apl-rooms-cs-key @p
icacls $env:USERPROFILE\.ssh\apl-rooms-cs.pem /grant:r "$($env:USERNAME):F"; Remove-Item $env:USERPROFILE\.ssh\apl-rooms-cs.pem
```
