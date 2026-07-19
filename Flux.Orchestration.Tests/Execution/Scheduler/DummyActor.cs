using System;
using System.Collections.Generic;
using System.Text;

namespace Flux.Orchestration.Tests.Execution.Scheduler;

/// <summary>
/// The DummyActor object is a simple class used for testing purposes in the context of orchestration execution and scheduling.
/// </summary>
public class DummyActor
{
    /// <summary>
    /// Performs update logic for the current object or component. Intended to be called during an update cycle to
    /// process state changes or perform periodic actions.
    /// </summary>
    public void OnUpdate()
    {
        // NOOP
    }

    /// <summary>
    /// Represents an asynchronous operation that completes immediately without performing any action.
    /// </summary>
    /// <returns>A completed ValueTask representing the finished asynchronous operation.</returns>
    public ValueTask OnUpdateValueTaskAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Represents an asynchronous operation that completes immediately without performing any action.
    /// </summary>
    /// <returns>A completed task that represents the asynchronous operation.</returns>
    public Task OnUpdateTaskAsync()
    {
        return Task.CompletedTask;
    }
}
