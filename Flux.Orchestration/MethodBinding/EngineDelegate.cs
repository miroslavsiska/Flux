using System;
using System.Collections.Generic;
using System.Text;

namespace Flux.Orchestration.MethodBinding;

/// <summary>
/// Represents an asynchronous operation that processes a target object within a given scene context.
/// </summary>
/// <param name="target">The object to be processed by the engine delegate. The specific type and meaning depend on the engine's
/// implementation.</param>
/// <param name="context">The scene context in which the operation is performed. Provides environmental information and services required for
/// processing.</param>
/// <param name="ct">A cancellation token that can be used to cancel the operation before it completes.</param>
/// <returns>A task that represents the asynchronous operation. The task completes when the processing is finished.</returns>
public delegate Task EngineDelegate(object target, SceneContext context, CancellationToken ct);
