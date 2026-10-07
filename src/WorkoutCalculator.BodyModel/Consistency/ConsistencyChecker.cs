using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.Consistency;

/// <param name="Text">Мягкая подсказка для пользователя — не ошибка, ввод не блокируется.</param>
public sealed record ConsistencyHint(string Text);

/// <param name="VolumeLiters">Объём тела по обхватам: манекен с поправкой по ИМТ.</param>
/// <param name="Deviation">(объём по обхватам − ожидаемый) / ожидаемый: +0,10 — на 10 % объёмнее.</param>
public sealed record ConsistencyReport(
    double VolumeLiters,
    double ExpectedVolumeLiters,
    double Deviation,
    IReadOnlyList<ConsistencyHint> Hints);

/// <summary>Сверка замеров между собой: объём манекена против веса и неправдоподобные сочетания.</summary>
public static class ConsistencyChecker
{
    /// <summary>
    /// Допустимое расхождение объёма по обхватам и объёма из веса. На людях ANSUR II (% жира — по формуле
    /// ВМС США) с поправкой по ИМТ разброс — 2,2–2,5 %, дальше 8 % уходят 0–0,1 % людей
    /// (tools/WorkoutCalculator.AnsurFit --validate). Свой % жира с весов или калипера тоже ошибается
    /// на несколько процентов, так что 8 % — около трёх стандартных отклонений: подсказка — к ошибке ввода.
    /// </summary>
    public const double VolumeTolerance = 0.08;

    /// <summary>
    /// Воздух в лёгких при спокойном выдохе (функциональная остаточная ёмкость, ≈ 2,5–3 л у взрослых).
    /// Сетка — внешний объём тела, а плотность по Сири относится к тканям без воздуха,
    /// поэтому к объёму из веса его надо прибавить. Без поправки манекен «толще» веса на 3–5 %.
    /// </summary>
    public static double LungAirLiters(Sex sex) => sex == Sex.Male ? 3.0 : 2.4;

    public static double ExpectedVolumeLiters(BodyProfile p) =>
        BodyDensity.TissueVolumeLiters(p.WeightKg, p.BodyFatPercent) + LungAirLiters(p.Sex);

    /// <summary>
    /// Поправка объёма манекена по ИМТ, подобранная на людях ANSUR II (tools/WorkoutCalculator.AnsurFit
    /// --validate): у худых манекен по обхватам выходит объёмнее реального тела, у полных — меньше.
    /// Вероятная причина — живот между уровнями замеров у полных и форма сечений, которую эллипс
    /// передаёт неточно. Отклонение = Offset + Slope · (ИМТ − 25), ИМТ ограничен 16…45.
    /// </summary>
    public static (double Offset, double Slope) MannequinBias(Sex sex) => sex == Sex.Male
        ? (0.0251, -0.00404)   // 4081 мужчина
        : (-0.0101, -0.00363); // 1978 женщин

    /// <summary>Множитель к объёму манекена, который убирает перекос по ИМТ.</summary>
    public static double MannequinCalibration(Sex sex, double bmi)
    {
        var (offset, slope) = MannequinBias(sex);
        return 1 / (1 + offset + slope * (Math.Clamp(bmi, 16, 45) - 25));
    }

    /// <summary>Объём по обхватам — по манекену с поправкой по ИМТ.</summary>
    public static ConsistencyReport Check(Mannequin mannequin) =>
        Check(mannequin.Profile, mannequin.VolumeLiters * MannequinCalibration(mannequin.Profile.Sex, mannequin.Profile.Bmi));

    public static ConsistencyReport Check(BodyProfile p, double volumeLiters)
    {
        double expected = ExpectedVolumeLiters(p);
        double deviation = volumeLiters / expected - 1;

        var hints = new List<ConsistencyHint>();
        if (deviation > VolumeTolerance)
            hints.Add(new ConsistencyHint(
                $"Замеры и вес плохо согласуются: по обхватам тело получается на {deviation * 100:0}\u00A0% объёмнее, " +
                "чем следует из веса и % жира. Проверьте обхваты (особенно талию и бёдра), вес и % жира."));
        else if (deviation < -VolumeTolerance)
            hints.Add(new ConsistencyHint(
                $"Замеры и вес плохо согласуются: по обхватам тело получается на {-deviation * 100:0}\u00A0% меньше, " +
                "чем следует из веса и % жира. Возможно, обхваты занижены или вес указан с одеждой."));

        hints.AddRange(PlausibilityHints(p));
        return new ConsistencyReport(volumeLiters, expected, deviation, hints);
    }

    /// <summary>Сочетания, которые встречаются редко и чаще говорят об ошибке ввода.</summary>
    public static IEnumerable<ConsistencyHint> PlausibilityHints(BodyProfile p)
    {
        bool male = p.Sex == Sex.Male;
        double fat = p.BodyFatPercent;

        // Незаменимый жир: ~3 % у мужчин, ~12 % у женщин (ACSM)
        double essential = male ? 3 : 12;
        if (fat < essential)
            yield return new ConsistencyHint(
                $"{fat:0}\u00A0% жира — ниже незаменимого минимума (≈\u00A0{essential:0}\u00A0%). Проверьте значение.");

        // Формула ВМС США: расхождение больше двух её стандартных ошибок — повод перепроверить ввод
        if (NavyBodyFat.Estimate(p) is double navy && Math.Abs(navy - fat) > NavyBodyFat.HintThresholdPercent)
            yield return new ConsistencyHint(
                $"По обхватам {(male ? "талии" : "талии, бёдер")} и шеи (формула ВМС США) жира ≈ {navy:0} %, " +
                $"а указано {fat:0} %. Формула ошибается на 3–4 %, расхождение больше — повод перепроверить " +
                "% жира или замеры.");

        double lean = male ? 15 : 22;
        if (p.WaistCm > p.ChestCm && fat < lean)
            yield return new ConsistencyHint(
                $"Талия больше груди при {fat:0}\u00A0% жира — при низком % жира так почти не бывает. " +
                "Проверьте обхват талии или % жира.");

        if (p.WaistToHeight > 0.6 && fat < lean)
            yield return new ConsistencyHint(
                $"Талия — {p.WaistToHeight:0.00} роста: это типично для заметного лишнего жира, а указано {fat:0}\u00A0%.");

        double high = male ? 25 : 33;
        if (p.WaistToHeight < 0.40 && fat > high)
            yield return new ConsistencyHint(
                $"Очень тонкая талия ({p.WaistToHeight:0.00} роста) при {fat:0}\u00A0% жира — проверьте замеры.");

        double athletic = male ? 10 : 18;
        if (p.Bmi > 32 && fat < athletic)
            yield return new ConsistencyHint(
                $"ИМТ {p.Bmi:0} при {fat:0}\u00A0% жира бывает только у очень мускулистых людей. Проверьте вес и % жира.");

        if (p.ThighCm > 0.8 * p.HipsCm)
            yield return new ConsistencyHint(
                "Обхват бедра почти равен обхвату ягодиц. Бедро меряют под ягодичной складкой, " +
                "бёдра — по самой выступающей точке ягодиц.");

        if (p.BicepsCm > 0.45 * p.ChestCm)
            yield return new ConsistencyHint(
                "Плечо слишком большое относительно груди — проверьте место замера (середина плеча).");
    }
}
