# EasyStock Server: implementation plan (.NET 10 / ASP.NET Core minimal API / EF Core + Npgsql / PostgreSQL)

The plan is built from `docs/03-server-spec.md` alone (362 lines). It has five parts: blockers, decisions, phased plan, technical design notes, and risks/effort.

---

## 0. Ground rules for the build

- **Build in the spec's layout from the first commit.** Use `server/src/EasyStock.Server/` and `server/tests/EasyStock.Server.Tests/`, so the folder can later drop into the app repo unchanged. Until then, treat `EasyStockServer/` as the future repo root, with `server/` and `docs/` as siblings.
- **Solution setup:**
  - `server/EasyStock.Server.slnx` (the new solution format)
  - `global.json`, pinned to the .NET 10 SDK
  - `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `InvariantGlobalization=true`, `ServerGarbageCollector=false`, `ConcurrentGarbageCollection=true`, `TieredPGO`
  - `Directory.Packages.props` for central package versions
- **Packages:**
  - Runtime: `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions` (snake_case), `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `QRCoder` (MIT, ASCII QR in the log).
  - Tests: `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, `Respawn`, `Microsoft.Extensions.TimeProvider.Testing`.
- **Time:** inject `TimeProvider` everywhere. Never call `DateTime.UtcNow` directly. Tests use `FakeTimeProvider` for expiries, lockout, retention and jobs.

---

## 1. Assumptions and blockers

### 1.1 Missing artifacts and how to proceed

| Missing | Blocks | How to proceed meanwhile |
|---|---|---|
| `docs/schema.dbml` | Shared-table entities and migrations; DTO field names; max lengths used by the suffix logic; validation | Do every phase that doesn't touch shared tables first. Then write placeholder entities derived from the spec (see 1.3), marked `// PLACEHOLDER-DBML`. Once the real dbml arrives, add a schema drift test: a small dbml parser compares tables, columns, types, nullability and unique indexes against the EF model. |
| `docs/test-cases/merge/*.json` | Conflict-rule verification | Write C# conflict rules from the spec's prose. Run them against stub fixtures in the proposed format (1.4), stored under `server/tests/.../Fixtures/merge-stub/` so they never pass for the real set. Swap in the real directory once it exists. |
| App `BackupMerge` (Kotlin) | Exact suffix formats (SKU, serial), truncation, ATTRIBUTE_MERGED semantics, rule order | Implement rules behind a pure `ConflictRules` class with one method per rule. Assume the `" (n)"` suffix for every renamed value, cutting the base so that base plus suffix fits the column maximum. Re-verify line by line against the Kotlin code. |
| App `Permissions` object | Exact role × operation matrix | Encode the spec's role prose (section 1.3 of the matrix below). **Recommendation:** add a shared fixture `docs/test-cases/permissions.json` (role → list of permission names). Both the Kotlin and the C# tests assert their object equals it, which removes drift for good. |
| App `@SerialName` values and enum string values | JSON names in push and pull | Use camelCase forms of the dbml column names (the spec examples are consistent with this: `articleId`, `occurredAt`). Keep all names in `Sync/Dtos/`, so a rename touches one folder. |
| App version numbers, schema versions | `server-info` and the 426 check | Use constants in `Infrastructure/Compat.cs`: `MinAppVersion = "0.0.0"` placeholder and `SupportedSchemaVersions = [1]`. Bump them in the same PR that changes the protocol. |
| QR content formats (setup, invite, public, reset) | Auth endpoint answers, setup log | Proposed below, to confirm with `02-app-changes.md`. |
| App backup file format | `GET/POST /admin/backup` | Phase 11 is blocked. Design the service boundary now; implement once the format is known. |
| 01/02 docs | Flow details (promotion UX, QR scanning, how photos are fetched) | Ask for them. Several decisions below may already be answered there. |

**Proposed QR content (to confirm with the app side).** Each is one URI. `fp` is present only when `EASYSTOCK_CERT_FINGERPRINT` is set.
- `easystock://setup?server=<urlenc PUBLIC_URL>&token=<b64url>&fp=<hex>`
- `easystock://invite?server=…&token=…&fp=…`
- `easystock://guest?server=…&token=…&fp=…` (the printed public QR)
- `easystock://reset?server=…&token=…&fp=…`

### 1.2 Ambiguities and contradictions, with a recommended decision for each

1. **Paths.** Endpoint lists say `/auth/...`, `/me`, `/sync/...`, but the base path is `/api/v1`.
   - **Decision:** everything goes under `/api/v1` through one `MapGroup("/api/v1")`.
   - Only `/health` and `/openapi/v1.json` sit at the root.
2. **Short code.** The spec says 6 characters, but the example `K7-4QX` has 5 plus a dash.
   - **Decision:** 6 characters from the 32-symbol alphabet `23456789ABCDEFGHJKLMNPQRSTUVWXYZ` (no 0/O/1/I), shown as `XXX-XXX`.
   - Input is normalised: strip dashes and spaces, uppercase.
   - Derivation: first 30 bits of `SHA-256(userId bytes)`, encoded as 6×5 bits.
   - Store it in `users.short_code` with a unique index. The server generates user IDs, so on a collision it regenerates the GUID and retries.
   - Every user has one; the token answer shows it for full users too.
3. **"Catalogue change" push.**
   - **Decision:** after commit, any push batch with at least one accepted or changed row in `category`, `attribute_definition`, `category_attribute`, `location` or `article` sends `sync` to every registered session except the pusher's.
   - This applies whatever the pusher's role. Only Admins can do most of these changes anyway, and Members creating articles benefits too.
   - Debounce per endpoint to 30 s. Flag this as a slight widening of "after an Admin changes the catalogue".
4. **Backup format.** Unknown, see 1.1.
   - **Decision:** merge import equals a synthetic push batch run by the importing Admin through the same `PushProcessor` and conflict rules. That gives one code path.
   - Replace is a separate truncate-and-load path (see item 21).
5. **ATTRIBUTE_MERGED.** An incoming `attribute_definition` has ID B with key K; the server already has ID A with key K.
   - **Decision:** do not insert B. The result is `changed`, conflict `ATTRIBUTE_MERGED`, with `row` set to the server's row (ID A). The app rewrites its local B→A references, as BackupMerge does locally.
   - Server side, rewrite references to B in the rest of the same batch (`category_attribute.attribute_definition_id`, and article attribute values if they are keyed by ID).
   - Persist `attribute_alias(alias_id pk, canonical_id)` so later batches from phones that haven't pulled yet are rewritten too.
   - Blocked on BackupMerge for whether article attribute values are keyed by ID or by key.
6. **Expiries.**
   - Promotion 7 days appears only in Tests: adopt it.
   - Invite expiry is not stated: **default 7 days**, fixed in code (could become a setting later).
   - Reset is 24 h (stated).
   - Setup has no expiry (stated).
7. **Rate limits for unauthenticated calls.** "Per device session" can't apply without a token.
   - **Decision:** the partition key is the `sid` claim when authenticated, otherwise the client IP.
   - `refresh`, `server-info`, `logout` and image GETs without a token fall under 120/min per IP.
8. **Advisory lock scope and pull consistency.** The spec only mentions push.
   - **Decision:** every writer of a `server_seq` column takes the same transaction-scoped advisory lock: push, retention job, backup import, user rename or delete, alert insert.
   - Pull runs in one `REPEATABLE READ` read-only transaction. See 3.1.
9. **`low_stock_alert` per-user delivery tracking.** There is no delivery table.
   - **Decision:** none needed. Alerts carry `server_seq` from the global sequence.
   - Pull includes alerts whose seq falls in the page window, filtered at pull time: the caller is a recipient (role and preference) and `caused_by_user_id != caller`.
   - The phone's cursor already tracks delivery.
10. **Image last-use tracking (30-day cleanup).**
    - **Decision:** add `image.unreferenced_since timestamptz null`.
    - The hourly job sets it to now on images no live article references (including articles that are soft-deleted), clears it on referenced ones, and deletes file and row when it is older than 30 days.
    - Uploaded-but-never-used images are covered the same way.
