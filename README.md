<p align="center">
  <img src="https://raw.githubusercontent.com/Mathorck/EasyStock/main/docs/images/icon.png" alt="EasyStock icon" width="96">
</p>

# 🗄️ EasyStock Server: the optional shared-stock server

<p align="center">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue"></a>
  <img alt="Status: under development" src="https://img.shields.io/badge/status-under%20development-orange">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white">
  <img alt="PostgreSQL 16+" src="https://img.shields.io/badge/PostgreSQL-16%2B-4169E1?logo=postgresql&logoColor=white">
  <img alt="Docker amd64 and arm64" src="https://img.shields.io/badge/docker-amd64%20%7C%20arm64-2496ED?logo=docker&logoColor=white">
</p>

---

## ⚠️ Under development: not usable yet

- **The server is being built.** Only the skeleton exists: configuration, `/health`, `/openapi/v1.json`, `GET /api/v1/server-info` (placeholder values) and the app-version check. There is no login, no sync and no image published yet.
- Everything marked **(planned)** below describes the intended behavior, not what runs today.
- Do not host real data on it. The data format and API may change without notice.

---

## About EasyStock Server

EasyStock is a free, open-source Android app to manage a physical stock with NFC tags: [Mathorck/EasyStock](https://github.com/Mathorck/EasyStock). It works fully offline on the phone, and **standalone stays the default**.

This server is an **optional** add-on. It lets several phones share one stock, with users, roles, movement history and low-stock alerts. It is meant to be hosted by anyone (a small business, an association, a hobbyist) and released under the MIT license, like the app.

---

## Features (planned)

- **Shared stock**: several phones sync one catalogue, locations and stock.
- **Roles**: `admin`, `member`, `viewer` and `guest`, with rights checked on every request.
- **Movement history**: stock movements are append-only, with the author.
- **Low-stock alerts**: sent to phones through [UnifiedPush](https://unifiedpush.org), no Google services needed.
- **Invites and Guest QR code**: add users with a single-use invite, or let people join as Guests with a printed QR code.
- **Photos and backups**: photos stored on the server, backup export and import by an Admin.

---

## Requirements

- Docker with Docker Compose.
- PostgreSQL 16 or newer (the Compose file starts one for you).
- Modest hardware: a small VPS, a NAS or a Raspberry Pi 4 or newer. Images are built for `linux/amd64` and `linux/arm64`.
- A reverse proxy for HTTPS (see [HTTPS](#-https)).

---

## Quick start (planned)

The Compose file exists in [`server/docker-compose.yml`](server/docker-compose.yml), but no image is published yet. Until a release exists, build it yourself with `docker compose up -d --build` in `server/`.

```yaml
services:
  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: easystock
      POSTGRES_USER: easystock
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data

  server:
    image: ghcr.io/mathorck/easystock-server:latest
    depends_on:
      db:
        condition: service_healthy
    environment:
      EASYSTOCK_DB: Host=db;Database=easystock;Username=easystock;Password=${POSTGRES_PASSWORD}
      EASYSTOCK_PUBLIC_URL: ${EASYSTOCK_PUBLIC_URL}
    volumes:
      - easystock-data:/data
    ports:
      - "8080:8080"

volumes:
  pgdata:
  easystock-data:
```

(The real file also has a database health check, restart policies and the optional variables.) Copy [`server/.env.example`](server/.env.example) to `.env`, set at least `POSTGRES_PASSWORD` and `EASYSTOCK_PUBLIC_URL`, then:

```
docker compose up -d
```

---

## Configuration

All settings come from environment variables, with safe defaults. Everything else (Guest limit, retention, alert recipients) is set by an Admin in the app and stored in the database.

| Variable | Required | Default | Content |
|---|---|---|---|
| `EASYSTOCK_DB` | yes | | PostgreSQL connection string |
| `EASYSTOCK_PUBLIC_URL` | yes | | Address phones use, written into QR codes (absolute `http(s)` URL) |
| `EASYSTOCK_DATA_DIR` | no | `/data` | Folder for photos, the signing key and data-protection keys |
| `EASYSTOCK_CERT_FINGERPRINT` | no | | SHA-256 of a self-signed certificate, written into QR codes |
| `EASYSTOCK_LOG_LEVEL` | no | from `appsettings.json` (`Information`) | Log level: `Verbose`/`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`/`Critical`. Overrides `appsettings.json` |
| `EASYSTOCK_TRUSTED_PROXIES` | no | empty | Comma-separated IPs or CIDR ranges of your reverse proxy |

The server refuses to start if a required variable is missing or invalid.

Logging uses Serilog and writes to the console. Levels can also be set in the `Serilog` section of `appsettings.json`, or with variables such as `Serilog__MinimumLevel__Override__Microsoft.AspNetCore=Information`. SQL commands always stay at `Warning`.

> **Set `EASYSTOCK_TRUSTED_PROXIES` when you use a reverse proxy.** Without it, the server does not trust `X-Forwarded-For`, so all clients behind the proxy share one rate-limit bucket.

---

## First start (planned)

On the first start with no user, the server prints a **setup token** and a `setup` QR code drawn in text in its log (`docker compose logs server`). Scan it with the app to create the first Admin.

- This is **the one place a token appears in the logs**. Everything else is kept out of them.
- The token is valid until the first Admin is created. A restart before that prints a new one.
- After setup, the setup endpoint answers 409 forever.

---

## 🔒 HTTPS

The server speaks plain HTTP on port 8080 and expects a reverse proxy for HTTPS. Plain HTTP works too, but the app warns before it connects.

**Caddy** (automatic Let's Encrypt), proxying to the container:

```
stock.example.org {
    reverse_proxy localhost:8080
}
```

Then set `EASYSTOCK_PUBLIC_URL=https://stock.example.org` and `EASYSTOCK_TRUSTED_PROXIES` to the proxy address.

**Self-signed certificate**: compute its SHA-256 fingerprint and put it in `EASYSTOCK_CERT_FINGERPRINT`, so the QR codes carry it and the app can pin it:

```
openssl x509 -in cert.pem -noout -fingerprint -sha256
```

---

## Backups

Your data lives in two places. **Back up both**:

- the PostgreSQL volume (`pgdata`), for example with `pg_dump`;
- the `/data` volume (`easystock-data`): photos, the token signing key and data-protection keys.

An Admin can also export a backup file with photos from the app (planned).

---

## 🛡️ Privacy

Built to store as little personal data as possible, in line with the Swiss nFADP and the GDPR.

- **Stored**: username, display name, role, last seen, device names, and who made each stock movement.
- **Not stored**: no email address, no phone number, no IP address in the database.
- **Logs** never contain passwords, tokens, display names or row content, only user IDs (the setup token is the single exception, see [First start](#first-start-planned)).
- **Retention**: an Admin can set how long the author of a movement is kept (default: keep). Deleting a user keeps their movements, but drops the link to a name.
- **You are the data controller.** Whoever hosts a server is responsible for the data on it: informing users, handling their requests, securing the host and the backups.

---

## Development

Requirements: the .NET 10 SDK and Docker (the tests start PostgreSQL with Testcontainers).

```
cd server
dotnet build
dotnet test
```

| Path | Content |
|---|---|
| `server/src/EasyStock.Server` | The web API (minimal APIs, EF Core, Npgsql) |
| `server/tests/EasyStock.Server.Tests` | xUnit tests against a real PostgreSQL |
| `server/Dockerfile`, `server/docker-compose.yml` | Hosting |
| `docs/` | [Specification](docs/03-server-spec.md) and [implementation plan](docs/04-server-implementation-plan.md) |

CI (`.github/workflows/ci.yml`) builds and tests `server/` on every push to `main` and every pull request.

Branch from `main`, open a pull request. Commits follow [Conventional Commits](https://www.conventionalcommits.org), like the app. Code, comments and commit messages in English.

---

## Releases

Tags are plain [SemVer](https://semver.org), no `v` prefix. Pushing a tag `X.Y.Z` runs [`.github/workflows/release.yml`](.github/workflows/release.yml): tests, then a multi-arch Docker image (`linux/amd64`, `linux/arm64`) published to GitHub Container Registry as `ghcr.io/mathorck/easystock-server:X.Y.Z` and `:latest`.

```
git tag 0.1.0
git push origin 0.1.0
```

---

## License

[MIT](LICENSE), © 2026 EasyStock contributors.
