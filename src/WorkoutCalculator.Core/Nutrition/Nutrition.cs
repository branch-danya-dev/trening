using System.Collections.Immutable;
using System.Globalization;
using WorkoutCalculator.Activity;

namespace WorkoutCalculator.Nutrition;

public enum NutritionBasis { Per100Gram, Serving }
public enum MealType { Breakfast, Lunch, Dinner, Snack, Custom }
public enum NutritionCoverage { NotRecorded, Partial, Complete }

/// <summary>Immutable label values. Null macros mean unknown, zero means a known zero.</summary>
public sealed record NutritionReference(string Name, NutritionBasis BasisType, decimal ReferenceGrams,
    decimal CaloriesKcal, decimal? ProteinGrams = null, decimal? FatGrams = null, decimal? CarbsGrams = null,
    string Source = "Manual", int SchemaVersion = 1)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 200 || !Enum.IsDefined(BasisType) || Source != "Manual" || SchemaVersion != 1)
            throw new ArgumentException("Проверьте название, источник и версию продукта.");
        NutritionRules.Number(ReferenceGrams, .01m, 10000m);
        if (BasisType == NutritionBasis.Per100Gram && ReferenceGrams != 100m) throw new ArgumentException("База на 100 г должна равняться 100 г.");
        NutritionRules.Number(CaloriesKcal, 0, 100000);
        foreach (var macro in new[] { ProteinGrams, FatGrams, CarbsGrams }) NutritionRules.Number(macro, 0, ReferenceGrams);
        if ((ProteinGrams ?? 0) + (FatGrams ?? 0) + (CarbsGrams ?? 0) > ReferenceGrams * 1.05m || CaloriesKcal > ReferenceGrams * 10m)
            throw new ArgumentException("КБЖУ несоразмерны указанной массе порции.");
    }
    public bool LabelMismatch => ProteinGrams is { } p && FatGrams is { } f && CarbsGrams is { } c &&
        Math.Abs(4m * p + 9m * f + 4m * c - CaloriesKcal) > Math.Max(1m, CaloriesKcal * .05m);
}

public sealed record NutritionTotals(decimal? CaloriesKcal, decimal? ProteinGrams, decimal? FatGrams, decimal? CarbsGrams);
public sealed record FoodConsumptionEntry(string Id, NutritionReference Reference, decimal ActualGrams, string? Note = null)
{
    public void Validate()
    {
        ActivityRules.Id(Id);
        if (Reference is null || Note?.Length > 1000) throw new ArgumentException("Некорректная запись продукта.");
        Reference.Validate(); NutritionRules.Number(ActualGrams, .01m, 10000m);
    }
    public NutritionTotals Calculate()
    {
        Validate();
        // Multiply before dividing. Round each entry once to six decimal places; never round UI values back into storage.
        decimal? Scale(decimal? n) => n is { } v ? decimal.Round(v * ActualGrams / Reference.ReferenceGrams, 6, MidpointRounding.ToEven) : null;
        return new(Scale(Reference.CaloriesKcal), Scale(Reference.ProteinGrams), Scale(Reference.FatGrams), Scale(Reference.CarbsGrams));
    }
}

public sealed record MealPlanSlot(string Id, MealType Type, string Label, TimeOnly? Time = null)
{
    public static ImmutableArray<MealPlanSlot> Defaults => [new("breakfast", MealType.Breakfast, "Завтрак", new(8,0)), new("lunch", MealType.Lunch, "Обед", new(13,0)), new("dinner", MealType.Dinner, "Ужин", new(19,0)), new("snack", MealType.Snack, "Перекус")];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 || !Enum.IsDefined(Type) || string.IsNullOrWhiteSpace(Label) || Label.Length > 100)
            throw new ArgumentException("Некорректный пункт плана питания.");
    }
}

