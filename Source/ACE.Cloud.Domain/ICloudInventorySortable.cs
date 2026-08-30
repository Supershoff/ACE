namespace ACE.Cloud.Domain;

/// <summary>The fields <see cref="CloudInventoryItemOrderPolicy"/> needs from any Mule Page row type.</summary>
public interface ICloudInventorySortable
{
    CloudItemId ItemId { get; }

    string Name { get; }

    int? Value { get; }

    int? Burden { get; }
}