11. **User display-name propagation.** Pull only sends names for users referenced by the movements it returns, so renames and "Deleted user" never reach phones that already have the name.
    - **Decision:** `users` gets a `server_seq`. A trigger bumps it only when `display_name` or the deleted state changes.
    - Pull returns, in `users`, every user whose seq is in the window plus the authors of returned movements.
12. **Alert preference.** `PATCH /me` changes it, but `users` has no column for it.
    - **Decision:** `users.alerts_enabled bool null`, where null means the role default (admin/member on, viewer off, guest always off).
    - "Alert recipients set by an Admin" in Configuration conflicts with this. **Decision:** no global recipient setting; per-user preference only. Flag for confirmation.
13. **Guest limit** is listed both in `public_invite` and as an Admin setting.
    - **Decision:** it lives only in `public_invite.guest_limit`. `PUT /admin/public-invite` is its only writer.
14. **Trusted proxies.** "A setting" conflicts with "everything else in the DB", and forwarded-header options are needed at startup.
    - **Decision:** new env var `EASYSTOCK_TRUSTED_PROXIES` (comma-separated IPs or CIDRs).
    - The default is empty: only loopback is trusted. That matters for Compose: without the var set, all clients share the proxy's IP bucket. Document it loudly.
15. **Server name** (in `server-info`) has no source.
    - **Decision:** setting `server_name`, default `"EasyStock"`, editable through `PUT /admin/settings`.
16. **`stock_movement.device_id`.**
    - **Decision:** it equals the `device_session.id` from the token's `sid` claim.
    - No FK, so that session deletion and the 90-day cleanup don't touch movements.
17. **Existing movement ID: accepted or MOVEMENT_KEPT?**
    - **Decision:** compare content, ignoring `created_by`, `device_id` and any server-only columns.
    - Identical content means `accepted`, a no-op.
    - Different content means `changed` with `MOVEMENT_KEPT` and the server row in the answer.
18. **Refresh retry race.** A phone that loses the network after the server rotated retries with the old token, and the spec says that revokes the session.
    - **Decision:** follow the spec strictly by default, and put this to the owner as an explicit open question.
    - Optional alternative behind a constant: a 60 s grace in which presenting the previous token re-rotates instead of revoking.
19. **`complete-promotion` authentication.**
    - **Decision:** it requires the Guest's Bearer token (the pending promotion belongs to that user).
    - It returns a full token answer, because the role claim changes. Rotate the refresh token on the same session.
20. **Deleting a user.** A hard delete would break `created_by` and the `users` list.
    - **Decision:** turn the row into a tombstone: `user_name = null`, `password_hash = null`, `display_name = "Deleted user"`, `deleted_at = now`, `short_code` kept (unique).
    - All sessions, push registrations and promotions are deleted, and the `users.server_seq` is bumped.
    - Guests deleted by logout or the 90-day job have no movements, so they are hard-deleted.
21. **Replace-mode backup import vs incremental pull.** Rows that vanish on the server stay on phones forever.
    - **Decision (needs app agreement):** setting `dataset_epoch` (a UUID), returned in `server-info` and in each pull answer, and sent by phones as `X-EasyStock-Dataset-Epoch`.
    - On a mismatch the server answers 409 `dataset-reset`, and the app wipes its server-mode data and pulls from 0.
    - Replace import generates a new epoch.
22. **"Marks invites, reset tokens and promotions as expired"** has no column to mark.
    - **Decision:** expiry is computed (`expires_at < now`).
    - Promotions get `ended_at` plus `end_reason` (`completed|cancelled|expired`). The job sets `expired` on overdue ones and deletes invites and resets that expired more than 30 days ago.
23. **Member rights gaps.**
    - "Tags" is read as NFC tags (the `barcode` column). Members may set or clear tags on articles, items and locations.
    - "Templates" is unclear; it probably means `category_attribute`. Admin only.
    - Can a Member rename a location? **Decision:** yes (edit). Not reparent, not soft-delete.
    - Can a Member create an `attribute_definition`? **Decision:** no, Admin only.
    - Confirm all of this against the Kotlin `Permissions`.
