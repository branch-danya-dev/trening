using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.BodyModel.Muscles;

/// <summary>
/// Relative visualization layer, not anatomical segmentation. Four uint8 region IDs and four uint8
/// weights per body vertex; weights sum to 255. Neutral (0) explicitly covers unmodelled surface.
/// Vertex order follows MakeHuman topology and survives morphs, fitting, posture and runtime skinning.
/// </summary>
public sealed class MuscleAtlas
{
    public const int Influences = 4;
    public const int Version = 1;
    public int VertexCount => RegionIndices.Length / Influences;
    public byte[] RegionIndices { get; }
    public byte[] Weights { get; }

    public MuscleAtlas(byte[] regionIndices, byte[] weights)
    {
        if (regionIndices.Length != weights.Length || weights.Length == 0 || weights.Length % Influences != 0)
            throw new ArgumentException("Atlas needs four region IDs and weights per vertex.");
        for (int v = 0; v < weights.Length; v += Influences)
        {
            int sum = 0;
            for (int i = 0; i < Influences; i++)
            {
                if (regionIndices[v + i] >= MuscleDefinitions.Regions.Count) throw new ArgumentException("Unknown atlas region.");
                sum += weights[v + i];
            }
            if (sum != 255) throw new ArgumentException("Atlas weights must sum to 255.");
        }
        RegionIndices = regionIndices;
        Weights = weights;
    }
}
