# Systemd Deployment

## Purpose

TopasRunOrchestration should run as a long-lived server process managed by `systemd`, not from an interactive terminal session.

Why this is the preferred production model:

- automatic startup after reboot
- consistent working directory and startup environment
- restart policy for transient failures
- centralized logs through `journalctl`
- explicit operational control with `systemctl`
- reproducible and auditable behavior that does not depend on an SSH session staying open

For hospital or cluster deployment, this is the preferred operating model.

## Deployment model

- Build or publish the app first.
- Copy the deployable output to one stable deploy folder.
- Run the published server from that stable folder with `systemd`.
- Keep `appsettings.json` and related config files in the deploy folder or another documented stable path.
- If config changes, restart the service unless the app explicitly supports hot reload.
- If the service unit changes, run `sudo systemctl daemon-reload` before restarting.

Example stable deploy folder:

```text
/srv/cluster/tsebt/TopasRunOrchestration/deploy
```

## Installation flow

1. Publish or build the app.
2. Copy deploy files to the stable deploy folder.
3. Create `/etc/systemd/system/topas-run-orchestration.service`.
4. Run `sudo systemctl daemon-reload`.
5. Run `sudo systemctl enable topas-run-orchestration.service`.
6. Run `sudo systemctl start topas-run-orchestration.service`.
7. Verify status and logs.

## Example service unit

```ini
[Unit]
Description=Topas Run Orchestration
After=network.target

[Service]
WorkingDirectory=/srv/cluster/tsebt/TopasRunOrchestration/deploy
ExecStart=/usr/bin/dotnet /srv/cluster/tsebt/TopasRunOrchestration/deploy/Server.dll
Restart=on-failure
RestartSec=5
User=fysiker
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Create the unit file here:

```bash
/etc/systemd/system/topas-run-orchestration.service
```

## Operational commands

These commands are compatible with `bash` and `fish`:

```bash
sudo systemctl status topas-run-orchestration.service
sudo systemctl stop topas-run-orchestration.service
sudo systemctl start topas-run-orchestration.service
sudo systemctl restart topas-run-orchestration.service
journalctl -u topas-run-orchestration.service -f
sudo systemctl daemon-reload
```

## Example deployment sequence

```bash
dotnet publish src/Server/Server.fsproj -c Release -o ./deploy
sudo mkdir -p /srv/cluster/tsebt/TopasRunOrchestration/deploy
sudo cp -r ./deploy/* /srv/cluster/tsebt/TopasRunOrchestration/deploy/
sudoedit /etc/systemd/system/topas-run-orchestration.service
sudo systemctl daemon-reload
sudo systemctl enable topas-run-orchestration.service
sudo systemctl start topas-run-orchestration.service
sudo systemctl status topas-run-orchestration.service
journalctl -u topas-run-orchestration.service -f
```

## Operational notes

- `systemd` should run the published server executable or `dotnet` entrypoint from the stable deploy folder.
- Do not rely on `dotnet run` in an SSH session for production uptime.
- Use a documented deploy path so operations staff can audit what is running.
- Restart after application updates, config changes, or environment changes.

## Troubleshooting

- Service fails immediately:
  check `ExecStart`, `WorkingDirectory`, file permissions, and whether the service user can read the deploy folder.
- Browser cannot reach the app:
  check `ASPNETCORE_URLS`, firewall rules, reverse proxy configuration, and the client-facing port.
- Config changed but behavior is unchanged:
  restart the service.
- Unit changed but behavior is unchanged:
  run `sudo systemctl daemon-reload` and then restart the service.
- Port `5000` is already in use:

```bash
sudo ss -ltnp | grep ':5000'
```
