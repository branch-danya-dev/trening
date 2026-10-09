using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.BodyModel.Avatars;

public sealed record AvatarPhotoResult(AvatarReconstructionInputs Inputs, bool Accepted, ImmutableArray<string> Warnings);
public static class AvatarPhotoReconstruction
{
    public const string PipelineVersion = "front-side-avatar-policy-1";
    /// <summary>Initial-draft sensor estimates, kept outside the writable factual snapshot.</summary>
    public static AvatarPhotoResult Apply(AvatarReconstructionInputs input, string session, Sex sex, double height,
        PhotoProfile? front, PhotoProfile? side)
    {
        input.Validate();
        var warnings = (front?.Warnings ?? []).Concat(side?.Warnings ?? []).Distinct().ToList();
        bool Good(PhotoProfile? p, PhotoView view) => p is not null && p.View == view &&
            double.IsFinite(p.CmPerPixel) && p.CmPerPixel > 0 && p.Pose.Count >= 33 &&
            new[] { 11, 12, 23, 24 }.All(i => p.Pose[i].Visibility >= .8) &&
            p.Levels.Count(l => !l.ArmOverlap && double.IsFinite(l.SizeCm) && l.SizeCm > 0) >= 30 && p.Warnings.Count == 0;
        if (!Good(front, PhotoView.Front) || !Good(side, PhotoView.Side)) warnings.Add("Для уточнения нужны оба качественных ракурса: спереди и сбоку. Текущая форма сохранена.");
        var profile = input.BaseProfile();
        if (sex != profile.Sex || Math.Abs(height - profile.HeightCm) > .1) warnings.Add("Рост или пол фотосессии отличается от исходных данных. Переснимите с актуальными параметрами.");
        if (warnings.Count > 0) return new(input, false, warnings.ToImmutableArray());
        var estimates = PhotoGirths.Girths.Select(g => PhotoGirths.Estimate(g, sex, front!, side!)).OfType<PhotoGirthEstimate>()
            .Where(e => input.Fact?.Measurements.ContainsKey(e.Girth) != true).ToArray();
        var observations = input.PhotoEstimates.ToBuilder();
        foreach (var e in estimates) { observations[e.Girth] = new(e.GirthCm, e.RmseCm, session); profile.SetGirth(e.Girth, e.GirthCm); }
        var result = input with { BaseProfileJson = JsonSerializer.Serialize(profile, ForecastJson.Default.BodyProfile),
            PhotoEstimates = observations.ToImmutable(),
            Photos = input.Photos.Where(p => p.SessionId != session).Append(new(session, PipelineVersion, .8)).ToImmutableArray(),
            Fields = input.Fields.Select(f => Enum.TryParse<Girth>(f.Field, out var g) && observations.ContainsKey(g)
                ? f with { Source = AvatarFieldSource.PhotoDerived } : f).ToImmutableArray() };
        result.Validate();
        return new(result, true, [estimates.Length == 0
            ? "Качественные фото связаны с черновиком как визуальное подтверждение. Все поддерживаемые обхваты уже измерены; ручные значения сохранены."
            : "Фото уточнили только поддерживаемые обхваты без ручных замеров. Это оценки по фото; исходные факты не изменены."]);
    }
}
