# Tasks: Offset Pagination Standard

Change `offset-pagination-standard` · baseline `main` at `2716aa6` · artifact store hybrid · `delivery_strategy: ask-on-risk`.

- Inputs, authoritative as files: `proposal.md` (D01–D24; PD-a, PD-b, PD-1–PD-7, PD-5 as expanded), the four specs, `design.md` (AD1–AD18), `exploration.md`, and the plan `docs/superpowers/plans/2026-09-13-cross-project-result-pagination-standard.md` (cited as `plan:<line>`), and `openspec/config.yaml` (`strict_tdd: true`).
- This is functional and error-handling work, not a visual change.
- Phase numbers are plan task numbers, in plan order.
- The user's completeness requirement overrides the skill's 530-word cap.

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~4,300–5,600 authored (A 2,000–2,500 · B 600–800 · C 1,600–2,100 · D 60–150), plus 7 file moves |
| 400-line budget risk | High |
| Chained PRs recommended | No |
| Suggested split | Direct-to-main commits per batch A → B → C → D (granularity pending) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: High
Work units map to direct-to-main commits per batch, because CLAUDE.md forbids pull requests and feature branches; the pending decision is commit granularity and push timing. Both open decisions are settled at DL.0, before Phase 1 starts.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| A | Tasks 1–8A: neutral problem infrastructure, AD8 mapping, offset API, SPEC amendment | Commit on main (batch A) | `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~PlatformDirectoryContractTests"` | `NET-G FT` over real PostgreSQL (Docker). Journeys N/A until C: the strict reader (E7) rejects the new shape | Reverts only with B and C (wire-shape coupling); no data migration |
| B | Tasks 9–10: `src/api/pagination.js`, clients, `useRead` | Commit on main (batch B) | `cd src/Web/ClientApp && npx vitest run src/api/pagination.test.js` | N/A: MSW-only; screens migrate in C; journeys run at 13.5 | Reverts with A and C |
| C | Tasks 11–11A: `TablePagination`, read states, walks, key deletions | Commit on main (batch C) | `cd src/Web/ClientApp && npx vitest run src/features/identity/roles/RolesPage.test.jsx` | Journeys command at 13.5 (English and the Spanish smoke) | Reverts with A and B |
| D | Task 12: leftover searches and D20 standards edits | Commit on main (batch D) | `rg -n "features/identity/fieldErrors" .agents/skills` (expect nothing) | N/A: documents and searches only | Reverts alone |

Batch A changes the wire shape and batches B–C teach the SPA to read it, so A–C reach `origin/main` together.

## Conventions and verification commands

- SPA paths are written from `src/Web/ClientApp/` (for example `src/api/pagination.js`).
- `FT:` stands for `tests/Application.FunctionalTests/IdentityAccess/`.
- Every other path is written from the repository root.

```text
SPA-F <files>  cd src/Web/ClientApp && npx vitest run <files>
SPA-G          cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npm run i18n:unused && npx vite build
NET-F <P> <C>  dotnet test <P> --filter "FullyQualifiedName~<C>"   (A|B expands to FullyQualifiedName~A|FullyQualifiedName~B)
NET-G <P>      dotnet test <P> --filter "TestCategory!=IndependentDevelopmentReview"
DT  tests/Domain.UnitTests/Domain.UnitTests.csproj
UT  tests/Application.UnitTests/Application.UnitTests.csproj
FT  tests/Application.FunctionalTests/Application.FunctionalTests.csproj
IT  tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj
```

- FT, IT and the journeys need Docker. Run one Aspire-bearing project per process.
- The plan's `rg` globs and the `LIST-CODES` script are written for a POSIX shell; under PowerShell they may need other quoting, but the expected results are unchanged.
- `LIST-CODES <file>`: regenerate `src/Web/wwwroot/openapi/v1.json` with `dotnet build src/Web/Web.csproj` (without `-p:OpenApiGenerateDocumentsOnBuild=false`), then run this from the repository root, redirected to `<file>`. It prints the sorted `x-problem-codes` of every status of the seven list `get` operations:

  ```text
  node -e "const d=require('./src/Web/wwwroot/openapi/v1.json');const o={};for(const p of ['/api/platform/organizations','/api/platform/identities','/api/platform/admins','/api/platform/audit','/api/tenants/{tenantId}/roles','/api/tenants/{tenantId}/members','/api/tenants/{tenantId}/invitations']){o[p]={};for(const [s,r] of Object.entries(d.paths[p].get.responses))if(r['x-problem-codes'])o[p][s]=[...r['x-problem-codes']].sort();}console.log(JSON.stringify(o,null,2))"
  ```

- Baseline-timeout rule, for every `SPA-G` gate: a Vitest test on the 1.1 record below that fails a `SPA-G` run is rerun alone with `SPA-F <its file>` and must pass there. The rule matches by file and test name; a failing test that is not on the record is not excused. Exception (decision A1-01, user, 2026-09-13): until 11A.8 rewrites them, the record's ExternalProofResume "resumes a roles target beyond the first page exactly once with its original draft and version" is excused in every intermediate `SPA-G` gate even when it also fails alone. The same applies to that file's "reports a refused role change that was resumed after the provider round trip" when it fails directly after that timeout (its recorded cascade). 13.2 excuses neither. Never raise `testTimeout` or add a per-test timeout. 11A.11, 12.9 and 13.2 apply the rule to exactly this list, and 13.8 reports every use of it.
- 1.1 record of load-sensitive Vitest tests, restated after the A1 validation (finding A1-02). Evidence: the `baseline-*`, `1.11-*`, `val-a1-*` and `corr-a1-*` logs in `<scratchpad>`, detailed in apply-progress "1.1 Baselines".
  - Failures at or beyond 15 s under parallel load: four tests in two files (three Vitest timeouts and one assertion failure), all rewritten later.
    - `src/features/identity/ExternalProofResume.test.jsx` "resumes a roles target beyond the first page exactly once with its original draft and version": `Test timed out in 15000ms` (15 056 ms). It sits at the 15 s edge even when its file runs alone. It timed out in four of five such runs (15 107, 15 937, 15 118 and 15 059 ms) and passed once, at 14 706 ms, in the A1 correction round, so the rule passes for it only by chance. Decision A1-01 (user, 2026-09-13): it is excused in 1.11 and in every later intermediate `SPA-G` gate until 11A.8 rewrites it, and the rewritten test must pass 13.2 outright. 13.8 reports the exception.
    - Same file, "resumes a members target beyond the first page exactly once with its original draft and version": an assertion failure after 15 s (15 150 ms; 15 818 ms at validation), not a Vitest timeout. Alone it passes (8 938–9 818 ms). 11A.8 rewrites it.
    - `src/features/identity/IdentityAccessReviewRevalidation.test.jsx` R6C "continues the 'roles' directory with 101 distinct records" and "continues the 'members' directory with 101 distinct records": `Test timed out in 15000ms` (16 772 and 18 803 ms). Alone the file passes 11/11. 11A.10 rewrites them.
  - Load-sensitive failures under 15 s:
    - `src/features/identity/roles/RolesPage.test.jsx` "roles page > lists the roles with what each one confers, and marks the built-in one": 1 421 ms at baseline. It is not a timeout, and it excuses no other test. Alone the file passes 10/10.
    - `src/features/identity/ExternalProofResume.test.jsx` "finishes the ownership transfer the person asked for, against the roster as it stands": 2 548 ms at baseline. Alone it passes (1 570–1 646 ms).
    - Same file, "does not resume when the members target is missing after return": 8 259 ms at 1.11 and 9 145 ms at validation. Alone it passes (7 569–7 985 ms).
    - Same file, "reports a refused role change that was resumed after the provider round trip": 1 338 ms at 1.11, directly after the roles-target timeout. It failed the same way in both baseline runs of its file alone (1 413 and 1 453 ms). It passed alone at 1.11, at validation and in the correction round (1 501, 1 509 and 1 317 ms), and isolated with `-t` (2 164 ms). It is a cascade of the roles-target timeout.
    - `src/features/identity/ExternalProofReturnRevalidation.test.jsx` "R6A resumes the original device revocation exactly once after a successful provider return": 1 869 ms at 1.11. Alone it passes 1/1 (1 550 and 1 481 ms).
  - After 11A.8 and 11A.10, the four entries at or beyond 15 s leave the record, and their rewritten tests must pass `SPA-G` outright. The entries under 15 s stay on it.

## Test-file boundary

### Declared test files

| # | File | Planned edit | Task |
|---|------|--------------|------|
| 1 | `src/features/identity/roles/RolesPage.test.jsx` | Rewrite: page fixtures, navigation, catalogue loaded once or failing, E4 and E8–E11; `:195` (failure beside Show more) is replaced by 11A.1 | 11.1, 11A.1, 11A.3 |
| 2 | `src/features/identity/members/MembersPage.test.jsx` | Rewrite: navigation, whole role catalogue | 11.3 |
| 3 | `src/features/identity/invitations/InviteMemberPage.test.jsx` | Rewrite: navigation, catalogue walk, walk bound, failed walk; `:247` becomes an offset page-change failure (AD15) | 11.5 |
| 4 | `src/features/identity/ExternalProofResume.test.jsx` | Rewrite (D12): offset reads, 26-row fixtures, failed walk; staged waits in the walk-failure test (decision D1-V04, 2026-09-14) | 11A.8 |
| 5 | `src/features/identity/IdentityAccessReviewRevalidation.test.jsx` | Rewrite R6C as a `pageNumber` walk over 26 rows (D09, D12). The shared list helper `pageResponse` (`:36`), which also feeds the R6A and R6B fixtures, answers an offset page; every R6A and R6B assertion stays (D12, finding B2-V03) | 11A.10 |
| 6 | `src/features/identity/useRead.test.jsx` | Rewrite: `cursor` becomes `page`; delete the merge test | 10.6 |
| 7 | `src/features/identity/api/identityClient.test.js` | Rewrite: URLs, null page, malformed page, catalogue | 10.1 |
| 8 | `src/features/identity/api/problemDetails.test.js` | Move to `src/api/`; add the retired-cursor test | 1.8, 9.5 |
| 9 | `src/features/platform/PlatformPanel.test.jsx` | Rewrite: three directories paginate; `:425-456` retries the requested page | 11.7 |
| 10 | `src/features/platform/identities/PlatformIdentitiesPage.test.jsx` | Rewrite: navigation and page-state tests | 11.9, 11A.2 |
| 11 | `src/features/platform/platformClient.test.js` | Import path; `(page, options)`; malformed page | 1.7, 10.3 |
| 12 | `src/features/platform/platformDirectory.test.js` | Import path; offset fixtures | 1.7, 10.4 |
| 13 | `src/AppRoutes.test.jsx` | Fixtures only | 11.11 |
| 14 | `src/i18n/presentation.test.jsx` | `:25` fixture; delete the test at `:116-119` (D22) | 11.14 |
| 15 | `src/i18n/spanish.test.jsx` | One added `getItemAriaLabel` assertion | 11.12 |
| 16 | `src/test/identityFiles.contract.test.js` | `:28` entry only; catch counts unchanged | 1.2 |
| 17 | `src/features/identity/ProblemMessage.test.jsx` | Move to `src/components/`; import paths only | 1.8 |
| 18 | `src/features/identity/api/apiTransport.test.js` | Move to `src/api/`; import paths only | 1.8 |
| 19 | `src/features/identity/problemCatalogue.contract.test.js` | Import path; words gate reads `languages.supported` through an `import.meta.glob` catalogue lookup; one added guard test, "reads every supported language, the source included, from the registry for the words gate", so the gate cannot loop zero times (accepted by the user, finding A1-03, 2026-09-13) | 1.7, 3.2 |
| 20 | `src/i18n/catalog.contract.test.js` | Import path only | 1.7 |
| 21 | `src/test/identityServer.js` (helper) | Import path only | 1.7 |
| 22 | `src/features/identity/fieldErrors.test.js` | No edit expected (PD-5) | 1.9 |
| 23 | `src/api/pagination.test.js` | Create (plan Task 9) | 9.1–9.8 |
| 24 | `FT:Platform/PlatformDirectoryContractTests.cs` | Rewrite (D13, D14); three new tests; using | 6.1, 6.8, 6.9 |
| 25 | `FT:Members/AdministrationDirectoryRevalidationTests.cs` | R6C walk (D09) over its three TestCases, with the order check; direct store calls `:76-83`; using | 6.6, 6.14 |
| 26 | `FT:Members/IndependentDevelopmentAdministrationReviewTests.cs` | `:114`, `:180` page types; test double `:216-217`; using | 6.14 |
| 27 | `FT:Members/MembershipAdministrationTests.cs` | Cross-tenant `404` with offset parameters; drop `NextCursor` `:320` | 6.3, 8.5 |
| 28 | `FT:Members/MembershipAtomicityTests.cs` | Drop `NextCursor` `:160` | 8.5 |
| 29 | `FT:Members/OwnershipTransferTests.cs` | Drop `NextCursor` `:325` | 8.5 |
| 30 | `FT:Lifecycle/ConcurrentDeactivationFloorTests.cs` | Test double `:134`; using | 6.14 |
| 31 | `FT:Roles/RoleAdministrationTests.cs` | HTTP offset RED; cross-tenant `404`; drop `NextCursor` `:337` | 6.3, 8.5 |
| 32 | `tests/Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs` | PD-3 shape; drop `:41`; rename `:130`; using | 6.5 |
| 33 | `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs` | Inspect; no edit expected (`cursor` is a string index) | 12.1 |
| 34–37 | `FT:Platform/{PlatformProjectionTests,PlatformIdentityLifecycleTests,PlatformDirectoryAccessTests,PlatformAdministrationTests}.cs` | Construct `PaginationQuery`; using | 6.15 |
| 38 | `FT:Api/OpenApiContractTests.cs` | `:491` catalogue path; `:567-592` migrated binding test | 1.3, 6.4 |
| 39 | `FT:Api/ProblemDetailsContractTests.cs` | No-envelope assertions; list `200` body members; Platform `pageSize` clamp over HTTP; list-read `403` problem shape | 2.1, 6.2 |
| 40 | `tests/Application.UnitTests/Common/Models/PaginationQueryTests.cs` | Create | 4.1 |
| 41 | `tests/Application.UnitTests/Common/Models/PaginatedListTests.cs` | Create | 4.3 |

