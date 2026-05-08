# Decisions — saas-billing-plugin

## 2026-05-08 Session Start
- Dual-license: AGPLv3 + commercial. Add LICENSE-COMMERCIAL.md.
- EF migrations: separate BillingDbContext in plugin.
- Subscription entity: DELETE cleanly.
- Plugin load failure: hard crash.
- Ingest bytes: all uploaded chunks including duplicates.
- Quota enforcement: session-create REST endpoint only, 60s TTL IMemoryCache.
- IBillingPluginRegistrar: two methods — Register() before builder.Build(), MapEndpoints() after.
- Plugin DLL path: env var BINSTASH_BILLING_PLUGIN_PATH only.
- BinStash.SaaS MAY reference BinStash.Infrastructure for BinStashDbContext (tenant provisioning) but must NOT add plugin entities to it.
