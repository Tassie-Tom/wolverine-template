# Wolverine best practices for event-driven .NET applications

**Wolverine (WolverineFx) excels when you embrace its conventions: pure-function handlers, cascading messages as return values, and the transactional outbox — not when you fight them with abstractions.** This guide distills the official documentation, Jeremy Miller's blog posts, and the Wolverine GitHub repository into actionable patterns for building a resilient, event-driven system on .NET 10.

---

## Sagas should be stateful pure functions, not orchestration black boxes

A Wolverine saga is both the **state document and the handler class** — it inherits from `Wolverine.Saga`, stores its own state as properties, and exposes `Start()`, `Handle()`, and `NotFound()` methods that Wolverine discovers by convention. The saga modifies its own state in response to incoming messages and returns cascading messages that tell Wolverine what to do next.

```csharp
public record StartPaymentCycle(string CycleId);
public record CompletePaymentCycle(string Id);
public record PaymentCycleTimeout(string Id) : TimeoutMessage(7.Days());

public class PaymentCycle : Saga
{
    public string? Id { get; set; }
    public PaymentCycleStatus Status { get; set; }

    public static (PaymentCycle, PaymentCycleTimeout) Start(
        StartPaymentCycle cmd, ILogger<PaymentCycle> logger)
    {
        return (new PaymentCycle { Id = cmd.CycleId, Status = PaymentCycleStatus.Active },
                new PaymentCycleTimeout(cmd.CycleId));
    }

    public void Handle(CompletePaymentCycle cmd) => MarkCompleted();

    public void Handle(PaymentCycleTimeout timeout, ILogger<PaymentCycle> logger)
    {
        logger.LogWarning("Payment cycle {Id} timed out", Id);
        MarkCompleted();
    }

    // CRITICAL: handle timeouts arriving after saga completion
    public static void NotFound(PaymentCycleTimeout timeout) { }
}
```

**Identity resolution** follows a strict precedence: the `[SagaIdentity]` attribute on a message property, then a property named `{SagaTypeName}Id` (e.g., `PaymentCycleId`), then a property named `Id`. Between Wolverine applications, the saga ID travels in message headers automatically, but when starting a saga and emitting outgoing messages in the same transaction, you must embed the saga identity into outgoing message bodies manually.

For PostgreSQL persistence, Wolverine offers three storage options. **Lightweight saga storage** serializes state as JSON into an auto-created `{SagaClassName}_saga` table — fast setup, no ORM needed, with built-in optimistic concurrency. **EF Core storage** uses your `DbContext` mappings — best when you need flat tables or already use EF Core. **Marten storage** uses document semantics with native versioning. Precedence matters: if Marten integration is active, Marten wins; if EF Core has a mapping for the saga type, EF Core wins; otherwise lightweight storage applies.

**Common anti-patterns to avoid in sagas:**

- **Never call `IMessageBus.InvokeAsync()` within a saga handler** — you'll act on stale or missing data. Use cascading return values instead.
- **Always implement `NotFound()` for timeout messages** — without it, Wolverine throws `UnknownSagaException` when timeouts arrive after saga completion.
- **Never publish messages deep in the call stack** — return them from the root handler for visibility and testability.
- **Never wrap Wolverine in custom abstractions** — you lose functionality and gain nothing.
- **Don't catch exceptions in handlers** — use Wolverine's configurable error policies instead.

---

## Handlers work best as pure functions with cascading return values

Wolverine discovers handlers by convention: public classes ending in `Handler` or `Consumer` with public methods named `Handle`, `Consume`, `HandleAsync`, or `ConsumeAsync`. No marker interfaces or base classes required. The first "not simple" parameter is treated as the incoming message.

**Cascading messages** are the idiomatic way to trigger downstream work. They are published only after the handler succeeds, run in separate threads with independent retry loops, and enable pure-function handlers that are trivially unit-testable without mocks:

