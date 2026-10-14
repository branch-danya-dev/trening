# UI design system V1

## Direction

The model workspace uses `100dvh` with a fixed 3D area and an independently scrolling task pane. Desktop retains left navigation; mobile uses a model/task split above safe-area bottom navigation. Photo controls belong to the model: date, **Фото на 3D**, **До / после**. Busy/error status stays visible at every width. Long explanations and secondary editor controls remain in the task pane.

A quiet fitness product: warm neutral background, white surfaces, restrained forest green, clear type and generous separation. 3D is content with a dedicated canvas, not a decorative dashboard tile. No neon glow or speculative imagery. Russian is the consumer language.

## Tokens

Canonical tokens live in `src/WorkoutCalculator.Web/wwwroot/css/ui-v1.css`, loaded after existing feature styles. Existing feature class names and semantic test hooks remain available.

| Role | Value |
|---|---|
| Space | 4 / 8 / 12 / 16 / 24 / 32 / 48 px |
| Background / panel / card / inset | #f3f5f1 / #f9faf7 / #ffffff / #f0f3ed |
| Text / secondary | #202d24 / #53624f |
| Primary / primary tint | #326344 / #e4eddf |
| Warning / warning tint / danger | #87531d / #fff2dc / #a33630 |
| Border / strong border | #dce2d8 / #a6b3a1 |
| Radius | 12 controls, 20 cards, 24–28 viewer |
| Elevation | 0 8px 28px #253c2610 |
| Body / supporting / heading | 16px/1.5; 13px/1.55; 28–38px display, 23px section |
| Maximum content | 1320px split, 1060px activity/profile |
| Focus | 3px #526dba, 3px offset |
| Touch | 44px minimum interactive height; checkbox hit area is its label |

## Responsive composition

Above 768px use a permanent left navigation. Model: large sticky viewer beside state/actions. Today and Profile use the full content width. At 768px and below use five equal bottom destinations with icons and labels, including safe-area padding. Document scroll avoids nested full-height desktop panels. Editors occupy the available width with stacked fields; controls never compress into desktop modals.

320px is supported. Seven-column calendars use an explicitly labelled local scrolling region when needed to retain usable date targets. Wide comparison tables have their own scroll region; page-level horizontal overflow is forbidden. Test 320/390/768/1024/1400.

## Components and interaction

Use one clear primary action per decision, secondary outlined actions, and native details for supporting information. Keep label text, keyboard focus and errors independent of color. Onboarding has an eight-step indicator and durable draft copy. Long CheckIn commits announce progress and publish success only after the service returns. Close-day review receives focus. Navigation has labelled tabs and keyboard arrows.

Progress charts consume validated factual records, retain missing values, name sources and expose a textual summary plus a values table. Trend lines connect recorded values; they do not create daily facts. 3D has textual parameters and a named focus target. Reduced-motion preferences disable decorative CSS animation. Offline messaging never reloads a draft.