public sealed record MealEvent(string Id, string DayId, MealType Type, string? Label, ImmutableArray<FoodConsumptionEntry> Entries,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? PlannedSlotId = null, TimeOnly? ActualTime = null, int SchemaVersion = 1)
{
    public void Validate()
    {
        ActivityRules.Id(Id); ActivityRules.Id(DayId);
        if (!Enum.IsDefined(Type) || SchemaVersion != 1 || Label?.Length > 100 || Type == MealType.Custom && string.IsNullOrWhiteSpace(Label) ||
            PlannedSlotId?.Length > 100 || CreatedAt == default || UpdatedAt < CreatedAt || Entries.IsDefaultOrEmpty || Entries.Length > 100)
            throw new ArgumentException("Приём пищи должен содержать хотя бы один продукт.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Entries) { if (e is null || !ids.Add(e.Id)) throw new ArgumentException("Повторяющийся продукт."); e.Validate(); }
    }
}

/// <summary>A view/snapshot of an explicitly saved Forecast plan; never a second editable target store.</summary>
public sealed record NutritionTarget(decimal CaloriesKcalPerDay, decimal? ProteinGramsPerDay, decimal? FatGramsPerDay, decimal? CarbsGramsPerDay)
{
    public void Validate()
    {
        NutritionRules.Number(CaloriesKcalPerDay, .01m, 100000m);
        foreach (var n in new[] { ProteinGramsPerDay, FatGramsPerDay, CarbsGramsPerDay }) NutritionRules.Number(n, 0, 10000);
    }
}

public sealed record ClosedNutritionSummary(NutritionTotals Totals, int MealCount, int NutritionEntryCount,
    NutritionCoverage Coverage, bool NoFoodConfirmed, ImmutableArray<string> MealIds, NutritionTarget? Target,
    int LabelMismatchCount, int SchemaVersion = 1, string ModelVersion = "nutrition-decimal-1")
{
    public bool Eligible => Coverage == NutritionCoverage.Complete;
}

public static class NutritionSummary
{
    public static ClosedNutritionSummary Build(ImmutableArray<MealEvent> meals, bool complete = false, bool noFood = false, NutritionTarget? target = null)
    {
        if (meals.IsDefault || meals.Length > 100) throw new ArgumentException("Некорректная коллекция питания.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meal in meals)
        {
            if (meal is null || !ids.Add(meal.Id)) throw new ArgumentException("Повторяющийся приём пищи.");
            meal.Validate();
            foreach (var entry in meal.Entries) if (!ids.Add(entry.Id)) throw new ArgumentException("Повторяющийся продукт.");
        }
        if (meals.Select(m => m.DayId).Distinct().Count() > 1) throw new ArgumentException("Сводка содержит разные дни.");
        target?.Validate();
        if (noFood && (!complete || !meals.IsEmpty) || complete && meals.IsEmpty && !noFood)
            throw new ArgumentException("Пустые записи не означают голодание. Подтвердите отдельно, что еды не было.");
        var entries = meals.SelectMany(m => m.Entries).ToArray();
        var values = entries.Select(e => e.Calculate()).ToArray();
        decimal? Sum(Func<NutritionTotals, decimal?> get) => values.Length == 0 ? noFood ? 0m : null : values.All(v => get(v).HasValue) ? values.Sum(v => get(v)!.Value) : null;
        return new(new(Sum(v => v.CaloriesKcal), Sum(v => v.ProteinGrams), Sum(v => v.FatGrams), Sum(v => v.CarbsGrams)),
            meals.Length, entries.Length, complete ? NutritionCoverage.Complete : entries.Length > 0 ? NutritionCoverage.Partial : NutritionCoverage.NotRecorded,
            noFood, meals.Select(m => m.Id).Order(StringComparer.Ordinal).ToImmutableArray(), target, entries.Count(e => e.Reference.LabelMismatch));
    }
    public static bool Matches(ClosedNutritionSummary a, ClosedNutritionSummary b) => a with { MealIds = b.MealIds } == b && a.MealIds.SequenceEqual(b.MealIds);
}

/// <summary>Future catalog providers supply values which are copied into the consumption entry.</summary>
public interface INutritionReferenceProvider { NutritionReference? Find(string referenceId); }

public static class NutritionRules
{
    public static void Number(decimal? n, decimal min, decimal max)
    {
        if (n is { } v && (v < min || v > max || decimal.Round(v, 6) != v)) throw new ArgumentException($"Число от {min} до {max}, до 6 знаков после запятой.");
    }
    public static decimal? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException("Введите число с точкой или запятой.");
        return value;
    }
}
