### General settings

- App uses SQLite as local persistent memory.
- SQLite stores run metadata, statuses, timestamps, paths, exit codes, and collection summaries.
- Large simulation files remain on disk; database stores only references.
- On startup, server creates required folders and initializes SQLite schema if missing.
- App configuration is file/environment based, not edited through the web UI initially.
- Windows is the development environment.
- Ubuntu 24.04 is the target runtime host.
- Deployment target is a published app folder copied to Linux, then optionally managed by systemd.

