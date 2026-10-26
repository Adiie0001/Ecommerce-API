# Ecommerce API

[![.NET Build & Test](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/dotnet.yml)
[![CodeQL](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/codeql.yml/badge.svg)](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/codeql.yml)
![.NET](https://img.shields.io/badge/.NET-10_LTS-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![EF Core](https://img.shields.io/badge/EF_Core-10_+_SQLite-512BD4?style=flat-square)
![JWT](https://img.shields.io/badge/auth-JWT_+_refresh_rotation-000000?style=flat-square)

A product-catalog REST API built with ASP.NET Core: JWT sign-in with rotating refresh tokens, role-based access,
searchable and pageable product listing, optimistic concurrency on edits, and a natural-language product search that
uses a deterministic ranking engine by default and can optionally ask Google Gemini. A small React admin console
(`ecommerce-admin/`) runs against the real API.

Everything stated here is something a test, the CI workflow or a file in this repository demonstrates. Limits and
things that have not been exercised are listed at the end.

## Contents

- [Run it](#run-it) · [Configuration](#configuration) · [API](#api) · [Authentication](#authentication)
- [Recommendations](#recommendations) · [Admin console](#admin-console) · [Quality gates](#quality-gates)
- [Docker](#docker) · [Design decisions](#design-decisions) · [Project layout](#project-layout)
- [Changes since the first release](#changes-since-the-first-release) · [Known limits](#known-limits)

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Adiie0001/Ecommerce-API.git
cd Ecommerce-API
dotnet run
```

`dotnet run` uses the `Development` environment (see `Properties/launchSettings.json`): it needs no secrets, creates
`ecommerce.db`, applies the migrations, inserts a 12-item demo catalog and two demo accounts, and serves Swagger UI.

| URL | What |
|---|---|
| http://localhost:5000/swagger | Interactive docs with a "Authorize" button for Bearer tokens |
| http://localhost:5000/health/live | Liveness: the process is up (no database access) |
| http://localhost:5000/health/ready | Readiness: the database is reachable (503 otherwise) |

Demo accounts, **Development only** (the app refuses to start with them in any other environment):

| Role | Email | Password |
|---|---|---|
| Admin | `admin@example.com` | `Admin@Demo12345` |
| Customer | `customer@example.com` | `Customer@Demo12345` |

In `Development` the JWT signing key is a random per-process value, so tokens stop working when the process restarts.

## Configuration

Settings come from `appsettings*.json`, environment variables (`Section__Key`) and user-secrets, in the usual
ASP.NET Core order. The application validates them at startup and refuses to run on a bad combination.

| Setting | Default | Notes |
|---|---|---|
| `Jwt__SigningKey` | none | **Required outside Development.** At least 32 bytes; placeholders such as `YourSecretKey…` are rejected. Generate: `openssl rand -base64 48` |
| `Jwt__AccessTokenMinutes` / `Jwt__RefreshTokenDays` | 15 / 14 | |
| `ConnectionStrings__DefaultConnection` | `Data Source=ecommerce.db` | SQLite |
| `Database__ApplyMigrationsOnStartup` | `false` (`true` in Development and in the Docker image) | Otherwise run `dotnet ef database update` |
| `Seed__DemoData` | `false` | Allowed only in Development |
| `Seed__AdminEmail`, `Seed__AdminPassword` | none | Creates the first administrator once, if none exists. Both or neither; password must pass the policy and may not be a demo password |
| `Auth__BcryptWorkFactor`, `Auth__MaxFailedAttempts`, `Auth__LockoutMinutes` | 12, 5, 15 | |
| `RateLimiting__Enabled`, `__AuthPerMinute`, `__RecommendPerMinute`, `__GlobalPerMinute` | on, 10, 20, 240 | Per client IP, in memory |
| `Cors__AllowedOrigins__0` … | none | Browser origins allowed to call the API |
| `Swagger__Enabled` | `false` (`true` in Development) | |
| `Recommendations__Gemini__Enabled`, `__ApiKey`, `__Model` | off | See [Recommendations](#recommendations) |
| `Security__HttpsRedirection` | `false` | Turn on when Kestrel itself terminates TLS; usually a proxy does |

Behind a reverse proxy you control, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so rate limiting sees the real
client address (the Docker image does this). Never expose such an instance directly: the setting trusts whatever
`X-Forwarded-*` headers arrive, so a direct client could pretend to be someone else.

## API

Routes live under `/api/v1`. The first release's `/api/auth` and `/api/product` paths still work as aliases and
answer with `Deprecation: true` and a `Link: <…>; rel="successor-version"` header. Errors are RFC 9457 problem
documents (`application/problem+json`) carrying a `traceId` and the request's `correlationId`; send your own
`X-Correlation-ID` (letters, digits, `-_.`, up to 64 characters) and it is echoed back and added to every log line.

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/auth/register` | public, rate limited | Creates a **customer**. Roles are never read from the request |
| POST | `/auth/login` | public, rate limited | Email + password → access token + refresh token |
| POST | `/auth/refresh` | public, rate limited | Rotates the refresh token |
| POST | `/auth/logout` | public, rate limited | Revokes the session; always 204 |
| GET | `/auth/me` | signed in | |
| GET | `/products` | public | `page`, `pageSize` (≤100), `q`, `minPrice`, `maxPrice`, `inStock`, `sort` (`name`/`price`/`stock`/`created`/`updated`), `order` |
| GET | `/products/{id}` | public | |
| GET | `/products/recommend?query=` | public, rate limited | See [Recommendations](#recommendations) |
| GET | `/products/stats` | Admin | Counts, units and inventory value computed in SQL |
| POST | `/products` | Admin | 201 + `Location` |
| PUT | `/products/{id}` | Admin | Requires the `version` from the last read; stale → 409 |
| DELETE | `/products/{id}` | Admin | 204 / 404 |

Money is a decimal with at most two places in JSON and integer cents in the database, so sorting, filtering and
summing are exact.

## Authentication

```bash
# sign in
curl -s localhost:5000/api/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"admin@example.com","password":"Admin@Demo12345"}'
# → { "accessToken": "…", "tokenType": "Bearer", "expiresIn": 900, "refreshToken": "…", "user": { … } }

# call an admin route
curl -s localhost:5000/api/v1/products/stats -H "Authorization: Bearer $ACCESS"

# rotate: the old refresh token stops working immediately
curl -s localhost:5000/api/v1/auth/refresh -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$REFRESH\"}"
```

- Access tokens: HS256 JWT, 15 minutes, issuer / audience / lifetime / algorithm validated, `role` claim enforced by
  the `AdminOnly` policy.
- Refresh tokens: 256-bit random, only a SHA-256 hash is stored, single use. Presenting a token that was already
  rotated is treated as theft: the **whole session family is revoked**, including the newest token.
- Passwords: BCrypt (work factor 12) with the SHA-384 pre-hash, so long passphrases are not truncated. Policy: 10–128
  characters with upper case, lower case and a digit; common passwords and the email's name part are rejected.
- Sign-in failures (unknown account, wrong password, locked out) return the same 401, and the password hash is checked in every case so response time does not reveal which one it was.
  Five consecutive failures lock the account for 15 minutes.

## Recommendations

`GET /api/v1/products/recommend?query=cheap wireless mouse` returns ranked products. Each answer says which engine
produced it:

| `source` | Meaning |
|---|---|
| `rules` | The deterministic engine answered. AI is not configured; nothing left the server |
| `gemini` | The model ranked a bounded candidate set and the answer passed validation |
| `rules-fallback` | AI is configured but failed (timeout, HTTP error, open circuit breaker, unreadable or invalid answer) so the deterministic engine answered |

**Deterministic engine** (`RuleBasedRecommender`): tokenises and lightly stems the query, weights name matches above
description matches, weights rare words above common ones (IDF), understands price intent words such as "cheap" and
"premium" relative to the catalog's own price quantiles, ranks sold-out items lower and returns nothing, not filler,
when nothing is relevant. Same input, same output.

**Gemini** (optional, off by default): enable with `Recommendations__Gemini__Enabled=true` and
`Recommendations__Gemini__ApiKey`. At most 40 products are sent, and only `id`, `name`, a truncated `description`,
`price` and an in-stock flag; the shopper's text is passed as data, not as instructions. The key travels in the
`x-goog-api-key` header, never in the URL. The model's answer is treated as untrusted: ids must belong to the sent
candidates, scores are clamped to 0–100, reasons are reduced to short plain text. Calls go through a resilience
pipeline (timeout, one retry, circuit breaker) and successful answers are cached for 5 minutes per query and catalog
version.

## Admin console

`ecommerce-admin/` is a React 19 + Vite + Tailwind app that requires the Admin role: sign-in, server-side search /
sort / paging, create / edit / delete with conflict handling, inventory totals, a live readiness badge, and a tester
for the recommendation endpoint. See [its README](ecommerce-admin/README.md).

## Quality gates

```bash
dotnet build -warnaserror                 # zero warnings
dotnet test                               # API: integration + unit tests
cd ecommerce-admin && npm test            # admin: client, auth flow, table, form, recommendation panel
```

The integration tests start the real application in-process against a temporary SQLite file with the real migrations
applied, and swap only the clock and the Gemini HTTP endpoint. They cover, among other things: a role matrix for every
protected route (anonymous / customer / admin), token tampering and expiry, refresh-token rotation and replay,
lockout, concurrent edits (exactly one winner), listing filters and paging, every Gemini failure path, the circuit
breaker, rate limits, CORS, security headers, problem-document shapes, and startup refusal for unsafe configuration.
A test also fails if the EF model changes without a migration.

CI (`.github/workflows/dotnet.yml`) runs the API build with warnings as errors, the tests with a coverage gate
(**line ≥ 94 %, branch ≥ 88 %, method ≥ 96 %**; measured 97.4 / 91.2 / 99.2 when the gate was set), the admin app's
lint / tests / build, and a Docker image build. CodeQL and Dependabot are configured.

## Docker

```bash
cp .env.example .env     # set JWT_SIGNING_KEY (and optionally ADMIN_EMAIL / ADMIN_PASSWORD)
docker compose up --build
# http://localhost:8080/health/ready
```

The image is a multi-stage build that runs as a non-root user, keeps the SQLite file on a volume and applies
migrations on start. Status: CI builds the image on every push; `docker compose up` itself has not been exercised end
to end yet.

## Design decisions

| Decision | Why |
|---|---|
| One web project, folders per concern, interfaces only at real seams (AI provider, clock, token service, password hasher) | A reviewer can read it in one sitting; no abstraction without a second implementation or a test that needs it |
| Money as integer cents | SQLite stores decimals as text, which cannot be ordered or summed correctly |
| Optimistic concurrency with a `version` token | Two admins editing the same product cannot silently overwrite each other |
| Refresh-token families with reuse detection | A stolen refresh token is detected the first time both parties use it |
| Rule engine as the default, AI as an optional layer with an honest `source` field | The feature works offline and in tests; AI failure degrades visibly instead of silently |
| `TimeProvider` everywhere | Token expiry, lockout and caching are tested by moving the clock, not by sleeping |
| Explicit mapping methods instead of a mapping library | A renamed property fails the build instead of failing at runtime |
| Migrations applied by opt-in config, never implicitly in production | The operator decides when a schema changes |

## Project layout

```
Controllers/          HTTP surface: AuthController, ProductsController
Contracts/            Request / response types (separate from entities)
Domain/               Entities, enums, domain exceptions
Data/                 DbContext, migrations, startup initializer, demo data
Services/             AuthService, ProductService, token + password services
Services/Recommendations/   Rule engine, Gemini client + recommender, fallback selector
Infrastructure/       Options + validators, middleware, exception handling, startup wiring
EcommerceAPI.Tests/   xUnit tests (integration + unit)
ecommerce-admin/      React admin console
```

## Changes since the first release

The first release was a CRUD sample; these changes are deliberate and some are breaking:

- Targets .NET 10 (LTS); .NET 8 leaves support in November 2026.
- Sign-in uses **email** (was username). Registration no longer accepts a `User` entity: it takes
  `email`, `displayName`, `password`, always creates a customer, and never returns a hash.
- Product updates require `version` and return the updated product (was 204); listing returns a paging envelope.
- `/api/auth/protected` is replaced by `/api/v1/auth/me`.
- The seeded demo accounts changed (see above) and exist only in Development.
- AutoMapper and the SQL Server provider were removed; migrations exist for SQLite only.
- The earlier "AI" recommender was a hash-based simulation; it was replaced by the real ranking engine above.

## Known limits

- SQLite is a single-node database; the in-memory rate limiter and the recommendation cache are per instance.
- No email verification, password reset, cart or orders. The catalog and identity model leave room for them.
- The admin console keeps its refresh token in `sessionStorage` (see its README for the trade-off).
- Recommendations consider the first 500 products; a larger catalog needs a retrieval index.
- The Gemini request and response handling is tested against a fake HTTP endpoint only; it has not been run against
  the live service in CI.
- The Docker files are built in CI but `docker compose up` has not been run end to end.
- TLS termination, backups and secret management are left to the deployment platform.

## Author

Aditya Maisuriya · [Portfolio](https://adityamaisuriya.pages.dev) · [GitHub](https://github.com/Adiie0001) ·
[LinkedIn](https://linkedin.com/in/aditya-maisuriya-39a540202)
