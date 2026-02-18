# Wolverine Template

A reusable .NET 10 starter template featuring **Wolverine** (HTTP + messaging + outbox), **EF Core + PostgreSQL**, **Supabase JWT auth**, **Serilog + Seq**, and **DDD patterns**.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- A [Supabase](https://supabase.com) project (for JWT authentication)

## Quick Start

```bash
# 1. Start dev infrastructure (PostgreSQL, pgAdmin, Seq)
docker compose up -d

# 2. Update Supabase URL in appsettings.Development.json
#    Replace the placeholder with your Supabase project URL

# 3. Run the API
cd src/Api
dotnet run

# 4. Open Scalar API docs
#    http://localhost:5000/scalar/v1
```

## Dev Infrastructure

| Service  | URL                        | Credentials              |
|----------|----------------------------|--------------------------|
| API      | http://localhost:5000      | -                        |
| pgAdmin  | http://localhost:5050      | admin@local.dev / admin  |
| Seq      | http://localhost:8081      | -                        |

## Project Structure

```
wolverine-template/
├── docker-compose.yml              # Dev infrastructure
├── Dockerfile                      # Production container
├── wolverine-template.slnx         # Solution file
├── src/Api/                        # Main API project
│   ├── Auth/                       # Supabase claims transformation
│   ├── Data/                       # EF Core DbContext + migrations
│   ├── Domain/                     # DDD entities + events
│   ├── Extensions/                 # ClaimsPrincipal helpers
│   ├── Features/                   # Feature-based endpoints
│   ├── Middleware/                  # Auto user sync from JWT
│   └── Services/                   # JWKS, email abstractions
└── tests/Api.Tests/                # Unit tests
```

## Key Patterns

### Wolverine HTTP Endpoints
- Static methods with `[WolverineGet/Post]` attributes
- FluentValidation auto-wired via middleware
- Compound handler pattern: `LoadAsync` -> `Validate` -> `Handle`
- Tuple returns for cascading domain events

### DDD Entities
- Private constructors with static `Create()` factory methods
- Private setters for immutability
- Domain events via `BaseEntity.RaiseDomainEvent()`

### Authentication
- Supabase JWT with JWKS key rotation
- Auto user sync middleware (creates DB user from JWT on first request)
- Claims transformation for role-based auth

### Messaging
- PostgreSQL transactional outbox (no RabbitMQ needed)
- Automatic retry policies for transient failures
- Domain events cascaded from HTTP endpoints

## Database Migrations

```bash
cd src/Api

# Create a migration
dotnet ef migrations add MigrationName

# Apply migrations (auto-applied on startup in dev)
dotnet ef database update

# Generate SQL script for production
dotnet ef migrations script -o migrations.sql
```

## Running Tests

```bash
dotnet test
```

## Configuration

Development settings are in `appsettings.Development.json`. For production, provide values via environment variables:

| Variable | Description |
|----------|-------------|
| `ConnectionStrings__appdb` | PostgreSQL connection string |
| `ConnectionStrings__seq` | Seq ingestion URL |
| `Supabase__Url` | Supabase project URL |
