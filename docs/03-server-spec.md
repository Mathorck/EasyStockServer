# EasyStock: Server Specification

What the optional EasyStock server is, how it is built, and how it behaves. The overall plan is in `01-server-mode-plan.md` and the app changes in `02-app-changes.md`.

## Purpose

* The server lets several phones share one stock, with users, roles, movement history and low-stock alerts.
* It is optional: the app works fully without it, and standalone stays the default.
* It is open source (MIT, like the app) and meant to be hosted by anyone: a small business, an association, a hobbyist.
* It must run on modest hardware (a small VPS, a NAS, a Raspberry Pi 4 or newer).
* It stores as little personal data as possible, in line with the Swiss nFADP and the GDPR.

## Stack

* .NET 10 (LTS) with ASP.NET Core minimal APIs.
* EF Core with the Npgsql provider, on PostgreSQL 16 or newer.
* ASP.NET Core Identity for users, password hashing and lockout only.
* `Microsoft.AspNetCore.Authentication.JwtBearer` to validate access tokens.
* `Microsoft.AspNetCore.OpenApi` to publish the API description.
* The built-in ASP.NET Core rate limiter.
* xUnit and Testcontainers (PostgreSQL) for tests.

## Repository layout

* The server lives in the same repository as the app, in a `server/` folder, so one pull request can change both sides.
* `server/src/EasyStock.Server/` is the web API.
* `server/tests/EasyStock.Server.Tests/` holds the tests.
* `server/Dockerfile` and `server/docker-compose.yml` are for hosting.
* `docs/schema.dbml` stays the single source of truth for the shared tables.
* `docs/test-cases/merge/*.json` holds the conflict test cases shared with the app.

## Inside the web API

* `Endpoints/` has one file per group: `ServerInfo`, `Auth`, `Me`, `Sync`, `Images`, `Push`, `Admin`.
* `Data/` has the EF Core `DbContext`, entity classes and migrations.
* `Sync/` has the push processor, the pull builder, the conflict rules and the Guest filter.
* `Auth/` has token creation, refresh-token handling, invites, the public QR code and promotions.
* `Alerts/` has the low-stock crossing detector and the push sender.
* `Jobs/` has the background cleanup service.
* `Permissions.cs` lists what each role can do, with the same content as the app's `Permissions` object.

## Configuration

* All settings come from environment variables, with safe defaults.
* `EASYSTOCK_DB`: PostgreSQL connection string (required).
* `EASYSTOCK_PUBLIC_URL`: the address phones use, written into QR codes (required). Checked at startup with the app's rules: lower-case `http://` or `https://`, a host, an optional port and path, no user info, query or fragment.
* `EASYSTOCK_DATA_DIR`: folder for photos, the signing key and data-protection keys (default `/data`).
* `EASYSTOCK_CERT_FINGERPRINT`: SHA-256 of a self-signed certificate, written into QR codes (optional). 64 hex characters, `:` separators allowed; anything else fails startup. Only written for an `https://` public URL (the app refuses `fp` on http); with http it is ignored with a warning.
* `EASYSTOCK_TRUSTED_PROXIES`: comma-separated IPs or CIDR ranges whose `X-Forwarded-For` is believed. Ranges wider than /8 (IPv4) or /64 (IPv6), or than /104 inside `::ffff:0:0/96`, fail startup.
* `EASYSTOCK_LOG_LEVEL`: log level (default `Information`).
* Everything else (the server name) is set by an Admin in the app and stored in the database.

## Shared tables

