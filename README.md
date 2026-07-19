# Flux

A high-performance, declarative orchestration runtime for **.NET 10** and **C#**, designed to model and execute complex workflows via clean, type-safe configurations.

## Features

- **Declarative API** – compose workflows with a fluent builder; no runtime reflection, no YAML
- **Type-safe context** – a single generic `TContext` object flows through every step, giving the compiler full visibility of your data
- **Sequential execution** – steps run in order; the first failure short-circuits the workflow
- **Parallel groups** – run independent steps concurrently with `AddParallelSteps`
- **Conditional branching** – route execution at runtime with `AddConditionalStep`
- **Automatic retries** – wrap any step with `AddRetryStep` for exponential-back-off retries
- **Structured logging** – integrates with `Microsoft.Extensions.Logging`
- **Dependency-injection ready** – a single `AddFlux()` call registers the engine

## Getting Started

### Installation

The library targets `net10.0`. Reference the `Flux.Core` project (NuGet package forthcoming).

### Minimal example

```csharp
using Flux.Core.Builder;
using Flux.Core.Engine;
using Flux.Core.Models;

// 1. Define your shared context
public sealed class OrderContext
{
    public int OrderId { get; set; }
    public bool IsValidated { get; set; }
    public bool IsShipped { get; set; }
}

// 2. Build the workflow
var workflow = new WorkflowBuilder<OrderContext>("process-order")
    .AddStep("validate", ctx =>
    {
        ctx.IsValidated = ctx.OrderId > 0;
        return ctx.IsValidated
            ? StepResult.Success()
            : StepResult.Failure("Invalid order ID.");
    })
    .AddStep("ship", ctx =>
    {
        ctx.IsShipped = true;
        return StepResult.Success();
    })
    .Build();

// 3. Execute
var engine = new WorkflowEngine();
var ctx    = new OrderContext { OrderId = 42 };
var result = await engine.ExecuteAsync(workflow, ctx);

Console.WriteLine(result.IsSuccess);   // True
Console.WriteLine(ctx.IsShipped);      // True
```

### Parallel steps

```csharp
var workflow = new WorkflowBuilder<ReportContext>("generate-report")
    .AddParallelSteps("fetch-data",
        new FetchSalesStep(),
        new FetchInventoryStep(),
        new FetchCustomersStep())
    .AddStep("render", ctx => { /* combine results */ return StepResult.Success(); })
    .Build();
```

### Conditional step

```csharp
var workflow = new WorkflowBuilder<PaymentContext>("payment")
    .AddConditionalStep(
        name:     "choose-gateway",
        condition: ctx => ctx.Amount > 1000,
        thenStep:  new PremiumGatewayStep(),
        elseStep:  new StandardGatewayStep())
    .Build();
```

### Retry with exponential back-off

```csharp
var workflow = new WorkflowBuilder<SyncContext>("data-sync")
    .AddRetryStep(
        step:         new CallExternalApiStep(),
        maxAttempts:  5,
        initialDelay: TimeSpan.FromMilliseconds(200))
    .Build();
```

### Dependency Injection

```csharp
// Program.cs / Startup.cs
services.AddFlux();

// Inject and use
public class OrderService(IWorkflowEngine engine)
{
    public Task<WorkflowResult> ProcessAsync(int orderId, CancellationToken ct)
    {
        var wf  = BuildWorkflow();
        var ctx = new OrderContext { OrderId = orderId };
        return engine.ExecuteAsync(wf, ctx, ct);
    }
}
```

## Architecture

```
Flux.Core/
├── Abstractions/
│   ├── IStep.cs              – Unit of work
│   ├── IWorkflow.cs          – Ordered collection of steps
│   └── IWorkflowEngine.cs    – Executes a workflow
├── Models/
│   ├── StepResult.cs         – Outcome of a single step
│   └── WorkflowResult.cs     – Consolidated outcome of a workflow
├── Builder/
│   ├── WorkflowBuilder.cs    – Fluent builder (public API)
│   └── Internal/
│       ├── ActionStep.cs     – Delegate-backed step
│       ├── ParallelStep.cs   – Concurrent step group
│       ├── ConditionalStep.cs– Branch step
│       ├── RetryStep.cs      – Exponential-back-off wrapper
│       └── Workflow.cs       – Immutable workflow record
├── Engine/
│   └── WorkflowEngine.cs     – Default IWorkflowEngine implementation
└── Extensions/
    └── ServiceCollectionExtensions.cs  – AddFlux() DI helper
```

## License

MIT – see [LICENSE](LICENSE).