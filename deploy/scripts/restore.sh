#!/bin/sh
# Restores a backup made by backup.sh into a NEW database and checks it. It never touches the live database:
# the target database must not exist yet. Use it for regular restore drills, and for disaster recovery
# (then point the application at the restored database or rename it during a maintenance window).
#
#   deploy/scripts/restore.sh BACKUP_FILE [TARGET_DATABASE] [compose args...]
#
# Encrypted backups (*.age) are decrypted with `age -d -i "$BACKUP_AGE_IDENTITY"`.
set -eu
file="${1:?usage: $0 BACKUP_FILE [TARGET_DATABASE] [docker compose args...]}"
database="${2:-personal_platform_restore_$(date -u +%Y%m%d%H%M%S)}"
shift; [ "$#" -gt 0 ] && shift
if [ "$#" -eq 0 ]; then
  set -- -f deploy/production/docker-compose.prod.yml --env-file /etc/personal-platform/prod.env
fi
case "$database" in
  personal_platform) echo "Refusing to restore into the live database name 'personal_platform'." >&2; exit 1 ;;
  *[!a-z0-9_]*) echo "Target database name may contain only a-z, 0-9 and _." >&2; exit 1 ;;
esac

if [ -f "$file.sha256" ]; then
  sha256sum -c "$file.sha256"
fi


exists=$(docker compose "$@" exec -T db psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$database'")
if [ "$exists" = "1" ]; then
  echo "Database '$database' already exists; choose another name. Nothing was changed." >&2
  exit 1
fi

docker compose "$@" exec -T db createdb -U postgres -O pp_owner "$database"
case "$file" in
  *.age) age -d -i "${BACKUP_AGE_IDENTITY:?set BACKUP_AGE_IDENTITY}" "$file" ;;
  *) cat "$file" ;;
esac | docker compose "$@" exec -T db pg_restore -U postgres -d "$database" --no-owner --role=pp_owner --exit-on-error

echo "Restored into '$database'. Row counts:"
docker compose "$@" exec -T db psql -U postgres -d "$database" -tA -F ' ' -c "
  SELECT 'users', count(*) FROM app.users
  UNION ALL SELECT 'tasks', count(*) FROM app.tasks
  UNION ALL SELECT 'habits', count(*) FROM app.habits
  UNION ALL SELECT 'transactions', count(*) FROM app.transactions
  UNION ALL SELECT 'ledger_entries', count(*) FROM app.ledger_entries
  UNION ALL SELECT 'notes', count(*) FROM app.notes"
echo "Ledger check (every transfer must net to zero per currency):"
docker compose "$@" exec -T db psql -U postgres -d "$database" -tA -c "
  SELECT count(*) AS unbalanced_transfers FROM (
    SELECT t.id FROM app.transactions t JOIN app.ledger_entries e ON e.transaction_id = t.id
    WHERE t.kind = 2 AND t.destination_currency_code = t.currency_code
    GROUP BY t.id HAVING sum(e.amount) <> 0) x"