```csharp
// Single cascading message — just return it
public static PaymentProcessed Handle(ProcessPayment cmd, PaymentService svc)
    => new PaymentProcessed(cmd.PaymentId, DateTimeOffset.UtcNow);

// Multiple typed messages via tuple
public static (PaymentProcessed, NotifyMerchant) Handle(ProcessPayment cmd)
    => (new PaymentProcessed(cmd.PaymentId, DateTimeOffset.UtcNow),
        new NotifyMerchant(cmd.MerchantId));

// Variable messages with scheduling control via OutgoingMessages
public static OutgoingMessages Handle(ProcessPayment cmd)
{
    var messages = new OutgoingMessages { new PaymentProcessed(cmd.PaymentId, DateTimeOffset.UtcNow) };
    messages.Delay(new RetryPayment(cmd.PaymentId), 5.Minutes());
    return messages;
}
```

**When to choose each return pattern:** Use **tuples** when you know exact types at compile time and want self-documenting code — each element is handled independently, mixing side effects with cascading messages (e.g., `(Insert<Order>, OrderCreated)`). Use **`OutgoingMessages`** when you need fine-grained control over delivery (delays, scheduling, respond-to-sender) or the message count is variable. Use **`IEnumerable<object>`** with `yield return` for conditional cascading. Use a **single return** when there's exactly one downstream message.

**Side effects vs. cascading messages** is a critical distinction. Side effects implement `ISideEffect` and run **inline within the same transaction and retry loop**. Cascading messages run in a **separate transaction with independent retries**. Use side effects for actions that must be atomic with the handler (file writes, external API calls that must succeed together). Use cascading messages for everything else — they provide natural transaction boundaries and independent retry semantics.

**Storage side effects** turn persistence into pure functions:

```csharp
public static Insert<Payment> Handle(RecordPayment cmd)
    => Storage.Insert(new Payment { Id = cmd.PaymentId, Amount = cmd.Amount });
```

The **compound handler pattern** (Load/Validate/Handle) splits concerns using naming conventions. `Load` or `LoadAsync` fetches data, `Validate` checks preconditions, and `Handle` contains pure business logic. This "A-Frame Architecture" eliminates service layers and makes business logic independently testable:

```csharp
public static class ProcessPaymentHandler
{
    public static async Task<PaymentCycle> LoadAsync(
        ProcessPayment cmd, AppDbContext db, CancellationToken ct)
        => await db.PaymentCycles.FindAsync(cmd.CycleId, ct);

    public static ProblemDetails Validate(ProcessPayment cmd, PaymentCycle cycle)
        => cycle.Status == CycleStatus.Closed
            ? new ProblemDetails { Detail = "Cycle is closed", Status = 400 }
            : WolverineContinue.NoProblems;

    public static PaymentProcessed Handle(ProcessPayment cmd, PaymentCycle cycle)
        => new PaymentProcessed(cmd.PaymentId, cycle.Id);
}
```

---

## Domain events belong in entities but flow through Wolverine's outbox

Jeremy Miller describes four approaches to domain events, in order of preference. For an EF Core application, **Approach 2 is recommended**: entities collect events internally, and Wolverine's EF Core transactional middleware scrapes them from the `DbContext.ChangeTracker`.

```csharp
public abstract class Entity
{
    public List<object> Events { get; } = new();
    protected void Publish(object @event) => Events.Add(@event);
}

public class PaymentCycle : Entity
{
    public void Complete()
    {
        Status = CycleStatus.Completed;
        Publish(new PaymentCycleCompleted(Id, DateTimeOffset.UtcNow));
    }
}
```

Configuration in Wolverine 5.6+:

```csharp
opts.PublishDomainEventsFromEntityFrameworkCore<Entity>(x => x.Events);
```

This approach keeps **business logic inside entities** (DDD-aligned) while ensuring events flow through Wolverine's transactional outbox — they're persisted in the same database transaction as entity changes and delivered reliably. The alternative of returning events from entity methods as arrays and relaying them as cascading handler returns is Jeremy Miller's second-favorite approach, making it explicit that additional messages will be published.

