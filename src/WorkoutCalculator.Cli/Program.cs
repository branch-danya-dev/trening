using WorkoutCalculator;

ConsoleUi.Setup();
Console.WriteLine("Калькулятор тренировок: ходьба и бег на дорожке и на улице");

var profile = ConsoleUi.ReadProfile(ProfileStore.Load());
if (!ProfileStore.Save(profile))
    Console.WriteLine("Не удалось сохранить профиль, в следующий раз его придётся ввести заново.");

do
{
    var workout = ConsoleUi.ReadWorkout();
    var result = EnergyCalculator.Calculate(profile, workout);
    ConsoleUi.PrintReport(workout, result);
}
while (Prompt.YesNo("\nПосчитать ещё одну тренировку?", true));
