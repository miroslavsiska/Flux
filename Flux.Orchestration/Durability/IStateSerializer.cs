namespace Flux.Orchestration.Durability;

/// <summary>
/// Serializes a scene context's parameter bag to/from a string for persistence.
/// </summary>
/// <remarks>
/// Because parameters are <c>object</c>, a serializer must preserve enough type information to round-trip
/// values back to their original CLR types. The default <see cref="JsonStateSerializer"/> does this via a
/// registry of known types (no arbitrary type loading), so durable scenes must use registered parameter types.
/// </remarks>
public interface IStateSerializer
{
    /// <summary>Serializes the parameter bag to a string payload.</summary>
    string Serialize(IReadOnlyDictionary<string, object> parameters);

    /// <summary>Deserializes a payload back into a typed parameter bag.</summary>
    Dictionary<string, object> Deserialize(string payload);
}
