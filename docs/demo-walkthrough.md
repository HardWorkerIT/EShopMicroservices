# Demo walkthrough (3–4 minutes)

Use this outline to record a short project walkthrough after the Docker environment is available. Keep `.env`, credentials, signing keys, and service keys out of the recording.

## Before recording

1. Copy `.env.example` to `.env` and set private values locally.
2. Start the stack with `docker compose up --build -d` and wait until all services report healthy with `docker compose ps`.
3. Open the Aggregator Swagger page at `http://localhost:5000/swagger`, the Product Swagger page at `http://localhost:5003/swagger`, and Grafana at `http://localhost:3000`.
4. Have a valid JWT issued by a trusted development issuer ready if you plan to show protected Customer or Order operations. This repository validates tokens but does not issue them.

## Recording outline

### 0:00–0:30 — What the project demonstrates

Introduce the ASP.NET Core microservice shop: Customer, Order, Product, and the Aggregator. Briefly show the architecture diagram in the README. Explain that each service owns its database.

### 0:30–1:10 — Run and inspect the APIs

Show `docker compose ps` with the services healthy, then open Swagger. Point out the separate APIs and their `/health/live` and `/health/ready` endpoints. Mention that readiness includes database connectivity and Gateway readiness checks downstream services.

### 1:10–1:50 — Aggregation and inventory

Run the seeded customer aggregation request:

```powershell
Invoke-RestMethod http://localhost:5000/api/v1/Shop/Customers/Guest | ConvertTo-Json -Depth 6
```

Show that the Gateway combines customer, order, and product data. In Product Swagger, demonstrate the catalog filters and pagination. Explain that Order calls Product over HTTP to reserve stock and uses a stable reservation ID for safe retries and compensation.

### 1:50–2:30 — Security and event delivery

In Swagger, show the Bearer requirement for Customer and Order writes, the Admin role requirement for catalog writes, and the internal service-key scheme for inventory operations. Avoid displaying any token or key value. Explain that order creation saves its event to a transactional outbox, and Customer deduplicates projections by event ID when consuming RabbitMQ messages.

### 2:30–3:00 — Observability

Open the provisioned Grafana EShop dashboard and point out the Prometheus datasource. If traces are flowing, show a request trace in Jaeger at `http://localhost:16686` and connect the trace to the W3C `traceparent` propagation demonstrated by the project tests.

### 3:00–3:30 — Engineering checks and limits

Show the integration test project and run:

```powershell
dotnet test .\Integration.Tests\Integration.Tests.csproj --configuration Release
```

Close by naming the main guarantees: request validation, ownership checks, resilient Gateway calls, idempotent inventory reservations, outbox delivery, and cancellation recovery. State that token issuance is external and that SQL Server/RabbitMQ production failure behavior needs deployment-specific verification.

## Recording status

This outline is ready to record. The video itself still needs a Docker-enabled environment with working SQL Server, RabbitMQ, and observability containers.
