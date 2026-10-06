using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace WorkoutCalculator.Desktop;

/// <summary>
/// Пошаговый мастер: Профиль → Тренировка → Параметры → Часы → Результат.
/// Каждый шаг проверяется при нажатии «Далее»; при ошибке фокус переходит на проблемное поле.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] StepNames = { "Профиль", "Тренировка", "Параметры", "Часы", "Результат" };

    /// <summary>Строка отрезка в режиме «скорость или уклон менялись».</summary>
    private sealed record SegmentRow(Grid Root, TextBox Duration, TextBox Speed, TextBox Incline, Button Remove);

    private readonly StackPanel[] _steps;
    private readonly List<SegmentRow> _rows = new();
    private int _step;

    // Данные, собранные на предыдущих шагах
    private UserProfile _profile = new();
    private ActivityType _activity;
    private Setting _setting;
    private List<TreadmillSegment> _segments = new();
    private bool _handrails;
    private double? _displayDistance;
    private double _outDistance, _outMinutes, _outGain;
    private Terrain _terrain;

    public MainWindow()
    {
        InitializeComponent();
        _steps = new[] { StepProfile, StepType, StepParams, StepWatch, StepResult };
        TerrainCombo.SelectedIndex = 0;

        MultiSegmentCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                ApplySegmentMode();
        };

        LoadProfile(ProfileStore.Load());
        GoTo(0);
    }

    // ───────────── Навигация ─────────────

    private void OnNext(object? sender, RoutedEventArgs e)
    {
        HideError();
        switch (_step)
        {
            case 0:
                if (!ReadProfileStep()) return;
                break;
            case 1:
                ReadTypeStep();
                break;
            case 2:
                if (!ReadParamsStep()) return;
                break;
            case 3:
                if (!ReadWatchStep(out var workout)) return;
                ShowResult(workout);
                break;
            case 4:
                ResetWorkout();
                GoTo(1);
                return;
        }
        GoTo(_step + 1);
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        HideError();
        if (_step > 0)
            GoTo(_step - 1);
    }

    private void GoTo(int step)
    {
        _step = step;
        for (int i = 0; i < _steps.Length; i++)
            _steps[i].IsVisible = i == step;

        StepCaption.Text = step < 4 ? $"Шаг {step + 1} из 4 · {StepNames[step]}" : StepNames[step];
        StepProgress.Value = step + 1;
        BackButton.IsVisible = step > 0;
        NextButton.Content = step switch
        {
            3 => "Рассчитать",
            4 => "Новый расчёт",
            _ => "Далее",
        };

        if (step == 2)
        {
            bool treadmill = _setting == Setting.Treadmill;
            TreadmillPanel.IsVisible = treadmill;
            OutdoorPanel.IsVisible = !treadmill;
            HandrailsCheck.IsVisible = _activity == ActivityType.Walking;
        }

        Scroller.ScrollToHome();
        FocusFirstInput(step);
    }

    private void FocusFirstInput(int step)
    {
        TextBox? target = step switch
        {
            0 => string.IsNullOrWhiteSpace(AgeBox.Text) ? AgeBox : null,
            2 when _setting == Setting.Outdoor => OutDistanceBox,
            2 when MultiSegmentCheck.IsChecked == true => _rows.FirstOrDefault()?.Duration,
            2 => DurationBox,
            3 => HrBox,
            _ => null,
        };

        // Фокус после того, как шаг станет видимым
        if (target is TextBox box)
            Dispatcher.UIThread.Post(() => box.Focus());
    }

    // ───────────── Шаги ─────────────

    private void LoadProfile(UserProfile? p)
    {
        if (p is null) return;
        MaleRadio.IsChecked = p.Sex == Sex.Male;
        FemaleRadio.IsChecked = p.Sex == Sex.Female;
        AgeBox.Text = p.Age.ToString();
        HeightBox.Text = Num(p.HeightCm);
        WeightBox.Text = Num(p.WeightKg);
        RestingHrBox.Text = p.RestingHr?.ToString();
        Vo2MaxBox.Text = p.Vo2Max is double v ? Num(v) : null;
    }

    private bool ReadProfileStep()
    {
        if (!ReadRequired(AgeBox, "Возраст", 10, 100, out double age)) return false;
        if (!ReadRequired(HeightBox, "Рост", 100, 250, out double height)) return false;
        if (!ReadRequired(WeightBox, "Вес", 30, 300, out double weight)) return false;
        if (!ReadOptional(RestingHrBox, "Пульс покоя", 30, 120, out double? restHr)) return false;
        if (!ReadOptional(Vo2MaxBox, "VO2max", 10, 90, out double? vo2)) return false;

        _profile = new UserProfile
        {
            Sex = FemaleRadio.IsChecked == true ? Sex.Female : Sex.Male,
            Age = (int)Math.Round(age),
            HeightCm = height,
            WeightKg = weight,
            RestingHr = restHr.HasValue ? (int)Math.Round(restHr.Value) : null,
            Vo2Max = vo2,
        };
        ProfileStore.Save(_profile);
        return true;
    }

    private void ReadTypeStep()
    {
        bool walk = WalkTreadmillRadio.IsChecked == true || WalkOutdoorRadio.IsChecked == true;
        bool treadmill = WalkTreadmillRadio.IsChecked == true || RunTreadmillRadio.IsChecked == true;
        _activity = walk ? ActivityType.Walking : ActivityType.Running;
        _setting = treadmill ? Setting.Treadmill : Setting.Outdoor;
    }

    private bool ReadParamsStep()
    {
        if (_setting == Setting.Treadmill)
        {
            var segments = new List<TreadmillSegment>();
            if (MultiSegmentCheck.IsChecked == true)
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    if (!ReadSegment(row.Duration, row.Speed, row.Incline, $"Отрезок {i + 1}", out var seg))
                        return false;
                    segments.Add(seg);
                }
            }
            else
            {
                if (!ReadSegment(DurationBox, SpeedBox, InclineBox, null, out var seg)) return false;
                segments.Add(seg);
            }

            if (!ReadOptional(DisplayDistanceBox, "Дистанция на табло", 0.01, 200, out _displayDistance)) return false;
            _segments = segments;
            _handrails = _activity == ActivityType.Walking && HandrailsCheck.IsChecked == true;
        }
        else
        {
            if (!ReadRequired(OutDistanceBox, "Дистанция", 0.01, 300, out _outDistance)) return false;
            if (!ReadDuration(OutDurationBox, "Длительность", out _outMinutes)) return false;
            if (!ReadOptionalOrZero(OutGainBox, "Набор высоты", 0, 10000, out _outGain)) return false;
            _terrain = (Terrain)Math.Max(0, TerrainCombo.SelectedIndex);
            _handrails = false;
        }
        return true;
    }

    private bool ReadSegment(TextBox durationBox, TextBox speedBox, TextBox inclineBox, string? prefix,
                             out TreadmillSegment segment)
    {
        segment = null!;
        string Name(string field) => prefix is null ? char.ToUpper(field[0]) + field[1..] : $"{prefix}: {field}";

        if (!ReadDuration(durationBox, Name("длительность"), out double minutes)) return false;
        if (!ReadRequired(speedBox, Name("скорость"), 0.5, 30, out double speed)) return false;
        if (!ReadOptionalOrZero(inclineBox, Name("уклон"), -10, 40, out double incline)) return false;

        segment = new TreadmillSegment(minutes, speed, incline);
        return true;
    }

    private bool ReadWatchStep(out WorkoutInput workout)
    {
        workout = null!;
        if (!ReadOptional(HrBox, "Средний пульс", 40, 230, out double? hr)) return false;
        if (!ReadOptional(WatchActiveBox, "Активные ккал", 0, 20000, out double? active)) return false;
        if (!ReadOptional(WatchTotalBox, "Всего ккал", 0, 20000, out double? total)) return false;
        if (!ReadOptional(WatchDistanceBox, "Дистанция по часам", 0, 300, out double? distance)) return false;

        workout = new WorkoutInput
        {
            Activity = _activity,
            Setting = _setting,
            Segments = _segments,
            HoldingHandrails = _handrails,
            TreadmillDisplayDistanceKm = _displayDistance,
            OutdoorDistanceKm = _outDistance,
            OutdoorMinutes = _outMinutes,
            OutdoorElevationGainM = _outGain,
            Terrain = _terrain,
            AvgHr = hr.HasValue ? (int)Math.Round(hr.Value) : null,
            WatchActiveKcal = active,
            WatchTotalKcal = total,
            WatchDistanceKm = distance,
        };
        return true;
    }

    /// <summary>Очищает данные тренировки для нового расчёта. Профиль и тип тренировки остаются.</summary>
    private void ResetWorkout()
    {
        var boxes = new[]
        {
            DurationBox, SpeedBox, InclineBox, DisplayDistanceBox, OutDistanceBox, OutDurationBox, OutGainBox,
            HrBox, WatchActiveBox, WatchTotalBox, WatchDistanceBox,
        };
        foreach (var box in boxes)
            box.Text = null;

        _rows.Clear();
        SegmentRows.Children.Clear();
        MultiSegmentCheck.IsChecked = false;
        HandrailsCheck.IsChecked = false;
        TerrainCombo.SelectedIndex = 0;
        StepResult.Children.Clear();
    }

    // ───────────── Отрезки ─────────────

    private void ApplySegmentMode()
    {
        bool multi = MultiSegmentCheck.IsChecked == true;
        if (multi && _rows.Count == 0)
        {
            // Уже введённое в одиночные поля становится первым отрезком
            AddSegmentRow(DurationBox.Text, SpeedBox.Text, InclineBox.Text);
            AddSegmentRow();
        }
        SingleSegmentPanel.IsVisible = !multi;
        MultiSegmentPanel.IsVisible = multi;
    }

    private void OnAddSegment(object? sender, RoutedEventArgs e)
    {
        var row = AddSegmentRow();
        Dispatcher.UIThread.Post(() => row.Duration.Focus());
    }

    private SegmentRow AddSegmentRow(string? duration = null, string? speed = null, string? incline = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*,8,40") };
        var durationBox = new TextBox { Watermark = "30 или 30:23", Text = duration };
        var speedBox = new TextBox { Text = speed };
        var inclineBox = new TextBox { Watermark = "0", Text = incline };
        var remove = new Button { Content = "×", FontSize = 16, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                                  HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        ToolTip.SetTip(remove, "Удалить отрезок");

        Grid.SetColumn(durationBox, 0);
        Grid.SetColumn(speedBox, 2);
        Grid.SetColumn(inclineBox, 4);
        Grid.SetColumn(remove, 6);
        grid.Children.Add(durationBox);
        grid.Children.Add(speedBox);
        grid.Children.Add(inclineBox);
        grid.Children.Add(remove);

        var row = new SegmentRow(grid, durationBox, speedBox, inclineBox, remove);
        remove.Click += (_, _) => RemoveSegmentRow(row);

        _rows.Add(row);
        SegmentRows.Children.Add(grid);
        UpdateRemoveButtons();
        return row;
    }

    private void RemoveSegmentRow(SegmentRow row)
    {
        _rows.Remove(row);
        SegmentRows.Children.Remove(row.Root);
        UpdateRemoveButtons();
    }

    private void UpdateRemoveButtons()
    {
        foreach (var row in _rows)
            row.Remove.IsEnabled = _rows.Count > 1;
    }

    // ───────────── Результат ─────────────

    private void ShowResult(WorkoutInput w)
    {
        var r = EnergyCalculator.Calculate(_profile, w);
        var panel = StepResult;
        panel.Children.Clear();

        panel.Children.Add(MakeText(Display.WorkoutTitle(w.Activity, w.Setting), "title"));

        // Главная цифра
        var main = new StackPanel { Spacing = 2 };
        main.Children.Add(MakeText("Активные калории, оценка", "hint"));
        main.Children.Add(new TextBlock
        {
            Text = $"{r.EstimateActiveKcal:0} ккал",
            FontSize = 40,
            FontWeight = FontWeight.SemiBold,
        });
        string sub = $"Всего {r.EstimateTotalKcal:0} ккал";
        if (r.UsedMethods.Count > 1)
            sub += $" · диапазон {r.MinActiveKcal:0}–{r.MaxActiveKcal:0} активных";
        main.Children.Add(MakeText(sub, "subtitle"));
        panel.Children.Add(MakeCard(main, "card"));

        // Сравнение с часами
        string? watch = CompareWithWatch(w, r);
        if (watch is not null)
        {
            var tb = MakeText(watch, null);
            tb.FontWeight = FontWeight.SemiBold;
            panel.Children.Add(tb);
        }

        // Параметры тренировки
        var details = new List<(string Key, string Value)>
        {
            ("Длительность", Display.Duration(r.DurationMin)),
            ("Дистанция", $"{r.DistanceKm:0.00} км" +
                          (w.WatchDistanceKm is double wd
                              ? $" (часы: {wd:0.00}, {Display.Deviation(wd, r.DistanceKm)})"
                              : "")),
        };
        if (r.ElevationGainM >= 1)
            details.Add(("Набор высоты", $"{r.ElevationGainM:0} м"));
        details.Add(("Скорость", $"{r.AvgSpeedKmh:0.0} км/ч, темп {Display.Pace(r.PaceMinPerKm)} /км"));
        if (w.AvgHr is int hr)
        {
            string s = $"{hr} уд/мин, {hr / r.HrMax * 100:0}% от макс. ({r.HrMax:0})";
            if (r.HrReserve is double hrr)
                s += $", {hrr * 100:0}% резерва";
            details.Add(("Пульс", s));
        }
        details.Add(("Интенсивность", $"≈ {r.Mets:0.0} МЕТ"));
        details.Add(("Базовый обмен", $"{r.RestingKcal:0} ккал за это время"));

        panel.Children.Add(MakeText("Тренировка", "label"));
        panel.Children.Add(KeyValueGrid(details));

        // Методы
        panel.Children.Add(MakeText("Как считали", "label"));
        panel.Children.Add(MethodsGrid(r));
        if (r.UsedMethods.Count < r.Methods.Count)
            panel.Children.Add(MakeText("* не входит в оценку, причина в примечаниях", "hint"));
        panel.Children.Add(MakeText("Активные = сверх базового обмена, как у Apple Watch.", "hint"));

        // Примечания
        if (r.Warnings.Count > 0)
        {
            var notes = new StackPanel { Spacing = 6 };
            notes.Children.Add(MakeText("Примечания", "label"));
            foreach (var msg in r.Warnings)
                notes.Children.Add(MakeText("• " + msg, null));
            panel.Children.Add(MakeCard(notes, "notes"));
        }
    }

    private static string? CompareWithWatch(WorkoutInput w, CalculationResult r)
    {
        var parts = new List<string>();
        if (w.WatchActiveKcal is double a)
            parts.Add(Compare("активных", a, r.EstimateActiveKcal));
        if (w.WatchTotalKcal is double t)
            parts.Add(Compare("всего", t, r.EstimateTotalKcal));
        return parts.Count == 0 ? null : "Apple Watch: " + string.Join("; ", parts) + ".";
    }

    private static string Compare(string what, double watch, double estimate)
    {
        double pct = estimate > 0 ? (watch / estimate - 1) * 100 : 0;
        string verdict = Math.Abs(pct) < 10 ? "близко к оценке"
                       : pct < 0 ? $"на {-pct:0}% меньше оценки"
                       : $"на {pct:0}% больше оценки";
        return $"{watch:0} {what}, {verdict}";
    }

    private static Grid KeyValueGrid(IReadOnlyList<(string Key, string Value)> items)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*") };
        for (int i = 0; i < items.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var key = new TextBlock { Text = items[i].Key, Opacity = 0.7, Margin = new Thickness(0, 3) };
            var value = new TextBlock { Text = items[i].Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3) };
            Grid.SetRow(key, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            grid.Children.Add(key);
            grid.Children.Add(value);
        }
        return grid;
    }

    private static Grid MethodsGrid(CalculationResult r)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        AddTableRow(grid, 0, new[] { "Метод", "Всего", "Активные" }, header: true, dim: false);
        for (int i = 0; i < r.Methods.Count; i++)
        {
            var m = r.Methods[i];
            string name = m.InEstimate ? m.Name : m.Name + " *";
            AddTableRow(grid, i + 1, new[] { name, $"{m.TotalKcal:0}", $"{m.ActiveKcal:0}" },
                        header: false, dim: !m.InEstimate);
        }
        return grid;
    }

    private static void AddTableRow(Grid grid, int row, string[] cells, bool header, bool dim)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int c = 0; c < cells.Length; c++)
        {
            var tb = new TextBlock
            {
                Text = cells[c],
                Margin = new Thickness(c == 0 ? 0 : 20, 3, 0, 3),
                TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Right,
                Opacity = header ? 0.6 : dim ? 0.5 : 1.0,
                FontSize = header ? 12 : 14,
            };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, c);
            grid.Children.Add(tb);
        }
    }

    private static TextBlock MakeText(string text, string? cssClass)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (cssClass is not null)
            tb.Classes.Add(cssClass);
        return tb;
    }

    private static Border MakeCard(Control child, string cssClass)
    {
        var border = new Border { Child = child };
        border.Classes.Add(cssClass);
        return border;
    }

    // ───────────── Проверка полей ─────────────

    private bool ReadRequired(TextBox box, string name, double min, double max, out double value)
    {
        if (InputParser.TryParseNumber(box.Text, out value) && value >= min && value <= max)
            return true;
        return Fail(box, string.IsNullOrWhiteSpace(box.Text)
            ? $"{name}: заполните поле."
            : $"{name}: введите число от {Num(min)} до {Num(max)}.");
    }

    private bool ReadOptional(TextBox box, string name, double min, double max, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(box.Text))
            return true;
        if (InputParser.TryParseNumber(box.Text, out double v) && v >= min && v <= max)
        {
            value = v;
            return true;
        }
        return Fail(box, $"{name}: введите число от {Num(min)} до {Num(max)} или оставьте поле пустым.");
    }

    private bool ReadOptionalOrZero(TextBox box, string name, double min, double max, out double value)
    {
        bool ok = ReadOptional(box, name, min, max, out double? v);
        value = v ?? 0;
        return ok;
    }

    private bool ReadDuration(TextBox box, string name, out double minutes)
    {
        if (InputParser.TryParseDuration(box.Text, out minutes))
            return true;
        return Fail(box, string.IsNullOrWhiteSpace(box.Text)
            ? $"{name}: заполните поле."
            : $"{name}: введите минуты (30 или 30,5) или время (30:23, 1:05:00).");
    }

    private bool Fail(TextBox box, string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
        box.Focus();
        return false;
    }

    private void HideError() => ErrorText.IsVisible = false;

    private static string Num(double v) => v.ToString("0.##");
}