- No task edits any other file under `tests/`, or any other `*.test.js` or `*.test.jsx`.
- If a journey or page object depends on cursors or "show more", stop and report (plan:122).
- `FT:Members/DelegatedAdministrationRevalidationTests.cs` stays unedited: it seeds at most 3 members, and its private `MemberPage(MemberRow[] Items)` (`:279`) compiles unchanged.
- These run unchanged as proof and are not declared (PD-5): `src/features/platform/invitations/{MfaRecoveryPage,PlatformInvitationPages}.test.jsx`, and the `PasswordPages`, `InvitationPages`, `PersonalPages` and `RegisterOrganizationPage` tests.
- The known slow tests are rewritten fast and green with 26-row fixtures, and `testTimeout` stays at 15000 ms:
  - `ExternalProofResume` "resumes a members/roles target beyond the first page";
  - `IdentityAccessReviewRevalidation` R6C "101 distinct records".

  Together they are all four entries at or beyond 15 s in the 1.1 record (see Conventions); no third timeout file exists. The record's entries under 15 s follow the baseline-timeout rule.
- The 3 `IndependentDevelopmentRetentionReviewTests` failures (intentional since b9a30d1) are not touched.
- These files gain `using CleanArchitecture.Application.Common.Models;`:
  - rows 24–26, 30, 32 and 34–37;
  - both new `Common/Models` test files.

  `ProblemDetailsContractTests.cs` already has it (`:4`).

## Precondition - Delivery decisions (before Phase 1; not an apply batch)

- [x] DL.0 (Settled with the user on 2026-09-13; answers recorded under "Open decisions".) Before any Phase 1 edit, the orchestrator settles both items under "Open decisions (orchestrator)" with the user and records each answer there ("Decision needed before apply: Yes"). Apply does not start while either is open; DL.4 and DL.5 follow the recorded answers.

## Phase 1 - Module-neutral SPA problem infrastructure (Batch A)

- [x] 1.1 Record baselines before any edit, one project per process; edit nothing.
  - Run: `SPA-G`; `NET-G FT`; `NET-G IT`; `dotnet test <FT> --filter "TestCategory=IndependentDevelopmentReview"`, and the same for `<IT>`.
  - Record: the 3 Vitest timeouts by file and test name (config.yaml:70 names two; name the third), the 3 retention failures, and the state of the administration-review tests.
    - Restated after validation (A1-02): the failures at or beyond 15 s are four tests in the two files config.yaml:70 names, and no third timeout exists. The load-sensitive failures under 15 s are listed separately. Both lists form the 1.1 record under Conventions.
  - Record `rg -n "eslint-disable" src/Web/ClientApp/src` (9 lines at `2716aa6`; the earlier "10" was a miscount, finding A1-06), for 11A.11 and 13.2.
  - Run `LIST-CODES <scratchpad>/list-problem-codes.baseline.json`. At `2716aa6` it matches the list-read-error-contract table:
    - the four Platform directories declare `400` `invalid_request`, `validation_failed`; `401` `authentication_required`, `invalid_session`, `recent_mfa_required`; `403` `permission_denied`; `500` `internal_server_error`;
    - the three tenant lists declare `400` `invalid_request`; `401` `authentication_required`, `invalid_session`; `403` `permission_denied`; `404` `not_found`; `500` `internal_server_error`.
- [x] 1.2 RED (plan Step 1, D06, D21): `src/test/identityFiles.contract.test.js`: replace only the `:28` entry with the neutral paths; catch counts `:8-18,117-128` stay. Expected failure: the path does not exist. Run `SPA-F src/test/identityFiles.contract.test.js`.
- [x] 1.3 RED (D04): `FT:Api/OpenApiContractTests.cs:491` reads `src/Web/ClientApp/src/api/problemCodes.json`. Expected failure: file not found. Run `NET-F FT OpenApiContractTests`.
- [x] 1.4 GREEN (plan Step 2, AD10): move `src/features/identity/api/{problemDetails,apiTransport}.js` to `src/api/` (schema import depth 6→4), `src/features/identity/problemCodes.json` to `src/api/` (bytes unchanged) and `src/features/identity/ProblemMessage.jsx` to `src/components/`. Change import paths only.
- [x] 1.5 GREEN (PD-5 expanded, D19): create `src/components/problemFields.js`.
  - It holds the nine exports plus the private `REQUIRED_FIELD_KEYS`, `sameFieldName` and `detailsForField`, moved verbatim from `fieldErrors.js:7-38,87-97,117-167`. Its only import is `../api/problemDetails`.
  - `fieldErrors.js` keeps its four registration exports and imports only `./register/cuit`. No re-export shim.
- [x] 1.6 GREEN (plan Step 2, D19): rewrite import paths in every importer of a moved module or helper.
  - Scope: the design importer table, its 17 mechanical importers (including `src/features/platform/shared/PlatformStepUpForm.jsx`), the five screens, `useRead.js` and both clients.
  - `PersonalPages.jsx` and `RegisterOrganizationPage.jsx` split their import in two.
- [x] 1.7 GREEN (plan Step 3, D06): import paths only, in `src/test/identityServer.js:2`, `src/i18n/catalog.contract.test.js:4`, `src/features/identity/problemCatalogue.contract.test.js:5`, `src/features/platform/platformClient.test.js:3` and `src/features/platform/platformDirectory.test.js:3`.
- [x] 1.8 (D06, AD10): move the tests, changing import paths only. `src/features/identity/api/{problemDetails,apiTransport}.test.js` go to `src/api/`, and `src/features/identity/ProblemMessage.test.jsx` goes to `src/components/`. `problemCatalogue.contract.test.js` stays.
- [x] 1.9 Verify (plan Step 4, PD-5):
  - `SPA-F src/api/problemDetails.test.js src/api/apiTransport.test.js src/components/ProblemMessage.test.jsx src/features/identity/problemCatalogue.contract.test.js src/test/identityFiles.contract.test.js src/features/platform/PlatformPanel.test.jsx src/features/platform/invitations/MfaRecoveryPage.test.jsx src/features/platform/invitations/PlatformInvitationPages.test.jsx src/features/identity/fieldErrors.test.js`
  - `NET-F FT OpenApiContractTests`
  - `rg -n "fieldErrors'" src/Web/ClientApp/src` lists only `PersonalPages.jsx`, `RegisterOrganizationPage.jsx` and `fieldErrors.test.js`.
  - `rg -n '^(import|export)' src/Web/ClientApp/src/features/identity/fieldErrors.js` lists exactly one import, from `./register/cuit`, and the four exports `organizationRegistrationFields`, `personalRegistrationFields`, `validateOrganizationRegistration` and `validatePersonalRegistration`. No `export {` line and no `./api/problemDetails` import remain.
  - `rg -n '^(import|export)' src/Web/ClientApp/src/components/problemFields.js` lists exactly one import, from `../api/problemDetails`, and the nine exports `validationDetailText`, `selectFieldErrors`, `claimedFieldNames`, `unclaimedFieldErrors`, `fieldIdFor`, `clearFieldError`, `fieldError`, `firstInvalid` and `fieldErrorText`.
- [x] 1.10 L6 proof (plan Step 5):
  - `SPA-F src/components/ProblemMessage.test.jsx src/i18n/spanish.test.jsx src/i18n/presentation.test.jsx src/i18n/catalog.contract.test.js`
  - `cd src/Web/ClientApp && npx eslint src/components/ProblemMessage.jsx src/components/problemFields.js`
  - `git diff --stat -- src/Web/ClientApp/src/i18n/locales` shows nothing.
  - Spanish proof (spec "Spanish words survive the move"): `spanish.test.jsx:98-114` renders the moved `ProblemMessage` in `es` through `NavMenu.jsx:153`; `catalog.contract.test.js` keeps every `es` `errors` value non-empty with the `en` placeholders; `ProblemMessage.test.jsx:80,107` covers `permission_denied` and the reference in `en`.
- [x] 1.11 Gate: `SPA-G`, under the baseline-timeout rule and its A1-01 exception. A stale import of a moved name fails `vite build`.

## Phase 2 - Result-to-HTTP mapping and problem metadata (Batch A)

- [x] 2.1 Characterization (plan Step 1): `FT:Api/ProblemDetailsContractTests.cs` asserts that list, create and bodyless successes carry no `success`, `data`, `error` or `value`. Expect PASS: this pins behaviour before the refactor. Run `NET-F FT ProblemDetailsContractTests`.
- [x] 2.2 (plan Step 2, AD8): add `ToAcceptedHttpResult(this Result, HttpContext, ApiProblemDetailsMapper)` to `src/Web/Infrastructure/ResultHttpExtensions.cs` (plan:240-246).
  - Success is `202`; failure is `mapper.ToHttpResult(result.Error!)`.
  - It writes no body, chooses no status and logs nothing. No new test: the AD8 proof list covers it.
