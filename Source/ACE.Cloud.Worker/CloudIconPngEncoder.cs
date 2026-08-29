using System.Buffers.Binary;
using System.IO.Compression;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Worker;

/// <summary>
/// Encodes a composed <see cref="CloudIconRasterLayer"/> into a "web-ready derivative" PNG (UI-006:
/// "Return web-ready derivatives through content-addressed caching"). Deliberately hand-rolled with
/// only <c>System.IO.Compression.ZLibStream</c> (pure managed, no native codec) rather than
/// <c>System.Drawing.Common</c>/GDI+: the rest of this Cloud Mule pipeline never depends on a
/// platform-specific imaging stack, and this compositor's own CI matrix builds and tests on both
/// Linux and Windows runners. Encoding is a pure function of the raster's bytes -- same pixels always
/// produce the same PNG bytes -- satisfying "Cache hits are bitwise stable for identical complete
/// composition keys".
/// </summary>
public static class CloudIconPngEncoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(CloudIconRasterLayer raster)
    {
        ArgumentNullException.ThrowIfNull(raster);

        using var output = new MemoryStream();
        output.Write(Signature);

        WriteChunk(output, "IHDR", BuildIhdr(raster.Width, raster.Height));
        WriteChunk(output, "IDAT", DeflateScanlines(raster));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static byte[] BuildIhdr(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // color type: truecolor with alpha
        ihdr[10] = 0; // compression method
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace method
        return ihdr;
    }

    private static byte[] DeflateScanlines(CloudIconRasterLayer raster)
    {
        var stride = raster.Width * 4;
        using var raw = new MemoryStream((stride + 1) * raster.Height);
        for (var y = 0; y < raster.Height; y++)
        {
            raw.WriteByte(0); // filter type 0: None
            raw.Write(raster.Rgba, y * stride, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, data.Length);
        output.Write(lengthBytes);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        crc = Crc32Update(crc, type);
        crc = Crc32Update(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Crc32Update(uint crc, byte[] bytes)
    {
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc;
    }
}
