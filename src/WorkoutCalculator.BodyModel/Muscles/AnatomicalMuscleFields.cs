using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.BodyModel.Muscles;

public interface IMuscleMorphFieldProvider
{
    string Version { get; }
    string? AssetHash { get; }
    void Apply(double[] positions, MuscleMorphState state, double heightCm);
}

public sealed record MuscleGeometrySelection(string ProviderVersion, string? AssetSha256)
{
    public const string ProceduralVersion = "muscle-field-procedural-1";
    public static MuscleGeometrySelection Procedural { get; } = new(ProceduralVersion, null);
    public void Validate()
    {
        if (ProviderVersion == ProceduralVersion && AssetSha256 is null) return;
        if (ProviderVersion == AnatomicalMuscleFields.ModelVersion && AssetSha256 is { Length:64 } hash && hash.All(Uri.IsHexDigit)) return;
        throw new ArgumentException("Unsupported muscle geometry descriptor.");
    }
}

public sealed record AnatomicalFieldEntry(ushort Vertex, byte Group, short X, short Y, short Z);
public sealed record AnatomicalFieldHeader(int VertexCount, string TopologyHash, string ArchiveHash, string MappingHash,
    int RegistrationVersion, int AlgorithmVersion, double ReferenceHeightCm, string[] Groups);

/// <summary>Checked sparse, micrometre-quantized displacement bases; all anatomy processing is offline.</summary>
public sealed class AnatomicalMuscleFields : IMuscleMorphFieldProvider
{
    public const string ModelVersion = "muscle-field-anatomical-1", FileName = "makehuman-anatomical-muscle-fields-v1.bin";
    public const double QuantumM = .000001, MaximumGroupM = .004, MaximumCombinedM = .006;
    private readonly AnatomicalFieldEntry[] _entries;
    private readonly IMuscleMorphFieldProvider? _fallback;
    public string Version => ModelVersion;
    public string AssetHash { get; }
    public AnatomicalFieldHeader Header { get; }
    public IReadOnlyList<AnatomicalFieldEntry> Entries => Array.AsReadOnly(_entries);
    public MuscleGeometrySelection Selection => new(Version, AssetHash);
    private AnatomicalMuscleFields(AnatomicalFieldHeader header, AnatomicalFieldEntry[] entries, string hash, IMuscleMorphFieldProvider? fallback)
    { Header=header;_entries=entries;AssetHash=hash;_fallback=fallback; }