* Every table from `docs/schema.dbml` exists on the server with the same columns: `category`, `attribute_definition`, `category_attribute`, `article`, `location`, `item`, `stock_movement`, `stock_review`, `stock_review_line`.
* JSON columns use `jsonb`, quantities use `bigint` thousandths like the app, timestamps are UTC.
* Every shared table gets a `server_seq bigint not null` column, indexed, filled from one global sequence.
* `server_seq` is set on every insert and every update, by the push processor.
* `stock_movement` gets `created_by` (user ID) and `device_id`, set by the server from the access token, never from the phone.
* `article.image_path` is replaced on the server by `image_sha256`, since the server stores photos by hash.
* Unique indexes match the app (`sku`, `barcode`, category name, attribute key, serial number) and are not filtered: the app's schema has no filter, and a value the app clears on soft delete is NULL, which PostgreSQL never counts as a duplicate.

## Server-only tables

* `users` (Identity table, extended): display name, role, kind (`full` or `guest`), short code, created at, last seen, disabled at.
* `device_session`: ID, user ID, device name, refresh token hash, previous token hash (for reuse detection and its grace window), created at, last used, revoked at.
* `invite`: token hash, role, created by, created at, expires at, used at.
* `public_invite` (one row): token hash, enabled, Guest limit, created at.
* `promotion`: user ID, target role, created by, created at, expires at, completed or cancelled at.
* `password_reset`: token hash, user ID, created at, expires at, used at.
* `sync_batch`: batch ID, device session ID, received at, a hash of the rows, and the saved response, so a repeated batch gets the same answer (the same ID with other rows gets 409 `batch-id-reused`).
* `attribute_alias`: attribute IDs merged into the server attribute with the same key, so later batches using the old ID are redirected.
* `low_stock_alert`: ID, article ID, crossed at, stock at that moment, caused by user ID, `server_seq`.
* `low_stock_state`: article ID and whether it is currently at or below its threshold, used to detect crossings.
* `push_registration`: device session ID and UnifiedPush endpoint URL.
* `image`: SHA-256, size, created at.
* `setting`: key and value, for the Admin settings and the `dataset_epoch` (not editable by the Admin).
* `setup_state` (one row): setup token hash and whether setup is done.

## First start

* The server applies EF Core migrations automatically at start.
* If no signing key exists in `EASYSTOCK_DATA_DIR`, it generates one (ECDSA P-256) and saves it there.
* If no user exists, it generates a setup token and writes it to the log, with a `setup` QR code drawn in text.
* The setup token stays valid until the first Admin is created, and a restart before that prints a new one.
* After setup, `POST /auth/setup` answers 409 forever.

## Authentication

### Accounts

* Usernames are 3 to 32 characters, letters, digits, dots, dashes and underscores, compared without case.
* Display names are 1 to 50 characters, free text.
* No email address is stored.
* Passwords need at least 10 characters and at most 128, with no other rule.
* Passwords are hashed by Identity's default password hasher.
* An account locks for 15 minutes after 5 failed passwords from one client address (IPv4 address, or IPv6 /64), for that address only: someone who knows a username cannot lock its owner out from elsewhere. Wrong current passwords on `POST /me/password` count too.
* 50 failed passwords for one account within 15 minutes, from any addresses, lock it everywhere for 15 minutes, so guessing spread over many addresses stays bounded.
* The counters are kept in memory only (no IP address in the database) and a password reset clears them.
* Login answers `invalid-credentials` alike for an unknown user, a wrong password and a locked account, so it never tells whether an account exists.
* Guest accounts have no username and no password hash.
* The short code is 6 characters from an alphabet without look-alike characters (no 0/O, 1/I), derived from the user ID.

### Access tokens

* A JWT signed with the server's ECDSA key, valid 15 minutes.
* Claims: user ID (`sub`), role, device session ID, and expiry.
* The role claim is used by the app for its buttons; the server reads the role from the database on every write.

### Refresh tokens

