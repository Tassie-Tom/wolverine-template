# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Project Overview

Wolverine Template is a full-stack starter template using:

**Backend (.NET 10.0)**
- **Wolverine** - HTTP endpoints, messaging, command handling, and transactional outbox
- **EF Core + PostgreSQL** - Database ORM and persistence
- **Supabase** - JWT authentication with JWKS key rotation
- **Serilog + Seq** - Structured logging

**Frontend (Angular)**
- **Angular** with standalone components and SSR
- **Tailwind CSS** for all styling
- **TypeScript** with strict mode
- **Vitest** for unit testing

**Infrastructure**
- **docker-compose** - Local dev infrastructure (PostgreSQL, pgAdmin, Seq)

## Common Development Commands

### Running the Application

```bash
# Start dev infrastructure
docker compose up -d

# Run the API
cd src/Api
dotnet run

# Run the frontend (separate terminal)
cd frontend
npm start
# Navigate to http://localhost:4200/
```

### Testing

```bash
# Backend tests
dotnet test

# Frontend tests
cd frontend
npm test
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
├── src/Api/                          # .NET API project
│   ├── Auth/                         # Supabase claims transformation
│   ├── Data/                         # EF Core DbContext + migrations
│   ├── Domain/                       # DDD entities + events
│   ├── Extensions/                   # Service + ClaimsPrincipal helpers
│   ├── Features/                     # Feature-based endpoints
│   ├── Middleware/                    # Auto user sync
│   └── Services/                     # JWKS, email abstractions
├── frontend/                         # Angular frontend with SSR
│   ├── src/
│   │   ├── app/                      # Components, services, routes
│   │   ├── environments/             # API URL configuration
│   │   ├── server.ts                 # Express SSR server
│   │   └── styles.scss               # Tailwind imports
│   ├── angular.json                  # Angular CLI configuration
│   └── Angular.md                    # Angular best practices guide
└── tests/Api.Tests/                  # Backend unit tests
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

## Frontend

### Environment Configuration

API URLs are configured in `frontend/src/environments/`:
- `environment.ts` — Development: `http://localhost:5000`
- `environment.production.ts` — Production: set your API URL

### Angular Conventions

- **Standalone components** with `OnPush` change detection
- **Signals** for all state — no lifecycle hooks, no manual subscriptions
- **Tailwind CSS only** — no custom CSS, `styles: []` on all components
- **Modern control flow** — `@if`, `@for`, `@switch`, `@let`
- **`input()`, `output()`, `model()`** functions, not decorators
- **Lazy-loaded routes** for code splitting

See `frontend/Angular.md` for the complete best practices guide.

## Reference Files

- `Wolverine.md` - Comprehensive Wolverine patterns and best practices (sagas, handlers, domain events, error policies, EF Core integration, HTTP endpoints, scheduling)
- `frontend/Angular.md` - Angular best practices (signals, components, SSR, Tailwind, testing)
