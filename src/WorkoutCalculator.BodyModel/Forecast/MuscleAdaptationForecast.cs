using System.Collections.Immutable;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.BodyModel.Forecast;

public sealed record MuscleAdaptationState(double Adaptation, double Fatigue, double BaselineLeanAllocationKg,
    double LeanDeltaKg, double RelativeGrowth);
public sealed record MuscleMorphState(ImmutableDictionary<string, double> Groups)
{
    public static MuscleMorphState Identity { get; } = new(ImmutableDictionary<string, double>.Empty);
    public void Validate()
    {
        if (Groups is null || Groups.Any(p => !MuscleDefinitions.Groups.Any(g => g.Id == p.Key) ||
            !double.IsFinite(p.Value) || Math.Abs(p.Value) > .25)) throw new ArgumentException("Некорректная локальная форма.");
    }
    public double[] RegionValues() => MuscleDefinitions.Regions.Select(r => Groups.GetValueOrDefault(r.GroupId)).ToArray();
}
public sealed record MuscleForecastWeek(int Week, ImmutableDictionary<string, double> Stimulus,
    ImmutableDictionary<string, MuscleAdaptationState> Groups, double AllocatedLeanKg, MuscleMorphState Morph);

/// <summary>Versioned deterministic relative adaptation. Allocation kg are bookkeeping, not measured muscle mass.</summary>
public static class MuscleAdaptationForecast
{
    public const string Version = "training-shape-1";
    public const double PriorFloor = .2, GroupShareCap = .22, RelativeCap = .25, BaselineLeanShare = .45;
    public static ImmutableDictionary<string, double> Parameters() => new Dictionary<string, double>
    {
        ["Muscle.HalfSaturation"] = TrainingStimulusEngine.HalfSaturation,
        ["Muscle.PriorFloor"] = PriorFloor, ["Muscle.GroupShareCap"] = GroupShareCap,
        ["Muscle.RelativeCap"] = RelativeCap, ["Muscle.BaselineLeanShare"] = BaselineLeanShare,
        ["Muscle.FatigueCarry"] = .35, ["Muscle.Refractory"] = .6, ["Muscle.Decay"] = .04,
        ["Muscle.BeginnerRate"] = .18, ["Muscle.IntermediateRate"] = .12, ["Muscle.AdvancedRate"] = .08,
        ["Muscle.FullEnergyKcal"] = 250, ["Muscle.StimulusScore"] = 4, ["Muscle.AdaptationCeiling"] = .5,
        ["Muscle.Preservation"] = 2, ["Muscle.NegativeAllocationCap"] = .9,
        ["Muscle.MorphMaxM"] = .008, ["Muscle.CalibrationShrinkage"] = 24,
        ["Muscle.RangeBaseCm"] = .3, ["Muscle.RangeSqrtWeek"] = .15, ["Muscle.RangeWeek"] = .04,
        ["Muscle.RangeAdherence"] = .6, ["Muscle.RangePhoto"] = .5
    }.Concat(Priors.Select(p => new KeyValuePair<string, double>("Muscle.Prior." + p.Key, p.Value))).ToImmutableDictionary();
    private static readonly double[] PriorWeights = [8, 2, 2, 2, 8, 4, 3, 3, 4, 2, 3, 3, 5, 10, 3, 16, 9, 5, 2, 6];
    public static ImmutableDictionary<string, double> Priors { get; } = MuscleDefinitions.Groups.Select((g, i) => (g.Id, Value: PriorWeights[i] / PriorWeights.Sum())).ToImmutableDictionary(p => p.Id, p => p.Value);

    public static (double Adaptation, double Fatigue, double Effective) Step(double adaptation, double fatigue, double stimulus, TrainingExperience experience)
    {
        double effective = stimulus / (1 + .6 * fatigue);
        double rate = experience switch { TrainingExperience.Beginner => .18, TrainingExperience.Intermediate => .12, _ => .08 };
        return (Math.Clamp(adaptation * (1 - .04 * (1 - stimulus)) + rate * effective * (1 - adaptation), 0, 1),
            .35 * fatigue + .65 * stimulus, effective);
    }