* The session ID (16 bytes), a nonce (32 bytes) and a 16-byte HMAC tag, encoded in Base64URL. Sent to the phone once and stored only as a SHA-256 hash. The tag key is derived from the signing key, so only the server can make a token that names a session.
* Each refresh returns a new refresh token and keeps the old hash as "previous". The new token is derived from the session ID and the old hash; the first one has a random nonce.
* If a phone presents any replaced token of the session (the previous one or an older one), the session is revoked, since this means the token was copied.
* Grace window: the previous token presented again within 10 seconds of the rotation returns the current refresh token again (with a new access token) and does not revoke. This covers two refreshes sent at once and a retry after a lost answer.
* A token with a bad tag, or one issued before the signing key was replaced, is only looked up by hash: it cannot revoke a session.
* A session expires after 90 days without use.
* Revoking a session sets `revoked_at`; the next refresh answers 401 with the problem type `session-revoked`.
* Disabling a user revokes all their sessions.

### Rate limits

* Login, setup, invite redeem, reset, promotion and password change endpoints: 10 requests per minute per IP address.
* Guest signup: 5 accounts per hour per IP address, and the Guest limit set by the Admin.
* Other endpoints: 120 requests per minute per device session.

## Roles and rights

* Four roles: `admin`, `member`, `viewer`, `guest`.
* Every endpoint and every pushed row is checked against `Permissions.cs`.
* Guest: reads stock, articles, photos and locations; receives movements without author, device and comment; receives no reviews; writes nothing.
* Viewer: everything a Guest reads, plus full movements, authors and reviews; writes nothing.
* Member: everything a Viewer reads; creates movements; creates, edits, soft-deletes and restores articles, items, tags, locations (including the hierarchy), categories, attributes and templates; runs reviews.
* Admin: everything a Member does, plus users, invites, the printed QR code, devices, backups and settings.
* No role can update or delete a `stock_movement` row.
* The last active Admin cannot be demoted, disabled or deleted (409).
* A full account cannot be changed into a Guest.

## API conventions

* Base path: `/api/v1`.
* JSON bodies, camelCase names matching the app's `@SerialName` values, gzip accepted and returned.
* Times are ISO 8601 in UTC in the auth and admin endpoints; synced rows keep the app's epoch milliseconds.
* Errors are RFC 9457 Problem Details with a stable `type` (for example `session-revoked`, `app-too-old`, `invite-expired`, `not-allowed`).
* Requests must carry `X-EasyStock-App-Version` and `X-EasyStock-Schema-Version`.
* The schema version is the app's Room DB version. The server accepts the versions whose shared tables it matches (`docs/schema.dbml` in the app repo): currently 10 and 11 (11 only adds an app-only table). A drift test checks that the app's `DB_VERSION` is among them.
* Backups: an import accepts `schema_version` up to the highest supported version (11) and refuses a newer one as `backup-newer-version`. An export writes the lowest supported version (10), so every supported app can read it.
* An app below the minimum version, or with an unsupported schema version, gets 426 with type `app-too-old`.
* The OpenAPI description is served at `/openapi/v1.json` only in Development or when `EASYSTOCK_OPENAPI=true`. It is always written to `server/openapi/v1.json` at build time.
* `GET /health` answers 200 when the database is reachable, for Docker health checks.
* `/health` and `/openapi` are rate-limited to 30 requests per minute per IP.

## Endpoints

### Public

* `GET /api/v1/server-info` returns the server name, server version, API version, minimum app version, supported schema versions, whether setup is done, whether the server holds data, whether Guest signup is on, and the dataset epoch (`datasetEpoch`).

### Auth

* `POST /auth/setup` takes the setup token, username, display name, password and device name, and returns tokens.
* `POST /auth/invite/redeem` takes the invite token, username, display name, password and device name, and returns tokens.
* `POST /auth/guest-signup` takes the public token, display name and device name, and returns tokens.
* `POST /auth/login` takes the username, password and device name, and returns tokens.
* `POST /auth/refresh` takes the refresh token, and returns new tokens.
* `POST /auth/reset` takes the reset token, the new password and device name, revokes all old sessions, and returns tokens.
* `POST /auth/complete-promotion` takes a username and password from a Guest with a pending promotion, and applies the new role.
* `POST /auth/logout` revokes the current session, and deletes the account if it is a Guest.

