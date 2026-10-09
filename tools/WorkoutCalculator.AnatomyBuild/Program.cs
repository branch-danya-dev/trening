using System.Security.Cryptography;
using System.Text.Json;
using WorkoutCalculator.AnatomyBuild;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;

if(args.Length<2){Console.Error.WriteLine("Usage: AnatomyBuild <repo-root> <pinned-archive.zip> [output-directory]. Downloads are intentional and separate; CI is offline.");return 2;}
var root=Path.GetFullPath(args[0]);var output=args.Length>2?Path.GetFullPath(args[2]):Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data");
var manifestBytes=File.ReadAllBytes(Path.Combine(root,"docs/assets/bodyparts3d/manifest.json"));
var mappingBytes=File.ReadAllBytes(Path.Combine(root,"docs/assets/bodyparts3d/mapping.json"));
var archiveHash=AnatomySource.ValidateManifest(manifestBytes,mappingBytes,Path.GetFileName(args[1]));
var manifest=JsonDocument.Parse(manifestBytes).RootElement;
void CheckInput(byte[] bytes,string key){if(Convert.ToHexString(SHA256.HashData(bytes))!=manifest.GetProperty(key).GetString())throw new InvalidDataException("Pinned input mismatch: "+key);}
CheckInput(File.ReadAllBytes(Path.Combine(root,"docs/assets/bodyparts3d/license-2026-10-09.html")),"licenseSnapshotSha256");
CheckInput(File.ReadAllBytes(Path.Combine(root,"docs/assets/bodyparts3d/CC-BY-4.0.txt")),"legalTextSha256");
var map=JsonDocument.Parse(mappingBytes).RootElement;
var ids=map.GetProperty("groups").EnumerateArray().SelectMany(g=>g.GetProperty("objects").EnumerateArray()).Concat(map.GetProperty("bones").EnumerateArray()).Select(o=>o.GetProperty("objectId").GetString()!).ToArray();
if(new FileInfo(args[1]).Length>256_000_000)throw new InvalidDataException("Archive byte budget exceeded.");
var meshes=AnatomySource.ReadArchive(File.ReadAllBytes(args[1]),archiveHash,ids);
var raw=File.ReadAllBytes(Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data",MakeHumanData.FileName));var data=MakeHumanData.Read(raw);
CheckInput(raw,"makeHumanSha256");
var atlasBytes=File.ReadAllBytes(Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data",MuscleAtlasBinary.FileName));CheckInput(atlasBytes,"muscleAtlasSha256");
var atlas=MuscleAtlasBinary.Read(atlasBytes,data.BodyVertexCount,SHA256.HashData(raw));
var built=FieldBuilder.Build(data,atlas,meshes,mappingBytes,archiveHash);
var checkedFields=AnatomicalMuscleFields.Read(built.Bytes,data);
Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,AnatomicalMuscleFields.FileName),built.Bytes);
File.WriteAllText(Path.Combine(output,"anatomy-build-report.json"),JsonSerializer.Serialize(new{build=built.Report,meshes=meshes.Select(m=>new{id=m.Key,vertices=m.Value.Vertices.Length,triangles=m.Value.Triangles.Length/3,m.Value.Components,m.Value.RemovedDegenerateFaces})},new JsonSerializerOptions{WriteIndented=true})+"\n");
Console.WriteLine($"{checkedFields.Entries.Count} entries; {built.Bytes.Length} bytes; SHA-256 {checkedFields.AssetHash}");return 0;
