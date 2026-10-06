namespace WorkoutCalculator;

public enum Sex { Male, Female }

public enum ActivityType { Walking, Running }

public enum Setting { Treadmill, Outdoor }

/// <summary>Покрытие на улице. Влияет на энергозатраты (песок — почти вдвое для ходьбы).</summary>
public enum Terrain { Asphalt, Dirt, Grass, Sand }

/// <summary>Параметры человека. Сохраняются между запусками.</summary>
public sealed class UserProfile
{
    public Sex Sex { get; set; }
    public int Age { get; set; }
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }

    /// <summary>Пульс покоя (Здоровье → Сердце → Пульс в покое). Необязательно.</summary>
    public int? RestingHr { get; set; }

    /// <summary>VO2max, мл/кг/мин (Здоровье → Сердце → Кардиофитнес). Необязательно.</summary>
    public double? Vo2Max { get; set; }
}

/// <summary>Отрезок на дорожке с постоянной скоростью и уклоном.</summary>
public sealed record TreadmillSegment(double Minutes, double SpeedKmh, double InclinePercent)
{
    public double DistanceM => SpeedKmh * 1000.0 / 60.0 * Minutes;
}

public sealed class WorkoutInput
{
    public ActivityType Activity { get; init; }
    public Setting Setting { get; init; }

    // --- Дорожка ---
    public List<TreadmillSegment> Segments { get; init; } = new();
    public bool HoldingHandrails { get; init; }
    /// <summary>Дистанция с табло дорожки — для сверки с расчётной.</summary>
    public double? TreadmillDisplayDistanceKm { get; init; }

    // --- Улица ---
    public double OutdoorDistanceKm { get; init; }
    public double OutdoorMinutes { get; init; }
    public double OutdoorElevationGainM { get; init; }
    public Terrain Terrain { get; init; } = Terrain.Asphalt;

    // --- Данные с часов ---
    public int? AvgHr { get; init; }
    public double? WatchActiveKcal { get; init; }
    public double? WatchTotalKcal { get; init; }
    public double? WatchDistanceKm { get; init; }
}

/// <param name="InEstimate">false — метод показан, но не входит в итоговую оценку (явно недостоверен).</param>
public sealed record MethodResult(string Name, double TotalKcal, double ActiveKcal, bool InEstimate = true);

public sealed class CalculationResult
{
    public double DurationMin { get; set; }
    public double DistanceKm { get; set; }
    public double ElevationGainM { get; set; }
    /// <summary>Базовый обмен за время тренировки (Миффлин — Сан Жеор).</summary>
    public double RestingKcal { get; set; }
    public double HrMax { get; set; }
    /// <summary>Доля резерва пульса (Карвонен), если известен пульс покоя.</summary>
    public double? HrReserve { get; set; }
    public double Mets { get; set; }

    public List<MethodResult> Methods { get; } = new();
    public List<string> Warnings { get; } = new();

    public double AvgSpeedKmh => DurationMin > 0 ? DistanceKm / (DurationMin / 60.0) : 0;
    public double PaceMinPerKm => DistanceKm > 0 ? DurationMin / DistanceKm : 0;

    /// <summary>Методы, входящие в итоговую оценку.</summary>
    public IReadOnlyList<MethodResult> UsedMethods => Methods.Where(m => m.InEstimate).ToList();

    public double EstimateTotalKcal => UsedMethods.Average(m => m.TotalKcal);
    public double EstimateActiveKcal => UsedMethods.Average(m => m.ActiveKcal);
    public double MinTotalKcal => UsedMethods.Min(m => m.TotalKcal);
    public double MaxTotalKcal => UsedMethods.Max(m => m.TotalKcal);
    public double MinActiveKcal => UsedMethods.Min(m => m.ActiveKcal);
    public double MaxActiveKcal => UsedMethods.Max(m => m.ActiveKcal);
}
