# Shop admin

A small React admin console for the [Ecommerce API](../README.md). It talks to the real API: sign-in, product
listing with server-side search, sorting and paging, create / edit / delete, inventory totals, and a tester for the
recommendation endpoint. Nothing on the screen is hard-coded.

## Run it

```bash
# 1. start the API (from the repository root) - Development seeds demo accounts
dotnet run

# 2. start the admin app
cd ecommerce-admin
npm ci
npm run dev        # http://localhost:5173
```

Sign in with the demo administrator printed in the API README (Development only). Customers are turned away: the
console requires the Admin role, and the API enforces that independently of this UI.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `VITE_API_URL` | `http://localhost:5000` | Base URL of the API. The API must list this app's origin under `Cors:AllowedOrigins`. |

## Scripts

| Command | What it does |
|---|---|
| `npm run dev` | Vite dev server |
| `npm run build` | Production bundle in `dist/` |
| `npm run lint` | oxlint |
| `npm test` | Vitest + Testing Library (API client, auth flow, table, form, recommendation panel) |

## How sessions work

The access token is kept in memory only. The single-use refresh token is kept in `sessionStorage`, so reloading the
page keeps you signed in while closing the tab ends the session. Expired access tokens are refreshed once
automatically (parallel requests share one refresh). `sessionStorage` is readable by any script running on the same
origin, so this app deliberately has no third-party scripts.

## Editing products safely

Every product carries a `version`. Saving sends the version that was loaded; if someone else changed the product in
the meantime the API answers 409 and the form offers "Load latest values" instead of overwriting their edit.