- [x] 2.3 REFACTOR (AD8): 24 `Results.NoContent()` sites move to `ToHttpResult(context, problems)`.
  - `src/Web/Endpoints/Identity.cs:72`
  - `Identity/`: AccountLifecycleEndpoints:97, ExternalLoginEndpoints:151,174, InvitationEndpoints:129, MembershipEndpoints:74,110, PasswordEndpoints:74, PersonalEndpoints:86, RoleEndpoints:105, SessionEndpoints:66,74,82
  - `Platform/`: PlatformEndpoints:210,224,248,268,291,325, PlatformInvitationEndpoints:74, PlatformMfaEndpoints:106,120,150, PlatformRetentionEndpoints:98
- [x] 2.4 REFACTOR (AD8): 18 `Ok`/`Created` sites move to `ToHttpResult(context, problems, onSuccess)`.
  - `Identity/`: ContextEndpoints:40,52, DocumentDisputeEndpoints:56, ExternalLoginEndpoints:157, MembershipEndpoints:80,88,101, PasswordEndpoints:52, PersonalEndpoints:92,106, RoleEndpoints:60,68,76,85,96, SessionEndpoints:58
  - `Platform/PlatformRetentionEndpoints:64,80`
  - The three list sites are rewritten again at 8.2.
- [x] 2.5 REFACTOR (AD8): 8 bodyless `202` sites move to `ToAcceptedHttpResult`.
  - Sites: `Identity.cs:64`; `Identity/` AccountLifecycleEndpoints:86, InvitationEndpoints:147, PasswordEndpoints:64, PersonalEndpoints:78; `Platform/` PlatformEndpoints:145,309, PlatformInvitationEndpoints:54.
  - Neutral flows stay byte-identical. Guards and effects keep their order (`ExternalLoginEndpoints.cs:150`).
  - Unchanged: `SessionEndpoints.cs:85-112,114+` and the early guards at `InvitationEndpoints.cs:100,126`.
- [x] 2.6 Optional (plan Step 3, plan:203,251; exploration.md:179): by default, no edit.
  - If `src/Web/Infrastructure/ApiProblemMetadata.cs` is touched at all, it gets XML, `//` or `#region` comments only.
  - No static nested groups, and no member renamed or moved, so no `ApiProblemMetadata.*` reference in `src` or `tests` changes (8.3 keeps `ApiProblemMetadata.InvalidRequest.Code`).
  - No behaviour change and no second registry.
  - If a comment is added, add an `ApiProblemMetadata.cs` row (Modify, comments only) to design.md File Changes in the same batch.
- [x] 2.7 Verify (plan Step 4, AD8 proof):
  - `NET-F FT "ProblemDetailsContractTests|OpenApiContractTests|ErrorCatalogContractTests|InvitationRouteContractTests|RegistrationHttpValidationTests|RegistrationTests|InvitationHttpContractTests|RegisterInvitedUserTests|PasswordLifecycleTests|IdentityLifecycleTests|PlatformInvitationOnboardingTests|PlatformBootstrapRecoveryTests|PlatformAdministrationTests|SessionTests"`
  - `rg -n -U "IsSuccess\s*\?[^;]*ToHttpResult\(result\.Error" src/Web/Endpoints` finds nothing.
- [x] 2.8 Gate (AD8 identical answers, before the compile unit opens): `NET-G FT`, compared with the 1.1 baseline, shows no new failure.
  - It proves all 50 migrated sites, including the endpoint classes the 2.7 filter omits: for example `SessionManagementTests`, `GoogleOidcTests`, `PlatformRetentionTests`, `PreferredLanguageTests`, `PlatformMfaRecoveryTests`, `PlatformMfaAuthenticationTests`, `PersonalJourneyTests`, `RoleAdministrationTests` and `MembershipAdministrationTests`.

## Phase 3 - Catalogue paths (Batch A)

- [x] 3.1 (plan Step 1): adjusted by D04. The `OpenApiContractTests.cs:491` path already moved at 1.3; no edit here.
- [x] 3.2 Test hardening, no production behaviour (plan Step 2, plan:286): the words gate (`:33-41`) in `src/features/identity/problemCatalogue.contract.test.js` iterates `languages.supported` from `src/i18n/languages.json`, through an `import.meta.glob` catalogue lookup. It also has one guard test, "reads every supported language, the source included, from the registry for the words gate" (accepted, A1-03).
  - The client-only-code check, the MSW guard (`:49-54`) and the catalogue checks stay.
  - Expect PASS.
- [x] 3.3 Verify (plan Step 3): `NET-F FT "OpenApiContractTests|ErrorCatalogContractTests"`; `SPA-F src/features/identity/problemCatalogue.contract.test.js src/i18n/catalog.contract.test.js`.

## Phase 4 - Offset pagination models (Batch A)

- [x] 4.1 RED (plan Step 1, E1, E2, PD-1): create `tests/Application.UnitTests/Common/Models/PaginationQueryTests.cs` (plan:336-376), plus `From(null, null)` → 1/25 and `From(2, null)` → 2/25.
  - Expected failure: `PaginationQuery` does not exist.
  - Run `NET-F UT PaginationQueryTests`.
- [x] 4.2 GREEN (plan Step 2, AD1, AD4): `src/Application/Common/Models/PaginationQuery.cs` per the design contract, with `MaxPageNumber = int.MaxValue / MaxPageSize` and `From(int?, int?)`.
- [x] 4.3 RED (plan Step 3, D03): create `PaginatedListTests.cs`. Expected failure: the type is missing. Run `NET-F UT PaginatedListTests`.
  - `new PaginatedList<int>([4, 5, 6], PageNumber: 2, PageSize: 3, TotalCount: 8)` has 3 pages and both flags true.
  - `TotalCount: 0` has 0 pages and both flags false.
  - Page 5 at size 2 over 3 rows has 2 pages and `HasNextPage` false.
- [x] 4.4 GREEN (plan Step 4): `src/Application/Common/Models/PaginatedList.cs` (plan:436-449), with no EF dependency.
- [x] 4.5 Verify (plan Step 5): `NET-F UT "PaginationQueryTests|PaginatedListTests"` passes; no refactor.

## Phase 5 - EF pagination helper (Batch A)

- [x] 5.1 (plan Step 1, AD2, AD3, D02): create `src/Infrastructure/Data/Pagination/PaginationExtensions.cs`.
  - Usings: `CleanArchitecture.Application.Common.Models` and `Microsoft.EntityFrameworkCore`.
  - It runs `CountAsync`, then `Skip`/`Take`/`ToListAsync`, with the rationale "keep query execution in Infrastructure".
  - No test file of its own (AD18); 6.3, 6.6 and 6.8 prove it on PostgreSQL.
  - Run `dotnet build src/Infrastructure/Infrastructure.csproj`.
- [x] 5.2 (plan Step 2): the helper never orders. Each caller orders by a unique key first (7.1–7.4).

## Phase 6 - Application query contracts (Batch A; opens compile unit 6–8)

Tasks 6–8 are one compile unit (D04, AD16):

- 6.1–6.6 compile against today's types and run RED at 6.7, before any production edit.
- From 6.8 until 8.4, no .NET project is expected to compile. 6.8 and 6.9 are compile-unit test edits: their RED is a recorded compile error, and their tests run green at 8.4.
- The test-double and direct store-call edits are scheduled at 6.14–6.15.

- [x] 6.1 RED (plan Task 8 Step 5, D08, L5): `FT:Platform/PlatformDirectoryContractTests.cs` gains `Every_list_route_declares_offset_parameters_and_its_binding_refusal` (plan:657-678). Expected failure: no `pageNumber`/`pageSize` (the `invalid_request` check is already green).
- [x] 6.2 RED (AD16, IA-REQ-038, PD-a, E1), in `FT:Api/ProblemDetailsContractTests.cs`. Expected failure: `nextCursor` is present and the `pageSize` member is absent.
  - A list `200` body has exactly the seven members and no `nextCursor`, `success`, `data`, `error` or `value`. The test reads `GET /api/tenants/{tenantId}/roles` as an organization owner, and `GET /api/platform/organizations` after `await PlatformScenario.ActiveOwnerAsync();`.
  - Over HTTP, after `await PlatformScenario.ActiveOwnerAsync();`, `GET /api/platform/{organizations,identities,admins,audit}?pageSize=0` answers `200` with `pageSize` 1, and `?pageSize=500` answers `200` with `pageSize` 100. Neither answers `400 validation_failed`. The test authentication handler carries that owner's MFA-proven session and Platform tenant (`TestAuthenticationHandler.cs`, `WebApiFactory.cs:117-137`).
  - Guard, passing today: a member holding only `members.read` requests `GET /api/tenants/{tenantId}/roles?pageNumber=1&pageSize=25`, and `IdentityHttpHarness.AssertProblemAsync(response, HttpStatusCode.Forbidden, "permission_denied")` passes, which checks `type`, `title`, `status`, `instance`, `code` and `traceId` (`IdentityHttpHarness.cs:75-97`).
- [x] 6.3 RED (AD16, PD-a, PD-1, E5), in `FT:Roles/RoleAdministrationTests.cs`. Expected failure: page members absent.
  - 30 roles with `?pageNumber=2&pageSize=25` return 5 items and `totalCount` 30.
  - `?pageSize=0` returns `pageSize` 1; `?pageSize=500` returns 100, never `validation_failed`.
  - The cross-tenant `404` is kept here and in `FT:Members/MembershipAdministrationTests.cs`, now requested with `pageNumber`/`pageSize` (plan:714).
- [x] 6.4 RED (D07, D15, E3, E12): `FT:Api/OpenApiContractTests.cs:567-592`. Expected failure: unbound parameters answer `200`.
  - Rename it `Every_page_parameter_binding_refusal_is_emitted_only_as_declared`.
  - Send `pageNumber=not-a-number`, `pageSize=not-a-number`, `pageNumber=2147483648` and `pageSize=2147483648` to all seven routes; each answers `400 invalid_request`.
  - After `TestApp.ResetCapturedLogs()`, no `[Error] ` entry is captured.
- [x] 6.5 RED (PD-3, D14): `tests/Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs`. Expected failure: `Query` is `PlatformDirectoryQuery`.
  - Drop the TestCase at `:41`.
  - `:130-144` asserts each List* query has exactly `Query` typed `PaginationQuery`, whose public properties are `Default`, `PageNumber`, `PageSize` and `Skip`.
  - Rename `:130` `A_directory_query_carries_only_a_bounded_limit_and_an_opaque_cursor` to `A_directory_query_carries_only_a_bounded_offset_page`.
- [x] 6.6 RED (D09, PD-1, D12): R6C in `FT:Members/AdministrationDirectoryRevalidationTests.cs:36-63` becomes an offset HTTP walk that compiles against today's types. Expected failure: the first page has 26 items (today's default is 100) and no `pageSize` member.
  - All three TestCases stay: `roles`/`roleId`, `members`/`membershipId` and `invitations`/`invitationId`.
  - `SeedDirectoryAsync` seeds so each directory holds exactly 26 rows, counting the rows the scenario already creates; `expected.Length` 26 replaces `:43`.
  - The first GET omits both parameters and returns 25 items, `pageSize` 25, `totalCount` 26 and `hasNextPage` true (PD-1).
  - `?pageNumber=2&pageSize=25` returns the last row with `hasNextPage` false. The pages do not overlap, and their union equals the seed.
  - Order: page 1 followed by page 2 at `pageSize` 25 equals the items of `?pageSize=100`, in the same order (no `ignoreOrder`).
  - The diagnostic helper `CaptureContinuationFailureAsync` (`:68-89`) keeps compiling against today's store signature until 6.14.
