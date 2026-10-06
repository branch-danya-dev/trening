using System.Globalization;
using System.Text;

namespace WorkoutCalculator;

public static class ConsoleUi
{
    public static void Setup()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* старые консоли */ }
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU"); } catch { /* режим без локалей */ }
    }

    public static UserProfile ReadProfile(UserProfile? saved)
    {
        Header("Профиль");
        if (saved is not null)
            Console.WriteLine("Enter — оставить значение в скобках, «-» — очистить необязательное поле.");

        var sex = (Sex)Prompt.Choice("Пол", new[] { "Мужской", "Женский" },
                                      saved is null ? (int?)null : (int)saved.Sex);
        int age = (int)Math.Round(Prompt.Number("Возраст, лет", saved?.Age, 10, 100));
        double height = Prompt.Number("Рост, см", saved?.HeightCm, 100, 250);
        double weight = Prompt.Number("Вес, кг", saved?.WeightKg, 30, 300);
        double? restHr = Prompt.OptionalNumber("Пульс покоя, уд/мин (необязательно)", saved?.RestingHr, 30, 120);
        double? vo2 = Prompt.OptionalNumber("VO2max / Кардиофитнес, мл/кг/мин (необязательно)", saved?.Vo2Max, 10, 90);

        return new UserProfile
        {
            Sex = sex,
            Age = age,
            HeightCm = height,
            WeightKg = weight,
            RestingHr = restHr.HasValue ? (int)Math.Round(restHr.Value) : null,
            Vo2Max = vo2,
        };
    }

    public static WorkoutInput ReadWorkout()
    {
        Header("Тренировка");
        int kind = Prompt.Choice("Тип тренировки",
            new[] { "Ходьба на дорожке", "Бег на дорожке", "Ходьба на улице", "Бег на улице" }, 0);

        var activity = kind is 0 or 2 ? ActivityType.Walking : ActivityType.Running;
        var setting = kind < 2 ? Setting.Treadmill : Setting.Outdoor;

        var segments = new List<TreadmillSegment>();
        bool handrails = false;
        double? displayDistance = null;
        double outDistance = 0, outMinutes = 0, outGain = 0;
        var terrain = Terrain.Asphalt;

        if (setting == Setting.Treadmill)
        {
            bool changed = Prompt.YesNo("Скорость или уклон менялись по ходу (например, была разминка)?", false);
            if (!changed)
            {
                // Обычный случай: вся тренировка с одной скоростью и одним уклоном
                segments.Add(ReadSegment("Длительность (мин или мм:сс)", "Скорость, км/ч", "Уклон, %"));
            }
            else
            {
                // Несколько отрезков: минимум два, дальше — по желанию
                do
                {
                    int n = segments.Count + 1;
                    segments.Add(ReadSegment($"Отрезок {n}: длительность (мин или мм:сс)",
                                             "  скорость, км/ч", "  уклон, %"));
                }
                while (segments.Count < 2 || Prompt.YesNo("Добавить ещё отрезок?", false));
            }

            if (activity == ActivityType.Walking)
                handrails = Prompt.YesNo("Держались за поручни?", false);

            displayDistance = Prompt.OptionalNumber("Дистанция на табло дорожки, км (Enter — пропустить)", null, 0.01, 200);
        }
        else
        {
            outDistance = Prompt.Number("Дистанция, км", null, 0.01, 300);
            outMinutes = Prompt.Duration("Длительность (мин или ч:мм:сс)");
            outGain = Prompt.Number("Набор высоты, м", 0, 0, 10000);
            terrain = (Terrain)Prompt.Choice("Покрытие", new[]
            {
                "Асфальт / плитка", "Грунтовка / утоптанная тропа", "Трава / пересечённая местность", "Песок",
            }, 0);
        }

        Header("Данные с часов (Enter — пропустить)");
        double? hr = Prompt.OptionalNumber("Средний пульс, уд/мин", null, 40, 230);
        double? watchActive = Prompt.OptionalNumber("Активные ккал", null, 0, 20000);
        double? watchTotal = Prompt.OptionalNumber("Всего ккал", null, 0, 20000);
        double? watchDistance = Prompt.OptionalNumber("Дистанция, км", null, 0, 300);

        return new WorkoutInput
        {
            Activity = activity,
            Setting = setting,
            Segments = segments,
            HoldingHandrails = handrails,
            TreadmillDisplayDistanceKm = displayDistance,
            OutdoorDistanceKm = outDistance,
            OutdoorMinutes = outMinutes,
            OutdoorElevationGainM = outGain,
            Terrain = terrain,
            AvgHr = hr.HasValue ? (int)Math.Round(hr.Value) : null,
            WatchActiveKcal = watchActive,
            WatchTotalKcal = watchTotal,
            WatchDistanceKm = watchDistance,
        };
    }

    private static TreadmillSegment ReadSegment(string durationLabel, string speedLabel, string inclineLabel)
    {
        double minutes = Prompt.Duration(durationLabel);
        double speed = Prompt.Number(speedLabel, null, 0.5, 30);
        double incline = Prompt.Number(inclineLabel, 0, -10, 40);
        return new TreadmillSegment(minutes, speed, incline);
    }

    public static void PrintReport(WorkoutInput w, CalculationResult r)
    {
        Console.WriteLine();
        Line('═');
        Console.WriteLine($" {Display.WorkoutTitle(w.Activity, w.Setting)}");
        Line('═');

        Row("Длительность", Display.Duration(r.DurationMin));
        Row("Дистанция", $"{r.DistanceKm:0.00} км");
        if (r.ElevationGainM >= 1)
            Row("Набор высоты", $"{r.ElevationGainM:0} м");
        Row("Средняя скорость", $"{r.AvgSpeedKmh:0.0} км/ч, темп {Display.Pace(r.PaceMinPerKm)} /км");
        if (w.AvgHr is int hr)
        {
            string s = $"{hr} уд/мин, {hr / r.HrMax * 100:0}% от макс. ({r.HrMax:0})";
            if (r.HrReserve is double hrr)
                s += $", {hrr * 100:0}% резерва";
            Row("Пульс", s);
        }
        Row("Интенсивность", $"≈ {r.Mets:0.0} МЕТ");

        Console.WriteLine();
        Console.WriteLine($" {"Метод",-30}{"Всего",11}{"Активные",11}");
        Line('─');
        foreach (var m in r.Methods)
        {
            string name = m.InEstimate ? m.Name : m.Name + " *";
            Console.WriteLine($" {name,-30}{m.TotalKcal,11:0}{m.ActiveKcal,11:0}");
        }
        Line('─');
        WriteColored(ConsoleColor.Green,
            $" {"Оценка (среднее)",-30}{r.EstimateTotalKcal,11:0}{r.EstimateActiveKcal,11:0}");
        if (r.UsedMethods.Count > 1)
        {
            string totalRange = $"{r.MinTotalKcal:0}–{r.MaxTotalKcal:0}";
            string activeRange = $"{r.MinActiveKcal:0}–{r.MaxActiveKcal:0}";
            Console.WriteLine($" {"Диапазон",-30}{totalRange,11}{activeRange,11}");
        }
        if (r.UsedMethods.Count < r.Methods.Count)
            Console.WriteLine(" * не входит в оценку, причина в примечаниях.");
        Console.WriteLine($" Активные = сверх базового обмена ({r.RestingKcal:0} ккал за это время), как у Apple.");

        bool hasWatchKcal = w.WatchTotalKcal.HasValue || w.WatchActiveKcal.HasValue;
        if (hasWatchKcal || w.WatchDistanceKm.HasValue)
        {
            Console.WriteLine();
            if (hasWatchKcal)
            {
                Console.WriteLine($" {"Apple Watch",-30}{Num(w.WatchTotalKcal),11}{Num(w.WatchActiveKcal),11}");
                Console.WriteLine($" {"Отклонение часов от оценки",-30}" +
                                  $"{Display.Deviation(w.WatchTotalKcal, r.EstimateTotalKcal),11}" +
                                  $"{Display.Deviation(w.WatchActiveKcal, r.EstimateActiveKcal),11}");
            }
            if (w.WatchDistanceKm is double wd)
                Row("Дистанция по часам", $"{wd:0.00} км ({Display.Deviation(wd, r.DistanceKm)})");
        }

        if (r.Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(" Примечания:");
            foreach (var msg in r.Warnings)
                WriteColored(ConsoleColor.Yellow, $" • {msg}");
        }
        Line('═');
    }

    private static string Num(double? v) => v is double x ? x.ToString("0") : "—";

    private static void Row(string label, string value) => Console.WriteLine($" {label,-22}{value}");

    private static void Line(char ch) => Console.WriteLine(new string(ch, 54));

    private static void Header(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"── {title} " + new string('─', Math.Max(3, 50 - title.Length)));
    }

    private static void WriteColored(ConsoleColor color, string text)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
    }
}

