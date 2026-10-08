namespace WorkoutCalculator.Web.Services;

/// <summary>
/// Незаконченный расчёт на вкладке «Тренировка»: шаг мастера, поля и итог. Живёт на странице, а не во
/// вкладке, — переключение вкладок его не сбрасывает.
/// </summary>
public sealed class WorkoutDraft
{
    public enum Step { Type, Params, Watch, Result }

    public Step Current { get; set; } = Step.Type;
    public WorkoutForm Form { get; } = new();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Итог последнего расчёта и ввод, по которому он посчитан; null — ещё не считали.</summary>
    public CalculationResult? Result { get; set; }
    public WorkoutInput? Input { get; set; }
}