- [x] 6.7 Run the REDs of 6.1–6.6 and record each expected failure: `NET-F FT "PlatformDirectoryContractTests|ProblemDetailsContractTests|RoleAdministrationTests|MembershipAdministrationTests|OpenApiContractTests|AdministrationDirectoryRevalidationTests"`; `NET-F UT PlatformApplicationShapeTests`.
- [x] 6.8 Compile-unit test edit (D13, D14): rewrite `FT:Platform/PlatformDirectoryContractTests.cs`.
  - `:21-31` expects `["pageNumber","pageSize"]`; `:37-51` is renamed `…_typed_offset_page`.
  - `:54-73` also forbids both parameters on `/api/identity/*`.
  - `:75-90` becomes a `pageSize` clamp test: `(1, 10_000)` returns 4 items at size 100; `(1, 0)` returns 1 item at size 1.
  - Delete `:96-115` and `:118-127`.
  - `:134-146` reads audit pages 1 and 2 at size 2: newest first, no overlap.
  - RED by compile error: run `dotnet build tests/Application.FunctionalTests/Application.FunctionalTests.csproj`. Expected failure: compile errors only in this file, such as CS1503 (a `PaginationQuery` passed where a List* query still takes `PlatformDirectoryQuery`) and CS1061 (`PageSize` missing on `PlatformDirectoryPage<T>`). They clear at 6.10–6.13, and the tests run green at 8.4.
- [x] 6.9 Compile-unit test edit (E2, E4, E12, D13): add `Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors` and `A_page_past_the_end_is_an_empty_page_with_the_real_totals_not_a_refusal` (plan:680-711). Each calls `await PlatformScenario.ActiveOwnerAsync();` before seeding.
  - RED by compile error: the 6.8 build also fails with CS1503 on `new ListPlatformOrganizationsQuery(new PaginationQuery(…))` in both tests. Both run green at 8.4.
- [x] 6.10 GREEN (D01, AD7), in `src/Application/IdentityAccess/Platform/Queries/PlatformDirectories.cs`:
  - Delete `PlatformDirectoryQuery` and `PlatformDirectoryPage<T>`.
  - `ListPlatform{Organizations,Identities,Administrators,Audit}Query(PaginationQuery Query)` return `Result<PaginatedList<…>>`.
  - Rewrite the keyset comments at `:7-10,21`.
- [x] 6.11 GREEN (D01, D02): `Platform/Queries/PlatformDirectoryHandlers.cs` keeps `RefusalAsync<PaginatedList<T>>` before the reader. `Platform/IPlatformOperationalProjectionReader.cs` takes `(PaginationQuery, CancellationToken)` and returns `Task<PaginatedList<…Projection>>`. Add usings.
- [x] 6.12 GREEN (D01), roles; add usings.
  - `Roles/RoleRequests.cs`: `ListRolesQuery(TenantId TenantId, PaginationQuery Pagination)`; update `Roles/RoleHandlers.cs`.
  - `Roles/IRoleAdministrationStore.cs`: `ListAsync(TenantId, PaginationQuery, CancellationToken)` returns `Task<PaginatedList<RoleView>>`; delete `RolePage`.
- [x] 6.13 GREEN (D01, plan Step 2), members; add usings.
  - `Members/MembershipRequests.cs`: `ListMembersQuery` and `ListTenantInvitationsQuery`; update `MembershipHandlers.cs`.
  - `IMembershipAdministrationStore.cs`: `ListAsync` and `ListInvitationsAsync` return pages; delete `MemberPage` and `InvitationSummaryPage`.
- [x] 6.14 Test doubles and direct calls (D04, D01); add usings.
  - `FT:Lifecycle/ConcurrentDeactivationFloorTests.cs:134` and `FT:Members/IndependentDevelopmentAdministrationReviewTests.cs:216-217` take the new signatures.
  - That file's `:114` and `:180` deserialize `PaginatedList<RoleView>` and `PaginatedList<MemberView>`.
  - `FT:Members/AdministrationDirectoryRevalidationTests.cs:76-83`: the diagnostic helper's direct store calls use `new PaginationQuery(2, 25)`.
- [x] 6.15 (D06): in `FT:Platform/{PlatformProjectionTests,PlatformIdentityLifecycleTests,PlatformDirectoryAccessTests,PlatformAdministrationTests}.cs`, `new PlatformDirectoryQuery(n, null)` becomes `new PaginationQuery(1, n)`. Assertions unchanged; add usings.

## Phase 7 - Stores and readers (Batch A)

- [x] 7.1 GREEN (plan Step 1, D02, D05, AD5, AD6, PD-1), in `src/Infrastructure/IdentityAccess/RoleAdministrationStore.cs`:
  - Order by `Id`, page with `ToPaginatedListAsync`, then `new PaginatedList<RoleView>(views, page.PageNumber, page.PageSize, page.TotalCount)`.
  - Delete `MaximumLimit`, `Cursor` and the `<= 0` → 100 default.
  - Add the `CleanArchitecture.Application.Common.Models` and `CleanArchitecture.Infrastructure.Data.Pagination` usings.
- [x] 7.2 GREEN (D02, D05): `MembershipAdministrationStore.cs` re-wraps members, and invitations with the role lookup after paging. Delete `MaximumLimit`, `OpaqueCursor` and the 100 default. Add the `CleanArchitecture.Application.Common.Models` and `CleanArchitecture.Infrastructure.Data.Pagination` usings.
- [x] 7.3 GREEN (D02, D05, AD5), in `src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs`:
  - Organizations, identities and admins page by `Id` directly.
  - Audit orders by `OccurredAt DESC, Id DESC`, then re-wraps the allowlisted metadata.
  - Delete `Bounded`, `Page`, `Decode*`, `Encode*` and the `System.Text` using; rewrite `:22-25`.
  - Add the `CleanArchitecture.Application.Common.Models` and `CleanArchitecture.Infrastructure.Data.Pagination` usings.
- [x] 7.4 Review (plan Step 2, AD5): each paged query ends in its unique key from the design ordering table, and each route keeps its current order.
- [x] 7.5 (plan Step 3): adjusted by D04/AD16. The focused run cannot compile mid-unit; it runs at 8.4.

## Phase 8 - Web endpoints (Batch A; closes the compile unit)

- [x] 8.1 GREEN (plan Step 1, D02): `src/Web/Endpoints/Identity/{RoleEndpoints,MembershipEndpoints}.cs` and `Platform/PlatformEndpoints.cs` bind `int? pageNumber, int? pageSize` through `PaginationQuery.From`. Delete `PlatformEndpoints.Page()` (`:329`); add usings.
- [x] 8.2 GREEN (plan Step 2, D02): the seven page DTOs.
  - Shape: `(IReadOnlyList<TItem> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)`, each with a static `From` (plan:615-621).
  - `RolePageResponse`, `MemberPageResponse` and `InvitationSummaryPageResponse`.
  - `Platform{Organization,Identity,Administrator,Audit}DirectoryResponse` in `Platform/Contracts/PlatformDirectoryContracts.cs`; rewrite its comment at `:48-52`, and add the `CleanArchitecture.Application.Common.Models` using (the file has none today).
  - Each list endpoint answers `result.ToHttpResult(context, problems, page => Results.Ok(X.From(page, …)))`.
- [x] 8.3 (plan Step 3, D08, AD9, D15): the declarations stay as they are.
  - Keep `.WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code)` on the seven routes; no inline `InvalidRequest`.
  - The `Directory` array (`PlatformEndpoints.cs:36-46`) is unchanged; `validation_failed` stays, and the rule-10 gap is recorded.
  - No code is added, removed or renamed.
- [x] 8.4 GREEN run (plan Steps 4–5): 6.1–6.9 go green, `recent_mfa_required` is kept, and OpenAPI names stay invariant English (L5).
  - `NET-F FT "OpenApiContractTests|PlatformDirectoryContractTests|ProblemDetailsContractTests|RoleAdministrationTests|MembershipAdministrationTests|AdministrationDirectoryRevalidationTests|ErrorCatalogContractTests|PlatformDirectoryAccessTests"`
  - `NET-F UT "PlatformApplicationShapeTests|PaginationQueryTests|PaginatedListTests"`
  - Run `LIST-CODES <scratchpad>/list-problem-codes.after.json` and diff it with the 1.1 baseline: identical, including `validation_failed` on the four Platform `400` sets (D15) and `not_found` on the tenant `404` sets (8.3).
- [x] 8.5 REFACTOR (D06): drop `NextCursor` from the private row records at `FT:Roles/RoleAdministrationTests.cs:337`, `FT:Members/MembershipAdministrationTests.cs:320`, `MembershipAtomicityTests.cs:160` and `OwnershipTransferTests.cs:325`. Run `NET-F FT "MembershipAtomicityTests|OwnershipTransferTests|RoleAdministrationTests|MembershipAdministrationTests"`.
- [x] 8.6 (D24, AD17): search `rg -n "CompareTo|IComparable|operator <|operator >" src tests` for consumers of the four IDs.
  - If none remains, remove `IComparable<T>`, `CompareTo` and the four operators from `src/Domain/IdentityAccess/{Tenants/TenantId,Authorization/RoleId,Memberships/MembershipId,Invitations/InvitationId}.cs`.
  - Otherwise, or if 8.7 fails after the removal, keep them and reword the keyset comments.
- [x] 8.7 Compile-unit gates, one project per process:
  - `dotnet build CleanArchitecture.slnx`; `NET-G DT`; `NET-G UT`; `NET-G FT`; `NET-G IT`
  - `dotnet test <FT> --filter "TestCategory=IndependentDevelopmentReview"` matches the 1.1 baseline.
  - `DelegatedAdministrationRevalidationTests` passes unedited.
- [x] 8.8 (plan Step 6): adjusted. Nothing is committed inside an apply batch (CLAUDE.md, user delivery rules); see Delivery.

## Phase 8A - Identity-access SPEC amendment (Batch A)

- [x] 8A.1 RED (D16, D18): run `rg -n 'nextCursor|limit.{0,3}/.{0,3}cursor|opaque cursor' docs/features/identity-access` and the spec's reviewed-exclusion search. Record the matches; at baseline the first hits `SPEC.md:239,306-309,464-465,1033,1037,1065`. Also record `rg -n -i cursor docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md`: at baseline it matches only `:36`.
- [x] 8A.2 (D17, D18, PD-4): at `docs/features/identity-access/SPEC.md:201`, add an indented IA-REQ-038 sub-paragraph dated 2026-09-13 (PD-4). Keep the no-envelope sentence. It states:
  - the offset page DTO is not an envelope;
  - default 25, `pageSize` 1–100, clamped;
  - a non-integer or beyond-Int32 value is `400 invalid_request`;
  - a page past the end is `200` with empty `items`.
