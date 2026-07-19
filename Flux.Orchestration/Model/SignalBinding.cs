using Flux.Orchestration.Attributes;
using System.Diagnostics;

namespace Flux.Orchestration.Model;

/// <summary>
/// Associates a signal identifier with optional metadata such as a description.
/// </summary>
[DebuggerDisplay("Signal = {Signal}, {Description ?? \"Unnamed\"}")]
public sealed class SignalBinding
{
    /// <summary>
    /// The signal identifier (e.g. "UserForm.checkout").
    /// </summary>
    public required string Signal { get; init; }

    /// <summary>
    /// Optional description for tooling or dashboards.
    /// </summary>
    public string? Description { get; init; }
}
