#!/usr/bin/env bash
# End-to-end tests: starts a throwaway server, runs the app's E2E tests against it, stops the server.
# Usage: server/e2e/run.sh [app-dir]   (default: ../EasyStock next to this repository)
# Needs Docker, a JDK 21+ and the Android SDK (ANDROID_HOME) for the app build. See docs/05-e2e-tests.md.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
app="$(cd "${1:-$repo/../EasyStock}" && pwd)"

trap '"$here/stop.sh"' EXIT
"$here/start.sh"
# shellcheck disable=SC1091
source "$here/.run/server.env"

cd "$app"
./gradlew :shared:testAndroidHostTest --tests 'ch.mathorck.easystock.e2e.*' --rerun --console=plain \
    "-Peasystock.e2e.url=$EASYSTOCK_E2E_URL" "-Peasystock.e2e.setupToken=$EASYSTOCK_E2E_SETUP_TOKEN"
