# Stable overlay and daily vocabulary implementation plan
> Execute using superpowers:subagent-driven-development, with isolated responsibilities and regression tests.
Goal: stabilize overlay, add explicitly enabled daily vocabulary inbox, ship self-contained portable exe with embedded dictionaries.
Architecture: stable local anchor estimator; candidate snapshot + explicit selection events; bounded writer to native Windows SQLite; separate WPF daily page; AppPaths for writable config/data and bundled initial data.
Spec: ../specs/2026-11-02-daily-vocabulary-design.md
Constraints: existing WPF .NET10 Windows19041+, no cloud translation, default off, no raw text logging, no all-input claim.
Review focus: cancellation vs commit; stale foreground/pinyin; dedupe per composition; mixed DPI anchor jitter; corrupt or read-only config and migration.
- [x] Task 1: DailyWordStore.cs, DailyCollector.cs and isolated daily tests: persistence, filtering, dedup, selection/cancel, retention and bounded memory. Agent owns new files only.
- [x] Task 2: root PositionStabilizer.cs + tests, Coordinator/Overlay integration. Verify small jitter ignored, real movement follows, missing row holds, channel reset and stale detection.
- [x] Task 3: WPF DailyVocabularyPage.cs + Console navigation; default-off switch, review/edit/export/custom import and safe date deletion. Task-specific UI tests.
- [x] Task 4: root hooks/App integration, process exclusions, no raw candidate logs; portable embedded assets, writable path migration, installation docs and clean-data packaging check.
- [x] Task 5: full build/regressions, isolated UI renders, review, backup/deploy, source synchronization and deliver portable package.
