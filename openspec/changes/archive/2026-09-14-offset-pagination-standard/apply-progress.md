# Apply Progress: Offset Pagination Standard

Change `offset-pagination-standard` · baseline `main` at `2716aa6` · artifact store hybrid (this file plus Engram topic
`sdd/offset-pagination-standard/apply-progress`) · Mode: **Strict TDD** · delivery: one direct-to-main commit after
Phase 13 (DL.0); no git write operation was made during apply.

This artifact is cumulative. Merge every later work unit into it; never drop a prior unit.

## Cumulative task state

| Work unit | Tasks | State |
| --- | --- | --- |
| A1 (Phases 1–3) | 1.1–1.10, 2.1–2.8, 3.1–3.3 | Completed `[x]` (21 tasks) |
| A1 (Phases 1–3) | 1.11 | Completed `[x]` on 2026-09-13, under the baseline-timeout rule and decision A1-01 (see "1.11 closure" in the A1 section) |
| A2 (Phases 4–5) | 4.1–4.5, 5.1–5.2 | Completed `[x]` (7 tasks) |
| A3 (Phases 6–8, one compile unit) | 6.1–6.15, 7.1–7.5, 8.1–8.8 | Completed `[x]` (28 tasks) |
| A4 (Phase 8A, document amendment) | 8A.1–8A.8 | Completed `[x]` (8 tasks) |
| B1 (Phase 9, client pagination helper) | 9.1–9.10 | Completed `[x]` (10 tasks) |
| B2 (Phase 10, SPA clients and useRead) | 10.1–10.8 | Completed `[x]` (8 tasks) |
| C1 (Phase 11, RolesPage, MembersPage, InviteMemberPage, PlatformPanel) | 11.1–11.8 | Completed `[x]` (8 tasks) |
| C2 (Phase 11, PlatformIdentitiesPage, route fixtures, Spanish guard, show-more keys, verification) | 11.9–11.15 | Completed `[x]` (7 tasks) |
| C3 (Phase 11A, read states, past-the-end correction, post-change page refresh) | 11A.1–11A.2, 11A.4–11A.7 | Completed `[x]` (6 tasks) |
| C3 (Phase 11A) | 11A.3 | Completed `[x]` on 2026-09-14 under decision C3-V01 option (a) (see "Decision C3-V01 (a) — 11A.3 closure" at the end of the C3 section). C3 correction round 1 had unmarked it (finding C3-V01), because its confirmation bullet had no evidence on RolesPage |
| C4 (Phase 11A, resume walks, resume and R6 test rewrites, batch C verification) | 11A.8–11A.12 | Completed `[x]` (5 tasks) |
| D1 (Phase 12, search cleanup, D20 standards edits, batch D gate) | 12.2–12.9 | Completed `[x]` (8 tasks) |
| D1 (Phase 12) | 12.1 | Completed `[x]` on 2026-09-14 under decision D1-V01/D1-V02 option (a) (see "Decision D1-V01/V02 (a): 12.1 closure" at the end of the D1 section). D1 and its correction round 1 had left it open on one search match outside the reviewed exclusions, `src/Web/ClientApp/src/api/pagination.js:58` |
| Later units | 13.1 onward, then Delivery | Pending |

Prior work units merged: A1 (Phases 1–3, with its correction round 1), kept verbatim below. A2 is appended after it, A3 after A2, A4 after A3, B1 after A4, B2 after B1's correction round 1, C1 after B2's correction round 1, C2 after C1, C3 after C2, C4 after C3's correction round 1, and D1 after C4.
Correction rounds merged: A1 correction round 1 (validator findings A1-01 to A1-03), recorded below. It preserves every A1 entry. A4 correction round 1 (validator finding A4-01), recorded in the A4 section. It preserves every A4 entry. B1 correction round 1 (validator finding B1-V01), recorded at the end of the B1 section. It preserves every B1 entry. C3 correction round 1 (validator finding C3-V01), recorded at the end of the C3 section. It preserves every C3 entry. D1 correction round 1 (validator findings D1-V01 to D1-V03), recorded at the end of the D1 section. It preserves every D1 entry.
Task closures merged: 1.11, closed on 2026-09-13 under the baseline-timeout rule and decision A1-01, recorded as "1.11 closure" at the end of the A1 section. It preserves every prior entry. 11A.3, closed on 2026-09-14 under decision C3-V01 option (a), recorded as "Decision C3-V01 (a) — 11A.3 closure" at the end of the C3 section, after C3 correction round 1. It preserves every prior entry. 12.1, closed on 2026-09-14 under decision D1-V01/D1-V02 option (a), recorded as "Decision D1-V01/V02 (a): 12.1 closure" at the end of the D1 section, after D1 correction round 1. It preserves every prior entry.

## Work unit A1 — Phases 1, 2 and 3

### 1.1 Baselines (recorded before any edit; `src` and `tests` clean at `2716aa6`)

Logs live in `<scratchpad>` = `C:/Users/ezequ/AppData/Local/Temp/claude/C--Users-ezequ-source-repos-RepositorioBase/44206285-9004-463b-be7a-03b29e719c1b/scratchpad`.

**SPA-G** (`baseline-*.log`): vitest exit 1, eslint exit 0, `npm run i18n:unused` exit 0 ("No unused keys found"), `vite build` exit 0.

- Full Vitest run: 45 files, 42 passed, 3 failed; 6 failed tests:
  - `src/features/identity/IdentityAccessReviewRevalidation.test.jsx` > R6C: directories can reach records after the default 100-item page > continues the 'roles' directory with 101 distinct records (16 772 ms, `Test timed out in 15000ms`)
  - same file > continues the 'members' directory with 101 distinct records (18 803 ms, `Test timed out in 15000ms`)
  - `src/features/identity/ExternalProofResume.test.jsx` > resuming a sensitive operation after a provider round trip > resumes a roles target beyond the first page exactly once with its original draft and version (15 056 ms, `Test timed out in 15000ms`)
  - same file > resumes a members target beyond the first page exactly once with its original draft and version (15 150 ms; an `AssertionError: expected [] to deeply equal [ { …(2) } ]` after 15 s, not a Vitest timeout — corrected in correction round 1)
  - same file > finishes the ownership transfer the person asked for, against the roster as it stands (2 548 ms, under load: `Unable to find an element with the text: Bruno`)
  - `src/features/identity/roles/RolesPage.test.jsx` > roles page > lists the roles with what each one confers, and marks the built-in one (1 421 ms, under load: `Unable to find role="button" and name "Retire Bookkeeper"`; not a timeout)
- Alone (`SPA-F <file>`):
  - `RolesPage.test.jsx` 10/10 pass; `IdentityAccessReviewRevalidation.test.jsx` 11/11 pass.
  - `ExternalProofResume.test.jsx` 7/9, twice: "resumes a roles target beyond the first page…" times out alone (15 107 ms, 15 937 ms); "reports a refused role change that was resumed after the provider round trip" fails after it and passes isolated (`-t`, 2 164 ms), so it is a cascade of the preceding timeout. Machine load at the time: ~1 % CPU, 16 logical processors.
- ~~The three recorded parallel-load tests, by name: (1) `ExternalProofResume` "resumes a members/roles target beyond the first page"; (2) `IdentityAccessReviewRevalidation` R6C "… 101 distinct records"; (3) the third, not rewritten later: `RolesPage.test.jsx` "lists the roles…".~~ **Superseded by the restated record below (finding A1-02):** the RolesPage entry failed at 1 421 ms and is not a timeout. Every failure at or beyond 15 s is in the two files `config.yaml:70` names.

