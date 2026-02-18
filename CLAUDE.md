# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Project Overview

Wolverine Template is a .NET 10.0 starter template using:
- **Wolverine** - HTTP endpoints, messaging, command handling, and transactional outbox
- **EF Core + PostgreSQL** - Database ORM and persistence
- **Supabase** - JWT authentication with JWKS key rotation
- **Serilog + Seq** - Structured logging
- **docker-compose** - Local dev infrastructure (PostgreSQL, pgAdmin, Seq)

## Common Development Commands

### Running the Application

```bash
# Start dev infrastructure
docker compose up -d

# Run the API
cd src/Api
dotnet run
```

### Testing

```bash
dotnet test
```

### Database Migrations

```bash
cd src/Api
dotnet ef migrations add MigrationName
dotnet ef database update
```

## Architecture

### Project Structure

```
wolverine-template/
├── src/Api/                          # Main API project
│   ├── Auth/                         # Supabase claims transformation
│   ├── Data/
│   │   ├── AppDbContext.cs           # EF Core DbContext
│   │   └── DesignTimeDbContextFactory.cs
│   ├── Domain/
│   │   ├── BaseEntity.cs            # Base class with domain events
│   │   ├── Entities/                # DDD entities
│   │   └── Events/                  # Domain events
│   ├── Extensions/                  # ClaimsPrincipal helpers
│   ├── Features/                    # Feature-based organization
│   ├── Middleware/                  # Auto user sync
│   └── Services/                   # JWKS, email abstractions
└── tests/Api.Tests/                # Unit tests
```

### Wolverine Patterns

**HTTP Endpoints** - Use `[WolverineGet]`, `[WolverinePost]` attributes with static methods.

**Compound Handlers** - Use `LoadAsync()` / `Validate()` / `Handle()` pattern.

**Cascading Messages** - Return tuples: `(HttpResponse, DomainEvent)`.

**Transactional Outbox** - PostgreSQL stores both data and outbox messages.

**FluentValidation** - Define nested `Validator` class inside request records.

### Wolverine Validate Pattern (CRITICAL)
- Return type: `ProblemDetails` (non-nullable), NOT `ProblemDetails?`
- Happy path: Return `WolverineContinue.NoProblems`, NOT `null`
- Error path: Return `new ProblemDetails { ... }`

## Important Notes

### DateTime UTC Requirement
Always use `DateTime.UtcNow`, never `DateTime.Now`. PostgreSQL requires UTC timestamps.

### DDD Conventions
- Private constructors with static `Create()` factory methods
- Private setters on all entity properties
- Domain events raised in factory methods
- `entity.Ignore(e => e.DomainEvents)` in DbContext configuration

### Do Not
- Do not call `SaveChangesAsync()` manually - Wolverine handles transactions
- Do not create entities with `new Entity { }` - use factory methods
- Do not use `IMessageBus.PublishAsync()` - return cascading messages instead

## API Documentation

- Scalar UI: `/scalar/v1`
- OpenAPI spec: `/openapi/v1.json`
- Health checks: `/health` and `/alive`
- Hello endpoint: `/hello`
