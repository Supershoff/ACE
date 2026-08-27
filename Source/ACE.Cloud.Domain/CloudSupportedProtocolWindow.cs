namespace ACE.Cloud.Domain;

/// <summary>
/// A deployment's declared inclusive range of contract protocol versions it accepts from the other
/// side of a Cloud boundary transaction (OPS-002: "declare supported ACE releases" and "use
/// versioned forward migrations"). This is what makes forward/backward compatibility possible
/// during a rolling upgrade: the newer side may declare a window spanning both its own and the
/// previous release's protocol version, while <see cref="CloudCompatibilityChecker"/> still
/// requires the ACE extension and Cloud schema versions to match exactly.
/// </summary>
public sealed class CloudSupportedProtocolWindow
{
    public CloudProtocolVersion MinimumInclusive { get; }

    public CloudProtocolVersion MaximumInclusive { get; }

    public CloudSupportedProtocolWindow(CloudProtocolVersion minimumInclusive, CloudProtocolVersion maximumInclusive)
    {
        ArgumentNullException.ThrowIfNull(minimumInclusive);
        ArgumentNullException.ThrowIfNull(maximumInclusive);

        if (maximumInclusive < minimumInclusive)
        {
            throw new ArgumentException(
                $"A supported protocol window's maximum ({maximumInclusive}) cannot be lower than its minimum ({minimumInclusive}).",
                nameof(maximumInclusive));
        }

        MinimumInclusive = minimumInclusive;
        MaximumInclusive = maximumInclusive;
    }

    public bool Contains(CloudProtocolVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version >= MinimumInclusive && version <= MaximumInclusive;
    }

    public override string ToString() => $"[{MinimumInclusive}, {MaximumInclusive}]";
}
