using Flux.Orchestration.Model;

namespace Flux.Orchestration.Registry;

/// <summary>Creates orchestration metadata from types.</summary>
public interface IMetadataFactory
{
    /// <summary>Builds <see cref="SceneMetadata"/> from the given type's attributes.</summary>
    /// <param name="type">The annotated scene type.</param>
    /// <returns>The resulting <see cref="SceneMetadata"/>.</returns>
    SceneMetadata CreateFrom(Type type);
}
