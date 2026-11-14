#!/usr/bin/env bash
# Starts a fresh EasyStock server and PostgreSQL in Docker for the end-to-end tests (docs/05-e2e-tests.md).
# Builds the image from server/Dockerfile, waits until the server is healthy, reads the setup token from
# its log and writes EASYSTOCK_E2E_URL and EASYSTOCK_E2E_SETUP_TOKEN to e2e/.run/server.env.
# Stop it with stop.sh (removes the containers and their volumes).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
server="$(dirname "$here")"
run_dir="$here/.run"
project="${EASYSTOCK_E2E_PROJECT:-easystock-e2e}"
mkdir -p "$run_dir"

# A free port on 127.0.0.1, unless one is given
port="${EASYSTOCK_E2E_PORT:-$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])')}"
url="http://127.0.0.1:$port"

export EASYSTOCK_E2E_PORT="$port"
export POSTGRES_PASSWORD="e2e-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
export EASYSTOCK_PUBLIC_URL="$url"
export EASYSTOCK_LOG_LEVEL="${EASYSTOCK_LOG_LEVEL:-Information}"
# Test-only values. docker compose also reads server/.env, so a real fingerprint or proxy list there would
# otherwise end up in the test server. Variables set here win over the .env file.
export EASYSTOCK_CERT_FINGERPRINT=""
export EASYSTOCK_TRUSTED_PROXIES=""

compose=(docker compose -p "$project" -f "$server/docker-compose.yml" -f "$here/compose.e2e.yml")
# Leftovers of an earlier run that was not stopped
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
echo "$project" >"$run_dir/project"

echo "Building and starting the server on $url ..."
if ! "${compose[@]}" up -d --build --wait --wait-timeout 180; then
    "${compose[@]}" logs --no-color server 2>&1 | sed -E 's/(Setup token: )[A-Za-z0-9_-]+/\1[redacted]/' >"$run_dir/server.log" || true
    echo "The server did not become healthy, see $run_dir/server.log" >&2
    exit 1
fi

# The setup token is printed once at the first start (docs/03-server-spec.md, "First start")
token=""
for _ in $(seq 1 30); do
    token="$("${compose[@]}" logs --no-color server 2>/dev/null | sed -n 's/.*Setup token: \([A-Za-z0-9_-]*\).*/\1/p' | tail -n 1)"
    [ -n "$token" ] && break
    sleep 1
done
if [ -z "$token" ]; then
    echo "No setup token in the server log" >&2
    exit 1
fi

# Keep the token out of the GitHub Actions step logs too
[ -z "${GITHUB_ACTIONS:-}" ] || echo "::add-mask::$token"

cat >"$run_dir/server.env" <<ENV
EASYSTOCK_E2E_URL=$url
EASYSTOCK_E2E_SETUP_TOKEN=$token
ENV
echo "Server ready: $url (settings in $run_dir/server.env)"
