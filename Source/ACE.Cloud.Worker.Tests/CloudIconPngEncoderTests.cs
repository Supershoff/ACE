using System.Buffers.Binary;
using System.IO.Compression;
using ACE.Cloud.Domain;
using ACE.Cloud.Worker;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// UI-006 Red/Green coverage for the "web-ready derivative" PNG encoder: a from-scratch chunk/CRC/
/// zlib decoder (independent of <see cref="CloudIconPngEncoder"/>'s own code) that proves the emitted
/// bytes are a structurally valid, pixel-exact PNG, plus determinism across repeated encodes.
/// </summary>
[TestClass]
public sealed class CloudIconPngEncoderTests
{
    [TestMethod]
    public void Encode_ARaster_ProducesAStructurallyValidPixelExactPng()
    {
        var raster = BuildTestRaster(width: 5, height: 3);

        var png = CloudIconPngEncoder.Encode(raster);
        var decodedRgba = DecodePngRgba(png, out var width, out var height);

        Assert.AreEqual(5, width);
        Assert.AreEqual(3, height);
        CollectionAssert.AreEqual(raster.Rgba, decodedRgba);
    }

    [TestMethod]
    public void Encode_CalledTwiceForTheSameRaster_ProducesBitwiseIdenticalBytes()
    {
        var raster = BuildTestRaster(width: 4, height: 4);

        var first = CloudIconPngEncoder.Encode(raster);
        var second = CloudIconPngEncoder.Encode(raster);

        CollectionAssert.AreEqual(first, second);
    }

    private static CloudIconRasterLayer BuildTestRaster(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            rgba[(pixel * 4) + 0] = (byte)(pixel * 7 + 1);
            rgba[(pixel * 4) + 1] = (byte)(pixel * 7 + 2);
            rgba[(pixel * 4) + 2] = (byte)(pixel * 7 + 3);
            rgba[(pixel * 4) + 3] = (byte)(255 - pixel);
        }

        return new CloudIconRasterLayer(width, height, rgba);
    }

    /// <summary>
    /// A minimal from-scratch PNG reader supporting exactly the subset <see cref="CloudIconPngEncoder"/>
    /// emits (8-bit RGBA, filter type None, single IDAT), verifying chunk CRCs along the way.
    /// </summary>
    private static byte[] DecodePngRgba(byte[] png, out int width, out int height)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        CollectionAssert.AreEqual(signature.ToArray(), png[..8]);

        var offset = 8;
        width = 0;
        height = 0;
        byte[]? idat = null;

        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var chunkData = png.AsSpan(offset + 8, length).ToArray();
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4));

            var crcInput = png.AsSpan(offset + 4, 4 + length).ToArray();
            Assert.AreEqual(storedCrc, Crc32(crcInput), $"CRC mismatch for chunk {type}.");

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(chunkData.AsSpan(0, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(chunkData.AsSpan(4, 4));
                    Assert.AreEqual(8, chunkData[8], "Expected 8-bit depth.");
                    Assert.AreEqual(6, chunkData[9], "Expected truecolor-with-alpha color type.");
                    break;
                case "IDAT":
                    idat = chunkData;
                    break;
                case "IEND":
                    Assert.AreEqual(0, length);
                    break;
            }

            offset += 12 + length;
        }

        Assert.IsNotNull(idat, "Expected exactly one IDAT chunk.");

        using var compressed = new MemoryStream(idat!);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var rawBytes = raw.ToArray();

        var stride = width * 4;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (stride + 1);
            Assert.AreEqual(0, rawBytes[rowStart], "Expected filter type None on every scanline.");
            Array.Copy(rawBytes, rowStart + 1, rgba, y * stride, stride);
        }

        return rgba;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
