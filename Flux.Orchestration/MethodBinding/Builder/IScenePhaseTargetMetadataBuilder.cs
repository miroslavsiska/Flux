using Flux.Orchestration.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flux.Orchestration.MethodBinding.Builder;

/// <summary>
/// Builds <see cref="ScenePhaseTargetMetadata"/> from a target (an instance and optional method name) by resolving how
/// its method binds to a phase, honouring the given <see cref="MethodResolutionMode"/>.
/// </summary>
public interface IScenePhaseTargetMetadataBuilder
{
    ScenePhaseTargetMetadata Build(object? instance, string? methodName, MethodResolutionMode? mode = null);

    ScenePhaseTargetMetadata Build(ScenePhaseTarget target, MethodResolutionMode? mode = null);

    bool TryBuild(object instance, string methodName, out ScenePhaseTargetMetadata? metadata);
}
