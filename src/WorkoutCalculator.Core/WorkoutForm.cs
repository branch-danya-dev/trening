using System.Globalization;

namespace WorkoutCalculator;

/// <summary>Ошибка в поле формы: какое поле (его id в интерфейсе) и что не так.</summary>
public sealed record FormError(string Field, string Message);

/// <summary>Поля одного отрезка дорожки — как их ввёл человек.</summary>
public sealed class SegmentFields
{
    public string? Duration { get; set; }
    public string? Speed { get; set; }
    public string? Incline { get; set; }
}

/// <summary>
/// Форма тренировки — строки, как их ввёл человек, и проверка по шагам мастера: параметры тренировки,
/// затем данные с часов. Ошибка указывает поле, чтобы интерфейс перевёл на него фокус. Время — «30»,
/// «30,5» или «30:23»; числа — с запятой или точкой (<see cref="InputParser"/>).
/// </summary>
public sealed class WorkoutForm
{
    public ActivityType Activity { get; set; }
    public Setting Setting { get; set; }

    // --- Дорожка: один отрезок или несколько, если скорость или уклон менялись ---
    public List<SegmentFields> Segments { get; set; } = [new()];
    public bool Handrails { get; set; }
    public string? DisplayDistanceKm { get; set; }

    // --- Улица ---
    public string? OutdoorDistanceKm { get; set; }
    public string? OutdoorDuration { get; set; }
    public string? OutdoorElevationGainM { get; set; }
    public Terrain Terrain { get; set; }

    // --- Часы ---
    public string? AvgHr { get; set; }
    public string? WatchActiveKcal { get; set; }
    public string? WatchTotalKcal { get; set; }
    public string? WatchDistanceKm { get; set; }

    /// <summary>id полей в интерфейсе: у отрезка — с номером (duration-0, speed-1…).</summary>
    public static class Ids
    {
        public static string Duration(int i) => $"duration-{i}";
        public static string Speed(int i) => $"speed-{i}";
        public static string Incline(int i) => $"incline-{i}";
        public const string DisplayDistance = "display-distance";
        public const string OutdoorDistance = "out-distance";
        public const string OutdoorDuration = "out-duration";
        public const string OutdoorGain = "out-gain";
        public const string AvgHr = "avg-hr";
        public const string WatchActive = "watch-active";
        public const string WatchTotal = "watch-total";
        public const string WatchDistance = "watch-distance";
    }

    /// <summary>Поручни бывают только у ходьбы на дорожке.</summary>
    public bool HandrailsApply => Activity == ActivityType.Walking && Setting == Setting.Treadmill;

