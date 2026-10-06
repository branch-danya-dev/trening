using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel;

/// <summary>Тело, построенное по замерам: процедурный манекен или модель MakeHuman.</summary>
public interface IBodyShape
{
    BodyProfile Profile { get; }
    BodyMesh Mesh { get; }

    /// <summary>Внешний объём тела по замкнутой сетке, л.</summary>
    double VolumeLiters { get; }

    /// <summary>Обхват, измеренный по готовой сетке, см.</summary>
    double MeasureGirthCm(Girth g);
}
