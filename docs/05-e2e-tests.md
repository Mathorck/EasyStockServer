# End-to-end tests

The end-to-end (E2E) tests run the app's real client code against a real server. The client code is
`ServerClient`, the join, account, admin and sync APIs, and `SyncEngine` with its Room database. The
server is the Docker image of `server/Dockerfile` with PostgreSQL. The tests are in the app repository,
package `ch.mathorck.easystock.e2e` of `shared/src/androidHostTest`. They run on the host JVM, so no
emulator is needed.

## Running

```bash
server/e2e/run.sh [app-dir]     # default app-dir: ../EasyStock next to this repository
```

`run.sh` does three things:

1. Starts a fresh server with `start.sh`.
2. Runs `./gradlew :shared:testAndroidHostTest --tests 'ch.mathorck.easystock.e2e.*'` in the app.
3. Stops the server with `stop.sh`, even when a test fails.

Prerequisites:

* Docker with Compose v2.
* Python 3, used to find a free port.
* JDK 21 or newer.
* The Android SDK. Set `ANDROID_HOME`, or set `sdk.dir` in the app's `local.properties`.

The test reports are in `<app-dir>/shared/build/reports/tests/testAndroidHostTest/`. The server log is
saved to `server/e2e/.run/server.log`.

### By hand

```bash
server/e2e/start.sh                       # prints the URL and writes server/e2e/.run/server.env
source server/e2e/.run/server.env
cd ../EasyStock
./gradlew :shared:testAndroidHostTest --tests 'ch.mathorck.easystock.e2e.*' \
    -Peasystock.e2e.url=$EASYSTOCK_E2E_URL -Peasystock.e2e.setupToken=$EASYSTOCK_E2E_SETUP_TOKEN
server/e2e/stop.sh                        # removes the containers and their volumes
```

You can run a single test class again on the same server. The first run sets the server up, and every
later run logs in as its Admin (`admin` / `admin-password-0123456789`). The other accounts get new names
on every run.

Without `-Peasystock.e2e.url`, or the `EASYSTOCK_E2E_URL` variable, every E2E test is skipped. A normal
app test run therefore needs no server.

## The throwaway server

`start.sh` builds the image. It starts it with `docker-compose.yml` and the test override
`e2e/compose.e2e.yml`, which:

* uses the project name `easystock-e2e` (change it with `EASYSTOCK_E2E_PROJECT`);
* listens on a free port of 127.0.0.1 with plain HTTP (set the port with `EASYSTOCK_E2E_PORT`);
* never restarts;
* uses fast health checks;
* uses a random database password.

`start.sh` then waits until the server is healthy. It reads the setup token from the log and writes
`EASYSTOCK_E2E_URL` and `EASYSTOCK_E2E_SETUP_TOKEN` to `e2e/.run/server.env`. The server runs with its
normal configuration: the tests need no server option.

## What is covered

| Test class | Scenarios |
|---|---|
| `AuthE2eTest` | Server info and the version headers: an old app gets `app-too-old`. Setup, login, refresh with token rotation, and logout. |
| `RolesE2eTest` | An invite read from its QR code, then redeemed. A Member pushes. A Viewer's phone never pushes, and the server answers `forbidden` to a Viewer's push. |
| `SyncE2eTest` | Two phones end with the same data. The edits cover categories, a location tree, stock in, a transfer, serial units, a reparent and soft deletes. In a conflict the first change wins: the loser keeps the server row and gets a `conflict:` rejected change. |
| `AdminE2eTest` | The user list (username, disabled). A disabled user loses the session. A server backup is read with the app's backup code. A replace import resets the data set: the other phone sees it from server info before it pushes, drops its unsent change and pulls again. |
| `AccountE2eTest` | Changing the password ends the other sessions and keeps this one. Your own display name and the Admin's rename are seen in the user list, in `me` and on the phone. |
| `ImageE2eTest` | A photo is uploaded by one phone and downloaded by another, byte for byte. |

## Limits to know

* The auth endpoints allow 10 requests a minute per IP. The tests retry a `rate-limited` answer after
  5 seconds, so a full run takes about a minute longer than its work.
* On the host JVM the stub of `android.database.SQLException` from android.jar breaks the SQLite driver.
  The app's host tests ship a working copy of it, for the tests only.