**Avoid** the `IEventPublisher` dependency-injection approach (wiring an event publisher into entities via DI) — Jeremy explicitly recommends against it despite supporting it. Also avoid publishing messages deep in service layers via injected `IMessageBus`; this makes systems hard to reason about.

---

## Commands are imperative verbs, events are past-tense facts

Wolverine makes **no structural differentiation** between commands and events — both are plain messages. The distinction is purely a naming convention that communicates intent:

**Commands** use imperative mood: `ProcessPayment`, `CreateTicket`, `DebitAccount`, `ShipOrder`, `CommitToSprint`. **Events** use past tense: `PaymentProcessed`, `TicketCreated`, `AccountDebited`, `OrderShipped`.

The routing difference matters more than naming. **Commands use `SendAsync()` or `InvokeAsync()`** — `SendAsync` throws if no subscriber exists, and `InvokeAsync` executes the handler inline synchronously. **Events use `PublishAsync()`** — it silently succeeds even if no subscribers exist, making it ideal for fire-and-forget notifications. `ScheduleAsync()` works for both commands and events that need future delivery.

```csharp
// Command: must have exactly one handler
await bus.SendAsync(new ProcessPayment(paymentId, amount));

// Command: execute inline, get response
var result = await bus.InvokeAsync<PaymentResult>(new ProcessPayment(paymentId, amount));

// Event: zero or many subscribers
await bus.PublishAsync(new PaymentProcessed(paymentId, DateTimeOffset.UtcNow));
```

---

## Error policies replace try-catch blocks entirely

Wolverine's error handling philosophy is explicit: **never catch exceptions in message handlers**. Instead, configure declarative error policies that preserve observability and are independently testable. Policies apply in fall-through order, with per-handler rules taking precedence over global rules.

```csharp
builder.UseWolverine(opts =>
{
    // Transient database errors: exponential backoff
    opts.OnException<NpgsqlException>()
        .RetryWithCooldown(50.Milliseconds(), 100.Milliseconds(), 250.Milliseconds());

    // Stripe rate limiting: scheduled retry
    opts.Policies.OnException<StripeException>(
            ex => ex.StripeError?.Code == "rate_limit")
        .ScheduleRetry(5.Seconds());

    // Unrecoverable Stripe errors: dead letter immediately
    opts.Policies.OnException<StripeException>(
            ex => ex.StripeError?.Code == "card_declined")
        .MoveToErrorQueue();

    // Timeout: schedule retry with delay
    opts.Policies.OnException<TimeoutException>().ScheduleRetry(5.Seconds());

    // System down: requeue and pause the listener
    opts.Policies.OnException<SystemUnavailableException>()
        .Requeue().AndPauseProcessing(10.Minutes());
});
```

Per-handler policies use either the `Configure(HandlerChain)` static method or attributes:

```csharp
public static class ProcessStripePaymentHandler
{
    public static void Configure(HandlerChain chain)
    {
        chain.OnException<StripeException>()
            .RetryWithCooldown(100.Milliseconds(), 500.Milliseconds(), 2.Seconds());
    }

    public static PaymentProcessed Handle(ProcessStripePayment cmd, StripeClient stripe)
        => new PaymentProcessed(cmd.PaymentId, DateTimeOffset.UtcNow);
}
```

**Circuit breakers** apply per-endpoint and pause the entire listener when failure rates spike:

```csharp
opts.ListenToPostgresqlQueue("stripe-payments")
    .CircuitBreaker(cb =>
    {
        cb.MinimumThreshold = 10;
        cb.PauseTime = 1.Minutes();
        cb.TrackingPeriod = 5.Minutes();
        cb.FailurePercentageThreshold = 20;
        cb.Include<StripeException>();
    });
```

**Dead letter management** stores failed messages in `wolverine_dead_letters` with full exception details. Wolverine provides REST endpoints for monitoring and replay via `app.MapDeadLettersEndpoints()`. Dead letters expire after **10 days** by default (configurable). To replay, either mark messages as `replayable = true` in the database or use the REST API.