/// <summary>Ввод с проверкой. Принимает и запятую, и точку как десятичный разделитель.</summary>
internal static class Prompt
{
    public static double Number(string label, double? def, double min, double max)
    {
        while (true)
        {
            string s = Ask(label, def?.ToString("0.##"));
            if (s.Length == 0 && def is double d) return d;
            if (InputParser.TryParseNumber(s, out double v) && v >= min && v <= max) return v;
            Error($"Введите число от {min} до {max}.");
        }
    }

    /// <summary>Пусто — значение по умолчанию, «-» — очистить.</summary>
    public static double? OptionalNumber(string label, double? def, double min, double max)
    {
        while (true)
        {
            string s = Ask(label, def?.ToString("0.##"));
            if (s.Length == 0) return def;
            if (s == "-") return null;
            if (InputParser.TryParseNumber(s, out double v) && v >= min && v <= max) return v;
            Error($"Введите число от {min} до {max} или нажмите Enter.");
        }
    }

    public static double Duration(string label)
    {
        while (true)
        {
            string s = Ask(label, null);
            if (InputParser.TryParseDuration(s, out double minutes)) return minutes;
            Error(s.Length == 0 ? "Это поле обязательно." : DurationHelp);
        }
    }

    public static int Choice(string label, string[] options, int? def)
    {
        Console.WriteLine($"{label}:");
        for (int i = 0; i < options.Length; i++)
            Console.WriteLine($"  {i + 1}. {options[i]}");

        while (true)
        {
            string s = Ask("Выбор", def.HasValue ? (def.Value + 1).ToString() : null);
            if (s.Length == 0 && def.HasValue) return def.Value;
            if (int.TryParse(s, out int n) && n >= 1 && n <= options.Length) return n - 1;
            Error($"Введите номер от 1 до {options.Length}.");
        }
    }

    public static bool YesNo(string label, bool def)
    {
        while (true)
        {
            string s = Ask(label + (def ? " (Д/н)" : " (д/Н)"), null).ToLowerInvariant();
            if (s.Length == 0) return def;
            if (s is "д" or "да" or "y" or "yes" or "1") return true;
            if (s is "н" or "нет" or "n" or "no" or "0") return false;
            Error("Ответьте «д» или «н».");
        }
    }

    public static void Error(string message)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  " + message);
        Console.ForegroundColor = old;
    }

    private const string DurationHelp = "Введите минуты (30 или 30,5) или время (30:23, 1:05:00).";

    private static string Ask(string label, string? def)
    {
        Console.Write(def is null ? $"{label}: " : $"{label} [{def}]: ");
        string? line = Console.ReadLine();
        if (line is null) // ввод закрыт (Ctrl+Z / Ctrl+D)
        {
            Console.WriteLine();
            Environment.Exit(0);
        }
        return line.Trim();
    }
}
