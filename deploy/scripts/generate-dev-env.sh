#!/bin/sh
# Generates a .env file with random LOCAL DEVELOPMENT secrets for docker compose.
# Production secrets must come from a secret manager, never from this script.
set -eu
cd "$(dirname "$0")/../.."
if [ -f .env ]; then
  echo ".env already exists; delete it first to regenerate." >&2
  exit 1
fi
rand() { head -c "$1" /dev/urandom | base64 | tr -d '\n/+=' | cut -c1-"$2"; }
umask 077
cat > .env <<ENV
POSTGRES_SUPERUSER_PASSWORD=$(rand 48 40)
PP_OWNER_PASSWORD=$(rand 48 40)
PP_APP_PASSWORD=$(rand 48 40)
JWT_KEY_ID=dev-$(date +%Y%m%d)
JWT_SIGNING_KEY=$(head -c 32 /dev/urandom | base64 | tr -d '\n')
ADMIN_EMAIL=
ENV
echo "Created .env with random local secrets (mode 600)."
