using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel;

/// <summary>Лента замера: замкнутая линия там, где меряется обхват. Точки — x, y, z подряд, метры.</summary>
public sealed record TapeLoop(Girth Girth, float[] Points);

/// <summary>Тело, построенное по замерам: процедурный манекен или модель MakeHuman.</summary>
public interface IBodyShape
{
    BodyProfile Profile { get; }
    BodyMesh Mesh { get; }

    /// <summary>Внешний объём тела по замкнутой сетке, л.</summary>
    double VolumeLiters { get; }

    /// <summary>Обхват, измеренный по готовой сетке, см.</summary>
    double MeasureGirthCm(Girth g);

    /// <summary>Ленты замеров на готовой сетке — показать, где меряется каждый обхват.</summary>
    IReadOnlyList<TapeLoop> Tapes { get; }
}
