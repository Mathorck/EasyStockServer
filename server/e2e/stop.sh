#!/usr/bin/env bash
# Stops the end-to-end test server of start.sh: saves its log to e2e/.run/server.log, then removes the
# containers and their volumes.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
server="$(dirname "$here")"
run_dir="$here/.run"
project="${EASYSTOCK_E2E_PROJECT:-$(cat "$run_dir/project" 2>/dev/null || echo easystock-e2e)}"

# The compose file needs these variables even to stop
export EASYSTOCK_E2E_PORT=0 POSTGRES_PASSWORD=unused EASYSTOCK_PUBLIC_URL=http://unused
compose=(docker compose -p "$project" -f "$server/docker-compose.yml" -f "$here/compose.e2e.yml")
mkdir -p "$run_dir"
# The setup token is a secret: redact it before the log is kept or uploaded as a CI artifact
"${compose[@]}" logs --no-color server 2>&1 | sed -E 's/(Setup token: )[A-Za-z0-9_-]+/\1[redacted]/' >"$run_dir/server.log" || true
"${compose[@]}" down -v --remove-orphans
rm -f "$run_dir/server.env" "$run_dir/project"
