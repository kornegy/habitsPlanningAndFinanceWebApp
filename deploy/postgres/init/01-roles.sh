#!/bin/sh
# Runs once when the PostgreSQL data directory is initialised (docker-entrypoint-initdb.d).
set -eu
: "${PP_OWNER_PASSWORD:?PP_OWNER_PASSWORD must be set}"
: "${PP_APP_PASSWORD:?PP_APP_PASSWORD must be set}"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db_name="$POSTGRES_DB" \
  -v owner_password="$PP_OWNER_PASSWORD" \
  -v app_password="$PP_APP_PASSWORD" \
  -f /docker-entrypoint-initdb.d/roles.sql.inc
