using System.Diagnostics;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Consistency;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;

namespace WorkoutCalculator.Web.Services;

/// <summary>
/// 3D-сцена: тела в двух слотах вида (viewer.js) — основное (<see cref="Primary"/>) и второе для сравнения
/// (<see cref="Secondary"/>). Модель — MakeHuman; манекен — только если данные MakeHuman не загрузились,
/// и как эталон для проверки замеров (<see cref="Report"/>). Сборка не чаще, чем успевает: пока строится сетка,
/// новые значения только запоминаются; точная подгонка (слой под вес) и проверка — после паузы.
/// </summary>
public sealed class BodyScene(Action changed)
{
    public const string Primary = "current", Secondary = "forecast";

    /// <summary>Точная подгонка и проверка замеров — после паузы во вводе.</summary>
    private const int CheckDelayMs = 150;

    private sealed class Slot
    {
        public BodyProfile? Wanted;
        public bool Dirty, Rebuilding;
        public int Version;
        public MakeHumanFit? Fit;
        public IBodyShape? Body;
    }

    private readonly Dictionary<string, Slot> _slots = new() { [Primary] = new(), [Secondary] = new() };
    private bool _viewerReady, _showTapes;
    private Girth? _highlight;

    public MakeHumanModel? MakeHuman { get; private set; }

    /// <summary>Пока модель не готова — текст состояния; null — готова.</summary>
    public string? Status { get; private set; } = "Загружаю модель…";

    private bool _fallback;

    /// <summary>Можно строить: MakeHuman загружен или не загрузился (тогда манекен).</summary>
    public bool CanBuild => MakeHuman is not null || _fallback;

    /// <summary>Проверка замеров основного тела — по манекену (он откалиброван под объём из веса).</summary>
    public ConsistencyReport? Report { get; private set; }

    public double BuildMs { get; private set; }
    public double UploadMs { get; private set; }
    public double CheckMs { get; private set; }

    public IBodyShape? Body(string slot) => _slots[slot].Body;

    private bool TapesNeeded => _showTapes || _highlight is not null;

    /// <summary>Данные MakeHuman (~2 МБ) — в фоне; не загрузились — манекен с пометкой.</summary>
    public async Task LoadAsync(HttpClient http)
    {
        try
        {
            // Версия в адресе — чтобы браузер не взял из кэша файл старого формата
            var bytes = await http.GetByteArrayAsync($"data/{MakeHumanData.FileName}?v={MakeHumanData.Version}");
            MakeHuman = new MakeHumanModel(MakeHumanData.Read(bytes));
            Status = null;
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException)
        {
            _fallback = true;
            Status = "Модель MakeHuman не загрузилась — показан упрощённый манекен";
        }
        RebuildAll();
        changed();
    }

    public void ViewerReady()
    {
        _viewerReady = true;
        RebuildAll();
    }

    /// <summary>Тело в слот; null — убрать.</summary>
    public void Set(string slot, BodyProfile? profile)
    {
        var s = _slots[slot];
        s.Wanted = profile?.Clone();
        if (profile is null)
        {
            s.Body = null;
            if (_viewerReady) ViewerInterop.ClearMesh(slot);
            return;
        }
        Request(slot);
    }

    public void ShowTapes(bool all)
    {
        _showTapes = all;
        if (all) SendTapes();
        if (_viewerReady) ViewerInterop.ShowTapes(all);
    }

    /// <summary>Подсветить ленту обхвата (null — снять подсветку).</summary>
    public void Highlight(Girth? g)
    {
        if (_highlight == g) return;
        bool had = TapesNeeded;
        _highlight = g;
        if (!had && TapesNeeded) SendTapes();
        if (_viewerReady) ViewerInterop.HighlightTape(g is Girth x ? (int)x : -1);
    }

    /// <summary>Режим вида: одно тело или сравнение (основное — контуром поверх второго, или рядом).</summary>
    public void SetCompare(bool compare, bool sideBySide)
    {
        if (_viewerReady) ViewerInterop.SetMode(compare ? "compare" : "current", sideBySide);
    }

    private void SendTapes()
    {
        if (!_viewerReady) return;
        foreach (var (name, s) in _slots)
            if (s.Body is not null) ViewerInterop.SetTapes(name, s.Body.Tapes);
    }

    private void RebuildAll()
    {
        foreach (var name in _slots.Keys)
            if (_slots[name].Wanted is not null) Request(name);
    }

    private void Request(string slot)
    {
        var s = _slots[slot];
        s.Dirty = true;
        if (_viewerReady && CanBuild && !s.Rebuilding)
            _ = RebuildLoopAsync(slot, s);
    }

    private async Task RebuildLoopAsync(string name, Slot s)
    {
        s.Rebuilding = true;
        try
        {
            while (s.Dirty && s.Wanted is BodyProfile profile)
            {
                s.Dirty = false;
                var sw = Stopwatch.StartNew();
                // Быстрая сборка: MakeHuman — с прошлого решения и без подгонки слоя под вес
                var body = Build(profile, s, fitVolume: false);
                if (name == Primary) BuildMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                Show(name, body);
                if (name == Primary) UploadMs = sw.Elapsed.TotalMilliseconds;
                s.Body = body;
                _ = CheckLaterAsync(name, s, ++s.Version);
                changed();
                await Task.Delay(1); // отдать управление браузеру: кадр и новые события ввода
            }
        }
        finally
        {
            s.Rebuilding = false;
        }
    }

    private IBodyShape Build(BodyProfile profile, Slot s, bool fitVolume)
    {
        if (MakeHuman is null) return Mannequin.Build(profile.Clone());
        var body = MakeHuman.Build(profile.Clone(), s.Fit, fitVolume);
        s.Fit = body.Fit;
        return body;
    }

    private async Task CheckLaterAsync(string name, Slot s, int version)
    {
        await Task.Delay(CheckDelayMs);
        if (version != s.Version || s.Wanted is not BodyProfile profile) return; // ввод ещё идёт
        var sw = Stopwatch.StartNew();
        if (name == Primary) Report = ConsistencyChecker.Check(Mannequin.Build(profile.Clone()));
        if (MakeHuman is not null)
        {
            var body = Build(profile, s, fitVolume: true);
            Show(name, body);
            s.Body = body;
        }
        if (name == Primary) CheckMs = sw.Elapsed.TotalMilliseconds;
        changed();
    }

    private void Show(string slot, IBodyShape body)
    {
        ViewerInterop.SetMesh(slot, body.Mesh);
        if (TapesNeeded) ViewerInterop.SetTapes(slot, body.Tapes);
    }
}
