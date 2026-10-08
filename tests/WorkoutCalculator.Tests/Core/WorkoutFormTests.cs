namespace WorkoutCalculator.Tests.Core;

/// <summary>Форма тренировки вкладки «Тренировка»: проверка полей по шагам и перевод в ввод калькулятора.</summary>
public class WorkoutFormTests
{
    private static WorkoutForm Treadmill(params (string? Duration, string? Speed, string? Incline)[] segments) => new()
    {
        Activity = ActivityType.Walking,
        Setting = Setting.Treadmill,
        Segments = segments.Select(s => new SegmentFields { Duration = s.Duration, Speed = s.Speed, Incline = s.Incline }).ToList(),
    };

    [Fact]
    public void Treadmill_OneSegment_CommaDecimalsAndClockTime()
    {
        var form = Treadmill(("30:30", "5,5", "12"));
        form.Handrails = true;
        form.DisplayDistanceKm = "2.8";

        Assert.Null(form.CheckParams());
        Assert.Null(form.CheckWatch());
        var input = form.ToInput();

        var segment = Assert.Single(input.Segments);
        Assert.Equal(30.5, segment.Minutes, 6);
        Assert.Equal(5.5, segment.SpeedKmh, 6);
        Assert.Equal(12, segment.InclinePercent, 6);
        Assert.True(input.HoldingHandrails);
        Assert.Equal(2.8, input.TreadmillDisplayDistanceKm);
        Assert.Null(input.AvgHr);
        Assert.Null(input.WatchActiveKcal);
    }

    [Fact]
    public void Treadmill_EmptyIncline_IsZero_AndManySegmentsKeepOrder()
    {
        var input = Treadmill(("5", "5", null), ("25", "6,5", "15")).ToInput();
        Assert.Equal([0.0, 15.0], input.Segments.Select(s => s.InclinePercent));
        Assert.Equal([5.0, 25.0], input.Segments.Select(s => s.Minutes));
    }

    [Fact]
    public void Treadmill_Errors_PointToField_WithSegmentNumber()
    {
        var empty = Treadmill((null, "5", null)).CheckParams();
        Assert.Equal(WorkoutForm.Ids.Duration(0), empty!.Field);
        Assert.StartsWith("Длительность: заполните", empty.Message);

        var speed = Treadmill(("30", "5", "0"), ("10", "45", "0")).CheckParams();
        Assert.Equal(WorkoutForm.Ids.Speed(1), speed!.Field);
        Assert.StartsWith("Отрезок 2: скорость", speed.Message);

        var time = Treadmill(("30:7x", "5", "0")).CheckParams();
        Assert.Equal(WorkoutForm.Ids.Duration(0), time!.Field);
        Assert.Contains("30:23", time.Message);
    }

    [Fact]
    public void Handrails_OnlyForTreadmillWalking()
    {
        var run = Treadmill(("30", "10", "1"));
        run.Activity = ActivityType.Running;
        run.Handrails = true;
        Assert.False(run.HandrailsApply);
        Assert.False(run.ToInput().HoldingHandrails);
    }

    [Fact]
    public void Outdoor_RequiresDistanceAndTime_GainOptional()
    {
        var form = new WorkoutForm { Activity = ActivityType.Running, Setting = Setting.Outdoor, Terrain = Terrain.Grass };
        Assert.Equal(WorkoutForm.Ids.OutdoorDistance, form.CheckParams()!.Field);

        form.OutdoorDistanceKm = "10,2";
        Assert.Equal(WorkoutForm.Ids.OutdoorDuration, form.CheckParams()!.Field);

        form.OutdoorDuration = "1:02:30";
        Assert.Null(form.CheckParams());
        var input = form.ToInput();
        Assert.Equal(10.2, input.OutdoorDistanceKm, 6);
        Assert.Equal(62.5, input.OutdoorMinutes, 6);
        Assert.Equal(0, input.OutdoorElevationGainM);
        Assert.Equal(Terrain.Grass, input.Terrain);
        Assert.Empty(input.Segments);
    }