Token answer:

```json
{
  "accessToken": "eyJ...",
  "accessTokenExpiresAt": "2026-11-11T12:30:00Z",
  "refreshToken": "q3V0...",
  "user": { "id": "…", "displayName": "Jean", "role": "member", "kind": "full", "shortCode": "K7-4QX" }
}
```

### Me

* `GET /me` returns the user, role, kind, short code, and the pending promotion (target role and expiry) if there is one.
* `PATCH /me` lets the user change their display name and their alert preference.
* `POST /me/password` lets a full account change its password with the current one.

### Sync

* `POST /sync/push` takes a batch ID and a list of rows grouped by table.
* `GET /sync/pull?since=<seq>&limit=<n>` returns rows with `server_seq` greater than `since`, at most `limit` (default 500, maximum 2000).

Push request:

```json
{
  "batchId": "6b0e…",
  "rows": {
    "category": [ { "id": "…", "name": "Hardware", "updatedAt": 1762859000000, "baseSeq": 18290 } ],
    "stock_movement": [ { "id": "…", "articleId": "…", "locationId": "…", "quantity": -2000, "reason": "consumption", "occurredAt": 1762859000000, "createdAt": 1762859000000 } ]
  }
}
```

* `baseSeq` (push only): on a row that edits or soft-deletes a row the server already has, the `serverSeq` of the server version the phone edited.
  The phone takes it from the row's `serverSeq` in the pull answer, or from the result's `serverSeq` after its own push.
  It is left out on a new row (and ignored there).

Push answer (one result per row):

```json
{
  "results": [
    { "table": "category", "id": "…", "status": "accepted", "serverSeq": 18342 },
    { "table": "article", "id": "…", "status": "changed", "conflict": "SKU_RENAMED", "row": { "sku": "BLT-8 (2)" }, "serverSeq": 18343 },
    { "table": "category", "id": "…", "status": "rejected", "reason": "not-allowed", "row": { }, "serverSeq": 18100 },
    { "table": "article", "id": "…", "status": "rejected", "reason": "conflict", "row": { "id": "…", "name": "Bolt M8", "…": "…" }, "serverSeq": 18301 }
  ]
}
```

* `serverSeq`: the row's `server_seq` on the server after this push (for a rejected row, the server row's). Left out when the server has no such row.
* `row` of a rejected result: the whole server row, or `{}` when there is none.
* `row` of an accepted movement: only `id`, `createdBy` and `deviceId`, the author the server set. The phone writes those two fields on its own movement (the only change a phone makes to a movement it already has). Other accepted rows have no `row`.

Pull answer:

```json
{
  "rows": { "article": [ ], "stock_movement": [ ] },
  "users": [ { "id": "…", "displayName": "Jean" } ],
  "alerts": [ { "id": "…", "articleId": "…", "crossedAt": 1762859000000, "stock": 4000 } ],
  "next": 18342,
  "hasMore": false,
  "datasetEpoch": "3f6c2a9e-0d1b-4c55-9a2e-7b1d8e4f0c11"
}
```

### Dataset epoch

* The server keeps a random UUID, the dataset epoch, in the `setting` table. It is created on the first start.
* It is returned as `datasetEpoch` in `GET /server-info` and in every pull answer.
* A replace backup import sets a new epoch, in the same transaction as the import. A merge import keeps it.
* Phones send the epoch they hold in `X-EasyStock-Dataset-Epoch` on `GET /sync/pull` and `POST /sync/push`.
* A different value gets 409 `dataset-reset`, with the current epoch as `datasetEpoch` in the problem body. The batch is not applied.
* A missing or empty header is accepted, for a first sync and for older apps. Such a phone is not protected against a replace.
* On 409 `dataset-reset` the app deletes its server-mode synced rows and cursor, stores the new epoch, and pulls from `since=0`.
* After a Guest is promoted, the server marks that session: its next pull gets 409 `dataset-reset` with the unchanged epoch, so the app re-pulls everything it can now see. The mark is cleared by that answer.
* A push refused with 409 is neither applied nor recorded. The app drops it: its rows belong to the old dataset (they can bring back rows the restore removed, or point to rows that no longer exist).
* Before wiping, the app tells the user how many local changes were not sent ("The server data was restored by an Admin: N unsent changes were discarded").
* The server caches the epoch in memory. It is loaded from the database at start (created there if missing) and updated after a replace import commits. `GET /sync/pull`, `POST /sync/push` and `GET /server-info` use the cached value. Push also re-reads it from the database under the sync lock, before the replay lookup, so a push that waited on an import does not apply to the new dataset.

