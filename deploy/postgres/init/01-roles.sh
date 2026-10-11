#!/bin/sh
# Runs once when the PostgreSQL data directory is initialised (docker-entrypoint-initdb.d).
# Passwords come from PP_OWNER_PASSWORD / PP_APP_PASSWORD or, preferably in production, from files named by
# PP_OWNER_PASSWORD_FILE / PP_APP_PASSWORD_FILE (Docker secrets).
set -eu
read_secret() {
  file_var="$1_FILE"
  eval "file=\${$file_var:-}"
  if [ -n "$file" ]; then
    cat "$file"
  else
    eval "printf '%s' \"\${$1:?$1 or $1_FILE must be set}\""
  fi
}
owner_password=$(read_secret PP_OWNER_PASSWORD)
app_password=$(read_secret PP_APP_PASSWORD)
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db_name="$POSTGRES_DB" \
  -v owner_password="$owner_password" \
  -v app_password="$app_password" \
  -f /docker-entrypoint-initdb.d/roles.sql.inc
