namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>Факт: вес в какой-то день и, если были фото, обхваты талии и бёдер по ним.</summary>
/// <param name="FromPhotos">Факт — фотосессия (иначе — запись веса).</param>
public sealed record FactPoint(DateOnly Date, double WeightKg, double? WaistCm = null, double? HipsCm = null, bool FromPhotos = false);

/// <summary>Факт против прогноза на ту же дату.</summary>
/// <param name="Week">Неделя плана (дробная): 1,5 — середина второй недели.</param>
public sealed record FactCheck(FactPoint Fact, double Week, double ForecastWeightKg, double ForecastWaistCm, double ForecastHipsCm)
{
    /// <summary>Факт минус прогноз, кг: плюс — тяжелее прогноза.</summary>
    public double WeightDiffKg => Fact.WeightKg - ForecastWeightKg;

    public double? WaistDiffCm => Fact.WaistCm - ForecastWaistCm;
    public double? HipsDiffCm => Fact.HipsCm - ForecastHipsCm;
}

/// <summary>
/// Слежение за планом: прогноз считается от профиля в день начала, факты (взвешивания и фотосессии)
/// сравниваются с прогнозом на свою дату. Прогноз недельный, между неделями — линейно.
/// </summary>
public static class ForecastTracking
{
    public static double WeekOf(DateOnly start, DateOnly date) => (date.DayNumber - start.DayNumber) / 7.0;

    /// <summary>Вес, жир и безжировая масса по прогнозу на дробной неделе (за пределами срока — по краю).</summary>
    public static (double WeightKg, double FatKg, double LeanKg) At(ForecastResult forecast, double week)
    {
        var w = forecast.Weeks;
        double t = Math.Clamp(week, 0, w.Count - 1);
        int i = Math.Min((int)Math.Floor(t), w.Count - 1);
        int j = Math.Min(i + 1, w.Count - 1);
        double u = t - i;
        double L(double a, double b) => a + u * (b - a);
        return (L(w[i].WeightKg, w[j].WeightKg), L(w[i].FatMassKg, w[j].FatMassKg), L(w[i].LeanMassKg, w[j].LeanMassKg));
    }

    /// <summary>
    /// Факты в пределах плана (от начала до конца срока) против прогноза. Обхваты по прогнозу — через
    /// изменение жира и безжировой массы на эту дату, как у итога прогноза; вода и гликоген есть на весах,
    /// но не на обхватах.
    /// </summary>
    public static IReadOnlyList<FactCheck> Compare(ForecastResult forecast, DateOnly start, IEnumerable<FactPoint> facts)
    {
        int last = forecast.Weeks.Count - 1;
        var result = new List<FactCheck>();
        foreach (var fact in facts.OrderBy(f => f.Date))
        {
            double week = WeekOf(start, fact.Date);
            if (week < 0 || week > last + 1e-9) continue;
            var (weight, fat, lean) = At(forecast, week);
            var girths = ForecastEngine.ApplyToGirths(forecast.Start, fat - forecast.Start.FatMassKg, lean - forecast.Start.LeanMassKg);
            result.Add(new FactCheck(fact, week, weight, girths.WaistCm, girths.HipsCm));
        }
        return result;
    }
}