### Images

* `HEAD /images/{sha256}` answers 200 if the server has the photo, 404 if not.
* `PUT /images/{sha256}` stores a JPEG of at most 5 MB (it must start with the SOI marker and end with the EOI marker), after checking that its hash matches the URL. Past the total `EASYSTOCK_IMAGE_QUOTA_MB` (default 2048) it answers 413 `image-quota-exceeded`.
* `GET /images/{sha256}` returns the photo, with a long cache time since the content never changes.
* Members and Admins can upload; every role can download.

### Push

* `POST /push/register` saves the UnifiedPush endpoint of the current session.
* `DELETE /push/register` removes it.

### Admin

* `GET /admin/users` lists users with role, kind, last seen, devices and pending promotion.
* `PATCH /admin/users/{id}` changes role, display name, or disables or enables the user.
* `DELETE /admin/users/{id}` deletes a user; their movements keep the user ID, and the display name becomes "Deleted user".
* `POST /admin/users/{id}/reset` creates a single-use reset token (valid 24 hours) and returns the `reset` QR content.
* `POST /admin/users/{id}/promote` takes the target role and the short code typed by the Admin, and creates a pending promotion.
* `DELETE /admin/users/{id}/promote` cancels a pending promotion.
* `GET /admin/devices` lists all sessions, and `DELETE /admin/devices/{id}` revokes one.
* `POST /admin/invites` takes a role and returns the `invite` QR content; `GET` lists open invites; `DELETE /admin/invites/{id}` cancels one.
* `GET /admin/public-invite` returns the state of the printed QR code.
* `PUT /admin/public-invite` turns it on or off and sets the Guest limit.
* `POST /admin/public-invite/regenerate` creates a new token, making old printed sheets stop working, and returns the new QR content.
* `GET /admin/backup` returns a backup file in the app's backup format, with photos.
* `POST /admin/backup?mode=merge|replace` imports a backup file into the server. A replace sets a new dataset epoch.
* `GET /admin/settings` and `PUT /admin/settings` read and change the server settings.

## Push processing

* The whole batch runs in one database transaction.
* The transaction first takes a PostgreSQL advisory lock, so only one push runs at a time.
* This keeps `server_seq` values visible in order: without it, a slow push could commit a lower number after a pull already moved past it.
* At 1 to 5 users, pushes are small and rare, so one push at a time costs nothing noticeable.
* If the batch ID is already in `sync_batch`, the saved answer is returned and nothing is applied again.
* Rows are applied in dependency order: `category`, `attribute_definition`, `category_attribute`, `location`, `article`, `item`, `stock_movement`, `stock_review`, `stock_review_line`.
* Each row is checked: allowed for this role, valid values (enums, lengths, quantities), and referenced rows exist.
* A row that fails a check is rejected with a reason, and the server's current version of that row is returned if there is one.
* A movement whose ID already exists is ignored and counted as accepted, since movements never change.
* A movement row's `created_by` and `device_id` come from the token, whatever the phone sent. They are returned in the result's `row`.
* Other rows: the first change wins (optimistic concurrency, checked under the push lock).
  * An edit or soft delete of an existing row must carry `baseSeq` equal to the row's current `server_seq`.
  * Otherwise (the row changed since the phone saw it, or no `baseSeq`) it is rejected with reason `conflict`, the whole server row and its `serverSeq`.
  * The app shows the conflict (keep mine / keep theirs). "Keep mine" pushes the row again with `baseSeq` set to the returned `serverSeq`; "keep theirs" takes the returned row.
  * The phone's `updated_at` plays no part. A row identical to the server's is accepted whatever its `baseSeq`.
  * Permission, value and reference checks come first: a row that can never be applied gets their reason, not `conflict`.
  * A new ID is never a conflict. A `baseSeq` that is not a whole number is `invalid`.
  * `conflict` is also the reason of the rare database-level failure the checks missed.
