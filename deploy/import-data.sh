#!/bin/sh
# Loads a PersonaOS data archive into this install's data volume: for moving to a new server, or
# for restoring a backup the updater made (./backups/before-*.tar.gz).
#
#   ./import-data.sh personaos-data.tar.gz
#
# The archive holds the database, attachments and the Data Protection keys that decrypt the stored
# keys; keep it private, and delete it once imported. Whatever the volume held before is replaced,
# so run this on a new install, or after taking a backup.
set -eu

archive="${1:?usage: ./import-data.sh <archive.tar.gz>}"
[ -f "$archive" ] || { echo "No such file: $archive" >&2; exit 1; }
folder=$(cd "$(dirname "$archive")" && pwd)
name=$(basename "$archive")
volume="${COMPOSE_PROJECT_NAME:-personaos}_api-data"

echo "Stopping the API..."
docker compose stop api

echo "Loading $name into $volume..."
docker volume create "$volume" >/dev/null
# 1654 is the API image's own non-root user ("app"); the files must be its to open.
docker run --rm -v "$volume:/data" -v "$folder:/in:ro" alpine:3.20 \
  sh -c "find /data -mindepth 1 -delete && tar xzf '/in/$name' -C /data && chown -R 1654:1654 /data"

echo "Starting the API..."
docker compose up -d api
echo "Done. Check the app, then delete $archive."
