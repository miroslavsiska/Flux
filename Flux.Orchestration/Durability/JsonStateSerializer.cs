using System.Text.Json;

namespace Flux.Orchestration.Durability;

/// <summary>
/// JSON state serializer that preserves CLR types via a discriminator registry. Each parameter is stored as
/// <c>{ "t": "&lt;discriminator&gt;", "v": &lt;json&gt; }</c>; on load the discriminator selects the exact type to
/// deserialize into. Only registered types are ever instantiated (safe — no arbitrary type loading).
/// </summary>
/// <remarks>
/// Built-in primitives (string, numeric, bool, Guid, DateTime/DateTimeOffset, decimal) are registered by
/// default. Register your own DTO types via the constructor; their discriminator is the full type name.
/// Serializing an unregistered type throws so the gap is caught early rather than silently dropping state.
/// </remarks>
public sealed class JsonStateSerializer : IStateSerializer
{
    private sealed record Envelope(string T, JsonElement V);

    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _byType = [];
    private readonly JsonSerializerOptions _options;

    public JsonStateSerializer(params Type[] knownTypes)
        : this((IEnumerable<Type>?)knownTypes, null) { }

    public JsonStateSerializer(IEnumerable<Type>? knownTypes, JsonSerializerOptions? options)
    {
        _options = options ?? new JsonSerializerOptions();

        Register("string", typeof(string));
        Register("int", typeof(int));
        Register("long", typeof(long));
        Register("double", typeof(double));
        Register("float", typeof(float));
        Register("decimal", typeof(decimal));
        Register("bool", typeof(bool));
        Register("guid", typeof(Guid));
        Register("datetime", typeof(DateTime));
        Register("datetimeoffset", typeof(DateTimeOffset));
        Register("timespan", typeof(TimeSpan));

        if (knownTypes is not null)
            foreach (var t in knownTypes)
                Register(t.FullName ?? t.Name, t);
    }

    private void Register(string discriminator, Type type)
    {
        _byName[discriminator] = type;
        _byType[type] = discriminator;
    }

    public string Serialize(IReadOnlyDictionary<string, object> parameters)
    {
        var map = new Dictionary<string, Envelope>(parameters.Count, StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            if (value is null) continue;
            var type = value.GetType();
            if (!_byType.TryGetValue(type, out var discriminator))
                throw new InvalidOperationException(
                    $"Parameter '{key}' has type '{type.FullName}', which is not registered for state serialization. " +
                    "Register it when constructing JsonStateSerializer.");
            var element = JsonSerializer.SerializeToElement(value, type, _options);
            map[key] = new Envelope(discriminator, element);
        }
        return JsonSerializer.Serialize(map, _options);
    }

    public Dictionary<string, object> Deserialize(string payload)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var map = JsonSerializer.Deserialize<Dictionary<string, Envelope>>(payload, _options);
        if (map is null) return result;

        foreach (var (key, envelope) in map)
        {
            if (!_byName.TryGetValue(envelope.T, out var type))
                throw new InvalidOperationException(
                    $"Parameter '{key}' has unknown state type discriminator '{envelope.T}'. " +
                    "Register the type when constructing JsonStateSerializer.");
            var value = envelope.V.Deserialize(type, _options);
            if (value is not null) result[key] = value;
        }
        return result;
    }
}
