# EShop Microservices — roadmap

Progress checklist for the resume-ready project. Mark an item complete only after its implementation and relevant checks pass.

1. [~] EF Core 10 migrations exist for all services and are applied at startup; CI checks for pending model changes. Legacy adoption review is blocked until the target database and its migration history are available.
2. [x] Database/schema initialization runs asynchronously in cancellable hosted startup services, outside DbContext constructors and before requests are served.
3. [x] Forty-two SQLite/HTTP tests cover protected routes, signed JWT/RBAC, request validation and HTTP CRUD for Product, Customer, and Order, Customer/Order ownership enforcement, a real Order → Product HTTP flow for inventory rejection and reservation release, Gateway outcomes/readiness/resilience, sanitized Problem Details from all applications, W3C propagation, inventory and Customer projection idempotency, database health checks, filtering, sorting, outbox persistence, cancellation compensation, and stale reservation recovery. A separate SQL Server migration test runs in CI.
4. [~] GitHub Actions is configured to provision SQL Server and apply each service's migrations to isolated clean databases; the first CI execution is pending, and local SQL Server execution remains unavailable because Docker Engine cannot start in this environment.
5. [x] Gateway tests cover missing customer (404), sanitized downstream failure (502), timeout (504), delayed response, cancellation propagation and successful aggregation.
6. [~] CI builds/tests, treats NuGet audit advisories NU1901–NU1904 as errors, validates Compose and observability configs, checks EF model drift, builds images and publishes Coverlet artifacts; coverage gate enforces 20% line and 30% branch minimums. Dependabot now schedules weekly NuGet, Docker base-image and GitHub Actions updates, grouping NuGet patch/minor updates. Latest local measurement (42 tests): 25.42% line and 35.63% branch; stricter gate passed. The SqlClient transitive Pkcs advisories were fixed. Docker image builds and containerized observability validation remain unavailable locally.
7. [~] Microservices check database readiness and Gateway checks downstream readiness; a SQLite test exercises the three microservice database health checks, and Compose probes/dependency ordering are configured. Runtime probes remain unverified without Docker Engine.
8. [x] Customer, Order, and Product APIs use validated request DTOs and response DTOs.
9. [x] All exception middleware uses Problem Details, hides exception internals and includes a trace ID; tests exercise all four handlers and Gateway's sanitized downstream transport failure.
10. [x] Collection endpoints enforce page size and safe Skip bounds, offer allow-listed sorting and have SQLite pagination/sorting coverage.
11. [x] Product filters by indexed name prefix and rate range; catalog filter behavior is covered by SQLite tests.
12. [~] Product reserve/release operations are idempotent by reservation ID, including release tombstones to block late reserve retries and a post-rollback key recheck when a concurrent reserve consumed the stock first. SQLite verifies repeated calls; concurrent SQL Server verification remains.
13. [~] Order records reservation intent before calling Product, commits order/outbox and reservation state atomically, and runs a stale-intent recovery worker. SQLite verifies cancellation compensation and stale-intent recovery; cross-service crash recovery still needs SQL Server/RabbitMQ runtime verification.
14. [~] RabbitMQ transports versioned order-created events to an idempotent Customer projection; a SQLite test verifies duplicate event delivery produces one projection. Broker-backed end-to-end tests and operational dashboard runtime checks remain blocked by unavailable Docker Engine.
15. [x] Gateway uses standard HttpClient resilience; integration tests verify transient-503 retry, hanging-downstream timeout, circuit opening after repeated failures and concurrency-limit rejection.
16. [x] W3C Activity is propagated across HttpClient calls and the trace ID is added to exception log scopes; a real local HTTP test verifies traceparent continuity.
17. [~] OpenTelemetry Collector routes traces to Jaeger and metrics to Prometheus; Grafana datasource/dashboard are provisioned and CI validates Collector, Prometheus and dashboard JSON configs. Runtime metrics/dashboard verification remains blocked by unavailable Docker Engine.
18. [x] JWT validation, role-protected catalog writes, customer/order ownership, service-key inventory reserve/release and per-operation Swagger auth schemes are configured and tested. The API intentionally consumes tokens from an external trusted issuer; token issuance is outside this service's scope.
19. [~] Secrets are externalized, demo seeding defaults to Development, and each service has separate runtime and migration logins. Runtime users receive only data reader/writer roles; startup migrations temporarily use the migration connection. SQL Server role and startup verification remain blocked by unavailable Docker Engine.
20. [~] README, Mermaid architecture diagram, reproducible Compose instructions, REST Client cookbook, and a 3–4 minute recording outline exist; the video recording remains blocked until the Docker stack can run.

## Verified baseline

- `dotnet build Microservice.WebApi.sln -c Release -m:1 -p:NuGetAudit=false` succeeds on .NET 10.
- Forty-two tests passed and the SQL Server migration test was skipped without `ESHOP_SQLSERVER_ADMIN_CONNECTION`; the HTTP test host and Coverlet DataCollector need local socket access.
- Before enabling startup migrations against an existing database, take a backup and verify its migration history/schema. The current service models have different legacy migration histories.
- Docker validation is blocked because `docker info` cannot connect to `npipe:////./pipe/dockerDesktopLinuxEngine` (permission denied). `docker compose config -q` validates the Compose file, but containers were not started.
- Dockerfiles target the .NET 10 SDK and runtime; image builds remain unverified locally because Docker Engine is unavailable.
- The latest successful Coverlet report (42 executed tests; one SQL Server test skipped) measured 25.42% line and 35.63% branch coverage; `eng/verify_coverage.ps1 -ResultsDirectory TestResults -MinimumLine 20 -MinimumBranch 30` passed.
- The outbox provides at-least-once delivery; consumers must remain idempotent because a process can stop after broker publish and before marking an outbox row as published.
