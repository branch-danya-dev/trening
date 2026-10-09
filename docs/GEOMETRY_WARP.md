# GeometryWarpRenderer — Phase 7

Локальная детерминированная геометрическая визуализация **одной сохранённой Hypothesis**. Это деформация исходной текстуры по рассчитанной форме, не фотография будущего, не AI-генерация и не новая физиологическая модель. Стоимость внешнего inference — $0. [Результаты и ограничения проверки](evidence/geometry-warp/README.md).

## База и аудит

PR #26 смержен обычным merge commit `7eb03abc2c1f36fe6f0ec78a72134b1a3802d962`. Полный [main CI](https://github.com/branch-danya-dev/trening/actions/runs/37942116786) прошёл до создания `codex/geometry-warp-renderer`.

Репозиторий **содержал** `BodyModel/Photos/PhotoWarp.cs`, `wwwroot/js/warp.js`, PhotosPanel, domain/JS tests. Это деформация строк по ширине силуэта, без полной mesh visibility. Её алгоритм и прежний UI сохранены; настоящий `PhotoWarp.Plan`/`warpRows` участвует в benchmark. JPEG legacy preview теперь тоже помечен synthetic. Ранее существовавшего production full-mesh projective renderer аудит исходников не обнаружил.

## Контракт и неизменность

`WorkoutCalculator.BodyModel.Rendering` содержит `IForecastRenderer`, capability metadata, `RenderSource`, `RenderRequest`, `RenderResult`, `RenderArtifact`, source/quality policies. C# orchestration — `Web/Services/GeometryWarpRenderer.cs`; WebGL и вычисления — независимые `geometry-warp*.js`. Видимый Three.js viewer и его камера не используются. Новых graphics/model dependencies нет.

Target берётся только из `Hypothesis.Core.ExactEndpoint.Geometry`. Source rebuild использует `CurrentAvatarRevisionAtIssue`, включая frozen inputs/corrections. Builder проверяет совместимость topology. Изменение live Avatar, CheckIn, текущей камеры или даты не заменяет эти входы. Hypothesis не мутирует ни при рендере, ни при удалении изображения.

Request фиксирует Hypothesis/core hash, Profile/Avatar/cycle, source revision ID/hash, target geometry hash, photo session/view/observed date, analysis hash, image bytes hash и версии. SHA-256 канонического request без ID/hash/attempt timestamp задаёт idempotency key `render:<hash>`. Повтор получает проверенный сохранённый artifact. C# дополнительно проверяет frozen revision hash при чтении и restore. Result содержит structural hashes, synthetic artifact/output hash, quality/metrics/reasons, timings/capability; `Photorealistic=false`.

| Контракт | Версия |
|---|---|
| Provider | `geometry-warp-1` |
| Source policy | `exact-revision-photo-1` |
| Alignment | `orthographic-neutral-1` |
| Structural conditions | `structural-condition-1` |
| Quality policy | `render-quality-1` |
| Artifact envelope | `workoutcalc.renders.v1` |

## Пригодный исходный снимок

Снимок должен существовать и иметь анализ к `EvidenceCutoff`/issue time, относиться к effective date исходной revision, иметь `OriginalObservation`, confidence ссылки ≥0.8 и **точно совпадающий frozen analysis hash** в `AvatarPhotoReference`. Timestamp черновика CheckIn может предшествовать окончанию его анализа; связь доказывается frozen hash, а анализ обязан завершиться до issue. Сохраняется SHA-256 нормализованных JPEG bytes и полная RLE-маска PhotoAnalyzer.

Новые качественные photo-derived revisions сохраняют эту ссылку. Старые revisions/analysis без маски/hash не мигрируют в «пригодные» задним числом. Нет поиска более нового фото, near-origin fallback или изменения issued Hypothesis. Сообщение: «Для этой гипотезы нет подходящего исходного фото, связанного с её исходным аватаром.»

Поддержаны front и canonical side (±90° по `FacingLeft`). Back отсутствует. Нужны полный рост с crown/floor внутри кадра, нейтральная поза стоя, руки в соответствии с исходной моделью, отсутствие больших перекрытий и прилегающая одежда. Проверяются 33 pose landmarks, visibility основных суставов ≥0.8, вертикальная последовательность shoulders/hips/ankles, наклон плеч ≤0.04 роста, front shoulder span ≥0.12 роста, side shoulder span ≤0.08 роста, нескрещённые front wrists, ≥30 snapped уровней без arm overlap, finite scale/coordinates и отсутствие analysis warnings. Далее обязательны residual gates.

Нет inverse kinematics, распознавания фасона одежды или обещания поддержки любого progress photo. Свободная одежда и несовпадение позы с канонической моделью могут не пройти mask/landmark alignment; тонкие ошибки одежды нельзя гарантированно обнаружить. Неподходящий источник оставляет доступным 3D endpoint.

