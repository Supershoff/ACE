using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// Serializes a <see cref="CloudShardId"/> as its bare string on the wire instead of a nested
/// object, so shard-scoped identifiers round-trip deterministically (issue #6 acceptance
/// criterion).
/// </summary>
public sealed class CloudShardIdJsonConverter : JsonConverter<CloudShardId>
{
    public override CloudShardId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, CloudShardId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Value);
    }
}
