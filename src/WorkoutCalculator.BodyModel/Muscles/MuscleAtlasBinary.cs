using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.BodyModel.Muscles;

/// <summary>Little endian MMA1: header (116 bytes), region IDs, UNORM8 weights. See docs/STRENGTH.md.</summary>
public static class MuscleAtlasBinary
{
    public const string FileName = "makehuman-muscle-atlas-v1.bin";
    public const int FormatVersion = 1;
    public const int HeaderBytes = 116;
    private static byte[] RegionHash() => SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
        MuscleDefinitions.Regions.Select(r => $"{r.Index}:{r.Id}:{r.GroupId}:{r.Side}"))));

    public static byte[] Write(MuscleAtlas atlas, ReadOnlySpan<byte> sourceHash)
    {
        if (sourceHash.Length != 32) throw new ArgumentException("Expected SHA-256 source hash.");
        var bytes = new byte[HeaderBytes + atlas.VertexCount * MuscleAtlas.Influences * 2];
        "MMA1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), MuscleAtlas.Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), atlas.VertexCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), MuscleAtlas.Influences);
        sourceHash.CopyTo(bytes.AsSpan(20, 32));
        RegionHash().CopyTo(bytes, 52);
        atlas.RegionIndices.CopyTo(bytes, HeaderBytes);
        atlas.Weights.CopyTo(bytes, HeaderBytes + atlas.RegionIndices.Length);
        SHA256.HashData(bytes.AsSpan(HeaderBytes)).CopyTo(bytes, 84);
        return bytes;
    }

    public static MuscleAtlas Read(ReadOnlySpan<byte> bytes, int expectedVertices, ReadOnlySpan<byte> sourceHash)
    {
        if (bytes.Length < HeaderBytes || !bytes[..4].SequenceEqual("MMA1"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]) != FormatVersion ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != MuscleAtlas.Version ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]) != expectedVertices ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]) != MuscleAtlas.Influences ||
            expectedVertices <= 0 || bytes.Length != HeaderBytes + (long)expectedVertices * MuscleAtlas.Influences * 2)
            throw new InvalidDataException("Unsupported or malformed muscle atlas contract.");
        if (sourceHash.Length != 32 || !bytes.Slice(20, 32).SequenceEqual(sourceHash) ||
            !bytes.Slice(52, 32).SequenceEqual(RegionHash()) ||
            !bytes.Slice(84, 32).SequenceEqual(SHA256.HashData(bytes[HeaderBytes..])))
            throw new InvalidDataException("Muscle atlas source, regions or payload hash mismatch.");
        int size = expectedVertices * MuscleAtlas.Influences;
        try
        {
            return new(bytes.Slice(HeaderBytes, size).ToArray(), bytes.Slice(HeaderBytes + size, size).ToArray());
        }
        catch (ArgumentException e) { throw new InvalidDataException("Invalid atlas influences.", e); }
    }
}
