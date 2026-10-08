using System.Buffers.Binary;
using System.Security.Cryptography;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;

namespace WorkoutCalculator.Tests.BodyModel;

public class MuscleAtlasBinaryTests(MakeHumanFixture fixture) : IClassFixture<MakeHumanFixture>
{
    private static byte[] Bytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", MuscleAtlasBinary.FileName));
    private static byte[] SourceHash() => SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", MakeHumanData.FileName)));

    [Fact]
    public void CanonicalAssetIsByteForByteGeneratedAndSourcePinned()
    {
        var generated = MakeHumanMuscleAtlas.Generate(fixture.Data);
        var bytes = Bytes();
        Assert.Equal(107156, bytes.Length);
        Assert.Equal("c5adeeba3e84e647e2d0a4905e8d16ab51d4a8ce1c863a86361137349f7b310d", Convert.ToHexString(SourceHash()).ToLowerInvariant());
        Assert.Equal("21d6bbf2002ee1d1e8611b312576a61f53b93ff5e4dfbbe2d6183dd767d7f7a4", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(bytes, MuscleAtlasBinary.Write(generated, SourceHash()));
        var loaded = MuscleAtlasBinary.Read(bytes, fixture.Data.BodyVertexCount, SourceHash());
        Assert.Equal(generated.RegionIndices, loaded.RegionIndices);
        Assert.Equal(generated.Weights, loaded.Weights);
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(8)] [InlineData(12)] [InlineData(16)]
    [InlineData(20)] [InlineData(52)] [InlineData(84)] [InlineData(116)] [InlineData(107155)]
    public void RejectsCorruptContractSourceRegionHashAndPayload(int offset)
    {
        var bytes = Bytes(); bytes[offset] ^= 1;
        Assert.Throws<InvalidDataException>(() => MuscleAtlasBinary.Read(bytes, fixture.Data.BodyVertexCount, SourceHash()));
    }

    [Fact]
    public void RejectsTruncationTrailingBytesWrongTopologyAndInvalidInfluences()
    {
        var bytes = Bytes();
        foreach (var length in new[] { 0, 3, 115, 120, bytes.Length - 1 })
            Assert.Throws<InvalidDataException>(() => MuscleAtlasBinary.Read(bytes.AsSpan(0, length), fixture.Data.BodyVertexCount, SourceHash()));
        Assert.Throws<InvalidDataException>(() => MuscleAtlasBinary.Read([..bytes, 0], fixture.Data.BodyVertexCount, SourceHash()));
        Assert.Throws<InvalidDataException>(() => MuscleAtlasBinary.Read(bytes, 1, SourceHash()));
        Assert.Throws<InvalidDataException>(() => MuscleAtlasBinary.Read(bytes, fixture.Data.BodyVertexCount, new byte[32]));
        bytes[MuscleAtlasBinary.HeaderBytes] = 255;
        SHA256.HashData(bytes.AsSpan(MuscleAtlasBinary.HeaderBytes)).CopyTo(bytes, 84);
        Assert.Throws<InvalidDataException>(() => MuscleAtlasBinary.Read(bytes, fixture.Data.BodyVertexCount, SourceHash()));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => MuscleAtlasBinary.Read(bytes, int.MaxValue, SourceHash()));
    }
}