    [Fact]
    public void Watch_OptionalFields_RangeChecked()
    {
        var form = Treadmill(("30", "5", "0"));
        form.AvgHr = "250";
        Assert.Equal(WorkoutForm.Ids.AvgHr, form.CheckWatch()!.Field);

        form.AvgHr = "142,4";
        form.WatchActiveKcal = "310";
        form.WatchTotalKcal = "";
        Assert.Null(form.CheckWatch());
        var input = form.ToInput();
        Assert.Equal(142, input.AvgHr);
        Assert.Equal(310, input.WatchActiveKcal);
        Assert.Null(input.WatchTotalKcal);
    }

    [Fact]
    public void Clear_KeepsWorkoutType()
    {
        var form = Treadmill(("30", "5", "0"), ("10", "6", "2"));
        form.Activity = ActivityType.Running;
        form.AvgHr = "150";
        form.Clear();
        Assert.Equal(ActivityType.Running, form.Activity);
        Assert.Equal(Setting.Treadmill, form.Setting);
        var segment = Assert.Single(form.Segments);
        Assert.Null(segment.Duration);
        Assert.Null(form.AvgHr);
    }

    [Fact]
    public void From_Treadmill_RoundTripsInput()
    {
        var input = new WorkoutInput
        {
            Activity = ActivityType.Walking,
            Setting = Setting.Treadmill,
            Segments = [new(5, 5, 0), new(30.5, 5.75, 12.5), new(65.25, 6, 3)],
            HoldingHandrails = true,
            TreadmillDisplayDistanceKm = 9.1,
            AvgHr = 128,
            WatchActiveKcal = 610,
        };
        var form = WorkoutForm.From(input);

        Assert.Equal(["5", "30:30", "1:05:15"], form.Segments.Select(s => s.Duration));
        Assert.Equal("5,75", form.Segments[1].Speed);
        Assert.Null(form.Segments[0].Incline);
        Assert.Null(form.CheckParams());
        Assert.Null(form.CheckWatch());
        var back = form.ToInput();
        Assert.Equal(input.Segments, back.Segments);
        Assert.True(back.HoldingHandrails);
        Assert.Equal(9.1, back.TreadmillDisplayDistanceKm);
        Assert.Equal(128, back.AvgHr);
        Assert.Equal(610, back.WatchActiveKcal);
        Assert.Null(back.WatchTotalKcal);
    }

    [Fact]
    public void From_Outdoor_RoundTripsInput()
    {
        var input = new WorkoutInput
        {
            Activity = ActivityType.Running,
            Setting = Setting.Outdoor,
            OutdoorDistanceKm = 10.25,
            OutdoorMinutes = 52 + 20 / 60.0,
            OutdoorElevationGainM = 85,
            Terrain = Terrain.Dirt,
        };
        var form = WorkoutForm.From(input);

        Assert.Equal("52:20", form.OutdoorDuration);
        Assert.Null(form.CheckParams());
        var back = form.ToInput();
        Assert.Equal(10.25, back.OutdoorDistanceKm);
        Assert.Equal(input.OutdoorMinutes, back.OutdoorMinutes, 9);
        Assert.Equal(85, back.OutdoorElevationGainM);
        Assert.Equal(Terrain.Dirt, back.Terrain);
        Assert.Empty(back.Segments);
    }

    [Fact]
    public void WatchComparison_CloseOrPercent()
    {
        var form = Treadmill(("30", "6", "10"));
        form.WatchActiveKcal = "1000";
        var input = form.ToInput();
        var result = EnergyCalculator.Calculate(new UserProfile { Sex = Sex.Male, Age = 30, HeightCm = 180, WeightKg = 80 }, input);
        string text = Display.WatchComparison(input, result)!;
        Assert.StartsWith("Apple Watch: 1000 активных, на ", text);
        Assert.Contains("больше оценки", text);

        Assert.Null(Display.WatchComparison(Treadmill(("30", "6", "10")).ToInput(), result));
    }
}
