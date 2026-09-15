# Apply Progress: account-setup-onboarding

Strict TDD. Hybrid store: this file and Engram `sdd/account-setup-onboarding/apply-progress` hold identical bytes. One cumulative section per slice.

## Slice 1 — CUIT or DNI first, CUIT-first copy

Tasks 1.1–1.9 [x]; committed as 5fac8928. Safety net: focused command 22/22 before any edit.

| Task | RED: test, failure reason | GREEN | REFACTOR |
|---|---|---|---|
| 1.1–1.2 | "focuses the CUIT first" plus :45-64, :66-104, :185-206 (refusal names legalName and cuit): 4 failed, focus expected `register-cuit`, received `register-legal-name` | 1.3, 23/23 | ➖ None needed |
| 1.4 | :73-93, :95-140, :342-374: 3 failed, focus expected `personal-document` / `add-personal-document`, received the full-name field | 1.5, 23/23 | ➖ None needed |
| 1.6 | "reads the Company choice CUIT first in en and es": failed, received "…its legal name and CUIT." | 1.7, 24/24 | ➖ None needed |
| 1.8 | — | — | Diff review: sorted lines vs HEAD differ only in three array reorders; one key per catalog; eslint exit 0 |

Triangulation: client check, server `validation_failed`, signed-in refusal, add-personal loop; Tab order on all four forms; en and es copy with Personal unchanged.

| Work unit evidence | Result |
|---|---|
| Focused test | Phase 1 focused command: 2 files, 24/24 passed |
| Runtime harness | V4: 31 passed, 0 failed, 0 skipped (includes the four Phase 1 form journeys) |
| Rollback boundary | Revert the seven SPA files; restores old order, focus maps and copy; no API or data effect |

Verification: V1 vitest 46 files, 634/634; eslint exit 0; vite build exit 0. V2 exit 0, no unused keys. V4 exit 0, 31/31. V5: nothing under `tests/`; only slice files, `tasks.md` and the orchestrator's `state.yaml`.

Changed files (Modified), under `src/Web/ClientApp/src/`: `features/identity/fieldErrors.js`, `features/identity/register/RegisterOrganizationPage.jsx` and `.test.jsx`, `features/identity/people/PersonalPages.jsx` and `.test.jsx`, `i18n/locales/en/identity.json`, `i18n/locales/es/identity.json`. Bookkeeping: `tasks.md`; `apply-progress.md` (Created).

Changed lines: 147 (100 additions, 47 deletions; change folder excluded).

Deviations: none from design §H and §I. Tests also assert Tab order (spec "first in DOM and tab order"; "which precedes Legal name") and the signed-in Legal name description.

## Slice 2a-0 — Pre-change fixtures, no behavior change

Tasks 2.1–2.6 [x]; commit pending orchestrator gate. Safety net (2.1): focused command 34/34 before any edit. Approval tests, so no RED: 2.4 and 2.5 passed with HEAD's handler-built fixture (35/35), then 2.2–2.3 swapped in seeded rows and all stayed green (35/35).

| Task | Test File | Layer | Safety Net | RED / APPROVAL | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 2.2 | `TestApp.cs` | Functional (PostgreSQL) | 34/34 | APPROVAL: rows from domain factories, `ITokenHasher`, `IOutboxSecretWriter` | 35/35 | Live, expired and 4 lifecycle spends | ➖ None needed |
| 2.3 | `ConfirmEmailTests.cs` | Functional | 34/34 | APPROVAL: helper seeds legacy rows; :167-200 unchanged | 35/35 | ➖ Existing cases | ➖ None needed |
| 2.4 | `ConfirmEmailTests.cs` | Functional | N/A (new) | APPROVAL: success; Active tenant and membership; owner named; secret consumed | 35/35 both sources | Expired and lifecycle refusals, same rows | ➖ None needed |
| 2.5 | `RegistrationTests.cs` | Functional | 34/34 | APPROVAL: only seeded tenant (no owner) and profile; no membership, role, assignment, audit, outbox, secret | 35/35 before and after 2.2–2.3 | Free-CUIT graph (`Trusted_matching_session_succeeds…`) | ➖ None needed |

| Work unit evidence | Result |
|---|---|
| Focused test | Phase 2 focused command: exit 0, 35/35 |
| Runtime harness | N/A: fixtures only; V4 31/31 guards regressions |
| Rollback boundary | Revert the three test files; alone before 2a-i, afterwards only with 2a-i; no product, API or data effect |

Verification: V1 vitest 46 files, 634/634; eslint exit 0; vite build exit 0. V2 exit 0, no unused keys. V3 Application.FunctionalTests exit 0, 755/755. V4 exit 0, 31/31. V5: under `tests/` only the three declared files, plus `tasks.md` and the orchestrator's `state.yaml`; nothing untracked.

Changed files (Modified), under `tests/Application.FunctionalTests/`: `Infrastructure/TestApp.cs`, `IdentityAccess/Organizations/ConfirmEmailTests.cs`, `IdentityAccess/Organizations/RegistrationTests.cs`. Bookkeeping: `tasks.md`, `apply-progress.md`.

Changed lines: 86 (80 additions, 6 deletions; change folder excluded; no new files).

Deviations: none from design or tasks. The Owner role carries no catalog grants, as the pre-change handler wrote here (Respawn empties the catalog); no `RegistrationSubmission` is seeded, and confirmation never reads one.