using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.Data;
using WorkoutCalculator.Data.Legacy;

namespace WorkoutCalculator.Tests.Data;

/// <summary>Перенос старых данных (строки localStorage прежней версии как есть) и резервная копия без потерь.</summary>
public class MigrationAndBackupTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(3));

    // Так прежняя версия писала профиль (StorageJson: PascalCase, перечисления числами)
    private const string OldProfile = """
        {"Sex":0,"Age":34,"HeightCm":178,"WeightKg":81.5,"BodyFatPercent":17,"ChestCm":101,"WaistCm":86,"HipsCm":99,
         "BicepsCm":34,"ThighCm":58,"NeckCm":39,"CalfCm":null,"WristCm":null,"RestingHr":58,"Vo2Max":44.5,
         "Posture":{"PelvicTilt":4,"Lordosis":6,"Kyphosis":0,"ShouldersForward":0},"Form":null}
        """;

    private const string OldWeights = """
        [{"Date":"2026-09-20","WeightKg":83.1},{"Date":"2026-10-01","WeightKg":82.4}]
        """;

    private const string OldHypotheses = """
        {"Selected":0,"Items":[
          {"Name":"План 1","Slot":0,"Plan":{"Weeks":12,"IntakeKcalPerDay":2300,"ActivityFactor":1.3,"CardioKind":0,
            "StartDate":"2026-09-15","StartProfile":{"Sex":0,"Age":34,"HeightCm":178,"WeightKg":84,"BodyFatPercent":19,
              "ChestCm":102,"WaistCm":89,"HipsCm":100,"BicepsCm":34,"ThighCm":59,"NeckCm":39,"Posture":null,"Form":null}}},
          {"Name":"Копия","Slot":1,"Plan":{"Weeks":8,"StartDate":null,"StartProfile":null}}]}
        """;

    private const string OldWorkouts = """
        [{"Id":"a1","Date":"2026-10-07","Activity":0,"Setting":0,"DurationMin":45,"DistanceKm":4.125,"ElevationGainM":330,
          "AvgHr":null,"ActiveKcal":367,"TotalKcal":410,"WatchActiveKcal":450,"WatchTotalKcal":null},
         {"Id":"a2","Date":"2026-10-01","Activity":1,"Setting":1,"DurationMin":45,"DistanceKm":8,"ElevationGainM":40,
          "AvgHr":152,"ActiveKcal":659,"TotalKcal":720,"WatchActiveKcal":null,"WatchTotalKcal":null}]
        """;

    private static int _ids;
    private static string NewId() => $"id{Interlocked.Increment(ref _ids)}";

    private static LegacyInput Old() => new()
    {
        ProfileJson = OldProfile,
        WeightsJson = OldWeights,
        HypothesesJson = OldHypotheses,
        WorkoutsJson = OldWorkouts,
        PhotoSessions =
        [
            new("s-1", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(3)), new DateOnly(2026, 10, 1), Sex.Male, 178, 82.6, 17.5),
            new("s-2", new DateTimeOffset(2026, 10, 1, 19, 0, 0, TimeSpan.FromHours(3)), new DateOnly(2026, 10, 1), Sex.Male, 178, 82.3, 17.5),
        ],
    };

    [Fact]
    public void Migration_KeepsEverything()
    {
        var r = LegacyMigration.Migrate(Old(), Today, Now, NewId);

        var p = r.Profile!;
        Assert.Equal(Sex.Male, p.Sex);
        Assert.Equal(new DateOnly(1992, 1, 1), p.BirthDate); // 34 года → 1 января года рождения
        Assert.True(p.BirthDateApproximate);
        Assert.Equal(178, p.HeightCm);
        Assert.All(r.Entries, e => Assert.Equal(p.Id, e.ProfileId));

        // Сегодня — всё из старого профиля
        var today = BodyResolver.At(p, r.Entries, Today)!;
        Assert.Equal(81.5, today.Body.WeightKg);
        Assert.Equal(17, today.Body.BodyFatPercent);
        Assert.Null(today.FatSource); // источник % жира в старой версии неизвестен
        Assert.Equal(86, today.Body.WaistCm);
        Assert.Equal(39, today.Body.NeckCm);
        Assert.Null(today.Body.CalfCm);
        Assert.Equal(6, today.Body.Posture.Lordosis);
        Assert.Equal(44.5, today.Body.Vo2Max);
        Assert.Equal(58, today.Body.RestingHr);
        Assert.Equal(34, today.Body.Age);

        // День начала плана — замеры того дня; журнал веса — по датам; обхваты держатся с начала плана
        var start = BodyHistory.At(r.Entries, new DateOnly(2026, 9, 15));
        Assert.Equal(84, start.Weight!.Value);
        Assert.Equal(89, start.Girth(Girth.Waist)!.Value);
        var sep20 = BodyHistory.At(r.Entries, new DateOnly(2026, 9, 20));
        Assert.Equal(83.1, sep20.Weight!.Value);
        Assert.Equal(89, sep20.Girth(Girth.Waist)!.Value);

        // Две фотосессии одного дня — две записи со ссылками; вечерняя — последняя в этот день
        Assert.Equal(2, r.PhotoLinks.Count);
        var linked = r.Entries.Single(e => e.PhotoSessionId == "s-2");
        Assert.Equal(r.PhotoLinks["s-2"], linked.Id);
        Assert.Equal(82.3, BodyHistory.At(r.Entries, new DateOnly(2026, 10, 1)).Weight!.Value);

        Assert.Equal(EntrySource.Migrated, linked.Source);
        Assert.Equal(1 + 2 + 2 + 1, r.Entries.Count); // начало плана, вес ×2, фото ×2, профиль
    }

    [Fact]
    public void Migration_Workouts_RebuiltFromTotals()
    {
        var r = LegacyMigration.Migrate(Old(), Today, Now, NewId);
        Assert.Equal(2, r.Workouts.Count);

        var walk = r.Workouts.Single(w => w.Setting == Setting.Treadmill);
        Assert.True(walk.Migrated);
        Assert.Equal(new DateOnly(2026, 10, 7), walk.LocalDate);
        Assert.Equal(12, walk.Start.Hour);
        var segment = Assert.Single(walk.Segments);
        Assert.Equal(45, segment.Minutes, 6);
        Assert.Equal(5.5, segment.SpeedKmh, 2);       // 4,125 км за 45 мин
        Assert.Equal(8.0, segment.InclinePercent, 1);  // 330 м на 4 125 м
        Assert.Equal(450, walk.WatchActiveKcal);
        Assert.Equal(4.125, walk.DistanceKm, 2);

        var run = r.Workouts.Single(w => w.Setting == Setting.Outdoor);
        Assert.Equal(ActivityType.Running, run.Activity);
        Assert.Equal(8, run.OutdoorDistanceKm);
        Assert.Equal(152, run.AvgHr);

        // Считаются по весу на свою дату (1 октября — 82,3 по вечерней фотосессии)
        var calc = WorkoutCalculations.Calculate(run, r.Profile!, r.Entries)!;
        Assert.Equal(82.3, calc.WeightKg);
    }

    [Fact]
    public void Migration_NothingOld_NoProfile()
    {
        var r = LegacyMigration.Migrate(new LegacyInput(), Today, Now, NewId);
        Assert.Null(r.Profile);
        Assert.Empty(r.Entries);
        Assert.Null(LegacyMigration.Migrate(new LegacyInput { HypothesesJson = """{"Items":[]}""" }, Today, Now, NewId).Profile);
    }

    [Fact]
    public void Migration_BrokenJson_MigratesTheRest()
    {
        var r = LegacyMigration.Migrate(new LegacyInput { ProfileJson = "{не json", WeightsJson = OldWeights }, Today, Now, NewId);
        Assert.NotNull(r.Profile);
        Assert.Equal(2, r.Entries.Count);
        Assert.Equal(82.4, BodyHistory.At(r.Entries, Today).Weight!.Value);
    }

    [Fact]
    public void Migration_MissingOrZeroFields_AreNotMeasurements()
    {
        // Неполный профиль: обхватов нет (были бы нулями), рост 0 — берётся по умолчанию, пульс вне диапазона
        var r = LegacyMigration.Migrate(new LegacyInput
        {
            ProfileJson = """{"Sex":0,"Age":34,"HeightCm":0,"WeightKg":81.5,"BodyFatPercent":0,"NeckCm":0,"RestingHr":0}""",
            WeightsJson = """[{"Date":"2026-10-01","WeightKg":0}]""",
        }, Today, Now, NewId);

        Assert.Equal(BodyDefaults.For(Sex.Male).HeightCm, r.Profile!.HeightCm);
        var entry = Assert.Single(r.Entries);
        Assert.Equal(81.5, entry.WeightKg);
        Assert.Null(entry.BodyFatPercent);
        Assert.All(Enum.GetValues<Girth>(), g => Assert.Null(entry.GetGirth(g)));
        Assert.Null(entry.RestingHr);

        // Модель строится по оценкам, а не по нулям
        var body = BodyResolver.At(r.Profile, r.Entries, Today)!;
        Assert.True(body.Origin(BodyField.Waist).IsEstimate);
        Assert.InRange(body.Body.WaistCm, 70, 110);
    }

    [Fact]
    public void Backup_SaveRestore_NoLoss()
    {
        var r = LegacyMigration.Migrate(Old(), Today, Now, NewId);
        var extra = new BodyEntry
        {
            Id = "e-photo", ProfileId = r.Profile!.Id, Date = Today, RecordedAt = Now.AddMinutes(5), Source = EntrySource.Photo,
            WaistCm = 85.5, FatSource = FatSource.Navy, BodyFatPercent = 16.2, Form = new BodyForm(Stomach: 0.2), PhotoSessionId = "s-3",
        };
        var data = new BackupData
        {
            ExportedAt = Now,
            Profiles = [r.Profile],
            Entries = [.. r.Entries, extra],
            Workouts = r.Workouts,
        };

        string json = data.ToJson();
        var back = BackupData.Parse(json);

        Assert.Equal(json, back.ToJson());
        Assert.Equal(data.Entries.Count, back.Entries.Count);
        var e = back.Entries.Single(x => x.Id == "e-photo");
        Assert.Equal(FatSource.Navy, e.FatSource);
        Assert.Equal(0.2, e.Form!.Stomach);
        Assert.Equal(Now.AddMinutes(5), e.RecordedAt);
        Assert.Equal(r.Workouts[0].Segments[0], back.Workouts[0].Segments[0]);
        Assert.Equal(r.Workouts[0].Start, back.Workouts[0].Start);
        Assert.Equal(r.Profile.BirthDate, back.Profiles[0].BirthDate);
        Assert.Contains("\"source\": \"Photo\"".Replace(" ", ""), json.Replace(" ", "")); // перечисления строками
    }

    [Fact]
    public void Backup_WrongFile_ClearError()
    {
        Assert.Throws<InvalidDataException>(() => BackupData.Parse("{"));
        Assert.Throws<InvalidDataException>(() => BackupData.Parse("""{"app":"Другое","profiles":[{}]}"""));
        Assert.Throws<InvalidDataException>(() => BackupData.Parse("""{"app":"Тренировки и тело","format":99,"profiles":[{}]}"""));
        Assert.Throws<InvalidDataException>(() => BackupData.Parse("""{"app":"Тренировки и тело","format":1,"profiles":[]}"""));
    }

    [Fact]
    public void Entry_JsonRoundTrip_ForIndexedDb()
    {
        var e = new BodyEntry { Id = "x", ProfileId = "p", Date = Today, RecordedAt = Now, WeightKg = 80, Posture = new Posture(Kyphosis: 5) };
        string json = JsonSerializer.Serialize(e, DataJson.Default.BodyEntry);
        Assert.DoesNotContain("chestCm", json); // пустые поля не пишутся
        var back = JsonSerializer.Deserialize(json, DataJson.Default.BodyEntry)!;
        Assert.Equal(80, back.WeightKg);
        Assert.Equal(5, back.Posture!.Kyphosis);
        Assert.Null(back.ChestCm);
    }
}