- [x] 8A.3 (D16): IA-REQ-045 (`SPEC.md:239`) moves to `pageNumber`/`pageSize` and the seven members. It keeps the `/api/identity/*` no-envelope sentence and uses no `limit`, `cursor` or `nextCursor`.
- [x] 8A.4 (D18): edit `SPEC.md:306-309` per the spec's row table (`recent_mfa_required` only on `:306-307`; `:308` untyped with offset metadata), and the scenario at `:464-465`.
- [x] 8A.5 (D09, D17, D18): the remaining rows and the C5 history.
  - `:1033` moves to offset and keeps the detail-route `404`; `:1037` moves to offset with no `404` and no default clause.
  - `:1065` text stays, followed by a dated 2026-09-13 C5-history note pointing to IA-REQ-038 and IA-REQ-045.
  - `:3` and `:695` stay unchanged.
- [x] 8A.6 (D07, D17, D18), in `docs/features/identity-access/TRACEABILITY.md`:
  - `:86` cites `Every_page_parameter_binding_refusal_is_emitted_only_as_declared` and the seven routes.
  - `:92` moves to offset wording and drops the `usePlatformRead.test.jsx` and "More accounts" citations.
  - `:99` drops "bounded/cursor DTOs" and "limits, cursors".
  - Add a dated evidence paragraph; `:18` and `:158` stay unchanged.
- [x] 8A.7 (PD-4): decision 17 in `docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md:36` names typed bounded offset page responses. The status stays Proposed.
- [x] 8A.8 GREEN (plan Step 2, D18, PD-4):
  - The first 8A.1 search returns only `SPEC.md:1065`, the C5 history D17/PD-4 keep (see Design corrections).
  - The reviewed-exclusion search shows only `SPEC.md:695`, `TASKS.md:936,940` and the C5 history with its note.
  - `rg -n -i cursor docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` finds nothing, and `rg -n '^\*\*Status:\*\* Proposed' docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` still matches `:3` (spec "ADR decision 17 names offset pages").

## Phase 9 - Client pagination helper (Batch B)

- [x] 9.1 RED (plan Step 1, D10, AD12, PD-1): create `src/api/pagination.test.js`. Expected failure: the module is missing. Run `SPA-F src/api/pagination.test.js`.
  - `paginationSearch({ pageNumber: 0, pageSize: 500 })` gives `pageNumber=1&pageSize=100`.
  - `paginationSearch({ pageNumber: 1, pageSize: 0 })` and `paginationSearch({ pageSize: -5 })` each give `pageNumber=1&pageSize=1`.
  - `{ pageNumber: 'abc' }`, `null` and `undefined` give `pageNumber=1&pageSize=25`.
  - `paginationMembers` equals the seven names.
- [x] 9.2 GREEN (plan Step 2): `src/api/pagination.js`, using constants rather than literals.
  - Exports: `paginationMembers`, `DEFAULT_PAGE_SIZE`, `MAX_PAGE_SIZE`, `MAX_WALK_PAGES = 100`, `pageSizeOptions`, `DEFAULT_PAGE`, `paginationSearch`.
  - `boundedPage` is null-safe (AD12). An integer `pageSize` is clamped to 1–100, so 0 and negatives give 1. An integer `pageNumber` is clamped to at least 1. Only a missing or non-integer member takes its `DEFAULT_PAGE` value.
  - Do not copy the plan snippet's `|| 25` fallback (plan:814-818), which turns a `pageSize` of 0 into 25.
- [x] 9.3 RED (plan Step 3, E7, L5): `readPage` refuses each of these with `/offset pagination contract/`, and returns a valid page unchanged. Expected failure: not exported. Run `SPA-F src/api/pagination.test.js`.
  - a missing total;
  - a fractional page;
  - `pageSize` 500;
  - string flags;
  - `pageNumber` 0;
  - a negative `totalPages`.
- [x] 9.4 GREEN: `readPage` (plan:842-850) in `src/api/pagination.js`, with an English developer message.
- [x] 9.5 Guard (plan Step 3, E7): `src/api/problemDetails.test.js` adds the retired-cursor refusal (plan:873-876). Expect PASS: `nextCursor` stays in `FORBIDDEN_SUCCESS_KEYS`, and `src/api/problemDetails.js` is not edited.
- [x] 9.6 RED (AD11, E7): `sendPage` with a mocked `send`. Expected failure: `sendPage` is not exported. Run `SPA-F src/api/pagination.test.js`.
  - It sends `?pageNumber=1&pageSize=25` with `{ expect: paginationMembers, signal }`.
  - A malformed body rejects as `ClientFailure` `{ code: 'unreadable_response', status: 0 }`.
  - `ApiProblem` and `ClientFailure` pass through unchanged.
- [x] 9.7 GREEN: `sendPage(send, path, page, { signal } = {})` in `src/api/pagination.js`, importing `ClientFailure` from `./apiTransport`.
- [x] 9.8 RED (AD13, PD-2): `readEveryPage`. Expected failure: `readEveryPage` is not exported. Run `SPA-F src/api/pagination.test.js`.
  - 30 rows served 25 per page: 2 reads at `pageSize` 100, deduplicated by key, and none after `hasNextPage` is false.
  - 3 rows: 1 read.
  - `hasNextPage` always true: exactly 100 reads, then `unreadable_response`.
  - The `signal` is forwarded.
- [x] 9.9 GREEN: `readEveryPage(readOne, keyOf, { signal } = {})` in `src/api/pagination.js`.
- [x] 9.10 Verify: `SPA-F src/api/pagination.test.js src/api/problemDetails.test.js`; `cd src/Web/ClientApp && npx eslint src/api/`.

## Phase 10 - SPA clients and useRead (Batch B)

- [x] 10.1 RED (D10, D21, E7): `src/features/identity/api/identityClient.test.js`, keeping its mocked-`send` style. Expected failure: offset URLs and `listRoleCatalogue` absent. Run `SPA-F src/features/identity/api/identityClient.test.js`.
  - `listRoles`, `listMembers` and `listTenantInvitations(tenantId, page, options)` build offset URLs; a `null` page sends `pageNumber=1&pageSize=25`.
  - A body with `pageNumber` 0 rejects as `unreadable_response` with status 0.
  - `listRoleCatalogue` walks until `hasNextPage` is false.
  - The body at `:7` becomes a full page.
- [x] 10.2 GREEN (plan Step 2, PD-2): `src/features/identity/api/identityClient.js`.
  - The three list methods use `sendPage`.
  - `listRoleCatalogue(tenantId, options)` wraps `readEveryPage` around `listRoles`, keyed by `roleId`.
  - Delete `continued` and its comment.
- [x] 10.3 RED (plan:920, D21): `src/features/platform/platformClient.test.js`. Expected failure: `limit` is sent. Run `SPA-F src/features/platform/platformClient.test.js`.
  - `list{Organizations,Identities,Administrators,Audit}(page, options)` send a bounded `pageNumber`/`pageSize` and pass `signal`.
  - A malformed page, and `{ items: [], nextCursor: null }`, each reject as `unreadable_response`.
- [x] 10.4 RED: `src/features/platform/platformDirectory.test.js` uses full-page fixtures, expects requests by `pageNumber`/`pageSize`, and has no `limit` or `cursor` assertion. Expected failure: the client still sends `limit` and no `pageNumber`. Run `SPA-F src/features/platform/platformDirectory.test.js`.
- [x] 10.5 GREEN (plan Steps 1–2): `src/features/platform/api/platformClient.js`: the four methods use `sendPage`. Delete `DIRECTORY`, the limit constants and `page()`.
- [x] 10.6 RED (plan Step 1b, E8, E10): `src/features/identity/useRead.test.jsx`. Expected failure: `load` receives `{ cursor, signal }` (`useRead.js:43`). Run `SPA-F src/features/identity/useRead.test.jsx`.
  - `cursor` becomes `page`, and `load` receives `{ page, signal }`.
  - The four-state, abort and stale-generation tests stay; delete only the merge test.
- [x] 10.7 GREEN: `src/features/identity/useRead.js`.
  - `refresh(page)` calls `lifecycle.load({ page, signal })`; delete the merge branch.
  - `errored` keeps the data, `refused` clears it, and the hook never rethrows.
- [x] 10.8 Verify (plan Step 3): `SPA-F src/features/identity/api/identityClient.test.js src/features/platform/platformClient.test.js src/features/platform/platformDirectory.test.js src/api/pagination.test.js src/features/identity/useRead.test.jsx`. `SPA-G` is not expected green until 11A.11, because the screen fixtures are still cursor-shaped.

## Phase 11 - SPA pagination UI (Batch C)

Every screen follows these rules:

- MUI is imported directly, and `sx` is for layout only.
- `id`, `data-testid`, role, heading, `disabled` and `en` copy stay unchanged.
- No MUI label props (L1) and no new text (L4).
- `TablePagination` reads the loaded `pageNumber - 1` and `pageSize`, and renders only when `totalCount > 0` (E9).
- Every paged collection gets page state per plan:990-997: a page-independent `load`, plus `requested`, `go` and `retry` calling `refresh(requested)`.

- [x] 11.1 RED (plan Step 4, D21, AD14): `src/features/identity/roles/RolesPage.test.jsx`, with fixtures from plan:1066-1071. Expected failure: no "Go to next page" button renders (the screen still shows "Show more roles"). Run `SPA-F src/features/identity/roles/RolesPage.test.jsx`.
  - Next and previous send `pageNumber ± 1`; 50 rows per page sends `pageNumber=1&pageSize=50` and shows "1–50 of 60".
  - 26 roles reach "26–26 of 26"; a single page disables both buttons; `totalCount` 0 shows the empty state and no control.
  - The permission catalogue is requested once across two page changes.
  - A failed catalogue read shows "We could not reach the service." and no rows, and "Try again" repeats only the catalogue.
  - Delete `:195` ("keeps listed roles and places a failed continuation beside Show more"); 11A.1 replaces it with the offset page-change failure.
- [x] 11.2 GREEN (plan Steps 1–2, AD14): `RolesPage.jsx`.
  - Page state: a page-independent `load`, plus `requested`, `go` and `retry` (plan:990-997).
  - A catalogue `useRead` loads once per tenant; skeleton while either read has no data; the table renders only when both have data.
  - After a successful create, edit or retire, the post-mutation refresh (`:169`) refreshes both reads, the roles page and the catalogue's `refresh()`, as today (AD14). A failed catalogue refresh renders as a read problem, and the mutation is never reported as failed (E11). 11A.7 sets the page argument.
  - One `ProblemMessage` at `:394`; `TablePagination` per plan:1007-1017.
  - Remove `roles.showMore`. The `toProblem(error)` count stays 2.
- [x] 11.3 RED (PD-2, D10): `src/features/identity/members/MembersPage.test.jsx`. Expected failure: no "Go to next page" button, and the role editor offers only the first 25 roles (the picker reads one page, `MembersPage.jsx:133`). Run `SPA-F src/features/identity/members/MembersPage.test.jsx`.
  - Navigation reaches the 26th member.
  - The role editor offers all 30 roles when they are served 25 per page: page 2 is requested, and nothing after it.
  - 3 roles take one request.
- [x] 11.4 GREEN: `MembersPage.jsx` gets page state (`requested`, `go`, `retry`) and `TablePagination`.
  - The picker load at `:131-137` becomes `identity.client.listRoleCatalogue(tenantId, { signal })`.
  - A failure renders in the role editor (`:451-467`).
  - Remove `members.showMore`.