## Проекция и reusable structural bundle

Ортографическая модель: единый scale по current crown/floor, vertical translation по floor, horizontal center по hip landmarks; canonical front/side rotation. Нет свободного подбора FOV, оптимизатора позы, деформации исходной модели для «улучшения» alignment или зависимости от viewer. Height, silhouette-row width, centerline и landmark residuals нормируются ростом фигуры в рабочих пикселях.

`WarpGL.conditions` экспортирует transient bundle: version, dimensions, alignment descriptor, current/target depth Float32 maps (0..1, background=1), target normal RGBA8 map (RGB = camera normal ×0.5+0.5, alpha = mask), current/target body masks и structural hashes geometry+alignment. CPU maps имеют верхний левый origin; GL textures перевёрнуты только на границе загрузки/чтения. Depth хранится в offscreen DEPTH_COMPONENT24 и отдельно кодируется в RGB24 для readback. Камера и значения нормалей одинаковы для обоих mesh. GL framebuffer/texture handles — transient implementation detail, не часть переносимого contract.

Phase 8 может потреблять эти target depth/normal/mask и alignment/hashes, сохраняя ту же immutable цель. Текущая реализация ничего не отправляет и не сохраняет диагностические карты постоянно. Original PhotoAnalyzer mask остаётся частью анализа оригинала, а не сгенерированным render artifact.

## Projective triangle warp и visibility

Shared-topology source vertices проецируются в source photo coordinates; target vertices — той же камерой в destination. Target rasterization использует z-buffer и backface culling. Fragment получает интерполированную source projection, проверяет front facing, UV bounds, исходную наблюдаемую body mask и current depth. Проверяются только четыре ближайших raster sample centers; глубина корректируется по плоскости треугольника. Допуск depth 0.0005 в диапазоне 4 m (2 mm); выбирается ближайший видимый sample. Скрытая исходная сторона не используется как доступная текстура. Target depth определяет перекрытия будущей формы.

Цвет копируется из исходного RGB без lighting/color correction; нет UV unwrap или генерации деталей. Nearest valid sampling сохраняет identity и делает политику видимости явной, но может давать небольшую пикселизацию. Антиалиасинг, blending и dithering выключены.

## Фон, недостающая текстура и quality

PhotoAnalyzer RLE mask задаёт source silhouette; target silhouette — raster target mesh. При уменьшении тела `source − target` заполняется ближайшим **исходным фоном** только в радиусе ≤0.025 роста. За пределами source/target transition zone исходные RGBA bytes остаются неизменны.

Недостающая body texture допускает только nearest valid body edge stretch в радиусе ≤0.008 роста. Невидимая область больше порога отклоняется. Нет generative inpainting, skin synthesis или произвольного дорисовывания одежды. Output mask вычисляется по реально покрытым/заполненным пикселям; оставшиеся отверстия дают Rejected. Rejected output не сохраняется и не показывается как faithful image.

| Метрика | Условие сохранения |
|---|---|
| Target silhouette IoU | ≥0.98 |
| Symmetric contour distance / body height | ≤0.006 |
| Row-width MAE / body height | ≤0.008 |
| Максимальное projected landmark displacement source→target / height | ≤0.025 |
| Invalid/hole body fraction | 0 |
| Repaired background / source body area | ≤0.08 |
| Stretched pixels / target body area | ≤0.015 |
| Changed background fraction outside transition band | 0 |
| Current model / observed source mask IoU | ≥0.90 |
| Body height residual | ≤0.03 |
| Alignment row-width MAE / height | ≤0.02 |
| Centerline error / height | ≤0.015 |
| Alignment landmark error / height | ≤0.045 |
| Finite/decodable output | required |

Accepted проходит все условия; Limited дополнительно отмечает stretch >0.002, repair >0.03 или source-mask IoU <0.95. Rejected означает нарушение quality policy. Unsupported означает неподдержанный источник/устройство/ошибку выполнения. Это structural checks, **не вероятность или процент точности прогноза**. Landmark drift здесь ограничивает изменение исходной позы shared topology; независимой оценки человеческой позы по сгенерированному изображению нет.

## Identity и устройства

При побайтово одинаковых current/target positions composite копирует исходные пиксели без repair. Зафиксированный synthetic fixture требует max byte difference=0, mask IoU=1, repair=0. Отдельный WebGL тест принудительно проходит shader identity без shortcut: ожидает max byte difference≤1, holes=0; текущий результат 0. Повторный nonidentity render допускает ≤1 byte между одинаковыми запусками на том же backend; измерено 0. Между различными GPU/браузерами raster edge rounding может отличаться, глобальная побайтовая переносимость не заявляется.

