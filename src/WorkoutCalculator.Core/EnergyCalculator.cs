namespace WorkoutCalculator;

/// <summary>
/// Расчёт энергозатрат тремя независимыми методами:
///  1. ACSM — по скорости, уклону и времени (метаболические уравнения ACSM).
///  2. Минетти — по уклону и дистанции (Minetti et al., J Appl Physiol, 2002). Учитывает и спуск.
///  3. Кейтел — по пульсу (Keytel et al., J Sports Sci, 2005).
/// Первые два не зависят от пульса, третий отражает реальное усилие,
/// но чувствителен к жаре, кофеину, недосыпу и дрейфу пульса на долгих тренировках.
/// </summary>
public static class EnergyCalculator
{
    /// <summary>Калорийный эквивалент кислорода: ~5 ккал на литр O2.</summary>
    private const double KcalPerLiterO2 = 5.0;

    /// <summary>Поручни снимают часть нагрузки: в исследованиях VO2 падает на 15–30%. Берём 20%.</summary>
    private const double HandrailFactor = 0.80;

    /// <summary>Сопротивление воздуха при беге на улице ≈ +1% уклона на дорожке (Jones &amp; Doust, 1996).</summary>
    private const double OutdoorRunningAirGrade = 0.01;

    /// <summary>Внутреннее представление отрезка: время, путь, набор высоты, уклон (если известен).</summary>
    private readonly record struct Leg(double Minutes, double DistanceM, double GainM, double? Grade);

    public static CalculationResult Calculate(UserProfile p, WorkoutInput w)
    {
        var legs = BuildLegs(w);

        var r = new CalculationResult
        {
            DurationMin = legs.Sum(l => l.Minutes),
            DistanceKm = legs.Sum(l => l.DistanceM) / 1000.0,
            ElevationGainM = legs.Sum(l => l.GainM),
            HrMax = HrMaxTanaka(p.Age),
        };
        r.RestingKcal = BmrMifflin(p) / 1440.0 * r.DurationMin;

        // 1. ACSM
        double acsm = AcsmKcal(p, w, legs, r.Warnings, out double peakVo2);
        r.Methods.Add(new MethodResult("ACSM (скорость + уклон)", acsm, Math.Max(0, acsm - r.RestingKcal)));

        // 2. Минетти — нужен уклон каждого отрезка, поэтому только для дорожки
        if (w.Setting == Setting.Treadmill)
        {
            double net = MinettiNetKcal(p, w, legs);
            r.Methods.Add(new MethodResult("Минетти (уклон + дистанция)", net + r.RestingKcal, net));
        }

        double mechanicalAvg = r.Methods.Average(m => m.TotalKcal);

        // 3. Кейтел (по пульсу)
        if (w.AvgHr is int hr)
        {
            double total = KeytelKcalPerMin(p, hr) * r.DurationMin;
            string name = p.Vo2Max.HasValue ? "Кейтел (пульс + VO2max)" : "Кейтел (по пульсу)";

            // Дрейф пульса: на долгой тренировке пульс растёт при той же нагрузке,
            // и пульсовая оценка уезжает вверх. В этом случае не даём ей тянуть итог.
            bool drift = r.DurationMin > 60 && total > mechanicalAvg * 1.25;
            bool lowHr = hr < 90;
            r.Methods.Add(new MethodResult(name, total, Math.Max(0, total - r.RestingKcal), !drift && !lowHr));

            if (p.RestingHr is int rest && r.HrMax > rest)
                r.HrReserve = (hr - rest) / (r.HrMax - rest);

            if (lowHr)
                r.Warnings.Add("Средний пульс ниже 90: формула по пульсу при такой низкой нагрузке ненадёжна, " +
                               "в оценку она не включена.");

            if (drift)
                r.Warnings.Add("Оценка по пульсу заметно выше механической и в итог не включена. На долгих " +
                               "тренировках пульс постепенно растёт при той же нагрузке (жара, потеря воды), " +
                               "поэтому пульсовые методы, включая режим «в помещении» на часах, завышают.");

            if (total < mechanicalAvg * 0.75 && !w.HoldingHandrails)
                r.Warnings.Add("Оценка по пульсу заметно ниже механической. Возможно, вы держались за поручни " +
                               "или формулы завышают для этого сочетания скорости и уклона.");
        }

        // Проверка реалистичности по VO2max
        if (p.Vo2Max is double vmax && peakVo2 > vmax)
            r.Warnings.Add($"По ACSM пиковая нагрузка ≈ {peakVo2:0} мл/кг/мин, это выше вашего VO2max ({vmax:0}). " +
                           "Долго держать такое невозможно, значит формула здесь завышает. " +
                           "Ориентируйтесь на нижнюю часть диапазона.");
        else if (!p.Vo2Max.HasValue && peakVo2 > 40)
            r.Warnings.Add($"По ACSM пиковая нагрузка ≈ {peakVo2:0} мл/кг/мин, это очень высоко. Укажите VO2max " +
                           "в профиле (Кардиофитнес в «Здоровье»), и калькулятор проверит, реально ли это для вас.");

        if (w.HoldingHandrails)
            r.Warnings.Add("Учтены поручни: механические методы уменьшены на 20% (в реальности 15–30%, " +
                           "зависит от силы опоры). Методу по пульсу поправка не нужна: он видит реальное усилие.");

        if (w.Setting == Setting.Treadmill && w.TreadmillDisplayDistanceKm is double shown && r.DistanceKm > 0)
        {
            double diff = Math.Abs(shown - r.DistanceKm) / r.DistanceKm;
            if (diff > 0.05)
                r.Warnings.Add($"Дистанция на табло ({shown:0.00} км) отличается от расчётной ({r.DistanceKm:0.00} км) " +
                               $"на {diff * 100:0}%. Проверьте время и скорость отрезков.");
        }

        r.Mets = r.EstimateTotalKcal / (p.WeightKg * r.DurationMin / 60.0);

        var unique = r.Warnings.Distinct().ToList();
        r.Warnings.Clear();
        r.Warnings.AddRange(unique);
        return r;
    }

