using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// Serializes a <see cref="CloudGuidId{TSelf}"/> as its bare Guid scalar on the wire instead of a
/// nested object, so shard-scoped identifiers round-trip deterministically (issue #6 acceptance
/// criterion) as plain values other services and languages can decode without special-casing this
/// project's wrapper types.
/// </summary>
public sealed class CloudGuidIdJsonConverter<TSelf> : JsonConverter<TSelf>
    where TSelf : CloudGuidId<TSelf>
{
    public override TSelf Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetGuid();
        return (TSelf)Activator.CreateInstance(typeToConvert, value)!;
    }

    public override void Write(Utf8JsonWriter writer, TSelf value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Value);
    }
}
