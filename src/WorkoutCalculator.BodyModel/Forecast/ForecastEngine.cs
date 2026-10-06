using C = WorkoutCalculator.BodyModel.Forecast.ForecastConstants;

namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>
/// Прогноз изменения тела по неделям. Каждую неделю:
/// 1) БМР по Миффлину — Сан Жеору от текущего веса (снижение расхода при похудении);
/// 2) расход = БМР × коэффициент активности + активные ккал тренировок (кардио — через EnergyCalculator);
/// 3) баланс = потребление − расход;
/// 4) дефицит — потеря жира (и 25 % из безжировой массы без силовых), профицит — прирост
///    безжировой массы до потолка по стажу (только с силовыми), остальное — жир.
/// В конце изменение жировой и безжировой массы раскладывается по регионам и переводится в обхваты.
/// </summary>
public static class ForecastEngine
{
    public static ForecastResult Run(BodyProfile start, ForecastInput input)
    {
        int weeks = Math.Max(0, input.Weeks);
        var warnings = new List<string>();
        double essentialFat = C.EssentialFatPercent(start.Sex) / 100;

        double fat = start.FatMassKg, lean = start.LeanMassKg;
        var history = new List<ForecastWeek>();
        double maxWeeklyLoss = 0;
        bool hitFatFloor = false;
        double cardioStart = 0, strengthStart = 0;

        for (int week = 0; week <= weeks; week++)
        {
            double weight = fat + lean;
            var (bmr, expenditure, cardio, strength) = Expenditure(start, weight, input);
            double balance = input.IntakeKcalPerDay - expenditure;
            history.Add(new ForecastWeek(week, weight, fat, lean, bmr, expenditure, balance));
            if (week == 0)
            {
                cardioStart = cardio;
                strengthStart = strength;
            }
            if (week == weeks) break;

            double weekly = balance * 7;
            double dFat, dLean;
            if (weekly < 0)
            {
                double leanShare = input.StrengthTraining ? C.LeanShareOfLossWithStrength : C.LeanShareOfLossWithoutStrength;
                double kcalPerKg = leanShare * C.LeanKcalPerKg + (1 - leanShare) * C.FatKcalPerKg;
                double loss = weekly / kcalPerKg; // отрицательное
                dFat = (1 - leanShare) * loss;
                dLean = leanShare * loss;

                // Жир не уходит ниже незаменимого: остаток дефицита покрывает безжировая масса
                double minFat = essentialFat * (fat + lean + dFat + dLean);
                if (fat + dFat < minFat)
                {
                    double allowed = Math.Min(0, minFat - fat);
                    double rest = weekly - (allowed * C.FatKcalPerKg + dLean * C.LeanKcalPerKg);
                    dFat = allowed;
                    dLean += rest / C.LeanKcalPerKg;
                    hitFatFloor = true;
                }
            }
            else
            {
                double ceiling = input.StrengthTraining
                    ? C.MonthlyLeanGainCeilingPercent(start.Sex, input.Experience) / 100 * weight / C.WeeksPerMonth
                    : 0;
                dLean = Math.Min(ceiling, weekly / C.LeanKcalPerKg);
                dFat = (weekly - dLean * C.LeanKcalPerKg) / C.FatKcalPerKg;
            }

            maxWeeklyLoss = Math.Max(maxWeeklyLoss, -(dFat + dLean) / weight);
            fat += dFat;
            lean += dLean;
        }

        var end = ApplyToGirths(start, history[^1].FatMassKg - start.FatMassKg, history[^1].LeanMassKg - start.LeanMassKg);
        end.WeightKg = history[^1].WeightKg;
        end.BodyFatPercent = history[^1].FatPercent;

        AddWarnings(warnings, start, input, history, maxWeeklyLoss, hitFatFloor);

        return new ForecastResult
        {
            Start = start.Clone(),
            End = end,
            Weeks = history,
            Warnings = warnings,
            CardioKcalPerSession = cardioStart,
            StrengthKcalPerSession = strengthStart,
        };
    }

