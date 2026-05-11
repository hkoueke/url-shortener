# Internal URL Shortener Service

## Implemented capabilities

- Shorten (`POST /api/v1/urls`) with optional custom alias, 409 conflict on alias collision, idempotency key support.
- Redirect (`GET /{code}`) with disabled/expired enforcement.
- Stats (`GET /api/v1/urls/{code}/stats`), top links (`GET /api/v1/urls/top`), and time-series analytics (`GET /api/v1/urls/{code}/stats/timeseries`).
- Lifecycle admin updates (`PATCH /api/v1/urls/{code}`) with ownership enforcement and audit entries.
- Team-aware domain allowlist policy, and team-partitioned rate limiting.
- Keycloak JWT auth with `links:write`, `links:read`, `links:admin` policies.
- Snowflake IDs + Base62 codes, FusionCache fail-safe + Redis L2/backplane, OpenTelemetry.
- MassTransit-based streaming for audit/analytics events with RabbitMQ transport and consumer wiring.
- Prometheus metrics endpoint with SLO dashboard/alerts templates under `ops/`.

## Production setup

Set environment variables:

- `ConnectionStrings__Postgres`
- `ConnectionStrings__Redis`
- `Keycloak__ServerUrl`
- `Keycloak__Realm`
- `Keycloak__Audience`
- `team` (preferred), `tenant`, or `group` claim must be mapped from Keycloak for ownership enforcement.
- `SNOWFLAKE_WORKER_ID`
- `Shortener__AllowedHosts` (fallback allowlist)
- `Shortener__AllowedHostsByTeam__platform=example.org,docs.example.org`
- `Streaming__Provider=RabbitMq`
- `Streaming__RabbitMqHost=localhost`
- `Streaming__RabbitMqUsername=guest`
- `Streaming__RabbitMqPassword=guest`

## API quick examples

```bash
curl -X POST http://localhost:5000/api/v1/urls \
  -H "Authorization: Bearer <token with links:write>" \
  -H "Idempotency-Key: req-123" \
  -H "Content-Type: application/json" \
  -d '{"longUrl":"https://example.org/docs","customCode":"docs-home","ownerTeam":"platform"}'
```
