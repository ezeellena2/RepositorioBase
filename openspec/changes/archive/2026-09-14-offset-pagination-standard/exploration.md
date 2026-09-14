## Exploration: offset-pagination-standard

Source plan: `docs/superpowers/plans/2026-09-13-cross-project-result-pagination-standard.md` (cited as `plan:<line>`).
Baseline: `main` at `2716aa6`. Evidence: six read-only verifiers, an adversarial re-check, and spot-checks by this phase
(SPEC rows, catalog keys, `FORBIDDEN_SUCCESS_KEYS`, endpoint declarations, `ResultHttpExtensions`, MUI `esES`,
`WithBodyBindingFailureCode`, the Platform `Directory` array). The plan's decisions (E1-E12, L1-L8, task order) are
accepted inputs and are not reopened here.

### Current State

**Backend: cursor (keyset) pagination on seven list routes.**
- Platform directories (organizations, identities, admins, audit):
  - Contract: `PlatformDirectoryQuery(int Limit, string? Cursor)` and `PlatformDirectoryPage<T>(Items, NextCursor)` (`PlatformDirectories.cs:12,22`).
  - Default page: 25 (`PlatformEndpoints.cs:329`).
  - Clamp: 1-100, "clamped rather than refused" (`PlatformDirectories.cs:17`, `PlatformOperationalProjectionReader.cs:169`).
  - Reads: `Take(limit + 1)` with encoded cursors (`:187-224`).
- Tenant lists (roles, members, invitations):
  - Contract: `ListRolesQuery(TenantId, int Limit, string? Cursor)` (`RoleRequests.cs:21`, `MembershipRequests.cs:17,20`).
  - Page records: `RolePage`, `MemberPage` and `InvitationSummaryPage`.
  - Default page: 100, not 25. Endpoints pass `limit ?? 0`, and the stores map values `<= 0` to 100 (`RoleEndpoints.cs:67`, `MembershipEndpoints.cs:79,87`, `RoleAdministrationStore.cs:23`, `MembershipAdministrationStore.cs:21,37`). `AdministrationDirectoryRevalidationTests.cs:49` pins this default.
- Invalid input:
  - An unreadable cursor silently restarts at page one.
  - A non-integer or beyond-Int32 query value fails binding and answers `400 invalid_request` on all seven routes (`DependencyInjection.cs:34-35`, `ProblemDetailsExceptionHandler.cs:80,98-102`, proven by `OpenApiContractTests.cs:567-592`).

**Error architecture (unchanged by this change).**
- Results and the writer:
  - Expected failures are `Result`/`ApplicationError`.
  - `ResultHttpExtensions` already has `ToHttpResult(Result, …)` returning 204 and `ToHttpResult<T>(Result<T>, …, onSuccess)` (`ResultHttpExtensions.cs:8-23`). The Platform directories use them (`PlatformEndpoints.cs:153,163,173,183`).
  - One writer holds the category-to-status switch (`ApiProblemDetailsMapper.cs:78-88`).
- Declared codes on list routes:
  - `WithBodyBindingFailureCode` adds a 400 `ApiProblemContractMetadata` entry for its code (`ApiProblemMetadata.cs:142-150`), so all seven routes already declare `invalid_request`.
  - No list query has a validator.
  - The Platform `Directory` array (`PlatformEndpoints.cs:36-46`) declares `recent_mfa_required` and `validation_failed` on all four directories. It does not list `InvalidRequest`.

**SPA.**
- Shared problem infrastructure lives under `features/identity`: `api/problemDetails.js`, `api/apiTransport.js`, `problemCodes.json` and `ProblemMessage.jsx`. Together they have 24 importing source files and 9 importing test or helper files.
  - `ProblemMessage.jsx:7` imports `features/identity/fieldErrors.js`, which in turn imports `register/cuit`.
  - `problemDetails.js:14` `FORBIDDEN_SUCCESS_KEYS` includes `items` and `nextCursor`.
- Client requests:
  - `identityClient.js:18-21` never sends `limit`.
  - `platformClient.js:14-23` clamps `limit`, with a default of 25.
  - `useRead.js:22,43,47-49` carries a `(cursor, merge)` pair.
- Screens:
  - Five screens render cursor "show more" controls.
  - `RolesPage.jsx:212-220` and `MembersPage.jsx:191-199` walk `nextCursor` to resume an operation.
  - `MembersPage.jsx:131-137` and `InviteMemberPage.jsx:113-116` call `listRoles(tenantId, null)` and treat the first page as the whole role catalogue.
  - `PlatformPanel` administrators and audit have no continuation control.
- Localization:
  - The five show-more keys exist in `en` and `es`.
  - `themeFor(language)` composes `esES` (`theme.jsx:288-294`, `@mui/material` 9.4.0).
  - `App.localization.test.jsx:10-30` already proves that `TablePagination` switches between English and Spanish.
  - `npm run i18n:unused` checks only the `en` catalog.

**Documents.**
- `docs/features/identity-access/SPEC.md` has status "Proposed for approval" (`:3`); only §14 C1-C7 are marked accepted.
  - Cursor wording appears at `:239` (IA-REQ-045), `:306-309`, `:464-465`, `:1033`, `:1037` and `:1065`.
  - `:201` pins "no pagination envelope".
- `TRACEABILITY.md` repeats the cursor or nullable-limit wording at `:86`, `:92` and `:99`.
- ADR-004 (Proposed), decision 17 (`:36`), says "bounded/cursor".
- `docs/features/whatsapp-bot/SPEC.md:295,304` and `SCREENS.md:53` describe cursor pages for routes that are not implemented.
- No `openspec/changes/offset-pagination-standard/` existed before this phase, and `openspec/` is untracked.

### Verification Checklist

