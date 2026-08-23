# Backup & restore

PersonaOS uses embedded **SQLite**, so persistence is just files — but that means
*you* own the durability story. This is the runbook.

## What makes up "your data"

Everything lives in the API's data directory (the `api-data` volume, mounted at
`/app/data`):

| Path | What it is | Losing it means |
|---|---|---|
| `personaos.db` (+ `-wal`, `-shm`) | The whole database: config, nickname, provider settings, chat, goals, planner, reminders, document metadata, and the **encrypted API key** | All data gone |
| `docs-storage/` | Uploaded document files | Documents gone |
| `dp-keys/` | Data Protection keyring — the key that **decrypts** the API key stored in the db | The stored API key becomes unreadable (re-enter it in Settings) |

> **Back these up together.** The encrypted key (in `personaos.db`) is useless
> without its keyring (`dp-keys/`). A backup that captures one but not the other
> can't restore a working instance — you'd restore the data and then re-enter the
> provider key in Settings.

## Layer 1 — the built-in nightly snapshot (on by default)

The API runs a `BackupService` that, roughly once a day, writes a self-contained
snapshot to `Backup__Path` (default `/app/data/backups/<timestamp>/`):

- `personaos.db` — a clean `VACUUM INTO` copy (safe while the app runs)
- `dp-keys/` and `docs-storage/` — copied alongside, so each snapshot is a
  complete, restorable set
- older snapshots beyond `Backup__KeepDays` (default 14) are pruned

Tune it with env: `Backup__Enabled`, `Backup__Path`, `Backup__KeepDays`.

> ⚠️ **The default path is inside `api-data`.** That protects against corruption and
> accidental deletes, but **not** against losing the volume/disk itself. For real
> resilience, mount `Backup__Path` on a **different disk or host**:
>
> ```yaml
> # docker-compose.yml → api service
> volumes:
>   - api-data:/app/data
>   - /srv/personaos-backups:/app/data/backups   # a separate disk / NAS mount
> ```

## Layer 2 — off-host continuous replication (Litestream, optional)

For near-zero data loss (seconds, not "since last night"), replicate `personaos.db`
continuously to an S3-compatible target with the Litestream overlay:

```bash
# set LITESTREAM_* in .env first (bucket, endpoint, keys)
docker compose -f docker-compose.yml -f docker-compose.litestream.yml up -d
```

Litestream streams the WAL off-host as it's written. **It covers only
`personaos.db`** — keep the nightly snapshot (Layer 1) running for `docs-storage/`
and `dp-keys/`, or replicate those with your own tool (`restic`, `rclone`).

## Layer 3 — storage-level redundancy (recommended for the paranoid)

Put the `api-data` volume (or its bind-mount host path) on redundant storage:
**RAID-1** or a **ZFS/btrfs mirror**. Add scheduled **snapshots** (ZFS/btrfs/LVM)
for point-in-time rollback, and `zfs send` them off-host. This is the gold
standard and complements — not replaces — Layers 1–2.

## Restore

**From a nightly snapshot** (Layer 1): stop the API, replace the live files with a
snapshot folder's contents, restart.

```bash
docker compose stop api
# copy the chosen snapshot back over the live data (inside the volume)
#   personaos.db  → /app/data/personaos.db   (delete stale -wal/-shm first)
#   dp-keys/      → /app/data/dp-keys/
#   docs-storage/ → /app/data/docs-storage/
docker compose start api
```

**From Litestream** (Layer 2): restore the db into a fresh/empty data dir, then
restore `dp-keys/` + `docs-storage/` from your Layer-1 snapshot, then start.

```bash
litestream restore -config deploy/litestream.yml /app/data/personaos.db
```

**Sanity checks after any restore:**
- On boot the API runs `PRAGMA integrity_check`; a failure is logged loudly.
- If the keyring was lost, the app still runs — just open **Settings** and
  re-enter the provider API key (keyless local providers need nothing).

## Rule of thumb

- **Minimum:** nightly snapshot (Layer 1) written to a *separate* disk/host.
- **Good:** + Litestream (Layer 2) for the database.
- **Gold:** + a ZFS/btrfs mirror with off-host snapshots (Layer 3).