    /// <summary>Расход в сутки при данном весе: БМР × коэффициент + тренировки в среднем за день.</summary>
    public static (double Bmr, double Total, double CardioPerSession, double StrengthPerSession) Expenditure(
        BodyProfile p, double weightKg, ForecastInput input)
    {
        var user = p.ToUserProfile();
        user.WeightKg = weightKg;
        double bmr = EnergyCalculator.BmrMifflin(user);

        // Только активные ккал: базовый обмен за время тренировки уже входит в БМР × коэффициент
        double cardio = input.Cardio is not null && input.CardioPerWeek > 0
            ? EnergyCalculator.Calculate(user, input.Cardio).EstimateActiveKcal
            : 0;
        double strength = input.StrengthTraining && input.StrengthPerWeek > 0
            ? (C.StrengthMet - 1) * weightKg * C.StrengthHours
            : 0;

        double training = (cardio * Math.Max(0, input.CardioPerWeek) + strength * Math.Max(0, input.StrengthPerWeek)) / 7;
        return (bmr, bmr * input.ActivityFactor + training, cardio, strength);
    }

    /// <summary>Новые обхваты: изменение массы → объём по регионам → обхват (цилиндр с той же длиной).</summary>
    public static BodyProfile ApplyToGirths(BodyProfile start, double fatChangeKg, double leanChangeKg)
    {
        var end = start.Clone();
        end.NeckCm = start.EffectiveNeckCm;
        var lengths = GirthSensitivity.EffectiveLengths(start);
        var fatShares = C.FatShares(start.Sex);

        foreach (Region region in Enum.GetValues<Region>())
        {
            if (GirthSensitivity.GirthOf(region) is not Girth g) continue;
            double liters = fatShares[region] * fatChangeKg / C.FatDensityKgPerL
                          + C.LeanShares[region] * leanChangeKg / C.LeanDensityKgPerL;
            end.SetGirth(g, GirthSensitivity.NewGirthCm(start.GetGirth(g), liters / 1000, lengths[g]));
        }
        return end;
    }

    private static void AddWarnings(List<string> warnings, BodyProfile start, ForecastInput input,
        List<ForecastWeek> history, double maxWeeklyLoss, bool hitFatFloor)
    {
        var first = history[0];
        var last = history[^1];

        if (maxWeeklyLoss > C.MaxWeeklyLossFraction)
            warnings.Add($"Темп похудения до {maxWeeklyLoss * 100:0.0}\u00A0% веса в неделю — больше 1 %. " +
                         "Такой дефицит трудно выдержать, и при нём теряются мышцы даже с силовыми тренировками.");

        if (input.IntakeKcalPerDay < first.BmrKcal)
            warnings.Add($"Потребление ({input.IntakeKcalPerDay:0} ккал) ниже базового обмена " +
                         $"(≈\u00A0{first.BmrKcal:0} ккал). Долго так питаться не стоит.");

        if (hitFatFloor)
            warnings.Add("Жир дошёл до незаменимого минимума — дальше дефицит покрывается только мышцами. " +
                         "Это опасно, цель стоит пересмотреть.");

        if (first.BalanceKcalPerDay > 0 && !input.StrengthTraining)
            warnings.Add("Без силовых тренировок профицит почти целиком уходит в жир.");

        if (input.TargetWeightKg is double target && input.Weeks > 0)
        {
            double needed = target - start.WeightKg;
            if (needed < 0)
            {
                double rate = -needed / input.Weeks / start.WeightKg;
                if (rate > C.MaxWeeklyLossFraction)
                    warnings.Add($"Цель {target:0.#} кг за {input.Weeks} нед. требует худеть на {rate * 100:0.0}\u00A0% веса " +
                                 "в неделю — быстрее реалистичного 1 %. Увеличьте срок.");
                else if (last.WeightKg > target + 0.5)
                    warnings.Add($"При этом плане к сроку будет ≈\u00A0{last.WeightKg:0.#} кг — до цели {target:0.#} кг " +
                                 $"не хватит {last.WeightKg - target:0.#} кг.");
            }
            else if (needed > 0)
            {
                double leanCap = C.MonthlyLeanGainCeilingPercent(start.Sex, input.Experience) / 100
                                 * start.WeightKg / C.WeeksPerMonth * input.Weeks;
                if (needed > leanCap)
                    warnings.Add($"Набрать {needed:0.#} кг за {input.Weeks} нед. одними мышцами нереально: " +
                                 $"потолок для вашего стажа ≈\u00A0{leanCap:0.#} кг, остальное будет жир.");
                if (last.WeightKg < target - 0.5)
                    warnings.Add($"При этом плане к сроку будет ≈\u00A0{last.WeightKg:0.#} кг — до цели {target:0.#} кг " +
                                 $"не хватит {target - last.WeightKg:0.#} кг.");
            }
        }
    }
}
