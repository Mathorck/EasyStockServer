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
"${compose[@]}" logs --no-color server >"$run_dir/server.log" 2>&1 || true
"${compose[@]}" down -v --remove-orphans
rm -f "$run_dir/server.env" "$run_dir/project"
