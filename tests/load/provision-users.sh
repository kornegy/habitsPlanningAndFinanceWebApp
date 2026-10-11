#!/bin/sh
# Registers N load-test accounts through the API and marks them confirmed directly in the database.
# Only for DISPOSABLE environments (it writes to the database behind the application's back), so it refuses to
# run unless LOADTEST_DISPOSABLE=yes. Prints the e-mail addresses, one per line.
#
#   LOADTEST_DISPOSABLE=yes tests/load/provision-users.sh http://127.0.0.1:18081 30 <docker compose args...>
set -eu
[ "${LOADTEST_DISPOSABLE:-}" = "yes" ] || { echo "Refusing: set LOADTEST_DISPOSABLE=yes for a disposable environment." >&2; exit 1; }
base="${1:?usage: $0 BASE_URL COUNT [docker compose args...]}"
count="${2:?usage: $0 BASE_URL COUNT [docker compose args...]}"
shift 2
password="${LOADTEST_PASSWORD:-load test password 2026}"
run=$(date +%s)
i=1
while [ "$i" -le "$count" ]; do
  email="load-$run-$i@example.test"
  status=$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"password\":\"$password\",\"displayName\":\"Load $i\",\"timeZoneId\":\"Europe/Berlin\",\"locale\":\"en-US\"}" \
    "$base/api/v1/auth/register")
  [ "$status" = "202" ] || { echo "register $email -> $status" >&2; exit 1; }
  echo "$email"
  i=$((i + 1))
done
docker compose "$@" exec -T db psql -U postgres -d personal_platform -q -c \
  "UPDATE app.users SET email_confirmed = true WHERE email LIKE 'load-$run-%@example.test'" >&2
