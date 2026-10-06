# Данные MakeHuman

`makehuman-hm08.bin` — базовая сетка MakeHuman hm08 (только тело: 13 380 вершин, 13 378 четырёхугольников),
вершины суставов и таргеты, которые нужны приложению:

- макро: пол × возраст (25 и 90 лет) × мускулатура × полнота; внешность — среднее трёх расовых таргетов;
- замеры: грудь, талия, бёдра, плечо, бедро, шея, голень, запястье (больше / меньше).

Источник — [makehumancommunity/makehuman](https://github.com/makehumancommunity/makehuman),
коммит `a8bc2d54ff0ac92e78ff71431b1023eda42bf482`. Ассеты MakeHuman (сетка и таргеты) выпущены
под **CC0 1.0** (см. `LICENSE.md` и `LICENSE.ASSETS.md` в их репозитории). Код MakeHuman (AGPL) не используется.

Координаты переведены в метры, смещения таргетов квантованы в int16 (погрешность меньше 0,01 мм).
Формат описан в `src/WorkoutCalculator.BodyModel/MakeHuman/MakeHumanData.cs`.

Пересобрать:

```bash
git clone --depth 1 https://github.com/makehumancommunity/makehuman /tmp/makehuman
dotnet run --project tools/WorkoutCalculator.MakeHumanImport -- \
  /tmp/makehuman/makehuman/data src/WorkoutCalculator.Body3D/wwwroot/data/makehuman-hm08.bin \
  "makehumancommunity/makehuman <коммит> (ассеты CC0 1.0)"
```
