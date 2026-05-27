# Systemd Deployment for TopasRunOrchestration

This document describes the production service setup for running the TopasRunOrchestration SAFE web app on the mother node.

Target machine:

```text
monte-carlo-01
```

Target app folder:

```text
/srv/cluster/tsebt/app
```

The app should first be tested manually with `dotnet Server.dll`. After manual startup works, keep it alive with `systemd`.

## 1. Manual startup test

On `monte-carlo-01`:

```fish
cd /srv/cluster/tsebt/app

set -x ASPNETCORE_ENVIRONMENT Production
set -x ASPNETCORE_URLS http://0.0.0.0:5000

dotnet Server.dll
```

Open the app from another machine:

```text
http://monte-carlo-01:5000
```

Only continue to `systemd` after this manual test works.

## 2. Create the systemd service file

Create the service file:

```fish
sudo nano /etc/systemd/system/topas-run-orchestration.service
```

Paste:

```ini
[Unit]
Description=TopasRunOrchestration SAFE web app
After=network-online.target remote-fs.target slurmctld.service slurmd.service
Wants=network-online.target

[Service]
Type=simple
User=fysiker
Group=fysiker
WorkingDirectory=/srv/cluster/tsebt/app

Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000

ExecStart=/usr/bin/dotnet /srv/cluster/tsebt/app/Server.dll

Restart=on-failure
RestartSec=5

KillSignal=SIGINT
TimeoutStopSec=30

NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
```

## 3. Enable and start the service

```fish
sudo systemctl daemon-reload
sudo systemctl enable topas-run-orchestration
sudo systemctl start topas-run-orchestration
```

## 4. Check service status

```fish
systemctl status topas-run-orchestration --no-pager
```

Live logs:

```fish
journalctl -u topas-run-orchestration -f
```

Recent logs:

```fish
journalctl -u topas-run-orchestration -n 100 --no-pager
```

## 5. Restart after deployment

After copying a new bundled deployment to `/srv/cluster/tsebt/app`, restart the service:

```fish
sudo systemctl restart topas-run-orchestration
systemctl status topas-run-orchestration --no-pager
```

## 6. Stop the service

```fish
sudo systemctl stop topas-run-orchestration
```

## 7. Verify listening port

```fish
ss -tulpn | grep 5000
```

Expected behavior:

```text
The app listens on 0.0.0.0:5000
```

## 8. Firewall check

Check firewall status:

```fish
sudo ufw status
```

If `ufw` is active and port 5000 is blocked:

```fish
sudo ufw allow 5000/tcp
sudo ufw status
```

## 9. Production config check

Before starting the service, confirm the production config exists:

```fish
cat /srv/cluster/tsebt/app/appsettings.json
```

It should contain:

```json
"AppRoot": "/srv/cluster/tsebt"
```

Node names should match Slurm node names:

```json
"Nodes": [
  { "Name": "monte-carlo-01", "Digit": "1" },
  { "Name": "monte-carlo-02", "Digit": "2" },
  { "Name": "monte-carlo-03", "Digit": "3" },
  { "Name": "monte-carlo-04", "Digit": "4" },
  { "Name": "monte-carlo-05", "Digit": "5" },
  { "Name": "monte-carlo-06", "Digit": "6" },
  { "Name": "monte-carlo-07", "Digit": "7" }
]
```

## 10. Operational notes

The service runs as:

```text
User=fysiker
Group=fysiker
```

This user must be able to read and write under:

```text
/srv/cluster/tsebt/templates
/srv/cluster/tsebt/inputs
/srv/cluster/tsebt/runs
/srv/cluster/tsebt/outputs
/srv/cluster/tsebt/database
/srv/cluster/tsebt/logs
```

The app should be started by `systemd` in production. Do not keep a manual `dotnet Server.dll` session running at the same time.

## 11. Quick command summary

```fish
sudo systemctl daemon-reload
sudo systemctl enable topas-run-orchestration
sudo systemctl start topas-run-orchestration
systemctl status topas-run-orchestration --no-pager
journalctl -u topas-run-orchestration -f
```
