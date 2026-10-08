using System.Text;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.Web.Services;

/// <summary>
/// Отчёт о разборе фотосессии для проверки на реальных снимках: только цифры — профиль, ширина и глубина
/// на ключевых уровнях, обхваты по фото против ленты, подгонка. Ни снимков, ни контуров в нём нет;
/// отправлять его или нет, решает пользователь.
/// </summary>
public static class TestReport
{
    private static readonly (string Name, Girth Girth, Func<Sex, double> Fraction)[] Levels =
    [
        ("Грудь", Girth.Chest, Proportions.ChestHeight),
        ("Талия", Girth.Waist, Proportions.WaistHeight),
        ("Бёдра", Girth.Hips, Proportions.HipsGirthHeight),
    ];

    /// <param name="fit">Подгонка модели к этой сессии; null — не делалась.</param>
    public static string Build(BodyProfile profile, PhotoSession session, PhotoFitResult? fit, string browser, string build)
    {
        var a = session.Analysis;
        var sb = new StringBuilder();
        sb.AppendLine("Тренировки и тело — отчёт для проверки (без снимков)");
        sb.AppendLine($"Сборка {(build.Length > 0 ? build : "без git")} · {Fmt.DateTime(DateTimeOffset.Now)}");
        sb.AppendLine($"Браузер: {browser}");
        sb.AppendLine();
        sb.AppendLine($"Профиль: {(profile.Sex == Sex.Female ? "женщина" : "мужчина")}, {profile.Age} лет, рост {Fmt.N(profile.HeightCm, 0)} см, " +
                      $"вес {Fmt.N(profile.WeightKg, 1)} кг, жир {Fmt.N(profile.BodyFatPercent, 1)} %");
        sb.AppendLine($"Лента сейчас, см: грудь {Fmt.N(profile.ChestCm, 1)} · талия {Fmt.N(profile.WaistCm, 1)} · " +
                      $"бёдра {Fmt.N(profile.HipsCm, 1)}");
        sb.AppendLine($"Осанка: таз {Fmt.Signed(profile.Posture.PelvicTilt, 0)}°, прогиб {Fmt.Signed(profile.Posture.Lordosis, 0)}°, " +
                      $"сутулость {Fmt.Signed(profile.Posture.Kyphosis, 0)}°");
        sb.AppendLine();
        sb.AppendLine($"Сессия {Fmt.DateTime(session.CreatedAt)}: {string.Join(", ", session.Views.Select(v => v == "side" ? "сбоку" : "спереди"))}; " +
                      $"при съёмке рост {Fmt.N(session.HeightCm, 0)} см, вес {Fmt.N(session.WeightKg, 1)} кг");
        if (a is null)
        {
            sb.AppendLine("Разбора нет.");
            return sb.ToString();
        }

        var any = a.Front ?? a.Side;
        sb.AppendLine($"Разбор: {Fmt.N(a.Milliseconds)} мс, 1 px ≈ {Fmt.N(any!.CmPerPixel * 10, 2)} мм" +
                      (a.Front?.ScaleFromSide == true ? " (масштаб спереди — по снимку сбоку)" : ""));
        foreach (var (name, p) in new[] { ("спереди", a.Front), ("сбоку", a.Side) })
        {
            if (p is null) continue;
            sb.AppendLine($"  {name}: снимок {p.Width}×{p.Height}, фигура {p.Bottom - p.Top} px, уровней {p.Levels.Count} " +
                          $"(у руки {p.Levels.Count(l => l.ArmOverlap)}, по яркости {p.Levels.Count(l => l.Snapped)})");
        }
        var warnings = (a.Front?.Warnings ?? []).Concat(a.Side?.Warnings ?? []).Distinct().ToList();
        sb.AppendLine(warnings.Count == 0 ? "Предупреждений нет." : "Предупреждения: " + string.Join("; ", warnings));
        sb.AppendLine();

        sb.AppendLine("Уровень: ширина / глубина, см · обхват по фото / лента, см");
        foreach (var (name, girth, fraction) in Levels)
        {
            double f = fraction(session.Sex);
            string width = a.Front?.At(f) is ProfileLevel w ? Fmt.N(w.SizeCm, 1) : "—";
            string depth = a.Side?.At(f) is ProfileLevel d ? Fmt.N(d.SizeCm, 1) : "—";
            string photo = PhotoGirths.Girths.Contains(girth) && a.Front is not null && a.Side is not null
                && PhotoGirths.Estimate(girth, session.Sex, a.Front, a.Side) is PhotoGirthEstimate e
                ? $"{Fmt.N(e.GirthCm, 1)} ± {Fmt.N(e.RmseCm, 1)}"
                : "—";
            sb.AppendLine($"  {name} ({Fmt.N(f * 100, 1)} % роста): {width} / {depth} · {photo} / {Fmt.N(profile.GetGirth(girth), 1)}");
        }

        if (fit is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Подгонка: расхождение {Fmt.N(fit.ErrorBeforeCm, 2)} → {Fmt.N(fit.ErrorAfterCm, 2)} см, {fit.Builds} сборок");
            sb.AppendLine($"  таз {Fmt.Signed(fit.Posture.PelvicTilt, 1)}°, прогиб {Fmt.Signed(fit.Posture.Lordosis, 1)}°, " +
                          $"сутулость {Fmt.Signed(fit.Posture.Kyphosis, 1)}°");
            sb.AppendLine($"  живот {Fmt.Signed(fit.Form.Stomach * 100, 0)}, ягодицы {Fmt.Signed(fit.Form.Buttocks * 100, 0)}, " +
                          $"глубина корпуса {Fmt.Signed(fit.Form.TorsoDepth * 100, 0)}, V-силуэт {Fmt.Signed(fit.Form.VShape * 100, 0)}");
        }
        return sb.ToString();
    }
}
