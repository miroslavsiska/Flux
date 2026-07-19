using System.Reflection;

namespace Flux.Orchestration.Model;

public class ScenePhaseTarget : IEquatable<ScenePhaseTarget>
{
    public ScenePhaseTarget(object instance, string? methodName)
    {
        Instance = instance;
        MethodName = methodName;
    }

    /// <summary>
    /// The target object to be orchestrated during this phase.
    /// </summary>
    public object Instance { get; }

    /// <summary>
    /// Gets the runtime type of the target object.
    /// </summary>
    public Type Type => _type ??= Instance.GetType();
    private Type? _type;

    /// <summary>
    /// The target object method that will be invoked during this phase.
    /// </summary>
    public string? MethodName { get; }

    public bool Equals(ScenePhaseTarget? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(Instance, other.Instance) &&
               string.Equals(MethodName, other.MethodName, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is ScenePhaseTarget other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Instance, MethodName);
    }
}