* `attribute_definition.key` cannot change once created: a different key for the same ID keeps the server's key (`ATTRIBUTE_KEY_KEPT`).
* A movement is never changed: a push with an existing movement ID and the same content is `accepted`, with different content it is `changed` with `conflict` `MOVEMENT_KEPT` and the server's movement as `row`.
* Only one open `stock_review` per location: a second one is rejected.
* Every accepted or changed row gets a new `server_seq`, returned as `serverSeq` in its result.

## Conflict rules

* The rules are the same as the app's `BackupMerge`, ported to C#.
* Except for the same row changed on both sides: push uses first change wins (see "Push processing"), not BackupMerge's newer `updatedAt`.
* Category name already used: the incoming category gets " (2)", " (3)"… (`CATEGORY_RENAMED`).
* SKU already used: the incoming article gets a suffix (`SKU_RENAMED`).
* NFC tag already on another article or location: removed from the incoming row (`BARCODE_CLEARED`).
* Serial number already used: the incoming unit gets a suffix (`SERIAL_RENAMED`).
* Same attribute key with another ID: both are treated as the same attribute (`ATTRIBUTE_MERGED`).
* Category already has this attribute: the incoming link is skipped (`CATEGORY_ATTRIBUTE_SKIPPED`).
* A parent that would make a location its own ancestor: the server's parent is kept (`LOCATION_PARENT_KEPT`).
* A movement with the same ID but different content: the server's movement is kept (`MOVEMENT_KEPT`).
* Suffixed values respect the column maximum lengths, like `BackupMerge` does.
* The test cases in `docs/test-cases/merge/*.json` are run by both the Kotlin and the C# tests.

## Pull building

* Rows are read in `server_seq` order and grouped by table.
* Soft-deleted rows are included, so phones learn about deletions.
* Display names of the users referenced by the returned movements are added in `users`.
* A rename or deletion of a user who authored movements is sent too, in the page where the user row changed, so phones that already hold those movements update the name. A user no movement names (a Guest, a user who never moved stock) is never sent.
* New `low_stock_alert` rows for this user are added in `alerts`.
* For a Guest: movements are sent without `created_by`, `device_id` and `comment`; `stock_review` and `stock_review_line` are left out; `users` and `alerts` are empty.
* The Guest filter is applied in one place, the pull builder, and covered by tests.

## Low-stock alerts

* During each push, the server lists the quantity-tracked articles touched by the batch (movements or threshold changes).
* After applying the batch, it computes their stock with `SUM(quantity)` and compares it with the threshold.
* If an article goes from above its threshold to at or below it, the server inserts one `low_stock_alert` and marks it low in `low_stock_state`.
* If an article goes back above its threshold, `low_stock_state` is cleared, so it can alert again.
* Articles with `tracking = serial`, archived or soft-deleted articles, and articles without a threshold never alert.
* Recipients: `users.alerts_enabled` decides; null means the role default (Admin and Member on, Viewer off). Guests, disabled and deleted users never receive alerts. The rule lives in `AlertRecipientsHelper`.
* The user who pushed the batch is not a recipient of the alerts it caused.
* After the transaction, the server sends a "sync now" push to the recipients that registered a UnifiedPush endpoint.

## Push sending

