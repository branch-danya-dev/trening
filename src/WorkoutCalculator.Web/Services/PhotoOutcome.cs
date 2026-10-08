using WorkoutCalculator.BodyModel;

namespace WorkoutCalculator.Web.Services;

/// <summary>Итог фото для модели: сессия, талия и бёдра по фото (null — уровень не найден), осанка и форма после подгонки.</summary>
public sealed record PhotoOutcome(string SessionId, double? WaistCm, double? HipsCm, Posture? Posture, BodyForm? Form);