- [x] 11.5 RED (AD13, AD15, PD-2): `src/features/identity/invitations/InviteMemberPage.test.jsx`. Expected failure: no "Go to next page" button, and only the first 25 roles are offered (the picker reads one page, `InviteMemberPage.jsx:114`). Run `SPA-F src/features/identity/invitations/InviteMemberPage.test.jsx`.
  - Navigation reaches the 26th invitation, and 30 roles are offered.
  - `hasNextPage` always true: exactly 100 role requests, then `unreadable_response` with "Try again" and no role.
  - A network error on page 2 shows the fieldset alert "We could not reach the service."; "Try again" requests page 1.
  - Rewrite `:247` ("keeps stale invitations and the retry beside Show more when pagination fails") as an offset page-change failure. Page 2 of 26 invitations answers `500` with a trace id. The page-1 invitations stay, and the alert "Something went wrong. Try again." and "Try again" render beside `TablePagination` (AD15). "Try again" requests `pageNumber=2`.
- [x] 11.6 GREEN: `InviteMemberPage.jsx` gets page state (`requested`, `go`, `retry`) and `TablePagination` for invitations.
  - The `'pagination'` retry (`:472`) calls `retry`.
  - The picker at `:113-116` uses `listRoleCatalogue`.
  - A failure renders in the roles fieldset (`:267-290`), and `permission_denied` keeps its sentence.
  - Remove `invitations.member.showMore`.
- [x] 11.7 RED (PD-6, D21): `src/features/platform/PlatformPanel.test.jsx`. Expected failure: no "Go to next page" button on the three directories, and "Try again" requests page 1 (`PlatformPanel.jsx:255`). Run `SPA-F src/features/platform/PlatformPanel.test.jsx`.
  - Organizations, administrators and audit each move to `pageNumber=2` and show rows 26–30.
  - Deliberate rewrite of `:425-456`: page 2 answers `500` with a trace id.
  - The alert shows "Something went wrong. Try again." and "Reference: <traceId>", the page-1 rows stay, and "Try again" requests page 2.
- [x] 11.8 GREEN (PD-6, AD15, plan:990-997): `src/features/platform/PlatformPanel.jsx`.
  - Organizations, administrators and audit each get page state: a page-independent `load`, plus `requested`, `go` and `retry` calling `refresh(requested)`.
  - The retry buttons at `:255`, `:366` and `:493` call their collection's `retry`, so 11.7's `:425-456` rewrite passes at 11.15.
  - Add three `TablePagination`s and keep the single section problem block. Remove `organizations.more` (`:358`).
- [x] 11.9 RED: `src/features/platform/identities/PlatformIdentitiesPage.test.jsx` uses full-page fixtures and tests next, previous and rows-per-page navigation. Expected failure: no "Go to next page" button (the screen still shows "More accounts"). Run `SPA-F src/features/platform/identities/PlatformIdentitiesPage.test.jsx`.
- [x] 11.10 GREEN (D23, PD-7): `PlatformIdentitiesPage.jsx` renames its `data: page` binding to `directory`, gets page state (`requested`, `go`, `retry`) and adds `TablePagination`. Remove `identities.more` (`:550`); keep `platform:identities.retry` (`:445`).
- [x] 11.11 (plan Step 4): `src/AppRoutes.test.jsx` changes fixtures only (it has failed since 10.5, as drift); `AppRoutes.jsx` is untouched. Run `SPA-F src/AppRoutes.test.jsx`.
- [x] 11.12 Guard (L1, L8, D22): `src/i18n/spanish.test.jsx` adds only the `esES` `getItemAriaLabel('next')` assertion, with the `ThemeProvider` and `TablePagination` imports. Expect PASS.
- [x] 11.13 RED→GREEN (plan Step 3, L3):
  - `cd src/Web/ClientApp && npm run i18n:unused` fails on the five `en` keys.
  - Delete them from `src/i18n/locales/{en,es}/platform.json` (`organizations.more`, `identities.more`) and `{en,es}/identity.json` (`members.showMore`, `invitations.member.showMore`, `roles.showMore`).
  - Keep `identities.retry`, add no key, and rerun: it passes.
- [x] 11.14 (D22): `src/i18n/presentation.test.jsx` gets the `pageOf` fixture at `:25`; delete the test at `:116-119`.
- [x] 11.15 Verify (plan Steps 3–4):
  - `SPA-F src/i18n/spanish.test.jsx src/i18n/catalog.contract.test.js src/i18n/presentation.test.jsx src/App.localization.test.jsx src/features/identity/roles/RolesPage.test.jsx src/features/identity/members/MembersPage.test.jsx src/features/identity/invitations/InviteMemberPage.test.jsx src/features/platform/PlatformPanel.test.jsx src/features/platform/identities/PlatformIdentitiesPage.test.jsx src/features/identity/useRead.test.jsx`
  - `rg -n "labelRowsPerPage|labelDisplayedRows|getItemAriaLabel" src/Web/ClientApp/src --glob '!**/*.test.*'` finds no prop.

## Phase 11A - Pagination error and edge states (Batch C)

- [x] 11A.1 RED (plan Step 1, E4, E8–E10): `RolesPage.test.jsx` gains plan:1094-1167, with exact `en` words. Run `SPA-F src/features/identity/roles/RolesPage.test.jsx`. Expected failure (plan:1176, "FAIL until Steps 2–4 land"): at least the past-the-end test fails, because the requests stay `[1]` and "26–30 of 30" never shows before 11A.6.
  - A failed next page keeps the rows and "1–25 of 30"; "Try again" requests page 2.
  - A `403` clears the rows and offers no retry.
  - A stale page 3 is corrected: requests are `[1, 2]`, showing "26–30 of 30".
  - Outrun clicks keep "1–50 of 100".
  - `totalCount` 0 takes one request and shows the empty state.
- [x] 11A.2 RED (plan Step 1): mirror 11A.1 in `PlatformIdentitiesPage.test.jsx`; the retry button is still named "Try again" (`platform:identities.retry`). Run `SPA-F src/features/platform/identities/PlatformIdentitiesPage.test.jsx`. Expected failure: at least the mirrored past-the-end test, as in 11A.1.
- [x] 11A.3 RED (E11): `RolesPage.test.jsx`. Page 2 is loaded, a retirement succeeds, and the page-2 refresh answers `500`. Run `SPA-F src/features/identity/roles/RolesPage.test.jsx`. Expected failure: the refresh after the retirement requests `pageNumber=1`, because `run` still calls `read.refresh(undefined)` (`RolesPage.jsx:169`).
  - RolesPage shows no confirmation after a retirement, so E11 is proven by one accepted retirement (one retirement request, the password field cleared), the read alert with its reference as the only alert, and no message saying the retirement failed (decision C3-V01 option a, user, 2026-09-14; plan:75 E11; the plan:1219 confirmation clause assumed a confirmation no paged screen has).
  - The refresh requested `pageNumber=2`.
- [x] 11A.4 GREEN (plan Step 2, AD15): read states in `RolesPage.jsx`, `MembersPage.jsx` and `InviteMemberPage.jsx`.
  - The skeleton shows only while `loading` with no data.
  - `errored` shows `ProblemMessage` and an outlined `common:actions.tryAgain` button calling `refresh(requested)`.
  - `refused` hides the rows and the control.
  - A refused page change still renders the `'pagination'` problem block without data (`RolesPage.jsx:432`).
  - Proof: RolesPage by 11A.1; InviteMemberPage's page-change failure placement by the 11.5 `:247` rewrite. MembersPage's read states, and InviteMemberPage's skeleton and refused states, have no RED of their own: they rely on the plan-scoped RolesPage (11A.1) and PlatformIdentitiesPage (11A.2) proofs (plan:1087,1091).
- [x] 11A.5 GREEN (plan Step 2, D23, D24): the same states in `PlatformPanel.jsx` and `PlatformIdentitiesPage.jsx`, which keeps `platform:identities.retry`. `PlatformRetentionPage.jsx` is dropped: it has no paginated read.
  - Proof: PlatformIdentitiesPage by 11A.2; PlatformPanel's retry by 11.7. PlatformPanel's other read states rely on the 11A.1 and 11A.2 proofs.
- [x] 11A.6 GREEN (plan Step 3, E4, L4): the past-the-end effect (plan:1204-1213) on the five screens. It runs once per answer, only when `items` is empty, `totalPages > 0` and `pageNumber > totalPages`, and adds no text.
  - The plan snippet's `// eslint-disable-next-line react-hooks/exhaustive-deps` (plan:1211) follows the existing precedent (`RolesPage.jsx:236,327`, `MembersPage.jsx:216`). No `i18next` suppression is added (spec "Localization lint stays clean").
  - Proof: RolesPage (11A.1) and PlatformIdentitiesPage (11A.2). MembersPage, InviteMemberPage and PlatformPanel rely on those proofs (plan:1087,1091).
- [x] 11A.7 GREEN (plan Step 4, E11): after a successful retire, revoke, reissue or status change, call `read.refresh({ pageNumber: read.data.pageNumber, pageSize: read.data.pageSize })`, or `DEFAULT_PAGE` when nothing has loaded.
  - RolesPage also calls its catalogue read's `refresh()` after a successful create, edit or retire (AD14; `grantable` depends on the acting user). MembersPage keeps refreshing its role catalogue beside the roster (`:153`).
  - A failed refresh of any read renders as a read problem and never reports the mutation as failed (E11).
  - Proof: RolesPage by 11A.3. The refreshes after revoke, reissue and status change on the other screens rely on that proof.
- [x] 11A.8 RED (D11, D12): `src/features/identity/ExternalProofResume.test.jsx`, fast, with no timeout raised. Run `SPA-F src/features/identity/ExternalProofResume.test.jsx`. Expected failure: the page-2 target is never requested or resumed, and the failed walk shows no alert, because the walks (`RolesPage.jsx:204-231`, `MembersPage.jsx:190-217`) still follow `nextCursor`, which offset pages never carry.
  - Recorded reads, continuation clicks and `limit`/`CURSOR` expectations move to `pageNumber` requests over 26 rows.
  - Every resumed-or-refused assertion stays.
  - Add a failed page-2 walk: the alert "We could not reach the service." shows and nothing is executed.
- [x] 11A.9 GREEN (D11, design flow c): the walks in `RolesPage.jsx:204-231` and `MembersPage.jsx:190-217`.
  - Walk `next = 1..L.totalPages`, skipping `L.pageNumber`, at `L.pageSize`.
  - Check `cancelled` after each request (`:217`/`:196`), before `forget()` (`:221`/`:200`) and in the `catch` (`:230`/`:210`).
  - A failure goes to `setActionProblem(toProblem(error))`; the effect dependencies become `L.pageNumber`/`L.totalPages`.
- [x] 11A.10 Rewrite (D09, D12): R6C in `src/features/identity/IdentityAccessReviewRevalidation.test.jsx` becomes a `pageNumber` walk over 26 rows at the default 25, with no `limit`, `CURSOR` or "Show more". Keep the revalidation assertions, fast, with no timeout raised.
  - The rewrite also covers the file's shared list helper `pageResponse` (`:36`), behind `rolesAre`, `membersAre`, `invitationsAre` and R6B's roster read (`:165`). It answers a full offset page with the seven page members (finding B2-V03).
  - R6A and R6B exercise no walk. They change only through that helper, and every R6A and R6B assertion stays. They have been red since 10.2, because the strict reader refuses `{ items, nextCursor }`: alone, the file fails 9 of 11 (R6A 5, R6B 1, R6C 3; the two session revocations read no list). All 11 pass at 11A.11.
