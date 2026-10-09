using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.AnatomyBuild;

public sealed record AnatomyMesh(Vec3[] Vertices, int[] Triangles, int Components, int RemovedDegenerateFaces);

/// <summary>Offline only. Never extracts archive paths to disk and never trusts OBJ counts/coordinates.</summary>
public static partial class AnatomySource
{
    public const int MaxVertices = 250_000, MaxFaces = 500_000, MaxObjectBytes = 32_000_000;
    [GeneratedRegex(@"^FJ[0-9]+M?$", RegexOptions.CultureInvariant)] private static partial Regex ObjectId();
    public static string ValidateManifest(byte[] manifestBytes,byte[] mappingBytes,string archiveFilename)
    {
        try
        {
            var manifest=JsonDocument.Parse(manifestBytes).RootElement;
            if(manifest.GetProperty("release").GetString()!="4.0" || manifest.GetProperty("license").GetString()!="CC-BY-4.0" ||
                manifest.GetProperty("archiveFilename").GetString()!=archiveFilename || archiveFilename!="isa_BP3D_4.0_obj_99.zip" ||
                Convert.ToHexString(SHA256.HashData(mappingBytes))!=manifest.GetProperty("mappingSha256").GetString())throw new InvalidDataException("Manifest/version/license/mapping mismatch.");
            string hash=manifest.GetProperty("archiveSha256").GetString()!;
            if(hash.Length!=64 || !hash.All(Uri.IsHexDigit))throw new InvalidDataException("Pinned archive SHA-256 required.");
            return hash;
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or NullReferenceException){throw new InvalidDataException("Malformed anatomy manifest.",e);}
    }
    public static Dictionary<string, AnatomyMesh> ReadArchive(byte[] bytes, string sha256, IEnumerable<string> selected)
    {
        if (bytes.Length>256_000_000 || sha256.Length != 64 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("BodyParts3D archive SHA-256 mismatch.");
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.Entries.Count is < 1 or > 5000) throw new InvalidDataException("Unexpected archive size.");
        var ids = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            var parts = e.FullName.Split('/');
            if (parts.Length != 2 || parts[0] != "isa_BP3D_4.0_obj_99" || !parts[1].EndsWith(".obj", StringComparison.Ordinal)
                || !ObjectId().IsMatch(parts[1][..^4]) || e.Length is < 1 or > MaxObjectBytes)
                throw new InvalidDataException("Unexpected archive entry/path.");
            if (!ids.TryAdd(parts[1][..^4], e)) throw new InvalidDataException("Duplicate object ID.");
        }
        var chosen = selected.ToArray();
        if (chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Length) throw new InvalidDataException("Duplicate selected object ID.");
        if(chosen.Sum(id=>ids.TryGetValue(id,out var entry)?entry.Length:0)>64_000_000)throw new InvalidDataException("Selected geometry exceeds expanded byte budget.");
        var result = new Dictionary<string, AnatomyMesh>(StringComparer.Ordinal);
        foreach (var id in chosen.Order(StringComparer.Ordinal))
        {
            if (!ids.TryGetValue(id, out var e)) throw new InvalidDataException($"Missing selected object: {id}");
            using var reader = new StreamReader(e.Open());
            result.Add(id, ParseObj(reader));
        }
        return result;
    }
    public static AnatomyMesh ParseObj(TextReader reader)
    {
        var points = new List<Vec3>(); var faces = new List<int>(); int lines=0, removed=0;
        string? line;
        while ((line=reader.ReadLine()) is not null)
        {
            if (++lines > 2_000_000 || line.Length > 8192) throw new InvalidDataException("OBJ limits exceeded.");
            var tokens=line.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length==0) continue;
            if (tokens[0]=="v")
            {
                if (tokens.Length!=4 || points.Count>=MaxVertices) throw new InvalidDataException("Invalid OBJ vertex.");
                var values=tokens.Skip(1).Select(t=>double.TryParse(t,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)&&Math.Abs(n)<=10000?n:throw new InvalidDataException("Nonfinite/out-of-bounds OBJ coordinate.")).ToArray();
                points.Add(new(values[0],values[1],values[2]));
            }
            else if (tokens[0]=="f")
            {
                if (tokens.Length is <4 or >33 || faces.Count/3>=MaxFaces) throw new InvalidDataException("Invalid OBJ face.");
                var face=tokens.Skip(1).Select(t=>
                {
                    if (!int.TryParse(t.Split('/')[0],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int n) || n==0)
                        throw new InvalidDataException("Invalid OBJ index.");
                    n=n<0?points.Count+n:n-1;
                    if (n<0 || n>=points.Count) throw new InvalidDataException("OBJ index outside vertices.");
                    return n;
                }).ToArray();
                for(int i=1;i+1<face.Length;i++)
                {
                    var a=face[0];var b=face[i];var c=face[i+1];
                    if ((points[b]-points[a]).Cross(points[c]-points[a]).Length<1e-10) { removed++; continue; }
                    if(faces.Count/3>=MaxFaces)throw new InvalidDataException("Too many triangles.");
                    faces.AddRange([a,b,c]);
                }
            }
            else if(tokens[0] is "vn" or "vt")
            {
                foreach(var t in tokens.Skip(1)) if(!double.TryParse(t,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))throw new InvalidDataException("Invalid OBJ attribute.");
            }
            else if(tokens[0] is not ("o" or "g" or "s" or "usemtl" or "mtllib")) throw new InvalidDataException("Unsupported OBJ statement.");
        }
        if(points.Count<3 || faces.Count<3)throw new InvalidDataException("Empty OBJ surface.");
        // Source OBJ repeats positions at normal seams. Weld exact duplicates without changing geometry.
        var unique=new Dictionary<Vec3,int>();var welded=new List<Vec3>();var remap=new int[points.Count];
        for(int i=0;i<points.Count;i++){if(!unique.TryGetValue(points[i],out int v)){v=welded.Count;unique.Add(points[i],v);welded.Add(points[i]);}remap[i]=v;}
        points=welded;for(int i=0;i<faces.Count;i++)faces[i]=remap[faces[i]];
        var parents=Enumerable.Range(0,points.Count).ToArray();
        int Root(int v){while(parents[v]!=v){parents[v]=parents[parents[v]];v=parents[v];}return v;}
        for(int i=0;i<faces.Count;i+=3){int a=Root(faces[i]);parents[Root(faces[i+1])]=a;parents[Root(faces[i+2])]=a;}
        int components=faces.Distinct().Select(Root).Distinct().Count();
        return new(points.ToArray(),faces.ToArray(),components,removed);
    }
}
