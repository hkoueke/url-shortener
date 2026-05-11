# URL Shortener SLO Dashboard Preset

## SLOs
- Redirect latency p95 < 150ms
- Redirect success ratio > 99.9%
- Authenticated shorten success ratio > 99.5%
- Cache hit ratio >= 0.80

## Metrics mapping
- `http.server.request.duration` (route: `/{code}`)
- `shortener.redirects.executed`
- `shortener.links.created`
- `shortener.cache.hit_ratio`