    private static List<Leg> BuildLegs(WorkoutInput w)
    {
        if (w.Setting == Setting.Treadmill)
        {
            return w.Segments.Select(s =>
            {
                double grade = s.InclinePercent / 100.0;
                return new Leg(s.Minutes, s.DistanceM, s.DistanceM * Math.Max(0, grade), grade);
            }).ToList();
        }

        return new List<Leg> { new(w.OutdoorMinutes, w.OutdoorDistanceKm * 1000.0, w.OutdoorElevationGainM, null) };
    }

    /// <summary>
    /// ACSM: VO2 (мл/кг/мин) = h·S + v·S·G + 3,5.
    /// Ходьба: h = 0,1, v = 1,8. Бег: h = 0,2, v = 0,9.
    /// Умножив на время, получаем: O2 = h·Дистанция + v·НаборВысоты + 3,5·Минуты,
    /// поэтому одна формула работает и для отрезков дорожки, и для уличного маршрута.
    /// </summary>
    private static double AcsmKcal(UserProfile p, WorkoutInput w, List<Leg> legs, List<string> warnings, out double peakVo2)
    {
        bool walking = w.Activity == ActivityType.Walking;
        double horizontalCoef = walking ? 0.1 : 0.2; // мл O2 на кг на метр пути
        double verticalCoef = walking ? 1.8 : 0.9;   // мл O2 на кг на метр подъёма
        double factor = MovementFactor(w);

        double o2MlPerKg = 0;
        peakVo2 = 0;

        foreach (var leg in legs)
        {
            CheckAcsmSpeed(w.Activity, leg.DistanceM / leg.Minutes, warnings);

            if (leg.Grade < 0)
                warnings.Add("ACSM не учитывает спуск: отрицательный уклон посчитан как 0%. Метод Минетти спуск учитывает.");

            double vertical = verticalCoef * leg.GainM;
            if (w.Setting == Setting.Outdoor && !walking)
                vertical += verticalCoef * leg.DistanceM * OutdoorRunningAirGrade;

            double movement = (horizontalCoef * leg.DistanceM + vertical) * factor;
            o2MlPerKg += movement + 3.5 * leg.Minutes;
            peakVo2 = Math.Max(peakVo2, movement / leg.Minutes + 3.5);
        }

        return o2MlPerKg * p.WeightKg / 1000.0 * KcalPerLiterO2;
    }