    public static ImmutableArray<MuscleForecastWeek> Run(BodyProfile start, ForecastInput input, IReadOnlyList<ForecastWeek> composition,
        TrainingStimulusForecast stimulus, ImmutableDictionary<string, double>? response = null)
    {
        var state = Priors.ToDictionary(p => p.Key, p => new MuscleAdaptationState(0, 0, start.LeanMassKg * BaselineLeanShare * p.Value, 0, 0));
        foreach (var week in stimulus.History)
            foreach (var id in Priors.Keys)
            {
                var s = state[id]; var next = Step(s.Adaptation, s.Fatigue, week.Stimulus[id], input.Experience);
                state[id] = s with { Adaptation = next.Adaptation, Fatigue = next.Fatigue };
            }
        var result = ImmutableArray.CreateBuilder<MuscleForecastWeek>();
        result.Add(new(0, Priors.ToImmutableDictionary(p => p.Key, _ => 0.0), state.ToImmutableDictionary(), 0, MuscleMorphState.Identity));
        double accumulatedPotential = 0;
        for (int w = 1; w < composition.Count; w++)
        {
            var scores = new Dictionary<string, double>();
            double netLean = composition[w].LeanMassKg - composition[0].LeanMassKg;
            double energy = Math.Clamp(composition[w - 1].BalanceKcalPerDay / 250, 0, 1);
            double growthCapacity = 0;
            foreach (var id in Priors.Keys)
            {
                var s = state[id]; var next = Step(s.Adaptation, s.Fatigue, stimulus.Planned.Stimulus[id], input.Experience);
                state[id] = s with { Adaptation = next.Adaptation, Fatigue = next.Fatigue };
                double factor = response?.GetValueOrDefault(id, 1) ?? 1;
                if (!double.IsFinite(factor) || factor is < .8 or > 1.2) throw new ArgumentException("Некорректная реакция мышц.");
                growthCapacity = Math.Max(growthCapacity, next.Effective * (1 - .5 * s.Adaptation));
                scores[id] = Priors[id] * (netLean >= 0 ? PriorFloor + 4 * next.Effective * (1 - .5 * s.Adaptation) * factor
                    : 1 / (1 + 2 * next.Effective));
            }
            double ceiling = ForecastConstants.MonthlyLeanGainCeilingPercent(start.Sex, input.Experience) / 100 *
                composition[w - 1].WeightKg / ForecastConstants.WeeksPerMonth;
            accumulatedPotential += Math.Max(0, ceiling) * energy * growthCapacity;
            // Net-negative composition never contains hidden positive regional kg. Non-muscle lean stays in composition.
            double requested = netLean >= 0 ? Math.Min(netLean, accumulatedPotential) : netLean * BaselineLeanShare;
            var caps = Priors.ToDictionary(p => p.Key, p => Math.Min(Math.Abs(requested) * GroupShareCap,
                state[p.Key].BaselineLeanAllocationKg * (requested >= 0 ? RelativeCap : .9)));
            var allocation = Allocate(Math.Abs(requested), scores, caps);
            foreach (var id in Priors.Keys)
            {
                double delta = allocation[id] * Math.Sign(requested);
                state[id] = state[id] with { LeanDeltaKg = delta, RelativeGrowth = Math.Clamp(delta / state[id].BaselineLeanAllocationKg, -RelativeCap, RelativeCap) };
            }
            result.Add(new(w, stimulus.Planned.Stimulus, state.ToImmutableDictionary(), state.Values.Sum(s => s.LeanDeltaKg),
                new(state.ToImmutableDictionary(p => p.Key, p => p.Value.RelativeGrowth))));
        }
        return result.ToImmutable();
    }

    /// <summary>Deterministic capped water filling; unallocatable mass remains outside the muscle layer.</summary>
    public static ImmutableDictionary<string, double> Allocate(double total, IReadOnlyDictionary<string, double> scores, IReadOnlyDictionary<string, double> caps)
    {
        var values = scores.Keys.Order(StringComparer.Ordinal).ToDictionary(k => k, _ => 0.0);
        if (!double.IsFinite(total) || total < 0 || scores.Any(p => !double.IsFinite(p.Value) || p.Value <= 0) ||
            caps.Count != scores.Count || scores.Keys.Any(k => !caps.TryGetValue(k, out var c) || !double.IsFinite(c) || c < 0)) throw new ArgumentException("Invalid allocation.");
        double remaining = Math.Min(total, caps.Values.Sum());
        for (int pass = 0; pass <= values.Count && remaining > 1e-12; pass++)
        {
            var free = values.Keys.Where(k => caps[k] - values[k] > 1e-12).ToArray();
            double sum = free.Sum(k => scores[k]); if (sum <= 0) break;
            double used = 0;
            foreach (var k in free) { double add = Math.Min(caps[k] - values[k], remaining * scores[k] / sum); values[k] += add; used += add; }
            remaining -= used;
        }
        return values.ToImmutableDictionary();
    }
}