- [x] 11A.11 Verify (plan Step 5):
  - `SPA-F` on the RolesPage, MembersPage, InviteMemberPage, PlatformPanel, PlatformIdentitiesPage, ExternalProofResume, IdentityAccessReviewRevalidation, useRead and spanish test files.
  - `SPA-G`, covering batches B and C together, under the baseline-timeout rule.
  - The `identityFiles.contract.test.js` counts stay at 2, 2, 1 and a total of 12.
  - `rg -n "eslint-disable" src/Web/ClientApp/src`, compared with the 1.1 record: the only added lines are `// eslint-disable-next-line react-hooks/exhaustive-deps` on the 11A.6 effects. No `i18next` suppression is added, and moved files keep their own.
- [x] 11A.12 (plan Step 6): adjusted. No commit here; see Delivery.

## Phase 12 - Search cleanup (Batch D)

- [x] 12.1 (plan Step 1, D24): no cursor-pagination match may remain outside the reviewed exclusions below. They are guards: never remove or reword them to empty the search.
  - `src/Web/ClientApp/src/api/problemDetails.js` `FORBIDDEN_SUCCESS_KEYS` (`nextCursor` stays forbidden; PD-b, E7), and the `nextCursor` assertions in `src/api/problemDetails.test.js`, including the 9.5 retired-cursor refusal;
  - the 10.3 `{ items: [], nextCursor: null }` drift test in `platformClient.test.js`, and any E7 drift fixture in `identityClient.test.js` or `pagination.test.js`;
  - the `limit`/`cursor` absence assertions in `PlatformDirectoryContractTests.cs` (6.1; 6.8 `:54-73`) and the whole 6.2 page-body test in `ProblemDetailsContractTests.cs`: its summary (`:381`), its name `A_list_success_is_its_offset_page_dto_with_no_cursor_or_envelope_member` (`:384`) and its no-`nextCursor` assertion inside `AssertOffsetPageBody` (`:437`) (decision D1-V01/D1-V02 option a, user, 2026-09-14);
  - `StackBaselineTests.cs:338-354`, where `cursor` is a string index;
  - `src/Web/ClientApp/src/web-api-client.ts`, which is gitignored and generated by NSwag.

  The searches:
  - `rg -n -i 'nextCursor|cursor|PlatformDirectoryQuery|PlatformDirectoryPage|OpaqueCursor|BoundedLimit|MaximumLimit|MinimumLimit' src tests --glob '!**/node_modules/**' --glob '!src/Web/wwwroot/openapi/**' --glob '!**/package-lock.json' --glob '!**/*.scss'`
  - `rg -n '\blimit\b' src/Web/Endpoints src/Web/ClientApp/src/features src/Application/IdentityAccess src/Infrastructure/IdentityAccess src/Infrastructure/Platform` shows rate limits only.
- [x] 12.2 RED (D19, D20): run the three design searches (design "Searches") and record the `.agents/skills` matches (`SKILL.md:109`; `error-handling-rules.md:288,328,400,476,594`).
  - The four retired paths in `src/Web/ClientApp/src` and `.agents/skills`.
  - `identity/fieldErrors` outside `features/identity`.
  - `features/identity/fieldErrors` in `.agents/skills`.
- [x] 12.3 (D20, PD-5): point `.agents/skills/error-handling-standards/SKILL.md:109` and `references/error-handling-rules.md:288,328,400,476` at `src/api/problemCodes.json` and `src/api/apiTransport.js`, and `:594` at `src/components/problemFields.js`. Leave `:362`, `:387-389` and `:823` as follow-ups.
- [x] 12.4 (D20): `.agents/skills/frontend-design-standards/references/ui-composition-rules.md:317` keeps real offset `TablePagination` controls and still forbids invented pagination. Re-glob `.claude/skills` and `.codex/skills` for mirrors (none at sdd-tasks).
- [x] 12.5 GREEN (plan Step 2): the three 12.2 searches and `rg -n -i cursor .agents/skills/frontend-design-standards/references/ui-composition-rules.md` all find nothing.
- [x] 12.6 (plan Step 3): run `rg -n "success|succeeded|data|error|value" src/Web/Endpoints src/Web/Infrastructure tests/Application.FunctionalTests` and inspect; no universal envelope may exist.
- [x] 12.7 (plan Step 4, E5, E6, L7):
  - `git show 2716aa6:src/Web/ClientApp/src/features/identity/problemCodes.json` equals `src/Web/ClientApp/src/api/problemCodes.json`.
  - `git diff --stat -- src/Web/ClientApp/src/i18n/locales/en/errors.json src/Web/ClientApp/src/i18n/locales/es/errors.json src/Application/IdentityAccess/Common/IdentityAccessErrors.cs "*.resx"` shows nothing.
  - `rg -n 'invalid_page|invalid_page_size|invalid_cursor|page_out_of_range' src tests --glob '!**/node_modules/**'` finds nothing.
- [x] 12.8 (plan Step 5, L3, D22):
  - `rg -n "organizations\.more|identities\.more|members\.showMore|invitations\.member\.showMore|roles\.showMore" src/Web/ClientApp/src` finds nothing.
  - `cd src/Web/ClientApp && npm run i18n:unused` passes; `SPA-F src/i18n/catalog.contract.test.js` passes.
  - The locales diff shows only the five deletions.
- [x] 12.9 Gate: `SPA-G`, under the baseline-timeout rule.

## Phase 13 - sdd-verify gates (run by sdd-verify; listed without checkboxes, user decision 2026-09-14)

These steps are not apply tasks. sdd-verify runs 13.1-13.8 once and records their evidence in `verify-report.md`. They carry no checkbox, so that native verification can start once every apply task is complete (user decision 2026-09-14).

- 13.1 Prerequisites:
  - Docker is running and no AppHost is running.
  - Playwright Chromium: `artifacts/bin/Web.AcceptanceTests/<configuration>/playwright.ps1 install chromium`.
  - Regenerate `src/Web/wwwroot/openapi/v1.json` with `dotnet build src/Web/Web.csproj`, WITHOUT `-p:OpenApiGenerateDocumentsOnBuild=false`; it shows `pageNumber` and no `nextCursor`.
- 13.2 `SPA-G` (plan Steps 3, 4, 6): every test passes, including both rewritten slow tests; only the entries under 15 s in the 1.1 record follow the baseline-timeout rule. Lint is clean, with no new `i18next` suppression: `rg -n "eslint-disable" src/Web/ClientApp/src` matches the 11A.11 result.
- 13.3 `dotnet build`, as in plan Step 5: `dotnet build CleanArchitecture.slnx -v minimal`.
- 13.4 (plan Steps 1, 2, 7), one project per process:
  - `NET-G DT`; `NET-G UT`; `NET-G FT`; `NET-G IT`.
  - `dotnet test <IT> --filter "TestCategory=IndependentDevelopmentReview"` shows exactly the 3 intentional failures, reported as baseline.
  - The FT category result is compared with 1.1.
- 13.5 (plan Step 8): `dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`.
  - Every journey passes, including the Spanish smoke.
  - A failure is fixed in the implementation, never in a page object.
  - Without Docker, report the journeys as not run and do not commit.
- 13.6 Test-file boundary:
  - `git status --short --untracked-files=all -- tests src/Web/ClientApp/src` lists every changed file on its own line, including the files inside the new `src/api/` folder. Compare each entry with declared rows 1–41: only declared test files change among tests, the moved-from paths of rows 8, 17 and 18 show ` D`, and `DelegatedAdministrationRevalidationTests.cs` is unchanged.
  - The plan's `git status --short -- tests` lists only declared files.
- 13.7 Searches and snapshots:
  - Rerun 8A.8 (Task 8A, the TRACEABILITY check and the ADR-004 checks, D18, PD-4), 12.1, 12.5, 12.7, 12.8 and the 11.15 label-prop search.
  - Run `LIST-CODES <scratchpad>/list-problem-codes.verify.json` against the document regenerated at 13.1, and diff it with the 1.1 baseline: identical.
- 13.8 Delivery note (plan Step 9). It covers:
  - each error path's code, status, where it is shown and its test (E1–E12);
  - AD8's neutral `202`s, byte-identical; no neutral route is paginated;
  - the `validation_failed` rule-10 gap;
  - the SPEC rows amended;
  - the E11 confirmation clause amended by decision C3-V01 (option a, user, 2026-09-14): 11A.3 and the spa-pagination-ui requirement "Mutation outcome survives a failed refresh" keep a `role="status"` confirmation only where a screen shows one, because no paged screen confirms a retire, revoke, reissue or status change;
  - keys removed (five, in `en` and `es`), none added, and the MUI locale;
  - translations awaiting native review: none, because no new Spanish copy is added;
  - the gates run and their results, including every use of the baseline-timeout rule and of its A1-01 exception. The ExternalProofResume roles-target test timed out alone at `2716aa6` and was excused in the intermediate gates until 11A.8 rewrote it;
  - the walk-failure test waits per journey step (up to three 1,000 ms steps; no timeout configuration changed), per decision D1-V04 (user, 2026-09-14). In `ExternalProofResume.test.jsx` (row 4), "reports a %s search that cannot reach the next page, and executes nothing" waits for the returned screen's heading, then the page-2 read after return, then the alert, where one 1,000 ms wait used to cover the whole return journey;
  - anything unverified: at least the neutral bodyless `202` of the Platform administrator invitation (`PlatformEndpoints.cs:309`), which has no HTTP-level neutral test and is proven only by the journeys (design Risks), plus any gate that could not run;
  - the user-visible changes: D09/PD-1 (tenant default 100 → 25; `pageSize <= 0` → 1), PD-6 and PD-2;
  - follow-ups: `docs/features/whatsapp-bot/SPEC.md:295,304`, `SCREENS.md:53`, `error-handling-rules.md:362,387-389,823` (the three places 12.3 leaves).

## Phase Delivery - Commit and push (orchestrator, after sdd-verify PASS; listed without checkboxes, user decision 2026-09-14)

The orchestrator runs DL.1-DL.6 only after sdd-verify passes, and records the outcome in the archive report. The post-archive `docs(openspec)` commit follows, as recorded under Open decisions.

- DL.1 Start only after sdd-verify passes with every Phase 13 gate run and green, and with DL.0 settled. Work directly on `main`: no branch, no PR (CLAUDE.md).
- DL.2 Run the Native Checking Contract and route only from `next_transition`: `gentle-ai review status --cwd C:/Users/ezequ/source/repos/RepositorioBase --contract gentle-ai.review-integration/v2 --agent codex --next-transition`.
- DL.3 If DL.2's single scoped correction (AGENTS.md "Native Checking Contract") changed any byte, rerun on the corrected bytes before staging:
  - `SPA-G` if anything under `src/Web/ClientApp` changed;
  - `NET-G` for each touched .NET project, one per process;
  - 13.5 if API or SPA code changed;
  - 13.6.

  Stage nothing until they pass. If nothing changed, record that and continue.
- DL.4 Stage only this change's paths, one explicit file path at a time; never use a directory pathspec such as `src/` or `.agents/`.
  - Include: the design.md "File Changes" paths, with both sides of each move so the deletions are staged; the declared test rows that changed; the three `docs/` files; `.agents/skills/error-handling-standards/SKILL.md`, `.agents/skills/error-handling-standards/references/error-handling-rules.md` and `.agents/skills/frontend-design-standards/references/ui-composition-rules.md`.
  - `openspec/` only as recorded at DL.0.
  - Never stage `AGENTS.md`, `CLAUDE.md` or `.agents/skills/engineering-standards/*`.
  - Compare `git diff --cached --name-only` with that list, and stop and report if any other path appears.
