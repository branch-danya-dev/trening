using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Body3D.Services;

/// <param name="Slot">Цвет на графике и в таблице: закреплён за гипотезой, а не за местом в списке.</param>
public sealed class SavedHypothesis
{
    public string Name { get; set; } = "";
    public int Slot { get; set; }
    public StoredHypothesis Plan { get; set; } = new();
}

/// <summary>
/// Гипотезы под своими названиями, не больше <see cref="Max"/>. Одна из них выбрана: её правит редактор
/// и показывает 3D-вид. Изменения сохраняются сразу, отдельной кнопки «Сохранить» нет.
/// </summary>
public sealed class StoredHypotheses
{
    public const int Max = 5;
    public const int MaxNameLength = 40;

    /// <summary>Слот выбранной гипотезы.</summary>
    public int Selected { get; set; }

    public List<SavedHypothesis> Items { get; set; } = [];

    [JsonIgnore]
    public SavedHypothesis Current => Items.FirstOrDefault(h => h.Slot == Selected) ?? Items[0];

    [JsonIgnore]
    public bool CanAdd => Items.Count < Max;

    public static StoredHypotheses Of(StoredHypothesis plan) => new()
    {
        Items = [new SavedHypothesis { Name = DefaultName(1), Slot = 0, Plan = plan }],
    };

    public void Select(int slot)
    {
        if (Items.Any(h => h.Slot == slot)) Selected = slot;
    }

    /// <summary>Новая гипотеза — копия выбранной; она же становится выбранной.</summary>
    public void AddCopy()
    {
        if (!CanAdd) return;
        int slot = Enumerable.Range(0, Max).First(s => Items.All(h => h.Slot != s));
        string name = Enumerable.Range(1, Max + 1).Select(DefaultName).First(n => !Taken(n, except: null));
        Items.Add(new SavedHypothesis { Name = name, Slot = slot, Plan = Current.Plan.Clone() });
        Selected = slot;
    }

    /// <summary>Удаляет гипотезу, кроме последней оставшейся. Выбранной становится следующая в списке.</summary>
    public void Remove(int slot)
    {
        int i = Items.FindIndex(h => h.Slot == slot);
        if (i < 0 || Items.Count == 1) return;
        Items.RemoveAt(i);
        if (Selected == slot) Selected = Items[Math.Min(i, Items.Count - 1)].Slot;
    }

    /// <summary>Пустое название не меняет прежнее; повтор чужого получает номер: «Дефицит 2».</summary>
    public void Rename(int slot, string? name)
    {
        var h = Items.FirstOrDefault(x => x.Slot == slot);
        string clean = Clean(name);
        if (h is null || clean.Length == 0) return;
        h.Name = Unique(clean, h);
    }

    /// <summary>
    /// Прочитанное из браузера — в порядок: не больше <see cref="Max"/> гипотез, у каждой свой слот
    /// 0…Max−1 и непустое неповторяющееся название, выбранная существует. Пустой список — null.
    /// </summary>
    public StoredHypotheses? Normalize()
    {
        var items = (Items ?? []).Where(h => h is not null).Take(Max).ToList();
        if (items.Count == 0) return null;

        // Сначала оставляем верные слоты (цвета не меняются), потом раздаём свободные остальным
        var used = new HashSet<int>();
        var lost = items.Where(h => h.Slot is < 0 or >= Max || !used.Add(h.Slot)).ToList();
        foreach (var h in lost)
        {
            h.Slot = Enumerable.Range(0, Max).First(s => !used.Contains(s));
            used.Add(h.Slot);
        }

        // Названия — по порядку: при повторе номер получает более поздняя гипотеза
        Items = items;
        var names = items.Select(h => Clean(h.Name)).ToList();
        items.ForEach(h => h.Name = "");
        for (int i = 0; i < items.Count; i++)
        {
            items[i].Plan ??= new StoredHypothesis();
            items[i].Name = Unique(names[i].Length > 0 ? names[i] : DefaultName(i + 1), items[i]);
        }
        if (items.All(h => h.Slot != Selected)) Selected = items[0].Slot;
        return this;
    }

    private static string DefaultName(int n) => $"План {n}";

    private static string Clean(string? name)
    {
        string s = (name ?? "").Trim();
        return s.Length > MaxNameLength ? s[..MaxNameLength].TrimEnd() : s;
    }

    private bool Taken(string name, SavedHypothesis? except) =>
        Items.Any(h => h != except && string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

    private string Unique(string name, SavedHypothesis self)
    {
        if (!Taken(name, self)) return name;
        string stem = name.Length > MaxNameLength - 2 ? name[..(MaxNameLength - 2)].TrimEnd() : name;
        return Enumerable.Range(2, Max).Select(n => $"{stem} {n}").First(n => !Taken(n, self));
    }
}

/// <summary>Гипотеза и её прогноз от текущего профиля.</summary>
public sealed record HypothesisForecast(SavedHypothesis Hypothesis, ForecastResult Forecast)
{
    /// <summary>Средний баланс за срок: последняя неделя — уже итог, в среднее не входит.</summary>
    public double AverageBalanceKcalPerDay =>
        Forecast.Weeks.Take(Forecast.Weeks.Count - 1).DefaultIfEmpty(Forecast.Weeks[0]).Average(w => w.BalanceKcalPerDay);
}
