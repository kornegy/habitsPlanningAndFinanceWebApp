#!/bin/sh
# Creates a consistent, compressed logical backup (pg_dump custom format) of the production database.
#
#   deploy/scripts/backup.sh /var/backups/personal-platform [compose args...]
#
# Compose args default to the production stack:  -f deploy/production/docker-compose.prod.yml --env-file /etc/personal-platform/prod.env
# If BACKUP_AGE_RECIPIENT is set (an age public key), the dump is encrypted with `age` before it touches disk.
# Schedule it (cron/systemd timer), copy the files off-host, and test restores regularly with restore.sh.
set -eu
target="${1:?usage: $0 BACKUP_DIRECTORY [docker compose args...]}"
shift
if [ "$#" -eq 0 ]; then
  set -- -f deploy/production/docker-compose.prod.yml --env-file /etc/personal-platform/prod.env
fi
umask 077
mkdir -p "$target"
stamp=$(date -u +%Y%m%dT%H%M%SZ)
file="$target/personal_platform-$stamp.dump"

# pg_dump runs inside the database container over the local socket; nothing is exposed on the network.
if [ -n "${BACKUP_AGE_RECIPIENT:-}" ]; then
  docker compose "$@" exec -T db pg_dump -U postgres -d personal_platform --format=custom --compress=6 --no-owner \
    | age -r "$BACKUP_AGE_RECIPIENT" > "$file.age"
  file="$file.age"
else
  docker compose "$@" exec -T db pg_dump -U postgres -d personal_platform --format=custom --compress=6 --no-owner > "$file"
fi

size=$(wc -c < "$file")
if [ "$size" -lt 1024 ]; then
  echo "Backup $file is suspiciously small ($size bytes); check the output above." >&2
  exit 1
fi
sha256sum "$file" > "$file.sha256"
echo "Backup written: $file ($size bytes)"