    /// <summary>Проверка шага «Параметры»; null — всё верно.</summary>
    public FormError? CheckParams()
    {
        if (Setting == Setting.Treadmill)
        {
            bool many = Segments.Count > 1;
            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                string Name(string field) => many ? $"Отрезок {i + 1}: {field}" : char.ToUpper(field[0]) + field[1..];
                if (Duration(s.Duration, Ids.Duration(i), Name("длительность")) is FormError d) return d;
                if (Required(s.Speed, Ids.Speed(i), Name("скорость"), 0.5, 30) is FormError v) return v;
                if (Optional(s.Incline, Ids.Incline(i), Name("уклон"), -10, 40) is FormError g) return g;
            }
            return Optional(DisplayDistanceKm, Ids.DisplayDistance, "Дистанция на табло", 0.01, 200);
        }
        return Required(OutdoorDistanceKm, Ids.OutdoorDistance, "Дистанция", 0.01, 300)
            ?? Duration(OutdoorDuration, Ids.OutdoorDuration, "Длительность")
            ?? Optional(OutdoorElevationGainM, Ids.OutdoorGain, "Набор высоты", 0, 10000);
    }

    /// <summary>Проверка шага «Часы» (все поля необязательны); null — всё верно.</summary>
    public FormError? CheckWatch() =>
        Optional(AvgHr, Ids.AvgHr, "Средний пульс", 40, 230)
        ?? Optional(WatchActiveKcal, Ids.WatchActive, "Активные ккал", 0, 20000)
        ?? Optional(WatchTotalKcal, Ids.WatchTotal, "Всего ккал", 0, 20000)
        ?? Optional(WatchDistanceKm, Ids.WatchDistance, "Дистанция по часам", 0, 300);

    /// <summary>Ввод для <see cref="EnergyCalculator"/>. Вызывать после успешных проверок обоих шагов.</summary>
    public WorkoutInput ToInput()
    {
        bool treadmill = Setting == Setting.Treadmill;
        return new WorkoutInput
        {
            Activity = Activity,
            Setting = Setting,
            Segments = treadmill
                ? Segments.Select(s => new TreadmillSegment(Minutes(s.Duration), Number(s.Speed), Number(s.Incline))).ToList()
                : [],
            HoldingHandrails = HandrailsApply && Handrails,
            TreadmillDisplayDistanceKm = treadmill ? NumberOrNull(DisplayDistanceKm) : null,
            OutdoorDistanceKm = treadmill ? 0 : Number(OutdoorDistanceKm),
            OutdoorMinutes = treadmill ? 0 : Minutes(OutdoorDuration),
            OutdoorElevationGainM = treadmill ? 0 : Number(OutdoorElevationGainM),
            Terrain = treadmill ? Terrain.Asphalt : Terrain,
            AvgHr = NumberOrNull(AvgHr) is double hr ? (int)Math.Round(hr) : null,
            WatchActiveKcal = NumberOrNull(WatchActiveKcal),
            WatchTotalKcal = NumberOrNull(WatchTotalKcal),
            WatchDistanceKm = NumberOrNull(WatchDistanceKm),
        };
    }

    /// <summary>Поля из сохранённой тренировки — для правки: <see cref="ToInput"/> вернёт те же значения.</summary>
    public static WorkoutForm From(WorkoutInput w)
    {
        bool treadmill = w.Setting == Setting.Treadmill;
        return new WorkoutForm
        {
            Activity = w.Activity,
            Setting = w.Setting,
            Segments = treadmill && w.Segments.Count > 0
                ? w.Segments.Select(s => new SegmentFields
                {
                    Duration = Time(s.Minutes),
                    Speed = Text(s.SpeedKmh),
                    Incline = s.InclinePercent == 0 ? null : Text(s.InclinePercent),
                }).ToList()
                : [new()],
            Handrails = w.HoldingHandrails,
            DisplayDistanceKm = OrNull(w.TreadmillDisplayDistanceKm),
            OutdoorDistanceKm = treadmill ? null : Text(w.OutdoorDistanceKm),
            OutdoorDuration = treadmill ? null : Time(w.OutdoorMinutes),
            OutdoorElevationGainM = treadmill || w.OutdoorElevationGainM == 0 ? null : Text(w.OutdoorElevationGainM),
            Terrain = w.Terrain,
            AvgHr = w.AvgHr?.ToString(CultureInfo.InvariantCulture),
            WatchActiveKcal = OrNull(w.WatchActiveKcal),
            WatchTotalKcal = OrNull(w.WatchTotalKcal),
            WatchDistanceKm = OrNull(w.WatchDistanceKm),
        };

        static string Text(double v) => v.ToString("0.####", CultureInfo.InvariantCulture).Replace('.', ',');
        static string? OrNull(double? v) => v is double x ? Text(x) : null;

        // Целые минуты — «30», иначе «мм:сс» (или «ч:мм:сс»), как на табло
        static string Time(double minutes)
        {
            int seconds = (int)Math.Round(minutes * 60);
            if (seconds % 60 == 0) return (seconds / 60).ToString(CultureInfo.InvariantCulture);
            return seconds >= 3600
                ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
                : $"{seconds / 60}:{seconds % 60:00}";
        }
    }

    /// <summary>Очищает поля тренировки для нового расчёта; тип тренировки остаётся.</summary>
    public void Clear()
    {
        Segments = [new()];
        Handrails = false;
        DisplayDistanceKm = OutdoorDistanceKm = OutdoorDuration = OutdoorElevationGainM = null;
        Terrain = Terrain.Asphalt;
        AvgHr = WatchActiveKcal = WatchTotalKcal = WatchDistanceKm = null;
    }

    private static FormError? Required(string? text, string id, string name, double min, double max)
    {
        if (InputParser.TryParseNumber(text, out double v) && v >= min && v <= max) return null;
        return new FormError(id, string.IsNullOrWhiteSpace(text)
            ? $"{name}: заполните поле."
            : $"{name}: введите число от {Num(min)} до {Num(max)}.");
    }

    private static FormError? Optional(string? text, string id, string name, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (InputParser.TryParseNumber(text, out double v) && v >= min && v <= max) return null;
        return new FormError(id, $"{name}: введите число от {Num(min)} до {Num(max)} или оставьте поле пустым.");
    }

    private static FormError? Duration(string? text, string id, string name)
    {
        if (InputParser.TryParseDuration(text, out _)) return null;
        return new FormError(id, string.IsNullOrWhiteSpace(text)
            ? $"{name}: заполните поле."
            : $"{name}: введите минуты (30 или 30,5) или время (30:23, 1:05:00).");
    }

    private static double Number(string? text) => InputParser.TryParseNumber(text, out double v) ? v : 0;

    private static double? NumberOrNull(string? text) => InputParser.TryParseNumber(text, out double v) ? v : null;

    private static double Minutes(string? text) => InputParser.TryParseDuration(text, out double m) ? m : 0;

    private static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
}