Original photo store нормализует до 2048 px. Рабочая сторона ограничена min(1024, MAX_TEXTURE_SIZE), aspect ratio сохраняется; слишком маленькая capability/невалидные размеры отклоняются. Не декодируются 12MP оригиналы. WebGL2 unavailable, context loss, shader/framebuffer/GL errors приводят к Unsupported; повтор создаёт новый context. Все buffers/textures освобождаются. Приложение, фото и Hypothesis сохраняются. WebGPU, сервер и арендуемый GPU не требуются.

## Хранение, PIN и retention

IndexedDB `body3d-photos` schema 3 добавляет **отдельный** `renderArtifacts`. Бинарные output и synthetic envelope не попадают в factual `sessions`/`images`. Shared DB нужен для атомарного удаления source и derived artifacts. AES-GCM seal/unseal, PBKDF2/PIN, lock/reload/background timeout те же, что у фото. Metadata и metrics, как прежние photo metadata, не шифруются; pixels шифруются при включённом PIN. Переключение PIN атомарно переписывает original images и synthetic blobs. URL отзываются при lock и source deletion, включая другие вкладки через BroadcastChannel.

Сохранение выполняется под общим `trening-archive` Web Lock: проверка restore generation, unchanged source session и exact request. Удаление source в одной transaction удаляет все его renders; удаление только render не меняет Hypothesis. Скачать — явный экспорт вне app retention. Regeneration требует исходного изображения, совместимых analysis/renderer versions и frozen refs.

Full backup включает metadata, blobs/ciphertext, IV/type и существующие PIN settings, без повторной генерации. Restore проверяет envelope, frozen Hypothesis lineage, output checksum, quality, source refs; сохраняет exact bytes и защищён от stale tabs. Старые backups без renderArtifacts допустимы. Depth/normal maps не включаются. Полный ZIP сохраняет прежний лимит 512 MiB; saved renders не исключаются молча.

## Граница фактов и приватность

Factual photo writers/analysis отвергают synthetic metadata и IDs `render:`; CheckIn/BodySnapshot/outcome не принимают такие refs. PNG содержит synthetic tEXt marker; legacy JPEG preview — APP15 marker. Повторный импорт такого файла даже с `OriginalObservation` claim отклоняется до нормализации. Это защита контролируемых путей приложения, а не forensic detector: внешнее редактирование/скриншот может удалить marker и требует правдивого подтверждения пользователя.

Рендер не содержит fetch, XHR, AI API или telemetry. Modules загружаются при старте приложения, source pixels проходят только local IndexedDB/Blob/WebGL. Smoke записывает все transport requests во время generation: HTTP requests=0; `blob:` URL — локальное отображение. Published PWA проверена после полного offline reload. Original ML photo analysis — отдельный pipeline с существующими model assets; benchmark не утверждает offline получение нового ML-анализа без этих assets.

Validation export добавляет отдельный opt-in render diagnostics: synthetic=true, псевдонимы refs/hashes, view/versions, metrics/state/reasons, timings/capability. Source/output bytes, blob URLs и raw identifiers не включаются. Existing photo opt-in остаётся выключенным по умолчанию и экспортирует только factual originals.

## Производительность и проверка

Отдельные timings: decrypt/load, current rebuild, target rebuild, alignment, structural maps, warp, repair/composite, encode, artifact preparation и save; cached repeat измеряется отдельно. Save transaction wall time возвращается вызывающему коду, сохранённая metadata фиксирует подготовку до commit. WorkingBytes — расчёт buffers, не измеренный browser/GPU peak RSS. Cached invocation пока реконструирует meshes до обращения в JS cache; это ограничение оптимизации.

В [evidence](evidence/geometry-warp/README.md) приведены per-case NoWarp, настоящий legacy row warp, GeometryWarp, состояния, holes/repair/runtime, screenshots и regression. Front/side × четыре профиля × identity, waist±, hips/thigh±, chest±, loss/gain, small posture, unsafe growth = 88 cases. Synthetic источники генерируются из закреплённых repository MakeHuman assets/BodyDefaults; их создание существует только в tests/tools, не в продукте.

Для последующей real-photo проверки участник локально снимает front/side по протоколу, проверяет factual analysis и avatar, выдаёт Hypothesis, сохраняет диагностику по согласию. До просмотра результатов фиксируются thresholds; записываются все отказы, одежда/поза/фон, ручные оценки артефактов, физический device/runtime. Фотографии не добавляются в public evidence без отдельного согласия. Нельзя выводить real-human visual accuracy или физиологическую точность из synthetic benchmark. Физический слабый телефон остаётся непроверенным.

Phase 8 managed renderer, backend/queue/API, arbitrary pose/IK, generative repair, BodyParts3D, DeltaShape и новые forecast formulas не реализованы.
