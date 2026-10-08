# Данные MakeHuman

`makehuman-muscle-atlas-v1.bin` — предрасчитанный sidecar атласа (107 156 байт); MakeHuman v3 не изменён.
Источник, SHA-256, версии, binary layout, regenerate и замеры — [docs/STRENGTH.md](../../../../docs/STRENGTH.md).
Полный import также пишет sidecar; его можно перегенерировать из canonical `makehuman-hm08.bin`
через режим `--atlas` того же инструмента. Runtime не выполняет геометрическую генерацию.

`makehuman-hm08.bin` — базовая сетка MakeHuman hm08 (только тело: 13 380 вершин, 13 378 четырёхугольников),
суставы, таргеты, зоны тела и скелет, которые нужны приложению:

- макро: пол × возраст (25 и 90 лет) × мускулатура × полнота; внешность — среднее трёх расовых таргетов;
- замеры: грудь, талия, бёдра, плечо, бедро, шея, голень, запястье, колено, щиколотка (больше / меньше);
- форма при тех же обхватах: живот (`stomach-pregnant`), ягодицы (`buttocks-volume`), глубина корпуса
  (`torso-scale-depth`), V-силуэт (`torso-vshape`);
- зоны тела для слоя мягких тканей (живот, таз, бедро, кисть, голова…): веса скелета MakeHuman
  (`rigs/default_weights.mhw`), сложенные по зонам, — доля зоны для каждой вершины;
- скелет MakeHuman (`rigs/default.mhskel`): 104 кости — позвоночник, шея, голова, руки с пальцами,
  ноги с пальцами; лицевые кости (челюсть, веки, губы, язык) слиты с головой. Для каждой вершины —
  до четырёх костей с долями (столько берёт three.js); у 1 % вершин отброшенные кости весили больше 11 %,
  у остальных меньше. Суставы — центры вспомогательных кубиков MakeHuman: в файле по одной вершине на
  кубик, её смещение в таргете — среднее смещений кубика, так что скелет после таргетов там же, где
  у MakeHuman.

Источник — [makehumancommunity/makehuman](https://github.com/makehumancommunity/makehuman),
коммит `a8bc2d54ff0ac92e78ff71431b1023eda42bf482`. Ассеты MakeHuman (сетка, таргеты, скелет и его веса) выпущены
под **CC0 1.0** (см. `LICENSE.md` и `LICENSE.ASSETS.md` в их репозитории). Код MakeHuman (AGPL) не используется.

Координаты переведены в метры, смещения таргетов квантованы в int16 (погрешность меньше 0,01 мм).
Формат описан в `src/WorkoutCalculator.BodyModel/MakeHuman/MakeHumanData.cs`.

Пересобрать:

```bash
git clone --depth 1 https://github.com/makehumancommunity/makehuman /tmp/makehuman
dotnet run --project tools/WorkoutCalculator.MakeHumanImport -- \
  /tmp/makehuman/makehuman/data src/WorkoutCalculator.Web/wwwroot/data/makehuman-hm08.bin \
  "makehumancommunity/makehuman <коммит> (ассеты CC0 1.0)"
```
