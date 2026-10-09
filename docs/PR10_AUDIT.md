# PR #10 audit before personalized forecast

## Follow-up: product consolidation after #15 / #16

Compared #10 head `70bcb2d` again with the implementation built from `main` `1ddb66e3`:

| Idea in #10 | Replacement in current architecture |
|---|---|
| `Onboarding` | Six steps, optional measurements/photos, reloadable draft, stable first Manual BodySnapshot ID, explicit confirmation, restore alternative. No estimated BF promoted to fact. |
| `ActivitySection` month/day/week | `ActivityCalendar` over existing cardio and strength stores, separate indicators, duration/kcal/completed sets, reps/tonnage/load, edit/delete. No parallel Workout model or historical future-fallback. |
| `BackupCard` | Complete versioned ZIP including strength, BodySnapshots, ForecastSnapshots/calibration, programs and encrypted photos. Hash/domain validation, explicit confirmation, backup-before-restore, durable rollback, interrupted-tab recovery and stale-tab protection. |
| `ProfileSection` / main navigation | Five public sections, birthday/goal/privacy, diagnostics, main overview and mobile bottom navigation. Forecast/history remain directly accessible. |

The useful product scope is superseded by this consolidation PR; **close #10 without merge**, with a link to the replacement in its conversation after integrated checks pass. No wholesale merge or old data migration is needed.

Concrete optional ideas left for future work: time-of-day and free-text cardio notes, reconstructing/editing original cardio segments (legacy main only stored totals), independent comparison of #10's ANSUR/Deurenberg estimators, and decorative calorie-sized calendar dots. They are not prerequisites for this stage; the first two would need a deliberate journal schema change. Current estimators, photo components and tape focus already cover the required workflows. Do not revive `AtOrEarliest`, the v3 data migration, or classic/debug gating.

See [PRODUCT_STRUCTURE](PRODUCT_STRUCTURE.md), [BACKUP](BACKUP.md), and [VALIDATION](VALIDATION.md) for implementation, recovery and evidence. The earlier audit below is retained as historical context; its decision to keep #10 open applied before this work.

## Earlier forecast-stage audit

Compared `origin/main` **379ba38** with PR #10 head **70bcb2d** (`claude/upbeat-thompson-ebbwjj`). Reviewed both the merge-base diff (60 files, +6687/-1196) and the head-to-head diff. The latter also exposes features subsequently added by merged PRs #9/#11/#12/#13/#14; these must survive.

| Area / evidence in #10 | Classification | Decision |
|---|---|---|
| `Data/BodyHistory.cs`, `BodyResolver.cs`, `HistoryBar`, `CompareCard`, `BodyScene` | Already covered differently / conflicts | Keep main's immutable partial `BodySnapshot`, explicit visual estimates, date comparison and strict storage. #10 carries older values forward per field; `AtOrEarliest` even permits future values. That is unsuitable for forecast evaluation/backtesting. |
| `MainApp`, `Onboarding`, `ProfileSection`, birth date / field provenance | Unique useful UI work | Preserve as follow-up scope in #10. Porting requires adapting the new UI to current history, strength, atlas and forecast APIs; replacing Home would hide these workflows. |
| `Features.cs`, `ClassicApp`, `Home.razor` replacement | Conflicts with this stage | Do not port: the default UI hides hypotheses, forecast and fact comparison behind `?classic=1`. |
| `ActivityLog`, `ActivitySection`, `Workout`, `WorkoutForm.From` | Unique calendar / editable dated cardio | Keep for a separate adapted change. Main already has cardio and strength journals, but does not have this month calendar. #10's calculation on a historical weight needs strict no-future fallback before reuse. The standalone form helper has no current caller, so copying it now would add unused API. |
| `BackupCard`, `Backup.cs`, `backup.js`, `DataJson.BackupData` | Unique useful unified ZIP backup | Keep #10 open for adaptation. Its transaction replaces `profiles/entries/workouts/sessions/images`, but omits main's strength/body-snapshot stores and new forecast history. Porting it unchanged would falsely promise a complete backup. Existing photo ZIP export remains available. |
| `AppData`, `data.js`, `db.js`, `LegacyMigration` | Stale migration / schema conflict | Do not port: IndexedDB v3 / new Data project bypasses main's strict reads, CAS and explicit body-history import. Legacy photo weight/BF copied from metadata must not become measured facts. |
| `AnsurMainGirths`, `DeurenbergBodyFat`, `TapePicker`, extracted photo/workout components | Potential follow-up improvements | Not needed by calibration; main already isolates visual estimates. Review separately with the UI redesign, without treating estimates as factual observations. |
| Viewer / MakeHuman / CSS / tests / documentation | Diverged | Keep main's skeletal animation, atlas/load, factual history and browser smoke. Do not replace newer implementations or testing documentation with the older branch. |

**Decision:** no wholesale merge or cherry-pick. No independent piece improves this forecast stage without bringing unrelated UI/data migration work. PR #10 remains **open**, because unified backup, onboarding and the activity calendar are real remaining work; it is not fully superseded. This audit is committed before the forecast implementation. Its future replacement should target current stores and preserve all newer features. No automatic merge is authorized.
