# three.js 0.186.1

Локальная копия без CDN и без сборщика. Взято из npm-пакета `three@0.186.1`:

| Файл | Из пакета | SHA-256 |
|---|---|---|
| `three.module.js` | `build/three.module.js` | `9052042d676cb0fdc1ddfefe193053f34b7ac0513a616fdac4535d49987812ea` |
| `three.core.js` | `build/three.core.js` (его импортирует `three.module.js`) | `9edde002b066a9a05676a6127f67735b62baf399bdea529f2f7e31657da769e6` |
| `addons/controls/OrbitControls.js` | `examples/jsm/controls/OrbitControls.js` | `3d79d07ecb686b4e5d93232eedab255331c1beef711e13164eaa1f68655a5f2b` |

Подключение — import map в `wwwroot/index.html`: `three` → `./lib/three/three.module.js`,
`three/addons/` → `./lib/three/addons/`. Лицензия MIT — `LICENSE`.

Обновление: `npm pack three@<версия>`, скопировать те же три файла, поправить версию здесь.
