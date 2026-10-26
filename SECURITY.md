# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private
[security advisory form](https://github.com/Adiie0001/Ecommerce-API/security/advisories/new) for this repository
and include steps to reproduce. You can expect an acknowledgement within a few days.

## Scope notes

- This is a portfolio project. No production data is stored in it.
- The demo accounts documented in the README exist only when the API runs in the `Development` environment; the
  application refuses to start with them in any other environment.
- Secrets (the JWT signing key, the Gemini API key, the bootstrap admin password) are supplied through environment
  variables or user-secrets and are never committed.