**Restated 1.1 record (correction round 1, finding A1-02).** This is the exact list the baseline-timeout rule applies to at 11A.11, 12.9 and 13.2, and that 13.8 reports. It is mirrored in tasks.md "Conventions and verification commands". Runs: baseline = `baseline-vitest.log` (full) and `baseline-alone-*`; 1.11 = `1.11-vitest.log` and `1.11-alone-*`; validation = `val-a1-vitest.log` and `val-a1-alone-*` (validator's runs); correction = `corr-a1-alone-*`.

| # | File > test | Kind | Under parallel load (full run) | Alone (`SPA-F <file>`) | Leaves the record |
| --- | --- | --- | --- | --- | --- |
| 1 | `ExternalProofResume.test.jsx` > resumes a roles target beyond the first page exactly once with its original draft and version | At or beyond 15 s; Vitest timeout | Timed out: baseline 15 056 ms, 1.11 16 426 ms, validation 15 155 ms | Timed out 4 of 5: 15 107 and 15 937 ms (baseline), 15 118 ms (1.11), 15 059 ms (validation). Passed once: 14 706 ms (correction). It sits at the 15 s edge, so the rule passes for it only by chance (Issues 1) | 11A.8 rewrite |
| 2 | same file > resumes a members target beyond the first page exactly once with its original draft and version | At or beyond 15 s; assertion failure after 15 s, not a timeout | Failed: baseline 15 150 ms, validation 15 818 ms; passed at 1.11 | Passed 5 of 5: 9 818, 9 265, 8 938, 9 086, 9 131 ms | 11A.8 rewrite |
| 3 | `IdentityAccessReviewRevalidation.test.jsx` > R6C > continues the 'roles' directory with 101 distinct records | At or beyond 15 s; Vitest timeout | Timed out: 16 772 ms, 16 333 ms, 16 510 ms | Passed 4 of 4: 9 461, 9 368, 9 428, 9 438 ms (file 11/11 each time) | 11A.10 rewrite |
| 4 | same file > R6C > continues the 'members' directory with 101 distinct records | At or beyond 15 s; Vitest timeout | Timed out: 18 803 ms, 17 712 ms, 18 216 ms | Passed 4 of 4: 13 978, 13 775, 13 742, 13 767 ms | 11A.10 rewrite |
| 5 | `roles/RolesPage.test.jsx` > roles page > lists the roles with what each one confers, and marks the built-in one | Under 15 s; load-sensitive; excuses no other test | Failed at baseline (1 421 ms); passed at 1.11 and validation | File 10/10 at baseline (777 ms) and in correction (772 ms) | Stays |
| 6 | `ExternalProofResume.test.jsx` > finishes the ownership transfer the person asked for, against the roster as it stands | Under 15 s; load-sensitive | Failed at baseline (2 548 ms); passed at 1.11 and validation | Passed 5 of 5: 1 629, 1 570, 1 646, 1 621, 1 637 ms | Stays |
| 7 | same file > does not resume when the members target is missing after return | Under 15 s; load-sensitive | Failed at 1.11 (8 259 ms) and validation (9 145 ms); passed at baseline | Passed 5 of 5: 7 985, 7 917, 7 635, 7 620, 7 569 ms | Stays |
| 8 | same file > reports a refused role change that was resumed after the provider round trip | Under 15 s; cascade of #1 | Failed at 1.11 (1 338 ms, directly after #1 timed out); passed at baseline and validation | Failed 2 of 5, both after #1 timed out (1 413, 1 453 ms, baseline). Passed 1 501 (1.11), 1 509 (validation), 1 317 ms (correction); isolated `-t` 2 164 ms | Stays |
| 9 | `ExternalProofReturnRevalidation.test.jsx` > R6A resumes the original device revocation exactly once after a successful provider return | Under 15 s; load-sensitive | Failed at 1.11 (1 869 ms); passed at baseline and validation | Passed 2 of 2: 1 550 (1.11), 1 481 ms (correction) | Stays |

- "Leaves the record": after 11A.8 and 11A.10, entries 1–4 leave it, and their rewritten tests must pass `SPA-G` outright (13.2, "including both rewritten slow tests"). Entries 5–9 stay under the rule, by file and test name.
- The finding said "four 15s timeouts in two files". The logs show four failures at or beyond 15 s in two files, but only three are Vitest timeouts; #2 is an assertion failure after 15 s. The record states this exactly.
- `openspec/config.yaml:70` still says "3 tests exceed the 15 s timeout … and pass when run alone". That is inaccurate for #1 and for the count. It was not edited, because no finding assigned it (Issues 7).

**`rg -n "eslint-disable" src/Web/ClientApp/src`**: **9** lines at `2716aa6` (tasks.md expected 10): `PlatformPanel.jsx:1`, `PlatformIdentitiesPage.jsx:1`, `PlatformRetentionPage.jsx:1`, `IdentityProvider.test.jsx:11`, `MembersPage.jsx:216`, `ProblemMessage.test.jsx:1` (now `src/components/`), `SessionsPage.jsx:121`, `RolesPage.jsx:236`, `RolesPage.jsx:327`.

**.NET** (one project per process, Docker 29.3.1):
- `NET-G FT`: 748 passed, 0 failed (9 min).
- `NET-G IT`: 370 passed, 0 failed.
- `dotnet test <FT> --filter "TestCategory=IndependentDevelopmentReview"`: 4 tests, **2 failed**, 2 passed. Failing: `Ownership_cannot_be_transferred_to_a_self_deactivated_identity_with_an_active_membership`, `Suspending_a_member_requires_the_recent_primary_proof_retained_by_accepted_C6`. This is the administration-review state 8.7 compares against.
- `dotnet test <IT> --filter "TestCategory=IndependentDevelopmentReview"`: 3 failed (the intentional retention failures): `A_held_first_document_does_not_starve_the_next_eligible_subject`, `A_held_revoked_session_is_preserved_until_hold_release`, `Erasing_a_revoked_session_writes_required_policy_evidence`.

**LIST-CODES**: `dotnet build src/Web/Web.csproj` (exit 0, regenerates `v1.json`), then the tasks.md node command → `<scratchpad>/list-problem-codes.baseline.json`. It matches the list-read-error-contract table exactly: the four Platform directories declare `400` [`invalid_request`, `validation_failed`], `401` [`authentication_required`, `invalid_session`, `recent_mfa_required`], `403` [`permission_denied`], `500` [`internal_server_error`]; the three tenant lists declare `400` [`invalid_request`], `401` [`authentication_required`, `invalid_session`], `403` [`permission_denied`], `404` [`not_found`], `500` [`internal_server_error`].

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | — | Baseline record | N/A (no edit) | N/A | N/A | N/A | N/A. Record restated in correction round 1 (A1-02), from the same logs plus the correction runs |
| 1.2 | `src/test/identityFiles.contract.test.js` | Unit (filesystem contract) | ✅ SPA-G baseline (file passed) | ✅ Written: `:28` → `api/problemDetails.js`; ran FAIL "api/problemDetails.js is missing" | ✅ 21/21 after the move (1.9) | ➖ Single: the coverage table scopes this RED to the `src/api/problemDetails.js` path only | ➖ None needed |
| 1.3 | `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs` | Functional (served OpenAPI + repo file) | ✅ NET-G FT 748/748 | ✅ Written: `:491` → `src/Web/ClientApp/src/api/problemCodes.json`; ran FAIL `DirectoryNotFoundException` (1 failed, 16 passed) | ✅ 17/17 after the move | ➖ Single path | ➖ None needed |
| 1.4 | moves `problemDetails.js`, `apiTransport.js`, `problemCodes.json` → `src/api/`; `ProblemMessage.jsx` → `src/components/` | Refactor move (PD-5: RED-less by design) | ✅ baseline + REDs 1.2/1.3 | N/A (existing tests are the approval tests) | ✅ 1.9 run 9 files/161 tests; OpenApi 17/17; `problemCodes.json` normalized hash equals blob `cb1efdcc…` | ✅ proven by the 1.9 searches and runs | ➖ Import paths only |
| 1.5 | `src/components/problemFields.js` (new), `features/identity/fieldErrors.js` | Refactor move (PD-5) | ✅ `fieldErrors.test.js` 4/4 | N/A (PD-5 adds no test) | ✅ 1.9: `fieldErrors.test.js`, `ProblemMessage.test.jsx`, `MfaRecoveryPage`, `PlatformInvitationPages`, `PlatformPanel` pass | ✅ searches: one import + nine exports; one import + four exports | ➖ Bodies moved verbatim |
| 1.6 | 24 importers (17 mechanical, 5 screens, `useRead.js`, `identityClient.js`) | Refactor (import paths) | ✅ baseline | N/A | ✅ 1.9 runs; 1.11 `vite build` exit 0 | ✅ `rg "fieldErrors'"` lists only PersonalPages, RegisterOrganizationPage, fieldErrors.test.js | ➖ |
| 1.7 | `identityServer.js`, `catalog.contract.test.js`, `problemCatalogue.contract.test.js`, `platformClient.test.js`, `platformDirectory.test.js` | Test import paths | ✅ baseline | N/A | ✅ 1.9/1.10 runs; 1.11 full run | ➖ | ➖ |
| 1.8 | moved `src/api/problemDetails.test.js`, `src/api/apiTransport.test.js`, `src/components/ProblemMessage.test.jsx` | Test moves (import paths only) | ✅ baseline | N/A | ✅ 25/25, 36/36, 24/24 | ➖ | ➖ |
| 1.9 | 9 files (see Focused results) | Verify | — | — | ✅ 9 files, 161 tests; `NET-F FT OpenApiContractTests` 17/17; all four searches as specified | — | — |
| 1.10 | `ProblemMessage.test.jsx`, `spanish.test.jsx`, `presentation.test.jsx`, `catalog.contract.test.js` | Unit/component (L6 guards) | — | N/A (guards) | ✅ 4 files, 66 tests; eslint on both modules exit 0; locales diff empty | — | — |
| 1.11 | SPA-G | Gate | baseline recorded at 1.1 | — | ⚠️ vitest 6 failed/542 passed (548); eslint 0; i18n 0; vite 0. Alone: `ExternalProofReturnRevalidation` 1/1, `IdentityAccessReviewRevalidation` 11/11, `ExternalProofResume` 8/9 (only record #1, the roles-target timeout, 15 118 ms). Correction round 1: a further alone run of `ExternalProofResume` passed 9/9 (#1 at 14 706 ms), which shows #1 sits at the 15 s edge; one lucky rerun is not gate evidence. **Not checked**: awaits the orchestrator's decision on A1-01 (Issues 1). **Closed later:** checked on 2026-09-13 after decision A1-01; see "1.11 closure" below | — | — |
| 2.1 | `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs` | Functional (HTTP, real PostgreSQL) | ✅ 748/748 | ✅ Approval test written first: `List_create_and_bodyless_successes_carry_no_universal_envelope` (task: expect PASS before the refactor) | ✅ class 36/36; name filter Total 1, passed | ✅ three success kinds: list `200`, create `201`, bodyless `204` | ➖ None needed |
| 2.2 | `src/Web/Infrastructure/ResultHttpExtensions.cs` | Mapping overload | ✅ 2.1 approval | N/A (task: no new test; AD8 proof list) | ✅ build clean; 2.7 325/325 | ➖ | ➖ |
| 2.3 | 24 `NoContent` sites | Refactor (AD8) | ✅ 2.1 approval + 748 baseline | N/A (refactor) | ✅ 2.7 325/325; 2.8 749/749 | ✅ 2.7 search empty | ✅ |
| 2.4 | 18 `Ok`/`Created` sites | Refactor (AD8) | ✅ same | N/A | ✅ same | ✅ same | ✅ The 2.7 search caught three RoleEndpoints sites (Create, Update, Retire) my first span edit missed; fixed before the build and runs |
| 2.5 | 8 bodyless `202` sites | Refactor (AD8) | ✅ same | N/A | ✅ same; exactly 8 `ToAcceptedHttpResult(context` callers | ✅ neutral-flow classes in the 2.7 filter | ✅ |
| 2.6 | `ApiProblemMetadata.cs` | Optional | — | — | ➖ No edit (default); no design.md row needed | — | — |
| 2.7 | 14 FT classes | Verify | — | — | ✅ 325/325; `rg -n -U "IsSuccess\s*\?[^;]*ToHttpResult\(result\.Error" src/Web/Endpoints` finds nothing | — | — |
| 2.8 | `NET-G FT` | Gate | 748/748 at 1.1 | — | ✅ 749/749 (748 + the 2.1 test); no new failure | — | — |
| 3.1 | — | Adjusted (D04) | — | — | ➖ No edit: path moved at 1.3 | — | — |
| 3.2 | `src/Web/ClientApp/src/features/identity/problemCatalogue.contract.test.js` | Unit (catalogue contract hardening) | ✅ 5/5 at 1.9 | N/A (task: test hardening, expect PASS) | ✅ 6/6: words gate ran for `en` and `es` by name; new registry guard passes. Correction round 1 re-run: 6/6 | ✅ languages come from `languages.json` `supported`; the guard asserts the source language is supported and every supported language has an `errors.json`, so the gate cannot loop zero times | ➖. ⚠️ The guard test exceeds declared row 19 (finding A1-03); awaits the orchestrator's decision (Deviations) |
| 3.3 | SPA + FT | Verify | — | — | ✅ SPA-F 2 files/23 tests; `NET-F FT OpenApiContractTests|ErrorCatalogContractTests` green inside the 2.7 run. Correction round 1 re-run: SPA-F 2 files/23 tests, exit 0 | — | — |

### Test Summary

- Tests written: 2 new (`List_create_and_bodyless_successes_carry_no_universal_envelope`; `reads every supported language, the source included, from the registry for the words gate`), plus 2 RED edits of existing tests (1.2, 1.3) and the parameterised words gate (3.2).
- Tests passing: all focused runs green; `NET-G FT` 749/749.
- Layers used: Unit (Vitest contract) 3, Functional (HTTP/PostgreSQL) 2.
- Approval tests (refactoring): 1 (2.1), plus the existing suites run unchanged for the PD-5 moves.
- Pure functions created: 0 (the nine helpers moved verbatim).
- Correction round 1: no test was written, edited, moved or deleted, and no production code changed. Only planning and progress artifacts changed. No new RED → GREEN cycle exists, so none is claimed.

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/api/problemDetails.test.js src/api/apiTransport.test.js src/components/ProblemMessage.test.jsx src/features/identity/problemCatalogue.contract.test.js src/test/identityFiles.contract.test.js src/features/platform/PlatformPanel.test.jsx src/features/platform/invitations/MfaRecoveryPage.test.jsx src/features/platform/invitations/PlatformInvitationPages.test.jsx src/features/identity/fieldErrors.test.js` → exit 0, 9 files, 161 tests. `npx vitest run src/components/ProblemMessage.test.jsx src/i18n/spanish.test.jsx src/i18n/presentation.test.jsx src/i18n/catalog.contract.test.js` → exit 0, 4 files, 66 tests. `npx vitest run --reporter=verbose src/features/identity/problemCatalogue.contract.test.js src/i18n/catalog.contract.test.js` → exit 0, 2 files, 23 tests. `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~OpenApiContractTests"` → 17/17. `… --filter "FullyQualifiedName~ProblemDetailsContractTests"` → 36/36. 2.7 filter (14 classes) → 325/325. **Correction round 1** (sequential, one file per process, `corr-a1-*` logs): `npx vitest run src/features/identity/ExternalProofResume.test.jsx` → exit 0, 9/9 (#1 14 706 ms); `npx vitest run src/features/identity/IdentityAccessReviewRevalidation.test.jsx` → exit 0, 11/11; `npx vitest run src/features/identity/roles/RolesPage.test.jsx` → exit 0, 10/10; `npx vitest run src/features/identity/ExternalProofReturnRevalidation.test.jsx` → exit 0, 1/1; `npx vitest run --reporter=verbose src/features/identity/problemCatalogue.contract.test.js src/i18n/catalog.contract.test.js` → exit 0, 2 files, 23 tests. |
| Runtime harness command/scenario and exact result | `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "TestCategory!=IndependentDevelopmentReview"` over real PostgreSQL through Aspire (Docker 29.3.1) → exit 0, 749/749 in 8 m 52 s. The journeys are N/A for this unit (tasks.md Suggested Work Units: batch A's runtime harness is `NET-G FT`; Phase 13 owns the journeys, which were not run). SPA-G full run: vitest 6 failed/542 passed (baseline-equivalent; Issues 1), eslint 0, `i18n:unused` 0, `vite build` 0. Correction round 1: N/A. No code, test or runtime path changed, so the harness was not rerun; the affected focused files were. |
| Rollback boundary | Phase 1: move `src/api/{problemDetails,apiTransport}.js`, `src/api/{problemDetails,apiTransport}.test.js`, `src/api/problemCodes.json`, `src/components/ProblemMessage.jsx` and `.test.jsx` back to `features/identity/…`; delete `src/components/problemFields.js`; restore `features/identity/fieldErrors.js`; revert the import lines in the 24 importers and in `identityServer.js`, `catalog.contract.test.js`, `problemCatalogue.contract.test.js`, `platformClient.test.js`, `platformDirectory.test.js`, `identityFiles.contract.test.js:28`, and `OpenApiContractTests.cs:491` (couples to the catalogue move). No behaviour change. Phase 2: revert `ResultHttpExtensions.cs` and the 15 endpoint files independently of Phase 1 (answers are identical); the 2.1 test can stay. Phase 3: revert the words gate in `problemCatalogue.contract.test.js`. No data or schema change. Correction round 1: revert only the tasks.md text it changed (the Conventions rule plus its 1.1 record, the 1.1 sub-bullet, the Test-file boundary sentence after the known slow tests, and the 13.2 clause) and this file's restated record; no code. |

### Files changed (A1)

| File (from repository root unless noted) | Action |
| --- | --- |
| `src/Web/ClientApp/src/api/problemDetails.js` | Moved from `features/identity/api/`; schema import depth 6 → 4 |
| `src/Web/ClientApp/src/api/apiTransport.js` | Moved from `features/identity/api/`; unchanged content |
| `src/Web/ClientApp/src/api/problemCodes.json` | Moved from `features/identity/`; bytes unchanged |
| `src/Web/ClientApp/src/components/ProblemMessage.jsx` | Moved from `features/identity/`; imports `../i18n`, `./problemFields` |
| `src/Web/ClientApp/src/api/problemDetails.test.js` | Moved (row 8); no content change |
| `src/Web/ClientApp/src/api/apiTransport.test.js` | Moved (row 18); import paths |
| `src/Web/ClientApp/src/components/ProblemMessage.test.jsx` | Moved (row 17); import paths |
| `src/Web/ClientApp/src/components/problemFields.js` | Created (PD-5): nine exports and three private members moved verbatim |
| `src/Web/ClientApp/src/features/identity/fieldErrors.js` | Modified: four registration exports only; imports only `./register/cuit` |
| 24 importers: `components/NavMenu.jsx`; `features/identity/{context/IdentityProvider, credentials/PasswordPages, credentials/ExternalAccountsPage, lifecycle/AccountLifecyclePages, invitations/InvitationPages, invitations/InviteMemberPage, login/LoginPage, people/PersonalPages, members/MembersPage, register/ConfirmEmailPage, register/RegisterOrganizationPage, tenants/TenantSelector, sessions/SessionsPage, roles/RolesPage}.jsx`, `features/identity/{useSubmit,useRead}.js`, `features/identity/api/identityClient.js`; `features/platform/{PlatformPanel, identities/PlatformIdentitiesPage, invitations/MfaRecoveryPage, invitations/PlatformInvitationPages, retention/PlatformRetentionPage, shared/PlatformStepUpForm}.jsx` | Modified: import paths only (PersonalPages and RegisterOrganizationPage split their import) |
| `src/Web/ClientApp/src/test/identityFiles.contract.test.js` | Modified (row 16): `:28` entry |
| `src/Web/ClientApp/src/test/identityServer.js` | Modified (row 21): import path |
| `src/Web/ClientApp/src/i18n/catalog.contract.test.js` | Modified (row 20): import path |
| `src/Web/ClientApp/src/features/platform/platformClient.test.js`, `platformDirectory.test.js` | Modified (rows 11, 12): import path |
| `src/Web/ClientApp/src/features/identity/problemCatalogue.contract.test.js` | Modified (row 19): import path; words gate reads `languages.supported`; registry guard (the guard exceeds row 19 as declared; A1-03 pending) |
| `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs` | Modified (row 38): `:491` catalogue path |
| `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs` | Modified (row 39): 2.1 characterization test and helper |
| `src/Web/Infrastructure/ResultHttpExtensions.cs` | Modified: `ToAcceptedHttpResult` |
| `src/Web/Endpoints/Identity.cs`; `Identity/{AccountLifecycle,Context,DocumentDispute,ExternalLogin,Invitation,Membership,Password,Personal,Role,Session}Endpoints.cs`; `Platform/{PlatformEndpoints,PlatformInvitationEndpoints,PlatformMfaEndpoints,PlatformRetentionEndpoints}.cs` | Modified: 50 AD8 sites (endpoints + extensions: +72/−78) |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes. Correction round 1 restated the 1.1 record: the Conventions rule and its record list, a 1.1 sub-bullet, the Test-file boundary sentence after the known slow tests, and the 13.2 clause |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Created by A1; merged by correction round 1 |

Test-file boundary: `git status --short --untracked-files=all -- tests src` lists only declared rows 8, 11, 12, 16–21, 38 and 39 among test files (moved-from paths of rows 8, 17, 18 show ` D`). `fieldErrors.test.js` (row 22) is unchanged, as PD-5 expects. Correction round 1 touched no test file. Its `git status` shows the same entries.

### Correction round 1 — validator findings A1-01 to A1-03 (2026-09-13)

| Finding | Disposition | Change made | Evidence |
| --- | --- | --- | --- |
| A1-01 (should-fix, 1.11) | **Not applied.** The fix is an orchestrator decision between (a) excusing record #1 until 11A.8 rewrites it and (b) deferring 1.11 so 11A.11 closes it. The executor does not choose. 1.11 stays `[ ]`, and `testTimeout` is unchanged. | The tasks.md record marks #1 as awaiting that decision. | #1 timed out alone in 4 of 5 runs and passed once at 14 706 ms (`corr-a1-alone-epr.log`, exit 0, 9/9). So (a) would excuse a test that passes alone only by chance. No other entry blocks the rule. |
| A1-02 (should-fix, 1.1) | **Applied.** | tasks.md Conventions: the rule matches the 1.1 record by file and test name; a failing test that is not on it is not excused; 11A.11, 12.9 and 13.2 apply it to exactly that list, and 13.8 reports it. The record lists four entries at or beyond 15 s in two files and five under 15 s, each with its alone evidence. RolesPage #5 excuses no other test. Also a 1.1 sub-bullet, the Test-file boundary sentence ("no third timeout file exists") and the 13.2 clause. This file's "1.1 Baselines" is restated. | The logs table above. One wording differs from the finding: it said four 15 s timeouts, but #2 is an assertion failure after 15 s, so the record says "three Vitest timeouts and one assertion failure". |
| A1-03 (should-fix, 3.2) | **Not applied.** The fix is an orchestrator decision: accept the guard and amend tasks.md row 19 and design.md:386, or remove it. The executor does not choose; the test file and both planning rows are unchanged. | None. | The guard still passes (6/6, `corr-a1-3.3-spa-f.log`). Evidence for that decision: Vitest 3.2's `taskFn.each` (`src/Web/ClientApp/node_modules/@vitest/runner/dist/chunk-hooks.js:840-866`) runs `cases.forEach` and registers zero tests for an empty array, with no error. Without the guard's `languages.supported` contains `languages.source` assertion, an empty `supported` list would skip the words gate silently. |

11A.11 and 12.9 were not edited: they already run "under the baseline-timeout rule", which now points at the restated record. 13.8 already reports "every use of the baseline-timeout rule".

### Deviations from design

- None in behaviour. `platformClient.js` has no imports (its transport is injected), so of "both clients" at 1.6 only `identityClient.js` changed; the design importer count of 24 still holds.
- 3.2 adds one guard test beside the words gate, so the registry-driven `it.each` can never iterate zero times; the client-only-code check, the MSW guard and the catalogue checks are unchanged. Validator finding A1-03: tasks.md Test-file boundary row 19 ("Import path; words gate reads `languages.supported`") and the design.md File Changes row (`design.md:386`) do not name this guard or its `import.meta.glob` catalogue lookup. So the 13.6 comparison does not match the declared edit kind until the orchestrator either amends both rows or has the guard removed.

### Issues found

1. **1.11 left unchecked; awaits the orchestrator's decision on A1-01.** The baseline-timeout rule requires a recorded test that fails SPA-G to pass alone. Record #1, `ExternalProofResume` "resumes a roles target beyond the first page exactly once…", timed out alone at `2716aa6` (15 107, 15 937 ms), after Phase 1 (15 118 ms) and at validation (15 059 ms). It passed alone once, at 14 706 ms, in correction round 1. It sits at the 15 s edge and passes alone only by chance. Every other SPA-G failure passes alone, before and after; lint, `i18n:unused` and `vite build` pass. So Phase 1 adds no regression, but 1.11 needs the orchestrator to choose: (a) excuse #1 until 11A.8 rewrites it, or (b) defer 1.11 and close it at 11A.11. `testTimeout` was not raised.
2. The load-sensitive Vitest set varies between runs and was undercounted as "3 recorded timeouts". Correction round 1 restated it as the nine-entry 1.1 record (A1-02); the table under "1.1 Baselines" is authoritative.
3. `rg -n "eslint-disable" src/Web/ClientApp/src` is 9 lines at baseline, not 10 (tasks.md 1.1). 11A.11 and 13.2 should compare with 9.
4. `IndependentDevelopmentAdministrationReviewTests` (FT category) has 2 failing tests at baseline (named above); 8.7 must match that.
5. The Write tool saved `fieldErrors.js` and `problemFields.js` with LF while the working tree is CRLF. `core.autocrlf=true` and no `.gitattributes`, so git normalizes them; `git diff` shows only content lines.
6. **A1-03 awaits the orchestrator's decision** (see Deviations and the correction-round table).
7. `openspec/config.yaml:70` `known_baselines` still says the full Vitest run has "3 tests [that] exceed the 15 s timeout under parallel load and pass when run alone". The restated record contradicts both the count and "pass when run alone" (for #1). It was not edited, because no finding assigned it.

### Remaining tasks

- 1.11 (awaits the A1-01 decision), then Phases 4, 5, 6, 7, 8, 8A (batch A), 9–10 (B), 11–11A (C), 12 (D), 13 and Delivery.

### Workload / PR boundary

- Mode: `size:exception` accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply).
- Current work unit: A1 (Phases 1–3), with correction round 1.
- Boundary: starts at `2716aa6` with clean `src`/`tests`; ends with the neutral SPA problem modules, the AD8 mapping migration and the hardened catalogue gate. The wire shape is unchanged. Correction round 1 changed only `openspec/changes/offset-pagination-standard/{tasks,apply-progress}.md`.
- Review impact: about 90 changed lines in endpoints and the extension, about 60 import lines in the SPA, the PD-5 file split (98 moved lines), 7 file moves, and about 60 test lines. Correction round 1 adds about 20 planning lines and no code.

### 1.11 closure — `SPA-G` under the baseline-timeout rule and decision A1-01 (2026-09-13)

Scope: task 1.11 only. Decision A1-01 (user, 2026-09-13) is recorded in tasks.md "Conventions and verification commands". It excuses record #1 in every intermediate `SPA-G` gate until 11A.8 rewrites it, and record #8 when #8 fails directly after #1's timeout. The orchestrator also recorded decision A1-03 (the 3.2 guard test is accepted) in tasks.md row 19 and in design.md File Changes `:386` and the testing note near `:454`. That settles A1 Deviations (3.2) and Issues 6. This closure changed no source or test file. `testTimeout` stays at 15000 ms, and no git write was made. Logs are `<scratchpad>/1.11-close-*`.

**Gate run.** `1.11-close-run.sh` ran each step from `src/Web/ClientApp`, even after an earlier step failed. Exit codes and timestamps are in `1.11-close-exits.txt` (19:24:19–19:27:23, -03:00).

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Vitest | `npx vitest run` | 1 | Test Files 2 failed / 43 passed (45); Tests 5 failed / 544 passed (549); 127.87 s | `1.11-close-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output: no problem reported | `1.11-close-eslint.log` |
| Unused keys | `npm run i18n:unused` | 0 | "No unused keys found" | `1.11-close-i18n.log` |
| Build | `npx vite build` | 0 | 2,628 modules transformed, built in 1.93 s. The only warning is the chunk-size notice the earlier 1.11 run also printed. No missing export, so no stale import of a moved name | `1.11-close-vite.log` |

- 549 tests = the 548 of the first 1.11 run plus the 3.2 guard test.
- The log has 66 MSW unhandled-request warnings, the same count as the baseline, first 1.11 and validation logs. No run has an `Errors` summary line or a file-level `FAIL`.
- The gate writes only the gitignored `src/Web/ClientApp/build`. `git status` over `src/Web/ClientApp` and `tests` lists 63 entries, none outside `src/Web/ClientApp/src` and the two test projects (`1.11-close-git-status.txt`).

**Classification of every failure** (`1.11-close-classification.txt`, `1.11-close-alone-classification.txt`). Matched by file and test name against the 1.1 record. The alone reruns ran one file per process, one after the other, after the gate had finished (`1.11-close-alone-exits.txt`).

| Record | File > test | Full run | Failure | Alone (`SPA-F <file>`) | Disposition |
| --- | --- | --- | --- | --- | --- |
| #1 | `ExternalProofResume.test.jsx` > resumes a roles target beyond the first page exactly once with its original draft and version | × 16 722 ms | `Test timed out in 15000ms` | ✓ 11 261 ms (file 9/9, exit 0) | **Excused by decision A1-01.** It also passed alone this time, but four of its five earlier alone runs timed out, so the exception, not the rerun, is the ground |
| #2 | same file > resumes a members target beyond the first page exactly once with its original draft and version | × 15 015 ms | `expected [] to deeply equal [ { …(2) } ]` (assertion, not a timeout) | ✓ 7 896 ms | Baseline-timeout rule: passes alone |
| #7 | same file > does not resume when the members target is missing after return | × 9 011 ms | `expected [ …(3) ] to deep equally contain { cursor: 'after-100', returned: true }` (assertion under load) | ✓ 6 386 ms | Baseline-timeout rule: passes alone |
| #3 | `IdentityAccessReviewRevalidation.test.jsx` > R6C > continues the 'roles' directory with 101 distinct records | × 16 013 ms | `Test timed out in 15000ms` | ✓ 7 442 ms (file 11/11, exit 0) | Baseline-timeout rule: passes alone |
| #4 | same file > R6C > continues the 'members' directory with 101 distinct records | × 17 253 ms | `Test timed out in 15000ms` | ✓ 10 117 ms | Baseline-timeout rule: passes alone |

- **Failing tests not on the record:** none. The 5 failures are exactly #1, #2, #3, #4 and #7.
- **Record entries that passed the full run:**
  - #5 RolesPage "lists the roles…" (1 373 ms);
  - #6 "finishes the ownership transfer…" (3 498 ms);
  - #8 "reports a refused role change…" (996 ms);
  - #9 R6A (2 492 ms).
- #8 ran directly after #1's timeout (`1.11-close-vitest.log:1147,1150`) and passed.
- **Uses of the baseline-timeout rule:** 4 (#2, #3, #4, #7). All four passed alone.
- **Uses of the A1-01 exception:** 1 (#1), and it was not load-bearing in this run, because #1 also passed alone. The #8 cascade clause was used 0 times.
- 13.8 reports these uses.

#### TDD Cycle Evidence (1.11 closure)

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.11 | `SPA-G`: the whole Vitest suite, `eslint src/`, `i18n:unused`, `vite build` | Gate | ✅ The 1.1 record (baseline `2716aa6`) and its restated nine entries | N/A: a gate writes no test. The moves it guards had their REDs at 1.2 and 1.3 | ✅ **Vitest** exit 1: 5 failed / 544 passed (549). Every failure is on the 1.1 record: #2, #3, #4 and #7 pass alone under the rule, and #1 is excused by A1-01 (it also passed alone, 11 261 ms). **eslint** exit 0. **`i18n:unused`** exit 0. **`vite build`** exit 0 | N/A: a gate has no behaviour of its own to triangulate | N/A: no code or test changed |

#### Work Unit Evidence (1.11 closure)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | Alone reruns, sequential: `cd src/Web/ClientApp && npx vitest run src/features/identity/ExternalProofResume.test.jsx` → exit 0, 1 file, 9/9 (`1.11-close-alone-ExternalProofResume.log`). `npx vitest run src/features/identity/IdentityAccessReviewRevalidation.test.jsx` → exit 0, 1 file, 11/11 (`1.11-close-alone-IdentityAccessReviewRevalidation.log`). |
| Runtime harness command/scenario and exact result | `SPA-G`, each step run on its own: `npx vitest run` → exit 1, Test Files 2 failed / 43 passed (45), Tests 5 failed / 544 passed (549), every failure classified above. `npx eslint src/` → exit 0. `npm run i18n:unused` → exit 0, "No unused keys found". `npx vite build` → exit 0, 2,628 modules. Journeys: N/A for batch A. Phase 13 owns them, and the strict client reader (E7) rejects the offset shape until batches B and C land. |
| Rollback boundary | Uncheck 1.11 in tasks.md, and remove this section and the 1.11 row change in "Cumulative task state". No code, test, data or schema change. |

## Work unit A2 — Phases 4 and 5

Scope: tasks 4.1–4.5 and 5.1–5.2, and nothing else. Before the first edit, `git status --short -- src/Application src/Infrastructure tests/Application.UnitTests` was empty. Logs are `<scratchpad>/a2-*.log`. No git write was made, and no parallel-work file was touched.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 4.1 | `tests/Application.UnitTests/Common/Models/PaginationQueryTests.cs` (new, row 40) | Unit (NUnit + Shouldly) | ✅ `NET-G UT` 236/236 before any edit (`a2-safety-ut.log`) | ✅ Written first: plan:336-376 verbatim (4 methods, 6 cases), plus `An_omitted_value_takes_the_default_page` with `From(null, null)` → 1/25 and `From(2, null)` → 2/25. Ran `NET-F UT PaginationQueryTests`: exit 1, 7 × CS0103/CS0246 "`PaginationQuery` does not exist", every error in this file (`a2-4.1-red.log`) | ✅ at 4.2 | ✅ 8 cases: the default page; 0/500 → 1/100; -5/-5 and 0/0 → 1/1; 3/10 unchanged; `int.MaxValue`/`int.MaxValue` → `MaxPageNumber`/100 with `Skip` not throwing; the two `From` cases | ➖ None needed |
| 4.2 | `src/Application/Common/Models/PaginationQuery.cs` (new) | Unit | N/A (new) | ✅ 4.1 | ✅ `NET-F UT PaginationQueryTests`: exit 0, 8/8 (`a2-4.2-green.log`); no compiler warning (warnings are errors in `Directory.Build.props`) | ✅ The clamp cases force `Math.Clamp` on both members with different bounds; `From(2, null)` forces a per-member default rather than a constant page | ➖ None needed: the design contract as written |
| 4.3 | `tests/Application.UnitTests/Common/Models/PaginatedListTests.cs` (new, row 41) | Unit (NUnit + Shouldly) | N/A (new) | ✅ Written first: three tests with D03's PascalCase named arguments. Ran `NET-F UT PaginatedListTests`: exit 1, 3 × CS0246 "`PaginatedList<>` not found", every error in this file (`a2-4.3-red.log`) | ✅ at 4.4 | ✅ Three paths: a middle page (8 rows, page 2, size 3 → 3 pages, both flags true); an empty collection (`TotalCount` 0 → 0 pages, both flags false, the `TotalCount == 0` branch); a page past the end (page 5, size 2, 3 rows → 2 pages, `HasNextPage` false) | ➖ None needed |
| 4.4 | `src/Application/Common/Models/PaginatedList.cs` (new) | Unit | N/A (new) | ✅ 4.3 | ✅ `NET-F UT PaginatedListTests`: exit 0, 3/3 (`a2-4.4-green.log`) | ✅ as 4.3 | ➖ None needed: plan:436-449 members as written |
| 4.5 | both test files | Verify | — | — | ✅ `NET-F UT "PaginationQueryTests|PaginatedListTests"`: exit 0, 11/11 (`a2-4.5-verify.log`) | — | ➖ No refactor, as the task says |
| 5.1 | `src/Infrastructure/Data/Pagination/PaginationExtensions.cs` (new) | Build only (AD18: no test file of its own) | ✅ `NET-G UT` 236/236 (UT references Infrastructure) | N/A by design: AD18 and task 5.1 assign the helper's proof to 6.3, 6.6 and 6.8 on PostgreSQL, green at 8.4 | ✅ `dotnet build src/Infrastructure/Infrastructure.csproj`: exit 0, 0 errors (`a2-5.1-build.log`). `NET-G UT` afterwards: exit 0, 247/247 = 236 + 11 (`a2-ut-after.log`) | ➖ Deferred to 6.3, 6.6 and 6.8 (AD18) | ➖ None needed |
| 5.2 | same file | Review | — | — | ✅ The helper never orders: `rg -n -i "OrderBy|ThenBy|cursor"` over the file finds nothing, and its XML doc says the caller orders by a unique key first (7.1–7.4 apply it) | — | — |

### Test Summary

- Tests written: 2 new files with 8 test methods and 11 cases. `PaginationQueryTests` has 5 methods and 8 cases; `PaginatedListTests` has 3 methods and 3 cases.
- Tests passing: 11/11 focused; `NET-G UT` 247/247 (baseline 236/236).
- Layers used: Unit 11. Integration and E2E: none in this unit (AD18 puts the helper's PostgreSQL proof in 6.3, 6.6 and 6.8).
- Approval tests (refactoring): none; no existing code was refactored.
- Pure functions created: `PaginationQuery` (clamping constructor, `Skip`, `From`) and `PaginatedList<T>` (`TotalPages`, `HasPreviousPage`, `HasNextPage`) are pure. `ToPaginatedListAsync` performs I/O by design.

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~PaginationQueryTests|FullyQualifiedName~PaginatedListTests"` → exit 0, 11 passed, 0 failed (`a2-4.5-verify.log`). `dotnet build src/Infrastructure/Infrastructure.csproj` → exit 0, 0 errors (`a2-5.1-build.log`). `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "TestCategory!=IndependentDevelopmentReview"` → exit 0, 247/247 (236 before the unit; `a2-safety-ut.log`, `a2-ut-after.log`). |
| Runtime harness command/scenario and exact result | N/A for this unit. The two models are pure records with no runtime boundary. The EF helper has no caller until 7.1–7.3, so no route, handler or store executes it yet. AD18 and task 5.1 assign its real-PostgreSQL proof to 6.3, 6.6 and 6.8 (`NET-F FT`), green at 8.4, and `NET-G FT` at 8.7. No SPA or wire shape changed, so neither the journeys nor `NET-G FT` were rerun. |
| Rollback boundary | Delete the five new files: `src/Application/Common/Models/{PaginationQuery,PaginatedList}.cs`, `src/Infrastructure/Data/Pagination/PaginationExtensions.cs` (and its now-empty folder), and `tests/Application.UnitTests/Common/Models/{PaginationQueryTests,PaginatedListTests}.cs`. Uncheck 4.1–4.5, 5.1 and 5.2 in tasks.md. No other source or test file references them, so nothing else changes; A1 is unaffected. No data or schema change. |

### Files changed (A2)

| File (from repository root) | Action |
| --- | --- |
| `src/Application/Common/Models/PaginationQuery.cs` | Created (4.2): the clamped offset request, `MaxPageNumber = int.MaxValue / MaxPageSize`, `From(int?, int?)` |
| `src/Application/Common/Models/PaginatedList.cs` | Created (4.4): items plus offset metadata; no EF dependency |
| `src/Infrastructure/Data/Pagination/PaginationExtensions.cs` | Created (5.1): `ToPaginatedListAsync` with explicit `CleanArchitecture.Application.Common.Models` and `Microsoft.EntityFrameworkCore` usings (D02) |
| `tests/Application.UnitTests/Common/Models/PaginationQueryTests.cs` | Created (row 40, 4.1) |
| `tests/Application.UnitTests/Common/Models/PaginatedListTests.cs` | Created (row 41, 4.3) |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 4.1–4.5, 5.1 and 5.2 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: A2 appended; the cumulative table updated; every A1 entry kept |

Test-file boundary: `git status --short --untracked-files=all -- src/Application src/Infrastructure tests/Application.UnitTests` lists exactly the five new files. The only test files among them are declared rows 40 and 41, both created as their rows say. No other test file was created, edited, moved or deleted.

### Deviations from design (A2)

- None in behaviour.
- `PaginationQuery.cs` writes the constants on separate lines, as plan:397-402 does, instead of the design sketch's single `const` list. It adds XML docs to `PageSize` and `From`. It leaves out the design sketch's "Replaces PlatformEndpoints.Page" sentence, because `PlatformEndpoints.Page()` is deleted only at 8.1 and the sentence would be false until then.
- `PaginatedList.cs` adds a type-level XML summary to plan:436-449; its members are as written.
- The two `From` cases of 4.1 are one parameterised test, `An_omitted_value_takes_the_default_page`, with two `TestCase`s. The three 4.3 scenarios are three tests, and the first keeps the plan's name, `Computes_total_pages_and_navigation_flags`.
- The REDs at 4.1 and 4.3 are compile errors. The tasks name the expected failure as a missing type, which C# can only report at compile time. Each RED failed only in its own new file, and only for that reason.
- 5.1 has no RED of its own, by design (AD18; task 5.1). Its behavioural proof is 6.3, 6.6 and 6.8 over PostgreSQL, green at 8.4.

### Issues found (A2)

1. No new blocker.
2. `.editorconfig:29` sets `end_of_line = lf`. The five new files are LF, which matches it (compare A1 Issue 5).
3. Carried unchanged, not acted on by A2: 1.11 (A1-01) and A1-03 still await the orchestrator.
4. The spec row `pageNumber=2147483647&pageSize=2147483647` → 21 474 836 / 100 is proven at the model level by the plan test through `PaginationQuery.MaxPageNumber`, whose value is `int.MaxValue / 100` = 21 474 836; `Skip` is then at most 2 147 483 500. No literal assertion was added beyond the task's cases. The HTTP-level clamping scenarios belong to 6.2, 6.3, 6.6, 6.8 and 6.9 (spec coverage table).

### Remaining tasks

- 1.11 (awaits the A1-01 decision).
- Batch A: 6.1–6.15 (opens the compile unit), 7.1–7.5, 8.1–8.8, 8A.1–8A.8.
- Batch B: 9.1–10.8. Batch C: 11.1–11A.12. Batch D: 12.1–12.9. Then Phase 13 and Delivery.

### Workload / PR boundary (A2)

- Mode: `size:exception` accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: A2 (Phases 4–5).
- Boundary: starts from A1's working tree; ends with two Application records, one Infrastructure helper and their unit tests. Nothing references them yet, so no wire shape, route or runtime behaviour changes, and the 6–8 compile unit is not opened.
- Review impact: about 87 authored production lines, including doc comments, and 100 test lines, all in new files.

## Work unit A3 — Phases 6, 7 and 8 (one compile unit, D04/AD16)

Scope: tasks 6.1–8.8, and nothing else. Logs are `<scratchpad>/a3-*.log`. No git write was made. No parallel-work file was touched (AGENTS.md, CLAUDE.md, `.agents/skills/*`, `.codex/*`, the module-separation plan). No journeys were run.

### Order of work

1. Wrote REDs 6.1–6.6 against today's types, then ran them at 6.7 (unit and functional).
2. Rewrote `PlatformDirectoryContractTests.cs` at 6.8/6.9; its RED was recorded as a compile error.
3. GREEN edits:
   - 6.10–6.15: Application types, test doubles and direct calls;
   - 7.1–7.3: stores and reader;
   - 8.1–8.2: endpoints and DTOs;
   - 8.3: unchanged.
4. Built the solution, then ran the 8.4 focused tests and `LIST-CODES`.
5. Refactored at 8.5, removed the Domain comparisons at 8.6, then ran the 8.7 gates.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 6.1 | `FT:Platform/PlatformDirectoryContractTests.cs` | Functional (served OpenAPI) | ✅ NET-G FT 749/749 (A1 2.8); in the 6.7 run every untouched test of the six edited classes passed (98) | ✅ `Every_list_route_declares_offset_parameters_and_its_binding_refusal` (plan:657-678). Ran FAIL: `names should contain "pageNumber" but was actually ["limit", "cursor"]` | ✅ 8.4 FT 111/111 | ✅ Seven routes; four name checks plus the declared `invalid_request` | ➖ None needed |
| 6.2 | `FT:Api/ProblemDetailsContractTests.cs` | Functional (HTTP, PostgreSQL) | ✅ same | ✅ Three tests. `A_list_success_is_its_offset_page_dto_with_no_cursor_or_envelope_member` FAILED: `nextCursor` present on the roles page. `Out_of_range_platform_page_sizes_are_clamped_over_http_and_never_refused_as_validation` FAILED: `pageSize` absent (`/api/platform/organizations?pageSize=0`). The guard `A_refused_list_read_is_a_problem_document_from_the_shared_writer` PASSED, as the task expects | ✅ 8.4 FT 111/111 | ✅ Tenant-list and Platform-directory bodies; four directories × `pageSize` 0 and 500 | ➖ None needed |
| 6.3 | `FT:Roles/RoleAdministrationTests.cs`, `FT:Members/MembershipAdministrationTests.cs` | Functional (HTTP, PostgreSQL) | ✅ same | ✅ `A_requested_role_page_carries_its_offset_metadata_and_clamps_the_page_size` FAILED: `the role page must carry totalCount`. The cross-tenant `404` tests, now requested with `?pageNumber=1&pageSize=25`, PASSED | ✅ 8.4 FT 111/111 | ✅ Page 2 of 30 (5 items); `pageSize` 0 → 1 (1 item); 500 → 100 (30 items) | ✅ 8.5 `NextCursor` dropped; 49/49 |
| 6.4 | `FT:Api/OpenApiContractTests.cs` | Functional (HTTP binding + OpenAPI + logs) | ✅ same | ✅ Renamed to `Every_page_parameter_binding_refusal_is_emitted_only_as_declared`. FAILED: `response.StatusCode should be BadRequest but was Forbidden` (see Deviations) | ✅ 8.4 FT 111/111 | ✅ Four unbindable values × seven routes; no `[Error] ` record after `ResetCapturedLogs` | ➖ None needed |
| 6.5 | `tests/Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs` | Unit (reflection) | ✅ `NET-G UT` 247/247 (A2) | ✅ TestCase `:41` dropped; renamed to `A_directory_query_carries_only_a_bounded_offset_page`. FAILED (1 of 36): `property.PropertyType should be …PaginationQuery but was …PlatformDirectoryQuery` | ✅ 8.4 UT 47/47 | ✅ `PaginationQuery`'s public properties are exactly `Default`, `PageNumber`, `PageSize` and `Skip`; each of the four queries has exactly one property, `Query`, of that type | ➖ None needed |
| 6.6 | `FT:Members/AdministrationDirectoryRevalidationTests.cs` | Functional (HTTP walk, PostgreSQL) | ✅ same | ✅ R6C walk over 26 rows (the seed counts the scenario's own rows). FAILED ×3: `firstIds.Length should be 25 but was 26` | ✅ 8.4 FT 111/111 | ✅ Roles, members and invitations: page 1 (25 items, `pageSize` 25, `totalCount` 26, `hasNextPage`), page 2 (1 row, `hasNextPage` false), no overlap, union equals the seed, order equals `?pageSize=100` | ✅ 6.14 direct store calls `new PaginationQuery(2, 25)` |
| 6.7 | — | RED run | — | ✅ UT exit 1 (1 of 36 failed); FT exit 1 (8 failed / 98 passed / 106). Each failure is the intended one | — | — | — |
| 6.8 | `FT:Platform/PlatformDirectoryContractTests.cs` | Functional | ✅ same | ✅ Compile RED: FT build exit 1, 7 × CS1503 (`PaginationQuery` → `PlatformDirectoryQuery`), all in this file | ✅ 8.4 FT 111/111 | ✅ OpenAPI parameters; typed schemas; identity routes forbid both names; clamp `(1, 10_000)` → 4 items at size 100 and `(1, 0)` → 1 item at size 1; audit pages 1–2 newest first, no overlap | ➖ None needed |
| 6.9 | same | Functional | ✅ same | ✅ Same compile RED (CS1503 in the two new tests) | ✅ 8.4 FT 111/111 | ✅ Huge page → `MaxPageNumber`/100 and empty; zero → 1/1 with 1 row; no `[Error] CleanArchitecture.` record; page 5 at size 2 → empty, real totals, `hasNextPage` false | ➖ None needed |
| 6.10–6.13 | covered by 6.1–6.9 | GREEN | — | via 6.1–6.9 | ✅ solution build exit 0; 8.4 UT 47/47, FT 111/111 | via 6.1–6.9 | ➖ None needed |
| 6.14–6.15 | test doubles and direct calls | Compile-unit edits | — | N/A (signature follow-through) | ✅ solution build exit 0; 8.7 gates | — | ➖ |
| 7.1–7.3 | covered by 6.2, 6.3, 6.6, 6.8, 6.9 | GREEN (PostgreSQL) | — | via those | ✅ 8.4 FT 111/111 | via those | ➖ None needed |
| 7.4 | — | Review | — | — | ✅ Every route orders before paging, by a unique key (see Reviews) | — | — |
| 7.5 | — | Adjusted (D04/AD16) | — | — | ➖ No run mid-unit; the focused run is 8.4 | — | — |
| 8.1–8.2 | covered by 6.1, 6.2, 6.4, 6.6 | GREEN (HTTP) | — | via those | ✅ 8.4 FT 111/111 | via those | ➖ None needed |
| 8.3 | — | No edit | — | — | ✅ `LIST-CODES` identical to the 1.1 baseline | — | — |
| 8.4 | — | GREEN run | — | — | ✅ UT 47/47; FT 111/111; `LIST-CODES` identical | — | — |
| 8.5 | four private row records | REFACTOR | ✅ 8.4 FT 111/111 | — | ✅ `MembershipAtomicityTests\|OwnershipTransferTests\|RoleAdministrationTests\|MembershipAdministrationTests` 49/49 | — | ✅ `NextCursor` dropped from `RolePageRow` and three `MemberPageRow` records |
| 8.6 | `src/Domain/IdentityAccess/{Tenants/TenantId,Authorization/RoleId,Memberships/MembershipId,Invitations/InvitationId}.cs` | REFACTOR (removal, AD17) | ✅ consumer search empty after Task 7 | — | ✅ search after removal: no match; solution build exit 0; `NET-G DT` 209/209; `NET-G UT` 246/246; `NET-G FT` exit 0, 754/754 (8 m 56 s; A1 2.8 had 749, and this unit adds a net 5 tests); `NET-G IT` exit 0, 370/370 (43 s; equal to the 1.1 baseline) | — | ✅ `IComparable<T>`, `CompareTo`, four operators and the keyset comments removed |
| 8.7 | — | Gates | — | — | build exit 0; DT 209/209; UT 246/246; FT exit 0, 754/754 (8 m 56 s; A1 2.8 had 749, and this unit adds a net 5 tests); IT exit 0, 370/370 (43 s; equal to the 1.1 baseline); DelegatedAdministrationRevalidationTests (unedited) focused 6/6; FT review category exit 1, 2 failed / 2 passed / 4 (11 s), matching the 1.1 baseline: the same two named reproductions fail (`Ownership_cannot_be_transferred_to_a_self_deactivated_identity_with_an_active_membership`, `Suspending_a_member_requires_the_recent_primary_proof_retained_by_accepted_C6`) | — | — |
| 8.8 | — | Adjusted | — | — | ➖ No commit (DL.0) | — | — |

### Test Summary (A3)

- **Tests written or rewritten:**
  - New tests: 7 methods, 7 cases.
    - `PlatformDirectoryContractTests`: 3 (`Every_list_route_declares_offset_parameters_and_its_binding_refusal`, `Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors`, `A_page_past_the_end_is_an_empty_page_with_the_real_totals_not_a_refusal`).
    - `ProblemDetailsContractTests`: 3.
    - `RoleAdministrationTests`: 1.
  - Rewritten or renamed tests: 6.
    - `PlatformDirectoryContractTests`: 3 (the parameter test, the schema test and the identity-route test) plus the clamp and audit tests.
    - The binding test in `OpenApiContractTests`.
    - The shape test in `PlatformApplicationShapeTests`.
    - The three R6C cases.
  - Deleted: the two cursor-only tests (D14).
- **Tests passing:** 8.4 focused UT 47/47 and FT 111/111; 8.5 focused FT 49/49; 8.7 gates as recorded above.
- **Layers used:**
  - Unit: 1 test (reflection).
  - Functional: 12 tests (HTTP and handler over PostgreSQL).
  - E2E: none (Phase 13).
- **Approval tests:** the existing suites, run unchanged, for the 6.14–6.15 signature follow-through. 8.5 and 8.6 are refactors proven by the focused run and the gates.
- **Pure functions created:** the seven page DTO `From` factories.

### Reviews and searches (A3)

- **7.4 ordering (AD5).** Every paged query orders before `ToPaginatedListAsync` and ends in a unique key:
  - roles `role.Id` (`RoleAdministrationStore.cs:27-28`);
  - members `membership.Id`, then the user and profile join (`MembershipAdministrationStore.cs:27-28`);
  - invitations `invitation.Id` (`MembershipAdministrationStore.cs:38-47`);
  - organizations `tenant.Id`, identities `user.Id` and administrators `membership.Id` (`PlatformOperationalProjectionReader.cs:36,52,76`);
  - audit `OccurredAt DESC, Id DESC` (`PlatformOperationalProjectionReader.cs:106-107`).

  These are the keys the cursor code used, so every route keeps its current order. The R6C walk (6.6) proves that pages 1 and 2 of members, roles and invitations equal one `?pageSize=100` page in the same order, and the audit test (6.8) proves newest first across pages 1 and 2.
- **8.3 declarations.** No `WithApiProblemDetails`, `WithBodyBindingFailureCode` or `Directory` entry changed. `LIST-CODES` after the unit is identical to the 1.1 baseline, including `validation_failed` on the four Platform `400` sets (the recorded rule-10 gap, D15) and `not_found` on the three tenant `404` sets.
- **8.4 invariant OpenAPI names (L5).** On all seven routes the served document names exactly `pageNumber` and `pageSize` (query, `int32`), with no description or other text, and `RolePageResponse` has exactly the seven page members. No `limit` or `cursor` parameter remains.
- **Leftover search** (over `src` and `tests` C#, for `PlatformDirectoryQuery`, `PlatformDirectoryPage`, `OpaqueCursor`, `NextCursor`/`nextCursor`, `MaximumLimit`, `BoundedLimit`, `MinimumLimit`, `DecodeGuid`, `EncodeGuid`, `DecodeMoment`, `EncodeMoment`, `RolePage`, `MemberPage`, `InvitationSummaryPage` and `Cursor.Encode`/`Cursor.Decode`): after 8.5 the only match is the no-`nextCursor` guard in `ProblemDetailsContractTests.cs`. `DelegatedAdministrationRevalidationTests.cs` keeps its own private `MemberPage(MemberRow[] Items)` record, which is unedited and not a cursor artefact.
- **8.6 consumer search** (`rg -n "CompareTo|IComparable|operator <|operator >" src tests`, plus typed-identifier comparison patterns): after Task 7 only the four definitions remained, so they were removed. After the removal the search finds nothing.

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| Focused test command and exact result | **RED:** `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~PlatformApplicationShapeTests"` → exit 1, 1 failed / 35 passed (`a3-6.7-red-ut.log`). `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj` filtered to `PlatformDirectoryContractTests`, `ProblemDetailsContractTests`, `RoleAdministrationTests`, `MembershipAdministrationTests`, `OpenApiContractTests` and `AdministrationDirectoryRevalidationTests` → exit 1, 8 failed / 98 passed / 106 (`a3-6.7-red-ft.log`). `dotnet build tests/Application.FunctionalTests/Application.FunctionalTests.csproj` → exit 1, 7 × CS1503, all in `PlatformDirectoryContractTests.cs` (`a3-6.8-red-build.log`). **GREEN:** `dotnet build CleanArchitecture.slnx` → exit 0, 0 errors (`a3-8.4-green-build.log`). UT `--no-build` filtered to `PlatformApplicationShapeTests`, `PaginationQueryTests` and `PaginatedListTests` → exit 0, 47/47 (`a3-8.4-green-ut.log`). FT `--no-build` filtered to the tasks.md 8.4 classes (`OpenApiContractTests`, `PlatformDirectoryContractTests`, `ProblemDetailsContractTests`, `RoleAdministrationTests`, `MembershipAdministrationTests`, `AdministrationDirectoryRevalidationTests`, `ErrorCatalogContractTests`, `PlatformDirectoryAccessTests`) → exit 0, 111/111 (`a3-8.4-green-ft.log`). `LIST-CODES` → `list-problem-codes.after.json`; its diff with the baseline is empty. **REFACTOR 8.5:** FT filtered to `MembershipAtomicityTests`, `OwnershipTransferTests`, `RoleAdministrationTests` and `MembershipAdministrationTests` → exit 0, 49/49 (`a3-8.5-refactor-ft.log`). |
| Runtime harness command/scenario and exact result | 8.7 gates over real PostgreSQL through Aspire, one project per process, after the 8.6 removal (`a3-8.7-*.log`, summary `a3-8.7-summary.txt`): `dotnet build CleanArchitecture.slnx` → exit 0; `NET-G DT` → exit 0, 209/209; `NET-G UT` → exit 0, 246/246; `NET-G FT` → exit 0, 754/754 (8 m 56 s; A1 2.8 had 749, and this unit adds a net 5 tests); `NET-G IT` → exit 0, 370/370 (43 s; equal to the 1.1 baseline); `dotnet test <FT> --filter "TestCategory=IndependentDevelopmentReview"` → exit 1, 2 failed / 2 passed / 4 (11 s), matching the 1.1 baseline: the same two named reproductions fail (`Ownership_cannot_be_transferred_to_a_self_deactivated_identity_with_an_active_membership`, `Suspending_a_member_requires_the_recent_primary_proof_retained_by_accepted_C6`). `dotnet test <FT> --no-build --filter "FullyQualifiedName~DelegatedAdministrationRevalidationTests"` → exit 0, 6/6 (`a3-8.7-delegated.log`), and `git diff --quiet` confirms the file is unedited. Journeys: N/A for this unit. Phase 13 owns them, and the strict client reader (E7) rejects the new shape until batches B and C land. |
| Rollback boundary | Compile unit 6–8 reverts as one piece. Restore the Application files (`Platform/Queries/PlatformDirectories.cs`, `Platform/Queries/PlatformDirectoryHandlers.cs`, `Platform/IPlatformOperationalProjectionReader.cs`, `Roles/{RoleRequests,RoleHandlers,IRoleAdministrationStore}.cs`, `Members/{MembershipRequests,MembershipHandlers,IMembershipAdministrationStore}.cs`); the Infrastructure files (`IdentityAccess/{RoleAdministrationStore,MembershipAdministrationStore}.cs`, `Platform/PlatformOperationalProjectionReader.cs`); the list methods, DTOs and `Page()` in the Web files (`Identity/{RoleEndpoints,MembershipEndpoints}.cs`, `Platform/PlatformEndpoints.cs`, `Platform/Contracts/PlatformDirectoryContracts.cs`), leaving A1's AD8 lines in the same files; and the four Domain ID files. Revert declared test rows 24–32 and 34–37, and the A3 hunks of rows 38 (6.4) and 39 (6.2). A2's models and helper stay; nothing references them after the revert. There is no data or schema change. At delivery the unit couples to batches B and C (E7). |

### Files changed (A3)

| File (from repository root) | Action |
| --- | --- |
| `src/Application/IdentityAccess/Platform/Queries/PlatformDirectories.cs` | Modified (6.10): `PlatformDirectoryQuery` and `PlatformDirectoryPage<T>` deleted; the four List* queries take `PaginationQuery Query` and return `Result<PaginatedList<…>>`; keyset docs replaced by an offset comment |
| `src/Application/IdentityAccess/Platform/Queries/PlatformDirectoryHandlers.cs` | Modified (6.11): `PaginatedList<T>`; the MFA gate still runs before the reader |
| `src/Application/IdentityAccess/Platform/IPlatformOperationalProjectionReader.cs` | Modified (6.11): `(PaginationQuery, CancellationToken)` returning `Task<PaginatedList<…>>`; `Common.Models` using |
| `src/Application/IdentityAccess/Roles/{RoleRequests,RoleHandlers,IRoleAdministrationStore}.cs` | Modified (6.12): `ListRolesQuery(TenantId, PaginationQuery Pagination)`; `ListAsync(TenantId, PaginationQuery, CancellationToken)` returns `Task<PaginatedList<RoleView>>`; `RolePage` deleted; using |
| `src/Application/IdentityAccess/Members/{MembershipRequests,MembershipHandlers,IMembershipAdministrationStore}.cs` | Modified (6.13): `ListMembersQuery` and `ListTenantInvitationsQuery` take `PaginationQuery Pagination`; the store returns pages; `MemberPage` and `InvitationSummaryPage` deleted; using |
| `src/Infrastructure/IdentityAccess/RoleAdministrationStore.cs` | Modified (7.1): order by `Id`, `ToPaginatedListAsync`, then re-wrap into views; `MaximumLimit`, the `<= 0` → 100 default and `Cursor` deleted; two usings |
| `src/Infrastructure/IdentityAccess/MembershipAdministrationStore.cs` | Modified (7.2): members re-wrapped; invitations paged, then the role lookup; `MaximumLimit`, the 100 default and `OpaqueCursor` deleted; two usings |
| `src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs` | Modified (7.3): organizations, identities and administrators page by `Id`; audit by `OccurredAt DESC, Id DESC`, then the allowlisted-metadata re-wrap; `Bounded`, `Page`, `Decode*`, `Encode*` and the `System.Text` using deleted; class doc rewritten; two usings |
| `src/Web/Endpoints/Identity/RoleEndpoints.cs`, `src/Web/Endpoints/Identity/MembershipEndpoints.cs` | Modified (8.1–8.2): `int? pageNumber, int? pageSize` via `PaginationQuery.From`; `RolePageResponse`, `MemberPageResponse` and `InvitationSummaryPageResponse` with the seven members and a static `From`; using |
| `src/Web/Endpoints/Platform/PlatformEndpoints.cs` | Modified (8.1–8.2): four list methods bind the offset parameters; `Page()` deleted; using |
| `src/Web/Endpoints/Platform/Contracts/PlatformDirectoryContracts.cs` | Modified (8.2): four directory DTOs with the seven members and a static `From`; comment rewritten; `Common.Models` and `Platform.Queries` usings |
| `src/Domain/IdentityAccess/{Tenants/TenantId,Authorization/RoleId,Memberships/MembershipId,Invitations/InvitationId}.cs` | Modified (8.6): `IComparable<T>`, `CompareTo`, the four operators and the keyset comments removed |
| `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformDirectoryContractTests.cs` | Modified (row 24; 6.1, 6.8, 6.9) |
| `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs` | Modified (row 39; 6.2): three tests and one helper |
| `tests/Application.FunctionalTests/IdentityAccess/Roles/RoleAdministrationTests.cs` | Modified (row 31; 6.3, 8.5): offset page test, seed and page-member helpers; cross-tenant `404` requested with offset parameters; `NextCursor` dropped |
| `tests/Application.FunctionalTests/IdentityAccess/Members/MembershipAdministrationTests.cs` | Modified (row 27; 6.3, 8.5): cross-tenant `404` requested with offset parameters; `NextCursor` dropped |
| `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs` | Modified (row 38; 6.4): binding test renamed and migrated, with a no-`[Error]` assertion |
| `tests/Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs` | Modified (row 32; 6.5): TestCase dropped; test renamed; PD-3 shape; using |
| `tests/Application.FunctionalTests/IdentityAccess/Members/AdministrationDirectoryRevalidationTests.cs` | Modified (row 25; 6.6, 6.14): R6C offset walk over 26 rows with the order check; direct store calls use `new PaginationQuery(2, 25)`; using |
| `tests/Application.FunctionalTests/IdentityAccess/Members/IndependentDevelopmentAdministrationReviewTests.cs` | Modified (row 26; 6.14): `:114` and `:180` read `PaginatedList<…>`; the double takes the new signatures; using |
| `tests/Application.FunctionalTests/IdentityAccess/Lifecycle/ConcurrentDeactivationFloorTests.cs` | Modified (row 30; 6.14): the double's `ListAsync`; using |
| `tests/Application.FunctionalTests/IdentityAccess/Platform/{PlatformProjectionTests,PlatformIdentityLifecycleTests,PlatformDirectoryAccessTests,PlatformAdministrationTests}.cs` | Modified (rows 34–37; 6.15): `new PaginationQuery(1, n)`; using |
| `tests/Application.FunctionalTests/IdentityAccess/Members/{MembershipAtomicityTests,OwnershipTransferTests}.cs` | Modified (rows 28–29; 8.5): `NextCursor` dropped from the private row record |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 6.1–8.8 |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: A3 appended; cumulative table updated; A1 and A2 kept verbatim |

Test-file boundary: `git --no-optional-locks status --short --untracked-files=all -- tests …` lists, among test files, exactly declared rows 24–32 and 34–41. Rows 38–39 are shared with A1, and rows 40–41 were created by A2. Row 33 `StackBaselineTests.cs` and `DelegatedAdministrationRevalidationTests.cs` are unchanged. No other test file was created, edited, moved or deleted.

### Deviations from design (A3)

- **6.4 expected-failure text.** tasks.md says the RED fails because "unbound parameters answer `200`". The recorded RED is `403` on the first route: `response.StatusCode should be BadRequest but was Forbidden`. With the parameters unbound, the request from `TestApp.RunAsDefaultUserAsync()` reaches the MediatR `Authorize(…, requiresTenant: true)` behaviour, and that user has no active tenant. The RED still discriminates: no route answered `400 invalid_request`. After the change, binding refuses first. The pre-change `limit=not-a-number` test with the same user already showed that ordering.
- **6.2 HTTP authorization.** The two Platform HTTP tests call `TestApp.SetHttpAuthorizationGranted(true)` after `PlatformScenario.ActiveOwnerAsync()`.
  - The functional host's default policy requires the test permission claim (`WebApiFactory.cs:88-94`).
  - `ActiveOwnerAsync` sets the MFA-proven session, the Platform tenant and the application permission, but not that flag.
  - Without it, `RequireAuthorization` would refuse every request before it reached the directory.
- **6.2 split.** The task's three bullets became three tests in `ProblemDetailsContractTests.cs`, plus one helper (`AssertOffsetPageBody`). The tests are `A_list_success_is_its_offset_page_dto_with_no_cursor_or_envelope_member`, `Out_of_range_platform_page_sizes_are_clamped_over_http_and_never_refused_as_validation` and `A_refused_list_read_is_a_problem_document_from_the_shared_writer`.
- **6.6 diagnostic helper.** Between 6.6 and 6.14, `CaptureContinuationFailureAsync` dropped its `cursor` parameter and passed `null` to today's store signature, so it kept compiling. At 6.14 its calls became `new PaginationQuery(2, 25)`.
- **6.8 names and assertions.** `:37-51` is now `Each_directory_returns_its_own_typed_offset_page`, and `:75-90` is renamed `A_page_size_beyond_the_bounds_is_clamped_rather_than_honoured`. The audit test asserts "no overlap" as an empty intersection of the two pages' event IDs. It asserts "newest first" as every page-2 event occurring no later than the last page-1 event.
- **6.9 triangulation.** `Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors` adds `zero.Value.Items.Count.ShouldBe(1)` to plan:680-696, for the spec scenario "A zero page size returns one row".
- **8.2 usings.** The four Platform DTO `From` methods take a concrete `PaginatedList<…Projection>`, as plan:615-621 does for roles. So `PlatformDirectoryContracts.cs` also gains the `CleanArchitecture.Application.IdentityAccess.Platform.Queries` using, beside the `Common.Models` using the task names.
- **Interface doc comments.** The three store and reader interface members gained a one-line `<summary>` stating their stable order. No behaviour changed.
- **Two mistakes in my own new tests, each fixed before any evidence was recorded from it:**
  - a Shouldly overload mismatch at 6.2 (CS1503; the first 6.7 attempt, `a3-6.7-red-ft-attempt1-cs1503.log`);
  - an index-from-end `^1` inside a Shouldly expression tree at 6.8 (CS8790/CS8791; the first GREEN build, `a3-8.4-green-build-attempt1-cs8790.log`).

### Issues found (A3)

1. No new blocker.
2. Carried unchanged, and not acted on by A3: 1.11 (A1-01) and A1-03 still await the orchestrator.
3. **Line endings.** Files written through the Write tool or copied from the scratchpad are LF.
   - Git warns that it will normalize them to CRLF (`core.autocrlf=true`); `.editorconfig:29` asks for LF.
   - Content diffs are unaffected (compare A1 Issue 5 and A2 Issue 2).
4. **Member ordering before the join.** The members page orders before the user and profile join, exactly as the cursor code did. The R6C member walk proves that pages 1 and 2 at size 25 equal one page of 100, in the same order.
5. **Build warnings.** The build reports two `ASPIRE010` warnings (`AspireUseCliBundle=false` in AppHost and TestAppHost). They come from the Aspire build targets and are not compiler warnings, so they do not fail the build under `TreatWarningsAsErrors`. They predate this change.
6. **Still on the cursor contract.** SPEC, TRACEABILITY and ADR-004 describe the cursor contract until 8A.1–8A.8. The SPA reads cursors until batches B and C (E7 coupling).

7. **Parallel work in the tree.** `git status` also lists changes to `AGENTS.md`, `CLAUDE.md`, `.agents/skills/*`, `.codex/*` and the module-separation plan. Their modification times run from 11:39 to 14:03, before this unit's first repository edit at about 16:33. They belong to the user's parallel chats; this unit wrote none of them.

### Remaining tasks

- 1.11 (awaits the A1-01 decision).
- Batch A: 8A.1–8A.8.
- Batch B: 9.1–10.8.
- Batch C: 11.1–11A.12.
- Batch D: 12.1–12.9.
- Then Phase 13 and Delivery.

### Workload / PR boundary (A3)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: A3 (Phases 6–8, one compile unit).
- **Boundary.** It starts from A2's working tree. It ends with the seven list routes serving offset pages over PostgreSQL, the cursor types and helpers deleted, and the Domain comparison members removed.
- **Review impact.** `git diff --stat` over the unit's source and test files shows 35 files, +550/−519. Two of those files also carry A1's earlier edits: about 40 lines in `ProblemDetailsContractTests.cs` and 1 line in `OpenApiContractTests.cs`.

## Work unit A4 — Phase 8A (identity-access SPEC amendment)

Scope: tasks 8A.1–8A.8, and nothing else. This is a document amendment. Only three documents changed: `docs/features/identity-access/SPEC.md`, `docs/features/identity-access/TRACEABILITY.md` and `docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md`. This change's `tasks.md` and `apply-progress.md` were also updated. No source or test file changed. Logs and edit inputs are `<scratchpad>/a4-*`. No git write was made. No parallel-work file was touched (AGENTS.md, CLAUDE.md, `.agents/skills/*`, `.codex/*`, the untracked module-separation plan). No journeys were run.

### Order of work

1. **8A.1.** Ran the three baseline searches and saved their output (`a4-8A.1-red-search1.log`, `a4-8A.1-red-exclusion.log`, `a4-8A.1-red-adr.log`).
2. **Readers of the documents.** Nothing in `src` or `tests` reads `SPEC.md` or `TRACEABILITY.md`. The only executable reader of the three documents is `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs:295-296`, which asserts that ADR-004 contains "Before Tasks 1 and 2" (`:14`).
3. **8A.2–8A.6.** Applied 27 exact replacements with `a4-apply-edits.mjs` and `a4-edits-docs.json` (`a4-8A.2-8A.6-apply.log`). Every replacement must match exactly once, or nothing is written. Line endings are kept: `SPEC.md` and `TRACEABILITY.md` stay CRLF on every line (1,347/1,347 and 200/200).
4. **8A.7.** The safety net ran first: `StackBaselineTests` 13/13. Then one replacement with `a4-edits-adr.json` (`a4-8A.7-apply.log`; `file` still reports CRLF), then `StackBaselineTests` 13/13 again.
5. **8A.8.** Reran the searches (`a4-8A.8-green-all.log`), then checked every amended row line by line against the spec scenarios (`a4-8A-scenario-checks.log`).

### Line map (baseline `2716aa6` → amended)

- **`SPEC.md`** (1,337 → 1,347 lines). The IA-REQ-038 sub-paragraph adds 7 lines after `:201`, and the C5 note adds 3 lines after baseline `:1065`. Resulting positions:
  - IA-REQ-045: `:239` → `:246`;
  - rows `:306-309` → `:313-316`, and the scenario `:464-465` → `:471-472`;
  - sessions `:695` → `:702`;
  - `:1033` → `:1040`, and `:1037` → `:1044`;
  - C5 `:1065` → `:1072`, with the note at `:1073-1075`.
- **`TRACEABILITY.md`** (175 → 200 lines). The evidence paragraph adds 25 lines after `:20`, so `:86` → `:111`, `:92` → `:117`, `:99` → `:124` and `:158` → `:183`.
- **ADR-004.** Decision 17 stays at `:36`.

### TDD Cycle Evidence

Strict TDD for a document amendment works like this. The RED is each 8A.1 search matching wording the spec forbids. The GREEN is the same search showing only the reviewed exclusions, plus the scenario checks. Phase 8A declares no test file, so no test was written.

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 8A.1 | The spec's two searches and the ADR-004 check (Work Unit Evidence) | Document search | N/A (no edit) | ✅ Recorded at baseline. **Search 1** matches exactly `SPEC.md:239,306,307,308,309,464,465,1033,1037,1065`, as tasks.md lists. **Reviewed-exclusion search** matches 17 lines: `TRACEABILITY.md:86,92,99`; `SPEC.md:239,306-309,464,465,695,1033,1037,1065`; `TASKS.md:936,940`. **ADR-004 `cursor`** matches only `:36` | — | — | — |
| 8A.2 | `SPEC.md` IA-REQ-038 | Document | N/A: no test reads `SPEC.md` | ✅ 8A.1: section 4 states no offset rule | ✅ New indented sub-paragraph at `:203-208`, dated 2026-09-13 and naming the PD-4 authorization. It states the offset page DTO with its seven members; "a declared success DTO, not an envelope"; `pageNumber` (≥ 1) and `pageSize` (1–100, default 25), clamped and never refused; `400 invalid_request` for a non-integer or beyond-Int32 value; and `200` with empty `items` past the end. It has no `limit` or `cursor`. The `:201` sentence "adds no pagination envelope to identity endpoints" is intact | ✅ Spec scenario "IA-REQ-038 carries the paging rules", checked term by term | ➖ None needed |
| 8A.3 | `SPEC.md` IA-REQ-045 | Document | N/A | ✅ Search 1 `:239` | ✅ `:246` names `pageNumber`, `pageSize` and all seven page members. It keeps the `/api/identity/*` no-envelope sentence and has no `limit`, `cursor` or `nextCursor` | ✅ Spec scenario "IA-REQ-045 describes offset directories" | ➖ None needed |
| 8A.4 | `SPEC.md` Platform rows and scenario | Document | N/A | ✅ Search 1 `:306-309,464,465` | ✅ `:313-316` each name `pageNumber`/`pageSize` and the offset members, with no `limit` or `cursor`. `recent_mfa_required` stays only on `:313-314`. `:315` stays untyped and carries the offset metadata. `:471` reads "a bounded `pageNumber` and `pageSize`", and `:472` reads "the offset page metadata" | ✅ Four rows and two scenario lines, per the spec row table | ➖ None needed |
| 8A.5 | `SPEC.md` `:1033`, `:1037`, C5 | Document | N/A | ✅ Search 1 `:1033,1037,1065` | ✅ `:1040` is offset, keeps the detail-route `404` and has no default clause. `:1044` is offset, with no `404` and no default clause. `:1072` is unchanged, followed by the dated C5 history note at `:1073-1075`, which uses no searched word. `:3` and the sessions row (`:695` → `:702`) are byte-identical to `HEAD` | ✅ Spec scenario "C5 history is kept with a dated note" | ➖ None needed |
| 8A.6 | `TRACEABILITY.md` | Document | N/A: no test reads it | ✅ Exclusion search `:86,92,99` | ✅ The retired binding-test name appears 0 times; the renamed one appears twice (row `:111` and the evidence paragraph). The shared-pipeline row cites "all seven paging-parameter routes". `usePlatformRead` and "More accounts" each appear 0 times. `:18` is unchanged, and the IA-REQ-053 row (baseline `:158`) is byte-identical. The evidence paragraph is at `:22-45` | ✅ Spec scenario "The migrated binding test is the cited proof" | ➖ None needed |
| 8A.7 | `ADR-004` decision 17 | Document, with an Integration safety net | ✅ `StackBaselineTests` 13/13 before the edit (`a4-8A.7-safety-stackbaseline.log`, 17:36:20) | ✅ ADR `rg -n -i cursor` matched `:36` | ✅ `:36` reads "Platform directories use typed bounded offset page responses". `StackBaselineTests` passed 13/13 after the edit (`a4-8A.7-after-stackbaseline.log`, 17:39:51; the ADR was written at 17:36:50) | ➖ Single sentence | ➖ None needed |
| 8A.8 | The 8A.1 searches | Document search | — | — | ✅ **Search 1:** only `SPEC.md:1072` (baseline `:1065`, the C5 history). **Reviewed exclusion:** only `SPEC.md:702` (baseline `:695`), `TASKS.md:936`, `TASKS.md:940` and `SPEC.md:1072`; the note lines do not match. **ADR-004 `cursor`:** no match (exit 1). **ADR-004 status:** `:3` "**Status:** Proposed" | — | — |

### Test Summary (A4)

- **Tests written:** none. Phase 8A declares no test file, and the documents have no tests of their own.
- **Tests passing:** `StackBaselineTests` 13/13, both before and after the ADR edit.
- **Layers used:** document searches and scenario checks, plus one Integration class as the safety net.
- **Approval tests:** `StackBaselineTests`, which is existing and was not changed.
- **Pure functions created:** none.

### Work Unit Evidence (A4)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | **Search 1:** `rg -n 'nextCursor\|limit.{0,3}/.{0,3}cursor\|opaque cursor' docs/features/identity-access` → exit 0, one line (`SPEC.md:1072`). **Reviewed-exclusion search** (spec `api-offset-pagination`, "Evidence and ADR follow the amendment") over `docs/features/identity-access` → exit 0, four lines (`TASKS.md:936`, `TASKS.md:940`, `SPEC.md:702`, `SPEC.md:1072`). **ADR:** `rg -n -i cursor docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` → exit 1, no match. **Status:** `rg -n '^\*\*Status:\*\* Proposed' docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` → exit 0, `:3`. **Safety net:** `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~StackBaselineTests"` → exit 0, 13 passed, 0 failed, both before and after the ADR edit. **Scenario checks** (`a4-8A-scenario-checks.log`): every check passes. |
| Runtime harness command/scenario and exact result | N/A. The change is documents only, with no runtime boundary. `StackBaselineTests` is the only executable reader of any of the three files, and it passed before and after. The journeys belong to Phase 13. |
| Rollback boundary | Revert the three documents only. `git diff --numstat -- docs` shows ADR-004 +1/−1, `SPEC.md` +19/−9 and `TRACEABILITY.md` +28/−3. Uncheck 8A.1–8A.8 in tasks.md and drop this section. There is no code, test, data or schema change. The amendment describes A3's offset API, so at delivery it reverts with batches A–C (proposal "Rollback Plan"). |

### Files changed (A4)

| File (from repository root) | Action |
| --- | --- |
| `docs/features/identity-access/SPEC.md` | Modified (8A.2–8A.5): the dated IA-REQ-038 sub-paragraph; IA-REQ-045; rows `:306-309`; the scenario at `:464-465`; rows `:1033` and `:1037`; the dated C5 history note after `:1065` |
| `docs/features/identity-access/TRACEABILITY.md` | Modified (8A.6): `:86`, `:92` and `:99`; a dated evidence paragraph after the P2-4 focused verification |
| `docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` | Modified (8A.7): decision 17 names typed bounded offset page responses; the status stays Proposed |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 8A.1–8A.8 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: cumulative table updated; A4 appended; A1–A3 kept verbatim |

Test-file boundary: no test file was created, edited, moved or deleted.

### Decisions inside the tasks (A4)

- **Evidence paragraph placement.** The tasks name no location. The paragraph sits at the top of `TRACEABILITY.md`, after the dated P2-4 focused verification. The migration spans the IA-REQ-038 and IA-REQ-045 rows and the tenant lists recorded under IA-REQ-053, and that row (`:158`) must stay unchanged.
- **`:92` read-hook citation.** The P2-2 claims are now cited to `useRead.test.jsx`.
  - The Platform directories read through the shared hook (`PlatformPanel.jsx:29`, `PlatformIdentitiesPage.jsx:29`), and `identityFiles.contract.test.js:141` asserts that `usePlatformRead.js` does not exist.
  - Each claim was checked against a test in `useRead.test.jsx:18-208`: abort on unmount; captured refreshes rejected after unmount, after disable and after loader replacement; a clean lifecycle for a replacement loader; an older loader that settles late is ignored; a retryable failure and its retry keep the data; and a non-retryable `403` clears it.
  - The "More accounts" clause is dropped, together with the "duplicate read" half it supported.
- **An unnamed proof.** The evidence paragraph describes `A_list_success_is_its_offset_page_dto_with_no_cursor_or_envelope_member` under `ProblemDetailsContractTests` without naming it, because its name contains a searched word.
- **IA-REQ-045** points to IA-REQ-038 for the paging rules instead of repeating the default and the range.

### Correction round 1 — validator finding A4-01 (2026-09-13)

Scope: finding A4-01 only. One sentence of the dated evidence paragraph in `docs/features/identity-access/TRACEABILITY.md` changed. This file was merged. `tasks.md` is unchanged: 8A.6 stays `[x]`, because the finding corrects a claim in its output and the corrected output is proven below. No source or test file changed, no git write was made, and no parallel-work file was touched. Logs and the edit input are `<scratchpad>/a4-corr1-*`.

| Finding | Disposition | Change made | Evidence |
| --- | --- | --- | --- |
| A4-01 (should-fix, 8A.6) | **Applied.** It contradicts no confirmed decision: the spec coverage table already places 6.9 at handler level (`tasks.md:611`). | `TRACEABILITY.md:34-36`. The clause "a huge or zero page answered `200` with no `Error` record" now reads "a huge or zero page is clamped and returned as a successful page by the handler, with no `Error` record". The rewrap adds one line, and no other sentence changed. The edit ran through `a4-apply-edits.mjs` with `a4-corr1-edits.json`: a dry run, then one exact match (`a4-corr1-apply.log`). | The cited test, `PlatformDirectoryContractTests.cs:142-160`, dispatches `ListPlatformOrganizationsQuery` through `TestApp.SendAsync` twice. It makes no HTTP call and asserts no status. No test requests a huge valid `pageNumber` over HTTP. The only URL match, `OpenApiContractTests.cs:587` `pageNumber=2147483648`, is the beyond-Int32 binding refusal, a `400`. So the fix names the layer instead of citing an HTTP proof. The HTTP clamp proofs stay the two `pageSize` tests the paragraph already names. |

#### TDD Cycle Evidence (A4 correction round 1)

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 8A.6 (A4-01) | `TRACEABILITY.md` evidence paragraph | Document search | N/A: no test reads `TRACEABILITY.md`; `StackBaselineTests` reads only ADR-004 | ✅ `a4-corr1-red.log`: ``rg -n 'answered `200` with'`` matched `:34` (exit 0). In the cited test body, the HTTP-call and status search found nothing (exit 1), and the handler-dispatch search counted 2 | ✅ `a4-corr1-green.log`: the same search exits 1, and `page answered` exits 1. The corrected lines match at `:34`, `:35` and `:36`, each anchored with `\r?$` (rg exit 0). No `200` follows the corrected clause (exit 1). Each of the two proof names appears once | ➖ Single clause | ➖ None needed |

#### Work Unit Evidence (A4 correction round 1)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | **RED** (`a4-corr1-red.log`): ``rg -n 'answered `200` with' docs/features/identity-access/TRACEABILITY.md`` → exit 0, `:34`. **GREEN** (`a4-corr1-green.log`): the same search → exit 1. The three anchored corrected-line searches → exit 0, at `:34`, `:35` and `:36`. **8A.8 rerun, identical to A4's GREEN:** Search 1 → exit 0, one line (`SPEC.md:1072`). The reviewed-exclusion search → exit 0, four lines (`TASKS.md:936`, `TASKS.md:940`, `SPEC.md:702`, `SPEC.md:1072`). ADR-004 `cursor` → exit 1, no match. ADR-004 status → exit 0, `:3`. **Structure:** `TRACEABILITY.md` has 201 lines, all CRLF. `:18` is unchanged, and the IA-REQ-053 row matches its `HEAD` content, now at `:184`. |
| Runtime harness command/scenario and exact result | N/A. One documentation sentence changed, with no runtime boundary. `StackBaselineTests` reads only ADR-004, which this round did not touch, so it was not rerun. |
| Rollback boundary | Restore the two original lines at `TRACEABILITY.md:34-35`; `a4-corr1-edits.json` holds both texts. Nothing else changes. |

#### Line map after correction round 1

- **`TRACEABILITY.md`** (200 → 201 lines). The evidence paragraph is now `:22-46`. The rows A4 cited move down by one: `:111` → `:112`, `:117` → `:118`, `:124` → `:125`, and the IA-REQ-053 row `:183` → `:184`. The renamed binding-refusal test still appears twice (`:40` and `:112`).
- `git diff --numstat -- docs`: ADR-004 +1/−1, `SPEC.md` +19/−9 and `TRACEABILITY.md` +29/−3 (A4 recorded +28/−3). The review impact becomes +49/−13 across three files.
- This round did not change `SPEC.md` or ADR-004.

#### Issues found (A4 correction round 1)

1. No new blocker.
2. **A check that proved nothing, rerun.** The first form of the corrected-clause check anchored `$` before the CRLF `\r`, and it printed `cut`'s exit status instead of `rg`'s. So it showed `exit=0` with no matching line. It was not used as evidence. The rerun, with `\r?$` and `PIPESTATUS`, is appended to the same log and is the result recorded above.
3. Carried unchanged: 1.11 (A1-01) and A1-03 still await the orchestrator, and A4 Issues 3 and 4 stand.

### Deviations from design (A4)

- The IA-REQ-038 sub-paragraph also says the parameters are optional, and that "a `pageSize` of 0 or less means 1". Both go beyond the four statements that task 8A.2 lists. They restate PD-1/E1, as the `api-offset-pagination` spec "Defaults and clamping within Int32" requires, and add no new rule.
- TRACEABILITY `:92` replaces the never-committed `usePlatformRead.test.jsx` citation with the real proof instead of only deleting it. This keeps the P2-2 sentence attributed (spec: "corrects two stale citations").

### Issues found (A4)

1. No new blocker.
2. Carried unchanged, and not acted on by A4: 1.11 (A1-01) and A1-03 still await the orchestrator.
3. **Forward-looking TRACEABILITY wording.**
   - `:117` says `platformClient.test.js` and `platformDirectory.test.js` "cover the parser and the offset page".
   - `:124` says `platformDirectory.test.js` covers "paging parameters, offset page metadata and ordering".
   - Both files still assert the retired shape until tasks 10.3 and 10.4 rewrite them.
   - `useRead.test.jsx` loses only its merge test at 10.6, and none of the cited P2-2 claims rests on that test.
   - Delivery is one commit (DL.0), so the wording holds in the committed tree. sdd-verify should confirm it.
4. **SPA evidence is not cited yet.** The dated evidence paragraph cites only .NET proofs, because the SPA offset tests arrive in batches B–C. No task adds SPA citations to TRACEABILITY. The orchestrator or 13.8 may decide whether one is wanted.
5. **Shell parsing.** Two long Bash one-liners and the first check script failed to parse at a `$'\r$'` inside a quoted command substitution.
   - None of the checks in those attempts ran.
   - The ADR replacement in the first attempt had already run, exactly once.
   - The checks were then rerun cleanly, and the logs named above come from those clean runs.
6. **Parallel work in the tree** is unchanged from A3 Issue 7. This unit wrote none of it, and it did not touch the untracked `docs/superpowers/plans/2026-09-13-identity-access-module-separation.md`.

### Remaining tasks

- 1.11 (awaits the A1-01 decision).
- Batch B: 9.1–10.8.
- Batch C: 11.1–11A.12.
- Batch D: 12.1–12.9.
- Then Phase 13 and Delivery.

### Workload / PR boundary (A4)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: A4 (Phase 8A). It closes batch A.
- **Boundary.** It starts from A3's working tree, where `SPEC.md`, `TRACEABILITY.md` and ADR-004 still describe the retired contract. It ends with all three describing the offset contract.
- **Review impact.** Documents only: +48/−13 lines across three files.

## Work unit B1 — Phase 9 (client pagination helper)

Scope: tasks 9.1–9.10, and nothing else. Three SPA files changed: `src/Web/ClientApp/src/api/pagination.js` (created), `src/Web/ClientApp/src/api/pagination.test.js` (created, row 23) and `src/Web/ClientApp/src/api/problemDetails.test.js` (row 8: one import and the 9.5 guard). This change's `tasks.md` (checkboxes 9.1–9.10) and `apply-progress.md` were also updated. `src/api/problemDetails.js` was not edited. Logs are `<scratchpad>/b1-*`. No git write was made. No parallel-work file was touched (AGENTS.md, CLAUDE.md, `.agents/skills/*`, `.codex/*`, the module-separation plan). `testTimeout` stays at 15000 ms, and no journey was run.

### Order of work

1. **Safety net.** `SPA-F src/api/problemDetails.test.js` passed 25/25 before its edit (`b1-safety-problemDetails.log`). `pagination.js` and `pagination.test.js` are new.
2. **Four RED → GREEN cycles,** each RED run before its production code: 9.1 → 9.2 (paging arguments), 9.3 → 9.4 (`readPage`), 9.6 → 9.7 (`sendPage`) and 9.8 → 9.9 (`readEveryPage`). Every GREEN run includes every earlier test in the file.
3. **9.5 guard** after 9.4, run against the unedited `problemDetails.js`.
4. **9.10 verification,** then the full `SPA-G` step sequence as regression evidence for this unit (not a Phase 9 gate), with the failing files rerun alone.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 9.1 | `src/api/pagination.test.js` (new, row 23) | Unit (Vitest) | N/A (new) | ✅ Written first: 9 tests for the four task bullets plus triangulation. Ran `SPA-F src/api/pagination.test.js`: exit 1, `Failed to resolve import "./pagination"`, no tests collected (`b1-9.1-red.log`) | ✅ at 9.2 | ✅ `{ pageNumber: 0, pageSize: 500 }` → 1/100; `{ pageNumber: 3, pageSize: 50 }` kept as asked, which forces real clamping instead of a constant; `pageSize` 0 and -5 → 1, never the default; `'abc'`, `null` and `undefined` → 1/25; `boundedPage(null)` → `{ pageNumber: 1, pageSize: 25 }`; the seven member names | ➖ None needed |
| 9.2 | `src/api/pagination.js` (new) | Unit | N/A (new) | ✅ 9.1 | ✅ exit 0, 9/9 (`b1-9.2-green.log`) | ✅ as 9.1 | ➖ None needed: the constants (`FIRST_PAGE_NUMBER`, `MIN_PAGE_SIZE`, `DEFAULT_PAGE_SIZE`, `MAX_PAGE_SIZE`, `DEFAULT_PAGE`) and the one `integerOr` helper were written in GREEN, as the task requires. The plan snippet's fallback that turns a `pageSize` of 0 into 25 is not copied |
| 9.3 | same test file | Unit | ✅ 9/9 (9.2) | ✅ Six refusals and two valid pages. Ran: exit 1, 8 failed / 9 passed (17), every failure `readPage is not a function` (`b1-9.3-red.log`) | ✅ at 9.4 | ✅ a missing total; a fractional page; `pageSize` 500; a string flag; `pageNumber` 0; `totalPages` -1; a page with rows and an empty collection, each returned as the same object | ➖ None needed |
| 9.4 | `src/api/pagination.js` | Unit | ✅ 9/9 | ✅ 9.3 | ✅ exit 0, 17/17 (`b1-9.4-green.log`) | ✅ as 9.3 | ➖ None needed: the bounds reuse the 9.2 constants, with one `isCount` helper. The developer message is English: "The page metadata does not match the offset pagination contract." |
| 9.5 | `src/api/problemDetails.test.js` (row 8) | Unit (guard) | ✅ 25/25 before the edit (`b1-safety-problemDetails.log`) | N/A: a guard, expected to PASS. It discriminates: without `nextCursor` in `FORBIDDEN_SUCCESS_KEYS`, `readSuccess` would throw "The response is missing pageNumber, …" instead, which does not match `/nextCursor/` | ✅ exit 0, 26/26 (`b1-9.5-guard.log`). `problemDetails.js` is unedited: its mtime is still 14:42:11 (A1), its only difference from `HEAD:src/Web/ClientApp/src/features/identity/api/problemDetails.js` is A1's schema import depth (+1/−1), and `FORBIDDEN_SUCCESS_KEYS` still ends in `nextCursor` (`b1-9.5-problemDetails-unchanged.log`) | ➖ Single scenario | ➖ None needed |
| 9.6 | `src/api/pagination.test.js` | Unit (mocked `send`) | ✅ 17/17 | ✅ Five tests. Ran: exit 1, 5 failed / 17 passed (22), every failure `sendPage is not a function` (`b1-9.6-red.log`) | ✅ at 9.7 | ✅ a `null` page sends `?pageNumber=1&pageSize=25` with `{ expect: paginationMembers, signal }`; page 2 sends `?pageNumber=2&pageSize=25`; `pageNumber` 0 rejects as `ClientFailure` `{ code: 'unreadable_response', status: 0 }`; an `ApiProblem` and a `ClientFailure` each reject as the same instance | ➖ None needed |
| 9.7 | `src/api/pagination.js` | Unit | ✅ 17/17 | ✅ 9.6 | ✅ exit 0, 22/22 (`b1-9.7-green.log`) | ✅ as 9.6 | ➖ None needed: a `send` rejection propagates outside the `try`, so only a `readPage` throw is reclassified |
| 9.8 | `src/api/pagination.test.js` | Unit (mocked `readOne`) | ✅ 22/22 | ✅ Four tests. Ran: exit 1, 4 failed / 22 passed (26), every failure `readEveryPage is not a function` (`b1-9.8-red.log`) | ✅ at 9.9 | ✅ 30 roles served 25 per page: only `{ pageNumber: 1, pageSize: 100 }` and `{ pageNumber: 2, pageSize: 100 }` are read, and 30 distinct role ids come back in order although page 2 repeats `role-25` as a fresh object, so the key, not object identity, deduplicates; 3 roles take one read; with `hasNextPage` always true, exactly 100 reads, the last at page 100, then `ClientFailure` `unreadable_response`; the same `signal` reaches both reads | ➖ None needed |
| 9.9 | `src/api/pagination.js` | Unit | ✅ 22/22 | ✅ 9.8 | ✅ exit 0, 26/26 (`b1-9.9-green.log`) | ✅ as 9.8 | ➖ None needed |
| 9.10 | both test files; `src/api/` lint | Verify | — | — | ✅ `SPA-F src/api/pagination.test.js src/api/problemDetails.test.js`: exit 0, 2 files, 52 tests (`b1-9.10-spa-f.log`). `npx eslint src/api/`: exit 0, no output (`b1-9.10-eslint.log`) | — | — |

### Test Summary (B1)

- **Tests written:** 27. `pagination.test.js` has 26: paging arguments 9, the strict reader 8, the paged read 5 and the whole-collection walk 4. `problemDetails.test.js` gains the 9.5 guard.
- **Tests passing:** 52/52 focused (26 in each file).
- **Layers used:** Unit 27 (Vitest). No MSW: `send` and `readOne` are `vi.fn` mocks.
- **Approval tests:** none. No existing code was refactored, and `problemDetails.js` is untouched.
- **Pure functions created:** 3 (`boundedPage`, `paginationSearch` and `readPage`). `sendPage` and `readEveryPage` are async and act only through the functions passed to them.

### Regression evidence (B1; not a Phase 9 gate)

The `SPA-G` steps ran from `src/Web/ClientApp`, each on its own, after 9.10 (`b1-gate-exits.txt`, 20:04:47–20:06:30):

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Vitest | `npx vitest run` | 1 | Test Files 2 failed / 44 passed (46); Tests 4 failed / 572 passed (576); 78.89 s | `b1-gate-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output | `b1-gate-eslint.log` |
| Unused keys | `npm run i18n:unused` | 0 | "No unused keys found" | `b1-gate-i18n.log` |
| Build | `npx vite build` | 0 | 2,628 modules, built in 1.14 s; the only warning is the known chunk-size notice | `b1-gate-vite.log` |

- 576 tests are the 549 of the 1.11 closure, plus the 26 in `pagination.test.js`, plus the 9.5 guard. The 46 files are the earlier 45 plus `pagination.test.js`. The log has 66 MSW unhandled-request warnings, as every earlier run does.
- **Classification by file and test name against the 1.1 record** (`b1-gate-classification.txt`). The alone reruns ran one file per process, one after the other, after the run had finished (`b1-alone-exits.txt`, 20:07:24–20:09:01):

| Record | File > test | Full run | Failure | Alone (`SPA-F <file>`) | Disposition |
| --- | --- | --- | --- | --- | --- |
| #1 | `ExternalProofResume.test.jsx` > resumes a roles target beyond the first page exactly once with its original draft and version | × 15 035 ms | `Test timed out in 15000ms` | ✓ 13 179 ms (file 9/9, exit 0) | **Excused by decision A1-01.** It also passed alone, so the exception was not load-bearing |
| #2 | same file > resumes a members target beyond the first page exactly once with its original draft and version | × 13 511 ms | `expected [] to deeply equal [ { …(2) } ]` | ✓ 7 590 ms | Baseline-timeout rule: passes alone |
| #6 | same file > finishes the ownership transfer the person asked for, against the roster as it stands | × 2 040 ms | `Unable to find an element with the text: Bruno` | ✓ 1 353 ms | Baseline-timeout rule: passes alone |
| #4 | `IdentityAccessReviewRevalidation.test.jsx` > R6C > continues the 'members' directory with 101 distinct records | × 16 721 ms | `Test timed out in 15000ms` | ✓ 10 337 ms (file 11/11, exit 0) | Baseline-timeout rule: passes alone |

- **Failing tests not on the record:** none.
- **Uses of the baseline-timeout rule:** 3 (#2, #4, #6). **Uses of the A1-01 exception:** 1 (#1, not load-bearing). **The #8 cascade clause:** 0; #8 passed.
- **Screen tests that go red because of batch B, so far (B1): none.** Nothing outside `src/api/` imports `pagination.js` yet (`grep -rn "api/pagination"` over `src` outside `src/api/` finds nothing), and the only edit to an existing test file adds one test. The four failures above are 1.1 record entries that pass alone, as at the 1.11 closure.
- **Expected from the next batch B unit.** Phase 10 moves `identityClient`, `platformClient` and `useRead` to offset pages, so the cursor-shaped screen fixtures are expected to go red from there until batch C (10.8: `SPA-G` is not expected green until 11A.11). That unit records the measured list.

### Work Unit Evidence (B1)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/api/pagination.test.js src/api/problemDetails.test.js` → exit 0, 2 files, 52 tests (`b1-9.10-spa-f.log`). `npx eslint src/api/` → exit 0, no output (`b1-9.10-eslint.log`). **RED runs:** 9.1 exit 1 (module missing); 9.3 exit 1, 8 failed / 9 passed; 9.6 exit 1, 5 failed / 17 passed; 9.8 exit 1, 4 failed / 22 passed. **GREEN runs:** 9/9, 17/17, 22/22 and 26/26; the 9.5 guard 26/26. |
| Runtime harness command/scenario and exact result | N/A for this unit. tasks.md Suggested Work Units gives batch B no runtime harness (MSW-only; the screens migrate in C; the journeys run at 13.5), and `pagination.js` has no caller yet, so no screen or route executes it. Regression evidence instead: the `SPA-G` steps above. Vitest exit 1 with only 1.1 record failures, each passing alone; eslint 0; `i18n:unused` 0; `vite build` 0. |
| Rollback boundary | Delete `src/Web/ClientApp/src/api/pagination.js` and `src/Web/ClientApp/src/api/pagination.test.js`. Remove the `paginationMembers` import and the retired-cursor test (9 added lines) from `src/Web/ClientApp/src/api/problemDetails.test.js`, keeping A1's move. Uncheck 9.1–9.10 in tasks.md, and drop this section and the B1 row of "Cumulative task state". No other file references the module, and there is no data or schema change. At delivery the unit couples to batches A and C (E7). |

### Files changed (B1)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/api/pagination.js` | Created (9.2, 9.4, 9.7, 9.9): `paginationMembers`, `DEFAULT_PAGE_SIZE`, `MAX_PAGE_SIZE`, `MAX_WALK_PAGES`, `pageSizeOptions`, `DEFAULT_PAGE`, `boundedPage`, `paginationSearch`, `readPage`, `sendPage` and `readEveryPage`. Its only import is `ClientFailure` from `./apiTransport`. 101 lines |
| `src/Web/ClientApp/src/api/pagination.test.js` | Created (row 23; 9.1, 9.3, 9.6, 9.8): 26 tests, 212 lines |
| `src/Web/ClientApp/src/api/problemDetails.test.js` | Modified (row 8; 9.5): the `paginationMembers` import and the retired-cursor refusal test (+9 lines against the moved file) |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 9.1–9.10 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: cumulative table updated; B1 appended; A1–A4 kept verbatim |

Test-file boundary: `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` (`b1-git-status.txt`, 65 entries) lists, among SPA test files, exactly A1's entries plus `src/api/pagination.test.js`. A1's entries are rows 8, 11, 12 and 16–21, with the moved-from paths of rows 8, 17 and 18 as ` D`. Row 23 was created, and row 8 gained the 9.5 guard, as their rows say. B1 created, edited, moved or deleted no other test file. The Phase 10 rows 6 and 7 are untouched, rows 11 and 12 carry only A1's import paths, and every screen test is untouched.

### Deviations from design (B1)

- **`boundedPage` is exported.** design.md's contract (`export function boundedPage(page)`) and plan:814 export it. Task 9.2's export list omits it but requires its null-safety. One test calls it directly (`boundedPage(null)`), as the design's testing strategy names.
- **Triangulation beyond the task bullets.** 9.1 adds an in-range page kept as asked, 9.3 adds a second valid page (an empty collection), and 9.6 adds a page-2 request. Each forces real logic instead of a constant.
- **9.5 test shape.** The guard uses the file's existing `json(200, …)` helper instead of the plan snippet's inline `new Response(…)` (D21: adapt snippets to each test file's style). The assertion is the plan's: `rejects.toThrow(/nextCursor/)` with `paginationMembers` declared.
- **Duplicate keys.** `readEveryPage` keeps a key's first position and the later read's row (`Map.set`). The design names only deduplication by key. The role pickers read names, so either choice offers the same roles.
- **`readOne` signature.** The walk calls `readOne({ pageNumber, pageSize: MAX_PAGE_SIZE }, { signal })`, which matches how 10.2 wraps `listRoles(tenantId, page, options)`.

### Issues found (B1)

1. No blocker.
2. **No upper bound on `pageNumber` in the client.** Per AD12 and task 9.2, an integer `pageNumber` is clamped only to at least 1.
   - An integer above `PaginationQuery.MaxPageNumber` (21 474 836) is sent as is, and the server clamps it.
   - One beyond Int32 would get `400 invalid_request`.
   - Screens take page numbers from loaded metadata, so neither case is reachable through `TablePagination`. Recorded, not acted on.
3. **Numeric strings take the default.** `Number.isInteger('50')` is false, so a string page size becomes 25. The plan's screen snippet converts with `Number(event.target.value)` (plan:1015), and batch C must keep that conversion.
4. **Line endings.** The Write tool saved `pagination.js` and `pagination.test.js` with CRLF on every line (101/101 and 212/212), and `problemDetails.test.js` stays CRLF (153/153). `.editorconfig:29` asks for LF, and git normalizes them (`core.autocrlf=true`). Content diffs are unaffected (compare A1 Issue 5 and A3 Issue 3).
5. **Engram split.** With B1, part 2 of the Engram copy would exceed 50,000 characters. B1 is therefore saved in the new topic `sdd/offset-pagination-standard/apply-progress-part-3`, and the preambles of parts 1 and 2 now name part 3.
6. **Parallel work in the tree** is unchanged from A3 Issue 7. B1 wrote none of it.
7. **`main` moved during this unit.** HEAD moved from `2716aa6` to `f29ca74` (`fix(apphost): forward the Google client and pin the local frontend port`), a commit from another chat. It changes only `docs/features/identity-access/RUNNING-LOCALLY.md` and `src/AppHost/Program.cs`, so it touches no B1 input or output, and the 9.5 comparison against `HEAD` reads a file that commit did not change. B1 made no git write, and nothing is staged.

### Remaining tasks

- Batch B: 10.1–10.8.
- Batch C: 11.1–11A.12.
- Batch D: 12.1–12.9.
- Then Phase 13 and Delivery.

### Workload / PR boundary (B1)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: B1 (Phase 9). It opens batch B.
- **Boundary.** It starts from A4's working tree, where no client module reads offset pages. It ends with the shared, tested helper in `src/api/pagination.js` and the retired-cursor guard. No client, hook or screen uses the helper yet, so the SPA's behaviour is unchanged.
- **Review impact.** +101 production lines and +221 test lines (212 new, and 9 added to `problemDetails.test.js`), all inside `src/Web/ClientApp/src/api/`.

### Correction round 1 — validator finding B1-V01 (2026-09-13)

Scope: finding B1-V01 only. No source file, test file, `tasks.md` or other planning text changed. This section is the only edit to this file. The header's "Correction rounds merged" line and "Cumulative task state" were left as they are, so Engram parts 1 and 2 stay identical to the file and only part 3 changes. No git write was made, and nothing is staged. Logs are `<scratchpad>/corr-b1-*`.

| Finding | Disposition | Change made | Evidence |
| --- | --- | --- | --- |
| B1-V01 (should-fix, B1 boundary check 5) | **No change to B1**, as the finding's fix says. Its two remaining actions are not the executor's: confirming with the user that `f29ca74` came from their parallel chat, and changing later validators' HEAD check from `2716aa6` to `f29ca74`. Both go to the orchestrator as decisions (Issues 1 below). | This section only. 9.1–9.10 stay `[x]`: their evidence does not depend on `HEAD`, and the focused rerun below is green on `f29ca74`. | The evidence list below |

**Evidence gathered again in this round** (`corr-b1-git.log`):

- `git log --oneline -1` → `f29ca74 fix(apphost): forward the Google client and pin the local frontend port`. `git reflog` records it as `commit` at 2026-09-13 20:08:21 -0300; the entry before it is `2716aa6` (2026-09-12 23:37:52 -0300).
- `git rev-parse --short f29ca74^` → `2716aa6`, and `git merge-base --is-ancestor 2716aa6 HEAD` → yes. `main` moved forward by one commit; nothing was rewritten.
- `git diff --name-only 2716aa6 f29ca74` → `docs/features/identity-access/RUNNING-LOCALLY.md` and `src/AppHost/Program.cs`. Neither is a design.md File Changes path or a declared test row. `git status --short` over both paths is empty, so this change holds no uncommitted edit to them either.
- The commit message ends in a `Co-Authored-By` trailer. DL.5 forbids that trailer for this change's commit only, so it does not bind `f29ca74`. It is one more sign that the commit is not this change's.
- `git diff --cached --name-only` → no lines.
- Timing: B1's alone reruns ran 20:07:24–20:09:01, so the commit landed during them. It touched no file under `src/Web/ClientApp`, so no Vitest run could read it.
- `origin/main` (the local ref; nothing was fetched) is also `f29ca74`, and `git rev-list --left-right --count origin/main...HEAD` → `0 0`.
- Not re-verified by the executor: the validator's transcript search placing the commit in the session "Gmail SMTP email delivery" (worktree `gmail-smtp-delivery-6e5640`). It is cited as the validator's evidence.

**Every `2716aa6` reference in this change's artifacts is a baseline, not a HEAD check.** In tasks.md they are `:3` (header), `:148` and `:149` (the 1.1 records), `:542` (12.7 `git show 2716aa6:…`, which keeps working because `2716aa6` is an ancestor of `HEAD`) and `:580` (13.8 history). proposal.md, design.md, exploration.md, two spec files and this file name it the same way. The validator's check 5 lives in the validator's prompt, not in any artifact, so no artifact text was changed.

**Hybrid parity** (`corr-b1-parity-before.log`, `corr-b1-parity-after.log`). `engram export` wrote the store to the scratchpad, and a script compared each Engram copy, without its preamble and with normalized line endings, to its file segment: `tasks` and `tasks-part-2` against tasks.md split at "## Phase 9", and the three apply-progress parts against this file split at "## Work unit A3" and "## Work unit B1". Before this round all five were identical. tasks.md did not change, so its Engram copies were already in sync and were not rewritten. After this round only part 3 was updated, and the same comparison, rerun after both writes, found all five identical.

#### TDD Cycle Evidence (B1 correction round 1)

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 9.1–9.10 (recheck for B1-V01) | `src/api/pagination.test.js`, `src/api/problemDetails.test.js` | Unit (Vitest), rerun only | ✅ B1's 52/52 | N/A: no test and no production code was written, edited or deleted. No new RED → GREEN cycle exists, so none is claimed | ✅ Rerun on `f29ca74`: exit 0, 2 files, 52 tests (`corr-b1-9.10-spa-f.log`, started 20:35:27). `npx eslint src/api/`: exit 0, no output (`corr-b1-9.10-eslint.log`) | N/A: nothing new to triangulate | N/A: no code changed |

#### Work Unit Evidence (B1 correction round 1)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/api/pagination.test.js src/api/problemDetails.test.js` → exit 0, Test Files 2 passed (2), Tests 52 passed (52) (`corr-b1-9.10-spa-f.log`). `npx eslint src/api/` → exit 0, no output (`corr-b1-9.10-eslint.log`). The B1 files keep their B1 modification times: `pagination.js` 20:03:12, `pagination.test.js` 20:02:41, `problemDetails.test.js` 19:59:48. `problemDetails.js` is still at 14:42:11 (A1). |
| Runtime harness command/scenario and exact result | N/A. No code, test or runtime path changed, and batch B has no runtime harness (tasks.md Suggested Work Units). The `SPA-G` regression run was not repeated, because `f29ca74` changes no path under `src/Web/ClientApp`. |
| Rollback boundary | Delete this section from this file and from Engram `sdd/offset-pagination-standard/apply-progress-part-3`. No code, test, `tasks.md`, data or schema change. |

#### Deviations (B1 correction round 1)

- The header's "Correction rounds merged" line and "Cumulative task state" were not edited; this section is the record of the round. Both live in Engram part 1, which would otherwise have to be rewritten in full for one line.
- The Engram tasks copies were proven identical to tasks.md by script instead of being rewritten, because tasks.md did not change.

#### Issues found (B1 correction round 1)

1. **ORCHESTRATOR DECISION NEEDED: confirm where `f29ca74` came from, and set later validators' HEAD check.** The finding asks the orchestrator to confirm with the user that `f29ca74` is their parallel chat's commit, and then have later validators expect `f29ca74` instead of `2716aa6`. The executor cannot confirm the provenance. A check pinned to one hash will fail again the next time another chat commits to `main`. One option for the orchestrator to judge: `HEAD` is `2716aa6` or a descendant of it, no commit since `2716aa6` touches a path in this change's scope, and nothing is staged.
2. **DL.6 note.** The local `origin/main` ref already points at `f29ca74`, so this change's single commit will sit on top of it. If `origin/main` gains commits that local `main` lacks before DL.6, a fast-forward push is impossible until they are integrated, and DL.6 forbids force-pushing. Recorded, not acted on.
3. No other issue. B1 Issues 1–7 stand unchanged.

## Work unit B2 — Phase 10 (SPA clients and useRead)

Scope: tasks 10.1–10.8, and nothing else. Seven SPA files changed. Three are production files: `src/Web/ClientApp/src/features/identity/api/identityClient.js`, `src/Web/ClientApp/src/features/platform/api/platformClient.js` and `src/Web/ClientApp/src/features/identity/useRead.js`. Four are declared test rows: 6 (`useRead.test.jsx`), 7 (`identityClient.test.js`), 11 (`platformClient.test.js`) and 12 (`platformDirectory.test.js`). This change's `tasks.md` (checkboxes 10.1–10.8) and `apply-progress.md` were also updated. No screen file and no screen test was edited. Logs are `<scratchpad>/b2-*`. No git write was made, and nothing is staged. No parallel-work file was touched (AGENTS.md, CLAUDE.md, `.agents/skills/*`, `.codex/*`, the module-separation plan). `testTimeout` stays at 15000 ms, and no journey was run. HEAD is still `f29ca74`.

### Order of work

1. **Safety net.** `SPA-F` over the four Phase 10 test files, before any edit: exit 0, 4 files, 35 tests (`identityClient.test.js` 1, `platformClient.test.js` 17, `platformDirectory.test.js` 7, `useRead.test.jsx` 10) (`b2-safety.log`, 20:47:02).
2. **Three RED → GREEN cycles,** each RED run against unedited production code: 10.1 → 10.2 (`identityClient.js`); 10.3 and 10.4 → 10.5 (`platformClient.js`, untouched until both REDs had run); 10.6 → 10.7 (`useRead.js`, untouched until its RED had run). The RED runs started at 20:50:11, 20:51:38, 20:52:35 and 20:53:51, and the GREEN runs at 20:54:51, 20:54:58 and 20:55:05.
3. **REFACTOR** of one fixture in `platformClient.test.js`, then the 10.8 verification, rerun after the refactor.
4. **Regression evidence:** the `SPA-G` steps, then every failing file alone, one per process, to separate batch B consequences from load.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 10.1 | `src/features/identity/api/identityClient.test.js` (row 7) | Unit (Vitest, mocked `send`) | ✅ 1/1 inside the 35/35 safety net | ✅ Written first: 14 new tests (for each of the three lists: the requested page's URL with `expect: paginationMembers` and the signal, a `null` page, a well-formed page returned as answered, and `pageNumber` 0; the catalogue walk; a one-page catalogue), and the `:7` body became a full page. Ran exit 1, 11 failed / 4 passed (15) (`b2-10.1-red.log`): the three URL tests (spy mismatch), the three `null`-page tests (the path had no query), the three `pageNumber` 0 tests (the body resolved as data) and both catalogue tests (`listRoleCatalogue is not a function`). The 4 passes are the signal test and the three "returns a well-formed page" tests, which pass on the old client because a mocked `send`'s body is returned as is | ✅ at 10.2 | ✅ page 2 at 50 and a `null` page; a well-formed page returned as the same object and `pageNumber` 0 refused as `ClientFailure` `{ code: 'unreadable_response', status: 0 }`; 30 roles over two served pages (only `pageNumber` 1 and 2 at `pageSize` 100 are requested, `role-25` repeats and is kept once, the signal and `paginationMembers` reach both reads) and 3 roles in one request | ➖ None needed |
| 10.2 | `src/features/identity/api/identityClient.js` | Unit | ✅ 10.1 | ✅ 10.1 | ✅ exit 0, 15/15 (`b2-10.2-green.log`) | ✅ as 10.1 | ✅ Written clean in GREEN: one private `tenantPage` helper serves the three lists; `listRoles` is one const shared by the method and the walk; `continued` and its comment deleted. `npx eslint` on the file: exit 0 |
| 10.3 | `src/features/platform/platformClient.test.js` (row 11) | Unit (Vitest; mocked `send`, and MSW with the real transport) | ✅ 17/17 inside the safety net | ✅ Written first: 12 new tests. For each of the four directories: `{ pageNumber: 0, pageSize: 500 }` sends `?pageNumber=1&pageSize=100` with `expect: paginationMembers` and the signal (mocked `send`); `pageNumber` 0 rejects as `ClientFailure` `unreadable_response` (mocked `send`); `{ items: [], nextCursor: null }` rejects as `unreadable_response` (MSW). The signal test now calls `(null, { signal })` over a full page, and the no-antiforgery read answers a full page. Ran exit 1, 14 failed / 15 passed (29) (`b2-10.3-red.log`): the four bounded-page tests (spy mismatch; the deleted `page()` built `?limit=25` for that argument), the four malformed-page tests (the body resolved), the four retired-shape tests (resolved instead of rejecting), the signal test (`Cannot read properties of null (reading 'limit')`) and the no-antiforgery read (`unreadable_response`, because the old client still declared the cursor member) | ✅ at 10.5 | ✅ drift in the metadata (mocked `send`, classified by `sendPage`) and drift in the members (MSW, refused by the transport); bounded values and a `null` page | ✅ After GREEN, the existing "refuses a Platform response shaped like an internal Result" test nests the offset page fixture instead of `{ items: [], nextCursor: null }` in `value`. Rerun exit 0, 29/29 (`b2-10.3-refactor.log`) |
| 10.4 | `src/features/platform/platformDirectory.test.js` (row 12) | Unit (Vitest, MSW with the real transport) | ✅ 7/7 inside the safety net | ✅ Rewritten first over full offset pages: the default page sends only `pageNumber=1&pageSize=25`; `pageSize` 10 000 and 0 (with `pageNumber` -3) send 100 and 1/1; the page asked for (1, then 2, at size 1) comes back with its position metadata; a `null` page; typed items with their metadata; a page missing `totalCount`; a bare array. No `limit` or `cursor` assertion remains. Ran exit 1, 5 failed / 2 passed (7) (`b2-10.4-red.log`): four failures are `unreadable_response`, because the old client declared the cursor member, so the transport refused every full offset page before the URL assertions ran; the `null`-page test failed on `Cannot read properties of null (reading 'limit')`. The two drift guards (a missing member, a bare array) passed on the old client too | ✅ at 10.5 | ✅ default, clamped high and clamped low; page 1 and page 2 with their flags; valid and drifted bodies | ➖ None needed |
| 10.5 | `src/features/platform/api/platformClient.js` | Unit | ✅ 10.3, 10.4 | ✅ 10.3, 10.4 | ✅ exit 0, 2 files, 36/36 (29 + 7) (`b2-10.5-green.log`) | ✅ as 10.3 and 10.4 | ✅ Written clean in GREEN: `DIRECTORY`, `MINIMUM_LIMIT`, `MAXIMUM_LIMIT` and `page()` deleted; the header comment describes offset pages; one `sendPage` import. `npx eslint` on the file: exit 0 |
| 10.6 | `src/features/identity/useRead.test.jsx` (row 6) | Unit (React Testing Library `renderHook`) | ✅ 10/10 inside the safety net | ✅ Written first: every `cursor` argument became a page (`secondPage`, `widerFirstPage`) and the fixture an offset page. Three assertions were added: the manual refresh calls `load` with `{ page, signal }` (abort test); the stale-generation test records the pages `[undefined, secondPage, widerFirstPage]` and the older request's aborted signal (E10); the retry test expects `load` to receive `{ page: secondPage, signal }` again and `refresh` to resolve `undefined` (E8; the hook never rethrows). The merge test is deleted. Ran exit 1, 3 failed / 6 passed (9) (`b2-10.6-red.log`), each failure `load` receiving `{ cursor, signal }` | ✅ at 10.7 | ✅ page 2 at 25 and page 1 at 50; a retryable `500` keeps the data and retries page 2; a refused `403` clears it; an older answer that settles late is ignored | ➖ None needed |
| 10.7 | `src/features/identity/useRead.js` | Unit | ✅ 10.6 | ✅ 10.6 | ✅ exit 0, 9/9 (`b2-10.7-green.log`) | ✅ as 10.6 | ✅ Written clean in GREEN: `refresh(page)` passes `{ page, signal }`; the merge branch is deleted, and a loaded answer replaces the data; one comment names E8 and E10. `toProblem(failure)` and `isRetryable(problem)` still appear exactly once each (`identityFiles.contract.test.js:136-138` passed in the full run). `npx eslint` on the file: exit 0 |
| 10.8 | the five Phase 9–10 test files | Verify | — | — | ✅ exit 0, 5 files, 86 tests (`b2-10.8-spa-f.log`, 20:55:59), and again after the REFACTOR (`b2-10.8-spa-f-final.log`, 20:57:52) | — | — |

### Test Summary (B2)

- **Tests written or rewritten:**
  - `identityClient.test.js`: 14 new, 1 fixture changed (15 in all).
  - `platformClient.test.js`: 12 new, 3 changed (the signal test, the no-antiforgery read fixture and the Result-shaped fixture) (29 in all).
  - `platformDirectory.test.js`: all 7 rewritten. Five are renamed: "asks for the default first page and no filter", "asks for the page it was given and reads where that page sits", "reads a null page as the default first page", "reads each directory as its own typed items and offset page metadata" and "refuses a directory page that is missing a declared page member".
  - `useRead.test.jsx`: 9 kept with page arguments (one renamed, "keeps loaded data through a retryable failure and retries the page that was asked for"), and the merge test deleted.
- **Tests passing:** 86/86 focused.
- **Layers used:** Unit (Vitest) only: mocked `send`, MSW with the real transport, and `renderHook`.
- **Approval tests:** the 35 existing tests of the four files, run before any edit.
- **Pure functions created:** none. The clients delegate to B1's `sendPage` and `readEveryPage`.

### Regression evidence (B2; not a gate)

10.8 does not expect `SPA-G` to be green until 11A.11. `b2-gate-run.sh` ran each step from `src/Web/ClientApp`, even after an earlier step failed (`b2-gate-exits.txt`, 20:57:46–20:59:07):

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Vitest | `npx vitest run` | 1 | Test Files 8 failed / 38 passed (46); Tests 97 failed / 504 passed (601); 53.71 s | `b2-gate-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output | `b2-gate-eslint.log` |
| Unused keys | `npm run i18n:unused` | 0 | "No unused keys found" | `b2-gate-i18n.log` |
| Build | `npx vite build` | 0 | Built; the only warning is the known chunk-size notice | `b2-gate-vite.log` |

- 601 tests are B1's 576, plus 14 in `identityClient.test.js` and 12 in `platformClient.test.js`, less the deleted merge test. The 46 files are the same as at B1.
- `identityFiles.contract.test.js` passed, so the twelve action catches and `useRead.js`'s single classification are unchanged.
- Record entry #9 (`ExternalProofReturnRevalidation.test.jsx`) passed. Entries #1–#8 sit in files that now fail deterministically for batch B reasons (below). The baseline-timeout rule and the A1-01 exception were therefore not used: every failing test also fails alone.

### Screen tests turned red by batch B (recorded for batch C; not edited)

Each failing file was rerun alone after the gate, one per process (`b2-alone-exits.txt`, 20:59:50–21:02:19). Every alone run reproduces the full-run count exactly, so all 97 failures are deterministic, not load. The counts of `nextCursor` below are occurrences in each test file, and the rendered-sentence counts are occurrences in its alone log.

| File (declared row) | Full run | Alone | Why it is red | Batch C task |
| --- | --- | --- | --- | --- |
| `src/features/identity/roles/RolesPage.test.jsx` (row 1) | 9 of 10 failed | exit 1, 9 failed / 1 passed | Its list fixtures answer `{ items, nextCursor }` (3 `nextCursor`). `listRoles` now expects the offset members, so the strict reader refuses them as `unreadable_response` ("We could not read the answer." 32 times), and no role renders. The screen's `load` still reads `cursor` and passes a merge callback that `useRead` no longer takes | 11.1, 11A.1, 11A.3 |
| `src/features/identity/members/MembersPage.test.jsx` (row 2) | 12 of 14 | exit 1, 12 / 2 | The same drift (5 `nextCursor`; the sentence 42 times): neither the roster nor the one-page role catalogue loads | 11.3 |
| `src/features/identity/invitations/InviteMemberPage.test.jsx` (row 3) | 13 of 15 | exit 1, 13 / 2 | The same drift on invitations and roles (6 `nextCursor`; the sentence 94 times) | 11.5 |
| `src/features/platform/PlatformPanel.test.jsx` (row 9) | 16 of 23 | exit 1, 16 / 7 | The same drift on the three directories (10 `nextCursor`; the sentence 68 times) | 11.7, 11.8 |
| `src/features/platform/identities/PlatformIdentitiesPage.test.jsx` (row 10) | 24 of 31 | exit 1, 24 / 7 | The same drift (10 `nextCursor`; the sentence 84 times) | 11.9, 11.10, 11A.2 |
| `src/features/identity/ExternalProofResume.test.jsx` (row 4) | 9 of 9 | exit 1, 9 / 0 | Its handlers answer `{ items, nextCursor }` and follow `?cursor=` (`:35`, `:41`, `:83-94`, `:205`). The rows ("Bruno") and the "Show more roles/members" continuation never render. The failure dumps print neither the English nor the Spanish unreadable-response sentence, so the alert itself is not evidenced; the cause is the same cursor-shaped fixture | 11A.8, with 11A.9 |
| `src/features/identity/IdentityAccessReviewRevalidation.test.jsx` (row 5) | 9 of 11 | exit 1, 9 / 2 | The shared helper `pageResponse = (items, nextCursor = null) => HttpResponse.json({ items, nextCursor })` (`:36`) feeds `rolesAre` and `membersAre`, so R6A (5 tests) and R6B (1 test) fail with R6C (3 tests, whose walk reads `cursor` and `limit` at `:195-227`). The sentence appears 32 times. The two passing tests are the R6A session revocations, which read no list | 11A.10; see Issues 2 |
| `src/i18n/presentation.test.jsx` (row 14) | 5 of 13 | exit 1, 5 / 8 | `pageOf = (items) => ({ items, nextCursor: null })` (`:25`). The Spanish screens render the `es` unreadable-response sentence ("No pudimos leer la respuesta. …" 24 times) instead of their rows | 11.14 |

- **A test file with cursor-shaped fixtures that stays green:** `src/AppRoutes.test.jsx` (row 13) passed 20/20. Its list fixtures still answer `{ items: [], nextCursor: null }` (`:41`, `:43`, `:44`), but its route tests assert only that each protected page renders. See Issues 3.
- **Failing tests not explained by batch B:** none.

### Work Unit Evidence (B2)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/features/identity/api/identityClient.test.js src/features/platform/platformClient.test.js src/features/platform/platformDirectory.test.js src/api/pagination.test.js src/features/identity/useRead.test.jsx` → exit 0, Test Files 5 passed (5), Tests 86 passed (86) (`b2-10.8-spa-f-final.log`, after the REFACTOR; `b2-10.8-spa-f.log` before it). **RED runs:** 10.1 exit 1, 11 failed / 4 passed; 10.3 exit 1, 14 failed / 15 passed; 10.4 exit 1, 5 failed / 2 passed; 10.6 exit 1, 3 failed / 6 passed. **GREEN runs:** 15/15; 36/36 over two files; 9/9. **REFACTOR:** `platformClient.test.js` 29/29. `npx eslint` over the seven files → exit 0, no output (`b2-eslint-files.log`). |
| Runtime harness command/scenario and exact result | N/A for this unit. tasks.md Suggested Work Units gives batch B no runtime harness (MSW-only; the screens migrate in C; the journeys run at 13.5), and 10.8 does not expect `SPA-G` green until 11A.11. The journeys would fail on every list screen until batch C, and were not run. Regression evidence instead: the `SPA-G` steps above. Vitest exit 1, where every failure is a batch B screen-fixture failure reproduced alone; eslint 0; `i18n:unused` 0; `vite build` 0. |
| Rollback boundary | Revert B2's hunks in `identityClient.js`, `platformClient.js` and `useRead.js`, keeping A1's import-path lines. Revert B2's hunks in `identityClient.test.js`, `platformClient.test.js`, `platformDirectory.test.js` and `useRead.test.jsx`, keeping A1's import paths in rows 11 and 12. Uncheck 10.1–10.8, and drop this section, the B2 row of "Cumulative task state" and B2's words in the two merge lines of the header. B1's `pagination.js` stays. The screens are unchanged, so nothing else depends on the new client shape. There is no data or schema change. At delivery the unit couples to batches A and C (E7). |

### Files changed (B2)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/features/identity/api/identityClient.js` | Modified (10.2): imports `readEveryPage` and `sendPage`; a private `tenantPage` helper; `listRoles`, `listMembers` and `listTenantInvitations(tenantId, page, options)` read offset pages; the new `listRoleCatalogue(tenantId, options)` walks every roles page, keyed by `roleId`; `continued` and its comment deleted. +18/−18 against `HEAD`, which includes A1's two import-path lines |
| `src/Web/ClientApp/src/features/platform/api/platformClient.js` | Modified (10.5): imports `sendPage`; the four directory methods take `(page, options)`; `DIRECTORY`, `MINIMUM_LIMIT`, `MAXIMUM_LIMIT` and `page()` deleted; the header comment rewritten. +11/−31 |
| `src/Web/ClientApp/src/features/identity/useRead.js` | Modified (10.7): `refresh(page)` passes `{ page, signal }` to `load`; the merge branch deleted; a loaded answer replaces the data. +9/−8 against `HEAD`, which includes A1's import-path line |
| `src/Web/ClientApp/src/features/identity/api/identityClient.test.js` | Modified (row 7; 10.1): 15 tests. +106/−1 |
| `src/Web/ClientApp/src/features/platform/platformClient.test.js` | Modified (row 11; 10.3): 29 tests. +58/−8 against `HEAD`, which includes A1's import path |
| `src/Web/ClientApp/src/features/platform/platformDirectory.test.js` | Modified (row 12; 10.4): 7 tests over full offset pages. +57/−29 against `HEAD`, which includes A1's import path |
| `src/Web/ClientApp/src/features/identity/useRead.test.jsx` | Modified (row 6; 10.6): 9 tests; the merge test deleted. +36/−31 |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 10.1–10.8 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: in the header, the B2 row, "Later units" from 11.1, and the two merge lines (which now also name B2 and B1 correction round 1); B2 appended; A1–B1 kept verbatim |

Test-file boundary: `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 68 entries (`b2-git-status.txt`). They are B1's 65 plus `identityClient.test.js`, `useRead.test.jsx` and `features/platform/api/platformClient.js`. Among SPA test files the list is exactly A1's and B1's entries plus rows 6 and 7; rows 11 and 12 carry A1's import paths and B2's edits. No screen test (rows 1–5, 9, 10, 13–15) and no other test file was created, edited, moved or deleted. `git diff --cached --name-only` is empty.

### Deviations from design (B2)

- **A private `tenantPage` helper in `identityClient.js`.** The design names only `sendPage` for each list. One helper builds the tenant path and forwards the signal for all three lists, instead of three copies. `listRoles` is a const, so the method and `listRoleCatalogue` share one definition.
- **`options?.signal`.** Both clients pass `{ signal: options?.signal }` to `sendPage` instead of `options` itself, so a `null` options argument cannot throw, as with the cursor-era methods.
- **`listRoleCatalogue` resolves to the array of roles,** which is `readEveryPage`'s result, not a page. Today the pickers read `page.items` (`MembersPage.jsx:133-134`, `InviteMemberPage.jsx:114-115`), so 11.4 and 11.6 must read the array directly.
- **10.4 RED failure text.** tasks.md names "the client still sends `limit` and no `pageNumber`". Four of the five failures are `unreadable_response`: the old client declared the cursor member, so the transport refused every full offset page before the URL assertions ran. The fifth is `Cannot read properties of null (reading 'limit')` on a `null` page. The bounded-page spy mismatches of the 10.3 RED are the direct URL evidence.
- **Triangulation beyond the task bullets.** 10.1 adds a well-formed page returned as answered for each list, and a one-page catalogue. 10.3 runs every scenario on all four directories. 10.6 adds the E10 abort assertion and the never-rethrows assertion.
- **One fixture beyond the task bullets (REFACTOR, row 11).** The existing "refuses a Platform response shaped like an internal Result" test now nests the offset page fixture in `value`. The envelope is still refused, and the only `nextCursor` left in the file is the 10.3 retired-shape drift test, which 12.1 excludes.

### Issues found (B2)

1. No blocker.
2. **Batch C scope flag (row 5, 11A.10).** `IdentityAccessReviewRevalidation.test.jsx` fails 9 tests, not only R6C. The shared `pageResponse` helper (`:36`) behind `rolesAre` and `membersAre` also feeds R6A (5 tests) and R6B (1 test). Row 5 and 11A.10 name only the R6C rewrite, but R6A and R6B turn green only when that helper answers an offset page. The orchestrator should confirm that the row 5 rewrite covers the shared helper, or amend the row.
3. **11.11's premise does not hold.** 11.11 says `src/AppRoutes.test.jsx` "has failed since 10.5, as drift". After 10.5 it passes 20/20, because its route tests assert only that each protected page renders. Its cursor-shaped fixtures (`:41`, `:43`, `:44`) still need the 11.11 fixture change, so the screens read real pages and the 12.1 search finds no leftover.
4. **The Platform screens pass `useRead`'s whole argument as the page.** `PlatformPanel.jsx:130-132` and `PlatformIdentitiesPage.jsx:200-201` call `list…(options)`, so `{ page, signal }` lands in the `page` parameter of `(page, options)`. `boundedPage` ignores its extra members, so page 1 at 25 is requested, but the caller's signal is not forwarded until 11.8 and 11.10 give those loads the `(page, { signal })` shape. `RolesPage.jsx:136`, `MembersPage.jsx:126` and `InviteMemberPage.jsx:118` still forward the signal and pass `cursor`, which is now `undefined`, so they read page 1. The pickers (`MembersPage.jsx:133`, `InviteMemberPage.jsx:114`) read one page until 11.4 and 11.6.
5. **A stray file, removed.** A CLI help check, `engram export --help`, treated `--help` as a file name and wrote a 2,798,232-byte Engram export named `--help` in the repository root. It was untracked. After its content was confirmed to be an Engram export, it was deleted with `rm`, so it cannot reach a commit, and `ls` confirms it is gone. Nothing else was written outside the scratchpad and the files listed above.
6. **Line endings.** All seven edited files are CRLF on every line (207, 126, 96, 229, 112, 273 and 126 lines). Git printed LF-to-CRLF warnings for the four test files written whole. Content diffs are unaffected (compare B1 Issue 4).
7. **Engram split.** B2 is in part 3 (`sdd/offset-pagination-standard/apply-progress-part-3`), which stays under 50,000 characters. Parts 1 and 3 were rewritten. Part 2 is unchanged: it still names parts 1 and 3, but its preamble describes part 3 as holding B1 only. `tasks-part-2` was re-synced, and `tasks` part 1 is unchanged and was checked identical to the file.
8. **Parallel work in the tree** is unchanged from A3 Issue 7, and HEAD is still `f29ca74`. B2 wrote none of it.

### Remaining tasks

- Batch C: 11.1–11A.12.
- Batch D: 12.1–12.9.
- Then Phase 13 and Delivery.

### Workload / PR boundary (B2)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: B2 (Phase 10). It closes batch B.
- **Boundary.** It starts from B1's working tree, where both clients read cursors and `useRead` merged pages. It ends with both clients and `useRead` on offset pages through the shared helper. The screens are unchanged, and their cursor-shaped test fixtures stay red until batch C.
- **Review impact.** Production +38/−57 over three files, and tests +257/−69 over four files, both against `HEAD`, which includes A1's few import-path lines.

### Correction round 1 — validator finding B2-V03 (2026-09-13)

Scope: B2-V03 only. No source or test file changed, and 10.1–10.8 stay `[x]`. tasks.md row 5, 11A.10 and the carry-over index now cover the shared helper `pageResponse` (`:36`) behind `rolesAre`, `membersAre`, `invitationsAre` and R6B's roster read (`:165`). R6A and R6B change only through it and keep every assertion. This follows D12 (this file's recorded reads move to offset equivalents), so no confirmed decision changes. design.md is unchanged: its D12 note (`:458-461`) already covers the file's recorded reads. Issue 2 above is closed. The header and Engram part 1 are unchanged, as in B1 correction round 1. No git write. Logs: `<scratchpad>/corr-b2-*`.

**TDD Cycle Evidence (B2-V03)**

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 10.1–10.8 (recheck) | the five 10.8 files | Unit (Vitest), rerun only | ✅ B2's 86/86 | N/A: no code or test written, so no cycle is claimed | ✅ exit 0, 5 files, 86/86 (`corr-b2-10.8-spa-f.log`) | N/A | N/A |

**Work Unit Evidence (B2-V03)**

| Evidence | Required value |
|---|---|
| Focused test command and exact result | The 10.8 `SPA-F` → exit 0, 86/86. `npx vitest run src/features/identity/IdentityAccessReviewRevalidation.test.jsx` → exit 1, 9 failed / 2 passed (11), as expected until 11A.10: R6A role create, edit and retire, membership assignment and ownership transfer (5), R6B (1), R6C (3); "We could not read the answer." 32 times (`corr-b2-row5-alone.log`) |
| Runtime harness command/scenario and exact result | N/A: planning text only, and batch B has no runtime harness |
| Rollback boundary | Restore tasks.md row 5, 11A.10 and the carry-over line; drop this section; re-sync Engram `tasks`, `tasks-part-2` and `apply-progress-part-3` |

Hybrid parity: `corr-b2-parity.js` compares all five Engram copies with their file segments before this round (`corr-b2-parity-before.log`, all identical) and after its writes (`corr-b2-parity-after.log`).

## Work unit C1 — Phase 11, tasks 11.1–11.8 (RolesPage, MembersPage, InviteMemberPage, PlatformPanel)

Scope: tasks 11.1–11.8, and nothing else. Eight SPA files changed: the screens `RolesPage.jsx`, `MembersPage.jsx`, `InviteMemberPage.jsx` and `PlatformPanel.jsx`, and their declared test rows 1, 2, 3 and 9. This change's `tasks.md` (checkboxes 11.1–11.8) and `apply-progress.md` were also updated. No catalog key was deleted (that is 11.13). No other test file, page object, `AppRoutes.jsx`, `features/*/api/` file, theme or CSS was touched, and no journey was run. `testTimeout` stays at 15000 ms, and no per-test or query timeout was added. No git write was made, nothing is staged, and HEAD is still `f29ca74`. No parallel-work file was touched. Logs are `<scratchpad>/c1-*`.

### Order of work

1. **Safety net.** The four screen test files together, before any edit: exit 1, 50 failed / 12 passed (62) (`c1-safety.log`, 21:57:11). RolesPage fails 9 of 10, MembersPage 12 of 14, InviteMemberPage 13 of 15 and PlatformPanel 16 of 23. These are exactly B2's recorded batch B reds for rows 1, 2, 3 and 9, which this unit exists to turn green, so they are not a new regression.
2. **Four RED → GREEN cycles,** each RED run against the unedited screen: 11.1 → 11.2, 11.3 → 11.4, 11.5 → 11.6 and 11.7 → 11.8. Every GREEN run includes `src/test/identityFiles.contract.test.js`, which pins the `toProblem(error)` counts.
3. **Final focused run** over the five files, then the `SPA-G` steps as regression evidence (not a gate), then the boundary searches.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 11.1 | `src/features/identity/roles/RolesPage.test.jsx` (row 1) | Integration (Vitest, Testing Library, MSW with the real transport) | ⚠️ 1/10: the 9 recorded B2 reds | ✅ Written first. The fixtures answer `pageOf` (plan:1066-1071), and a `rolesServed` handler serves a collection by page and records each query string. The cursor test at `:195` is deleted. Six tests are added: 26 roles reached and back, 60 roles at 50 rows per page, a single page, an empty organization, the catalogue read once across two page changes, and a failed catalogue read. Ran exit 1, 5 failed / 10 passed (15) (`c1-11.1-red.log`, 22:00:50): four failures are `Unable to find role="button" and name "Go to next page"` or the range text; the failed-catalogue test fails with `expected [ '?pageNumber=1&pageSize=25', …(1) ] to deeply equal [ '?pageNumber=1&pageSize=25' ]`, because "Try again" re-requested the roles with the catalogue. The empty-organization test passes on the old screen too, which drew no control for it | ✅ at 11.2 | ✅ 26 rows (next, then previous), 60 rows at 50 per page, 3 rows (both buttons disabled), 0 rows (empty state, no control), two page changes with one catalogue read, and a catalogue that fails and is retried alone | — |
| 11.2 | `src/features/identity/roles/RolesPage.jsx` | same | ✅ 11.1 | ✅ 11.1 | ✅ exit 0, 2 files, 36/36: RolesPage 15, contract 21 (`c1-11.2-green.log`, 22:02:50). `npx eslint` on both files: exit 0 | ✅ as 11.1 | ✅ Written clean in GREEN. The page-independent `load`, `requested`, `go` and `retry` follow plan:990-997, and `rolePageReadTarget` replaces the literal; `appendRoles` and `showMore` are deleted |
| 11.3 | `src/features/identity/members/MembersPage.test.jsx` (row 2) | same | ⚠️ 2/14: the 12 recorded B2 reds | ✅ Written first. The fixtures answer `pageOf`; `membersServed` serves the roster by page; `rolesServedTwentyFiveAtATime` serves roles at most 25 to a page, whatever size is asked for, and records each page number. Three tests are added: the 26th member, 30 roles offered with pages `[1, 2]`, and 3 roles with `[1]`. Ran exit 1, 2 failed / 15 passed (17) (`c1-11.3-red.log`, 22:04:47): `Unable to find an element with the text: 1–25 of 26` and `Unable to find a label with the text of: Role 30`. The one-page test passes on the old picker, which reads one page | ✅ at 11.4 | ✅ 30 roles over two pages, 3 roles on one, and 26 members | — |
| 11.4 | `src/features/identity/members/MembersPage.jsx` | same | ✅ 11.3 | ✅ 11.3 | ✅ exit 0, 2 files, 38/38: MembersPage 17, contract 21 (`c1-11.4-green.log`, 22:05:41). eslint exit 0 | ✅ as 11.3 | ✅ Written clean in GREEN; `appendMembers` and `showMore` are deleted |
| 11.5 | `src/features/identity/invitations/InviteMemberPage.test.jsx` (row 3) | same | ⚠️ 2/15: the 13 recorded B2 reds | ✅ Written first. The fixtures answer `pageOf`; `invitationsServed` and `rolesServedTwentyFiveAtATime` serve by page. The `:247` test is rewritten as an offset page-change failure. Five tests are added: the 26th invitation; page 2 answering `500` with `trace-invitation-page`, where the page-1 rows and "1–25 of 26" stay and the alert follows the page control in document order, and "Try again" requests `[1, 2, 2]`; 30 roles with `[1, 2]`; a catalogue whose `hasNextPage` is always true, which takes exactly 100 requests and ends in "We could not read the answer." with "Try again" and no checkbox; and a network error on role page 2, which shows "We could not reach the service." in the fieldset, where "Try again" walks `[1, 2, 1, 2]` and offers Role 30. Ran exit 1, 5 failed / 14 passed (19) (`c1-11.5-red.log`, 22:08:29): two missing range texts or next-page buttons, `Unable to find a label with the text of: Role 30`, and two `Unable to find role="alert"`, because the old picker never walked | ✅ at 11.6 | ✅ many pages, one page never ending, a failed second page, and a failed page change | — |
| 11.6 | `src/features/identity/invitations/InviteMemberPage.jsx` | same | ✅ 11.5 | ✅ 11.5 | ✅ exit 0, 2 files, 40/40: InviteMemberPage 19, contract 21 (`c1-11.6-green.log`, 22:11:53). The 100-request walk took 309 ms. eslint exit 0 | ✅ as 11.5 | ✅ Written clean in GREEN. `appendInvitations`, `showMore` and the now-unused `pendingAction.more` are deleted |
| 11.7 | `src/features/platform/PlatformPanel.test.jsx` (row 9) | same | ⚠️ 7/23: the 16 recorded B2 reds | ✅ Written first. `directories()` and four handlers answer `pageOf`; `directoryServed` serves a directory by page. An `it.each` covers Organizations, Administrators and Audit: each section moves on its own to page 2, shows rows 26 and 30 but not 25, and records `[1, 2]`. The `:425-456` test is deliberately rewritten: page 2 answers `500` with `trace-refresh`; the alert shows "Something went wrong. Try again." and "Reference: trace-refresh"; `acme-1` and "1–25 of 30" stay; "Try again" requests `[1, 2, 2]`; the section is `aria-busy` while the retry waits; and `acme-26` shows with no alert after it. Ran exit 1, 4 failed / 22 passed (26) (`c1-11.7-red.log`, 22:10:55): three `Unable to find an element with the text: 1–25 of 30`, and the rewrite fails with `Unable to find an accessible element with the role "button" and name "Go to next page"` | ✅ at 11.8 | ✅ three directories, and a failed page change with its retry | — |
| 11.8 | `src/features/platform/PlatformPanel.jsx` | same | ✅ 11.7 | ✅ 11.7 | ✅ exit 0, 2 files, 47/47: PlatformPanel 26, contract 21 (`c1-11.8-green.log`, 22:12:46). eslint exit 0 | ✅ as 11.7 | ✅ Written clean in GREEN. A file-local `useDirectoryPages` hook and a `DirectoryPagination` component serve the three directories (Deviations 5); `organizations.more` is removed |

### Test Summary (C1)

- **Tests written or rewritten:**
  - RolesPage: 6 new, 1 deleted, 2 fixtures changed (15 in all).
  - MembersPage: 3 new, 3 fixtures changed (17).
  - InviteMemberPage: 5 new, one of them the `:247` rewrite that replaces the deleted test; 2 fixtures changed (19).
  - PlatformPanel: an `it.each` of 3 new cases, the `:425-456` rewrite, and 4 fixtures changed (26).
- **Tests passing:** 98/98 focused: the four files and the contract.
- **Layers used:** Integration (Vitest with Testing Library and MSW over the real transport).
- **Approval tests:** the existing tests of the four files, kept with offset fixtures, are the approval net for the unchanged behaviour (proof, field errors, refusals, confirmations).
- **Pure functions created:** none. The screens delegate to B1's helper and B2's clients.

### Final focused run and regression evidence (C1; not a gate)

`c1-exits.txt` records each step, run one after another from `src/Web/ClientApp` (22:14:34–22:16:07):

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Focused | `npx vitest run` over the four screen test files and `src/test/identityFiles.contract.test.js` | 0 | Test Files 5 passed (5); Tests 98 passed (98) | `c1-focused.log` |
| Vitest | `npx vitest run` | 1 | Test Files 4 failed / 42 passed (46); Tests 47 failed / 569 passed (616); 61.99 s | `c1-gate-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output | `c1-gate-eslint.log` |
| Unused keys | `npm run i18n:unused` | 1 | `en/identity`: `invitations.member.showMore`, `members.showMore` and `roles.showMore` are unused (Issues 2) | `c1-gate-i18n.log` |
| Build | `npx vite build` | 0 | Built; the only warning is the known chunk-size notice | `c1-gate-vite.log` |

- 616 tests are B2's 601, plus 5 in RolesPage, 3 in MembersPage, 4 in InviteMemberPage and 3 in PlatformPanel.
- **Every remaining failure is a batch B file owned by a later task, with B2's exact count:** `src/i18n/presentation.test.jsx` 5 of 13 (11.14), `IdentityAccessReviewRevalidation.test.jsx` 9 of 11 (11A.10), `ExternalProofResume.test.jsx` 9 of 9 (11A.8) and `PlatformIdentitiesPage.test.jsx` 24 of 31 (11.9, 11.10, 11A.2). That is B2's 97 less the 50 this unit turned green. No other test fails.
- The baseline-timeout rule and the A1-01 exception were not used: this run is not a gate.

**Searches** (`c1` boundary run):

- `rg -n "labelRowsPerPage|labelDisplayedRows|getItemAriaLabel" src/Web/ClientApp/src --glob '!**/*.test.*'` finds nothing (exit 1). Every `TablePagination` label comes from the MUI locale.
- `rg -n "eslint-disable" src/Web/ClientApp/src` lists the same 9 lines as the 1.1 record. Only line numbers moved: `MembersPage.jsx:216` → `:226`, `RolesPage.jsx:236` → `:247`, `:327` → `:326`.
- Show-more call sites, locales excluded: `roles.showMore`, `members.showMore`, `invitations.member.showMore` and `organizations.more` have none in production code. `identities.more` remains at `PlatformIdentitiesPage.jsx:550` (11.10), and `presentation.test.jsx:117-118` still asserts both platform keys (11.14).
- `nextCursor`/`cursor` in the four screens remains only in the RolesPage and MembersPage resume walks (`RolesPage.jsx:154,225-230,248`; `MembersPage.jsx:149,201-208,227`). 11A.9 rewrites those walks, and 11A.8 expects them to still follow `nextCursor` until then.
- `toProblem(error)`: RolesPage 2, MembersPage 2, InviteMemberPage 1; the contract's total of 12 passes.
- `variant="contained"`: exactly one in each of the four screens, as before.

### Work Unit Evidence (C1)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/features/identity/roles/RolesPage.test.jsx src/features/identity/members/MembersPage.test.jsx src/features/identity/invitations/InviteMemberPage.test.jsx src/features/platform/PlatformPanel.test.jsx src/test/identityFiles.contract.test.js` → exit 0, Test Files 5 passed (5), Tests 98 passed (98) (`c1-focused.log`). **RED runs:** 11.1 exit 1, 5 failed / 10 passed; 11.3 exit 1, 2 failed / 15 passed; 11.5 exit 1, 5 failed / 14 passed; 11.7 exit 1, 4 failed / 22 passed. **GREEN runs** (each with the contract): 36/36, 38/38, 40/40 and 47/47. `npx eslint` over each screen and its test → exit 0, no output. |
| Runtime harness command/scenario and exact result | N/A for this unit, as recorded. tasks.md Suggested Work Units gives batch C the journeys command at 13.5, and Phase 13 owns it; this unit was told not to run the journeys. They would also fail today: PlatformIdentitiesPage still reads its directory as a cursor page until 11.10. Regression evidence instead: the `SPA-G` steps above, with Vitest failing only in the four batch B files later tasks own, eslint 0, `vite build` 0, and `i18n:unused` flagging the three identity keys 11.13 deletes. |
| Rollback boundary | Revert C1's hunks in `RolesPage.jsx`, `MembersPage.jsx`, `InviteMemberPage.jsx` and `PlatformPanel.jsx`, keeping A1's import-path lines. Revert C1's hunks in the four test files (rows 1, 2, 3, 9) to their `HEAD` content. Uncheck 11.1–11.8, and drop this section, the C1 row of "Cumulative task state" and C1's words in the merge line, plus Engram `sdd/offset-pagination-standard/apply-progress-part-4`. B1's helper and B2's clients stay. There is no data or schema change. At delivery the unit couples to batches A and B (E7). |

### Files changed (C1)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/features/identity/roles/RolesPage.jsx` | Modified (11.2): the page-independent roles `load`; a separate permission catalogue `useRead` loaded once per tenant; `requested`, `go` and `retry`; one list `ProblemMessage` for the roles problem, else the catalogue problem, whose "Try again" repeats only what failed; the wait holds while either read has no data and gives way to any problem; rows and the control render only when both reads have data; `TablePagination` with the page-change problem beside it; the post-mutation refresh also refreshes the catalogue; `appendRoles`, `showMore` and `roles.showMore` removed. +68/−46 against `HEAD`, which includes A1's import-path lines |
| `src/Web/ClientApp/src/features/identity/members/MembersPage.jsx` | Modified (11.4): the page-independent roster `load`; `requested`, `go` and `retry`; the picker reads `listRoleCatalogue` and still drops retired roles; `TablePagination` with the page-change problem beside it; `appendMembers`, `showMore` and `members.showMore` removed. +38/−32 |
| `src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.jsx` | Modified (11.6): the page-independent invitations `load`; `requested`, `go` and `retry`, used by both invitation retries; the picker reads `listRoleCatalogue`; `TablePagination` in the invitations section, before the `'pagination'` problem and its retry; `appendInvitations`, `showMore`, `pendingAction.more` and `invitations.member.showMore` removed. +33/−37 |
| `src/Web/ClientApp/src/features/platform/PlatformPanel.jsx` | Modified (11.8): `useDirectoryPages(list, enabled)` gives each directory a page-independent `load`, `requested`, `go` and `retry`; the three section retries call `retry`; `DirectoryPagination` renders a `TablePagination` under each directory; `organizations.more` removed. +45/−12 |
| `src/Web/ClientApp/src/features/identity/roles/RolesPage.test.jsx` | Modified (row 1; 11.1): 15 tests. +129/−16 |
| `src/Web/ClientApp/src/features/identity/members/MembersPage.test.jsx` | Modified (row 2; 11.3): 17 tests. +109/−5 |
| `src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.test.jsx` | Modified (row 3; 11.5): 19 tests. +167/−20 |
| `src/Web/ClientApp/src/features/platform/PlatformPanel.test.jsx` | Modified (row 9; 11.7): 26 tests. +111/−23 |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 11.1–11.8 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: the C1 row, "Later units" from 11.9, and C1 in the merge line; C1 appended; A1–B2 kept verbatim |

Test-file boundary: `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 72 entries (`c1-git-status.txt`): B2's 68 plus rows 1, 2, 3 and 9. Rows 4, 5, 10 and 13–15 are untouched, and no other test file was created, edited, moved or deleted. `git diff --cached --name-only` is empty.

### Deviations from design (C1)

1. **RolesPage "Try again" repeats each read that errored.** AD14 says it refreshes only the read that errored. When both errored, the one button refreshes both, still only failed reads. The list `ProblemMessage` shows the roles problem when the list is the read target, else the catalogue problem.
2. **RolesPage's wait gives way to a problem.** The skeleton shows while either read is loading without data, except when either read already has a problem. That keeps the initial wait and a terminal error mutually exclusive (ui-composition-rules "Loading"). The permission fieldset's own two-row wait now follows the catalogue read. A row-scoped refusal is claimed only by the rows actually drawn.
3. **The resume walks are untouched.** `RolesPage.jsx` and `MembersPage.jsx` keep reading `nextCursor` for their walks only, because 11A.9 rewrites those walks and 11A.8's RED expects them unchanged. On offset pages `nextCursor` is always `null`, so no walk searches past the loaded page until 11A.9, as it has been since B2.
4. **Post-mutation refreshes still request the first page.** The roles, roster and invitations refreshes after a write keep `refresh(undefined)`, because 11A.7 sets the page argument and 11A.3's RED expects `pageNumber=1`. RolesPage adds the catalogue's `refresh()` beside it (AD14), and PlatformPanel keeps its three `refresh(undefined)` calls.
5. **PlatformPanel uses a file-local hook and component.** Three directories in one component need the same page state and the same control, so `useDirectoryPages` and `DirectoryPagination` live in `PlatformPanel.jsx` instead of three inline copies. They are not shared modules, carry no styling, and compose standard MUI only, like the file's existing `StatusChip`, `Placeholder` and `EmptyBlock`.
6. **Control placement.** Each PlatformPanel control sits directly under its directory. That is before the suspension or revocation confirmation card, where "More organizations" used to come after it. On the three tenant screens the control follows the collection, and the page-change problem and its outlined retry follow the control (AD15). The page-change retries on RolesPage and MembersPage keep the old `disabled={isBusy}`. `go` does not set `isBusy`, as the old `showMore` did, so row actions stay enabled while a page loads.
7. **Test design.** The picker tests serve roles 25 to a page although the walk asks for 100, as a server with a smaller cap would; PD-2 names "served 25 per page". InviteMemberPage asserts AD15's "beside the control" as the alert following "1–25 of 26" in document order. PlatformPanel's three directory tests are one `it.each`.
8. **11.7's recorded RED for the `:425-456` rewrite** stops at the missing "Go to next page" button, the first assertion to fail, before the assertion that "Try again" requests page 2.

### Issues found (C1)

1. No blocker.
2. **Ordering note for 11.13 and 11.14.** `i18n:unused` scans test files. `organizations.more` is not flagged although no production code uses it, because `presentation.test.jsx:117` still reads it, and `:118` reads `identities.more`. So after 11.10, 11.13's RED shows only the three identity keys until 11.14 deletes `presentation.test.jsx:116-119`. Deleting the platform keys first would turn that test red until 11.14. The later unit should run 11.14's deletion with, or before, 11.13's platform-key deletion.
3. **Line endings.** The four test files were written whole and are LF (333, 400, 454 and 586 lines). The four screens stay CRLF (561, 537, 477 and 546 lines). Git warns it will normalize the test files to CRLF (`core.autocrlf=true`), and `.editorconfig:29` asks for LF. Content diffs are unaffected (compare B1 Issue 4 and B2 Issue 6).
4. **Still red, owned by later tasks,** with B2's counts: PlatformIdentitiesPage 24, ExternalProofResume 9, IdentityAccessReviewRevalidation 9 and presentation 5.
5. **Parallel work in the tree** is unchanged from A3 Issue 7, and HEAD is still `f29ca74`. C1 wrote none of it.

### Remaining tasks

- Batch C: 11.9–11.15 and 11A.1–11A.12.
- Batch D: 12.1–12.9.
- Then Phase 13 and Delivery.

### Workload / PR boundary (C1)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: C1 (tasks 11.1–11.8). It opens batch C.
- **Boundary.** It starts from B2's working tree, where the four screens read offset pages through cursor-era code and their tests were red. It ends with page controls, page state and whole role catalogues on those four screens, all four test files green, and the resume walks, read-state edges and key deletions left to later batch C tasks.
- **Review impact.** Production +184/−127 over four files, and tests +516/−64 over four files, against `HEAD`, which includes A1's import-path lines.

## Work unit C2 — Phase 11, tasks 11.9–11.15 (PlatformIdentitiesPage, route fixtures, Spanish guard, show-more keys, verification)

Scope: tasks 11.9–11.15, and nothing else. Nine SPA files changed: the screen `PlatformIdentitiesPage.jsx`; declared test rows 10, 13, 14 and 15 (`PlatformIdentitiesPage.test.jsx`, `AppRoutes.test.jsx`, `presentation.test.jsx` and `spanish.test.jsx`); and the four catalogs `src/i18n/locales/{en,es}/{platform,identity}.json`. This change's `tasks.md` (checkboxes 11.9–11.15) and `apply-progress.md` were also updated. `AppRoutes.jsx`, page objects, `features/*/api/`, the theme and CSS are untouched, and no journey was run. `testTimeout` stays at 15000 ms, and no per-test timeout was added. No git write was made, nothing is staged, and HEAD is still `f29ca74`. No parallel-work file was touched. Logs are `<scratchpad>/c2-*`.

### Order of work

1. **Safety net** (`c2-safety.log`, 22:46:32), before any edit, over the five test files this unit touches or proves with: exit 1, 29 failed / 64 passed (93). PlatformIdentitiesPage fails 24 of 31 and presentation 5 of 13, exactly B2's recorded batch B reds for rows 10 and 14. AppRoutes passes 20/20, spanish 12/12 and `catalog.contract.test.js` 17/17.
2. **11.9 → 11.10**, one RED → GREEN cycle whose GREEN ran in two steps (Deviations 2).
3. **11.11, 11.12 and 11.14**, each run on its own.
4. **11.13 after 11.14** (Deviations 1): the unused-key RED, the `en` deletions, a parity RED, then the `es` deletions.
5. **11.15**, then the `SPA-G` steps as regression evidence (not a gate), then the boundary searches.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 11.9 | `src/features/platform/identities/PlatformIdentitiesPage.test.jsx` (row 10) | Integration (Vitest, Testing Library, MSW with the real transport) | ⚠️ 7/31: the 24 recorded B2 reds | ✅ Written first. A `pageOf` helper (C1's shape) answers every fixture: `countedDirectory` and the five inline handlers. `identitiesServed` serves the directory by page and records each query string, and `numberedIdentities` builds numbered accounts. The two cursor tests are deleted: "reaches the next page with the cursor the server returned…" and "disables pagination until its in-flight read settles". Five tests are added: 26 accounts reached with next and back with previous (searches at size 25 for pages 1, 2, 1); 60 accounts, page 2, then 50 rows per page (`?pageNumber=1&pageSize=50`, "1–50 of 60"); 3 accounts with both buttons disabled; an empty directory with its empty state and no control; and page 1's rows, its range "1–25 of 26" and the progress bar held while page 2 is on its way, with no skeleton (it replaces the in-flight test). Ran against the unedited screen: exit 1, 4 failed / 30 passed (34) (`c2-11.9-red.log`, 22:52:07): `Unable to find an element with the text: 1–25 of 26` and `… 1–3 of 3`, and twice `Unable to find role="button" and name "Go to next page"`. The empty-directory test passes on the old screen too, which drew no control for it. Every pre-existing test passes with the offset fixtures | ✅ at 11.10 | ✅ 26 rows (next, then previous), 60 rows at 50 per page, 3 rows (both buttons disabled), 0 rows (no control), and a page change still in flight | — |
| 11.10 | `src/features/platform/identities/PlatformIdentitiesPage.jsx` | same | ✅ 11.9 | ✅ 11.9 | ✅ **Step A** (page state, `TablePagination`, the `directory` rename and the removal of `identities.more`, with the old loader kept): exit 1, 3 failed / 31 passed (34) (`c2-11.10-stepA.log`, 22:53:28): `expected [ 1, 1 ] to deeply equal [ 1, 2 ]`, and "26–26 of 26" and "26–50 of 60" never show, because the loader passed `useRead`'s whole `{ page, signal }` object as the page, so every page change asked for page 1. **Step B** (the loader becomes `({ page, signal }) => platform.listIdentities(page ?? DEFAULT_PAGE, { signal })`): exit 0, 2 files, 55/55: PlatformIdentitiesPage 34, contract 21 (`c2-11.10-green.log`, 22:54:40). `npx eslint` on the screen and its test: exit 0 | ✅ as 11.9 | ✅ Written clean in GREEN. The `Divider` import and the `pagerSlot` constant, which only the old pager used, are deleted, and the pager comment now describes the page control |
| 11.11 | `src/AppRoutes.test.jsx` (row 13) | Integration (router) | ✅ 20/20. **The RED premise does not hold:** tasks.md says the file has failed since 10.5, but it passed at the safety net | N/A: fixtures only. The three `{ items: [], nextCursor: null }` bodies (roles, members, invitations) answer one `emptyPage` offset constant. The permission catalogue's `[]` and the external-accounts body are not pages and stay | ✅ exit 0, 20/20 (`c2-11.11.log`, 22:54:59). `AppRoutes.jsx` is untouched | ➖ Fixtures only: no test or assertion changed | — |
| 11.12 | `src/i18n/spanish.test.jsx` (row 15) | Component (MUI locale through the theme) | ✅ 12/12 | N/A: a guard, expected to pass. One test renders `TablePagination` (count 120, page 0, 25 rows per page) inside `ThemeProvider` with `themeFor('es')`, and finds the enabled button named `esES` `getItemAriaLabel('next')`. The imports added are `ThemeProvider` and `TablePagination`. It discriminates: a node probe of `@mui/material/locale` gives "Ir a la página siguiente" for `esES`, where the English default is "Go to next page" | ✅ exit 0, 13/13 (`c2-11.12.log`, 22:55:11). The first eslint run failed: `166:38 error disallow literal string: themeFor('es') i18next/no-literal-string`, because the rule's `jsx-only` mode reads a call inside a JSX attribute. The theme is now built in a local `spanishTheme` constant, with no suppression: eslint exit 0, and 13/13 again (`c2-11.12-after-lint.log`, 22:57:23) | ➖ Single: the rows-per-page label keeps its existing proof at `:142-146` | ✅ The lint fix above |
| 11.13 | `npm run i18n:unused`, `src/i18n/catalog.contract.test.js` (row 20, not edited) and the four catalogs | Catalog gates | ✅ i18n:unused in C1's state (the three identity keys flagged); catalog 17/17 | ✅ **Unused-key RED, after 11.14:** `npm run i18n:unused` exit 1, `[en/identity]` `invitations.member.showMore`, `members.showMore` and `roles.showMore` (`c2-11.13-red.log`, 22:56:00). The script exits at its first failing namespace, so `platform` was checked through the same CLI: `CI=true node node_modules/i18next-cli/dist/esm/cli.js status en --unused --namespace platform` exit 1, `identities.more` and `organizations.more` (`c2-11.13-red-platform.log`, 22:56:29). No call site of the five keys remained outside the locales. **Parity RED, after the `en` deletions only:** `catalog.contract.test.js` exit 1, 2 failed / 15 passed; `es/identity` and `es/platform` report `unexpected key` for exactly the five keys (`c2-11.13-catalog-red.log`, 22:57:12), while `i18n:unused` already passed (`c2-11.13-unused-en-deleted.log`) | ✅ After the `es` deletions all four catalogs parse. `npm run i18n:unused` exit 0, "No unused keys found" for `common`, `identity` and `platform` (`c2-11.13-green-unused.log`, 22:59:57). `catalog.contract.test.js` exit 0, 17/17 (`c2-11.13-green-catalog.log`, 23:00:01) | ✅ Two gates, each seen failing on its own language: the unused-key gate on `en` and the parity gate on `es` | ➖ Deletions only; no key added, `identities.retry` kept |
| 11.14 | `src/i18n/presentation.test.jsx` (row 14) | Integration (Spanish rendering) | ⚠️ 8/13: the 5 recorded B2 reds | N/A: one fixture and one deletion. The `:25` `pageOf` answers one offset page with the seven members (`totalPages` is `Math.ceil(items.length / 25)`, so 0 when empty). The test at `:116-119`, "uses infinitives for Platform load-more actions", is deleted (D22) | ✅ exit 0, 12/12 (`c2-11.14.log`, 22:55:47). eslint exit 0 | ➖ Fixture and deletion only | — |
| 11.15 | the ten listed test files and the label-prop search | Verify | — | — | ✅ `SPA-F` over `spanish`, `catalog.contract`, `presentation`, `App.localization`, `RolesPage`, `MembersPage`, `InviteMemberPage`, `PlatformPanel`, `PlatformIdentitiesPage` and `useRead`: exit 0, Test Files 10 passed (10), Tests 163 passed (163) (`c2-11.15-spa-f.log`, 23:00:08). `rg -n "labelRowsPerPage|labelDisplayedRows|getItemAriaLabel" src/Web/ClientApp/src --glob '!**/*.test.*'`: exit 1, no match | — | — |

### Test Summary (C2)

- **Tests written or rewritten:**
  - PlatformIdentitiesPage: 5 new, 2 cursor tests deleted (one of them replaced by the in-flight page test), and 6 fixtures changed (34 in all).
  - AppRoutes: 3 fixtures changed (20).
  - spanish: 1 new guard (13).
  - presentation: 1 fixture changed and 1 test deleted (12).
- **Tests passing:** 163/163 in the 11.15 focused run; 55/55 in the 11.10 GREEN run with the contract; 20/20 AppRoutes; 17/17 catalog contract.
- **Layers used:** Integration (Vitest with Testing Library and MSW over the real transport), one component guard, and the two catalog gates.
- **Approval tests:** PlatformIdentitiesPage's pre-existing tests, kept with offset fixtures, are the approval net for the unchanged proof, refusal, confirmation and precondition behaviour.
- **Pure functions created:** none. The screen uses B1's helper and B2's client.

### Final focused run and regression evidence (C2; not a gate)

`c2-exits.txt` records each exit code. The steps ran one after another from `src/Web/ClientApp` (22:59:57–23:02:12):

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Focused | the 11.15 `SPA-F` | 0 | Test Files 10 passed (10); Tests 163 passed (163) | `c2-11.15-spa-f.log` |
| Vitest | `npx vitest run` | 1 | Test Files 2 failed / 44 passed (46); Tests 18 failed / 601 passed (619); 80.31 s | `c2-gate-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output | `c2-gate-eslint.log` |
| Unused keys | `npm run i18n:unused` | 0 | "No unused keys found" for `common`, `identity` and `platform` | `c2-gate-i18n.log` |
| Build | `npx vite build` | 0 | 2,639 modules transformed; the only warning is the known chunk-size notice | `c2-gate-vite.log` |

- 619 tests are C1's 616, plus 3 in PlatformIdentitiesPage (31 → 34) and 1 in spanish, less the 1 deleted in presentation.
- **Every remaining failure is a batch B file owned by a later task, with B2's exact count:** `ExternalProofResume.test.jsx` 9 of 9 (11A.8) and `IdentityAccessReviewRevalidation.test.jsx` 9 of 11 (11A.10: R6A 5, R6B 1, R6C 3). PlatformIdentitiesPage (24) and presentation (5) are now green, so 97 − 50 (C1) − 29 (C2) = 18. No other test fails.
- The baseline-timeout rule and the A1-01 exception were not used: this run is not a gate.

**Searches** (`c2` boundary run):

- The 11.15 label-prop search finds nothing (exit 1): every `TablePagination` label comes from the MUI locale.
- `rg -n "organizations\.more|identities\.more|members\.showMore|invitations\.member\.showMore|roles\.showMore" src/Web/ClientApp/src` (12.8's search) finds nothing (exit 1), locales and tests included.
- `platform:identities.retry` stays: `en` "Try again", `es` "Volver a intentar", read at `PlatformIdentitiesPage.jsx:454`.
- `git diff --stat -- src/Web/ClientApp/src/i18n/locales`: 4 files, +8/−18. The changed lines are exactly the five keys in `en` and in `es`, plus the comma dropped from the line before a key that closed its object.
- `rg -n -i "cursor|nextCursor" src/Web/ClientApp/src/features/platform/identities` finds nothing.
- `rg -n "eslint-disable" src/Web/ClientApp/src` lists the same 9 lines as C1; `PlatformIdentitiesPage.jsx:1` is unchanged.
- `variant="contained"` in `PlatformIdentitiesPage.jsx`: 2, as at `HEAD` (the suspension and reactivation confirmations, which never show together).

### Work Unit Evidence (C2)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/i18n/spanish.test.jsx src/i18n/catalog.contract.test.js src/i18n/presentation.test.jsx src/App.localization.test.jsx src/features/identity/roles/RolesPage.test.jsx src/features/identity/members/MembersPage.test.jsx src/features/identity/invitations/InviteMemberPage.test.jsx src/features/platform/PlatformPanel.test.jsx src/features/platform/identities/PlatformIdentitiesPage.test.jsx src/features/identity/useRead.test.jsx` → exit 0, Test Files 10 passed (10), Tests 163 passed (163) (`c2-11.15-spa-f.log`). **RED runs:** 11.9 exit 1, 4 failed / 30 passed; 11.10 step A exit 1, 3 failed / 31 passed; 11.13 `i18n:unused` exit 1 (three identity keys) and the platform-namespace check exit 1 (two keys); 11.13 parity exit 1, 2 failed / 15 passed. **GREEN runs:** 11.10 55/55 with the contract; 11.11 20/20; 11.12 13/13; 11.14 12/12; 11.13 `i18n:unused` exit 0 and catalog 17/17. `npx eslint` over every changed JS file → exit 0. |
| Runtime harness command/scenario and exact result | N/A for this unit, as recorded. tasks.md Suggested Work Units gives batch C the journeys command at 13.5, and Phase 13 owns it; this unit was told not to run the journeys. Regression evidence instead: the `SPA-G` steps above, with Vitest failing only in the two batch B files 11A.8 and 11A.10 own, eslint 0, `i18n:unused` 0 and `vite build` 0. |
| Rollback boundary | Revert C2's hunks in `PlatformIdentitiesPage.jsx`, keeping A1's import-path line. Revert C2's hunks in `PlatformIdentitiesPage.test.jsx`, `AppRoutes.test.jsx`, `spanish.test.jsx` and `presentation.test.jsx` (rows 10, 13, 15 and 14) to their `HEAD` content. Restore the five keys in `src/i18n/locales/{en,es}/{platform,identity}.json` (their `HEAD` content). Uncheck 11.9–11.15, and drop this section, the C2 row of "Cumulative task state", "11A.1 onward" and C2's words in the merge line, plus C2's part of Engram `sdd/offset-pagination-standard/apply-progress-part-4`. C1, B1's helper and B2's clients stay. There is no data or schema change. At delivery the unit couples to batches A and B (E7). Restoring the keys alone without the screen and presentation reverts turns `i18n:unused` red, because nothing reads them. |

### Files changed (C2)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.jsx` | Modified (11.10): `data: page` renamed to `directory`; the page-independent loader `({ page, signal }) => platform.listIdentities(page ?? DEFAULT_PAGE, { signal })`; `requested`, `go` and `retry`, with the section's "Try again" calling `retry`; `TablePagination` inside the directory section, after the table, while `totalCount > 0`; the "More accounts" pager, its `Divider` import, the `pagerSlot` constant and `identities.more` removed; `identities.retry` kept. +31/−25 against `HEAD`, which includes A1's import-path line |
| `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.test.jsx` | Modified (row 10; 11.9): 34 tests. +112/−42 |
| `src/Web/ClientApp/src/AppRoutes.test.jsx` | Modified (row 13; 11.11): fixtures only. +4/−3 |
| `src/Web/ClientApp/src/i18n/spanish.test.jsx` | Modified (row 15; 11.12): two imports and one guard test. +14/−0 |
| `src/Web/ClientApp/src/i18n/presentation.test.jsx` | Modified (row 14; 11.14): the `:25` fixture; the `:116-119` test deleted. +9/−6 |
| `src/Web/ClientApp/src/i18n/locales/en/platform.json`, `es/platform.json` | Modified (11.13): `organizations.more` and `identities.more` deleted. +2/−4 each |
| `src/Web/ClientApp/src/i18n/locales/en/identity.json`, `es/identity.json` | Modified (11.13): `invitations.member.showMore`, `members.showMore` and `roles.showMore` deleted. +2/−5 each |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 11.9–11.15 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: the C2 row, "Later units" from 11A.1, and C2 in the merge line; C2 appended; A1–C1 kept verbatim |

Test-file boundary: `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 80 entries (`c2-git-status.txt`): C1's 72, plus rows 10, 13, 14 and 15 and the four catalogs. Rows 4 and 5 are untouched, and no other test file was created, edited, moved or deleted. `git diff --cached --name-only` is empty. The five edited JS files stay CRLF; they were edited in place.

### Deviations from design (C2)

1. **11.14 ran before 11.13.** C1 Issue 2 recorded that `presentation.test.jsx:116-119` was the last reader of the two platform keys. Deleting it first lets 11.13's RED show all five keys, as the task expects. `i18n:unused` exits at its first failing namespace (`identity`), so the two platform keys were recorded with the same CLI run on `platform` alone. The `en` and `es` deletions were split to show the parity gate failing too.
2. **11.10's GREEN ran in two steps.** Step A added the control and page state and kept the old loader, which proved that the loader passed `useRead`'s `{ page, signal }` object as the page (every page change asked for page 1). Step B fixed the loader.
3. **"Try again" calls `retry` from 11.10.** The task names `retry` in the page state, so the section's existing retry calls `refresh(requested)`. The pre-existing 500 test proves it still reads page 1 after a first-read failure. Its page-2 proof is 11A.2's mirror of 11A.1, which may therefore pass at its own RED run; 11A.2's expected failure is "at least the mirrored past-the-end test".
4. **The in-flight pager test became a page-state test.** The old "More accounts" button was `disabled={isBusy || status === 'loading'}`. `TablePagination` stays enabled while a page loads, as C1's controls do. `useRead` aborts the older request (E10), and 11A.2 covers outrun clicks. The rewritten test proves the loading row of the read-state table instead: the loaded rows, their range and the progress bar stay, and no skeleton shows.
5. **Placement.** The control sits inside the directory's section, directly after the table, where the pager sat. It follows MUI's standard table composition, with no `Divider`, and renders only while rows are drawn and `totalCount > 0`. The section's single problem block and its retry stay above it (AD15).
6. **11.12's theme is a local constant.** The guard builds `themeFor('es')` outside the JSX, because `i18next/no-literal-string` flags the call inside a JSX attribute and no suppression may be added. The plan snippet's `labelRowsPerPage` assertion is left out, as tasks.md 11.12 says (the label's proof stays at `:142-146`).
7. **11.11 has no RED.** Its premise, that the file has failed since 10.5, does not hold: it passed 20/20 before and after the fixture change.

### Issues found (C2)

1. No blocker.
2. **For 11A.5 and 11A.6 on PlatformIdentitiesPage.** A past-the-end answer (empty `items`, `totalCount > 0`) draws no rows, so this screen shows its empty sentence ("No accounts are listed here.") and no control until 11A.6's effect requests the last page. That effect should keep the empty state from rendering, or flashing, while `totalCount > 0`.
3. **Still red, owned by later tasks,** with B2's counts: ExternalProofResume 9 (11A.8) and IdentityAccessReviewRevalidation 9 (11A.10).
4. **`i18n:unused` stops at the first failing namespace** (`scripts/check-unused-i18n.mjs:22`), so a run that fails can hide unused keys in later namespaces. 12.8 and Phase 13 should read a red run with that in mind.
5. **Post-mutation refreshes still request page 1.** `run` and `onProved` keep `refresh(undefined)`, because 11A.7 sets the page argument.
6. **Line endings.** The five JS files were edited in place and stay CRLF; the four catalogs keep their LF. Content diffs are unaffected.
7. **Parallel work in the tree** is unchanged from A3 Issue 7, and HEAD is still `f29ca74`. C2 wrote none of it.

### Remaining tasks

- Batch C: 11A.1–11A.12.
- Batch D: 12.1–12.9.
- Then Phase 13 and Delivery.

### Workload / PR boundary (C2)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: C2 (tasks 11.9–11.15). It closes Phase 11.
- **Boundary.** It starts from C1's working tree, where PlatformIdentitiesPage still paged by cursor and its tests, and presentation's, were red, and the five show-more keys were still in the catalogs. It ends with page controls on all five screens, the five keys gone from `en` and `es`, the Spanish page-control name guarded, and every Phase 11 test file green. The read-state edges, the past-the-end correction, the post-mutation page refresh and the resume walks are left to Phase 11A.
- **Review impact.** Production +31/−25 in one screen (against `HEAD`, including A1's import-path line), tests +139/−51 over four files, and catalogs +8/−18 over four files.

## Work unit C3 — Phase 11A, tasks 11A.1–11A.7 (read states, past-the-end correction, post-change page refresh)

Scope: tasks 11A.1–11A.7, and nothing else. Seven SPA files changed: the screens `RolesPage.jsx`, `MembersPage.jsx`, `InviteMemberPage.jsx`, `PlatformPanel.jsx` and `PlatformIdentitiesPage.jsx`, and declared test rows 1 and 10 (`RolesPage.test.jsx` and `PlatformIdentitiesPage.test.jsx`). This change's `tasks.md` (checkboxes 11A.1–11A.7) and `apply-progress.md` were also updated. `PlatformRetentionPage.jsx` is out of scope and untouched. No catalog key was added or deleted, no copy changed, and `platform:identities.retry` stays. The resume walks (11A.9) and rows 4 and 5 (11A.8, 11A.10) are untouched. `AppRoutes.jsx`, page objects, `features/*/api/`, the theme and CSS are untouched, and no journey was run. `testTimeout` stays at 15000 ms, and no per-test timeout was added. No git write was made, nothing is staged, and HEAD is still `f29ca74`. No parallel-work file was touched. Logs are `<scratchpad>/c3-*`.

### Order of work

1. **Safety net** (`c3-safety.log`, 23:27:59), before any edit, over the five screen test files and `src/test/identityFiles.contract.test.js`: exit 0, Test Files 6 passed (6), Tests 132 passed (132).
2. **Three REDs against the unedited screens:** 11A.1 (RolesPage), 11A.2 (PlatformIdentitiesPage), then 11A.3 (RolesPage).
3. **11A.4 and 11A.5:** no production edit. Their read-state tests already passed at the 11A.1 and 11A.2 REDs, on C1's and C2's screens (Deviations 1).
4. **11A.6 in two steps.** Step A added the effect alone to RolesPage and PlatformIdentitiesPage. Step B added the gate that draws nothing for a past-the-end page, and the effect and gate on the other three screens. ESLint then refused the effect's synchronous `setState`, so the correction was deferred to a microtask on all five screens (Deviations 3).
5. **11A.7** on all five screens.
6. The `SPA-G` steps as regression evidence (not a gate), then the boundary searches.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 11A.1 | `src/features/identity/roles/RolesPage.test.jsx` (row 1) | Integration (Vitest, Testing Library, MSW with the real transport) | ✅ 15/15, inside the 132 | ✅ Written first. Two helpers: `pagesServed` chooses the answer per page number and records each one; `watchShownText` is a `MutationObserver` that records the text of every node added while it watches. Five tests: (1) a network error on page 2 keeps "Retire Role 1" and "1–25 of 30", shows the alert "We could not reach the service.", and "Try again" requests `[1, 2, 2]`; (2) a `403` on page 2 shows "You do not have permission to do that here." and removes the rows, "Try again" and "Go to next page" (requests `[1, 2]`); (3) a first answer past the end (`pageNumber` 3, `totalCount` 30) moves to "26–30 of 30" with requests `[1, 2]`, no alert, and the empty sentence never drawn; (4) `totalCount` 0 keeps its empty state, with requests `[1]` after a 50 ms settle; (5) outrun clicks: page 2 is held until "1–50 of 100" shows, then released, and "1–50 of 100" and "Retire Role 50" stay, never "26–50 of 100". Ran exit 1, 1 failed / 19 passed (20) (`c3-11A.1-red.log`, 23:33:07): the past-the-end test, `Unable to find an element with the text: 26–30 of 30`. The other four pass on C1's screen (Deviations 1) | ✅ at 11A.6 step B | ✅ five paths: errored, refused, past the end, empty and not corrected, outrun | — |
| 11A.2 | `src/features/platform/identities/PlatformIdentitiesPage.test.jsx` (row 10) | same | ✅ 34/34, inside the 132 | ✅ Written first: the mirror of 11A.1, with the same two helpers (its `pagesServed` also installs the antiforgery and Platform context handlers). The retry is still named "Try again" (`platform:identities.retry`). Ran exit 1, 1 failed / 38 passed (39) (`c3-11A.2-red.log`, 23:34:16): the mirrored past-the-end test, `Unable to find an element with the text: 26–30 of 30`. The other four pass on C2's screen | ✅ at 11A.6 step B | ✅ as 11A.1 | — |
| 11A.3 | `src/features/identity/roles/RolesPage.test.jsx` (row 1) | same | ✅ 11A.1 | ✅ Written first. Page 2 of 30 roles is loaded and the password typed; "Retire Role 26" answers `204`, and the page-2 read after it answers `500` with `trace-retired`. Expected: the alert "Something went wrong. Try again." with "Reference: trace-retired"; one retirement; requests `[1, 2, 2]`; exactly one alert, and none in the role's row; the password field cleared. Ran exit 1, 2 failed / 19 passed (21) (`c3-11A.3-red.log`, 23:34:46): `Unable to find role="alert"`, because `run` still called `refresh(undefined)`, page 1, which answered `200`. The other failure is 11A.1's past-the-end test | ✅ at 11A.7 | ➖ Single: the spec has one scenario (a failed refresh after a retirement), and tasks.md 11A.7 assigns the other screens to this proof | — |
| 11A.4 | `RolesPage.jsx`, `MembersPage.jsx`, `InviteMemberPage.jsx` | same | ✅ 132/132 | ✅ 11A.1 | ✅ No production edit: the states already hold, and the errored, refused and outrun tests passed at the 11A.1 RED. Checked on each screen: (a) the skeleton shows only while loading with no data (RolesPage `awaitingReads`, MembersPage `initialLoading`, InviteMemberPage `status === 'loading' && data === null`); (b) `errored` keeps the rows and shows `ProblemMessage` plus an outlined `common:actions.tryAgain` calling `refresh(requested)`, at the list and at the page-change position; (c) `refused` clears the data, so the rows and `TablePagination` go; (d) the `'pagination'` block still renders its problem without data | ✅ as 11A.1 | ➖ None needed |
| 11A.5 | `PlatformPanel.jsx`, `PlatformIdentitiesPage.jsx` | same | ✅ 132/132 | ✅ 11A.2 | ✅ No production edit, as at 11A.4. The mirrored errored, refused and outrun tests passed at the 11A.2 RED, and 11.7 proves PlatformPanel's retry of the requested page. `platform:identities.retry` stays | ✅ as 11A.2 | ➖ None needed |
| 11A.6 | the five screens | same | ✅ 132/132 | ✅ 11A.1, 11A.2 | ✅ **Step A** (the effect alone, on RolesPage and PlatformIdentitiesPage): exit 1, 3 failed / 57 passed (60) (`c3-11A.6-stepA.log`, 23:35:50). Both past-the-end tests now reach "26–30 of 30" with `[1, 2]`, but fail `expected true to be false`: the false empty sentence was drawn while the correction ran. The third failure is 11A.3. **Step B** (the `pastTheEnd` gate, which draws no rows, empty state or control for a past-the-end page; effect and gate on all five screens): exit 1, 1 failed / 142 passed (143) (`c3-11A.6-green.log`, 23:37:32); only 11A.3 fails. **ESLint** then failed: 5 × `react-hooks/set-state-in-effect` (`c3-11A.6-eslint.log`). **After deferring the correction** (Deviations 3): eslint exit 0 (`c3-11A.6-eslint-after.log`, 23:39:01); tests exit 1, 1 failed / 142 passed (143), again only 11A.3 (`c3-11A.6-green-after-lint.log`, 23:39:08) | ✅ past the end, `totalCount` 0 (not corrected), and the empty sentence watched through the whole correction | ✅ The deferral above, with the tests still green after it |
| 11A.7 | the five screens | same | ✅ 11A.6 | ✅ 11A.3 | ✅ exit 0, Test Files 6 passed (6), Tests 143 passed (143) (`c3-11A.7-green.log`, 23:40:43). eslint over the five screens and two test files: exit 0 (`c3-11A.7-eslint.log`). `rg -n "refresh\(undefined\)\|refresh\(\)"` over the five screens leaves only the unpaged catalogue reads (`InviteMemberPage.jsx:293`, `RolesPage.jsx:201,365`, `MembersPage.jsx:184,482`) | ➖ Single: tasks.md assigns the other screens to 11A.3's proof | ➖ None needed. The page state stays inline per screen, as plan:990-997 and C1 chose (Deviations 7) |

### Test Summary (C3)

- **Tests written:**
  - RolesPage: 6 (the five 11A.1 tests and 11A.3), for 21 in all.
  - PlatformIdentitiesPage: 5 (11A.2), for 39 in all.
  - Each file gains two helpers, `pagesServed` and `watchShownText`. No existing test or fixture changed.
- **Tests passing:** 143/143 in the final focused run: RolesPage 21, MembersPage 17, InviteMemberPage 19, PlatformPanel 26, PlatformIdentitiesPage 39, contract 21.
- **Layers used:** Integration (Vitest with Testing Library and MSW over the real transport).
- **Approval tests:** the 132 safety-net tests, unchanged, are the approval net for everything else on the five screens.
- **Pure functions created:** none. The screens keep composing `useRead` and B1's helper.

### Final focused run and regression evidence (C3; not a gate)

`c3-exits.txt` records each exit code. The steps ran one after another from `src/Web/ClientApp` (23:41:49–23:43:11):

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Focused | `npx vitest run` over the five screen test files and `src/test/identityFiles.contract.test.js` | 0 | Test Files 6 passed (6); Tests 143 passed (143) | `c3-11A.7-green.log` |
| Vitest | `npx vitest run` | 1 | Test Files 2 failed / 44 passed (46); Tests 18 failed / 612 passed (630); 64.64 s | `c3-gate-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output | `c3-gate-eslint.log` |
| Unused keys | `npm run i18n:unused` | 0 | "No unused keys found" | `c3-gate-i18n.log` |
| Build | `npx vite build` | 0 | 2,639 modules transformed; the only warning is the known chunk-size notice | `c3-gate-vite.log` |

- 630 tests are C2's 619, plus 6 in RolesPage and 5 in PlatformIdentitiesPage.
- **Every remaining failure is a batch B file owned by a later task, with B2's exact count:** `ExternalProofResume.test.jsx` 9 of 9 (11A.8) and `IdentityAccessReviewRevalidation.test.jsx` 9 of 11 (11A.10: R6A 5, R6B 1, R6C 3). No other test fails.
- The baseline-timeout rule and the A1-01 exception were not used: this run is not a gate.

**Searches** (`c3` boundary run):

- `rg -n "eslint-disable" src/Web/ClientApp/src` lists 14 lines. Nine are the 1.1 record, at new line numbers (`MembersPage.jsx:247`, `RolesPage.jsx:268` and `:347`). Five are `// eslint-disable-next-line react-hooks/exhaustive-deps` on the 11A.6 effects: `RolesPage.jsx:172`, `MembersPage.jsx:167`, `InviteMemberPage.jsx:149`, `PlatformPanel.jsx:130` and `PlatformIdentitiesPage.jsx:232`. `git diff HEAD` adds no other `eslint-disable` line and no `i18next` suppression.
- The 11.15 label-prop search finds nothing (exit 1).
- `variant="contained"`: 1 in each tenant screen and in PlatformPanel, and 2 in PlatformIdentitiesPage, as before.
- The `toProblem(error)` counts pass through the contract (21/21).
- `git status --short` over `ExternalProofResume.test.jsx`, `IdentityAccessReviewRevalidation.test.jsx`, `tests/Web.AcceptanceTests`, `AppRoutes.jsx`, `theme.jsx` and `src/i18n/locales` lists only C2's four catalogs.

### Work Unit Evidence (C3)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/features/identity/roles/RolesPage.test.jsx src/features/identity/members/MembersPage.test.jsx src/features/identity/invitations/InviteMemberPage.test.jsx src/features/platform/PlatformPanel.test.jsx src/features/platform/identities/PlatformIdentitiesPage.test.jsx src/test/identityFiles.contract.test.js` → exit 0, Test Files 6 passed (6), Tests 143 passed (143) (`c3-11A.7-green.log`). **RED runs:** 11A.1 exit 1, 1 failed / 19 passed; 11A.2 exit 1, 1 failed / 38 passed; 11A.3 exit 1, 2 failed / 19 passed; 11A.6 step A exit 1, 3 failed / 57 passed. **GREEN runs:** 11A.6 exit 1, 1 failed / 142 passed, before and after the deferral (only 11A.3); 11A.7 143/143. `npx eslint` over the five screens and the two test files → exit 0. |
| Runtime harness command/scenario and exact result | N/A for this unit, as recorded. tasks.md Suggested Work Units gives batch C the journeys command at 13.5, and Phase 13 owns it; this unit was told not to run the journeys. Regression evidence instead: the `SPA-G` steps above, with Vitest failing only in the two batch B files 11A.8 and 11A.10 own, eslint 0, `i18n:unused` 0 and `vite build` 0. |
| Rollback boundary | Revert C3's hunks in the five screens: the `pastTheEnd` expression, its effect and its render conditions; `reloadPage` (`reload` in PlatformPanel's `useDirectoryPages`) and its call sites; and `useEffect` in PlatformIdentitiesPage's React import. Revert C3's helpers and tests in rows 1 and 10. Uncheck 11A.1–11A.7. Drop this section, the C3 row of "Cumulative task state", "11A.8 onward" and C3's words in the merge line. Delete Engram `sdd/offset-pagination-standard/apply-progress-part-5`, and remove its name from the other parts' prefaces. C1, C2, B1's helper and B2's clients stay. There is no data or schema change. At delivery the unit couples to batches A and B (E7). |

### Files changed (C3)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/features/identity/roles/RolesPage.jsx` | Modified (11A.6, 11A.7). `pastTheEnd`, and its effect, deferred to a microtask and cancelled by a newer answer. `listedRoles` is null for a past-the-end page, so no rows, empty state or control are drawn. `reloadPage` reads the loaded page (or `DEFAULT_PAGE`), records it as requested, and is what `run` calls beside the catalogue refresh. About +22/−2 on C2's tree; +90/−46 against `HEAD`, including C1's and A1's lines |
| `src/Web/ClientApp/src/features/identity/members/MembersPage.jsx` | Modified (11A.6, 11A.7): the same `pastTheEnd`, effect, render gate and `reloadPage`, called by `run` beside the role-catalogue refresh. About +23/−2; +61/−34 against `HEAD` |
| `src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.jsx` | Modified (11A.6, 11A.7): the same for the invitations read; `run` calls `reloadPage`. About +24/−2; +57/−39 against `HEAD` |
| `src/Web/ClientApp/src/features/platform/PlatformPanel.jsx` | Modified (11A.6, 11A.7). `useDirectoryPages` gains `pastTheEnd`, its effect and `reload`, and returns both. `DirectoryPagination` and the three directory chains draw nothing for a past-the-end page. `run` reloads each directory's loaded page. About +26/−4; +71/−16 against `HEAD` |
| `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.jsx` | Modified (11A.6, 11A.7). `useEffect` is imported. `pastTheEnd` and its effect are added, and the empty block is not drawn for a past-the-end page. `reloadPage` is a `useCallback`, used by `onProved` and by both refreshes in `run`. About +28/−6; +59/−31 against `HEAD` |
| `src/Web/ClientApp/src/features/identity/roles/RolesPage.test.jsx` | Modified (row 1; 11A.1, 11A.3): two helpers and six tests, 21 in all. About +152; +281/−15 against `HEAD` |
| `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.test.jsx` | Modified (row 10; 11A.2): two helpers and five tests, 39 in all. About +114; +226/−41 against `HEAD` |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 11A.1–11A.7 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: the C3 row, "Later units" from 11A.8, and C3 in the merge line; C3 appended; A1–C2 kept verbatim |

Test-file boundary: `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 80 entries (`c3-git-status.txt`), the same as C2, because rows 1 and 10 were already modified. No other test file was created, edited, moved or deleted. `git diff --cached --name-only` is empty. The five screens stay CRLF (edited in place). `RolesPage.test.jsx` stays LF, and `PlatformIdentitiesPage.test.jsx` stays CRLF.

### Deviations from design (C3)

1. **11A.4 and 11A.5 needed no production edit, and most of their proof passed at RED.** C1 (11.2, 11.4, 11.6, 11.8) and C2 (11.10) had already built the loading, errored, refused and page-change states, as C2 Deviations 3 anticipated. So of the five tests in each of 11A.1 and 11A.2, only the past-the-end test failed at RED, which is the failure tasks.md expects ("at least the past-the-end test"). The other eight were checked against each state and are recorded as passing at RED, not as new GREENs.
2. **A past-the-end page draws nothing.** This goes beyond plan:1204-1213. Each screen computes `pastTheEnd` (empty `items`, `totalPages > 0`, `pageNumber > totalPages`) and draws no rows, empty state or `TablePagination` for that answer.
   - Without it, the screen called a collection of 30 empty: "No roles have been made for this organization yet" until the correction arrived (RolesPage), or "No accounts are listed here." for one commit (PlatformIdentitiesPage). MUI would also have rendered an out-of-range page. Step A's run proves the sentence was drawn.
   - This answers C2 Issue 2, adds no text, and keeps the spec's rule that the empty state belongs to `totalCount` 0. The effect reads the same expression, so what is drawn and what is corrected agree.
   - During the correction RolesPage draws nothing in the collection's place, because its skeleton rule is "loading with no data". PlatformIdentitiesPage's existing skeleton ("loading with no rows") shows instead.
3. **The correction is deferred to a microtask.** plan:1204-1213 calls `go` inside the effect. The repository's `eslint-plugin-react-hooks` refuses that (`react-hooks/set-state-in-effect`, 5 errors), and the only suppression allowed is `react-hooks/exhaustive-deps`. So the effect follows the precedent of `useRead.js:76` and the resume effects in `RolesPage.jsx` and `MembersPage.jsx`: `Promise.resolve().then(...)` with a `cancelled` flag set by the cleanup, which a newer answer or leaving the screen trips. It still runs once per answer, and only for a loaded past-the-end page.
4. **11A.3 cannot assert a `role="status"` confirmation, because RolesPage has none.** tasks.md 11A.3 says "the confirmation (`role="status"`) and the read alert both show". RolesPage renders no confirmation after a retirement; its only `role="status"` is the loading wait. Adding one would be new copy, which L4 and "No key added" forbid. The test asserts instead:
   - the accepted change: one retirement sent, and the password field cleared, as after every accepted change;
   - the read problem with its reference, as the only alert, and none in the role's row;
   - page 2 requested again.

   The spec scenario's "the retirement confirmation stays" has nothing to keep on this screen. The paged screens with a `role="status"` confirmation are InviteMemberPage (a sent invitation) and PlatformPanel (an invited administrator).
5. **The tests coordinate deterministically where the plan slept.**
   - The outrun tests hold page 2 until "1–50 of 100" shows and then release it, where plan:1155 used a 50 ms delay and an 80 ms wait. A `setTimeout(0)` flush follows the release.
   - The two not-corrected tests wait 50 ms before asserting `[1]`, because the absence of a request has no event to wait for. The past-the-end tests show that a correction request arrives sooner.
   - The tests use `numberedRoles` and `numberedIdentities` ("Role 1"…) instead of the plan's `thirty` ("Role 0"…).
   - The refused tests also assert that "Go to next page" is gone (spec table: "no control").
6. **The refreshed page becomes the requested page.**
   - `reloadPage` (`reload` in PlatformPanel) calls `setRequested` with the page it reads, so a "Try again" after a failed refresh repeats that page (E8), not an older page change.
   - PlatformIdentitiesPage's `reloadPage` is a `useCallback`, because `onProved` lists it as a dependency and no suppression may be added.
   - Its concurrency-conflict refresh and `onProved` also read the loaded page (design "How screens use it": a mutation, proof or concurrency refresh).
   - The catalogue refreshes are not pages and stay as they were: `catalogRead.refresh()`, `catalog.refresh(undefined)` and `rolesRead.refresh(undefined)`.
7. **No shared page-state hook.** Four screens now repeat `requested`, `go`, `retry`, `pastTheEnd` and `reloadPage`; PlatformPanel holds them once, in `useDirectoryPages`. A shared hook would be a new module that design.md File Changes does not list, so the state stays inline, as plan:990-997 and C1 chose. It is recorded as a follow-up candidate (Issues 4).

### Issues found (C3)

1. No blocker.
2. **tasks.md 11A.3 names a confirmation RolesPage does not have** (Deviations 4). The spec scenario "A failed refresh after a retirement" also says "the retirement confirmation stays". Nothing was added. The orchestrator may want the task and spec wording amended at verify.
3. **Still red, owned by later tasks,** with B2's counts: ExternalProofResume 9 (11A.8) and IdentityAccessReviewRevalidation 9 (11A.10). The resume walks still follow `nextCursor` until 11A.9; the operations they resume go through `run`, which now reads the loaded page again.
4. **Follow-up candidate:** one shared page-state hook for the five screens (Deviations 7). It is out of this change's scope.
5. **Engram split.** Part 4 already held 41,332 characters of content, so C3 goes into a new part, `sdd/offset-pagination-standard/apply-progress-part-5`, and the prefaces of parts 1–4 now name it.
6. **Hooks.** The Vercel plugin asked for `Skill(vercel-functions)`, `Skill(react-best-practices)` and `Skill(verification)`. None was invoked: the instructions forbid the Skill tool, and none applies to this repository.
7. **Parallel work in the tree** is unchanged from A3 Issue 7, and HEAD is still `f29ca74`. C3 wrote none of it.

### Remaining tasks

- Batch C: 11A.8–11A.12.
- Batch D: 12.1–12.9.
- Then Phase 13 and Delivery.

### Workload / PR boundary (C3)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13; `chain_strategy` does not apply). No commit was made.
- Current work unit: C3 (tasks 11A.1–11A.7).
- **Boundary.** It starts from C2's working tree, where every Phase 11 test file was green but a page past the end was called empty and never corrected, and a change refreshed page 1 whatever page it was made from. It ends with the read states proven on RolesPage and PlatformIdentitiesPage, the past-the-end correction on all five screens, and every post-change refresh reading the loaded page. The resume walks (11A.8, 11A.9), the R6 rewrite (11A.10), the batch C verification (11A.11) and 11A.12 remain.
- **Review impact.** About +123/−16 in production over the five screens, and about +266 in tests over two files. These figures are `git diff --numstat HEAD` less C1's and C2's recorded figures, so they are approximate.

### Correction round 1 — validator finding C3-V01 (2026-09-14)

Scope: finding C3-V01 only. No source, test, catalog, spec, design or plan file changed. This round changed two things: the 11A.3 checkbox in tasks.md, now unmarked, and this file. It also re-synced Engram `sdd/offset-pagination-standard/tasks-part-2` and apply-progress parts 1 and 5. No git write was made, nothing is staged, and HEAD is still `f29ca74`. Logs are `<scratchpad>/c3-corr1-*`.

| Finding | Disposition | Change made |
| --- | --- | --- |
| C3-V01 (should-fix, 11A.3) | **Not applied: ORCHESTRATOR DECISION NEEDED.** The fix is addressed to the orchestrator and offers two options. Neither is the executor's to choose. (a) Amend the 11A.3 bullet and the spec requirement and scenario, so that a screen with no confirmation proves E11 with one accepted change plus the read alert. (b) Move the confirmation assertion to a screen that has one. That needs a new test in declared row 3 (`InviteMemberPage.test.jsx`, planned for 11.5 only) or row 9 (`PlatformPanel.test.jsx`, planned for 11.7 only), which those rows do not allow. | tasks.md: 11A.3 is unmarked `[ ]`. Its bullet "The confirmation (`role="status"`) and the read alert both show" has no evidence and can get none on RolesPage. No wording changed. The test, the screen and 11A.7 are untouched. |

**Evidence**

- **RolesPage has no confirmation.** Its only `role="status"` is the loading wait (`RolesPage.jsx:440-444`, shown while `awaitingReads && listProblem === null`, with `roles.loading` and three skeletons). After an accepted change, `run` (`:187-207`) clears the password and the draft and reloads the page and the catalogue; it sets no confirmation. Adding one would be new copy (L4; "No pagination key is added").
- **What the 11A.3 test proves.** The test (`RolesPage.test.jsx:451-478`) asserts everything else the task and the scenario ask for:
  - exactly one retirement;
  - the password field cleared;
  - the read alert "Something went wrong. Try again." with "Reference: trace-retired", as the only alert and none in the role's row;
  - requests `[1, 2, 2]`.

  It has no `role="status"` assertion. Its RED stands as recorded (`c3-11A.3-red.log`: `Unable to find role="alert"`, because `run` still called `refresh(undefined)`).
- **Re-run.** `cd src/Web/ClientApp && npx vitest run src/features/identity/roles/RolesPage.test.jsx` gave exit 0: Test Files 1 passed (1), Tests 21 passed (21), 16.42 s, started 00:16:20 (`c3-corr1-roles-focused.log`). The 11A.3 test took 1 638 ms.
- **For option (b): no paged screen confirms an E11 change.** None of the five screens shows a `role="status"` confirmation after a retire, revoke, reissue or status change. The only success confirmations follow a sent invitation:
  - `InviteMemberPage.jsx:244-248`: `sent`, set only by `invite` (`:179-188`) after `run` has awaited `reloadPage()` (`:169`);
  - `PlatformPanel.jsx:517-521`: `inviteOutcome === 'sent'`, set by `onSucceeded` (`:505-508`) before `run` reloads the three directories (`:215`).

  The other `role="status"` regions are loading waits (`MembersPage.jsx:341`, `PlatformIdentitiesPage.jsx:459-460`). `useRead` turns every read failure into `errored` or `refused` and never rethrows (`useRead.js:54-62`; exploration.md V20). So both confirmations already survive a failed reload in the code. A test there would pass on its first run, which makes it a guard, not a RED. It would also prove E11 for a sent invitation, which the spec's list of changes does not name.
- **For option (a): where the words come from.** The requirement's "The mutation's `role="status"` confirmation MUST stay" and the scenario's "the retirement confirmation stays" (`specs/spa-pagination-ui/spec.md:159-167`) follow plan:1219. On the five screens, that clause covers none of the changes the requirement lists.

#### TDD Cycle Evidence (C3 correction round 1)

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 11A.3 | `src/features/identity/roles/RolesPage.test.jsx` (row 1) | Integration (Vitest, Testing Library, MSW with the real transport) | ✅ Unchanged from C3 | ✅ Unchanged (`c3-11A.3-red.log`) | ✅ Re-run exit 0, 21/21 (`c3-corr1-roles-focused.log`) | ➖ Single, unchanged | ➖ None: no code or test changed. **Unmarked `[ ]`**: the confirmation bullet has no evidence until the C3-V01 decision |

#### Work Unit Evidence (C3 correction round 1)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/features/identity/roles/RolesPage.test.jsx` → exit 0, Test Files 1 passed (1), Tests 21 passed (21) (`c3-corr1-roles-focused.log`). |
| Runtime harness command/scenario and exact result | N/A: no code, test or runtime path changed. The journeys belong to 13.5 and were not run. |
| Rollback boundary | Re-mark 11A.3 `[x]` in tasks.md. Restore the C3 row, drop the 11A.3 row and restore the merge line in "Cumulative task state". Drop this section. Re-sync Engram `tasks-part-2`, `apply-progress` and `apply-progress-part-5` from the restored files. No code, test or data change. |

#### Issues found (C3 correction round 1)

1. **ORCHESTRATOR DECISION NEEDED (C3-V01).** Choose (a) or (b) before sdd-verify.
   - With (a), the amended 11A.3 can be re-marked `[x]` on its existing evidence, with no new test.
   - With (b), row 3 or row 9 and the spec must be amended first. The new test then passes on its first run and must be recorded as a guard, not a RED.
2. **11A.7 stays `[x]`.** Its bullets name no confirmation, and its proof, the 11A.3 test, still passes. The spec coverage row "Mutation outcome survives a failed refresh" (11A.3, with 11A.7 relying on it) is unchanged.
3. **Engram part.** The launch prompt names part 4 for batch C. Part 4 already holds C1 and C2, and C3 is in part 5 (C3 Issues 5), so this round goes into part 5, which stays under 50,000 characters. Part 1's cumulative table is re-synced.
4. **Task count.** tasks.md now shows 105 of 134 checked. The launch prompt's 84 of 134 predates C1–C3.
5. **Parallel work in the tree** is unchanged from A3 Issue 7, and HEAD is still `f29ca74`. This round wrote none of it.

#### Remaining tasks (C3 correction round 1)

- 11A.3 (awaits the C3-V01 decision).
- Batch C: 11A.8–11A.12. Batch D: 12.1–12.9. Then Phase 13 and Delivery.

### Decision C3-V01 (a) — 11A.3 closure (2026-09-14)

Scope: task 11A.3 only. On 2026-09-14 the user chose option (a) of finding C3-V01: amend the task and the spec, with no new copy and no new test. Only `tasks.md`, `specs/spa-pagination-ui/spec.md` and this file changed. No source or test file changed, and no git write was made. Logs are `<scratchpad>/c3-decision-a-*`.

- **Why.** E11 (plan:75) says only that a failed refresh after a successful mutation shows as a read failure, and that the mutation is never reported as failed. The `role="status"` clause came from plan:1219, which assumed a confirmation no paged screen has (C3 correction round 1, Evidence).
- **tasks.md.** The 11A.3 confirmation bullet now says RolesPage shows no confirmation after a retirement. E11 is therefore proven by one accepted retirement (one retirement request, the password field cleared), the read alert with its reference as the only alert, and no message saying the retirement failed. The bullet cites the decision, plan:75 and plan:1219. The `pageNumber=2` bullet stays, and 11A.3 is `[x]` again. 13.8 gains a bullet on the amended clause, and the carry-over index lists C3-V01 (a).
- **spec.md.** "Mutation outcome survives a failed refresh" is marked "(Amended 2026-09-14, decision C3-V01)". A `role="status"` confirmation MUST stay where the screen shows one. Where it shows none, the accepted mutation's effect MUST stand, and no message MUST report it as failed. The scenario's THEN/AND lines are now: the read alert appears as the only alert; no message says the retirement failed; the retirement is not repeated.
- **Test check.** `RolesPage.test.jsx:451-478` asserts exactly what the amended bullet says:
  - `retirements` is 1: one request, not repeated;
  - the password field is empty;
  - the alert "Something went wrong. Try again." with "Reference: trace-retired" is the only alert, and none is in the role's row;
  - the requests are `[1, 2, 2]`.

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 11A.3 | `src/features/identity/roles/RolesPage.test.jsx` (row 1) | Integration (Vitest, Testing Library, MSW) | ✅ C3 (`c3-safety.log`) | ✅ C3, unchanged: exit 1, `Unable to find role="alert"` (`c3-11A.3-red.log`) | ✅ C3 at 11A.7, 143/143 (`c3-11A.7-green.log`); 21/21 at correction round 1 (`c3-corr1-roles-focused.log`) and now (`c3-decision-a-roles-focused.log`) | ➖ Single, unchanged | ➖ N/A: only wording changed, so there is no code or test to refactor |

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/features/identity/roles/RolesPage.test.jsx` → exit 0, Test Files 1 passed (1), Tests 21 passed (21) (`c3-decision-a-roles-focused.log`). With `--reporter=verbose` → exit 0, 21/21, and the 11A.3 test passed in 1 595 ms (`c3-decision-a-roles-verbose.log`). |
| Runtime harness command/scenario and exact result | N/A: no code, test or runtime path changed. The journeys belong to 13.5. |
| Rollback boundary | Restore the old 11A.3 bullet and unmark it. Drop the 13.8 bullet and the carry-over line. Restore the spec requirement and scenario. Drop this section, and restore the 11A.3 row and the closure line in "Cumulative task state". Re-sync Engram `tasks-part-2`, `spec-spa-pagination-ui`, `apply-progress` and `apply-progress-part-5`. |

Issues: none blocking. tasks.md shows 111 of 134 checked. 11A.7 and the spec coverage row are unchanged, and no other change artifact mentions the confirmation.

## Work unit C4 — Phase 11A, tasks 11A.8–11A.12 (resume walks, resume and R6 test rewrites, batch C verification)

Scope: tasks 11A.8–11A.12, and nothing else. Four SPA files changed: the screens `RolesPage.jsx` and `MembersPage.jsx` (the resume walks only), and declared test rows 4 and 5 (`ExternalProofResume.test.jsx` and `IdentityAccessReviewRevalidation.test.jsx`). This change's `tasks.md` (checkboxes 11A.8–11A.12) and `apply-progress.md` were also updated. No catalog key, copy, `id`, role, heading or `disabled` expression changed. `AppRoutes.jsx`, page objects, `features/*/api/`, the theme, CSS and the locales are untouched, and no journey was run. `testTimeout` stays at 15000 ms, and no per-test or per-query timeout was added. 11A.3 stays `[ ]` (C3-V01). No git write was made, nothing is staged, and HEAD is still `f29ca74`. Logs are `<scratchpad>/c4-*`.

### Order of work

1. **Safety net** (`c4-safety.log`, 00:38:50), before any edit, over rows 4 and 5, the two screens' own test files and `src/test/identityFiles.contract.test.js`: exit 1, Test Files 2 failed / 3 passed (5), Tests 18 failed / 61 passed (79). The failures are exactly B2's recorded red: `ExternalProofResume` 9 of 9 and `IdentityAccessReviewRevalidation` 9 of 11 (R6A 5, R6B 1, R6C 3), all because the strict reader refuses their cursor fixtures. RolesPage 21, MembersPage 17 and the contract 21 pass.
2. **11A.8 RED** against the unedited walks.
3. **11A.9 GREEN** in both screens, then a triangulating assertion.
4. **11A.10** rewrite of R6C and the shared `pageResponse` helper.
5. **11A.11** gate; then a wording change to two assertion messages (Deviations 7) and the gate again on the final bytes.
6. **11A.12**: no commit (Delivery).

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 11A.8 | `src/features/identity/ExternalProofResume.test.jsx` (row 4) | Integration (Vitest, Testing Library, MSW with the real transport, the whole `App` under a router) | ⚠️ 0/9: red since 10.2 (B2), recorded above | ✅ Written first. A `pageOf` helper; `owner()` answers offset pages. `paginatedTarget` serves 26 rows (25 others, then the target alone on page 2), records `{ pageNumber, pageSize, returned }` and every query-parameter name, and takes `afterReturn` = eligible, missing, ineligible or unreachable (a network error on page 2 after return). `beginPaginatedOperation` clicks "Go to next page" and expects the reads `[1, 2]` at 25. The resume test waits for the first row's action to be enabled (the busy-gated stand-in for "Show more"), expects "Go to next page" enabled, the page-2 read after return, and exactly the parameters `pageNumber` and `pageSize`. Every resumed-or-refused assertion stays. Added: an `it.each` over members and roles whose page-2 search fails: the alert starts "We could not reach the service.", page 2 was asked for, and nothing is written. Ran exit 1, 7 failed / 4 passed (11) (`c4-11A.8-red.log`, 00:43:02): both resumes (`expected [] to deeply equal [ … ]`); both not-resumed cases and the late lookup (`… to deep equally contain { pageNumber: 2, pageSize: 25, … }`); both failed walks (`Unable to find role="alert"`). The four tests without a walk pass | ✅ at 11A.9 | ✅ members and roles × found, missing, ineligible, unreachable, and left mid-walk | ✅ Assertion-message wording (Deviations 7), gate rerun |
| 11A.9 | `RolesPage.jsx`, `MembersPage.jsx` | same | ✅ RolesPage 21/21, MembersPage 17/17, contract 21/21 (inside `c4-safety.log`) | ✅ 11A.8 | ✅ exit 0, Test Files 4 passed (4), Tests 70 passed (70) (`c4-11A.9-green.log`, 00:45:05): ExternalProofResume 11/11 (the resumes 2 477 and 3 454 ms), RolesPage 21, MembersPage 17, contract 21. `npx eslint` over both screens and row 4: exit 0 (`c4-11A.9-eslint.log`). Neither screen contains `cursor` any more; both stay CRLF | ✅ Added to the not-resumed test: the returned screen's reads are exactly `[1, 2]` at 25, so the loaded page is not read again and nothing past `totalPages` is asked for. Alone: exit 0, 11/11 (`c4-11A.9-triangulate.log`, 00:47:52). It passed on its first run, because the walk was already general, so it is recorded as triangulation, not as a RED | ➖ None: the two walks stay inline, one per screen (two occurrences; design lists no shared module) |
| 11A.10 | `src/features/identity/IdentityAccessReviewRevalidation.test.jsx` (row 5) | same | ⚠️ 2/11: R6A 5, R6B 1 and R6C 3 red since 10.2 (B2) | ➖ No new RED. The file's recorded red is the RED for R6A and R6B, which change only through `pageResponse` (now a full offset page). R6C migrates a continuation test to screens whose `TablePagination` C1 and C2 already built, so it passes on its first run | ✅ Alone: exit 0, 11/11 (`c4-11A.10.log`, 00:47:33); R6C 820–1 124 ms. `npx eslint` over rows 4 and 5: exit 0 (`c4-11A.10-eslint.log`) | ✅ three directories: roles, members, invitations | ➖ None needed |
| 11A.11 | `SPA-F` (nine files) and `SPA-G` | Gate | ✅ the 1.1 record | N/A: a gate writes no test | ✅ See "11A.11 gate" below: every step exit 0, twice | N/A | N/A |
| 11A.12 | — | Adjusted | — | — | ➖ No commit (DL.0: one commit after Phase 13) | — | — |

### Test Summary (C4)

- **Tests written:** ExternalProofResume goes from 9 to 11 tests (the failed walk, for members and roles); its nine tests keep their names and their resumed-or-refused assertions. IdentityAccessReviewRevalidation keeps 11 tests; R6C is rewritten and renamed (Deviations 4).
- **Tests passing:** 632/632 in the full Vitest run (C3's 630, plus the two failed-walk tests).
- **Layers used:** Integration (Vitest with Testing Library and MSW over the real transport).
- **Approval tests:** the four walk-free resume tests and R6A/R6B, unchanged in their assertions.
- **Pure functions created:** none.

### 11A.11 gate

Run from `src/Web/ClientApp`, one step after another; exits and timestamps are in `c4-11A.11-exits.txt` and `c4-11A.11-final-exits.txt`. The first run (00:48:49–00:50:43) and the final-bytes run after Deviations 7 (00:53:56–00:55:52) gave the same results:

| Step | Command | Exit | Result | Final log |
| --- | --- | --- | --- | --- |
| Focused | `SPA-F` on RolesPage, MembersPage, InviteMemberPage, PlatformPanel, PlatformIdentitiesPage, ExternalProofResume, IdentityAccessReviewRevalidation, useRead and spanish | 0 | Test Files 9 passed (9); Tests 166 passed (166) | `c4-11A.11-final-spa-f.log` |
| Vitest | `npx vitest run` | 0 | Test Files 46 passed (46); Tests 632 passed (632); 66.54 s | `c4-11A.11-final-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output | `c4-11A.11-final-eslint.log` |
| Unused keys | `npm run i18n:unused` | 0 | "No unused keys found" | `c4-11A.11-final-i18n.log` |
| Build | `npx vite build` | 0 | 2,639 modules transformed; only the known chunk-size notice | `c4-11A.11-final-vite.log` |

- **Baseline-timeout rule: 0 uses. A1-01 exception: 0 uses.** No test failed. The rewritten record entries 1–4 passed outright under full load: the roles and members resumes in 6 419 and 3 532 ms, and R6C roles and members in 1 342 and 2 179 ms (invitations 1 524 ms). Entries 5–9 passed too, for example #6 in 2 714 ms.
- **Contract:** `identityFiles.contract.test.js` passed 21/21 inside the run, so the pinned catches stay: RolesPage 2, MembersPage 2, InviteMemberPage 1, total 12 (`:8-18`, `:127`). The walks' `catch` is unchanged.
- **`rg -n "eslint-disable" src/Web/ClientApp/src`:** 14 lines (`c4-11A.11-eslint-disable.txt`). Nine are the 1.1 record, at new line numbers (`MembersPage.jsx:252`, `RolesPage.jsx:273` and `:352`). Five are C3's `// eslint-disable-next-line react-hooks/exhaustive-deps` on the 11A.6 effects. `git diff HEAD` adds exactly those five `eslint-disable` lines. C4 added none, no `i18next` suppression was added, and the moved `ProblemMessage.test.jsx` keeps its own.

### Work Unit Evidence (C4)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/features/identity/ExternalProofResume.test.jsx src/features/identity/roles/RolesPage.test.jsx src/features/identity/members/MembersPage.test.jsx src/test/identityFiles.contract.test.js` → exit 0, Test Files 4 passed (4), Tests 70 passed (70) (`c4-11A.9-green.log`). `npx vitest run src/features/identity/IdentityAccessReviewRevalidation.test.jsx` → exit 0, 11/11 (`c4-11A.10.log`). RED: `npx vitest run src/features/identity/ExternalProofResume.test.jsx` → exit 1, 7 failed / 4 passed (`c4-11A.8-red.log`). Final `SPA-F` over the nine 11A.11 files → exit 0, 166/166. |
| Runtime harness command/scenario and exact result | N/A for this unit. tasks.md Suggested Work Units gives batch C the journeys command at 13.5, and Phase 13 owns it; this unit was told not to run the journeys. Regression evidence: `SPA-G` on the final bytes, every step exit 0 (Vitest 632/632, eslint, `i18n:unused`, `vite build`). |
| Rollback boundary | Revert C4's hunks in `RolesPage.jsx` and `MembersPage.jsx`: the three loaded-page constants, the bounded walk and the effect dependency list (restoring `nextCursor`). Revert rows 4 and 5 to C3's tree (both were unchanged from `HEAD` before this unit, so `git diff HEAD` on them is C4's whole change). Uncheck 11A.8–11A.12. Drop this section, the C4 row and "12.1 onward" in "Cumulative task state", and C4's words in the merge line. Drop C4 from Engram `apply-progress-part-5` and part 1's table and preface, and re-sync `tasks-part-2`. C1–C3, B1's helper and B2's clients stay. No data or schema change; at delivery the unit couples to batches A and B (E7). |

### Files changed (C4)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/features/identity/roles/RolesPage.jsx` | Modified (11A.9): `loadedPageNumber`, `loadedTotalPages` and `loadedPageSize` replace `nextCursor`; the walk asks for pages 1 to `loadedTotalPages`, skipping the loaded page, at its size, stopping when the role is found; `cancelled` is still checked after each request, before `forget()` and in the `catch`; dependencies `[waiting, roles, loadedPageNumber, loadedTotalPages, proof.isReady, tenantId]`. About +14/−9 on C3's tree; +104/−55 against `HEAD` |
| `src/Web/ClientApp/src/features/identity/members/MembersPage.jsx` | Modified (11A.9): the same walk over `listMembers`. About +14/−9; +75/−43 against `HEAD` |
| `src/Web/ClientApp/src/features/identity/ExternalProofResume.test.jsx` | Modified (row 4; 11A.8, 11A.9 triangulation): +71/−26 |
| `src/Web/ClientApp/src/features/identity/IdentityAccessReviewRevalidation.test.jsx` | Modified (row 5; 11A.10): +42/−31. Hunks only at the constants, the `pageOf`/`pageResponse` helper and R6C; R6A and R6B are untouched |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 11A.8–11A.12 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: the C4 row, "Later units" from 12.1, and C4 in the merge line; C4 appended; A1–C3 kept verbatim |

Test-file boundary: `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 82 entries (`c4-git-status.txt`): C3's 80 plus rows 4 and 5. No other test file was created, edited, moved or deleted. `git diff --cached --name-only` is empty. All four files are CRLF: the two test files were written whole and converted back to CRLF, and the screens were edited in place.

### Deviations from design (C4)

1. **The busy check moved to a row action.** The old test waited for "Show more {kind}" to be enabled, which was disabled while a change ran. `TablePagination`'s buttons are not busy-gated, so the test waits for the first row's action ("Edit roles of Member 0", "Edit Role 0"; both `disabled={isBusy}`), then checks "Go to next page" is enabled.
2. **The failed walk is tested on both screens.** tasks.md asks for "a failed page-2 walk"; both walks changed, so it is an `it.each` over members and roles.
3. **The missing target is a page past the end.** After return, page 1 still reports 26 rows, so the walk searches page 2, which answers `[]` with `totalCount` 25 (the target left between the two reads). This mirrors the old fixture, whose page 1 always carried a next cursor.
4. **R6C's continuation is named exactly.** The automatic-loading branch and the generic `/next|more|continue…/` lookup are gone: offset screens never load page 2 by themselves, so R6C requires an executable "Go to next page" (the MUI name), with the same failure message shape. Added: the 26th row is not on page 1, the parameter names are exactly `pageNumber` and `pageSize`, and an unexpected-page guard replaces the unexpected-cursor guard. The describe and test titles now say 25 and 26; record entries 3 and 4 leave the record under their old names.
5. **`loadedPageSize` is not a dependency.** The list follows design flow (c) (`L.pageNumber`, `L.totalPages`). Every new page answer also changes `roles` or `members`, which is listed, so the size cannot go stale; the existing `exhaustive-deps` suppression covers it, and no suppression was added.
6. **The plan's Step 4 wording is superseded.** plan:1220 says a failed walk "abandons the resume silently" and plan:1233 says "change only the fixtures". D11, D12 and tasks.md win: the failure still renders through `setActionProblem`, and the recorded reads and continuation clicks moved to offset equivalents. As before, a failed walk does not call `forget()`; the tests do not assert that either way.
7. **Two assertion messages were reworded after the first gate.** They said pages are asked for "never by cursor or limit", which batch D's 12.1 search (`rg -i cursor src tests`) would flag outside its reviewed exclusions. They now say "by page number and page size alone". Only message text changed; the full gate was rerun on the final bytes.

### Issues found (C4)

1. No blocker.
2. **11A.3 stays `[ ]`**, awaiting the C3-V01 decision; C4 did not touch it.
3. **Engram part.** The launch prompt names `apply-progress-part-4` for batch C, but part 4 already holds C1 and C2 and C3 is in part 5. C4 is appended to part 5, which stays under 50,000 characters. Part 1's table, merge line and preface, and `tasks-part-2`, are re-synced; `tasks` (part 1 of the tasks) is unchanged because nothing before Phase 9 changed. Parts 2–4 are not rewritten: C4 adds no part and changes none of their content.
4. **The 1.1 record in tasks.md still lists entries 1–4.** tasks.md already says they leave the record after 11A.8 and 11A.10; no task assigns editing the list. 13.2 applies the rule only to entries 5–9. `openspec/config.yaml:70` is also out of date (A1 Issue 7).
5. **Task count:** 110 of 134 checked.
6. **Hooks.** The Vercel plugin asked for `Skill(verification)`. It was not invoked: the Skill tool is forbidden here, and it does not apply to this repository.
7. **Parallel work in the tree** is unchanged from A3 Issue 7, and HEAD is still `f29ca74`. C4 wrote none of it. No stray `--help` file exists.

### Remaining tasks

- 11A.3 (awaits the C3-V01 decision).
- Batch D: 12.1–12.9. Then Phase 13 and Delivery.

### Workload / PR boundary (C4)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13). No commit was made.
- Current work unit: C4 (tasks 11A.8–11A.12).
- **Boundary.** It starts from C3's tree, where both resume walks still followed `nextCursor` (absent from offset pages, so a target past page 1 was never found) and rows 4 and 5 were red on cursor fixtures. It ends with bounded page walks, both files green and fast, and batch C verified by `SPA-G`.
- **Review impact.** About +28/−18 in production over two screens, and +113/−57 in tests over two files.

## Work unit D1 — Phase 12, tasks 12.1–12.9 (search cleanup, D20 standards edits, batch D gate)

Scope: tasks 12.1–12.9, and nothing else. Three standards documents changed, the ones D20 names: `.agents/skills/error-handling-standards/SKILL.md`, `.agents/skills/error-handling-standards/references/error-handling-rules.md` and `.agents/skills/frontend-design-standards/references/ui-composition-rules.md`. This change's `tasks.md` (checkboxes 12.2–12.9) and `apply-progress.md` were also updated. No source, test, catalog or configuration file changed, so no SPA byte changed. 12.1 stays `[ ]` (Issues 1). `git status --porcelain` on the three documents was empty at 08:29:58 and again at 08:32:36, immediately before the edits. No other `.agents/skills` path, `AGENTS.md`, `CLAUDE.md`, `.codex/**` or the module-separation plan was touched. No journey was run, and `testTimeout` stays at 15000 ms. No git write was made, nothing is staged, and HEAD is still `f29ca74`. Logs are `<scratchpad>/d1-*`.

### Order of work

1. **12.1** searches (08:29:52). Each match was then classified against the reviewed exclusions, reading the code around it.
2. **12.2 RED**: the three design searches and the `ui-composition-rules.md` cursor search (08:29:58).
3. **Pre-edit check and the 12.4 mirror re-glob** (08:32:36).
4. **12.6, 12.7 and 12.8** read-only checks (08:32:46–08:33:21).
5. **12.3 and 12.4** edits (08:35–08:37): seven exact replacements with the Edit tool, changing eight lines.
6. **12.8** locales proof (08:36:24).
7. **12.9** `SPA-G` (08:36:20–08:38:11), in the background while the last three `error-handling-rules.md` replacements were made (Deviations 4).
8. **12.5 GREEN** (08:37:23), after the last replacement.

### 12.1 classification

Search 1: `rg -n -i 'nextCursor|cursor|PlatformDirectoryQuery|PlatformDirectoryPage|OpaqueCursor|BoundedLimit|MaximumLimit|MinimumLimit' src tests --glob '!**/node_modules/**' --glob '!src/Web/wwwroot/openapi/**' --glob '!**/package-lock.json' --glob '!**/*.scss'` → exit 0, 21 lines (`d1-12.1-search1.log`). None names `PlatformDirectoryQuery`, `PlatformDirectoryPage`, `OpaqueCursor`, `BoundedLimit`, `MaximumLimit` or `MinimumLimit`.

| Match | Reviewed exclusion | Disposition |
| --- | --- | --- |
| `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs:338,341,342,343,350,351,354` | `StackBaselineTests.cs:338-354`, where `cursor` is a string index | Excluded |
| `src/Web/ClientApp/src/api/problemDetails.js:14` `FORBIDDEN_SUCCESS_KEYS` | The first exclusion (`nextCursor` stays forbidden). The same line was already at `2716aa6` (`features/identity/api/problemDetails.js:14`) | Excluded |
| `src/Web/ClientApp/src/api/problemDetails.test.js:131` (the "a pagination wrapper" drift case), and `:137`, `:140` and `:141` (the 9.5 refusal's summary, title and assertion) | The first exclusion: the `nextCursor` assertions in that file, including the 9.5 retired-cursor refusal | Excluded |
| `src/Web/ClientApp/src/features/platform/platformClient.test.js:188-189` (title and fixture) | The second exclusion: the 10.3 `{ items: [], nextCursor: null }` drift test | Excluded |
| `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformDirectoryContractTests.cs:70` and `:136` | The third exclusion. `:70` is inside 6.8's `:54-73`, and `:136` is inside the 6.1 test `Every_list_route_declares_offset_parameters_and_its_binding_refusal` (`:125-140`) | Excluded |
| `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:437` | The third exclusion: the 6.2 no-`nextCursor` assertion, in the helper `AssertOffsetPageBody` | Excluded |
| Same file, `:381` and `:384`: the summary ("no retired cursor") and the name `A_list_success_is_its_offset_page_dto_with_no_cursor_or_envelope_member` of the 6.2 test that makes that assertion | The third exclusion, read as the test that holds the assertion | Excluded; the reading is recorded for confirmation (Issues 2) |
| `src/Web/ClientApp/src/api/pagination.js:58` | **None** | **Outside the exclusions. Not edited; 12.1 stays `[ ]`** (Issues 1) |
| `src/Web/ClientApp/src/web-api-client.ts` | The fifth exclusion. The file is gitignored (`.gitignore:43`), so `rg` skips it; `rg --no-ignore -c -i cursor` on it finds nothing as well (exit 1) | Excluded (no match) |

Search 2: `rg -n '\blimit\b' src/Web/Endpoints src/Web/ClientApp/src/features src/Application/IdentityAccess src/Infrastructure/IdentityAccess src/Infrastructure/Platform` → exit 0, 14 lines (`d1-12.1-search2.log`). Every line is a rate limit or an attempt budget, so this search passes:

- `PlatformEndpoints.cs:134` (an exhausted limit with its `Retry-After`);
- `AttemptBudgetCleanup.cs:12`, `PostgreSqlAttemptBudget.cs:12,36,63,67` and `ISharedAttemptBudget.cs:23` (the shared attempt budget);
- `IdentityLifecycleHandlers.cs:203` ("the same rate limit the front door has");
- `RecoverPendingPlatformOwnerInvitation.cs:29` and `RecoverPendingPlatformOwnerInvitationHandler.cs:16,35` (the recovery limiter);
- `PlatformMfaHandlers.cs:207` (the second-factor guessing limit);
- `platformClient.test.js:79` and `PlatformInvitationPages.test.jsx:411` (an exhausted recovery limit, `429`).

### TDD Cycle Evidence

Strict TDD for batch D works as it did for 8A. The RED is a search that matches the retired wording, and the GREEN is the same search finding nothing. Phase 12 declares no test file, so no test was written; the other tasks record their exact command results.

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 12.1 | The two 12.1 searches | Search | N/A (no edit) | N/A: a search task | ⚠️ Search 1 exit 0: 20 of 21 lines inside the reviewed exclusions, `pagination.js:58` outside. Search 2 exit 0: rate limits only. **Not checked** (Issues 1) | N/A | N/A |
| 12.2 | The three design searches, and `rg -n -i cursor` over `ui-composition-rules.md` | Document search | N/A (no edit) | ✅ `d1-12.2-red.log` (08:29:58). **Retired paths** exit 0: `SKILL.md:109` and `error-handling-rules.md:288`, `:328`, `:400`, `:476`; nothing under `src/Web/ClientApp/src`. **`identity/fieldErrors` outside `features/identity`** exit 1: the boundary has held since 1.9. **`features/identity/fieldErrors` in `.agents/skills`** exit 0: `error-handling-rules.md:594`. **Cursor** exit 0: `ui-composition-rules.md:317`. These are exactly the six `.agents/skills` lines tasks.md lists, plus `:317` | ✅ at 12.5 | — | — |
| 12.3 | `error-handling-standards/SKILL.md` and `references/error-handling-rules.md` | Document | N/A: no file under `src` or `tests` reads either document (`rg -l` for `.agents.skills`, both standards' names and both reference names: exit 1, `d1-observations.log`) | ✅ 12.2 | ✅ Six single-line replacements. `SKILL.md:109` and `:288` name `src/Web/ClientApp/src/api/problemCodes.json`. `:328` reads `RepositoryFile("src/Web/ClientApp/src/api/problemCodes.json")`, as `OpenApiContractTests.cs:491` does. `:400` imports `'../api/problemCodes.json'`, as `src/test/identityServer.js:2` does (Deviations 1). `:476` names `src/api/apiTransport.js`, and `:594` names `src/components/problemFields.js`. `git diff --numstat`: +1/−1 and +5/−5. Both files stay CRLF. The follow-up lines `:362`, `:387`, `:388`, `:389` and `:823` are unchanged (`d1-12.3-12.4-diff.log`) | ➖ Single: one path per line | ➖ None needed |
| 12.4 | `frontend-design-standards/references/ui-composition-rules.md`; the mirror re-glob | Document | N/A: as 12.3 | ✅ 12.2 | ✅ `:317-318` now read "Preserve real offset `TablePagination` controls where the route already owns them.", rewrapped (Deviations 2). `:316` still forbids adding `pagination` merely because a reference image contains it. +2/−2, CRLF kept. **Mirrors** (`d1-12.4-mirrors.log`, read-only): `.claude/skills` and `.codex/skills` hold no `*standards*` folder, and no `ui-composition-rules.md` or `error-handling-rules.md`. Their only similar name is the generic `frontend-design` skill (`SKILL.md`, `LICENSE.txt`), which names neither standard nor its references (`rg -l` exit 1). There is no mirror to match | ➖ Single sentence | ➖ None needed |
| 12.5 | The four 12.2 searches | Document search | — | ✅ 12.2 | ✅ `d1-12.5-green.log` (08:37:23): all four exit 1. The prohibited-additions check exits 0 on `:316` (invented `pagination`) and `:317` (offset `TablePagination` controls) | — | — |
| 12.6 | `rg -n "success\|succeeded\|data\|error\|value" src/Web/Endpoints src/Web/Infrastructure tests/Application.FunctionalTests` | Inspection | — | N/A | ✅ exit 0, 757 lines (`d1-12.6-search.log`), inspected through targeted searches (see "12.6 inspection"). No universal envelope exists | — | — |
| 12.7 | Catalogue, words, resources and codes | Snapshot | — | N/A | ✅ `d1-12.7.log`: the catalogue is unchanged, the diffs are empty, and no paging code exists (see "12.7 results") | — | — |
| 12.8 | Key search, `i18n:unused`, `catalog.contract.test.js`, locales diff | Catalog gates | ✅ C2 11.13 | N/A | ✅ Key search exit 1; `i18n:unused` exit 0; catalog 17/17; locales proof exit 0 (see "12.8 results") | ✅ Two proofs of the locales diff: semantic (flattened keys) and textual (line pairs) | — |
| 12.9 | `SPA-G` | Gate | ✅ The 1.1 record, and C4's 11A.11 gate (632/632) | N/A: a gate writes no test | ✅ Every step exit 0 (see "12.9 gate") | N/A | N/A |

### 12.6 inspection

Logs are `d1-12.6-inspect.log` and `d1-12.6-inspect2.log`.

- **Members named like an envelope, assigned in Web** (`rg -i '\b(success|succeeded|data|error|value)\s*[=:]\s*[^=>]'` over `src/Web/Endpoints` and `src/Web/Infrastructure`): 6 lines, none of them a response member. They are the locals `value` (`ApiAuthorizationMiddlewareResultHandler.cs:47`) and `error` (`ProblemDetailsExceptionHandler.cs:71`, `LoginRateLimiting.cs:99`), and the CSP source `data:` (`IdentitySecurityHeaders.cs:23,34,35`).
- **Web records or classes with a `Success`, `Succeeded`, `Data`, `Error` or `Value` member:** none (exit 1).
- **Envelope-named types in `src` and `tests`:** 18 records, all outbox or delivery payloads in Application and Infrastructure (for example `InvitationEnvelope(Guid InvitationId)`). They carry identifiers only (IA-REQ-029) and are never HTTP bodies.
- **Success writers.** `ResultHttpExtensions.cs` writes only `Results.NoContent()` (`:12`) and the bodyless `202` (`:21`); its `onSuccess` overload leaves the body to the endpoint. The 29 success writers in `src/Web/Endpoints` each pass a DTO or the value their `onSuccess` lambda receives. Neither folder contains an anonymous object (exit 1), and no `Result` or `ApplicationError` is handed to a writer or declared in `Produces` (both exit 1).
- **Guards:** `ProblemDetailsContractTests.cs:339` `List_create_and_bodyless_successes_carry_no_universal_envelope`, its helper `AssertNoUniversalEnvelope` (`:367`), and the 6.2 page-body test (`:384`), which calls it (`:436`).
- The largest match counts are `SessionTests.cs` (49), `OpenApiContractTests.cs` (41) and `PlatformEndpoints.cs` (38).

### 12.7 results

- `git rev-parse 2716aa6:src/Web/ClientApp/src/features/identity/problemCodes.json` and `git hash-object src/Web/ClientApp/src/api/problemCodes.json` both give `cb1efdccebbdbdb8e9f150d07077f0225550720d`. A raw `git show … | cmp` differs at byte 2 only because the working file has CRLF line ends (`core.autocrlf=true`); with CR removed, `diff` exits 0. The old path no longer exists.
- `git diff --stat -- src/Web/ClientApp/src/i18n/locales/en/errors.json src/Web/ClientApp/src/i18n/locales/es/errors.json src/Application/IdentityAccess/Common/IdentityAccessErrors.cs "*.resx"` shows nothing, and so does the same command against `2716aa6`. `git status --porcelain --untracked-files=all` on those paths is empty too.
- `rg -n 'invalid_page|invalid_page_size|invalid_cursor|page_out_of_range' src tests --glob '!**/node_modules/**'` → exit 1.

### 12.8 results

- `rg -n "organizations\.more|identities\.more|members\.showMore|invitations\.member\.showMore|roles\.showMore" src/Web/ClientApp/src` → exit 1 (`d1-12.8-search.log`).
- `npm run i18n:unused` → exit 0, "No unused keys found" for `common`, `identity` and `platform` (`d1-12.8-i18n.log`, 08:33:03).
- `npx vitest run src/i18n/catalog.contract.test.js` → exit 0, Test Files 1 passed (1), Tests 17 passed (17) (`d1-12.8-catalog.log`, 08:33:17).
- **Locales diff.** `git diff --stat`, against the index and against `2716aa6` alike: 4 files, +8/−18, and no untracked locale file. `d1-12.8-locales-proof.mjs` (`d1-12.8-locales-proof.log`, 08:36:24) exits 0 with "LOCALES PROOF: PASS":
  - In `en` and in `es`, `identity.json` removes exactly `invitations.member.showMore`, `members.showMore` and `roles.showMore`, and `platform.json` removes exactly `identities.more` and `organizations.more`.
  - No key is added and no value changes.
  - Each of the eight `+` lines equals a `-` line with only its trailing comma dropped, two per file. JSON requires that when the next key is deleted, so the diff holds only the five deletions (Deviations 3).

### 12.9 gate

Each step ran from `src/Web/ClientApp`, one after another (`d1-12.9-exits.txt`, 08:36:20–08:38:11):

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Vitest | `npx vitest run` | 0 | Test Files 46 passed (46); Tests 632 passed (632); 71.00 s | `d1-12.9-vitest.log` |
| Lint | `npx eslint src/` | 0 | No output | `d1-12.9-eslint.log` |
| Unused keys | `npm run i18n:unused` | 0 | "No unused keys found" for `common`, `identity` and `platform` | `d1-12.9-i18n.log` |
| Build | `npx vite build` | 0 | 2,639 modules transformed, built in 1.81 s; only the known chunk-size notice | `d1-12.9-vite.log` |

- **Baseline-timeout rule: 0 uses. A1-01 exception: 0 uses.** No test failed.
  - The record's entries under 15 s passed: #5 in 1 291 ms, #6 in 2 612 ms, #7 in 2 833 ms, #8 in 1 922 ms and #9 in 2 362 ms.
  - The rewritten tests behind entries 1–4 passed outright: the roles and members resumes in 6 499 and 3 828 ms, and R6C roles, members and invitations in 1 374, 1 943 and 1 546 ms.
- The log has 94 MSW unhandled-request warnings, the same count as C4's final 11A.11 gate log.
- `rg -n "eslint-disable" src/Web/ClientApp/src` finds 14 lines, as at 11A.11.

### Test Summary (D1)

- **Tests written:** none. Phase 12 declares no test file, and the test-file boundary expects none in batch D.
- **Tests passing:** 632/632 in the full Vitest run; `catalog.contract.test.js` 17/17 at 12.8.
- **Layers used:** document searches, snapshots, the catalog gates and the full SPA gate.
- **Approval tests:** none.
- **Pure functions created:** none.

### Work Unit Evidence (D1)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | tasks.md's focused check for unit D, `rg -n "features/identity/fieldErrors" .agents/skills`, → exit 1, no match. It is in `d1-12.5-green.log` with the other three 12.5 searches, all exit 1. RED: `d1-12.2-red.log`, exit 0 on exactly `SKILL.md:109`, `error-handling-rules.md:288,328,400,476,594` and `ui-composition-rules.md:317`. `cd src/Web/ClientApp && npm run i18n:unused` → exit 0. `npx vitest run src/i18n/catalog.contract.test.js` → exit 0, 17/17. `node <scratchpad>/d1-12.8-locales-proof.mjs` → exit 0, PASS. 12.7: equal blob hashes, empty diffs, and `rg` exit 1. |
| Runtime harness command/scenario and exact result | N/A: documents and searches only (tasks.md Suggested Work Units, unit D). Phase 13 owns the journeys (13.5), and they were not run. Regression evidence: `SPA-G`, every step exit 0 (Vitest 632/632, eslint, `i18n:unused`, `vite build`). |
| Rollback boundary | Revert the three documents only: `SKILL.md` (+1/−1) and `error-handling-rules.md` (+5/−5) back to the `features/identity/…` paths, and `ui-composition-rules.md` (+2/−2) back to "Preserve real cursor controls where the route already owns them." Uncheck 12.2–12.9. Drop this section, the two D1 rows, "13.1 onward, then Delivery" and D1's words in the merge line. Drop Engram `apply-progress-part-6`, the part-6 mentions in parts 1–5 and the D1 rows in part 1, and re-sync `tasks-part-2`. There is no code, test, data or schema change; batch D reverts alone. |

### Files changed (D1)

| File (from repository root) | Action |
| --- | --- |
| `.agents/skills/error-handling-standards/SKILL.md` | Modified (12.3): rule 14 (`:109`) names `src/Web/ClientApp/src/api/problemCodes.json`. +1/−1 |
| `.agents/skills/error-handling-standards/references/error-handling-rules.md` | Modified (12.3): `:288`, `:328`, `:400`, `:476` and `:594`. +5/−5 |
| `.agents/skills/frontend-design-standards/references/ui-composition-rules.md` | Modified (12.4): `:317-318`, offset `TablePagination` controls. +2/−2 |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: checkboxes 12.2–12.9 only |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: the two D1 rows, "13.1 onward, then Delivery", and D1 in the merge line; D1 appended; A1–C4 kept verbatim |

Test-file boundary: `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 82 entries (`d1-git-status.txt`), the same as C4. No source or test file was created, edited, moved or deleted, and `git diff --cached --name-only` is empty.

### Deviations from design (D1)

1. **`:400` names the catalogue by its relative import.** The snippet is labelled `// test/identityServer.js`, and the rules say "Copy the shape", so it imports `'../api/problemCodes.json'`, exactly as `src/test/identityServer.js:2` does. That import resolves to `src/api/problemCodes.json`, but the literal string does not appear on the line.
2. **`ui-composition-rules.md` changes two lines.** "offset `TablePagination`" is longer than "cursor", so the sentence rewraps and `:318` changes too. Otherwise the wording is the old sentence.
3. **"Only the five deletions" includes comma-only rewrites.** Deleting the last key of an object drops the trailing comma of the key before it, four times per language. The proof shows those `+` lines are the removed lines without the comma, with no key added and no value changed. C2 recorded the same shape at 11.13.
4. **The gate overlapped the last three replacements.** They were Markdown under `.agents`, which no `SPA-G` step reads, and they changed no SPA byte. So the gate ran on the final SPA bytes.

### Issues found (D1)

1. **ORCHESTRATOR DECISION NEEDED: 12.1 is left `[ ]`.** Search 1 matches `src/Web/ClientApp/src/api/pagination.js:58`, in `readPage`'s doc comment (`:56-61`): "The transport has already refused an undeclared member such as `nextCursor` and a missing one".
   - B1 created the file (it is untracked), so it is this change's own source.
   - The comment documents the E7 guard that the reviewed exclusions keep (`FORBIDDEN_SUCCESS_KEYS`). It is not a remnant of the retired cursor contract, and no exclusion lists it. Rewording it is therefore a judgement call, and it was not edited.
   - Option (a): reword the comment to drop the example (for example "has already refused an undeclared member and a missing one"). The change is comment-only; afterwards rerun 12.1, `npx eslint src/` and `npx vite build`.
   - Option (b): add `src/api/pagination.js:57-58` (`readPage`'s summary naming the E7 guard) to 12.1's reviewed exclusions in tasks.md, then check 12.1 on the existing evidence.
2. **A classification to confirm.** The third exclusion names "the no-`nextCursor` assertion in `ProblemDetailsContractTests.cs` (6.2)". It was read as covering that test's summary (`:381`) and its name (`:384`, which contains `no_cursor`), as well as the assertion at `:437`. The first and second exclusions were read the same way: the 9.5 refusal's summary and title (`problemDetails.test.js:137,140`) and the 10.3 drift test's title (`platformClient.test.js:188`). If only the assertion lines are excluded, those lines need the same decision as Issues 1. They sit in declared test rows 39, 8 and 11, and batch D edits no test file.
3. **Out-of-scope observations in the edited documents,** not edited:
   - `SKILL.md:17` and `:22` still describe the SPA transport as `features/*/api/`. The transport is now `src/api/apiTransport.js`, while the clients stay in `features/identity/api/` and `features/platform/api/`. No task names these lines, and CLAUDE.md, which says the same, is off-limits.
   - The `fieldError` snippet under `error-handling-rules.md:594` still shows `(problem, name, rule)`, while the moved helper is `fieldError(problem, name, t)` (`src/components/problemFields.js:82`). This predates the change.
   - `:362`, `:387-389` and `:823` stay as the recorded follow-ups (13.8).
4. **No mirrors.** 12.4's re-glob finds no copy of either standard, so no mirror decision is needed.
5. **Engram split.** Parts 1, 3 and 5 are near Engram's limit, so D1 is the new topic `sdd/offset-pagination-standard/apply-progress-part-6`.
   - Every part's preface names part-6, part 1's cumulative table carries the D1 rows, and `tasks-part-2` is re-synced from "## Phase 9" to the end.
   - The limit is 50,000 UTF-8 bytes, not characters. The first rewrite of part 3, whose preface was longer, came to 50,023 bytes, and Engram cut its last 23 bytes with a `... [truncated]` suffix. Part 3's preface was shortened and the part rewritten.
   - `engram export` into the scratchpad (`d1-engram-export-*.json`) and a script compared all seven copies with their scratch files, which are built from this file's segments.
6. **Hooks.** The Vercel plugin asked for `Skill(vercel-functions)` when `src/api/pagination.js` was read, and for `Skill(verification)` on the `vite` command. Neither was invoked: the Skill tool is forbidden here, and neither applies to this repository.
7. **Parallel work in the tree** is unchanged. `git status` lists 29 entries for `AGENTS.md`, `CLAUDE.md`, `.codex/**` and the module-separation plan, and 25 `.agents` entries outside the three D20 documents. D1 wrote none of them, and HEAD is still `f29ca74`.
8. **Task count:** 119 of 134 checked.

### Remaining tasks

- 12.1 (awaits the decision in Issues 1).
- Phase 13 (13.1–13.8), then Delivery (DL.1–DL.6).

### Workload / PR boundary (D1)

- Mode: `size:exception`, accepted by the user at DL.0 (one direct-to-main commit after Phase 13). No commit was made.
- Current work unit: D1 (tasks 12.1–12.9), which is all of batch D.
- **Boundary.** It starts from C4's tree, where the error-handling standards still named `features/identity/…` paths and the composition rules preserved cursor controls. It ends with those documents naming the module-neutral paths and offset `TablePagination` controls, batch D's checks and `SPA-G` green, and 12.1 open on one doc comment.
- **Review impact.** Documents only: +8/−8 over three files.

### Correction round 1 — validator findings D1-V01 to D1-V03 (2026-09-14)

Scope: the three validator findings on D1, and nothing else. One planning line changed, the 13.8 follow-up bullet in `tasks.md`. This section, the D1 row in "Cumulative task state" and the merge line were added to this file, and three Engram topics were re-synced. No source, test, catalog, configuration or standards file changed, so no SPA byte changed. The three D20 documents were not edited in this round. `git status --porcelain` still lists them as ` M`, and `git diff --numstat` gives +1/−1, +5/−5 and +2/−2, exactly D1's own 12.3 and 12.4 edits. `pagination.js` and the 12.1 exclusions are unchanged, and no task checkbox changed. No journey was run, and `testTimeout` stays at 15000 ms. No git write was made, nothing is staged, and HEAD is still `f29ca74`. Logs are `<scratchpad>/d1-corr1-*`.

| Finding | Disposition | Change made | Evidence |
| --- | --- | --- | --- |
| D1-V01 (blocking, 12.1) | **Not applied. ORCHESTRATOR DECISION NEEDED.** The finding leaves the choice between D1's options to the orchestrator or the user. Option (a) rewords the `pagination.js:57-58` comment. Option (b) adds those lines to 12.1's reviewed exclusions. 12.1 stays `[ ]`. | None | Both searches were rerun. Search 1 exits 0 with 20 match lines (`d1-corr1-12.1-search1.log`), and search 2 exits 0 with 14 (`d1-corr1-12.1-search2.log`). With CR removed and the lines sorted, each is the same set as D1's log (`d1-corr1-sorted-{d1,rerun}-{1,2}.txt`). D1's "21 lines" also counted the log's trailing `search1 exit 0` line. `src/Web/ClientApp/src/api/pagination.js:58` is still the only match outside the reviewed exclusions, and every other match falls under the exclusion D1 gave it. Search 2 still shows rate limits and attempt budgets only. |
| D1-V02 (should-fix, 12.1) | **Not applied. ORCHESTRATOR DECISION NEEDED.** Its fix settles the exclusion-3 reading in the same decision as D1-V01, for example by rewording exclusion 3 to "the 6.2 page-body test (name, summary and its no-`nextCursor` assertion)". That rewords a reviewed exclusion, which the executor may not do on its own, and batch D edits no test file. | None | `tasks.md:528` is unchanged. In `ProblemDetailsContractTests.cs`, the summary (`:381`) and the name (`:384`) belong to the 6.2 test, which calls `AssertOffsetPageBody` at `:389` and `:395`. The no-`nextCursor` assertion is at `:437`, inside that helper (`:434`). All three lines still match search 1. |
| D1-V03 (should-fix, 12.3) | **Applied.** | The `tasks.md` 13.8 follow-up bullet names `error-handling-rules.md:362,387-389,823` (the three places 12.3 leaves). Before, it named `:362,823`. | 13.8 (`tasks.md:586`) now names the same three places as 12.3 (`tasks.md:539`). `rg -n "362\|387\|823"` over the change folder, with `apply-progress.md` excluded, finds no third follow-up list. Its only other hits are `tasks.md:621` (`OpenApiContractTests.cs:368-387`), `design.md:145` (`RegisterInvitedUserTests` `:362`) and `design.md:516`. The last is the design's own follow-up note, which already puts `:388` and `:389` in the same follow-up as `:362` and `:823`, while `:387`'s label stays accurate. The stale content is recorded in `d1-corr1-rules-head.log`. `:362` names `features/identity/problemMessages.js`, which does not exist. `:388` imports `'./problemCodes.json'` under the label `// features/identity/problemCatalogue.contract.test.js` (`:387`), but that test imports `'../../api/problemCodes.json'` (`problemCatalogue.contract.test.js:6`). `:389` imports `'./problemMessages'`, and `:823` names `problemMessages.js`. All five lines are byte-identical to HEAD, as 12.3 requires. |

#### TDD Cycle Evidence (D1 correction round 1)

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 12.1 | The two 12.1 searches | Search | N/A (no edit) | N/A: a search task | ⚠️ Rerun: search 1 exit 0, 20 lines, the same set as D1, with `pagination.js:58` outside the exclusions. Search 2 exit 0, 14 lines, rate limits only. **Not checked** (D1-V01 and D1-V02 await the orchestrator) | N/A | N/A |
| 13.8 (text only; D1-V03) | `tasks.md` 13.8 follow-up bullet | Planning text | N/A: no test reads it | N/A: no behaviour | ✅ 13.8 and 12.3 name the same `error-handling-rules.md` places (`:362`, `:387-389`, `:823`) | N/A | N/A |

No test was written, edited or run as a RED, because the round changed no code; as Strict TDD requires, the table records the command results instead.

#### Work Unit Evidence (D1 correction round 1)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `rg -n -i 'nextCursor\|cursor\|PlatformDirectoryQuery\|PlatformDirectoryPage\|OpaqueCursor\|BoundedLimit\|MaximumLimit\|MinimumLimit' src tests --glob '!**/node_modules/**' --glob '!src/Web/wwwroot/openapi/**' --glob '!**/package-lock.json' --glob '!**/*.scss'` → exit 0, 20 lines, the same set as D1. `rg -n '\blimit\b' src/Web/Endpoints src/Web/ClientApp/src/features src/Application/IdentityAccess src/Infrastructure/IdentityAccess src/Infrastructure/Platform` → exit 0, 14 lines, the same set as D1. `rg -n "362\|387\|823"` over the change folder shows 12.3 and 13.8 aligned. `node <scratchpad>/d1-corr1-verify-engram.mjs <export> <label>` compares every Engram part with its file segment (see "Engram re-sync"). |
| Runtime harness command/scenario and exact result | N/A. The round changed only planning and progress text, and no SPA, API, test or standards byte. `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 82 entries, identical to D1's `d1-git-status.txt` (`d1-corr1-git-status.txt`), so D1's 12.9 `SPA-G` still ran on the current SPA bytes and was not rerun. `npx eslint src/` and `npx vite build`, which option (a) would need, were not run, because option (a) was not applied. |
| Rollback boundary | Restore 13.8's follow-up bullet to `error-handling-rules.md:362,823`. Remove this section, the sentence added to the D1 `12.1` row and the D1 sentence in "Correction rounds merged". Re-sync Engram `apply-progress`, `apply-progress-part-6` and `tasks-part-2` from the file. No code, test, data or schema change. |

#### Engram re-sync (D1 correction round 1)

- Before any edit in this round, `d1-corr1-verify-engram.mjs` read a fresh export (`d1-corr1-engram-export-0.json`; `d1-corr1-parity-pre.log`). Parts 1–6 each ended with their exact file segment, and each preface names part-6. `tasks-part-2` differed from the edited file by 39 bytes, 38,283 → 38,322, which is the 13.8 edit.
- D1's `d1-engram-parity-final.log` ends in `ENGRAM PARITY: FAIL`. The only flag behind that is `truncated: true` on part 6, and it is a false positive. Part 6's text quotes Engram's `... [truncated]` marker in D1's Issues 5, but the stored content does not end with it: 23,926 bytes, with `equalsScratch` and `bodyEqualsFileSegment` both true. This round's script counts a part as truncated only when its content ends with the marker.
- Re-synced topics:
  - `tasks-part-2` (#1034), from "## Phase 9" to the end.
  - `apply-progress` (#1065). The D1 row and the merge line grew by about 240 bytes, which keeps the part under 50,000 bytes, so part 1's cumulative table carries them.
  - `apply-progress-part-6` (#1133), with this section.
- Parts 2–5 are unchanged and still name part-6.
- The re-sync is checked with the same script on a fresh export (`d1-corr1-parity-post.log`).

#### Issues found (D1 correction round 1)

1. **ORCHESTRATOR DECISION NEEDED: 12.1 stays open on one decision that covers D1-V01 and D1-V02.**
   - (a) Reword the `pagination.js:57-58` comment so it no longer uses `nextCursor` as its example. This is comment-only. Then rerun 12.1, `npx eslint src/` and `npx vite build`.
   - (b) Add `pagination.js:57-58` to 12.1's reviewed exclusions.
   - In the same decision, confirm or reword exclusion 3, for example as "the 6.2 page-body test (name, summary and its no-`nextCursor` assertion)". The guard lines themselves stay unchanged.
   - Either way, check 12.1 against the rerun evidence above, then update this file, part 1's table and part 6.
2. D1's final parity log reports a false `FAIL`; see "Engram re-sync".
3. Hooks: reading `pagination.js` again triggered the Vercel plugin's `Skill(vercel-functions)` request. It was not invoked, because the Skill tool is forbidden here and the skill does not apply.
4. Task count is unchanged: 119 of 134 checked.

#### Remaining tasks (D1 correction round 1)

- 12.1 (awaits the decision in Issues 1).
- Phase 13 (13.1–13.8), then Delivery (DL.1–DL.6).

### Decision D1-V01/V02 (a): 12.1 closure

Decision (user, 2026-09-14): option (a) for findings D1-V01 and D1-V02. `readPage`'s doc comment is reworded so it no longer uses `nextCursor` as its example, and 12.1's exclusion 3 is reworded to cover the whole 6.2 page-body test. 12.1 is then checked on a fresh run of both searches.

Scope: that decision, and nothing else. Three files changed:
- `src/Web/ClientApp/src/api/pagination.js`: two comment lines;
- `tasks.md`: 12.1's checkbox and exclusion 3;
- this file: the 12.1 row in "Cumulative task state", the "Task closures merged" line and this section.

No test file, catalog, configuration or standards document changed. The three D20 documents still show +1/−1, +5/−5 and +2/−2 (`git diff --numstat`). The full Vitest suite was not run, no journey was run, and `testTimeout` stays at 15000 ms. No git write was made, nothing is staged, and HEAD is still `f29ca74`. Logs are `<scratchpad>/d1-close-*`.

#### Order of work

1. **Before any edit (10:06–10:09).**
   - `pagination.js` was copied to `<scratchpad>/d1-close-pagination.pre.js`: 4,602 bytes, sha256 `010f5bcfb4f2e8960986728aa378303efb487444314a04b45ac0c20a8dd4f49f`, identical to the file.
   - A fresh Engram export (`d1-close-engram-export-0.json`) was checked by `d1-corr1-verify-engram.mjs` (`d1-close-parity-pre.log`): PASS, all seven topics equal to their file segments.
   - The safety net ran (10:08:46), then the RED searches (10:08:56).
2. **The two edits** (10:09), with the Edit tool.
3. **GREEN searches and byte checks** (10:10:03).
4. **The focused gate**: tests, lint and build, one after another (10:10:08–10:10:31).
5. **This file**, then the Engram re-sync.

#### Edits

| File and line | Before | After |
| --- | --- | --- |
| `src/Web/ClientApp/src/api/pagination.js:57-58` | "The transport has already refused an undeclared member such as `nextCursor` and a missing one; this refuses metadata the contract cannot produce. The message is developer-facing" | "The transport has already refused undeclared and missing members; this refuses metadata the contract cannot produce. The message is developer-facing" |
| `tasks.md:525` | `- [ ] 12.1` | `- [x] 12.1` |
| `tasks.md:528` (exclusion 3) | "… and the no-`nextCursor` assertion in `ProblemDetailsContractTests.cs` (6.2);" | "… and the whole 6.2 page-body test in `ProblemDetailsContractTests.cs`: its summary (`:381`), its name `A_list_success_is_its_offset_page_dto_with_no_cursor_or_envelope_member` (`:384`) and its no-`nextCursor` assertion inside `AssertOffsetPageBody` (`:437`) (decision D1-V01/D1-V02 option a, user, 2026-09-14);" |

- **`pagination.js`: comment only.**
  - `diff` against the copy shows lines 57–58 only (`d1-close-pagination.diff`), and `git diff --no-index --numstat` gives 2/2.
  - The file went from 4,602 to 4,573 bytes, −29, exactly the removed words. It has 101 LF and 0 CR before and after, counted byte by byte.
  - Line 57 is 115 characters and line 58 is 85. Lines 59–60 are unchanged, so the comment still says the message is developer-facing, stays invariant English (L5), and that callers turn a throw into `unreadable_response`, whose words come from `errors.json`.
  - No code changed: `readPage`'s body, its developer message and every export are as before. The sha256 after the edit is `62189cd69de27bd86946828194eb8d8f16c64c52c8e6045991cf91c7a77cd5e5`.
- **`tasks.md`.**
  - Compared with the pre-edit copy stored in Engram (`d1-close-pre-expected-t2.md`), the segment from "## Phase 9" to the end has 331 lines before and after, and exactly two changed lines, 525 and 528.
  - The file has 0 CR. 120 tasks are checked and 14 unchecked.
  - Exclusions 1, 2, 4 and 5, and both searches, are unchanged.
- **Tests untouched.** The guard lines in `ProblemDetailsContractTests.cs` are unchanged; that file's ` M` is the change's own 6.2 edit (declared row 39). Under `tests` and `src/Web/ClientApp/src`, no file other than `pagination.js` is newer than the pre-edit copy.
- **Git state.** `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 82 entries, identical to `d1-corr1-git-status.txt` (`d1-close-git-status.txt`). `git diff --cached --name-only` is empty.

#### 12.1 classification (GREEN)

**Search 1**, the exact tasks.md command, exits 0 with 19 lines (`d1-close-green-search1.log`, 10:10:03).
- The RED run before the edit exited 0 with 20 lines (`d1-close-red-search1.log`, 10:08:56): the same set as D1 and D1 correction round 1.
- Sorted, with CR removed, the GREEN drops exactly one line, `src\Web\ClientApp\src\api\pagination.js:58`, and adds none.

| Match | Reviewed exclusion |
| --- | --- |
| `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs:338,341,342,343,350,351,354` (7 lines) | 4: `StackBaselineTests.cs:338-354`, where `cursor` is a string index |
| `src/Web/ClientApp/src/api/problemDetails.js:14` `FORBIDDEN_SUCCESS_KEYS` | 1: `nextCursor` stays forbidden |
| `src/Web/ClientApp/src/api/problemDetails.test.js:131,137,140,141` (4 lines) | 1: the `nextCursor` assertions in that file. `:131` is the "a pagination wrapper" drift case, and `:137`, `:140` and `:141` are the 9.5 retired-cursor refusal's summary, title and assertion |
| `src/Web/ClientApp/src/features/platform/platformClient.test.js:188,189` (title and fixture) | 2: the 10.3 `{ items: [], nextCursor: null }` drift test |
| `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformDirectoryContractTests.cs:70,136` | 3: the `limit`/`cursor` absence assertions (6.8 `:54-73`; 6.1) |
| `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:381,384,437` | 3, as reworded: the whole 6.2 page-body test, with its summary (`:381`), its name (`:384`) and its no-`nextCursor` assertion in `AssertOffsetPageBody` (`:437`) |
| `src/Web/ClientApp/src/web-api-client.ts` | 5: gitignored (`.gitignore:43`), so `rg` skips it; `rg --no-ignore -c -i cursor` on it exits 1 |

7 + 1 + 4 + 2 + 2 + 3 = 19 lines. No match remains outside the reviewed exclusions.

**Search 2** exits 0 with 14 lines (`d1-close-green-search2.log`). Sorted, it is the same set as the RED run (diff exit 0) and as D1: rate limits and attempt budgets only.
- `PlatformEndpoints.cs:134`;
- `IdentityLifecycleHandlers.cs:203`;
- `platformClient.test.js:79` and `PlatformInvitationPages.test.jsx:411`;
- `ISharedAttemptBudget.cs:23`, `PostgreSqlAttemptBudget.cs:12,36,63,67` and `AttemptBudgetCleanup.cs:12`;
- `RecoverPendingPlatformOwnerInvitation.cs:29` and `RecoverPendingPlatformOwnerInvitationHandler.cs:16,35`;
- `PlatformMfaHandlers.cs:207`.

#### Focused gate

Each step ran from `src/Web/ClientApp` (`d1-close-exits.txt` holds the three post-edit exits):

| Step | Command | Exit | Result | Log |
| --- | --- | --- | --- | --- |
| Safety net, before the edit (10:08:46) | `npx vitest run src/api/pagination.test.js` | 0 | Test Files 1 passed (1); Tests 26 passed (26) | `d1-close-safety-vitest.log` |
| Tests (10:10:08) | `npx vitest run src/api/pagination.test.js` | 0 | Test Files 1 passed (1); Tests 26 passed (26); 2.44 s | `d1-close-vitest.log` |
| Lint (10:10:15) | `npx eslint src/` | 0 | No output (0 bytes) | `d1-close-eslint.log` |
| Build (10:10:27) | `npx vite build` | 0 | Built in 845 ms; only the known chunk-size notice | `d1-close-vite.log` |

- The full Vitest suite was not run, as instructed. D1's 12.9 `SPA-G` (632/632) ran on the same SPA bytes apart from this comment.
- `npm run i18n:unused` was not rerun, because no catalog or key changed.

#### TDD Cycle Evidence (12.1 closure)

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 12.1 | The two 12.1 searches; `src/api/pagination.test.js` for the edited file | Search | ✅ `pagination.test.js` 26/26 before the edit | ✅ D1's recorded match `pagination.js:58`, outside every exclusion (`d1-12.1-search1.log`, `d1-corr1-12.1-search1.log`). Confirmed before the edit: search 1 exit 0, 20 lines (`d1-close-red-search1.log`) | ✅ Search 1 exit 0, 19 lines, each under a reviewed exclusion, with `pagination.js:58` gone. Search 2 exit 0, 14 lines, rate limits only. `pagination.test.js` 26/26; `npx eslint src/` exit 0; `npx vite build` exit 0 | ➖ Single: one comment and one exclusion | ➖ None needed: lines 59–60 keep their wrap |

No test was written or edited. The change is a doc comment and a planning line, and batch D declares no test file. As for the other search tasks (8A.1/8A.8, 12.2/12.5), the RED is the search match, and the GREEN is the same search with nothing left outside the exclusions.

#### Work Unit Evidence (12.1 closure)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `rg -n -i 'nextCursor\|cursor\|PlatformDirectoryQuery\|PlatformDirectoryPage\|OpaqueCursor\|BoundedLimit\|MaximumLimit\|MinimumLimit' src tests --glob '!**/node_modules/**' --glob '!src/Web/wwwroot/openapi/**' --glob '!**/package-lock.json' --glob '!**/*.scss'` → exit 0, 19 lines, all under the reviewed exclusions. `rg -n '\blimit\b' src/Web/Endpoints src/Web/ClientApp/src/features src/Application/IdentityAccess src/Infrastructure/IdentityAccess src/Infrastructure/Platform` → exit 0, 14 lines, rate limits only. `cd src/Web/ClientApp && npx vitest run src/api/pagination.test.js` → exit 0, 1 file, 26/26 tests. |
| Runtime harness command/scenario and exact result | N/A: no runtime behaviour changed, only a doc comment and planning text. Build proof: `cd src/Web/ClientApp && npx eslint src/` → exit 0, no output; `cd src/Web/ClientApp && npx vite build` → exit 0. The journeys belong to Phase 13 (13.5) and were not run. |
| Rollback boundary | Restore `pagination.js` from `<scratchpad>/d1-close-pagination.pre.js` (lines 57–58 only). In `tasks.md`, uncheck 12.1 and restore exclusion 3 to "the no-`nextCursor` assertion in `ProblemDetailsContractTests.cs` (6.2)". In this file, restore the 12.1 row to its pending text (kept in Engram `apply-progress`, see "Engram re-sync"), drop the 12.1 sentence from "Task closures merged" and drop this section. Re-sync Engram `tasks-part-2` and `apply-progress-part-6`. No test, data or schema change. |

#### Cumulative task state, as in the file

Engram part 1 cannot take this closure (see "Engram re-sync"), so the two lines it changed at the top of this file are recorded here as well.

| Work unit | Tasks | State |
| --- | --- | --- |
| D1 (Phase 12) | 12.1 | Completed `[x]` on 2026-09-14 under decision D1-V01/D1-V02 option (a) (see "Decision D1-V01/V02 (a): 12.1 closure" at the end of the D1 section). D1 and its correction round 1 had left it open on one search match outside the reviewed exclusions, `src/Web/ClientApp/src/api/pagination.js:58` |

"Task closures merged" gains this sentence: 12.1, closed on 2026-09-14 under decision D1-V01/D1-V02 option (a), recorded as "Decision D1-V01/V02 (a): 12.1 closure" at the end of the D1 section, after D1 correction round 1. It preserves every prior entry.

#### Open observation D1-V04 (for 13.2)

Validator observation D1-V04 (2026-09-14) is recorded as reported and is not diagnosed here. Under load, two Vitest tests failed:

- `src/AppRoutes.test.jsx` "finishes a provider sign-in on the public return route and lands on the account" (`:67`);
- `src/features/identity/ExternalProofResume.test.jsx` "reports a roles search that cannot reach the next page", the test rewritten at 11A.8. In the file it is the `roles` case of `it.each(['members', 'roles'])('reports a %s search that cannot reach the next page, and executes nothing', …)` (`:285`).

Neither test is on the 1.1 record, so the baseline-timeout rule excuses neither, and 13.2 requires every test to pass. A quiet diagnosis, a run without parallel load, follows before 13.2. This closure did not run the full suite, so it neither reproduces nor clears D1-V04; both tests passed in D1's 12.9 `SPA-G` (632/632). The observation stays open for 13.2.

#### Engram re-sync (12.1 closure)

- **Before any edit.** `d1-close-parity-pre.log` shows all seven topics equal to the file (PASS). Part 1 (#1065) stored 49,857 bytes.
- **Part 1 is not updated.** With this closure, part 1's file segment (the header, "Cumulative task state" and units A1–A2) grows from 48,670 to 48,878 bytes. With its stored preface, the part would be 50,065 bytes, or 50,088 with the preface's 12.1 wording updated; either is over Engram's 50,000-byte limit (`d1-close-p1-size.log`). So:
  - part 1 keeps its stored bytes, and its table still shows 12.1 as pending;
  - the two changed lines are recorded in this section ("Cumulative task state, as in the file"), which is part 6;
  - part 6's preface says so.
- **Re-synced topics:**
  - `tasks-part-2` (#1034), from "## Phase 9" to the end: 12.1's checkbox and exclusion 3;
  - `apply-progress-part-6` (#1133), with this section. Its preface says 12.1 is closed and that part 1's table is behind this section on 12.1.
- **Parts 2–5 are unchanged.** The prefaces of parts 2, 4 and 5 still say D1 leaves "12.1 … open for an orchestrator decision", and part 1's does too. They were outside this closure's re-sync scope; see Issues 2.
- **Check.** `d1-close-verify-engram.mjs` runs on a fresh export (`d1-close-engram-export-1.json`; `d1-close-parity-post.log`). It requires:
  - parts 2–5 and `tasks-part-2` to equal their file segments;
  - part 6 to equal its file segment and name part 1's limit;
  - part 1 to equal its pre-closure bytes, while its file segment differs from them only in the two table-area lines.

#### Files changed (12.1 closure)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/api/pagination.js` | Modified: `readPage`'s doc comment, lines 57–58 (2/2). Untracked, created by B1 |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: 12.1 checked (`:525`); exclusion 3 reworded (`:528`) |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: the 12.1 row and "Task closures merged" updated; this section appended; every earlier section kept verbatim |

#### Issues found (12.1 closure)

1. **Engram part 1 is behind the file on 12.1.** The closure would take part 1 over the 50,000-byte limit, so the current row lives in part 6 (see "Engram re-sync"). A later split of part 1, for example moving A2 into its own topic, would let part 1 carry it again. That split was outside this closure.
2. **Stale prefaces.** Parts 1, 2, 4 and 5 still describe 12.1 as left open for an orchestrator decision. Only part 6 and `tasks-part-2` were in this closure's re-sync scope. The file, part 6 and `tasks-part-2` are authoritative.
3. **D1-V04 is open for 13.2** (see "Open observation D1-V04").
4. **Hooks.** Reading `pagination.js` again triggered the Vercel plugin's `Skill(vercel-functions)` request, and the `vite build` command triggered `Skill(verification)`. Neither was invoked: the Skill tool is forbidden here, and neither applies to this repository.
5. **Parallel work in the tree** is unchanged: `AGENTS.md`, `CLAUDE.md`, `.codex/**`, the module-separation plan and the other `.agents` entries were not written.
6. **Task count:** 120 of 134 checked.

#### Remaining tasks (12.1 closure)

- Phase 13 (13.1–13.8), with D1-V04 diagnosed before 13.2, then Delivery (DL.1–DL.6).

## Remediation D1-V04: staged waits

Decision (user, 2026-09-14; Engram #1139, topic `sdd/offset-pagination-standard/decision-d1-v04-staged-waits`): question 1 of validator observation D1-V04. Apply the staged-wait robustness fix to `src/Web/ClientApp/src/features/identity/ExternalProofResume.test.jsx`, "reports a %s search that cannot reach the next page, and executes nothing" (the `members` and `roles` cases; declared test-file row 4, rewritten at 11A.8). Questions 2 (the other sessions' `find.exe` processes) and 3 (`src/AppRoutes.test.jsx:67`, only if it fails on a quiet machine) are not part of this remediation.

Scope: that decision, and nothing else. Three files changed:
- `src/Web/ClientApp/src/features/identity/ExternalProofResume.test.jsx`: the body of that one test, one hunk (+2/−1);
- `tasks.md`: row 4's planned edit, one "Items from apply validation" bullet and one 13.8 bullet;
- this file: this section.

No production code, catalog, configuration, page object or other test changed. `testTimeout` stays at 15000 ms (`src/Web/ClientApp/vitest.config.js:23`). No `asyncUtilTimeout` or per-call timeout was added or changed, and no retry, skip or only was added. No task checkbox changed: 120 of 134 checked. The top of this file ("Cumulative task state" and the merge lines) is unchanged, because no task changed state and Engram part 1 is at its limit. The full Vitest suite and the journeys were not run, as instructed. No git write was made, nothing is staged, and HEAD is still `f29ca74`. Logs are `<scratchpad>/d1v04-remediation-*`; the diagnosis logs are `<scratchpad>/d1v04-*`.

### Root cause

- After `renderAt('/external/return?outcome=proved')`, one `findByRole('alert')` (`:291` before the edit), with Testing Library's default 1,000 ms, waited across the whole return journey: the "Finishing up" screen, the Members or Roles screen, the page-2 lookup that the fixture fails (`HttpResponse.error()`), and the alert.
- The diagnosis instrumented copies of the file outside the repository (`d1v04-timeline-setup.js`, `d1v04-timeline.config.mjs`). Measured from the "Finishing up" heading, which appears as the return screen mounts:

| Timeline | members: heading / page-2 request / alert | roles: heading / page-2 request / alert |
| --- | --- | --- |
| Original test, quiet (`d1v04-timeline-quiet-epr.tsv`) | 446 / 839 / 1,113 ms | 469 / 857 / 1,115 ms |
| Original test, event loop starved 6 ms every 20 ms (`d1v04-timeline-starve6-orig.tsv`) | 401 / 740 / 999 ms | 413 / 681 / 899 ms |
| Staged-wait scratch copy, quiet (`d1v04-timeline-quiet-fix-1.tsv`) | 349 / 617 / 838 ms | 336 / 586 / 762 ms |

- The single wait therefore used its whole 1,000 ms budget or more (the decision records 713–1,115 ms). The instrumented runs still passed, so a pass depended on when the timeout callback itself ran. Under full-suite load the budget ran out:
  - diagnosis full run 2 (`d1v04-full-2.clean.log`: 4 files and 7 tests failed, 142.64 s) failed both cases with `TestingLibraryElementError: Unable to find role="alert"` (members 5,472 ms, roles 8,166 ms);
  - the validator's D1-V04 run failed the roles case.
- It is a test-robustness defect, not an implementation race. Every timeline shows the same order (screen heading, page-2 request, alert), the alert is the `network_unavailable` sentence, and no write is sent. In the scratch copy, no step took more than 349 ms.

### Test change (pre → post)

- Pre-edit copy: `<scratchpad>/d1v04-remediation-pre.test.jsx`, 15,537 bytes, sha256 `2aabf85913dfd2ea6c6c132bc2cc7fc245b12deffea4e9a35029f5a9e254009b`, identical to the file at 12:24:40.
- After the edit: 15,689 bytes, sha256 `9221e8a4ff24418726b1724cd206fe72c5f7ac78a35e57286a02f5aec6630687`. `git diff --no-index --numstat` gives 2 added and 1 deleted, in one hunk (`d1v04-remediation.diff`). The file keeps CRLF line ends: 295 CR and 295 LF before, 296 and 296 after.

```diff
     renderAt('/external/return?outcome=proved');
 
+    expect(await screen.findByRole('heading', { level: 1, name: kind === 'members' ? 'Members' : 'Roles' })).toBeInTheDocument();
+    await waitFor(() => expect(recorded.reads).toContainEqual({ pageNumber: 2, pageSize: PAGE_SIZE, returned: true }));
     expect(await screen.findByRole('alert')).toHaveTextContent(/^We could not reach the service\./);
-    expect(recorded.reads).toContainEqual({ pageNumber: 2, pageSize: PAGE_SIZE, returned: true });
     expect(recorded.writes).toEqual([]);
```

Every earlier assertion keeps its meaning, and none is dropped:
- **Alert text:** unchanged, the same `/^We could not reach the service\./` on the same `role="alert"`.
- **Page-2 read after return:** the same expected `{ pageNumber: 2, pageSize: PAGE_SIZE, returned: true }`. It is now awaited before the alert instead of checked once after it. The screen draws that alert only after this read has failed, so the fact asserted is the same.
- **No write:** `expect(recorded.writes).toEqual([])` is unchanged and still runs last, after the alert.
- **Added:** the returned screen's `h1` (`Members` or `Roles`), the journey's first step. The same heading query is used at `:168` of this file.
- The snippet uses the file's own names: the `it.each` parameter `kind`, `recorded` from `paginatedTarget`, `PAGE_SIZE` (`:14`), and `waitFor`, already imported at `:1`. No import, helper, fixture or other test changed.

### Declared allowance

Each of the three steps has Testing Library's default 1,000 ms, so the waits after the return mount can take up to about 3,000 ms in total, where one 1,000 ms wait covered the whole journey before. No timeout configuration changed: `testTimeout` stays 15000 ms and no `asyncUtilTimeout` is set. 13.8 now declares this allowance (tasks.md 13.8, "the walk-failure test waits per journey step").

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| D1-V04 remediation (declared row 4; no task checkbox) | `src/features/identity/ExternalProofResume.test.jsx`, "reports a %s search that cannot reach the next page, and executes nothing" | Integration (App, MemoryRouter, MSW) | ✅ 11/11 before the edit (12:24:42, exit 0; members 2,237 ms, roles 3,261 ms; `d1v04-remediation-safetynet.log`) | ✅ Characterization, not a forced failure: a deterministic RED would need a timeout change, which is forbidden. Evidence: in the diagnosis timelines the single wait at the old `:291` spans 1,113 ms (members) and 1,115 ms (roles) from the return mark, beyond its 1,000 ms budget, and diagnosis full run 2 failed both cases with `Unable to find role="alert"` | ✅ 5 sequential runs, each exit 0 and 11/11 (see below); `npx eslint` on the file exit 0 | ➖ The `it.each` already runs two cases (members and roles, on different screens and routes); both pass in every run | ➖ None needed: one hunk, no helper |

#### Safety net and GREEN runs

Every run used `cd src/Web/ClientApp && npx vitest run src/features/identity/ExternalProofResume.test.jsx --reporter=verbose` (with `NO_COLOR=1`), one after another.

| Run | Start | Exit | Tests | members | roles | Duration | Log |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Safety net (before the edit) | 12:24:42 | 0 | 11/11 | 2,237 ms | 3,261 ms | 31.47 s | `d1v04-remediation-safetynet.log` |
| GREEN 1 | 12:28:23 | 0 | 11/11 | 2,131 ms | 3,274 ms | 29.47 s | `d1v04-remediation-green-1.log` |
| GREEN 2 | 12:28:58 | 0 | 11/11 | 2,243 ms | 3,219 ms | 30.01 s | `d1v04-remediation-green-2.log` |
| GREEN 3 | 12:29:34 | 0 | 11/11 | 2,202 ms | 3,199 ms | 29.69 s | `d1v04-remediation-green-3.log` |
| GREEN 4 | 12:30:09 | 0 | 11/11 | 2,233 ms | 3,175 ms | 29.65 s | `d1v04-remediation-green-4.log` |
| GREEN 5 | 12:30:45 | 0 | 11/11 | 2,135 ms | 3,317 ms | 30.02 s | `d1v04-remediation-green-5.log` |

- The case durations include each test's first visit and its provider start, not only the waits. After the edit they stay in the safety net's range (members 2,131–2,243 ms, roles 3,175–3,317 ms), so the staged waits add no measurable time.
- Lint (12:32:11): `npx eslint src/features/identity/ExternalProofResume.test.jsx` → exit 0, no output (`d1v04-remediation-eslint.log`).
- Summary: `d1v04-remediation-green-summary.log`.

#### Machine load

The process snapshots are in `d1v04-remediation-procs.txt`, taken by `d1v04-remediation-procs.ps1`. No process was stopped.

| Snapshot | `find.exe` | `node` with `vitest` in its command line | `dotnet`, `testhost` or `vstest` | CPU load |
| --- | --- | --- | --- | --- |
| 12:24:40, before the safety net | 7 | 0 | 0 | not recorded |
| 12:27:29, after the safety net | 7 | 0 | 0 | 2 % |
| 12:28:21, 12:28:56, 12:29:32, 12:30:07 and 12:30:43, before GREEN 1–5 | 7 each | 0 each | 0 each | 0, 0, 2, 10 and 0 % |

- The 12:24:40 snapshot (`d1v04-remediation-procs-pre.txt`) lost its column separators to a shell-quoting error, but the process names can still be counted in it.
- The seven `find.exe` processes belong to other sessions (Git's `find.exe / -iname …` searches) and were not touched. The machine has 16 logical processors.
- These were therefore quiet-machine runs. They prove the test passes alone with its assertions intact; they do not reproduce the contention behind D1-V04. The robustness argument rests on the per-step margin: at most 349 ms per step in the diagnosis, against 1,000 ms per step. Per decision D1-V04, the proof under a full `SPA-G` on an unloaded machine belongs to 13.2.

### Work Unit Evidence (D1-V04 remediation)

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `cd src/Web/ClientApp && npx vitest run src/features/identity/ExternalProofResume.test.jsx --reporter=verbose` → 5 sequential runs, each exit 0, Test Files 1 passed (1), Tests 11 passed (11); members 2,131–2,243 ms, roles 3,175–3,317 ms. `cd src/Web/ClientApp && npx eslint src/features/identity/ExternalProofResume.test.jsx` → exit 0, no output. |
| Runtime harness command/scenario and exact result | N/A: a test-only change. No SPA, API or journey code changed, and no runtime boundary moved. The full `SPA-G` (13.2) and the journeys (13.5) were not run, as instructed. |
| Rollback boundary | Restore the test from `<scratchpad>/d1v04-remediation-pre.test.jsx` (one hunk, `:289-295`). In `tasks.md`, drop row 4's appended clause, the D1-V04 carry-over bullet and the 13.8 walk-failure bullet. Drop this section. Delete Engram `apply-progress-part-7`, restore part 6's preface, and re-sync `tasks` and `tasks-part-2`. No production code, data or schema change. |

### tasks.md edits

| Place | Edit |
| --- | --- |
| Test-file boundary, row 4 | The planned edit gains "; staged waits in the walk-failure test (decision D1-V04, 2026-09-14)". The Task column stays `11A.8` |
| Carry-over index, "Items from apply validation" | New bullet: "D1-V04 staged waits (row 4); 13.8 declares the up-to-3-step wait allowance" |
| 13.8 | New bullet after the gates bullet: the walk-failure test waits per journey step (up to three 1,000 ms steps; no timeout configuration changed), per decision D1-V04 |

No checkbox, search, exclusion or other row changed.

### Test-file boundary and git state

- `git status --short --untracked-files=all -- src/Web/ClientApp/src tests` lists 82 entries (`d1v04-remediation-git-status.txt`). Sorted, with CR removed, the list is identical to `d1-close-git-status.txt`. `ExternalProofResume.test.jsx` was already ` M` as declared row 4.
- `git diff --cached --name-only` is empty.

### Engram re-sync (D1-V04 remediation)

- **Before any planning edit.** `engram export` wrote `d1v04-remediation-engram-export-0.json` (1,139 observations). `d1v04-remediation-verify-engram.mjs … pre` passed (`d1v04-remediation-parity-pre.log`):
  - `tasks` (#1033, 43,810 bytes), `tasks-part-2` (#1034, 38,555 bytes) and `apply-progress-part-6` (#1133, 48,416 bytes) each ended with their exact file segment;
  - parts 1–5 were saved as stored;
  - `apply-progress-part-7` did not exist.
- **Topics written:**
  - `tasks` (#1033): row 4;
  - `tasks-part-2` (#1034): the carry-over bullet and the 13.8 bullet;
  - `apply-progress-part-6` (#1133): its preface only. It now says "Part 6 of 7" and names part 7. Its body is unchanged, because this section starts a new H2 after D1;
  - `apply-progress-part-7` (new; type architecture, project repositoriobase, `capture_prompt: false`): its preface and this section.
- **Parts 1–5 are unchanged.** Their prefaces still say "of 6" and do not name part 7 (Issues 3).
- **Check.** The same script runs in `post` mode on a fresh export. It requires:
  - #1033, #1034, part 6 and part 7 to equal the built files `d1v04-remediation-expected-{t1,t2,p6,p7}.md` exactly and to end with their live file segments;
  - each of them to be single, live, under 50,000 bytes and not truncated;
  - parts 1–5 to equal their pre-remediation bytes.
- **Result:** PASS (`d1v04-remediation-parity-post-1.log`, on export `d1v04-remediation-engram-export-1.json`, 1,140 observations).
  - `tasks` (#1033, 43,879 bytes), `tasks-part-2` (#1034, 39,062 bytes), part 6 (#1133, 48,588 bytes) and part 7 (#1140, 16,337 bytes) equal their built files;
  - parts 1–5 are unchanged.
- **Second check.** Recording this result changed part 7 and this file. Part 7 was therefore rebuilt from the file, written again and checked again (`d1v04-remediation-parity-post-2.log`).

### Files changed (D1-V04 remediation)

| File (from repository root) | Action |
| --- | --- |
| `src/Web/ClientApp/src/features/identity/ExternalProofResume.test.jsx` | Modified: the body of "reports a %s search that cannot reach the next page, and executes nothing", one hunk (+2/−1). Declared row 4 |
| `openspec/changes/offset-pagination-standard/tasks.md` | Modified: row 4's planned edit, one carry-over bullet, one 13.8 bullet |
| `openspec/changes/offset-pagination-standard/apply-progress.md` | Merged: this section appended; every earlier section kept verbatim |

### Deviations from the decision

None. The three waits and the kept `writes` assertion are as decided, and the snippet needed no renaming.

### Issues found (D1-V04 remediation)

1. **Quiet-machine proof only.** None of the six runs had a Vitest or .NET test process alongside it, so they show the test passes with its assertions intact, not that it survives contention. Per decision D1-V04, the proof under load is 13.2's full `SPA-G` on an unloaded machine.
2. **D1-V04 is only partly addressed.**
   - `src/AppRoutes.test.jsx:67`, "finishes a provider sign-in on the public return route and lands on the account", is untouched (question 3).
   - The seven `find.exe` processes from other sessions are still running (question 2).
   - The "Open observation D1-V04" text in the D1 section is kept verbatim as history.
3. **Stale prefaces.** Parts 1–5 still say "Part N of 6" and do not name part 7. Updating them was outside this remediation, and part 1 is at Engram's limit. The file, part 6's preface and part 7 are authoritative.
4. **Hooks.** The Vercel plugin asked for `Skill(verification)` on the lint command, because it matched `vite`. It was not invoked: the Skill tool is forbidden here, and the skill does not apply to this repository.
5. **Parallel work in the tree** is unchanged. `AGENTS.md`, `CLAUDE.md`, `.codex/**`, the module-separation plan and the `.agents` entries were not written.
6. **Task count:** 120 of 134 checked.

### Remaining tasks (D1-V04 remediation)

- Phase 13 (13.1–13.8), with 13.2's full `SPA-G` on an unloaded machine carrying the D1-V04 proof, and questions 2 and 3 still open. Then Delivery (DL.1–DL.6).