* The server sends an HTTP POST with a tiny body (`sync`) to each registered UnifiedPush endpoint.
* Push is also sent to a Guest's phone when an Admin promotes them, and to all phones after an Admin changes the catalogue.
* A failed push is logged and ignored, since the 15-minute background sync covers it.
* An endpoint that answers 404 or 410 is deleted.

## Background jobs

* One hosted service runs every hour.
* It deletes Guest accounts not seen for 90 days, with their sessions.
* It marks invites, reset tokens and promotions as expired.
* It deletes sessions not used for 90 days.
* It deletes `sync_batch` rows older than 7 days.
* It deletes photos no article references any more, 30 days after their last use.

## Personal data (nFADP / GDPR)

* Stored per user: username, display name, role, last seen, device names, and who made each movement.
* No email, no phone number, no IP address stored in the database.
* Logs never contain passwords, tokens, display names or row content; they contain user IDs only.
* A movement always keeps who made it. There is no retention setting.
* Deleting a user keeps their movements (stock needs them) but drops the link to a name.
* The README explains to self-hosters that they are the data controller for their server.

## Docker

* Multi-stage `Dockerfile`: build with the .NET SDK image, run with the ASP.NET runtime image.
* Images for `linux/amd64` and `linux/arm64`.
* The container runs as a non-root user and listens on port 8080.
* `/data` is a volume for photos and keys.
* `HEALTHCHECK` calls `/health`.
* `docker-compose.yml` starts the server and PostgreSQL with a named volume for each, and a `.env.example` file lists the variables.

## HTTP and HTTPS

* The server itself speaks plain HTTP on port 8080 and expects a reverse proxy for HTTPS.
* The README shows a Caddy example (automatic Let's Encrypt) and explains the self-signed option with `EASYSTOCK_CERT_FINGERPRINT`.
* The server works over plain HTTP too, since the app allows it after a warning.
* When behind a proxy, the server reads `X-Forwarded-For` only from proxies listed in a setting, so rate limits use the real IP.

## Tests

* Integration tests run the real API against PostgreSQL in Testcontainers.
* Auth: setup once only, invite redeem, expired and reused invites, login lockout, refresh rotation, refresh reuse revokes the session (the previous token or an older one), the previous token replayed within the grace window returns the same token, a forged tag does not revoke, revoked session answers `session-revoked`.
* Guest: signup off, signup on, Guest limit, IP limit, the public token cannot create anything above Guest, regenerate makes the old token fail.
* Promotion: short code must match, pending Guest keeps Guest rights, password sets the role, 7-day expiry, cancel.
* Roles: for each of the four roles, every endpoint and every table in a push answers as `Permissions.cs` says.
* Last Admin: cannot be demoted, disabled or deleted.
* Sync: two simulated phones edit offline, push, pull, and end with identical data.
* Sync: a repeated batch ID returns the same answer and applies nothing twice.
* Sync: concurrent pushes never let a pull skip a row.
* Conflicts: every JSON test case gives the expected result.
* Guest filter: a Guest pull never contains an author, a device, a comment or a review; stock computed from it matches a Member's.
* End-to-end: the app's own client and sync code (Room on the host JVM) runs against the Docker image with PostgreSQL. `server/e2e/run.sh` starts a throwaway server, runs the app's E2E tests and stops it; see [05-e2e-tests.md](05-e2e-tests.md). No server option is needed for it.
* Alerts: exactly at the threshold, just under, just above, under and back within one batch, serial articles ignored, the author is not notified.

## CI and releases

* The existing GitHub Actions workflow gets a server job: build, run tests (Testcontainers works on GitHub runners).
* On a release tag `X.Y.Z`, the release workflow also builds the Docker image for amd64 and arm64.
* The image is published to GitHub Container Registry as `ghcr.io/mathorck/easystock-server:X.Y.Z` and `:latest`.
* The server version and the app version use the same tag, and `server-info` reports the minimum app version it accepts.