| # | Item | Result | Evidence |
| --- | --- | --- | --- |
| V1 | Task 1 move sources exist; destinations absent | holds | `features/identity/api/problemDetails.js`, `api/apiTransport.js`, `features/identity/problemCodes.json` and `features/identity/ProblemMessage.jsx` are present. `src/Web/ClientApp/src/api/` is absent. `src/components/` exists. |
| V2 | Task 6/7/8 modify paths exist with the cursor shapes described | holds (list incomplete, D01) | `PlatformDirectories.cs:12,22,84-98`; `RoleRequests.cs:21`; `MembershipRequests.cs:17,20`; `IRoleAdministrationStore.cs:58`; `IMembershipAdministrationStore.cs:64,66`; `RoleEndpoints.cs:65`; `MembershipEndpoints.cs:77,85`; `PlatformEndpoints.cs:150-187,329`; `PlatformDirectoryContracts.cs:53-59` |
| V3 | Route/OpenAPI path keys in `ListPaths` | holds | `OpenApiContractTests.cs:574-580` uses the same seven keys |
| V4 | Task 4/5 create paths free; no type collision | holds | No `PaginationQuery`/`PaginatedList`/`PageNumber`/`TotalCount` under `src` or `tests`; no `src/Infrastructure/Data/Pagination` |
| V5 | SPEC `:201` (IA-REQ-038 "no pagination envelope") | holds (not the closing sentence, D18) | `SPEC.md:201` contains "adds no pagination envelope to identity endpoints" in the middle of the paragraph; IA-REQ-038 continues at `:203-217` |
| V6 | SPEC `:306-309` pin `limit`/`cursor`/`nextCursor` | holds | Each row says "with `limit`/`cursor`" and `{ items…, nextCursor }`; `recent_mfa_required` appears only on `:306-307`; `:308` is untyped `{ items, nextCursor }` |
| V7 | SPEC `:464-465` scenario | holds | ":464 opaque cursor and bounded limit"; ":465 … and `nextCursor`" |
| V8 | SPEC `:1033`, `:1037` | holds | `limit` (1–100) / `cursor` and `{ items: …, nextCursor }` on both |
| V9 | SPEC `:1065` amendment | holds | "The bounded `limit`/`cursor` shape on `/api/tenants/*` amends IA-REQ-038 and IA-REQ-045." |
| V10 | The plan's pinned-row list is complete | does not hold | `SPEC.md:239` (IA-REQ-045) pins "opaque `cursor`" and `{ items, nextCursor }`, and is missing from plan:42-47 and :743-749 (D16) |
| V11 | TRACEABILITY `:92`, `:99` cite the cursor shape | partially holds | `:92` "bounded/cursor DTO", "cursor envelope"; `:99` "bounded/cursor DTOs", "limits, cursors". `:86` and `:92` also cite the `limit` binding test ("nullable-limit"), which the plan omits (D07, D18) |
| V12 | Five show-more keys in `en`/`es` | holds | `en/platform.json:22` "More organizations", `:85` "More accounts"; `en/identity.json:126` "Show more invitations", `:176` "Show more members", `:223` "Show more roles"; `es` mirrors at the same lines ("Mostrar más …"); used at `PlatformPanel.jsx:358`, `PlatformIdentitiesPage.jsx:550`, `InviteMemberPage.jsx:462`, `MembersPage.jsx:524`, `RolesPage.jsx:441` |
| V13 | `FORBIDDEN_SUCCESS_KEYS` in `problemDetails.js` contains `items` and `nextCursor` | holds | `problemDetails.js:14` `['succeeded','success','data','error','errors','value','items','nextCursor']`; declared members are exempt at `:132-133`, so `nextCursor` staying forbidden reports drift (E7) |
| V14 | `RoleEndpoints`/`MembershipEndpoints` do not declare `InvalidRequest` | partially holds | Absent from the `WithApiProblemDetails` arguments (`RoleEndpoints.cs:30`, `MembershipEndpoints.cs:25,31`). Declared anyway, because `.WithBodyBindingFailureCode(InvalidRequest.Code)` (`RoleEndpoints.cs:31`, `MembershipEndpoints.cs:26,32`) adds `ApiProblemContractMetadata(400, code)` (`ApiProblemMetadata.cs:142-150`). The served v1.json lists `invalid_request` on all seven routes. The Platform `Directory` array has no `InvalidRequest` (`PlatformEndpoints.cs:36-46`), contrary to plan:628 (D08) |
| V15 | Existing `ResultHttpExtensions.ToHttpResult` (both overloads) | holds | `ResultHttpExtensions.cs:8-14` (NoContent), `:16-23` (`onSuccess`); failures go through `mapper.ToHttpResult(result.Error!)` |
| V16 | Optional 202 overload condition (at least two bodyless 202 endpoints) | holds | `PlatformEndpoints.cs:146,310`, `PlatformInvitationEndpoints.cs:55`, `Identity.cs:64`, `InvitationEndpoints.cs:147`, and others |
| V17 | MUI `esES` translates `TablePagination` | holds | `@mui/material` 9.4.0; `locale/esES.js:9` `formatNumber('es-ES')`, `:27` 'Ir a la página siguiente', `:32` 'Filas por página:', `:33-37` `labelDisplayedRows` with formatted numbers; `enUS` is empty and English comes from component defaults (`TablePagination.js:32,128-137,173`) |
| V18 | E1 policy quote | partially holds | `PlatformDirectories.cs:17` matches, but for Platform only; tenant routes treat a value `<= 0` as 100 (D09) |
| V19 | E2 overflow safety | holds within Int32 | `MaxPageNumber = int.MaxValue / 100`; largest `Skip` 2,147,483,500. Values beyond Int32 never reach the clamp (400 via E3, D15) |
| V20 | `useRead` behaviour behind E8/E10/E11 | holds | `useRead.js:15-34` generation abort, `:36-41` keeps data while loading, `:53-62` errored keeps data and refused clears it, and it never rethrows |
| V21 | No journey page object depends on cursors or "show more" | holds | No match in `tests/Web.AcceptanceTests`; page objects open selects by id (`PlatformOperationsPage.cs:173-174,280-281`) |
| V22 | E6: stale `invalid_cursor` fixture gone | holds | No match under `src`; only point-in-time audit docs mention it |
| V23 | Every declared test file exists | holds (list incomplete, D06) | 15 SPA files and 10 .NET files exist; `StackBaselineTests.cs:338-354` uses `cursor` only as a string index |
| V24 | WhatsApp SPEC lines, routes not implemented | holds | `whatsapp-bot/SPEC.md:295,304` `{ items, nextCursor }`, `:3` Proposed; nothing under `src` references `/api/whatsapp` or `bot/capabilities` |

### Discrepancies

Only verdicts confirmed or corrected by the adversarial re-check are listed. Corrected reality is used where it applies,
and overlapping verifier findings are merged (source ids in brackets).

