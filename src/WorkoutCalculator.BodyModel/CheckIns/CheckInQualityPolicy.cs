using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.BodyModel.CheckIns;

public static class CheckInQualityPolicy
{
    public const string Version = "checkin-quality-2", PhotoVersion = "checkin-front-side-1";
    public const double MinimumConfidence = .8, MaxManualResidualCm = 3, MaxFitterResidualCm = 12;

    public static CheckInPhoto Analyze(string session, DateOnly date, CheckInPhotoSource source, bool confirmed,
        Sex sex, double height, bool frontPresent, bool sidePresent, bool backPresent, PhotoProfile? front, PhotoProfile? side)
    {
        var reasons = new List<CheckInReason>();
        int Usable(PhotoProfile? p) => p?.Levels.Count(l => !l.ArmOverlap && double.IsFinite(l.SizeCm) && l.SizeCm > 0) ?? 0;
        bool ShapeValid(PhotoProfile? p, PhotoView view) => p is not null && p.View == view && p.Width > 0 && p.Height > 0 &&
            p.Pose.Count >= 33 && p.Levels.All(l => double.IsFinite(l.Fraction) && double.IsFinite(l.SizeCm)) &&
            double.IsFinite(p.CmPerPixel) && p.CmPerPixel > 0 && double.IsFinite(p.Floor) && double.IsFinite(p.Crown) && p.Floor > p.Crown;
        double? confidence = front?.Pose.Count >= 33 && side?.Pose.Count >= 33
            ? new[] {front,side}.Min(p => new[] {11,12,23,24}.Min(i => p.Pose[i].Visibility)) : null;
        if (confidence is { } c && !AvatarRules.In(c,0,1)) confidence = 0;
        if (frontPresent || sidePresent)
        {
            if (!frontPresent || !sidePresent) reasons.Add(CheckInReason.PairRequired);
            if (!ShapeValid(front,PhotoView.Front) || !ShapeValid(side,PhotoView.Side) || Usable(front)<30 || Usable(side)<30) reasons.Add(CheckInReason.SilhouetteUnusable);
            if ((front?.Warnings.Count ?? 0)>0 || (side?.Warnings.Count ?? 0)>0) reasons.Add(CheckInReason.ImageWarning);
            if (confidence is not >= MinimumConfidence) reasons.Add(CheckInReason.LowPhotoConfidence);
        }
        double? Height(PhotoProfile? p) { var h = p is null ? (double?)null : (p.Floor-p.Crown)*p.CmPerPixel; return h is { } n && AvatarRules.In(n,0,1000) ? n : null; }
        var estimates = ImmutableDictionary.CreateBuilder<Girth,GirthObservation>();
        if (ShapeValid(front,PhotoView.Front) && ShapeValid(side,PhotoView.Side))
            foreach (var g in PhotoGirths.Girths)
                if (PhotoGirths.Estimate(g,sex,front!,side!) is { } e && AvatarRules.In(e.GirthCm,BodySnapshot.Limits(g).Min,BodySnapshot.Limits(g).Max))
                    estimates.Add(g,new(e.GirthCm,MeasurementMethod.PhotoDerived,e.RmseCm));
        var hash = front is null && side is null ? null : HypothesisHash.Of(new CheckInAnalysis(front,side),CheckInJson.Default.CheckInAnalysis);
        var result = new CheckInPhoto(session,date,source,confirmed,sex,height,frontPresent,sidePresent,backPresent,PhotoVersion,hash,
            confidence,Usable(front),Usable(side),Height(front),Height(side),reasons.Distinct().ToImmutableArray(),estimates.ToImmutable());
        result.Validate(); return result;
    }

    public static ImmutableArray<CheckInReason> PhotoReasons(BodyCheckIn c, Profile p)
    {
        if (c.Photo is not { } photo) return [];
        var reasons = photo.Reasons.ToList();
        if (photo.Source != CheckInPhotoSource.OriginalObservation || !photo.ConfirmedOriginal) reasons.Add(CheckInReason.SourceNotFactual);
        if (photo.ObservedDate != c.ObservedDate) reasons.Add(CheckInReason.PhotoDateMismatch);
        if (photo.Sex != p.Sex || Math.Abs(photo.HeightCm-p.HeightCm)>.1) reasons.Add(CheckInReason.ProfileMismatch);
        if (photo.Front || photo.Side)
        {
            if (!photo.Front || !photo.Side) reasons.Add(CheckInReason.PairRequired);
            if (photo.FrontUsableLevels<30 || photo.SideUsableLevels<30 || photo.AnalysisHash is null) reasons.Add(CheckInReason.SilhouetteUnusable);
            if (photo.Confidence is not >= MinimumConfidence) reasons.Add(CheckInReason.LowPhotoConfidence);
            if (photo.FrontHeightCm is not { } fh || photo.SideHeightCm is not { } sh || Math.Abs(fh-p.HeightCm)>p.HeightCm*.03 || Math.Abs(sh-p.HeightCm)>p.HeightCm*.03)
                reasons.Add(CheckInReason.ScaleMismatch);
        }
        foreach (var (g,manual) in c.Manual.Girths)
            if (photo.Estimates.TryGetValue(g,out var estimate) && Math.Abs(manual-estimate.Cm)>Math.Max(5,3*estimate.ModelRmseCm!.Value))
                reasons.Add(CheckInReason.ManualPhotoConflict);
        return reasons.Distinct().ToImmutableArray();
    }

    public static string Message(CheckInReason code) => code switch
    {
        CheckInReason.ManualPhotoConflict => "Замеры сохранены, но фото не использовано для обновления аватара: данные противоречат друг другу.",
        CheckInReason.PairRequired => "Для обновления по фото нужны снимки спереди и сбоку.",
        CheckInReason.SourceNotFactual => "Для замера подходят только ваши реальные фотографии. Сгенерированные изображения не принимаются.",
        CheckInReason.ScaleMismatch or CheckInReason.ProfileMismatch => "Масштаб или параметры фото не совпадают с профилем. Проверьте рост и переснимите фото.",
        CheckInReason.PhotoDateMismatch => "Дата фото не совпадает с датой замера. Выберите актуальный снимок.",
        CheckInReason.SupersededRevision or CheckInReason.CycleChanged => "Аватар уже изменился. Наблюдение сохранено, более новая версия оставлена текущей.",
        CheckInReason.OlderObservation => "Наблюдение старше текущего аватара и сохранено только в истории.",
        CheckInReason.NoSupportedMeasurements => "Фото сохранено для истории. Доступных измерений для обновления формы нет.",
        CheckInReason.AbruptWeightChange or CheckInReason.PriorGeometryConflict => "Изменение сильно отличается от предыдущей формы. Проверьте дату, замеры и фотографии.",
        CheckInReason.FactualResidual or CheckInReason.FitterResidual or CheckInReason.TissueLimit or CheckInReason.MissingGeometry => "Замеры сохранены, но безопасно согласовать форму не удалось. Предыдущий аватар сохранён.",
        CheckInReason.TechnicalFailure => "Обработка прервалась по технической причине. Данные наблюдения сохранены.",
        _ => "Фото недостаточно качественное. Переснимите фигуру целиком при ровном освещении."
    };
}

public sealed record CheckInAnalysis(PhotoProfile? Front, PhotoProfile? Side);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy=System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter=true,RespectRequiredConstructorParameters=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
[System.Text.Json.Serialization.JsonSerializable(typeof(BodyCheckIn))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CheckInAnalysis))]
public sealed partial class CheckInJson : System.Text.Json.Serialization.JsonSerializerContext;
