namespace ACE.Cloud.Domain;

/// <summary>
/// One already-decoded layer, ready to composite: straight (non-premultiplied) RGBA, row-major, 4
/// bytes per pixel (UI-005). The constructor is the single choke point that keeps an oversized or
/// structurally inconsistent buffer from ever reaching the compositor -- callers that decode raw DAT
/// bytes must classify a too-large or inconsistent result as
/// <see cref="CloudIconLayerResolutionOutcomeKind.Oversized"/>/<see cref="CloudIconLayerResolutionOutcomeKind.Malicious"/>
/// themselves *before* calling this constructor, since after this point the buffer is trusted.
/// </summary>
public sealed record CloudIconRasterLayer
{
    /// <summary>
    /// No legitimate AC icon layer is anywhere near this large; a resolver must reject a decoded
    /// buffer above this size as Oversized before ever constructing this type.
    /// </summary>
    public const int MaxDimension = 512;

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }

    public CloudIconRasterLayer(int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        if (width is <= 0 or > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height is <= 0 or > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var expectedLength = checked(width * height * 4);
        if (rgba.Length != expectedLength)
        {
            throw new ArgumentException(
                $"A {width}x{height} RGBA raster requires exactly {expectedLength} bytes, not {rgba.Length}.", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba;
    }
}
