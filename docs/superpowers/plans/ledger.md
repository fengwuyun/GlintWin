# SDD ledger — plan: docs/superpowers/plans/2026-11-02-daily-vocabulary.md
Base source commit: 88f90a1. Existing staged workspace reused; source synchronized, no separate worktree required.
| Tasks | Shared boundary | Ruling |
| 1 / 3 | Store.Query/Count/Update/DeleteDate | fixed contract in task-1 brief, page size50 |
| 1 / 4 | Collector Observe/Confirm | explicit selection inference always NeedsReview; support numeric only with verified sequential OCR mapping |
| 2 / 4 | Coordinator callbacks | root owns both, no conflicting modifications |
| 3 / 4 | Console/AppPaths | UI agent owns Console + new page; root owns AppPaths/App/Config |
| 1 | tests vs bounded queue | overflow exposed in LastError, not unlimited growth |
| 2 | positions | no fallback to full window after OCR miss; hide after expiry |
| 3 | destructive delete | app confirmation dialog per date, no automatic test of production data |
| 4 | writable assets | default LocalAppData, preserve legacy config/custom assets; clean verification via explicit separate path |
| 5 | verification | isolated app tests, build and package smoke, native UI optional if helper fails |
Ruling: screenshots and raw OCR text removed from normal diagnostic content logging. Collection UI explicitly describes inferred candidates rather than complete input history.
Implementation completed in staging. Backend contributor produced storage/collector and 14 initial tests; root completed queue contention protection, export/frequency tests and full integration after agent became unavailable. Root reviewed null/date/queue/foreground/cancellation and upgrade boundaries. Final tests: Position9, Daily16, Config6, Parser22, UI15; all pass (68 checks). Release build0 warnings/errors. Clean folder containing only192148254-byte EXE self-check:121564 base entries,121645 merged entries,7 resources,.NET10.0.14,OCR available,collection disabled. No clean foreign-machine VM available; UI validated via isolated WPF rendering, no claim of real input-method end-to-end verification. Source/deployment verification follows.
