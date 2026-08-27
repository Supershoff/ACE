using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// Serializes a <see cref="CloudItemId"/> as its bare numeric GUID on the wire instead of a
/// nested object, matching ACE's own native biota GUID representation.
/// </summary>
public sealed class CloudItemIdJsonConverter : JsonConverter<CloudItemId>
{
    public override CloudItemId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetUInt32());

    public override void Write(Utf8JsonWriter writer, CloudItemId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteNumberValue(value.Value);
    }
}
