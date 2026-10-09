using System.Text.Json.Serialization;
namespace WorkoutCalculator.Web.Services;
public sealed class ManualValidation
{
    public double? Tape { get; set; }
    public double? Photo { get; set; }
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public double? ExternalKcal { get; set; }
    public int? AvatarSimilarity { get; set; }
    public void Validate()
    {
        if (!DateOnly.TryParse(Date, out _) || AvatarSimilarity is < 1 or > 5 || Tape is <= 0 or > 300 || Photo is <= 0 or > 300 || ExternalKcal is < 0 or > 20000 ||
            new[] { Tape, Photo, ExternalKcal }.Any(n => n.HasValue && !double.IsFinite(n.Value)))
            throw new ArgumentException("Проверьте дату и диапазон контрольных значений.");
    }
}
[JsonSerializable(typeof(ManualValidation))]
[JsonSerializable(typeof(System.Collections.Immutable.ImmutableArray<WorkoutCalculator.BodyModel.Forecast.ForecastObservation>))]
internal sealed partial class ValidationJson : JsonSerializerContext;