**Important caveat:** when using `InvokeAsync()` (inline execution), only `Retry` and `RetryWithCooldown` policies apply. Other actions like `Requeue` and `MoveToErrorQueue` require asynchronous message handling.

---

## EF Core integration demands specific configuration for reliability

The EF Core integration requires two independent configurations: **message persistence** (where Wolverine stores inbox/outbox envelopes) and **transactional middleware** (how Wolverine wraps handler execution in database transactions).

```csharp
builder.UseWolverine(opts =>
{
    // Message storage: inbox, outbox, dead letters, scheduled messages
    opts.PersistMessagesWithPostgresql(connectionString);

    // EF Core: one-step registration (preferred)
    // Sets DbContextOptions to Singleton, activates transactional middleware,
    // maps envelope storage in DbContext for command batching
    opts.Services.AddDbContextWithWolverineIntegration<AppDbContext>(
        x => x.UseNpgsql(connectionString));

    // Auto-wrap any handler with a DbContext dependency in transactions
    opts.Policies.AutoApplyTransactions();

    // Make all local queues durable (inbox/outbox)
    opts.Policies.UseDurableLocalQueues();
});

builder.Host.UseResourceSetupOnStartup(); // Auto-create schema
```

**`AutoApplyTransactions()`** detects any handler that depends on a `DbContext` — directly or through injected services — and automatically wraps it in transactional middleware. Wolverine calls `SaveChangesAsync()` after the handler completes and flushes outbox messages in the same transaction. Use the `[Transactional]` attribute instead if you want explicit, per-handler control.

**Critical pitfalls to avoid with EF Core:**

- **Register `DbContextOptions` as Singleton** — this is described as "weirdly important" for performance. `AddDbContextWithWolverineIntegration` does this automatically; if registering manually, pass `optionsLifetime: ServiceLifetime.Singleton`.
- **Only one inbox/outbox per database** — Wolverine currently cannot use the transactional outbox with multiple databases simultaneously (planned for future versions).
- **Avoid lambda IoC registrations** — `AddScoped<T>(s => ...)` with opaque lambdas forces Wolverine to fall back to service locator, potentially resolving wrong `IMessageBus` instances.
- **Map envelope storage in your `DbContext`** — call `modelBuilder.MapWolverineEnvelopeStorage()` in `OnModelCreating` for better command batching. `AddDbContextWithWolverineIntegration` does this automatically.
- **EF Core doesn't support upsert** — unlike Marten, `Storage.Store()` maps to `Update`, not upsert. Use `Insert` or `Update` explicitly.

For **using the outbox outside Wolverine handlers** (e.g., in a background service that processes webhooks):

```csharp
public async Task ProcessWebhook(
    Event webhookEvent,
    IDbContextOutbox<AppDbContext> outbox)
{
    var payment = MapToPayment(webhookEvent);
    outbox.DbContext.Payments.Add(payment);
    await outbox.PublishAsync(new PaymentReceived(payment.Id));
    await outbox.SaveChangesAndFlushMessagesAsync(); // Atomic commit
}
```

---

## HTTP endpoints follow Load/Validate/Handle with typed returns

Wolverine.Http replaces controllers with convention-based endpoint classes. The compound handler pattern splits HTTP concerns into `Load` (data fetching), `Validate` (precondition checking with `ProblemDetails`), and `Handle`/`Post`/`Put` (pure business logic). Methods are discovered by naming convention on the same class.

```csharp
public static class CreateTicketEndpoint
{
    public static ProblemDetails Validate(CreateTicket cmd)
    {
        if (string.IsNullOrEmpty(cmd.EventName))
            return new ProblemDetails { Detail = "Event name required", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/tickets")]
    public static (TicketCreationResponse, TicketCreated) Post(
        CreateTicket cmd, AppDbContext db)
    {
        var ticket = new Ticket { EventName = cmd.EventName };
        db.Tickets.Add(ticket);
        return (
            new TicketCreationResponse(ticket.Id), // HTTP 201 response
            new TicketCreated(ticket.Id)             // Cascading message
        );
    }
}

public record TicketCreationResponse(int Id)
    : CreationResponse("/tickets/" + Id); // Auto-sets 201 + Location header
```