    public void Apply(double[] positions, MuscleMorphState state, double heightCm)
    {
        state.Validate();
        if(positions.Length<Header.VertexCount*3 || !double.IsFinite(heightCm) || heightCm is <100 or >250)throw new ArgumentException("Invalid morph geometry.");
        if(!state.Groups.Values.Any(v=>v!=0))return;
        var delta=new double[Header.VertexCount*3];
        var scales=Header.Groups.Select(g=>state.Groups.GetValueOrDefault(g)/.25*heightCm/Header.ReferenceHeightCm*QuantumM).ToArray();
        foreach(var e in _entries){int i=e.Vertex*3;double s=scales[e.Group];delta[i]+=e.X*s;delta[i+1]+=e.Y*s;delta[i+2]+=e.Z*s;}
        // Documented no-direct-field groups retain the frozen procedural mapping (only lats is nonzero in v1).
        var unsupported=state.Groups.Where(p=>!Header.Groups.Contains(p.Key)).ToImmutableDictionary();
        if(unsupported.Count>0)_fallback?.Apply(delta,new(unsupported),heightCm);
        double cap=MaximumCombinedM*heightCm/Header.ReferenceHeightCm;
        for(int i=0;i<delta.Length;i+=3){double len=Math.Sqrt(delta[i]*delta[i]+delta[i+1]*delta[i+1]+delta[i+2]*delta[i+2]);double s=len>cap?cap/len:1;positions[i]+=delta[i]*s;positions[i+1]+=delta[i+1]*s;positions[i+2]+=delta[i+2]*s;}
    }
    public static string TopologyHash(MakeHumanData data)
    {
        using var m=new MemoryStream();using var w=new BinaryWriter(m);
        w.Write(data.BodyVertexCount);foreach(var p in data.Positions)w.Write(p);foreach(var q in data.Quads)w.Write(q);
        return Convert.ToHexString(SHA256.HashData(m.ToArray()));
    }
    public static byte[] Write(AnatomicalFieldHeader header, IEnumerable<AnatomicalFieldEntry> entries)
    {
        using var m=new MemoryStream();using var w=new BinaryWriter(m,Encoding.UTF8,true);
        w.Write(Encoding.ASCII.GetBytes("AMF1"));w.Write(1);w.Write(header.VertexCount);
        foreach(var h in new[]{header.TopologyHash,header.ArchiveHash,header.MappingHash})w.Write(Convert.FromHexString(h));
        w.Write(header.RegistrationVersion);w.Write(header.AlgorithmVersion);w.Write(header.ReferenceHeightCm);
        w.Write(header.Groups.Length);foreach(var g in header.Groups)w.Write(g);
        var sorted=entries.OrderBy(e=>e.Group).ThenBy(e=>e.Vertex).ToArray();w.Write(sorted.Length);
        foreach(var e in sorted){w.Write(e.Vertex);w.Write(e.Group);w.Write(e.X);w.Write(e.Y);w.Write(e.Z);}
        w.Flush();var bytes=m.ToArray();w.Write(SHA256.HashData(bytes));return m.ToArray();
    }
    public static AnatomicalMuscleFields Read(byte[] bytes, MakeHumanData data, string? expectedHash=null, IMuscleMorphFieldProvider? fallback=null)
    {
        try
        {
            if(bytes.Length is <180 or >4_000_000)throw new InvalidDataException("Invalid anatomical sidecar size.");
            string hash=Convert.ToHexString(SHA256.HashData(bytes));
            if(expectedHash is not null && !hash.Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Anatomical asset hash mismatch.");
            if(!SHA256.HashData(bytes.AsSpan(0,bytes.Length-32)).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length-32)))throw new InvalidDataException("Anatomical checksum mismatch.");
            using var m=new MemoryStream(bytes,0,bytes.Length-32,false);using var r=new BinaryReader(m,Encoding.UTF8);
            if(Encoding.ASCII.GetString(r.ReadBytes(4))!="AMF1" || r.ReadInt32()!=1)throw new InvalidDataException("Unknown anatomical format.");
            int vertices=r.ReadInt32();string topology=Convert.ToHexString(r.ReadBytes(32)),archive=Convert.ToHexString(r.ReadBytes(32)),mapping=Convert.ToHexString(r.ReadBytes(32));
            int registration=r.ReadInt32(),algorithm=r.ReadInt32();double height=r.ReadDouble();int groups=r.ReadInt32();
            if(vertices!=data.BodyVertexCount || topology!=TopologyHash(data) || registration!=1 || algorithm!=1 || height!=175 || groups is <1 or >20)throw new InvalidDataException("Incompatible anatomical sidecar.");
            var names=new string[groups];for(int i=0;i<groups;i++){names[i]=r.ReadString();if(!MuscleDefinitions.Groups.Any(g=>g.Id==names[i]) || names[i].Length>64)throw new InvalidDataException("Unknown anatomical group.");}
            if(names.Distinct().Count()!=groups)throw new InvalidDataException("Duplicate anatomical group.");
            int count=r.ReadInt32();if(count<1 || count>vertices*groups || m.Length-m.Position!=count*9L)throw new InvalidDataException("Invalid anatomical entry count.");
            var entries=new AnatomicalFieldEntry[count];int last=-1;
            for(int i=0;i<count;i++)
            {
                var e=new AnatomicalFieldEntry(r.ReadUInt16(),r.ReadByte(),r.ReadInt16(),r.ReadInt16(),r.ReadInt16());
                int key=e.Group*vertices+e.Vertex;double length=Math.Sqrt((double)e.X*e.X+(double)e.Y*e.Y+(double)e.Z*e.Z)*QuantumM;
                if(e.Vertex>=vertices || e.Group>=groups || key<=last || length>MaximumGroupM+2*QuantumM || MuscleFieldProtection.IsProtected(data,e.Vertex))throw new InvalidDataException("Unsafe anatomical field entry.");
                entries[i]=e;last=key;
            }
            return new(new(vertices,topology,archive,mapping,registration,algorithm,height,names),entries,hash,fallback);
        }
        catch(Exception e) when(e is EndOfStreamException or ArgumentException or OverflowException or FormatException){throw new InvalidDataException("Malformed anatomical sidecar.",e);}
    }
}

public static class MuscleFieldProtection
{
    public static bool IsProtected(MakeHumanData data,int vertex)
    {
        var s=data.Skeleton!;
        for(int k=0;k<MakeHumanSkeleton.Influences;k++)
        {
            int i=vertex*MakeHumanSkeleton.Influences+k;var b=s.Bones[s.SkinBones[i]].Name;
            if(s.SkinWeights[i]>0 && new[]{"hand","finger","thumb","foot","toe","head","neck","jaw","eye"}.Any(b.Contains))return true;
        }
        // Canonical central perineum/genitals: exclude the complete narrow crotch region.
        double x=data.Positions[vertex*3],y=data.Positions[vertex*3+1];
        return Math.Abs(x)<.055 && y is >-.18 and <.015;
    }
}