24. **Version-header exemptions:** `/health`, `/openapi/*`, `GET /api/v1/server-info`, and `GET/HEAD /api/v1/images/*` (image loaders often can't add custom headers; confirm).
    - A missing or unparsable header gets 426 `app-too-old` (an app without headers is by definition too old).
25. **Retention clock.** **Decision:** based on the movement's `created_at` (app epoch ms).
26. **Problem `type` format.** **Decision:** the bare slug (`"type": "session-revoked"`). That is a valid relative URI reference per RFC 9457 and matches the spec examples. Never change a slug once shipped.
27. **Guest or Viewer calling `/sync/push`.** **Decision:** 403 `not-allowed` for the whole request, no per-row results.
28. **Lockout and disabled responses.**
    - Locked: 401 `account-locked` with `retryAfter`.
    - Wrong password: 401 `invalid-credentials`.
    - Disabled: 401 `account-disabled`, revealed only after a correct password.
29. **"Whether the server holds data"** (`server-info`) means at least one row in `article`, `category` or `location`.
30. **Imported historical movements.** **Decision:** `created_by` and `device_id` are null (unknown author), not the importing Admin.
31. **SSRF through UnifiedPush endpoints.**
    - **Decision:** require an absolute `http(s)` URL, no redirects, 10 s timeout, ignore the response body, limit URL length to 2048.
    - Private addresses are not blocked, since self-hosted ntfy on a LAN is normal. Document the residual risk.
32. **Compression vs BREACH.** Do not compress `/api/v1/auth/*` responses (they hold tokens). Everything else gzip.
33. **Setup token in the log** is a deliberate exception to "logs never contain tokens". Document it in the README.

### 1.3 Placeholder shared tables (until the dbml arrives)

Every table has `id uuid pk`, `updated_at bigint` (epoch ms), `deleted_at bigint null` (where soft-deletable) and `server_seq bigint not null`.
- `category` (name unique where `deleted_at is null`)
- `attribute_definition` (key unique, immutable; type; options jsonb)
- `category_attribute` (category_id, attribute_definition_id, unique pair)
- `location` (name, parent_id self-FK, barcode unique filtered)
- `article`:
  - name, sku (unique filtered), barcode (unique filtered), category_id
  - tracking (`quantity|serial`), low_stock_threshold bigint null, archived_at null, `image_sha256 char(64) null` (no FK)
  - attributes jsonb
- `item` (article_id, location_id, serial_number unique filtered, barcode?)
- `stock_movement`:
  - article_id, location_id, item_id null, quantity bigint, reason, comment null
  - occurred_at, created_at
  - `created_by uuid null`, `device_id uuid null` (server-set)
- `stock_review` (location_id, status or closed_at; partial unique index on location_id where open)
- `stock_review_line` (review_id, article_id, counted bigint)

### 1.4 Proposed merge test-case format (to confirm with the app side)

```json
{
  "name": "sku-collision-gets-suffix",
  "description": "Incoming article with an SKU already used by another article",
  "actorRole": "admin",
  "existing": { "article": [ { "id": "a1", "sku": "BLT-8" } ] },
  "incoming": { "article": [ { "id": "a2", "sku": "BLT-8" } ] },
  "expected": {
    "results": [ { "table": "article", "id": "a2", "status": "changed", "conflict": "SKU_RENAMED", "row": { "sku": "BLT-8 (2)" } } ],
    "final": { "article": [ { "id": "a1", "sku": "BLT-8" }, { "id": "a2", "sku": "BLT-8 (2)" } ] }
  }
}
```

- `row` and `final` are partial matches: only the listed fields are asserted.
- The C# runner seeds `existing` through EF, runs `incoming` through the real `PushProcessor` over HTTP or in-process, and asserts both `results` and `final`.
- The Kotlin runner ignores `status` and `actorRole`, and asserts `conflict` codes and `final`.

---

## 2. Phases

The build order deviates from the suggested one, on purpose. Everything not blocked on dbml, BackupMerge or the backup format (auth, roles, admin user management, images, infrastructure) comes first. The data model is split into server-only tables (early) and shared tables (once dbml arrives). A dev `docker-compose` for PostgreSQL arrives in Phase 1; the production Docker work is Phase 12.

### Phase 1: Skeleton, configuration, health, cross-cutting middleware (M)

**Goals:** the app boots, reads its env vars, connects to PostgreSQL, serves `/health`, OpenAPI and `server-info`, and returns Problem Details.

**Files**
- `server/EasyStock.Server.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`
- `server/docker-compose.dev.yml` (PostgreSQL only, for local runs)
- `src/EasyStock.Server/Program.cs`. Keep it thin: `builder.AddEasyStock()` / `app.UseEasyStock()` extension methods.
- `src/EasyStock.Server/Infrastructure/`:
  - `ServerOptions.cs`: binds `EASYSTOCK_*` and validates at start (fail fast when `EASYSTOCK_DB` or `EASYSTOCK_PUBLIC_URL` is missing, or `PUBLIC_URL` is not absolute).
  - `ProblemTypes.cs`: the catalogue, see 3.6.
  - `Problems.cs`: helpers returning `TypedResults.Problem` with a stable `type`.
  - `ExceptionMapping.cs`: maps `DbUpdateException`/`PostgresException` to Problem Details without the PostgreSQL message, which contains row content.
  - `VersionHeaderMiddleware.cs`, `Compat.cs` (min app version, supported schema versions, server version from `AssemblyInformationalVersion`).
  - `Json.cs`: `JsonSerializerDefaults.Web`, camelCase properties, `DictionaryKeyPolicy = null` so table-name keys stay snake_case, string enums with an explicit naming policy.
  - `HealthCheck.cs`: `AddHealthChecks().AddDbContextCheck<>()` at `/health`, plus a `--healthcheck` CLI mode for Docker (Phase 12).
  - `Logging.cs`: level from `EASYSTOCK_LOG_LEVEL`; `Microsoft.EntityFrameworkCore.Database.Command` at Warning; `EnableSensitiveDataLogging` never on.
- `src/EasyStock.Server/Endpoints/ServerInfoEndpoints.cs`
- `tests/EasyStock.Server.Tests/Infrastructure/`:
  - `PostgresFixture.cs`: one Testcontainers PostgreSQL 16 per test run, shared as an xUnit assembly fixture.
  - `ApiFactory.cs`: `WebApplicationFactory<Program>` that overrides env vars, replaces `TimeProvider` with `FakeTimeProvider`, and uses a temp `EASYSTOCK_DATA_DIR`.
  - `DbReset.cs`: Respawn between tests, keeping `__EFMigrationsHistory`.
  - `TestClient.cs`: an `HttpClient` that always adds the version headers.

**Middleware order:** ForwardedHeaders → ExceptionHandler/StatusCodePages (ProblemDetails) → RequestDecompression → ResponseCompression (with the auth exclusion) → VersionHeader → Routing → Authentication → Authorization → SessionValidation (inside the JWT event, see 3.4) → RateLimiter → endpoints.

**Tests:** health 200 with the DB up and 503 with it down; `server-info` shape; missing or old version header gives 426 `app-too-old` and exempt paths pass; unknown route gives a Problem Details 404; OpenAPI document served.

**Done when:** `dotnet test` is green against Testcontainers, and `dotnet run` against the dev compose answers `/health` and `/api/v1/server-info`.

### Phase 2: Server-only data model, migrations, sequence infrastructure (M)

**Goals:** `EasyStockDbContext` (snake_case naming) with all server-only tables, the global sequence, the advisory lock helper, and auto-migration at start.

**Files**
- `Data/EasyStockDbContext.cs`: `IdentityUserContext<AppUser, Guid>`. No role tables; the role is a column. Ignore or strip unused Identity columns: email, phone, two-factor, and `AccessFailedCount`/`Lockout*` kept.
- `Data/Entities/`: `AppUser.cs` (users), `DeviceSession.cs`, `Invite.cs`, `PublicInvite.cs`, `Promotion.cs`, `PasswordReset.cs`, `SyncBatch.cs`, `LowStockAlert.cs`, `LowStockState.cs`, `PushRegistration.cs`, `ImageRecord.cs`, `Setting.cs`, `SetupState.cs`, `AttributeAlias.cs`
- `Data/Configurations/*.cs`: one `IEntityTypeConfiguration` per entity; table names exactly as in the spec (`users`, `device_session`, …)
- `Data/ServerSeq.cs`: sequence name, lock key constant, `AcquireSyncLockAsync(tx)`
- `Data/Migrations/` (`Initial`, plus a hand-written SQL migration for the sequence and triggers)
- `Data/MigrationRunner.cs`: `MigrateAsync()` at start (EF 9+ takes its own migration lock)

**Key decisions**
- `users`:
  - Columns: `id uuid`, `user_name`/`normalized_user_name` (nullable, unique where not null), `password_hash` (nullable), `display_name varchar(50)`, `role` (text enum), `kind` (`full|guest`), `short_code char(6)` (unique), `alerts_enabled bool null`, `created_at`, `last_seen_at`, `disabled_at`, `deleted_at`, `server_seq`, plus Identity's `security_stamp`, `concurrency_stamp`, `access_failed_count`, `lockout_end`, `lockout_enabled`.
  - Drop the email, phone and two-factor columns via `Ignore`.
- `device_session`: `refresh_token_hash bytea unique`, `previous_token_hash bytea null`, indexed (unique where not null), `rotated_at`, `last_used_at`, `revoked_at`, `revoke_reason`.
- Token hashes are `bytea` (32-byte SHA-256) everywhere.
- `sync_batch`: `batch_id uuid pk`, `user_id`, `device_session_id`, `received_at`, `response jsonb`.
- `low_stock_alert`: `server_seq` from the global sequence; `caused_by_user_id`; `stock bigint`; `crossed_at bigint` (epoch ms, as in the pull example).
- `setting`: `key text pk`, `value jsonb`.

**Tests:** migrations apply on an empty DB; the sequence exists; a uniqueness test for short code and username (case-insensitive).

**Done when:** a fresh DB migrates at start and the EF model snapshot is committed.

### Phase 3: Core auth: Identity, signing key, tokens, sessions, setup, login, refresh, logout, rate limits, forwarded headers (L)

**Files**
- `Auth/IdentitySetup.cs`:
  - `AddIdentityCore<AppUser>().AddSignInManager().AddEntityFrameworkStores<>()`
  - Password options: length 10, no other rules; plus `MaxLengthPasswordValidator` (≤128)
  - `UsernameValidator`: regex `^[A-Za-z0-9._-]{3,32}$`, unique by normalized name, null allowed only for guests. It replaces the default `UserValidator`, which rejects a null username.
  - Lockout: 5 tries, 15 min, `AllowedForNewUsers=true`
- `Auth/SigningKeyStore.cs`: load or create `${DATA_DIR}/keys/jwt-es256.pem` (see 3.5)
- `Auth/TokenService.cs`: ES256 JWT with `sub`, `role`, `sid`, `iat`, `exp` (15 min); issuer = `PUBLIC_URL`, audience `easystock-app`; `JsonWebTokenHandler`
- `Auth/RefreshTokens.cs`: generate 32 bytes → Base64URL; hash; rotate, with `SELECT … FOR UPDATE` on the session row
- `Auth/SessionService.cs`: create, rotate, revoke, revoke-all-for-user
- `Auth/CurrentUser.cs`: a scoped `ICurrentUser` (user ID, session ID, role and kind from the DB)
- `Auth/SessionValidation.cs`: the `JwtBearerEvents.OnTokenValidated` handler (3.4)
- `Auth/SetupService.cs`: startup hosted step; log the setup token and the ASCII QR (`QRCoder.AsciiQRCode`)
- `Auth/QrContent.cs`: builds the URIs from 1.1
- `Infrastructure/RateLimiting.cs`: policies `auth-ip` (fixed window 10/min per IP) and `default` (120/min per `sid` or IP, applied as the group default); `GuestSignupCounter` (in-memory success counter, Phase 4); 429 Problem `rate-limited` with `Retry-After`
- `Infrastructure/ForwardedHeaders.cs`: `XForwardedFor|XForwardedProto`, `ForwardLimit=1`, clear the defaults and add from `EASYSTOCK_TRUSTED_PROXIES` (.NET 10: `KnownIPNetworks`/`KnownProxies`)
- `Endpoints/AuthEndpoints.cs`: `setup`, `login`, `refresh`, `logout` (the others arrive in Phase 4)

**Key decisions**
- Setup:
  - On start, if `setup_state.done = false`, generate a new token every start, store its hash and print it.
  - `POST /auth/setup` runs in a transaction with `pg_advisory_xact_lock(SETUP_KEY)`, re-checks `done`, creates the Admin plus a session, and sets `done = true`.
  - Afterwards it answers 409 `setup-done` forever.
- Login:
  - `CheckPasswordSignInAsync(user, pw, lockoutOnFailure: true)`.
  - Guests and tombstones have no username, so a lookup fails as `invalid-credentials`.
  - Run a dummy hash on unknown users to keep timing equal.
- Refresh (strict, per spec), three outcomes:
  - Hash matches `refresh_token_hash` on a live session: rotate (previous ← current, current ← new) and update `last_used_at`.
  - Hash matches `previous_token_hash`: revoke with reason `token-reuse`, answer 401 `session-revoked`.
  - Revoked session, or `last_used_at` more than 90 days ago: 401 `session-revoked`.
- Logout: revoke the current session. For a Guest, hard-delete the user (cascade sessions, push registration, promotions).
- `last_seen_at`/`last_used_at` writes are throttled: only written when older than 5 minutes.

**Tests:**
- Setup works once, then 409.
- A restart before setup prints a new token and the old one fails.
- Login lockout after 5 failures, unlocked after 15 min of `FakeTimeProvider`.
- Refresh rotation; reuse revokes; a revoked session gets `session-revoked`; 90-day expiry.
- Access token rejected 15 min later; a disabled user is rejected immediately (Phase 5 hook).
- Rate limiting: the 11th login in a minute gets 429.
- `X-Forwarded-For` honoured only from a trusted proxy.
- Log capture contains no passwords or tokens, except the setup line.

**Done when:** the auth tests from the spec's Tests section (setup, login lockout, refresh rotation and reuse, revoked) are green.

### Phase 4: Invites, Guest signup, public QR, reset, promotion, Me (M)

**Files**
- `Auth/InviteService.cs`, `Auth/PublicInviteService.cs`, `Auth/PasswordResetService.cs`, `Auth/PromotionService.cs`, `Auth/ShortCode.cs`
- `Endpoints/AuthEndpoints.cs`: add `invite/redeem`, `guest-signup`, `reset`, `complete-promotion`
- `Endpoints/MeEndpoints.cs`: `GET/PATCH /me`, `POST /me/password`

**Key decisions**
- Invite: role ∈ {admin, member, viewer}, 7-day expiry, single use. Redeem marks `used_at` in the same transaction, using `UPDATE … WHERE used_at IS NULL` and checking the affected row count.
  - Unknown token: 404 `invite-invalid`. Expired: 410 `invite-expired`. Used: 410 `invite-used`.
- Guest signup checks, in order:
  - Public invite enabled (otherwise 403 `guest-signup-off`).
  - Token hash matches (otherwise 404 `invite-invalid`).
  - The live Guest count is below `guest_limit` (otherwise 409 `guest-limit-reached`). The count runs under the advisory lock `GUEST_SIGNUP_KEY` to avoid a race.
  - The per-IP success counter is below 5 per hour (in-memory, sliding window; no IPs reach the DB). Over the limit gives 429 `rate-limited`.
  - The endpoint also carries the `auth-ip` limiter.
  - The created user is always role guest, kind guest. The public token can never carry a role.
- Regenerate: new token; the old hash is overwritten, so old sheets fail with `invite-invalid`.
- Reset: Admin-created, 24 h, single use. Redeeming it sets a new hash through `UserManager` (`RemovePasswordAsync`/`AddPasswordAsync`), updates the security stamp, clears lockout, revokes all sessions and creates a new one.
- Promotion:
  - `POST /admin/users/{id}/promote` (built here, wired into Admin in Phase 5) requires target kind guest and target role ∈ {member, viewer, admin}.
  - The typed short code must match after normalisation (otherwise 400 `short-code-mismatch`).
  - Only one open promotion per user (a new one replaces it). 7-day expiry. Queues a push to the Guest's sessions.
  - Until completion the user keeps Guest rights.
  - `complete-promotion` requires Bearer, the caller must be that Guest, and the promotion must be open and not expired (otherwise 410 `promotion-expired` or 404 `no-promotion`).
  - It validates username and password, then sets username, hash, `kind=full` and role, ends the promotion as `completed`, rotates the refresh token on the same session, and returns a token answer.
- `PATCH /me`: `displayName` (1–50, trimmed) and `alertsEnabled` (bool or null). It bumps `users.server_seq` through the trigger. Guests may change their display name only.
- `POST /me/password`: full accounts only. Wrong current password gives 400 `invalid-credentials`. Other sessions are revoked, the current one kept.

**Tests:** the spec's Guest and Promotion test lists, plus expired and reused invites, reset revoking old sessions, and Me changes.

**Done when:** every listed Auth, Guest and Promotion test is green.

### Phase 5: Roles, `Permissions.cs`, Admin user, device, invite, public-invite and settings endpoints (M)

**Files**
- `src/EasyStock.Server/Permissions.cs`:

  ```csharp
  public enum Role { Admin, Member, Viewer, Guest }
  public enum Permission { /* read */ ReadStock, ReadMovementsFull, ReadReviews, ReceiveAlerts,
    /* sync writes */ CreateMovement, CreateArticle, EditArticle, ArchiveArticle, SoftDeleteArticle, RestoreArticle,
    CreateItem, EditItem, SoftDeleteItem, SetTag, CreateCategory, EditCategory, DeleteCategory,
    CreateAttribute, EditAttribute, DeleteAttribute, EditCategoryAttribute,
    CreateLocation, EditLocation, ReparentLocation, SoftDeleteLocation,
    CreateReview, EditReview,
    /* other */ UploadImage, ManageUsers, ManageInvites, ManagePublicInvite, ManageDevices, ManageBackups, ManageSettings }
  public static class Permissions { public static bool Has(Role r, Permission p); public static IReadOnlySet<Permission> For(Role r); }
  ```

  Names mirror the Kotlin object once it is known. Assert against `docs/test-cases/permissions.json`.
- `Auth/Policies.cs`: endpoint filter `.RequirePermission(Permission.X)`, which reads the role from `ICurrentUser`, the DB-backed role (not the claim).
- `Endpoints/AdminEndpoints.cs` (partial-class files `AdminEndpoints.Users.cs`, `.Devices.cs`, `.Invites.cs`, `.PublicInvite.cs`, `.Settings.cs` are fine if "one file" gets too long):
  - users list, patch, delete, reset, promote, unpromote
  - devices list and revoke
  - invites create, list, delete
  - public-invite get, put, regenerate
  - settings get and put

**Key decisions**
- Last-admin guard: in a transaction with `pg_advisory_xact_lock(ADMIN_KEY)`, count active admins (role admin, not disabled, not deleted) excluding the target. Zero gives 409 `last-admin`. It applies to demote, disable and delete, including self-actions.
- A full account cannot become a Guest: 409 `role-not-allowed`. Setting a role on a Guest through `PATCH` also gives 409 `use-promotion`.
- Disabling revokes all sessions. Role changes take effect at once on the server and queue a `sync` push so the app refreshes.
- Settings schema: `server_name` (string ≤ 50) and `movement_author_retention_days` (int ≥ 1, or null = keep). Unknown keys give 400 `validation-failed`.

**Tests:**
- The role matrix: an xUnit `[Theory]` over (role × endpoint) asserts 2xx or 403 `not-allowed` per `Permissions`. The push part of the matrix is added in Phase 8.
- Last admin cannot be demoted, disabled or deleted, including two concurrent demotions.
- Delete creates a tombstone and the name becomes "Deleted user".
- Device revoke leads to `session-revoked` on refresh.

**Done when:** the roles matrix (endpoint half) and the last-admin tests are green.

### Phase 6: Images (S)

**Files:** `Images/ImageStore.cs`, a small helper folder (not in the spec layout, acceptable), and `Endpoints/ImagesEndpoints.cs`.

**Decisions**
- Path: `${DATA_DIR}/images/ab/cd/<sha256>.jpg` (lowercase hex). The route constraint checks 64 hex characters.
- `PUT`:
  - Requires `UploadImage` (Member and Admin). The body limit is 5 MB through `IHttpMaxRequestBodySizeFeature`, with 413 `image-too-large` above it.
  - `Content-Type: image/jpeg`, otherwise 415 `unsupported-media-type`.
  - Stream to a temp file in the same folder while hashing with `IncrementalHash`. Check the `FF D8 FF` magic bytes. A hash different from the URL gives 400 `hash-mismatch`.
  - `File.Move` atomically, then insert the row `ON CONFLICT DO NOTHING`. A second identical upload answers 200 (idempotent); a new one answers 201.
- `GET`: any authenticated role. `Results.File` with `Cache-Control: private, max-age=31536000, immutable` and `ETag: "<sha>"`; honour `If-None-Match`.
- `HEAD`: any role, 200 or 404.
- Version-header exempt (1.2 #24). `image_sha256` on articles has no FK, since photos may arrive after the row.

**Tests:** upload, head, get round trip; hash mismatch; oversize; non-JPEG; Viewer upload gets 403; Guest download gets 200; ETag gives 304.

**Done when:** these tests are green.

### Phase 7: Shared tables data model (M, BLOCKED on `schema.dbml`; start with the placeholders from 1.3)

**Files:** `Data/Entities/Shared/*.cs` (nine entities), `Data/Configurations/Shared/*.cs`, a new migration, and `Sync/TableRegistry.cs` (table name → entity type, DTO type, dependency order, soft-delete column, unique columns, max lengths).

**Decisions**
- Column names follow the dbml through snake_case naming. `article.image_path` becomes `image_sha256`.
- `stock_movement` gets `created_by`, `device_id`.
- Unique indexes are filtered with `HasFilter("deleted_at IS NULL")` where the app clears the value on soft delete.
- Partial unique index for the open `stock_review` per location.
- Indexes on `server_seq` for every table; `stock_movement(article_id)` for `SUM`.
- FKs between shared tables are real but not cascading. The cross-table NFC-tag uniqueness (article vs location) is enforced in code under the sync lock.
- Triggers (hand-written SQL migration) on every shared table, plus `low_stock_alert`, plus `users` (with a column `WHEN` condition): `BEFORE INSERT OR UPDATE … NEW.server_seq := nextval('server_seq'); PERFORM pg_advisory_xact_lock(<SYNC_KEY>);`. See 3.1. EF maps `server_seq` as `ValueGeneratedOnAddOrUpdate`.
- `Sync/Dtos/*.cs`: one record per table with app field names. Mappers stay explicit, no AutoMapper.

**Tests:**
- Migration applies.
- Trigger bumps seq on update.
- Drift test against `docs/schema.dbml` (skipped with a clear message while the file is missing; fails once it exists and differs).

**Done when:** the tables exist; done against the placeholders, re-verified when the real dbml lands, and the drift test is green.

### Phase 8: Sync push and pull, conflict rules, Guest filter (L, the biggest phase; partly blocked on BackupMerge and the test cases)

**Files**
- `Sync/PushProcessor.cs`: orchestration (3.1, 3.2)
- `Sync/RowOperationClassifier.cs`: compares the incoming DTO with the stored entity and returns the set of operations {Create, Edit, Archive, Unarchive, SoftDelete, Restore, Reparent, SetTag}
- `Sync/PushPermissions.cs`: (table, operation) → `Permission`. A row needs every permission for its operations.
- `Sync/RowValidator.cs`: enums, lengths, quantity rules, required references
- `Sync/ConflictRules.cs`: a port of BackupMerge. Pure functions over an `IConflictLookup` (DB-backed in production, in-memory in unit tests). One method per code: `CATEGORY_RENAMED`, `SKU_RENAMED`, `BARCODE_CLEARED`, `SERIAL_RENAMED`, `ATTRIBUTE_MERGED`, `CATEGORY_ATTRIBUTE_SKIPPED`, `LOCATION_PARENT_KEPT`, `MOVEMENT_KEPT`, `ATTRIBUTE_KEY_KEPT`. Plus `Suffix.Apply(base, n, maxLen)`.
- `Sync/PullBuilder.cs`, `Sync/GuestFilter.cs`
- `Sync/Dtos/PushRequest.cs`, `PushResult.cs`, `PullResponse.cs`
- `Endpoints/SyncEndpoints.cs`

**Push algorithm (per request)**
1. Endpoint filter: a role without any sync write permission (Guest, Viewer) gets 403 `not-allowed`. Body limit 10 MB, at most 5000 rows, otherwise 413 `batch-too-large`.
2. Begin a READ COMMITTED transaction, then `pg_advisory_xact_lock(SYNC_KEY)`.
3. Look up `sync_batch` by `batch_id`:
   - Found with the same user: return the saved `response`.
   - Found with a different user: 409 `batch-id-reused`.
4. Load the caller's role from the DB (already done by `ICurrentUser`).
5. For each table in dependency order (`category`, `attribute_definition`, `category_attribute`, `location`, `article`, `item`, `stock_movement`, `stock_review`, `stock_review_line`):
   - `location` rows are first topologically sorted by `parent_id` within the batch.
   - For each row, rewrite IDs via `attribute_alias` and in-batch merges, then:
     1. Load the existing row (including soft-deleted ones).
     2. Classify the operations and check permissions. On failure: rejected `not-allowed` plus the server row.
     3. Validate. On failure: rejected `invalid` plus the server row. References must exist, deleted or not: a movement for a soft-deleted article is accepted, because stock truth wins.
     4. Apply the conflict rules. They mutate the row and add conflict codes; the status becomes `changed` with the changed fields in `row`.
     5. Movements: if the ID exists, compare content and return accepted, or `MOVEMENT_KEPT`. Otherwise set `created_by` and `device_id` from the token.
     6. `attribute_definition.key` is immutable: keep the server key (`ATTRIBUTE_KEY_KEPT`).
     7. `stock_review`: a second open review for the location gets rejected `review-open`.
     8. An identical no-op update is accepted without a write and without a seq bump.
     9. `CreateSavepoint`, `SaveChanges`, `ChangeTracker.Clear()`. On a `DbUpdateException`, `RollbackToSavepoint` and reject `conflict` with the server row. This is a backstop; the pre-checks should catch everything.
6. Collect touched articles (movements, threshold, tracking or archive changes). Run `LowStockDetector` (Phase 9) in the same transaction.
7. Insert `sync_batch` with the response, then commit.
8. After commit: enqueue alert pushes and catalogue pushes (Phase 9).

**Pull algorithm:** see 3.1.

**Guest filter:** `GuestFilter.Apply(PullResponse)` is the only place.
- Movements lose `createdBy`, `deviceId` and `comment` (set to null, and omitted with `JsonIgnoreCondition.WhenWritingNull` on a Guest-specific DTO projection).
- `stock_review` and `stock_review_line` keys are removed.
- `users` and `alerts` are empty.
- The cursor still advances past filtered rows.
- Guests cannot push, so no push answer ever carries data to a Guest. The admin and backup endpoints are permission-gated.

**Tests**
- The push half of the roles matrix: role × table × operation, with an exact status per `Permissions`.
- Two simulated phones (a `TestPhone` helper with a local in-memory store, offline edit queue, `push()`/`pull()`) edit offline with overlapping SKUs, categories and tags, push and pull in both orders, and end with identical stores.
- A repeated batch ID returns a byte-identical answer, and the movement count is unchanged.
- Concurrency: 8 parallel pushers × 50 batches while 2 pullers loop. The union of pulled IDs equals all accepted IDs, and the cursor never skips.
- Every merge JSON case (stub set now, real set later).
- Guest filter: random dataset; a Guest pull has no author, device, comment or review, and `SUM(quantity)` per article equals a Member's.
- Movement immutability: an update with the same ID and different content gives `MOVEMENT_KEPT`, the row is unchanged, and no role can update or delete one.

**Done when:** all tests are green. The conflict rules are done against the stub cases and placeholder schema; re-verify when the real dbml, test cases and BackupMerge arrive.

### Phase 9: Low-stock alerts and push sending (M)

**Files:** `Alerts/LowStockDetector.cs`, `Alerts/AlertRecipients.cs`, `Alerts/PushQueue.cs` (bounded `Channel<PushJob>`), `Alerts/PushSender.cs` (`BackgroundService` plus typed `HttpClient`), `Endpoints/PushEndpoints.cs`.

**Detector (inside the push transaction, after the rows are applied)**
- For touched article IDs, `SELECT a.id, a.low_stock_threshold, COALESCE(SUM(m.quantity),0) … GROUP BY a.id`.
- Eligible articles: tracking = quantity, not archived, not deleted, threshold not null.
- Ineligible articles: delete their `low_stock_state` row.
- Eligible articles:
  - Stock ≤ threshold and no state row (missing means "not low"): insert `low_stock_alert` (`crossed_at = now`, `stock`, `caused_by_user_id = caller`) and the state row.
  - Stock > threshold: delete the state row.
- Evaluated once per batch, so "under and back within one batch" alerts nothing.
- Note: setting a threshold on an article that is already low alerts once (defined behaviour).

**Recipients** (`AlertRecipients.IsRecipient(user)`, reused by pull and push):
- Role admin or member: `alerts_enabled ?? true`.
- Viewer: `alerts_enabled ?? false`.
- Guest: never.
- The cause is excluded.

**Push sending**
- The endpoint allowlist is described in 1.2 #31.
- `POST` with body `sync`, `Content-Type: text/plain`, `TTL` header 86400 (UnifiedPush/WebPush convention), and `Urgency: normal`.
- 404 or 410 deletes the registration. Other failures are logged at Debug with the session ID only, then ignored.
- Jobs are enqueued only after commit, so a rolled-back batch never pings. Per-endpoint debounce is 30 s.
- Triggers: alerts (recipients' sessions), catalogue change (all other sessions), promotion created (Guest's sessions), role or disable change (target's sessions).
- `POST /push/register` upserts by `device_session_id`; `DELETE` removes it. Any authenticated role may register.

**Tests:**
- Threshold cases: exactly at, just under, just above, under and back within one batch.
- Serial articles ignored; archived ignored.
- The author gets no alert in pull; another Member does; a Viewer only after opting in; a Guest never.
- Push sender (fake `HttpMessageHandler`): sent after commit only; 410 deletes the registration; a failure is swallowed.

**Done when:** the spec's alert tests are green.

### Phase 10: Background jobs (S–M)

**File:** `Jobs/CleanupService.cs`, a `BackgroundService` using `PeriodicTimer(1h)` driven by `TimeProvider`. The first run is 1 minute after start. Each task runs in its own DI scope and try/catch.

**Tasks**
1. Delete Guests with `last_seen_at` more than 90 days ago (hard delete, with cascades).
2. Expire promotions (`ended_at`, `end_reason='expired'`). Purge invites and resets that expired more than 30 days ago.
3. Delete sessions with `last_used_at` more than 90 days ago (and their push registrations).
4. Delete `sync_batch` rows older than 7 days.
5. Retention:
   - If `movement_author_retention_days` is set, `UPDATE stock_movement SET created_by=NULL, device_id=NULL WHERE created_at < @cutoff AND (created_by IS NOT NULL OR device_id IS NOT NULL)` in chunks of 1000 rows.
   - Each chunk is its own transaction holding the sync lock, so pushes are never blocked long.
   - The trigger bumps `server_seq`.
6. Images: mark and clear `unreferenced_since`, then delete files and rows past 30 days (file first, then row; a missing file is ignored).

Expose `CleanupService.RunOnceAsync()` for tests.

**Tests:** each task with `FakeTimeProvider`; retention removes authors after the set time and bumps seq, and a Member pull then receives the anonymised rows.

**Done when:** the retention test from the spec and the per-task tests are green.

### Phase 11: Backup export and import (M–L, BLOCKED on the app backup format)

**Files:** `Backup/BackupExporter.cs`, `Backup/BackupImporter.cs` (new folder beyond the spec layout), and `AdminEndpoints.Backup.cs`.

**Decisions**
- Export:
  - Read-only REPEATABLE READ snapshot. Stream the zip (or whatever the app format is) with `ZipArchive` over the response body, with no full in-memory buffer.
  - Map `image_sha256` back to the app's `image_path` convention.
  - Omit server-only columns (`created_by`, `device_id`) unless the app format has a place for them.
- Merge import: parse into the same DTOs, then call `PushProcessor.ApplyAsync(rows, actor: importing admin, source: Import)`. The import may exceed the 5000-row limit, so it goes through an internal entry point without that limit.
  - Movements are imported with null `created_by` and `device_id`.
  - Photos are written through `ImageStore`.
  - Returns a summary (counts per status and conflict code).
- Replace import, in one transaction holding the sync lock:
  - Truncate the shared tables, `low_stock_state` and `low_stock_alert`.
  - Bulk insert (the trigger assigns seq) and recompute `low_stock_state` without creating alerts.
  - Set a new `dataset_epoch` and push `sync` to all sessions.
- Request size: 500 MB limit on this endpoint only. `RequestDecompression` handles gzip.

**Tests:**
- Export then replace-import into an empty server gives identical rows.
- Merge import of the stub with conflicts gives the expected codes.
- A phone with an old epoch gets 409 `dataset-reset`.
- A non-Admin gets 403.

**Done when:** round-trip tests are green against the real format.

### Phase 12: Docker and Compose (S)

**Files:** `server/Dockerfile`, `server/.dockerignore`, `server/docker-compose.yml`, `server/.env.example`, optional `server/Caddyfile.example`.

**Dockerfile**
- Build stage: `FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build`, then restore, then `dotnet publish -c Release -a $TARGETARCH -p:Version=$VERSION -p:PublishReadyToRun=true -o /out`. This cross-compiles without QEMU.
- Runtime stage: `FROM mcr.microsoft.com/dotnet/aspnet:10.0`.
  - `ENV ASPNETCORE_HTTP_PORTS=8080 EASYSTOCK_DATA_DIR=/data DOTNET_gcServer=0`
  - `RUN mkdir /data && chown $APP_UID /data` (the only `RUN` in the final stage; the release build needs QEMU for it)
  - `USER $APP_UID`, `VOLUME /data`, `EXPOSE 8080`
- `HEALTHCHECK CMD ["dotnet","EasyStock.Server.dll","--healthcheck"]`. The aspnet image ships neither curl nor wget. The `--healthcheck` branch in `Program.cs` runs before the host is built and does one `HttpClient` GET to `http://127.0.0.1:8080/health`, exiting 0 or 1. Interval 30 s, start period 30 s.

**Compose**
- `db`: `postgres:17-alpine` with a named volume `pgdata` and a `pg_isready` healthcheck.
- `server`: image `ghcr.io/mathorck/easystock-server:latest` (with a `build: .` alternative), `depends_on: db: condition: service_healthy`, named volume `easystock-data:/data`, `ports: "8080:8080"`, `env_file: .env`, `restart: unless-stopped`.
- `.env.example`: `EASYSTOCK_DB`, `EASYSTOCK_PUBLIC_URL`, `EASYSTOCK_CERT_FINGERPRINT`, `EASYSTOCK_LOG_LEVEL`, `EASYSTOCK_TRUSTED_PROXIES`, `POSTGRES_PASSWORD`.

**Done when:**
- `docker compose up` on a clean machine reaches healthy and prints the setup QR.
- An arm64 image builds with `docker buildx build --platform linux/amd64,linux/arm64`.
- Idle RSS is checked on arm64 (target under 150 MB).

### Phase 13: CI and releases (S)

- In the app repo's existing workflow, add a `server` job: `actions/setup-dotnet` 10.x, `dotnet restore`/`build`/`test` in `server/`, with a path filter on `server/**` and `docs/test-cases/**`. Testcontainers works with Docker on `ubuntu-latest`.
- Also run the Kotlin merge tests whenever `docs/test-cases/**` changes, so both sides gate shared fixtures.
- Release workflow on tag `X.Y.Z` (no `v` prefix):
  - `docker/setup-qemu-action`, `docker/setup-buildx-action`, login to GHCR with `GITHUB_TOKEN` (`permissions: packages: write`).
  - `docker/build-push-action` with `platforms: linux/amd64,linux/arm64`, `build-args: VERSION=${{ github.ref_name }}`, tags `ghcr.io/mathorck/easystock-server:${tag}` and `:latest`, plus OCI labels.

**Done when:** a test tag publishes both architectures and `server-info` reports the tag as its server version.

### Phase 14: README and docs (S)

`server/README.md` covers:
- Quick start with Compose; the first-start setup QR.
- A Caddy reverse-proxy example and the self-signed certificate option with fingerprint computation (`openssl x509 -noout -fingerprint -sha256`).
- `EASYSTOCK_TRUSTED_PROXIES` with a Compose example.
- Backups of `pgdata` and `/data` (signing key, photos).
- Upgrading.
- nFADP/GDPR: the self-hoster is the data controller; what is stored; the retention setting; no IPs in the DB; the log policy and its setup-token exception.
- Hardware notes for the Pi 4.

Also update `docs/03-server-spec.md` with the decisions from 1.2 so the spec stays true.

---

## 3. Key technical design notes

### 3.1 `server_seq`: one sequence plus one advisory lock

- **The sequence and the trigger.** One `CREATE SEQUENCE server_seq` serves all shared tables plus `low_stock_alert` plus `users`. A `BEFORE INSERT OR UPDATE` trigger sets `NEW.server_seq := nextval('server_seq')` and calls `pg_advisory_xact_lock(SYNC_KEY)`. With the trigger, no writer can forget to bump the seq or to take the lock. Re-acquiring the lock in the same transaction is free.
  - The `users` trigger fires only `WHEN (OLD.display_name IS DISTINCT FROM NEW.display_name OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at)`, so `last_seen` writes don't churn pulls.
- **The explicit lock.** The push processor (and import and retention) still take the lock explicitly at transaction start, because conflict pre-checks must read under the lock, not only the writes.
- **Why this is enough.**
  - Writers are serialised, so every seq handed out by transaction T1 is lower than T2's, and T1 commits before T2 starts allocating.
  - A pull snapshot therefore sees a prefix of the seq space, plus nothing above it.
  - `nextval` gaps from rollbacks are harmless.
- **Pull**, run in one read-only REPEATABLE READ transaction:
  1. For each pullable table, select `WHERE server_seq > @since ORDER BY server_seq LIMIT @limit + 1`. Pullable tables are the 9 shared tables, `low_stock_alert` and `users`, minus the review tables for Guests.
  2. Merge-sort the results in memory by seq and keep the first `limit`.
  3. `next` is the seq of the last kept row, or `since` if none; `hasMore` is whether more remained.
  4. Filter alerts per user and apply the Guest filter after the cut, so the cursor still advances.
  5. `users` is the users in the window plus the authors of returned movements (a second query within the same snapshot).
  6. `limit` defaults to 500, maximum 2000 (400 `validation-failed` above that).
  7. Include `datasetEpoch`.
- **The SQL-only alternative**, a single `UNION ALL` of `(table, id, seq)` ordered and limited, is cleaner, but the per-table approach needs no raw SQL and 11×2001 rows is trivial.

### 3.2 Idempotent batches

- `sync_batch.batch_id` is the PK. It is checked after taking the lock, so two simultaneous identical batches serialise and the second gets the saved answer.
- The response is stored as `jsonb`, with the exact serialised bytes, so replays are byte-identical.
- Scope: same user, else 409 `batch-id-reused`. This allows a phone that re-logged in to replay safely.
- Retention is 7 days (job).

### 3.3 Refresh-token rotation with reuse detection

- Storage: SHA-256 of the 32 raw bytes, never the token. Lookup is one indexed query on `refresh_token_hash = @h OR previous_token_hash = @h` (two unique partial indexes), then `SELECT … FOR UPDATE` on the found row.
- The three outcomes are listed in Phase 3. Reuse sets `revoked_at` and `revoke_reason='token-reuse'`.
- Logging only covers the session ID and the reason.
- Concurrent refreshes with the same current token: the row lock makes the second see the token as "previous", which revokes the session. This is strict but correct per spec. Mention the grace-window option (1.2 #18) to the owner.
- Refresh tokens don't depend on the JWT key, so losing the key only forces a refresh, not a re-login.

### 3.4 Per-request session validation

- In `JwtBearerEvents.OnTokenValidated`, load `(session, user)` by `sid` and `sub` with one join query and no cache. That costs about 1 ms on a Pi, and it means revocation and disabling take effect immediately.
- Fail with 401 `session-revoked` if the session is revoked or missing, and 401 `account-disabled` if the user is disabled or deleted.
- Populate the scoped `ICurrentUser` with the DB role and kind. Permission checks never trust the `role` claim.
- Throttled `last_used_at`/`last_seen_at` writes use `ExecuteUpdateAsync`.
- `MapInboundClaims = false`; `ValidAlgorithms = [ES256]`; `ClockSkew = 30s`.
- `OnChallenge` and `OnForbidden` write Problem Details: `token-expired`, `unauthorized`, `not-allowed`.

### 3.5 JWT ECDSA key storage

- `${DATA_DIR}/keys/jwt-es256.pem`, PKCS#8 PEM from `ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()`.
- Created with `FileStreamOptions { UnixCreateMode = UserRead|UserWrite }` (0600): written to a temp file, then renamed.
- Load once at start. `kid` is the Base64URL SHA-256 of the SubjectPublicKeyInfo.
- No rotation in v1. Deleting the file rotates the key; the effect is one refresh per phone.
- Data-protection keys go to `${DATA_DIR}/dp-keys` (`PersistKeysToFileSystem`, `SetApplicationName("EasyStock.Server")`). They are barely used, but this keeps Identity's token providers stable.

### 3.6 Problem Details type catalogue (`Infrastructure/ProblemTypes.cs`)

| type | status | when |
|---|---|---|
| `validation-failed` | 400 | bad body or query; `errors` map |
| `invalid-credentials` | 401 | wrong username or password, wrong current password |
| `account-locked` | 401 | lockout; `retryAfter` |
| `account-disabled` | 401 | disabled or deleted |
| `unauthorized` | 401 | missing or invalid token |
| `token-expired` | 401 | expired access token |
| `session-revoked` | 401 | revoked, expired or reused refresh token; revoked session |
| `not-allowed` | 403 | permission denied (endpoint or whole push) |
| `guest-signup-off` | 403 | public QR disabled |
| `not-found` | 404 | resource missing |
| `invite-invalid` | 404 | unknown invite, public or reset token |
| `no-promotion` | 404 | complete-promotion without a pending one |
| `setup-done` | 409 | setup after done |
| `username-taken` | 409 | |
| `last-admin` | 409 | |
| `role-not-allowed` | 409 | full to Guest, invalid target role |
| `use-promotion` | 409 | role change on a Guest via PATCH |
| `guest-limit-reached` | 409 | |
| `batch-id-reused` | 409 | |
| `dataset-reset` | 409 | epoch mismatch |
| `invite-expired`, `invite-used`, `reset-expired`, `reset-used`, `promotion-expired` | 410 | |
| `short-code-mismatch` | 400 | |
| `hash-mismatch` | 400 | image |
| `image-too-large`, `batch-too-large`, `backup-too-large` | 413 | |
| `unsupported-media-type` | 415 | |
| `app-too-old` | 426 | version or schema header |
| `rate-limited` | 429 | with `Retry-After` |
| `internal-error` | 500 | `traceId` only |

Per-row push reasons are a separate, smaller set inside `results[].reason`: `not-allowed`, `invalid`, `missing-reference`, `review-open`, `conflict`.

### 3.7 Version-header middleware

- `VersionHeaderMiddleware` runs before routing and skips the exempt prefixes (1.2 #24).
- It parses `X-EasyStock-App-Version` as `System.Version` (major.minor.patch) and `X-EasyStock-Schema-Version` as an int.
- If the app version is below `Compat.MinAppVersion`, the schema version is outside `Compat.SupportedSchemaVersions`, or either header is missing or unparsable, it answers 426 `app-too-old`. The extensions carry `minAppVersion` and `supportedSchemaVersions` so the app can show a precise message.
- An OpenAPI operation transformer documents both headers on every non-exempt operation.

### 3.8 Identity customisation

- `IdentityUserContext<AppUser, Guid>` means no roles, claims or logins tables are used. The role is the column `users.role`, so drop Identity's role tables by not using `IdentityDbContext`.
- `AppUser : IdentityUser<Guid>`. Ignore `Email`, `NormalizedEmail`, `EmailConfirmed`, `PhoneNumber`, `PhoneNumberConfirmed` and `TwoFactorEnabled` through `builder.Entity<AppUser>().Ignore(…)`, which works because `UserStore` only touches them through feature-specific interfaces you don't call. Verify against the EF model snapshot.
- Guests: `UserName = null`, `PasswordHash = null`, `LockoutEnabled = false`. Create them with `UserManager.CreateAsync(user)` (no password) after replacing `IUserValidator<AppUser>` with the custom validator that permits a null username when `Kind == Guest`.
- Normalisation is the default `UpperInvariantLookupNormalizer`. It is case-insensitive and works under `InvariantGlobalization`.

### 3.9 Forwarded headers and trusted proxies

- `ForwardedHeadersOptions { ForwardedHeaders = XForwardedFor | XForwardedProto, ForwardLimit = 1 }`.
- Clear `KnownIPNetworks`/`KnownProxies`, then add loopback plus `EASYSTOCK_TRUSTED_PROXIES` entries (parsing `IPNetwork.Parse` for CIDRs).
- The rate-limiter IP partition reads `HttpContext.Connection.RemoteIpAddress` after the middleware. Normalise IPv4-mapped IPv6 addresses, and bucket IPv6 by /64.

### 3.10 Guest filter location

- It lives in `Sync/GuestFilter.cs` and is called only from `PullBuilder`, the only code path that returns shared rows to a Guest.
- An architecture test asserts that no endpoint other than pull returns shared DTOs to a role without `ReadMovementsFull`:
  - Admin, backup and push endpoints require permissions Guests lack.
  - Images carry no personal data.
- The property test in Phase 8 is the safety net.

---

## 4. Risks and effort

### Effort per phase

| # | Phase | Effort | Blocked? |
|---|---|---|---|
| 1 | Skeleton, config, health, middleware | M | no |
| 2 | Server-only data model, migrations | M | no |
| 3 | Core auth, tokens, sessions, rate limits | L | QR format (soft) |
| 4 | Invites, Guest, reset, promotion, Me | M | QR format (soft) |
| 5 | Permissions, Admin users, devices, invites, settings | M | Kotlin `Permissions` (soft) |
| 6 | Images | S | no |
| 7 | Shared tables | M | **schema.dbml** |
| 8 | Sync push, pull, conflicts, Guest filter | **XL** (largest; plan 30–40% of total effort) | **dbml, BackupMerge, test cases, SerialNames** |
| 9 | Alerts and push sending | M | dbml (article columns) |
| 10 | Background jobs | S–M | no |
| 11 | Backup import and export | M–L | **app backup format** |
| 12 | Docker and Compose | S | no |
| 13 | CI and release | S | app repo workflow |
| 14 | README and docs | S | no |

### Risks, with mitigations

1. **Schema and serialization drift from the app.** This is the highest risk. Mitigations: the dbml drift test, all DTOs in one folder, shared JSON fixtures for merges and permissions, and contract tests that use the app's real serialized JSON samples once available.
2. **Conflict-rule divergence from BackupMerge** (suffix format, truncation, rule order, ATTRIBUTE_MERGED ID handling). Mitigation: the shared test cases are the contract. Don't ship sync until the real cases pass on both sides.
3. **Permission semantics are per operation, not per table.** Bugs here mean either a privilege escalation (Member reparents or soft-deletes) or blocked work. Mitigation: the exhaustive role × table × operation theory test, and the classifier unit-tested on its own.
4. **Pull skipping rows** if any writer bypasses the lock or pull isn't snapshotted. Mitigation: the trigger takes the lock, pull uses REPEATABLE READ, and the concurrency stress test runs in CI.
5. **One unique violation aborting the push transaction.** Mitigation: code pre-checks under the lock, plus a savepoint per row, plus `ChangeTracker.Clear()`.
6. **Refresh-token strictness causing spurious logouts** on flaky mobile networks. Mitigation: raise the grace-window decision now; it is cheap to add.
7. **Replace import leaving stale data on phones.** Mitigation: the dataset epoch, which needs app work. Agree it before Phase 11.
8. **Rate-limit misconfiguration behind Docker or a proxy** (everyone shares one IP bucket, so 10 logins per minute server-wide). Mitigation: `EASYSTOCK_TRUSTED_PROXIES`, README examples, and a startup warning when requests arrive with `X-Forwarded-For` from an untrusted private address.
9. **Personal data in logs** through EF or Npgsql exception messages. Mitigation: exception mapping, sensitive-data logging off, and a log-capture test asserting that no display name or token appears.
10. **Pi resource limits** (startup time, memory). Mitigation: workstation GC, ReadyToRun, no in-memory caches of row data, chunked retention, streamed export and import.
11. **SSRF through push endpoints.** Mitigation: the restrictions in 1.2 #31, documented.
12. **Last-admin and Guest-limit races.** Mitigation: dedicated advisory locks, covered by concurrent tests.

### Open questions to send to the app side, in priority order

1. Can `schema.dbml`, the Kotlin `Permissions`, `BackupMerge`, the backup format and the `@SerialName` list be shared or copied?
2. QR URI formats.
3. The dataset-epoch mechanism.
4. Adopting the canonical ID after ATTRIBUTE_MERGED.
5. Whether the image loader can send the version and Authorization headers.
6. Refresh grace window.
7. Short-code display format.
8. The current minimum app version and schema version numbers.

---

### Critical files for implementation
- /home/mathorck/Documents/dev/EasyStockServer/docs/03-server-spec.md (source spec; update with the decisions from 1.2)
- /home/mathorck/Documents/dev/EasyStockServer/server/src/EasyStock.Server/Sync/PushProcessor.cs (planned; lock, idempotency, per-row pipeline)
- /home/mathorck/Documents/dev/EasyStockServer/server/src/EasyStock.Server/Sync/ConflictRules.cs (planned; BackupMerge port)
- /home/mathorck/Documents/dev/EasyStockServer/server/src/EasyStock.Server/Permissions.cs (planned; role × operation matrix)
- /home/mathorck/Documents/dev/EasyStockServer/server/src/EasyStock.Server/Data/EasyStockDbContext.cs (planned; Identity customisation, snake_case, sequence and triggers migration)
- /home/mathorck/Documents/dev/EasyStockServer/server/src/EasyStock.Server/Sync/PullBuilder.cs (planned; snapshot pull, cursor, Guest filter call site)