**Return type semantics for HTTP:** The **first tuple element** is always the HTTP response body. Returning `CreationResponse` yields **201** with a `Location` header. Returning `AcceptResponse` yields **202**. Returning a nullable type auto-generates **404** when null. Returning `string` writes `text/plain`. Using `[EmptyResponse]` returns **204** and treats all return values as side effects or cascading messages.

**Three ways to cascade messages from HTTP endpoints:** typed tuple returns (compile-time safety), `OutgoingMessages` in a tuple (fine-grained delivery control), or direct `IMessageBus` injection (imperative style). Prefer tuple returns for clarity and testability.

**Parameter resolution** follows a specific precedence: route parameters by name match, `[FromHeader]` for headers, simple types from query strings, the first complex parameter as JSON body, and everything else from IoC. Endpoint classes should use the `Endpoint` suffix to avoid being mistaken for message handlers.

---

## Scheduled messages enable self-healing workflows

Wolverine supports scheduling through `ScheduleAsync()` on `IMessageBus`, `DelayedFor()` / `ScheduledAt()` extension methods on cascading messages, the `OutgoingMessages.Schedule()` method, and the `TimeoutMessage` base class for saga timeouts. With PostgreSQL persistence configured, scheduled messages are stored durably in `wolverine_outgoing_envelopes` and survive process restarts.

For a **weekly workflow with retry**, use the self-scheduling pattern:

```csharp
public record ProcessWeeklyPayments(Guid CycleId);
public record WeeklyPaymentsProcessed(Guid CycleId, int Count);

public static class ProcessWeeklyPaymentsHandler
{
    public static async Task<OutgoingMessages> Handle(
        ProcessWeeklyPayments cmd,
        AppDbContext db,
        CancellationToken ct)
    {
        var pendingPayments = await db.Payments
            .Where(p => p.CycleId == cmd.CycleId && p.Status == PaymentStatus.Pending)
            .ToListAsync(ct);

        foreach (var payment in pendingPayments)
            await ProcessPaymentAsync(payment);

        var messages = new OutgoingMessages
        {
            new WeeklyPaymentsProcessed(cmd.CycleId, pendingPayments.Count)
        };

        // Schedule next Friday at 9 AM UTC
        var nextFriday = GetNextFriday(DateTimeOffset.UtcNow);
        messages.Schedule(new ProcessWeeklyPayments(cmd.CycleId), nextFriday);

        return messages;
    }

    private static DateTimeOffset GetNextFriday(DateTimeOffset from)
    {
        int daysUntilFriday = ((int)DayOfWeek.Friday - (int)from.DayOfWeek + 7) % 7;
        if (daysUntilFriday == 0) daysUntilFriday = 7;
        return from.Date.AddDays(daysUntilFriday).AddHours(9);
    }
}
```

**Important caveat from the docs:** the built-in outbox scheduling is designed for **low-to-moderate volumes** of scheduled messages — it was primarily meant for scheduled retries. For high-volume scheduling, use Wolverine's PostgreSQL transport queues (`UsePostgresqlPersistenceAndTransport`), which have separate optimized tables for scheduled messages.

---

## Conclusion

Wolverine's design philosophy centers on **making the right thing easy**: pure-function handlers eliminate mocks in tests, cascading return values make message flow visible, the transactional outbox guarantees consistency without distributed transactions, and declarative error policies replace brittle try-catch blocks. The anti-patterns matter as much as the patterns — never abstract Wolverine behind interfaces, never call `InvokeAsync` inside sagas, and never bury `PublishAsync` calls deep in service layers. Let Wolverine's conventions do the heavy lifting.