    private static void CheckAcsmSpeed(ActivityType activity, double metersPerMin, List<string> warnings)
    {
        double kmh = metersPerMin * 0.06;
        if (activity == ActivityType.Walking)
        {
            if (metersPerMin < 50)
                warnings.Add($"Ходьба медленнее 3 км/ч ({kmh:0.0} км/ч): формулы на таких скоростях неточны.");
            else if (metersPerMin > 100)
                warnings.Add($"Формула ACSM для ходьбы проверена на 3–6 км/ч; при {kmh:0.0} км/ч точность ниже.");
        }
        else if (metersPerMin < 80)
        {
            warnings.Add($"Бег медленнее 5 км/ч ({kmh:0.0} км/ч) — скорее ходьба; формула для бега здесь завышает.");
        }
    }

    /// <summary>
    /// Минетти: стоимость передвижения в Дж/(кг·м) сверх покоя как полином от уклона i (0,15 = 15%).
    /// Справедлива для уклонов от −45% до +45%, на спуске даёт снижение затрат.
    /// </summary>
    private static double MinettiNetKcal(UserProfile p, WorkoutInput w, List<Leg> legs)
    {
        bool walking = w.Activity == ActivityType.Walking;
        double joules = 0;

        foreach (var leg in legs)
        {
            double i = Math.Clamp(leg.Grade ?? 0, -0.45, 0.45);
            double cost = walking ? MinettiWalk(i) : MinettiRun(i);
            joules += Math.Max(0, cost) * p.WeightKg * leg.DistanceM;
        }

        return joules / 4184.0 * MovementFactor(w);
    }

    private static double MinettiWalk(double i) =>
        280.5 * Math.Pow(i, 5) - 58.7 * Math.Pow(i, 4) - 76.8 * Math.Pow(i, 3) + 51.9 * i * i + 19.6 * i + 2.5;

    private static double MinettiRun(double i) =>
        155.4 * Math.Pow(i, 5) - 30.4 * Math.Pow(i, 4) - 43.3 * Math.Pow(i, 3) + 46.3 * i * i + 19.5 * i + 3.6;

    /// <summary>Кейтел: кДж/мин по пульсу, весу, возрасту и полу (точнее, если известен VO2max).</summary>
    private static double KeytelKcalPerMin(UserProfile p, int hr)
    {
        bool male = p.Sex == Sex.Male;
        double w = p.WeightKg, a = p.Age;

        double kJ = p.Vo2Max is double v
            ? (male ? -95.7735 + 0.634 * hr + 0.404 * v + 0.394 * w + 0.271 * a
                    : -59.3954 + 0.450 * hr + 0.380 * v + 0.103 * w + 0.274 * a)
            : (male ? -55.0969 + 0.6309 * hr + 0.1988 * w + 0.2017 * a
                    : -20.4022 + 0.4472 * hr - 0.1263 * w + 0.0740 * a);

        return Math.Max(0, kJ) / 4.184;
    }

    /// <summary>Поправки к механическим методам: покрытие (улица) и поручни (ходьба на дорожке).</summary>
    private static double MovementFactor(WorkoutInput w)
    {
        double f = 1.0;
        if (w.Setting == Setting.Outdoor)
            f *= TerrainFactor(w.Terrain, w.Activity);
        if (w.Setting == Setting.Treadmill && w.Activity == ActivityType.Walking && w.HoldingHandrails)
            f *= HandrailFactor;
        return f;
    }

    /// <summary>Множители покрытия (Pandolf, 1977; Lejeune, 1998 для песка). На беге покрытие влияет слабее.</summary>
    private static double TerrainFactor(Terrain t, ActivityType a) => (t, a) switch
    {
        (Terrain.Dirt, ActivityType.Walking) => 1.1,
        (Terrain.Dirt, _) => 1.05,
        (Terrain.Grass, ActivityType.Walking) => 1.2,
        (Terrain.Grass, _) => 1.1,
        (Terrain.Sand, ActivityType.Walking) => 1.8,
        (Terrain.Sand, _) => 1.4,
        _ => 1.0,
    };

    /// <summary>Базовый обмен, ккал/сутки (Миффлин — Сан Жеор). Здесь нужны рост, возраст и пол.</summary>
    public static double BmrMifflin(UserProfile p) =>
        10 * p.WeightKg + 6.25 * p.HeightCm - 5 * p.Age + (p.Sex == Sex.Male ? 5 : -161);

    /// <summary>Максимальный пульс по Танаке: точнее, чем 220 − возраст.</summary>
    public static double HrMaxTanaka(int age) => 208 - 0.7 * age;
}