- DL.5 Write conventional commits at the granularity recorded at DL.0, with NO `Co-Authored-By` trailer and no AI attribution.
- DL.6 Push to `origin/main` only when DL.1's verification, and DL.3's when it ran, is green for the committed bytes. A–C land together (E7 coupling); never force-push.

## Spec coverage (requirement → RED or guard tasks)

| Spec | Requirement | Tasks |
|------|-------------|-------|
| api-offset-pagination | Offset page parameters | 6.1, 6.3 |
| | Offset page response | 4.3, 6.2 |
| | Defaults and clamping within Int32 | 4.1 (`From`, the binding defaults), 6.2 (Platform `pageSize` 0 and 500 over HTTP), 6.3, 6.6, 6.8, 6.9 |
| | Past-the-end page | 4.3, 6.9 (handler level, as plan:680-711; the HTTP body is the 8.2 DTO `From`); `404` kept at 6.3 |
| | Deterministic page order | 6.6 (set and order), 6.8 |
| | Cursor pagination retired | 6.5; search 12.1 |
| | Section 4 states the offset contract | 8A.1 → 8A.8 |
| | Route rows carry the offset shape | 8A.1 → 8A.8 |
| | Evidence and ADR follow the amendment | 8A.1 → 8A.8 (ADR-004 checks at 8A.8, rerun at 13.7); 6.4 |
| list-read-error-contract | Paging parameter binding refusal | 6.4, 6.1 |
| | Declared and emitted codes are unchanged | `LIST-CODES` snapshot at 1.1, diffed at 8.4 and 13.7 (every status, `validation_failed` included); 6.1; 6.2 (Platform `pageSize` 0 and 500 never `validation_failed`); 6.3; exact pins `OpenApiContractTests.cs:168-177` (Platform `401`) and `:368-387` (tenant `404`); guards `ErrorCatalogContractTests`, `PlatformDirectoryAccessTests` (8.4) |
| | Pagination adds no error code | 12.7; guard `problemCatalogue.contract.test.js:49-54` (3.3) |
| | Expected paging outcomes are not errors | 6.9, 6.4 |
| | Strict client page reader | 9.3, 9.5 (guard), 9.6, 10.1, 10.3 |
| | Null-safe bounded paging arguments | 9.1 (including `pageSize` 0 and -5), 10.1 |
| spa-pagination-ui | Page control on every collection | 11.1, 11.3, 11.5, 11.7, 11.9 |
| | Read states per page | 11A.1, 11A.2, 11.7, 11.5 (`:247`); MembersPage and the remaining InviteMemberPage and PlatformPanel states rely on 11A.1 and 11A.2 (11A.4, 11A.5) |
| | Past-the-end correction | 11A.1, 11A.2; MembersPage, InviteMemberPage and PlatformPanel rely on them (11A.6) |
| | Mutation outcome survives a failed refresh | 11A.3; the other screens rely on it (11A.7) |
| | Bounded resume walks | 11A.8, 11A.10 |
| | Role pickers read the whole catalogue | 9.8, 11.3, 11.5 |
| | Pagination labels come from the MUI locale | guard 11.12; guard `spanish.test.jsx:162-164`; search 11.15 |
| | Retired show-more keys removed in every language | 11.13, 11.14 |
| | No new text and invariant paging data | 9.3; lint in `SPA-G` and the suppression search (11A.11, 13.2) |
| shared-problem-infrastructure | Module-neutral problem modules | RED 1.2 (the `src/api/problemDetails.js` path only) and RED 1.3 (`src/api/problemCodes.json`); the `apiTransport.js` and `ProblemMessage.jsx` moves are RED-less refactor moves (PD-5), proven by the 1.9 searches and runs and the 1.11 `vite build`; guard `PlatformPanel.test.jsx` (1.9) |
| | Field-error helpers have one module-neutral owner | No RED (PD-5 adds no test): searches 1.9 (importers, exports and imports) and 12.2; the 1.9 runs; the 1.11 `vite build`; guard `MfaRecoveryPage.test.jsx:121-122` (1.9) |
| | Error words unchanged by the move | guards 1.10: `spanish.test.jsx:98-114` (the moved `ProblemMessage` in `es`), `catalog.contract.test.js` (`es` parity), `ProblemMessage.test.jsx` (`en`), empty locales diff |
| | One catalogue, same gates | 1.3, 3.2 |
| | Result stays internal behind one mapper | 2.1, 6.2 (list `200` body; list-read `403` problem shape) |
| | Standards documents match the new contract | 12.2 → 12.5 |

## Carry-over index

- **D items:**
  - D01 6.10–6.14 · D02 5.1, 6.11–6.13, 7.1–7.3, 8.1–8.2 · D03 4.3 · D04 1.3, 3.1, 6.1–6.9, 6.14, 7.5 · D05 7.1–7.3 · D06 declared list, 1.7–1.8, 6.15, 8.5
  - D07 6.4, 8A.6 · D08 6.1, 8.3 · D09 6.3, 6.6, 7.1–7.2, 8A.5, 11A.10, 13.8 · D10 9.1–9.2, 10.1–10.2, 11.3–11.6 · D11 11A.8–11A.9 · D12 6.6, 11A.8, 11A.10
  - D13 6.8–6.9 · D14 6.5, 6.8 · D15 1.1, 6.2, 6.4, 8.3–8.4, 13.7–13.8 · D16 8A.3 · D17 8A.2, 8A.5–8A.7 · D18 8A.1, 8A.4–8A.6, 8A.8
  - D19 1.5–1.6, 1.9, 12.2–12.5 · D20 12.2–12.5 · D21 1.2, 10.1, 10.3, 10.5, 11.1–11.2, 11.7–11.8 · D22 11.12, 11.14, 12.8 · D23 11.10, 11A.5 · D24 8.6, 11A.5, 12.1, 13.8
- **PD items:** PD-a 4.1, 6.2–6.3, 8.3 · PD-b 12.1, 13.8 · PD-1 4.1, 6.3, 6.6, 9.1–9.2, 13.8 · PD-2 9.8, 10.2, 11.3–11.6 · PD-3 6.5, 6.10 · PD-4 8A.2–8A.8 · PD-5 (expanded) 1.5–1.6, 1.9, 12.3 · PD-6 11.7–11.8 · PD-7 11.10, 11A.5
- **AD items:**
  - AD1 4.2, 4.4 · AD2 5.1 · AD3 5.1 · AD4 4.2 · AD5 7.1–7.4 · AD6 7.1–7.3
  - AD7 6.10, 6.12–6.13 · AD8 2.2–2.5, 2.7–2.8 · AD9 8.3 · AD10 1.4–1.8 · AD11 9.6–9.7 · AD12 9.1–9.2
  - AD13 9.8–9.9, 10.2, 11.5–11.6 · AD14 11.1–11.2, 11A.7 · AD15 11.5–11.6, 11.8, 11A.4–11A.5 · AD16 6.1–6.9 · AD17 8.6 · AD18 5.1, 6.6, 6.8
- **Items inherited from the validator:**
  - `ApiProblemMetadata.cs` optional comments 2.6 · the `platformClient` malformed-page test 10.3
  - the widened Task 12 searches, `.agents/skills` included, 12.2–12.5 · the `package-lock`/`*.scss` exclusions 12.1
  - the `SCREENS.md:53` follow-up and the D09 delivery-note line 13.8
  - a refused page still renders the `'pagination'` block without data 11A.4
  - `MAX_WALK_PAGES=100` 9.8, 11.5 · the `cancelled` guard 11A.9 · the RolesPage catalogue read states 11.1–11.2
- **Items from the tasks review (2026-09-13):**
  - ORDER-1 DL.0 · ORDER-4 and CONTRACTS-04 2.6 · DESIGN-01 2.8 · DESIGN-04 11.2, 11A.7 · DESIGN-05 7.1–7.3, 8.2
  - SPEC-01 8A.1, 8A.8, 13.7 · SPEC-02 coverage table · SPEC-03 1.9 · SPEC-05 1.10 and the spec scenario · SPEC-06 6.2 · SPEC-07 6.6
  - SPEC-09 11.1, 11.5, 11A.4–11A.7 · SPEC-10 1.1, 11A.6, 11A.11, 13.2 and the spec scenario · SPEC-11 and CONTRACTS-01 1.1, 8.4, 13.7 · SPEC-12 6.2
  - TESTS-01 12.1 · TESTS-02 conventions, 1.1, 1.11, 11A.11, 12.9, 13.2, 13.8 · TESTS-03 6.6–6.9 · TESTS-04 the RED run and expected-failure lines · TESTS-05 13.6 · TESTS-06 DL.3 · TESTS-07 DL.4 · TESTS-09 6.5
  - CONTRACTS-02 9.1–9.2 · CONTRACTS-05 11.8 (page state also at 11.4, 11.6, 11.10) · CONTRACTS-06 13.8
- **Items from apply validation:**
  - B2-V03, the shared `pageResponse` helper behind R6A and R6B, row 5 and 11A.10
  - C3-V01 (a) 11A.3 and spec "Mutation outcome survives a failed refresh"; 13.8 notes the amendment
  - D1-V04 staged waits (row 4); 13.8 declares the up-to-3-step wait allowance

## Design corrections found at sdd-tasks

- The plan's Task 8A Step 2 search matches `SPEC.md:1065` at baseline, and D17/PD-4 keep that text as C5 history. "Returns empty" (proposal Success Criteria) therefore cannot hold; 8A.8 expects only `:1065`.
- There are no mirrored standards: neither `.claude/skills` nor `.codex/skills` has a `*-standards` folder.
- `platformClient.test.js:3` and `platformDirectory.test.js:3` import the moved transport. Both are already declared, and their import edit is scheduled at 1.7.
- Spec correction, shared-problem-infrastructure "Spanish words survive the move": no existing test renders `errors:permission_denied` or `errors:reference` in `es`, and D22 plus the import-paths-only move of `ProblemMessage.test.jsx` forbid adding one. The scenario now names its real proofs (`spanish.test.jsx:98-114`, `es` catalogue parity, an empty `es/errors.json` diff).
- Spec correction, spa-pagination-ui "Localization lint stays clean": "no new suppression" now means no new `i18next/no-literal-string` suppression, so the plan's `react-hooks/exhaustive-deps` suppression on the past-the-end effect (plan:1211) is allowed.

## Open decisions (orchestrator; settled at DL.0, before Phase 1)

1. Commit granularity: one commit per batch A–D (held locally, pushed once after Phase 13), or one commit. Batch B alone does not pass `SPA-G` (the screen fixtures migrate in C), and CLAUDE.md forbids committing unverified work.
2. Whether the `openspec/` artifacts are committed, and in which commit.

**Answers (user, 2026-09-13):**

1. **One single commit.** Batches A–D go into one conventional commit on `main`, made only after every Phase 13 gate is green and DL.2–DL.3 have run, then pushed once. DL.5 writes exactly one commit.
2. **After archive, in a separate commit.**
   - The code commit (DL.4–DL.6) stages nothing under `openspec/`.
   - After sdd-verify passes and sdd-archive completes, one separate `docs(openspec)` commit adds `openspec/`: `config.yaml`, the skeleton, the archived change folder and the merged main specs.
   - That commit runs the Native Checking Contract, stages explicit paths only, and is pushed to `origin/main`.
   - It is a post-archive delivery step, not a checkbox task, so it never blocks archive.