| Id | Plan claim | Reality | Evidence | Impact | Proposed plan adjustment |
| --- | --- | --- | --- | --- | --- |
| D01 | Task 6 changes only five Application files (plan:503-509); Step 2 deletes `PlatformDirectoryQuery`/`PlatformDirectoryPage` and generic cursor helpers | The handlers and the Platform reader port also use the cursor members and are in no task, so Application stops compiling. The three page records are unnamed, and a test deserializes HTTP bodies into two of them | `RoleHandlers.cs:42-49`; `MembershipHandlers.cs:20-27,34-41`; `PlatformDirectoryHandlers.cs:29-58`; `IPlatformOperationalProjectionReader.cs:15-21`; `RolePage` `IRoleAdministrationStore.cs:15`, `MemberPage`/`InvitationSummaryPage` `IMembershipAdministrationStore.cs:21,35`; `IndependentDevelopmentAdministrationReviewTests.cs:114,180` [paths-backend-1, -16; cursor-scope-sweep-1] | material | Add the four files to Task 6 Files. Name the three page records for replacement, and note the deserialization dependency |
| D02 | Task 5 snippet compiles as written; "Application must not depend on EF Core" (plan:472-492); endpoint and port snippets use `PaginationQuery`/`PaginatedList` | Infrastructure has no global using for EF Core or Common.Models. Application already references EF Core (the placement still stands). Web endpoints, the Platform contracts file and three port interfaces lack `using CleanArchitecture.Application.Common.Models;` | `src/Infrastructure/GlobalUsings.cs`; `Application.csproj`; `src/Application/GlobalUsings.cs:2`; `RoleHandlers.cs:12`; `src/Web/GlobalUsings.cs`; `IRoleAdministrationStore.cs:1`; `IMembershipAdministrationStore.cs:1-2`; `IPlatformOperationalProjectionReader.cs:1` [paths-backend-8, -18] | minor | Add the usings. Reword the rationale to "keep query execution in Infrastructure" |
| D03 | Task 4 test `new PaginatedList<int>([4,5,6], pageNumber: 2, pageSize: 3, totalCount: 8)` (plan:421) | The record's positional parameters are PascalCase, so the call fails with CS1739. After Step 4 it still does not compile, Step 5 can never pass, and the Application.UnitTests build breaks | plan:436-440 [paths-backend-7] | material | Use `PageNumber:`/`PageSize:`/`TotalCount:` or positional arguments |
| D04 | TDD order: Task 2 Step 4 passes, Task 6 deletes types "only after all callers compile", Task 7 Step 3 fails "until Web/API… then PASS" | (a) Task 1 moves `problemCodes.json`, but `OpenApiContractTests.cs:491` reads the old path until Task 3 Step 1, so Task 2 Step 4 fails. (b) Task 6 breaks Application, Infrastructure, Web and every test project until handlers (D01), test doubles and endpoints are edited, so Task 7 Step 3 cannot run. The test doubles are never scheduled. (A compile-failure RED for new types in Task 4 does not violate `engineering-rules.md:47`) | `OpenApiContractTests.cs:490-491,707-718`; `ConcurrentDeactivationFloorTests.cs:134`; `IndependentDevelopmentAdministrationReviewTests.cs:216-217`; `AdministrationDirectoryRevalidationTests.cs:77-83`; `PlatformEndpoints.cs:150-187,329` [paths-backend-13, -14] | material | Move the `:491` path edit into Task 1. Treat Tasks 6-8 as one compile unit, with a behavioural HTTP RED (parameter names, response members, clamp) written first. Schedule the test-double and direct store-call edits explicitly |
| D05 | Task 7 replaces each cursor read with one `ToPaginatedListAsync` call (plan:540-554) | Four list methods page an intermediate row and build the view afterwards, so they need a re-wrap step. Organizations, identities and administrators project directly | `RoleAdministrationStore.cs:30-33,265-283,291`; `MembershipAdministrationStore.cs:28-32,43-69,247-268,272`; `PlatformOperationalProjectionReader.cs:130-160` [paths-backend-9] | material | Add `new PaginatedList<TView>(views, page.PageNumber, page.PageSize, page.TotalCount)` (or a `Map`) for roles, members, invitations and audit |
| D06 | "Test Files This Functional Change Edits" is complete, and Task 13 Step 8's `git status -- tests` shows only listed files (plan:95-121, :1403) | Undeclared .NET files: `PlatformProjectionTests.cs`, `PlatformIdentityLifecycleTests.cs`, `PlatformDirectoryAccessTests.cs` and `PlatformAdministrationTests.cs` (all construct `PlatformDirectoryQuery`); `OpenApiContractTests.cs`; `ProblemDetailsContractTests.cs`; the new `PaginationQueryTests.cs` and `PaginatedListTests.cs`. Undeclared SPA files: `test/identityFiles.contract.test.js`, `ProblemMessage.test.jsx`, `api/apiTransport.test.js`, `problemCatalogue.contract.test.js`, `i18n/catalog.contract.test.js`, `test/identityServer.js` (helper). Task 1 never states the test-file moves that Task 9 assumes | `PlatformProjectionTests.cs:30,53,66,92,113,136-139`; `PlatformIdentityLifecycleTests.cs:72`; `PlatformDirectoryAccessTests.cs:92`; `PlatformAdministrationTests.cs:26`; `identityFiles.contract.test.js:28`; `ProblemMessage.test.jsx:5-6`; `apiTransport.test.js:5`; `problemCatalogue.contract.test.js:5`; `catalog.contract.test.js:4`; `identityServer.js:2`; plan:205-206, :328-329 [paths-backend-2; paths-client-tests-1..6, -16, -18; cursor-scope-sweep-2; error-contracts-2] | material | Add every file to the declared lists with its edit. List the moves of `problemDetails.test.js`, `apiTransport.test.js` and `ProblemMessage.test.jsx` in Task 1 Files. Keep the pinned catch counts in `identityFiles.contract.test.js:8-18,117-127,136-138` unchanged |
| D07 | Task 8 Step 4 only renames OpenAPI parameters; E3 is proven by a new OpenAPI assertion (plan:67, :640-650) | `Every_numeric_limit_binding_refusal_is_emitted_only_as_declared` sends `?limit=not-a-number` to all seven routes and expects 400. Once `limit` is unbound it is ignored and the test fails. It is E3's real emission proof, and `TRACEABILITY.md:86` and `:92` cite it | `OpenApiContractTests.cs:567-592` (`:585`) [paths-backend-3; spec-rows-3; cursor-scope-sweep-3; error-contracts-2] | material | Migrate the test to `?pageNumber=not-a-number` and `?pageSize=not-a-number`, rename it, cite it as E3's proof, and update `TRACEABILITY.md:86,92` in Task 8A |
| D08 | E3: tenant routes do not declare `invalid_request`; Platform routes get it "through their shared `Directory` contract array"; the new OpenAPI assertion proves it (plan:67, :628-638, :675-676) | All seven routes already declare and publish `invalid_request` through `WithBodyBindingFailureCode`. The `Directory` array does not contain it. The inline addition is redundant, because the transformer deduplicates. In the new test only the parameter-name assertions are RED; `ShouldContain("invalid_request")` is green today | `ApiProblemMetadata.cs:142-150`; `ApiExceptionOperationTransformer.cs:14-18`; `PlatformEndpoints.cs:36-46`; `OpenApiContractTests.cs:653-661,693-703`; error-handling SKILL.md:99-100 [paths-backend-4; error-contracts-1] | material | Restate E3 and Task 8 Step 3 as "already declared through `WithBodyBindingFailureCode`; keep it on the renamed parameters". Make the inline declaration optional. Treat only the parameter-name assertions as RED |
| D09 | One default `pageSize` of 25 and a clamp of 1-100 for every route, recorded as "the existing policy" (plan:65, :395-411, :597, :745) | Tenant routes default to 100 today, and a value `<= 0` means 100. The plan silently changes the tenant default to 25 and makes `pageSize=0` return 1 row. This affects the SPA tenant screens, the role pickers (D10) and the R6C tests | `RoleEndpoints.cs:67`; `MembershipEndpoints.cs:79,87`; `RoleAdministrationStore.cs:19,23`; `MembershipAdministrationStore.cs:17,21,37`; `identityClient.js:18-21`; `AdministrationDirectoryRevalidationTests.cs:39-63` (`:49`); `IdentityAccessReviewRevalidation.test.jsx:17,186` [paths-backend-10; error-contracts-3; spec-rows-4; cursor-scope-sweep-7] | material | Record the tenant default change (100 to 25; a value `<= 0` now means 1) as an explicit consequence next to E1 and in the delivery note. Specify the R6C rewrite as a walk over `pageNumber`. The amended IA-REQ-038 text already states "default 25", so rows `:1033` and `:1037` need no default clause |
| D10 | Task 10 `listRoles(tenantId, page, options)` via `boundedPage({ pageNumber = 1, pageSize = 25 } = {})`; only RolesPage is migrated (plan:814-827, :907-918, :990) | `MembersPage.jsx:131-137` and `InviteMemberPage.jsx:113-116` pass `null`. A destructuring default applies only to `undefined`, so a TypeError becomes `client_failure` (not E7's `unreadable_response`). With a valid page, the pickers list at most 25 roles, and missing roles fall back to raw ids | `apiTransport.js:27`; `useRead.js:53-62`; `identityClient.test.js:14-16`; `MembersPage.jsx:270-273`; `InviteMemberPage.jsx:201-204` [paths-client-tests-9; error-contracts-4; cursor-scope-sweep-6] | material | Name both callers in Task 10/11. Make `paginationSearch`/`boundedPage` null-safe or change the callers. State how a role catalogue reads every role (page walk or `pageSize` 100), with a test for more than 25 roles |
| D11 | Task 11A Step 4 replaces only `RolesPage.jsx:212-220`; a failed walk "abandons the resume silently, as today" (plan:1219-1231) | `MembersPage.jsx:191-199,217` has the same walk, which `ExternalProofResume.test.jsx:175,223` exercises for members. Today a failed walk is not silent: the catch calls `setActionProblem(toProblem(error))`, and that problem is rendered | `RolesPage.jsx:204-231,145,292-295,333-335,392`; `MembersPage.jsx:190,209-211` [paths-client-tests-8; cursor-scope-sweep-4; error-contracts-5] | material | Extend Step 4 to MembersPage. Describe the current behaviour accurately, or declare silent abandonment a deliberate change with a test |
| D12 | In `ExternalProofResume.test.jsx` and `IdentityAccessReviewRevalidation.test.jsx`, "change only the fixtures … assertions stay as they are" (plan:1233) | Both tests assert cursor and limit requests and click "Show more" controls that L3 deletes. R6C `:212` would pass vacuously, while `:211`, `:217` and `:227` can never pass | `ExternalProofResume.test.jsx:83-87,109,122,185,186,229,242`; `IdentityAccessReviewRevalidation.test.jsx:17-18,36,195-227` [paths-client-tests-7; cursor-scope-sweep-5] | material | List the assertions that move to offset equivalents (recorded reads, continuation control, `limit`/`CURSOR` expectations), keep "what is resumed or refused", and decide whether the 100/101-row fixtures shrink |
| D13 | The Task 8 Step 5 tests use "the same arrangement as the existing clamp test at :79–:95" (plan:654-711) | The snippets omit `await PlatformScenario.ActiveOwnerAsync();`. After `ResetState` the caller is anonymous, so `AuthorizationBehaviour` throws `UnauthorizedAccessException` before the MFA gate. The file lacks `using CleanArchitecture.Application.Common.Models;`. The clamp test spans `:75-90` | `PlatformDirectoryContractTests.cs:1-6,15,82,99,121,137`; `TestBase.cs:8`; `TestApp.cs:447`; `AuthorizationBehaviour.cs:54-57`; `PlatformDirectoryHandlers.cs:18-21` [paths-backend-5; error-contracts-6; cursor-scope-sweep-15] | material | Add `ActiveOwnerAsync()` before seeding in both tests, add the using, and cite `:75-90` |
| D14 | Task 8 Step 5 says only "Add these tests"; `PlatformApplicationShapeTests.cs` is listed with no instruction (plan:120, :516-520, :652) | Five existing cursor tests break: `:21-31` fails at runtime, and `:79-90`, `:97-115`, `:119-127` and `:135-146` stop compiling. `:135-146` is the only audit newest-first proof. `:37-51` still passes but carries a cursor name. The shape test requires `PlatformDirectoryQuery` with exactly `[Cursor, Limit]` and a `Query` property, and the plan never fixes the replacement property name (`Query` or `Pagination`) | `PlatformDirectoryContractTests.cs:10-146`; `PlatformApplicationShapeTests.cs:41,130-144` [paths-backend-6, -15] | material | State each test's fate: rewrite the parameter and audit newest-first tests over pageNumber 1 and 2, convert the limit clamp test to a pageSize clamp test, delete the two cursor-only tests, and rename `:37-51`. Name the Platform query property and give the replacement shape assertion |
| D15 | E5 limits `recent_mfa_required` to organizations and identities and omits `validation_failed`; E2 and acceptance :1430 say out-of-range values (int.MaxValue included) answer 200 | All four Platform directories gate on MFA and declare `recent_mfa_required`, and all four declare `400 validation_failed`, which no validator can emit. Values beyond Int32 fail binding (400 `invalid_request`) and never reach the clamp | `PlatformEndpoints.cs:36-46,63,69,75,81`; `PlatformDirectoryHandlers.cs:33,41,49,57`; `OpenApiContractTests.cs:167-177`; `RoleEndpoints.cs:65`; `DependencyInjection.cs:34-35` [paths-backend-17; error-contracts-7, -8] | minor | Correct the E5 list. Record whether the declared but unreachable `validation_failed` stays (rule 10). Scope E1/E2 to Int32 values and add a beyond-Int32 case to the migrated binding test (D07) |
| D16 | The SPEC pins cursors at `:201`, `:306-309`, `:464-465`, `:1033`, `:1037` and `:1065`, and Task 8A Step 2's `rg` then returns nothing (plan:42-47, :743-759, :1437) | IA-REQ-045 at `SPEC.md:239` also pins "bounded `limit` (1–100) and opaque `cursor` … `{ items, nextCursor }`" and is missing from Task 8A. Section 4 governs over §14 (`:578-580`). The Step 2 search matches `:239`, so its expected empty result cannot hold | `SPEC.md:239`; `:1065` names IA-REQ-045; `TRACEABILITY.md:99` [paths-backend-11; spec-rows-1; cursor-scope-sweep-8] | blocking | Add an IA-REQ-045 (`:239`) row to Task 8A that replaces `limit`/`cursor`/`nextCursor` with `pageNumber`/`pageSize` and the offset metadata, keeping its `/api/identity/*` no-envelope sentence. Add it to plan:42-47 |
| D17 | Task 8A rewrites `:1065` in place to cover `/api/tenants/*` and `/api/platform/*`, and edits only SPEC.md and TRACEABILITY.md (plan:737-749) | (a) `:1065` is C5's historical "Amends." record, and C5 never introduced an offset shape on `/api/platform/*`. Precedent 8711656 edits §14.5 rows in place but pairs that with a dated TRACEABILITY evidence paragraph, which Task 8A does not plan. (b) ADR-004 decision 17 (`:36`) still says "bounded/cursor". The ADR is Proposed and not binding, and it has no post-acceptance amendment convention | `SPEC.md:978-979,1065`; `TRACEABILITY.md:158` (P2-4 evidence paragraph); ADR-004 `:3,36,42` [spec-rows-2, -5; cursor-scope-sweep-9] | material | Make the normative change in section 4 (IA-REQ-038, IA-REQ-045) and the route rows. Leave `:1065` as history or add a dated note. Add a dated TRACEABILITY evidence paragraph. Either reword ADR-004 decision 17 or record why it is excluded |
| D18 | Task 8A row instructions and the Step 2 proof are precise (plan:43, :739, :745-748, :756) | Several details are off. `:201` is mid-paragraph, not the closing sentence, and "append" is ambiguous (precedent 1462696 added an indented sub-paragraph). `recent_mfa_required` exists only on `:306-307`, and `:308` is untyped `{ items, nextCursor }`. The `404` clause on `:1033` belongs to the detail route, and `:1037` has none (`:1028` holds the list 404). The Step 2 regex misses TRACEABILITY `:86`, `:92` and `:99`. `:92` cites `usePlatformRead.test.jsx`, which was never committed, and a "More accounts" clause that L3 makes stale | `SPEC.md:201-217,306-309,1028,1033,1037`; `TRACEABILITY.md:86,92,99` [paths-backend-12; spec-rows-6, -7, -8, -9] | minor | State the insertion point. Adapt the row substitutions per row. Add a case-insensitive `cursor`/`nullable-limit` check on TRACEABILITY, with a reviewed exclusion for `SPEC.md:695` and historical `TASKS.md:936,940`. Fix the `:92` citations |
| D19 | Moving `ProblemMessage` to `src/components` makes problem UI module-neutral; L6 lists `errors:<code>`, `retryAfter` and `reference` (plan:87, :132, :186, :1274, :1424) | `ProblemMessage.jsx:7` imports `features/identity/fieldErrors.js`, which imports `register/cuit` and holds registration validators. The neutral component would still depend on `features/identity`, and the Task 12 Step 2 search misses it. `ProblemMessage` also resolves `errors:unknown`, `errors:validation.fieldMessage`, `errors:validation.fields.*` and the validation-detail keys | `fieldErrors.js:1-2,26-37,99-115`; `ProblemMessage.jsx:9-14,62,79-84`; Platform importers `PlatformPanel.jsx:27`, `PlatformStepUpForm.jsx:6`, `MfaRecoveryPage.jsx:14`, `PlatformInvitationPages.jsx:21` [paths-client-tests-10; i18n-mui-5] | material | Split the shared rendering helpers (`unclaimedFieldErrors`, `fieldIdFor`, `validationDetailText`) next to `ProblemMessage`, leaving registration validators in Identity. Or record the remaining dependency explicitly. Extend L6 and the Task 12 search |
| D20 | Removing Identity-specific shared error paths is proven by searching `src/Web/ClientApp/src` (plan:1269-1277); the frontend standard needs no change | The binding error-handling skill names the old paths: `SKILL.md:109` (rule 14) and `references/error-handling-rules.md:288,328,400,476`. `frontend-design-standards/references/ui-composition-rules.md:316-317` says "Preserve real cursor controls", which becomes stale | The cited lines [paths-client-tests-11; i18n-mui-6] | material | Update both standards documents in the same change, or list them as explicit follow-ups. Widen the Task 12 Step 2 search to `.agents/skills`. `TRACEABILITY.md:18` is a dated record and needs no edit |
| D21 | SPA snippets fit the target files (plan:140-144, :920-932, :944-954, :990) | Several snippets do not fit. The `RolesPage` load also fetches the permission catalogue into `read.data.catalog`, and the snippet drops it; keeping it re-requests the catalogue on every page. `identityClient.test.js` uses a mocked `send`, not MSW/`TENANT`, and its `:7` body fails `readPage`. No `platformClient` list signature is given, and the plan does not say whether PlatformPanel administrators/audit get a control. `identityFiles.contract.test.js` has no `source` variable, and its existence list is an `it.each` | `RolesPage.jsx:134-140,144,488-513`; `identityClient.test.js:1-8`; `platformClient.js:18-23,62-77`; `PlatformPanel.jsx:130-132,357-359`; `identityFiles.contract.test.js:5-7,26-41` [paths-client-tests-12, -13, -14, -17; error-contracts-9] | minor | Say where the catalogue read lives, adapt the test snippets to each file's style, specify the `platformClient` signature and the PlatformPanel scope, and express Task 1 Step 1 as replacing the `:28` entry |
| D22 | L3/L8 proofs: `i18n:unused` fails while the keys remain, the `rg` at plan:1305 proves no orphan text, `presentation.test.jsx` only swaps a cursor fixture, and one `spanish.test.jsx` assertion proves the locale | `i18n:unused` checks only `en`; `es` leftovers are caught by `catalog.contract.test.js:45-47,151-157`. The `rg` sees only `t()` call sites, because catalog keys are nested. `presentation.test.jsx:116-119` asserts two deleted keys, throws after L3 and must be deleted. `App.localization.test.jsx:10-30` and `spanish.test.jsx:142-146` already prove `labelRowsPerPage`; only the `getItemAriaLabel` part is new, and it needs `ThemeProvider`/`TablePagination` imports | `check-unused-i18n.mjs:13`; i18next-cli `status.js:841-842`; `i18n/index.js:153-159,223-225` [i18n-mui-1, -2, -3; paths-client-tests-15; cursor-scope-sweep-13] | minor | Reword the proofs, schedule the deletion of `presentation.test.jsx:116-119`, cite the existing locale tests, and keep only the aria-label assertion as new coverage |
| D23 | Task 11A's template uses `t('common:actions.tryAgain')`, and the delivery note lists five removed keys (plan:1188, :1410) | `PlatformIdentitiesPage.jsx:445` uses `platform:identities.retry`, the key's only call site (`en`/`es` `platform.json:75`, same words). Applied literally, the template orphans it, and `i18n:unused` (which scans `platform`) fails | `staticUnusedNamespaces.js:1` [i18n-mui-4; error-contracts-10] | minor | Keep `identities.retry` on that screen, or delete it in `en` and `es` as a sixth, declared removal |
| D24 | Scope entries are exact: Task 11A modifies "the six screens"; Task 12's searches and follow-up note are complete (plan:977, :1086, :1263, :1411) | `PlatformRetentionPage.jsx` has no paginated read (`:171-174`), yet Task 11A counts it. Domain identifier doc comments justify `CompareTo` and the operators with keyset cursors, and their consumers disappear after Task 7 (`RoleAdministrationStore.cs:28`, `MembershipAdministrationStore.cs:26,41`, `PlatformOperationalProjectionReader.cs:38,95`); Task 12 would surface them. Task 12 Step 1 also hits non-pagination `cursor` tokens, which need triage. The follow-up note omits `whatsapp-bot/SCREENS.md:53` | `TenantId.cs:17-21`; `RoleId.cs:17-24`; `MembershipId.cs:17-21`; `InvitationId.cs:17-22`; package-lock files; `ClientApp-Angular/src/styles.scss:120` [error-contracts-11; cursor-scope-sweep-10, -11, -12, -14] | minor | Drop `PlatformRetentionPage` from Task 11A. Name the four Domain files and decide the fate of `CompareTo` and the operators. Add exclusions for `package-lock.json` and `*.scss`. Add `SCREENS.md:53` to the follow-ups |

### Checked and refuted

These sub-claims were rejected or narrowed by the adversarial re-check or by this phase's spot-checks:

- The Task 4 compile-error RED does not violate `engineering-rules.md:47`, which applies to defect fixes, not new types.
- Five PlatformDirectoryContractTests tests break, not six. `:37-51` still passes because the plan keeps the response names.
- The tenant page-response records are already in Task 8's scope (plan:575-576, :612-621). Only the usings were missing (D02).
- The store `MaximumLimit` constants, `Cursor`, `OpaqueCursor` and the reader encode/decode helpers are covered by the deletion of `PlatformDirectoryQuery`, the generic "local cursor value types/helpers" entry, and the Task 12 name search. Only the three page records go unnamed (D01).
- The new Task 8 Step 5 tests do not run as the default user and hit `recent_mfa_required`. The caller is anonymous and hits `UnauthorizedAccessException` (D13).
- The proposed OpenAPI test is not green overall. Its parameter-name assertions are RED; only `invalid_request` is green (D08).
- The depth change of `problemDetails.js`'s schema import is not an omission, because Task 1 Step 2 allows relative-path rewrites.
- `TRACEABILITY.md:18` is a dated verification log and needs no edit.
- `usePlatformRead.test.jsx` was not "removed in 8711656"; it was never committed. The citation is stale either way (D18).
- `SPEC.md:695` (sessions: "no `limit`, `cursor` and no envelope") is not matched by the Step 2 regex, and the acceptance wording does not flag it.
- `TRACEABILITY.md:158` has no cursor or default wording, and SPEC rows `:1033`/`:1037` need no separate default clause (D09).
- In-place edits of §14.5 route rows are established practice (commit 8711656). The ADR "Amended 2026-09-06" markers are pre-acceptance fold-ins, not a post-acceptance convention.
- ADR-004 decision 17 is not binding, because the ADR is Proposed (D17).
- The "keep" instructions for `:306-309`/`:1033`/`:1037` do not ask to add clauses; they are simply vacuous where no clause exists.
- `presentation.test.jsx:25`'s `pageOf` fixture is covered by Task 11 Step 4. Only the `:116-119` test is unplanned (D22).
- The Task 12 Step 1 cursor search is a semantic triage ("no cursor-pagination leftovers"), not a literal empty result, so it is satisfiable.
- The `StackBaselineTests.cs` and `PlatformRetentionPage.jsx` list entries are conditional and harmless. Only Task 11A's "six screens" count is wrong (D24).
- The Domain doc comments are within Task 12's "Entire repository" scope, not missed by every task (D24).
- Task 8 Step 4 does run `OpenApiContractTests`, so the binding-test break would surface at that gate. The missing instruction is the defect (D07).
- Nothing in the evidence was left unchecked or had a failed refuter.

### Out-of-scope cursor sites

These are intentionally left unchanged by this change and listed for the delivery note:

- **Proposed, unimplemented specification:** `docs/features/whatsapp-bot/SPEC.md:295` (`GET /api/whatsapp/links`) and `:304` (`GET /api/platform/bot/capabilities`), both `{ items, nextCursor }`; `docs/features/whatsapp-bot/SCREENS.md:53` ("Bounded and cursor-paged"). There is no code under `src` and no tests.
- **Historical records:** `docs/features/error-handling/AUDIT-FINDINGS.md:383,596,1049,1061`; `docs/features/error-handling/REMEDIATION-PLAN.md:292`; `docs/features/identity-access/TASKS.md:936,940`; `docs/superpowers/plans/2026-08-31-identity-access-foundation.md:1116,1157`.
- **Non-product mockups** (`docs/mockups/README.md:3-6`): `docs/mockups/api/src/server.ts:754-759`, `docs/mockups/web/src/lib/usePaged.ts`, `docs/mockups/web/src/lib/api.ts:83,239-246`, `docs/mockups/web/src/components/PageHeader.tsx:21-41`.
- **Generated and gitignored, regenerated on build:** `src/Web/wwwroot/openapi/v1.json` (7 `limit`, 7 `cursor`, 14 `nextCursor`), `src/Web/ClientApp/src/web-api-client.ts` (NSwag), and `ClientApp-Angular/src/app/web-api-client.ts` (absent).
- **Non-pagination `cursor` tokens:** `src/Web/ClientApp/package-lock.json` and `src/Web/ClientApp-Angular/package-lock.json` (`cli-cursor`/`restore-cursor`); `ClientApp-Angular/src/styles.scss:120` (`cursor: pointer`); `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs:338-354`.
- **Negative statement that must stay:** `SPEC.md:695` (sessions list has no `limit`/`cursor`).

### Affected Areas

"(not in plan)" marks files that the confirmed discrepancies add.

- **Application:**
  - `src/Application/Common/Models/PaginationQuery.cs` and `PaginatedList.cs` (new).
  - `IdentityAccess/Platform/Queries/PlatformDirectories.cs`.
  - `PlatformDirectoryHandlers.cs` (not in plan).
  - `Platform/IPlatformOperationalProjectionReader.cs` (not in plan).
  - `Roles/RoleRequests.cs`, `Roles/RoleHandlers.cs` (not in plan), `Roles/IRoleAdministrationStore.cs` (`RolePage`).
  - `Members/MembershipRequests.cs`, `Members/MembershipHandlers.cs` (not in plan), `Members/IMembershipAdministrationStore.cs` (`MemberPage`, `InvitationSummaryPage`).
- **Infrastructure:**
  - `src/Infrastructure/Data/Pagination/PaginationExtensions.cs` (new).
  - `Platform/PlatformOperationalProjectionReader.cs`.
  - `IdentityAccess/RoleAdministrationStore.cs` and `IdentityAccess/MembershipAdministrationStore.cs` (re-wrap, D05).
- **Domain, comments and possibly dead operators (not in plan):** `IdentityAccess/Tenants/TenantId.cs`, `Authorization/RoleId.cs`, `Memberships/MembershipId.cs`, `Invitations/InvitationId.cs`.
- **Web:**
  - `Endpoints/Identity/RoleEndpoints.cs` and `Endpoints/Identity/MembershipEndpoints.cs` (inline tenant page responses at `:116`, `:127` and `:137`).
  - `Endpoints/Platform/PlatformEndpoints.cs` and `Endpoints/Platform/Contracts/PlatformDirectoryContracts.cs`.
  - `Infrastructure/ResultHttpExtensions.cs` (optional 202 overload) and `Infrastructure/ApiProblemMetadata.cs` (comments only).
- **SPA:**
  - `src/api/problemDetails.js`, `apiTransport.js`, `problemCodes.json` and `pagination.js`; `src/components/ProblemMessage.jsx`.
  - `features/identity/fieldErrors.js` (split decision, D19).
  - The 24 source importers of the moved modules.
  - `features/identity/api/identityClient.js`, `features/platform/api/platformClient.js` and `features/identity/useRead.js`.
  - `RolesPage.jsx`, `MembersPage.jsx` (list, resume walk and role catalogue), `InviteMemberPage.jsx` (list and role catalogue), `PlatformPanel.jsx` and `PlatformIdentitiesPage.jsx`.
  - `i18n/locales/{en,es}/platform.json` and `identity.json`.
- **.NET tests:**
  - The declared list (plan:111-121).
  - Not in plan: `Platform/PlatformProjectionTests.cs`, `PlatformIdentityLifecycleTests.cs`, `PlatformDirectoryAccessTests.cs`, `PlatformAdministrationTests.cs`, `Api/OpenApiContractTests.cs` and `Api/ProblemDetailsContractTests.cs`.
  - New: `tests/Application.UnitTests/Common/Models/PaginationQueryTests.cs` and `PaginatedListTests.cs`.
- **SPA tests:**
  - The declared list (plan:95-110).
  - Not in plan: `test/identityFiles.contract.test.js`, `features/identity/ProblemMessage.test.jsx`, `features/identity/api/apiTransport.test.js`, `features/identity/problemCatalogue.contract.test.js`, `i18n/catalog.contract.test.js`, `test/identityServer.js`, and `fieldErrors.test.js` if the helpers move.
- **Documents and standards:**
  - `docs/features/identity-access/SPEC.md` (`:201`, `:239`, `:306-309`, `:464-465`, `:1033`, `:1037`, `:1065`).
  - `TRACEABILITY.md` (`:86`, `:92`, `:99`, plus a dated evidence paragraph).
  - `docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md:36` (decide).
  - `.agents/skills/error-handling-standards/SKILL.md:109` and `references/error-handling-rules.md:288,328,400,476`.
  - `.agents/skills/frontend-design-standards/references/ui-composition-rules.md:317`.

### Approaches

1. **The approach is the plan.** Its decisions (E1-E12, L1-L8), architecture and task order are accepted inputs, with no alternatives. The confirmed discrepancies force only these adjustments:
   - **Compile scope and TDD sequencing (D01-D05):**
     - Add the handlers, the reader port and the page records to Task 6.
     - Fix the `PaginatedList` test arguments and the missing usings.
     - Add the view re-wrap for four list methods.
     - Move the `OpenApiContractTests.cs:491` path edit into Task 1.
     - Run Tasks 6-8 as one compile unit, with an HTTP-level RED written first, and schedule the test doubles.
   - **Test inventory and test rewrites (D06, D07, D12-D14):**
     - Complete both declared test lists.
     - Migrate the `limit` binding-refusal test.
     - Rewrite the cursor assertions in the two slow SPA tests.
     - Add `ActiveOwnerAsync()` to the new Platform tests.
     - State the fate of each existing cursor test and of the shape test.
   - **Error contract precision (D08, D15):** E3 is already declared through `WithBodyBindingFailureCode`. Correct the E5 code list, and scope E1/E2 to Int32 values.
   - **Tenant behaviour made explicit (D09-D11):**
     - Record the tenant default change from 100 to 25.
     - Fix the null page passed by the role-catalogue callers, and decide how a role catalogue reads every role.
     - Extend the resume walk to MembersPage, and describe today's failure behaviour accurately.
   - **SPEC and standards amendments (D16-D20):**
     - Add IA-REQ-045 `:239`.
     - Treat `:1065` as history, add a dated TRACEABILITY evidence paragraph, and decide on ADR-004 decision 17.
     - Tighten the Task 8A row edits and searches.
     - Split the shared field-error rendering helpers away from Identity.
     - Update the error-handling and frontend standards documents that name retired paths or cursor controls.
   - **SPA and localization details (D21-D24):**
     - Keep the RolesPage permission catalogue.
     - Adapt the test snippets to each file's style.
     - Specify the `platformClient` signature and the PlatformPanel scope.
     - Correct the L3/L8 proof wording, and delete `presentation.test.jsx:116-119`.
     - Keep `identities.retry` or declare its removal.
     - Drop PlatformRetentionPage from Task 11A, name the Domain comment files, and add `SCREENS.md:53` to the follow-ups.
   - Effort: High (backend, SPA, tests, SPEC and standards documents, across roughly 70 files).

### Evidence for pending decisions

**(a) Clamp versus `400 validation_failed` for out-of-range `pageNumber`/`pageSize`**

- **Current behaviour:**
  - Clamping is today's documented and tested policy, but only for the Platform directories (`PlatformDirectories.cs:17-18`, `PlatformOperationalProjectionReader.cs:169`, pinned by `PlatformDirectoryContractTests.cs:75-90`).
  - Tenant stores never refuse. An omitted, zero or negative value returns 100 rows, and values above 100 are capped (`RoleAdministrationStore.cs:23`, `MembershipAdministrationStore.cs:21,37`).
  - Both SPA clients already assume clamping: `platformClient.js:14-23` clamps before sending, `identityClient.js:18-21` never sends `limit`, and the plan's `boundedPage` clamps as well.
- **What the SPEC says:** `:239`, `:1033` and `:1037` state "(1–100)" with no refuse or clamp outcome. No list-route row declares `validation_failed`.
- **Cost of `400 validation_failed`:**
  - No validator exists for any List* query.
  - The approved validation vocabulary has no range code with `min`/`max` params (`validationErrorSchema.json`, `ValidationErrorCodes.cs:10-30`), so a new code would need the schema, `errors:validation.<code>` in `en` and `es`, placeholder parity and tests.
  - The tenant list routes would have to declare `ValidationFailed` (`RoleEndpoints.cs:30`, `MembershipEndpoints.cs:25,31`); the Platform routes already declare it but never emit it (`PlatformEndpoints.cs:44`).
  - Task 8A would widen to rows `:306-309`, `:1033` and `:1037` and to IA-REQ-038's validation sub-contract (`:203-215`).
- **How the SPA would handle a 400:**
  - `useRead` would mark it `refused` and clear the rows (`useRead.js:56-61`).
  - `pageNumber`/`pageSize` have no field to claim, so `ProblemMessage` would list unclaimed field errors with the fallback label `errors:validation.fields.unknown`.
  - CLAUDE.md requires input-only field errors to be shown on a field with focus moved there, and `TablePagination` is not a form field.
- **Rules pulling toward refusal:** error-handling rule 2 (`SKILL.md:84`) classifies input-only rules as `validation_failed`. Clamping avoids having such a rule at all, rather than refusing input.
- **Facts that hold under both options:**
  - `TablePagination` cannot produce an out-of-range value, because its page and size come from loaded metadata and `rowsPerPageOptions` is `[10, 25, 50, 100]`. A range refusal would only ever answer hand-built requests. A page past the end is E4's `200`.
  - Non-integer or beyond-Int32 values stay `400 invalid_request` through binding.
  - Neither option logs at `Error`: `ValidationException` is written without logging (`ProblemDetailsExceptionHandler.cs:73-76,94`).
  - Refusal would also need an explicit upper bound on `pageNumber` so the `checked` Skip cannot become a 500.
- **Consequence of clamping as planned:** the tenant default drops from 100 to 25, and `pageSize=0` returns 1 row instead of 100 (D09).

**(b) `docs/features/whatsapp-bot/SPEC.md` as a follow-up**

- **Status and content:** the SPEC is "Proposed for approval" (`:3`), and `:286` says routes are contractual drafts and generated OpenAPI becomes the source of truth. `:295` and `:304` specify `{ items, nextCursor }` without naming query parameters. `SCREENS.md:53` also says "Bounded and cursor-paged". `TRACEABILITY.md` in that folder has no cursor mention. `:6` declares a dependency on the identity-access first increment.
- **Why deferring is safe now:**
  - No code under `src` or `tests` implements or references these routes, and no problem code, catalog key or journey is tied to them.
  - Leaving the SPEC unchanged breaks no build, test or planned gate: Task 8A Step 2 searches only `docs/features/identity-access`, Task 12 Step 1 only `src` and `tests`, and acceptance :1417 covers implemented endpoints.
- **Why it cannot wait indefinitely:**
  - After this change `nextCursor` stays in `FORBIDDEN_SUCCESS_KEYS` (E7), so a client built to the unamended SPEC would get `unreadable_response`. The alignment must land before that feature is implemented.
  - The planned `/api/platform/bot/capabilities` route would inherit the Platform directory shape defined by IA-REQ-045 (`SPEC.md:239`), which D16 amends.
- **Gap in the plan:** its follow-up note cites only `SPEC.md:295,304` and misses `SCREENS.md:53`.

### Risks

- **Known baselines, not regressions:**
  - The full Vitest run has 3 tests that exceed the 15 s timeout under parallel load and pass alone (`vitest.config.js:23`). Two are `ExternalProofResume.test.jsx` "resumes a members/roles target beyond the first page" and `IdentityAccessReviewRevalidation.test.jsx` R6C "101 distinct records". Both are rewritten by this change (D12), so the timeouts may change either way.
  - `tests/Infrastructure.IntegrationTests/IdentityAccess/IndependentDevelopmentRetentionReviewTests.cs` (3 tests, category IndependentDevelopmentReview) has failed on purpose since b9a30d1; CI excludes it.
  - The pass or fail state of `IndependentDevelopmentAdministrationReviewTests.cs` (same category, 4 tests) is unknown, and this change edits it (D01).
- **Build and gate risks:**
  - Tasks 6-8 leave every .NET project uncompiled between steps (D04). Mid-plan gates cannot run as written.
  - D16 (IA-REQ-045) makes Task 8A Step 2 fail as written. The plan cannot complete its own gate until the plan is corrected.
- **User-visible behaviour changes:**
  - The tenant directories and SPA screens drop from 100 to 25 rows per page (D09).
  - Role pickers in tenants with more than 25 roles lose options and show raw ids unless D10 is resolved.
- **Retry semantics:** E8/E9 retry the requested page, while `PlatformPanel.test.jsx:425-456` pins that "Try again" after a failed "More organizations" reloads page one. That test must be rewritten deliberately, not to make a regression pass.
- **Scope ambiguity:**
  - `useRead.js`, `useSubmit.js` and `fieldErrors.js` stay under `features/identity` while Platform and shell code import them (for example `PlatformPanel.jsx:27-30`, `NavMenu.jsx:53`). The scope of acceptance :1424 is ambiguous (D19).
  - Whether the PlatformPanel administrators and audit directories get a pagination control is undecided. Adding one may read as "inventing pagination" (frontend SKILL.md:42); leaving it out keeps rows past page 1 unreachable.
- **Localization coverage:** screen tests render without the App `ThemeProvider`, and the `es` smoke journey visits only `/identity`. Localized pagination labels on a real screen are proven only at App level.
- **Document status:** identity-access `SPEC.md:3` is still "Proposed for approval", while the plan calls it approved (plan:49, :735). The amendment convention for §4, §6 and C5 needs a stated approver or a dated note (D17).
- **Mirrored skill copies:** updating `.agents/skills` standards documents (D20) may require updating the mirrored copies under `.claude/skills` and `.codex/skills` (reported byte-identical at sdd-init). Not verified in this phase.
- **Delivery:** CLAUDE.md requires direct commits to `main`, while the SDD 400-line review guard assumes PR slices, and the change is large. `openspec/` is untracked, and journeys need Docker with no AppHost running.

### Ready for Proposal

Yes, with conditions. The plan's architecture and decisions stand. The proposal must carry adjustments D01-D24,
resolving the blocking D16 and every material item, and must surface these choices to the user before sdd-spec:

1. Accept the tenant default page change from 100 to 25 (D09).
2. How a role catalogue reads every role: page walk or `pageSize` 100 (D10).
3. Whether the PlatformPanel administrators and audit directories get `TablePagination` (D21).
4. Fix the Platform list-query property name, `Query` or `Pagination` (D14).
5. Split or keep the `fieldErrors.js` dependency (D19).
6. How `:1065` and ADR-004 decision 17 are treated (D17).
7. Keep or retire `identities.retry` (D23).
