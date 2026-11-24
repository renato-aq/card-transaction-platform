# 2. Use .NET Aspire for local orchestration

**Status:** Accepted

## Context

The platform runs four services plus PostgreSQL and Kafka. Starting them by hand, or keeping a
docker-compose file in sync with service configuration, is error-prone and slows down development.

## Decision

Use .NET Aspire as the local orchestrator. The `AppHost` project declares every resource and its
dependencies; `ServiceDefaults` applies OpenTelemetry, health checks, resilience and service discovery
to every service.

## Consequences

- The whole platform starts with `dotnet run --project src/CardPlatform.AppHost`.
- Connection strings and service URLs are injected by Aspire instead of being hard-coded.
- Traces, metrics and logs are visible in the Aspire dashboard from day one.
- A container runtime is required for local development.
