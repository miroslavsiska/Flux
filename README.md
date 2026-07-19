# Flux 🎬

> **A deterministic, attribute-driven orchestration engine for .NET.**
> Declare work as *scenes* of *phases*; Flux plans them into a dependency DAG and runs them — in parallel where
> safe, in order where it matters — with resilience, durability, and replay built in.

Flux turns imperative "call this, then that" glue into **declarative orchestration**. You annotate classes and methods
with `[Scene]`, `[ScenePhase]`, and `[SceneMethod]`; Flux discovers them, builds an execution plan, and runs it.
Ordering is **data-driven** — a phase declares what it `DependsOn`, `Reads`, and `Writes`, and the engine derives a
correct, maximally-parallel DAG for you. No manual `Task.WhenAll` choreography, no hand-wired ordering.

Built on **.NET 10**, distributed as the [`Flux.Orchestration`](https://www.nuget.org/) NuGet package.

---

## Core concepts

| Concept | What it is |
|---|---|
| **Scene** | A unit of orchestration — a class marked `[Scene(Id = "…")]`, run to completion via the agent. |
| **Phase** | A step within a scene — `[ScenePhase(SceneId, PhaseId, …)]`. Phases are the nodes of the DAG. |
| **Target** (`[SceneMethod]`) | The actual work — a method bound to a phase with `[SceneMethod(SceneId, PhaseId)]`. Flexible method binding resolves the signature. |
| **`SceneContext`** | Per-run state: a `CorrelationId`, a thread-safe `Parameters` bag, and a typed, versioned, tear-free `Resources` store for race-free inter-phase data. |
| **Agent** (`IAgent`) | The façade — register components, execute scenes and signals. |
| **Planner · Engine · Scheduler** | The planner builds and walks the DAG (recursion-safe, `dryRun`-capable); the engine invokes the bound delegates (`sync` / `Task` / `ValueTask`); the scheduler orders execution levels. |

## The DAG model

A phase declares its edges; Flux computes the order:

- **`DependsOn`** — explicit ordering: the phase starts only after every listed phase completes.
- **`Reads` / `Writes`** — named resources. Flux inserts a **write-before-read** edge automatically, and orders
  concurrent writers of the same resource deterministically by `(Priority, PhaseId)`.

Phases with no ordering relationship run in **parallel**; everything else is serialized exactly as the data demands.
Because the order falls out of declared reads/writes, a correct plan is computed rather than hand-maintained. (The
older `Parallel` flag is deprecated in favour of this model.)

## Quick start

```bash
dotnet add package Flux.Orchestration
```

```csharp
using Flux.Orchestration;              // IAgent, SceneContext
using Flux.Orchestration.Attributes;   // [Scene], [ScenePhase], [SceneMethod]
using Microsoft.Extensions.DependencyInjection;

[Scene(Id = "build")]
[ScenePhase(SceneId = "build", PhaseId = "restore")]
[ScenePhase(SceneId = "build", PhaseId = "compile", DependsOn = new[] { "restore" })]
[ScenePhase(SceneId = "build", PhaseId = "test",    DependsOn = new[] { "compile" })]
public sealed class BuildScene
{
    [SceneMethod(SceneId = "build", PhaseId = "restore")]
    public async Task Restore(SceneContext ctx, CancellationToken ct) { /* … */ }

    [SceneMethod(SceneId = "build", PhaseId = "compile")]
    public async Task Compile(SceneContext ctx, CancellationToken ct) { /* … */ }

    [SceneMethod(SceneId = "build", PhaseId = "test")]
    public async Task Test(SceneContext ctx, CancellationToken ct) { /* … */ }
}

var services = new ServiceCollection();
services.AddFluxOrchestration();
var provider = services.BuildServiceProvider();

var agent = provider.GetRequiredService<IAgent>();
agent.RegisterComponent(new BuildScene());   // discovers [Scene] / [ScenePhase] / [SceneMethod]
await agent.ExecuteSceneAsync("build", new SceneContext());
```

`ExecuteSceneAsync` runs the whole scene to completion via a **recursion-safe direct walk** of the plan. The planner
also supports a side-effect-free **`dryRun`** — compute the plan and see what *would* run without invoking anything
(handy for lookahead and "what-if" foresight).

Method signatures are resolved flexibly (`MethodResolutionMode.Flexible` by default), so a `[SceneMethod]` can take
the `SceneContext` and/or a `CancellationToken` and return `void`, `Task`, or `ValueTask`.

## What's in the box

- **Execution** — the DAG **planner**, an execution **engine** (sync / `Task` / `ValueTask` delegate invocation), a
  **scheduler** for execution levels, and per-phase **resilience** (`Timeout`, `MaxRetries`).
- **Durability** — an orchestration **journal**, a **scene-state store**, a JSON state serializer, and an
  **`OrchestrationReplayer`**, so a scene's progress can be persisted and resumed or replayed. In-memory, file-based,
  and null implementations ship out of the box.
- **Resources** — the typed, versioned `IResourceStore` for race-free inter-phase data (reads never tear; ordering is
  enforced by the DAG via `Reads`/`Writes`).
- **Diagnostics** — orchestration diagnostics hooks for tracing what ran and when.
- **Signals** — scenes can declare `Triggers` and be run by name via `IAgent.ExecuteSignalAsync`.
- **Animation** — frame-clock-driven animation drivers (tween, spring) with snapshot buffering, for time-based /
  animated orchestration.
- **DI** — `services.AddFluxOrchestration()` wires the agent, planner, engine, registries, and defaults.

## Project layout

| Project | What it is |
|---|---|
| `Flux.Orchestration` | the engine — attributes, model, execution (engine · planner · scheduler · resilience), durability, diagnostics, resources, animation, DI |
| `Flux.Orchestration.Tests` | the test suite |

## Status

- **.NET 10**; package version **1.0.1**.
- Used as the orchestration kernel by **SpareAi**, where recursive plan cycles run as Flux scenes (each node a
  `plan.node` scene executed via the recursion-safe `ExecuteSceneAsync`).
