# MediaPipe Tasks Vision 1.1.0 — поза человека в браузере

Используется съёмкой с подсказками (`wwwroot/js/capture.js`): по кадру с камеры модель находит
33 точки тела, по ним проверяется поза (весь ли человек в кадре, спереди или боком, где руки).
Всё считается в браузере, кадры никуда не отправляются.

| Файл | Откуда | sha256 |
|---|---|---|
| `vision_bundle.mjs` | npm `@mediapipe/tasks-vision@1.1.0`, `vision_bundle.mjs` | `9d5fc9ef74b22329cf9aa88dc543956d192b8c254210b187271614713eb5b10c` |
| `wasm/vision_wasm_internal.js` | там же, `wasm/` | `7c652617b5bef832f42878ce8c5f752b0724bca6054e4bc1013a37d4b1949723` |
| `wasm/vision_wasm_internal.wasm` | там же, `wasm/` | `782eda3ec1f414fdb4f1d66a8b59093eb4d5ab437e06117796abf05962d68eef` |
| `pose_landmarker_full.bin` | `https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/1/pose_landmarker_full.task` | `5134a3aad27a58b93da0088d431f366da362b44e3ccfbe3462b3827a839011b1` |

- Лицензия кода и модели — Apache 2.0 (`LICENSE`; модель — по её карточке, BlazePose GHUM 3D).
- Только вариант с SIMD: его поддерживают все браузеры, где работает приложение (Safari с 16.4).
- Модель переименована из `.task` в `.bin`: файлы с незнакомым расширением сервер разработки не отдаёт.
- Папка с версией в имени: при обновлении адреса меняются, и офлайн-кэш (service worker) не отдаст
  старые файлы. Эти файлы не кладутся в кэш при установке приложения — только при первой съёмке.
