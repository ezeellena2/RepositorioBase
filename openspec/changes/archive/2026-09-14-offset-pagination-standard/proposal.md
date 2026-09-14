# Proposal: Offset Pagination Standard

Change `offset-pagination-standard` · baseline `main` at `2716aa6` · inputs: plan
`docs/superpowers/plans/2026-09-13-cross-project-result-pagination-standard.md` (E1–E12, L1–L8, Task 8A, Task 11A, task
order and declared test list are accepted) and `exploration.md` (evidence for D01–D24).

## Intent

- Seven list routes page with cursors (`limit`/`cursor`/`nextCursor`): `/api/platform/{organizations,identities,admins,audit}`
  and `/api/tenants/{tenantId}/{roles,members,invitations}`. Replace them with one offset standard: `pageNumber`/`pageSize`
  in; `items`, `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, `hasNextPage` out.
- Shared SPA problem infrastructure lives under `features/identity` although Platform and shell code import it. Make it
  module-neutral.
- Keep `Result`/`ApplicationError` internal and RFC 9457 Problem Details for failures, with no generic success envelope.

## Change classification

- **Functional and error-handling work, not a visual change** (`openspec/config.yaml` `rules.proposal`, CLAUDE.md).
- Tests under `tests/` and `*.test.jsx`/`*.test.js` files are edited only file by file, as declared: the plan's "Test Files
  This Functional Change Edits" plus the D06 additions.
- No other test file, page object, `AppRoutes.jsx` or `features/*/api/` file changes unless a declared task names it
  (Task 1 moves, Task 10 clients).
- Any restyling stays bound by CLAUDE.md "Contracts a visual change must never break", including the MUI `Select` rule
  from `5c59ff9`.

## Scope

### In Scope

- Tasks 1–3: `src/api/{problemDetails.js,apiTransport.js,problemCodes.json}`, `src/components/ProblemMessage.jsx` and
  `src/components/problemFields.js` (every module-neutral field-error helper, PD-5); `ResultHttpExtensions` stays the
  only Result-to-HTTP mapper.
- Tasks 4–8: `PaginationQuery`, `PaginatedList<T>`, the EF helper, offset queries, stores and endpoint DTOs.
- Task 8A: the document amendments listed under "Documents touched".
- Tasks 9–11A: `src/api/pagination.js`, client migration, `TablePagination`, read states E8–E11, deletion of the five
  "show more" keys in `en` and `es`.
- Tasks 12–13: cleanup searches, full verification, delivery note.
- Standards documents that name retired paths or cursor controls (D20).
- User-visible consequence, repeated in the delivery note: tenant lists default to 25 rows instead of 100, and
  `pageSize <= 0` means 1 (D09, PD-1).

### Out of Scope

- `docs/features/whatsapp-bot/SPEC.md:295,304` and `SCREENS.md:53` (PD-b confirmed; follow-up: align them with the offset standard before that feature is
  implemented).
- The general visual redesign of combos, tables and components: deferred by the user, not started.
- Historical documents, mockups, generated or gitignored files and non-pagination `cursor` tokens (exploration
  "Out-of-scope cursor sites"); the `SPEC.md:695` negative statement stays.
- The three intentionally failing `IndependentDevelopmentRetentionReviewTests`.
- New error codes (E6) and `.resx` changes (L7).
- Relocating `useRead.js`/`useSubmit.js` and the Identity registration validators in `features/identity/fieldErrors.js`;
  acceptance plan:1424 is read as transport, rendering, and the field-error rendering and selection helpers (PD-5).

## Capabilities

`openspec/specs/` holds only `.gitkeep`; no existing capability names apply.

### New Capabilities

- `api-offset-pagination`: `PaginationQuery`/`PaginatedList<T>`, deterministic ordering, `pageNumber`/`pageSize` parameters
  and offset page DTOs on the seven routes, clamping (E1, E2 scoped to Int32), the past-the-end page (E4), default page
  size 25 (D09).
- `list-read-error-contract`: codes declared and emitted by list routes (E3, E5 as corrected by D15, E6), logging (E12),
  and the strict client page reader: `readPage`, `nextCursor` kept forbidden, drift as `unreadable_response` (E7),
  null-safe paging arguments (D10).
- `spa-pagination-ui`: `TablePagination` on the five migrated screens plus PlatformPanel administrators and audit (PD-6),
  per-page read states (E8–E11), past-the-end correction (E4), bounded resume walks (D11), full role catalogue (PD-2),
  MUI locale labels and the five key deletions (L1–L5, L8).
- `shared-problem-infrastructure`: module-neutral SPA problem transport, catalogue and `ProblemMessage`, plus every
  module-neutral field-error rendering or selection helper in `src/components/problemFields.js`, so that no module
  outside `features/identity` imports `features/identity/fieldErrors.js` (D19, PD-5 as expanded, L6); unchanged
  `problemCodes.json` content and catalogue gates; `ResultHttpExtensions` as the sole Result-to-HTTP mapper with no
  success envelope.

### Modified Capabilities

- None. The `docs/features/identity-access/SPEC.md` amendment is a Task 8A document deliverable, not an openspec
  capability.

## Approach

The plan's architecture and task order (1–8, 8A, 9–11, 11A, 12, 13) stand, with E1–E12 and L1–L8 unchanged, as adjusted
below.

## Plan adjustments (D01–D24)

Evidence: `exploration.md` "Discrepancies". PD ids refer to the next section.

| Id | Severity | Adjustment | Plan task(s) |
| --- | --- | --- | --- |
| D16 | blocking | Add IA-REQ-045 (`SPEC.md:239`) to Task 8A and the pinned-row list (plan:42-47): `limit`/`cursor`/`nextCursor` become `pageNumber`/`pageSize` and the offset metadata, keeping its `/api/identity/*` no-envelope sentence, so the Step 2 search can return empty | 8A |
| D01 | material | Add `RoleHandlers.cs`, `MembershipHandlers.cs`, `PlatformDirectoryHandlers.cs` and `IPlatformOperationalProjectionReader.cs` to Task 6; name `RolePage`, `MemberPage`, `InvitationSummaryPage` for replacement; note `IndependentDevelopmentAdministrationReviewTests.cs:114,180` deserializes two | 6 |
| D03 | material | Build the `PaginatedList<int>` test with `PageNumber:`/`PageSize:`/`TotalCount:` or positional arguments | 4 |
| D04 | material | Move the `OpenApiContractTests.cs:491` catalogue-path edit into Task 1; run Tasks 6–8 as one compile unit opened by an HTTP RED (parameter names, response members, clamp); schedule the test-double and direct store-call edits | 1, 6–8 |
| D05 | material | Re-wrap paged rows into views (`new PaginatedList<TView>(views, page.PageNumber, page.PageSize, page.TotalCount)` or a `Map`) for roles, members, invitations and audit | 7 |
| D06 | material | Declare the four Platform test files, `OpenApiContractTests.cs`, `ProblemDetailsContractTests.cs`, both new unit-test files, the six SPA files and `fieldErrors.test.js` (under PD-5), each with its edit; list the three test-file moves in Task 1; keep the `identityFiles.contract.test.js` catch counts | Test list, 1, 13 |
| D07 | material | Migrate `Every_numeric_limit_binding_refusal_is_emitted_only_as_declared` to `pageNumber=not-a-number`, `pageSize=not-a-number` and a beyond-Int32 case; rename it; cite it as E3's emission proof; update `TRACEABILITY.md:86,92` | 8, 8A |
| D08 | material | Restate E3 and Task 8 Step 3: all seven routes already declare `invalid_request` through `WithBodyBindingFailureCode`; keep it on the renamed parameters; the inline declaration is optional; only parameter-name assertions are RED | 8 |
| D09 | material | Record the tenant default change (100 to 25; `pageSize <= 0` means 1) next to E1 and in the delivery note; rewrite R6C as a `pageNumber` walk; add no default clause to `:1033`/`:1037` (PD-1) | 6–8, 11A, 13 |
| D10 | material | Name the `MembersPage` and `InviteMemberPage` role-catalogue callers in Tasks 10–11; make `boundedPage`/`paginationSearch` null-safe; read every role with a `hasNextPage` walk, tested with more than 25 roles (PD-2) | 9–11 |
| D11 | material | Extend the Task 11A Step 4 page walk to `MembersPage.jsx:191-199`; correct "silently, as today" and keep today's behaviour, where a failed walk renders through `setActionProblem` | 11A |
| D12 | material | Move the recorded-read, continuation-control and `limit`/`CURSOR` assertions in `ExternalProofResume.test.jsx` and `IdentityAccessReviewRevalidation.test.jsx` to offset equivalents; keep every resumed-or-refused assertion; size fixtures to span at least two pages | 11A |
| D13 | material | Add `await PlatformScenario.ActiveOwnerAsync();` before seeding in both new tests, plus the `Common.Models` using; cite the clamp test at `:75-90` | 8 |
| D14 | material | Rewrite the parameter and audit newest-first tests over pages 1 and 2; turn the limit clamp test into a `pageSize` clamp test; delete the two cursor-only tests; rename `:37-51`; assert a `Query` property typed `PaginationQuery` (PD-3) | 8 |
| D17 | material | Amend section 4 (IA-REQ-038, IA-REQ-045) and the route rows normatively; keep `:1065` as C5 history with a dated note; add a dated TRACEABILITY evidence paragraph; reword ADR-004 decision 17 (PD-4) | 8A |
| D19 | material | Move every module-neutral field-error helper exported by `fieldErrors.js` (`validationDetailText`, `selectFieldErrors`, `claimedFieldNames`, `unclaimedFieldErrors`, `fieldIdFor`, `clearFieldError`, `fieldError`, `firstInvalid`, `fieldErrorText`) to `src/components/problemFields.js`; its 11 importers import it directly, with no re-export shim; the four registration exports stay in Identity; extend L6 with `errors:unknown` and `errors:validation.*`; widen the Task 12 search to `identity/fieldErrors` outside `features/identity` (PD-5, expanded 2026-09-13) | 1, 12 |
| D20 | material | In the same change, point `error-handling-standards` `SKILL.md:109` and `error-handling-rules.md:288,328,400,476,594` at the new paths (`:594`, the field-helper location, follows the PD-5 expansion) and turn `ui-composition-rules.md:317` from cursor to offset controls; widen the Task 12 Step 2 search to `.agents/skills` | 12 |
| D02 | minor | Add the EF Core and `Common.Models` usings to the helper, endpoints, Platform contracts and three ports; reword Task 5's rationale to "keep query execution in Infrastructure" | 5, 6, 8 |
| D15 | minor | Correct E5: `recent_mfa_required` and the declared-but-unemitted `validation_failed` cover all four Platform directories; `validation_failed` stays declared (rule-10 gap recorded; unreachable because PD-a confirmed clamping); scope E1/E2 and plan:1430 to Int32 | 8, 13 |
| D18 | minor | Insert the IA-REQ-038 sentence as an indented sub-paragraph at `:201`; adapt each row edit (`recent_mfa_required` only on `:306-307`, `:308` untyped, list 404 at `:1028`); add a case-insensitive `cursor`/`nullable-limit` TRACEABILITY check excluding `SPEC.md:695`, `TASKS.md:936,940`; fix the `:92` citations | 8A |
| D21 | minor | Load the RolesPage permission catalogue once, outside page changes; adapt snippets to each test file's style (`identityClient.test.js` mocked `send`; the `identityFiles.contract.test.js:28` entry); give `platformClient` list methods `(page, options)`; PlatformPanel scope per PD-6 | 1, 10, 11 |
| D22 | minor | Reword the L3/L8 proofs (`i18n:unused` covers `en`, `catalog.contract.test.js` covers `es`, the key search sees only call sites); delete `presentation.test.jsx:116-119`; cite `App.localization.test.jsx:10-30`, `spanish.test.jsx:142-146`; add only the `getItemAriaLabel` assertion | 11, 12 |
| D23 | minor | Keep `platform:identities.retry` at `PlatformIdentitiesPage.jsx:445` instead of `common:actions.tryAgain`; removals stay five (PD-7) | 11A |
| D24 | minor | Drop `PlatformRetentionPage.jsx` from Task 11A; name the four Domain ID files, removing `CompareTo`/operators only if no consumer remains, else rewording the keyset comments; exclude `package-lock.json`, `*.scss` from Task 12 Step 1; add `SCREENS.md:53` to follow-ups | 11A, 12, 13 |

## Confirmed decisions

The user confirmed every item on 2026-09-13, one question at a time: PD-a, then PD-b, then PD-1–PD-7 together with the
adjustment details listed at the end of this section. Later the same day the user expanded PD-5.

- **PD-a — Clamp instead of `400 validation_failed`.** Confirmed: clamp out-of-range `pageNumber`/`pageSize`, as
  E1/E2 say, scoped to Int32 values; non-integer and beyond-Int32 values stay `400 invalid_request` through binding.
  Evidence: exploration.md "Evidence for pending decisions (a)".
- **PD-b — WhatsApp SPEC as a follow-up.** Confirmed: `docs/features/whatsapp-bot/SPEC.md:295,304` and
  `SCREENS.md:53` stay out of scope, with a recorded follow-up: the alignment must land before that feature is
  implemented, because `nextCursor` stays in `FORBIDDEN_SUCCESS_KEYS`. Evidence: exploration.md "(b)".
- **PD-1 (D09) — Confirmed.** The tenant default page size changes from 100 to 25, and
  `pageSize <= 0` means 1. This is the explicit, user-visible consequence of E1, recorded here and in the delivery note.
- **PD-2 (D10) — Confirmed.** A role catalogue reads every role through a client helper that
  walks `pageNumber` until `hasNextPage` is false, serving the MembersPage and InviteMemberPage pickers. Paging arguments
  are null-safe. A test covers more than 25 roles.
- **PD-3 (D14) — Confirmed.** The Platform list-query property keeps the name `Query`, typed
  `PaginationQuery`, and the `PlatformApplicationShapeTests` assertion is updated to match.
- **PD-4 (D17) — Confirmed.** The normative change goes in section 4 (IA-REQ-038, IA-REQ-045)
  and in the route rows. `:1065` stays as C5 history with a dated note. TRACEABILITY gets a dated evidence paragraph.
  ADR-004 decision 17 is reworded (the ADR is Proposed).
- **PD-5 (D19) — Confirmed, expanded by the user on 2026-09-13.** Every module-neutral field-error rendering or
  selection helper exported by `features/identity/fieldErrors.js` moves to `src/components/problemFields.js`, next to
  `src/components/ProblemMessage.jsx`, so that no module outside `features/identity` imports error-rendering helpers
  from Identity (acceptance plan:1424, base-product capability gate). This supersedes the first scope of three helpers
  (`unclaimedFieldErrors`, `fieldIdFor`, `validationDetailText`). Importers import from the new path, with no re-export
  shim (plan Task 1 Step 2). Classification, from `fieldErrors.js` at `2716aa6` (evidence in design.md, "Problem
  infrastructure"):
  - **Moves** (generic over a problem, a field-error map, field names, field ids or `t`): `validationDetailText`,
    `selectFieldErrors`, `claimedFieldNames`, `unclaimedFieldErrors`, `fieldIdFor`, `clearFieldError`, `fieldError`,
    `firstInvalid` and `fieldErrorText`, with the private `REQUIRED_FIELD_KEYS`, `sameFieldName` and `detailsForField`.
  - **Stays** (Identity registration rules): `organizationRegistrationFields`, `personalRegistrationFields`,
    `validateOrganizationRegistration` and `validatePersonalRegistration`, with the private `detail`, `addError` and the
    email, password-shape, name, CUIT and DNI rules.
  - **Flagged:** none. No private helper serves both sets, so `fieldErrors.js` imports nothing from `problemFields.js`.
  - No test file is added or edited for the expansion: the only test importing `fieldErrors.js` is `fieldErrors.test.js`,
    for `validatePersonalRegistration`, which stays.
- **PD-6 (D21) — Confirmed.** The PlatformPanel administrators and audit directories get a
  `TablePagination`; otherwise rows past page 1 are unreachable. This is user-visible.
- **PD-7 (D23) — Confirmed.** `platform:identities.retry` stays on PlatformIdentitiesPage, so
  there is no sixth key removal.
- **Adjustment details approved with PD-1–PD-7.**
  - D11: a failed resume walk keeps rendering through `setActionProblem`.
  - D12: rewritten fixtures use the smallest counts that span two pages.
  - D15: `validation_failed` stays declared on the Platform directories, with the rule-10 gap recorded.
  - D21: `platformClient` list methods take `(page, options)`, and the RolesPage permission catalogue loads once.
  - D24: Domain `CompareTo` and the comparison operators are removed only when no consumer remains.
  - `useRead.js` and `useSubmit.js` stay in `features/identity`.

## Documents touched

| Document | Locations | Note |
| --- | --- | --- |
| `docs/features/identity-access/SPEC.md` | `:201`, `:239`, `:306-309`, `:464-465`, `:1033`, `:1037`, `:1065` | Status (`:3`) is "Proposed for approval", only §14 C1–C7 accepted, while the plan calls it approved |
| `docs/features/identity-access/TRACEABILITY.md` | `:86`, `:92`, `:99`, plus a dated evidence paragraph | `:18` and `:158` unchanged |
| `docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` | decision 17 (`:36`) | Proposed; no accepted ADR is touched |
| `.agents/skills/error-handling-standards/` | `SKILL.md:109`; `references/error-handling-rules.md:288,328,400,476,594` | D20; `:594` names the field-helper location and follows the PD-5 expansion |
| `.agents/skills/frontend-design-standards/references/ui-composition-rules.md` | `:317` | D20 |
| Mirrors under `.claude/skills`, `.codex/skills` | none found | Exploration flagged possible copies; a read-only check found no `*-standards` folder in either; confirm at sdd-tasks |

## Affected Areas

| Area | Impact | Description |
| --- | --- | --- |
| `src/Application/Common/Models/` | New | `PaginationQuery.cs`, `PaginatedList.cs` |
| `src/Application/IdentityAccess/{Platform,Roles,Members}/` | Modified | Queries, handlers, ports, page records (D01) |
| `src/Infrastructure/Data/Pagination/` | New | `PaginationExtensions.cs` |
| `src/Infrastructure/{Platform,IdentityAccess}/` | Modified | Reader and stores; view re-wrap (D05) |
| `src/Domain` (four ID value objects) | Modified | Keyset comments and operators (D24) |
| `src/Web/Endpoints/{Identity,Platform}/`, `src/Web/Infrastructure/ResultHttpExtensions.cs` | Modified | Parameters, page DTOs, declarations |
| `src/Web/ClientApp/src/api/`, `src/Web/ClientApp/src/components/{ProblemMessage.jsx,problemFields.js}` | New (moved) | Shared problem infrastructure, field-error helpers (PD-5), `pagination.js` |
| `src/Web/ClientApp/src/features/{identity,platform}/` | Modified | 24 importers, clients, `useRead.js`, five screens plus PlatformPanel; `fieldErrors.js` keeps only registration rules, and its 11 importers take the new path, which adds `platform/shared/PlatformStepUpForm.jsx` (PD-5) |
| `src/Web/ClientApp/src/i18n/locales/{en,es}/` | Modified | Five key deletions |
| `tests/` and SPA test files | Modified | Declared list plus D06 |

Full inventory: exploration.md "Affected Areas".

## Risks

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| Known baselines: 3 Vitest 15 s timeouts, including the two tests D12 rewrites; 3 intentional `IndependentDevelopmentRetentionReviewTests` failures; unknown state of `IndependentDevelopmentAdministrationReviewTests`, which D01 edits | High | Record baselines before batch A; rerun timeouts alone; keep `TestCategory!=IndependentDevelopmentReview`; run the administration category deliberately before and after |
| Tasks 6–8 do not compile between steps (D04) | High | One compile unit, HTTP RED first, one commit |
| SDD 400-line review guard versus CLAUDE.md direct-to-main delivery (about 70 files) | High | Resolve `delivery_strategy` at sdd-tasks; map batches A–D to commits |
| Between batches A and C, `main` serves offset pages to a cursor-reading SPA | Med | Decide at sdd-tasks whether A–C push together |
| Journeys need Docker, Playwright Chromium, the generated `v1.json` and no running AppHost | Med | Check prerequisites before Task 13; if unavailable, report and do not commit |
| `openspec/` is untracked | Med | Decide at sdd-tasks whether SDD artifacts are committed, and in which batch |
| User-visible changes: tenant default 25 rows, PlatformPanel pagination, role pickers | Med | PD-1, PD-6, PD-2; delivery note |
| `SPEC.md:3` is "Proposed for approval" while the plan calls it approved | Med | Dated amendment note (PD-4); name the approver at sdd-spec |
| `PlatformPanel.test.jsx:425-456` pins a page-one retry; E8/E9 retry the requested page | Med | Declared deliberate rewrite, never a regression pass |
| Spot-check contradicts exploration: no mirrored standards copies exist | Low | Keep exploration's resolution: confirm at sdd-tasks |

## Rollback Plan

- **Delivery shape:** each apply batch is committed directly to `main` after its verification: A (Tasks 1–8A), B (9–10),
  C (11–11A), D (12); Task 13 gates the pushes.
- **Revert:** `git revert` the batch commits newest first and push; no history rewrite or force push. D reverts alone;
  A–C revert together.
- **API and SPA together:** batch A changes the wire shape and batches B–C teach the SPA to read it. The client reader is
  strict (E7), so a one-sided revert leaves the seven list screens reporting `unreadable_response`. The SPEC amendment
  (8A, in A) reverts with the code it describes.
- **No data migration:** pagination is read-only, with no EF migration, schema change, persisted cursor or new code
  (E5, E6); a revert needs no database step.

## Dependencies

- PD-a, PD-b and PD-1–PD-7 confirmed by the user on 2026-09-13; PD-5 expanded by the user the same day.
- Docker, Playwright Chromium and the gitignored `src/Web/wwwroot/openapi/v1.json` for the journeys.

## Success Criteria

- [ ] `cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npm run i18n:unused && npx vite build` passes (Task 13
      Steps 3, 4, 6), recorded timeout baselines aside.
- [ ] `dotnet test` passes for `tests/Domain.UnitTests`, `tests/Application.UnitTests` and
      `tests/Application.FunctionalTests` with `--filter "TestCategory!=IndependentDevelopmentReview"`, and the solution
      builds (Task 13 Steps 1, 2, 5, 7).
- [ ] `dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`
      passes, Spanish smoke included (Step 8).
- [ ] `git status --short -- tests` lists only the declared files (plan list plus D06).
- [ ] The Task 8A Step 2 search returns empty (D16), and the TRACEABILITY check shows only reviewed exclusions (D18).
- [ ] Task 12: no cursor-pagination leftovers; no `identity/fieldErrors` import outside `features/identity`, and no
      retired path in `.agents/skills` (PD-5, D20); `problemCodes.json` and `errors.json` content unchanged (E5, E6).
