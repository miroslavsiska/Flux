using Flux.Orchestration.MethodBinding;

namespace Flux.Orchestration.Model;

/// <summary>
/// A phase target (<see cref="ScenePhaseTarget"/>) paired with its resolved <see cref="MethodBindingInfo"/> — the
/// instance and method the engine invokes for a phase, plus how the method's signature was bound. Value-equal by
/// instance, method name, and binding.
/// </summary>
public class ScenePhaseTargetMetadata : IEquatable<ScenePhaseTargetMetadata>
{
    public ScenePhaseTarget Target { get; }
    public MethodBindingInfo MethodBindingInfo { get; }

    public ScenePhaseTargetMetadata(ScenePhaseTarget target, MethodBindingInfo methodBindingInfo)
    {
        Target = target;
        MethodBindingInfo = methodBindingInfo;
    }
    public object Instance => Target.Instance;
    public string? MethodName => Target.MethodName;
    public Type Type => Target.Type;

    public bool Equals(ScenePhaseTargetMetadata? other)
    {
        if (other is null) return false;
        return Target.Instance == other.Target.Instance &&
               Target.MethodName == other.Target.MethodName &&
               MethodBindingInfo == other.MethodBindingInfo;
    }

    public override bool Equals(object? obj) => obj is ScenePhaseTargetMetadata other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Target.Instance, Target.MethodName, MethodBindingInfo);
}