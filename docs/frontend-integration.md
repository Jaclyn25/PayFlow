# Frontend Integration Readiness

## API base URL

The API is exposed by Docker on `http://localhost:5000`. The development Vite
server is expected to run on `http://localhost:5173`.

## CORS

Allowed browser origins are configured through `Cors:AllowedOrigins`. The
development configuration allows only `http://localhost:5173`. Docker Compose
reads the first origin from `FRONTEND_ORIGIN` in `.env`.

Origins must be explicit. Do not use `*` when credentials are enabled, and do
not add production origins to development configuration.

## Authentication contract

`POST /api/auth/register`, `POST /api/auth/login`, and `POST /api/auth/refresh`
return an access token and a refresh token in the JSON response body. Refresh
tokens are hashed at rest and rotated on refresh; reuse of a revoked token is
rejected by the API.

The current contract does not use an HttpOnly cookie. A future cookie-based
contract must be introduced as a separately reviewed breaking/security change
with an explicit CSRF strategy and updated API and frontend tests. Until then,
the frontend must not log tokens or include them in telemetry.

## Error responses

Validation and domain errors are returned as RFC 7807 `ProblemDetails` by the
API exception middleware. Frontend clients should preserve the response status,
`title`, `detail`, and validation `errors` when presenting failures.

## Financial request requirements

- `POST /api/transfer` requires an `Idempotency-Key` header.
- The backend is the source of truth for balances and transaction results.
- A client must not retry a timed-out transfer with a new idempotency key until
  the original operation has been reconciled.
