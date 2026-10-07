using System.Text.Json.Serialization;

namespace WorkoutCalculator.BodyModel;

/// <summary>
/// Осанка — отклонения от осанки модели MakeHuman, градусы. Плюс: таз наклонён вперёд, прогиб в пояснице
/// глубже, верх спины круглее, плечи сведены вперёд. Человек при этом стоит прямо: наклон таза уравновешивает
/// поясница (поэтому он углубляет прогиб); прогиб меняет только дугу поясницы — таз и грудь на месте;
/// сутулость наполовину уравновешивает грудопоясничный отдел, остальное — шея (взгляд вперёд, голова
/// уходит вперёд); руки висят вертикально. Подробно — в PostureRig.
/// </summary>
public sealed record Posture(double PelvicTilt = 0, double Lordosis = 0, double Kyphosis = 0, double ShouldersForward = 0)
{
    public static Posture Neutral { get; } = new();

    [JsonIgnore]
    public bool IsNeutral => this == Neutral;

    /// <summary>Пределы, градусы: дальше скиннинг ломает форму, а у живых людей такое редкость.</summary>
    public const double MinPelvicTilt = -10, MaxPelvicTilt = 15;
    public const double MinLordosis = -15, MaxLordosis = 20;
    public const double MinKyphosis = -15, MaxKyphosis = 25;
    public const double MinShouldersForward = -10, MaxShouldersForward = 20;

    public Posture Clamped() => new(
        Math.Clamp(PelvicTilt, MinPelvicTilt, MaxPelvicTilt),
        Math.Clamp(Lordosis, MinLordosis, MaxLordosis),
        Math.Clamp(Kyphosis, MinKyphosis, MaxKyphosis),
        Math.Clamp(ShouldersForward, MinShouldersForward, MaxShouldersForward));
}

/// <summary>
/// Форма тела при тех же обхватах — таргеты MakeHuman от −1 до 1: живот вперёд, объём ягодиц, глубина
/// корпуса (спереди назад), V-силуэт (плечи шире, талия уже). Обхваты подгонка возвращает к замерам,
/// поэтому меняется распределение: например, при том же обхвате талии живот выступает вперёд, а бока уже.
/// </summary>
public sealed record BodyForm(double Stomach = 0, double Buttocks = 0, double TorsoDepth = 0, double VShape = 0)
{
    public static BodyForm Neutral { get; } = new();

    [JsonIgnore]
    public bool IsNeutral => this == Neutral;

    public const double Min = -1, Max = 1;

    public BodyForm Clamped() => new(
        Math.Clamp(Stomach, Min, Max),
        Math.Clamp(Buttocks, Min, Max),
        Math.Clamp(TorsoDepth, Min, Max),
        Math.Clamp(VShape, Min, Max));
}
