#!/bin/sh
# Creates the secret files used by deploy/production/docker-compose.prod.yml in DIRECTORY on the target host.
# Run once per environment. Existing files are never overwritten (rotating a secret is a deliberate, manual step).
# If you use a secret manager, write the same file names from it instead of running this script.
#
#   sudo deploy/scripts/generate-prod-secrets.sh /etc/personal-platform/secrets
#
# Files: pg_superuser_password, pp_owner_password, pp_app_password, app_connection_string,
# migrations_connection, jwt_signing_key, smtp_password (empty placeholder: put the real SMTP password in it),
# dataprotection.pfx + dataprotection_certificate_password (certificate that encrypts Data Protection keys at rest).
set -eu
dir="${1:?usage: $0 DIRECTORY}"
umask 077
mkdir -p "$dir"
chmod 700 "$dir"

rand() { head -c 48 /dev/urandom | base64 | tr -d '\n/+=' | cut -c1-40; }
create() { # name value
  if [ -e "$dir/$1" ]; then
    echo "keep     $1 (already exists)"
  else
    printf '%s' "$2" > "$dir/$1"
    echo "created  $1"
  fi
}
read_file() { cat "$dir/$1"; }

create pg_superuser_password "$(rand)"
create pp_owner_password "$(rand)"
create pp_app_password "$(rand)"
create app_connection_string "Host=db;Database=personal_platform;Username=pp_app;Password=$(read_file pp_app_password);Timeout=15;Command Timeout=30;Maximum Pool Size=50"
create migrations_connection "Host=db;Database=personal_platform;Username=pp_owner;Password=$(read_file pp_owner_password)"
create jwt_signing_key "$(head -c 32 /dev/urandom | base64 | tr -d '\n')"
create smtp_password ""
create dataprotection_certificate_password "$(rand)"

if [ -e "$dir/dataprotection.pfx" ]; then
  echo "keep     dataprotection.pfx (already exists)"
else
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes -subj "/CN=personal-platform-data-protection" \
    -keyout "$tmp/key.pem" -out "$tmp/cert.pem" 2>/dev/null
  openssl pkcs12 -export -inkey "$tmp/key.pem" -in "$tmp/cert.pem" -out "$dir/dataprotection.pfx" \
    -passout "file:$dir/dataprotection_certificate_password"
  echo "created  dataprotection.pfx"
fi

# The directory (700, root) keeps other host users out; the files must be readable by the container users
# (app: uid 1654, postgres: uid 70), which do not share a group with the host, hence 444 inside the 700 directory.
chmod 444 "$dir"/*
echo "Done. Back up $dir securely: without dataprotection.pfx, stored Data Protection keys cannot be decrypted."
