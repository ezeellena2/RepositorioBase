# Tasks: Account Setup Onboarding

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 2,700–4,300 in 16 slices (additions + deletions; 25–45 per test) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | 1 → 2a-0 → 2a-i → 2a-ii → 2b → 3a → 3b → 4 → 5a-i → 5a-ii → 5b → 6a-i → 6a-ii → 6b → 7a → 7b |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

- Each "PR" is one verified conventional commit pushed to `origin/main` (CLAUDE.md): no branch, no pull request.
- The 16 slices refine the proposal's seven deliveries: order and copy (1); activation (2a-0, 2a-i, 2a-ii, 2b); signal (3a, 3b); form reuse (4); setup screen (5a-i, 5a-ii, 5b); guard, redirect and journeys (6a-i, 6a-ii, 6b); Google carry (7a, 7b).
- Delivery is ask-on-risk: the orchestrator confirms this split with the user before apply starts.
- The guard (6a-ii) never ships before both setup steps (5a-ii and 5b).

### Slices whose upper estimate exceeds 350 lines

| Slice | Lines | Fallback cut |
|---|---|---|
| 2a-i | 300–510 | 2a-i-a = 3.1–3.2 (typed outcome, still `202`, no test edit); 2a-i-b = 3.3–3.18. Refinement 1 keeps every first failing test with its behavior: if 2a-i-b still measures above 400, stop and ask the user for a size exception |
| 3a | 340–510 | 3a-i = 6.1–6.12 (230–355); 3a-ii = 6.13–6.17 (110–160) |
| 7b | 280–425 | 7b-i = 16.1–16.7 (170–260); 7b-ii = 16.8–16.13 (110–165) |

Each part of a cut is its own verified commit. Every RED test of the slice is written and seen failing before its first GREEN task, so a later part keeps its RED evidence.

### Suggested Work Units

| Unit | Goal | Depends on | Lines |
|---|---|---|---|
| 1 | CUIT or DNI first; CUIT-first copy | none | 150–230 |
| 2a-0 | Pre-change fixtures, no behavior change | none | 80–135 |
| 2a-i | Immediate activation, `204`, `Registered` replay | 2a-0 | 300–510 |
| 2a-ii | Replay by caller scope | 2a-i | 70–120 |
| 2b | Status-aware registration in the SPA | 2a-i | 150–240 |
| 3a | `setupRequired` signal | 2a-i | 340–510 |
| 3b | Strict SPA reader | 3a | 160–260 |
| 4 | Form-reuse refactor | 1 | 110–190 |
| 5a-i | Setup shell | 3b | 230–340 |
| 5a-ii | Personal step | 5a-i, 4 | 190–300 |
| 5b | Company step | 5a-ii, 2b | 140–210 |
| 6a-i | Exemption list, member fixtures | none | 70–110 |
| 6a-ii | Guard, setup journeys | 5b, 6a-i | 200–310 |
| 6b | `/personal/register` redirect | 6a-ii | 80–135 |
| 7a | Shared Google button | 6a-ii | 160–240 |
| 7b | Type carry, return page | 7a, 5b | 280–425 |

Each phase states its focused test command, runtime harness, rollback boundary and declared test edits. Rollback reverts on `main`, newest first, never force-pushing (design §O).

## Refinements over design §N

1. W-A: the ConfirmEmailTests seeder rewrite and live-envelope test move to 2a-0 (2.2–2.4). Signed-in replay `204`, concurrency, the stale anonymous form and anonymous parity ship in 2a-i (3.6, 3.7, 3.10, 3.11). 2a-ii keeps only `Replay(submission, signedIn)` and the pre-change `409` (4.1–4.3).
2. W-B: 6a is cut into 6a-i (Phase 12; journeys stay green) and 6a-ii (Phase 13; depends on 5b and 6a-i). Rollback order: 6b, 6a-ii, 6a-i.
3. S-a: 5a-i asserts only the step heading and change type over an empty step body (9.2, 9.3); data-step assertions land in Phases 10 and 11.
4. S-b: the V3 selection rule below; each phase names its projects.
5. S-c: 3a fallback cut 3a-i / 3a-ii (table above, Phase 6). `SessionTests.cs:490` stays in 3a-i because the DTO changes the member set.
6. S-d: the slices refine the proposal's seven deliveries and need the ask-on-risk confirmation before apply (Forecast).
7. S-e: V5 (`git status`) is part of every verification task.

Planner additions: the 2a-i fallback cut; `tests/Application.FunctionalTests/Infrastructure/TestApp.cs` helpers (2.2, 4.1); approval assertions for the amended conflict scenario (2.5); deleting the step the rewritten scenario leaves unused (13.6); 7a depends on 6a-ii.

## Verification protocol

Journey prerequisites: Docker; no AppHost already running (the harness starts its own); the gitignored `src/Web/wwwroot/openapi/v1.json`, built by `dotnet build src/Web/Web.csproj` without `-p:OpenApiGenerateDocumentsOnBuild=false`; Playwright Chromium (`artifacts/bin/Web.AcceptanceTests/<configuration>/playwright.ps1 install chromium`). Failures listed in `openspec/config.yaml` `testing.known_baselines` are not regressions.

- V1: `cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npx vite build`
- V2: `cd src/Web/ClientApp && npm run i18n:unused`
- V3: each selected project, one Aspire-bearing project per process: `dotnet test tests/<Project>/<Project>.csproj --filter "TestCategory!=IndependentDevelopmentReview"` (`testing.dotnet.project_test_commands`). Selection: a Domain change runs all four projects; an Application or Web change runs Application.UnitTests, Infrastructure.IntegrationTests and Application.FunctionalTests; functional-test files alone run Application.FunctionalTests; SPA or journey files alone run none.
- V4: `dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`
- V5: `git status` shows no change under `tests/` beyond the slice's declared test edits.

Commit only when V1–V5 pass. A failed or unrun check is reported, not committed. Stage the slice's files by path (never the user's parallel-chat edits) with this `tasks.md`. Slice 1 also stages the untracked change package unless it is already committed. Use a conventional message with no Co-Authored-By or AI attribution, then `git push origin main`.

## Phase 1: Slice 1 — CUIT or DNI first, CUIT-first copy

- Scope: the CUIT or DNI comes first in DOM, tab order and focus maps on the four forms; `register.choose.organization.detail` changes in `en` and `es` (account-creation-entry). Depends on: none. Lines: 150–230.
- Focused: `cd src/Web/ClientApp && npx vitest run src/features/identity/register/RegisterOrganizationPage.test.jsx src/features/identity/people/PersonalPages.test.jsx`
- Runtime harness: journeys that fill these forms by unchanged ids: "A pending registration becomes usable only after confirmation", "A signed-in identity registers another organization without a second account", "A newcomer sets up a personal account and their document is never shown in full", "An identity that already has an organization adds a personal context".
- Rollback: only after 4, 5a-i, 5a-ii, 5b, 7a and 7b; restores the old order and copy.
- Declared test edits: `RegisterOrganizationPage.test.jsx`, `PersonalPages.test.jsx`; none under `tests/`. V3: none.

- [x] 1.1 RED `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.test.jsx` → "focuses the CUIT first": an empty anonymous submit tabs CUIT, Legal name, Email, Password, focuses CUIT and sends nothing.
- [x] 1.2 RED same file, tests at :45-64, :66-104 and :185-206 expect CUIT focus; a signed-in validation_failed naming legalName and cuit focuses CUIT.
- [x] 1.3 GREEN `src/Web/ClientApp/src/features/identity/fieldErrors.js` :50 and `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx` (:32; register-cuit before register-legal-name) per design §H.
- [x] 1.4 RED `src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx`, tests at :73-93, :95-140 and :342-374 expect DNI focus and the document field first.
- [x] 1.5 GREEN `src/Web/ClientApp/src/features/identity/fieldErrors.js` :51 and `src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx` (:79-83, :352-356; personal-document and add-personal-document first).
- [x] 1.6 RED `src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx` → "reads the Company choice CUIT first in en and es" (the Personal choice is unchanged).
- [x] 1.7 GREEN `src/Web/ClientApp/src/i18n/locales/en/identity.json` and `src/Web/ClientApp/src/i18n/locales/es/identity.json` :21 per design §I.
- [x] 1.8 REFACTOR: diff review; no id, name, label, type, autoComplete, required, disabled expression or helper text changed.
- [x] 1.9 VERIFY V1, V2, V4, V5; commit `feat(identity): ask for the CUIT or DNI first`; push. Verified; committed as 5fac8928.

## Phase 2: Slice 2a-0 — Pre-change fixtures, no behavior change

- Scope: pre-change signed-in registration rows are seeded directly, so the confirmation tests stop depending on the branch 2a-i replaces; an approval test for a live pre-change envelope (IA-REQ-005); approval assertions for the amended scenario "A signed-in caller submits a claimed CUIT". Passes before and after 2a-i. Depends on: none. Lines: 80–135.
- Focused: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~ConfirmEmailTests|FullyQualifiedName~Organizations.RegistrationTests"`
- Runtime harness: N/A — test fixtures only, no product behavior; V4 guards regressions.
- Rollback: alone before 2a-i ships; afterwards only together with 2a-i (the old seeder needs the pre-change branch).
- Declared test edits: `tests/Application.FunctionalTests/Infrastructure/TestApp.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/ConfirmEmailTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs`. V3: Application.FunctionalTests.

These are approval tests (strict-tdd.md, "Approval Testing"): they pass before and after the change.

- [x] 2.1 SAFETY NET: run the focused command and record the passing count.
- [x] 2.2 APPROVAL `tests/Application.FunctionalTests/Infrastructure/TestApp.cs`: SeedPreChangeSignedInRegistrationAsync beside SetConfirmationLifecycleAsync seeds a PendingConfirmation tenant, profile, responsible membership with the Owner role, the pending_confirmation audit, and the confirmation envelope with a live secret for GetRegistrationRawToken(), through the domain factories and the application's hasher and secret writer.
- [x] 2.3 APPROVAL `tests/Application.FunctionalTests/IdentityAccess/Organizations/ConfirmEmailTests.cs`: RegisterAsSignedInCallerAsync (:234-246) uses the seeder and its doc names legacy rows; the tests at :167-200 keep their assertions and stay green.
- [x] 2.4 APPROVAL same file → `A_pre_change_signed_in_registration_with_a_live_envelope_still_confirms_and_names_the_owner` (success; Active tenant and membership; owner named).
- [x] 2.5 APPROVAL `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs` test at :249-270: the conflicting submission leaves only the seeded tenant and profile, with no membership, role, role assignment, ownership, audit record, outbox message or secret.
- [x] 2.6 VERIFY V1, V2, V3, V4, V5; commit `test(identity): seed pre-change signed-in registrations directly`; push. Verified; committed as a3762b69.

## Phase 3: Slice 2a-i — Immediate activation, `204`, `Registered` replay

- Scope (identity-access IA-REQ-003/048, IA-REQ-004 and IA-REQ-005 signed-in scenarios; threat rows for enumeration, tenant context and double submit):
  - the signed-in branch commits an Active owned graph with no envelope, records `Registered` and answers a bodyless `204`;
  - a replay or a concurrent twin answers `204` with one graph;
  - a stale anonymous form with a valid session registers;
  - anonymous answers stay identical;
  - OpenAPI declares `204`;
  - journey line :33 is removed.
- Depends on: 2a-0. Lines: 300–510; fallback cut in the Forecast.
- Focused: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~Organizations.RegistrationTests|FullyQualifiedName~SignedInRegistrationHttpTests|FullyQualifiedName~OpenApiContractTests|FullyQualifiedName~Sessions.SessionTests"`
- Runtime harness: journey "A signed-in identity registers another organization without a second account" (both organizations are offered without confirmation).
- Rollback: only after 3, 5 and 6, as a forward commit that keeps `Registered` with a replay arm answering `409 registration_conflict` and writing nothing; after archive, also revert the identity-access deltas (design §O).
- Declared test edits: `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/SignedInRegistrationHttpTests.cs` (new), `tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`, `tests/Web.AcceptanceTests/Features/IdentityAccess.feature`, `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs`. V3: all four (2a-i-a: all but Domain.UnitTests).

- [x] 3.1 REFACTOR (prep) `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganization.cs` returns `Result<OrganizationRegistrationOutcome>` (Accepted only); `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs` types follow; `src/Web/Endpoints/Identity.cs` maps it to 202 through ToHttpResult.
- [x] 3.2 Run the focused command and Application.UnitTests: the unchanged 202 behavior stays green (boundary of 2a-i-a). Part 2a-i-a verified; commit pending orchestrator gate.
- [ ] 3.3 RED `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs` → `Signed_in_registration_creates_an_active_owned_graph_without_a_confirmation`. Expects an Active tenant and membership, an owner, the Owner role, audit outcome registered, submission Registered, and no outbox message or secret.
- [ ] 3.4 RED same file: AssertSinglePendingConfirmationGraphAsync (:480-496) becomes AssertSingleActiveOwnedGraphAsync for the test at :146-168 ("A signed-in registration cannot commit").
- [ ] 3.5 RED same file → `An_immediate_organization_matches_a_confirmed_one` (tenant type and status, membership status, ownership, initial roles).
- [ ] 3.6 RED same file → `A_replayed_signed_in_registration_answers_204_without_a_second_graph` (one tenant, membership and audit record).
- [ ] 3.7 RED same file → `Concurrent_identical_signed_in_registrations_create_one_active_graph_and_both_answer_204` (PostgreSQL).
- [ ] 3.8 RED `tests/Application.FunctionalTests/IdentityAccess/Organizations/SignedInRegistrationHttpTests.cs` (new) → `Signed_in_registration_answers_bodyless_204`.
- [ ] 3.9 RED same file → `A_signed_in_registration_keeps_the_active_tenant_and_lists_the_new_one`.
- [ ] 3.10 RED same file → `A_stale_anonymous_form_with_a_valid_session_registers_and_replays_204`.
- [ ] 3.11 RED same file → `Anonymous_answers_stay_identical_after_a_signed_in_registration_activates_the_cuit` (byte-identical 202 apart from traceId).
- [ ] 3.12 RED `tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs` :215 expects NoContent; `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs` :71 also expects a bodyless 204.
- [ ] 3.13 GREEN `src/Domain/IdentityAccess/Organizations/RegistrationSubmissionOutcome.cs` and `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganization.cs` add Registered.
- [ ] 3.14 GREEN `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs` :99-115 per design §B steps 1–5, in the order of `src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs` (read-only) :177-192. ResultFor maps Registered; delete ConfirmationEnvelope (:240).
- [ ] 3.15 GREEN `src/Web/Endpoints/Identity.cs`: Registered maps to NoContent, otherwise 202; the route also produces 204; problem codes unchanged.
- [ ] 3.16 Journey: delete `tests/Web.AcceptanceTests/Features/IdentityAccess.feature` :33 and `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs` :147-148.
- [ ] 3.17 REFACTOR the handler's XML docs and branch comments to describe immediate activation; the focused command stays green.
- [ ] 3.18 VERIFY V1, V2, V3, V4, V5; commit `feat(identity): activate organizations registered with a session`; push.

## Phase 4: Slice 2a-ii — Replay by caller scope

- Scope: `Replay(submission, signedIn)`. A signed-in replay of a submission recorded `Accepted` before the change answers `409 registration_conflict` and writes nothing; anonymous `Accepted` stays `202` (design §B; threat row "legacy pending rows"). Depends on: 2a-i. Lines: 70–120.
- Focused: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~Organizations.RegistrationTests|FullyQualifiedName~SignedInRegistrationHttpTests"`
- Runtime harness: N/A — a pre-change submission cannot be produced through the browser; V4 guards regressions.
- Rollback: alone; a legacy signed-in replay answers `202` again, for a link that was really sent.
- Declared test edits: `tests/Application.FunctionalTests/Infrastructure/TestApp.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/SignedInRegistrationHttpTests.cs`. V3: Application.UnitTests, Infrastructure.IntegrationTests, Application.FunctionalTests.

- [ ] 4.1 RED `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs` → `A_pre_change_signed_in_replay_answers_registration_conflict`. Complete a signed-in registration, rewrite its submission to Accepted (new SetRegistrationSubmissionOutcomeAsync in `tests/Application.FunctionalTests/Infrastructure/TestApp.cs`) and its rows to PendingConfirmation (SetConfirmationLifecycleAsync), then replay; counts unchanged.
- [ ] 4.2 RED `tests/Application.FunctionalTests/IdentityAccess/Organizations/SignedInRegistrationHttpTests.cs` → `A_pre_change_signed_in_replay_answers_409_and_writes_nothing` (problem+json code, traceId, no new rows).
- [ ] 4.3 GREEN `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs` :71 and :182-184: replay with signedIn = a session identity is present; signed-in Accepted maps to RegistrationConflict; the anonymous replay tests stay green.
- [ ] 4.4 REFACTOR: the XML doc on Replay gives the design §B reasons; focused command green.
- [ ] 4.5 VERIFY V1, V2, V3, V4, V5; commit `feat(identity): refuse signed-in replays of pre-activation registrations`; push.

## Phase 5: Slice 2b — Status-aware registration in the SPA

- Scope: the transport accepts a status list and `withStatus`; `registerOrganization` accepts exactly `202` or `204` and resolves the status; `/organizations/register` renders from the observed status; key `register.organization.registered`; journey step :144. Specs: account-setup "Company completion activates immediately"; threat "stale context in other tabs". Depends on: 2a-i. Lines: 150–240.
- Focused: `cd src/Web/ClientApp && npx vitest run src/api/apiTransport.test.js src/features/identity/api/identityClient.test.js src/features/identity/register/RegisterOrganizationPage.test.jsx`
- Runtime harness: journey "A signed-in identity registers another organization without a second account" (asserts the registered status).
- Rollback: only after 3, 5 and 6; every success shows the acknowledgement again.
- Declared test edits: `apiTransport.test.js`, `identityClient.test.js`, `RegisterOrganizationPage.test.jsx`; `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs`, `tests/Web.AcceptanceTests/Pages/IdentityAccessPages.cs`. V3: none.

- [ ] 5.1 RED `src/Web/ClientApp/src/api/apiTransport.test.js` → "resolves the observed status within an expected list and refuses any other 2xx" (a single number keeps today's behavior).
- [ ] 5.2 RED `src/Web/ClientApp/src/features/identity/api/identityClient.test.js` → "resolves 202 and 204 from organization registration and refuses 200".
- [ ] 5.3 RED `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.test.jsx` test at :158-183: MSW 204, the status "The organization is registered and ready to use.", one context reload, no link or delivery help.
- [ ] 5.4 RED same file → "shows the registered status and reloads once when an anonymous form answers 204".
- [ ] 5.5 GREEN `src/Web/ClientApp/src/api/apiTransport.js` send (:105-133) and `src/Web/ClientApp/src/features/identity/api/identityClient.js` :60 per design §C.
- [ ] 5.6 GREEN `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx`: a 204 shows only the registered status and reloads once, and a failed reload is not a failed registration; a 202 keeps :104-123. The key goes into `src/Web/ClientApp/src/i18n/locales/en/identity.json` and `src/Web/ClientApp/src/i18n/locales/es/identity.json`.
- [ ] 5.7 Journey: `tests/Web.AcceptanceTests/Pages/IdentityAccessPages.cs` adds AssertRegisteredAsync; `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs` :144 uses it.
- [ ] 5.8 VERIFY V1, V2, V4, V5; commit `feat(identity): say a signed-in registration is ready to use`; push.

## Phase 6: Slice 3a — `setupRequired` signal

- Scope: `IdentitySetupRequirement.IsRequiredAsync` (design §A) in both context handlers, and `setupRequired` on the three context responses. Specs: identity-context-contract "Setup-required signal" (11 scenarios); threats "authorization bypass", "platform invitee lockout", "legacy pending rows". Depends on: 2a-i. Lines: 340–510; fallback cut 3a-i = 6.1–6.12, 3a-ii = 6.13–6.17.
- Focused: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~SetupRequiredTests|FullyQualifiedName~Sessions.SessionTests|FullyQualifiedName~GoogleOidcTests"`
- Runtime harness: every journey reads the context (the SPA tolerates the additive member until 3b); "A supported target language survives sign-in and full-page navigation" also uses the language answer.
- Rollback: only after 5–7 and after 3b.
- Declared test edits: `tests/Application.FunctionalTests/IdentityAccess/Context/SetupRequiredTests.cs` (new), `tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/ExternalLogins/GoogleOidcTests.cs`. V3: Application.UnitTests, Infrastructure.IntegrationTests, Application.FunctionalTests.

Write 6.2–6.8 and 6.13–6.16 and see them fail before 6.9.

- [ ] 6.1 SAFETY NET: the focused command is green before any edit.
- [ ] 6.2 RED `tests/Application.FunctionalTests/IdentityAccess/Context/SetupRequiredTests.cs` (new) → `A_memberless_identity_reads_setupRequired_true`.
- [ ] 6.3 RED same file → `Membership_and_tenant_status_decide_setupRequired`, one TestCase per row: Active/Active for Organization, Personal and Platform, and each Suspended pairing, read false with empty availableTenants; Revoked/Active and Active/Closed read true.
- [ ] 6.4 RED same file → `A_pending_tenant_or_membership_does_not_count` (a reinstated pending membership; rows from SeedPreChangeSignedInRegistrationAsync).
- [ ] 6.5 RED same file → `One_counting_membership_is_enough`.
- [ ] 6.6 RED same file → `A_bound_pending_platform_invitee_is_not_setup_required` (bound pending invitation; Pending enrollment; Verified enrollment with a pending invitation).
- [ ] 6.7 RED same file → `An_ended_platform_invitation_no_longer_exempts` (expired; cancelled).
- [ ] 6.8 RED `tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs` :490 member set includes setupRequired, which is true; `tests/Application.FunctionalTests/IdentityAccess/ExternalLogins/GoogleOidcTests.cs` :145-160 first Google sign-in reads true.
- [ ] 6.9 GREEN `src/Application/IdentityAccess/Context/IdentitySetupRequirement.cs` (new) per design §A.
- [ ] 6.10 GREEN `src/Application/IdentityAccess/Context/GetIdentityContext/GetIdentityContext.cs` and `src/Web/Endpoints/Identity/Contracts/IdentityContextResponse.cs` append SetupRequired; `src/Application/IdentityAccess/Context/GetIdentityContext/GetIdentityContextHandler.cs` injects TimeProvider and calls the rule with the tenant count.
- [ ] 6.11 GREEN `src/Application/IdentityAccess/Context/SelectTenant/SelectTenantHandler.cs`: the rule with the attempt's now (:41).
- [ ] 6.12 REFACTOR: the XML doc on the rule explains the D2 short-circuit; 6.2–6.8 are green (end of 3a-i).
- [ ] 6.13 RED `tests/Application.FunctionalTests/IdentityAccess/Context/SetupRequiredTests.cs` → `Setup_required_ignores_query_and_header_values` (read false, revoke, re-read with setupRequired=false in query and header, get true).
- [ ] 6.14 RED same file → `Tenant_and_language_answers_carry_the_same_value`.
- [ ] 6.15 RED same file → `The_context_member_set_is_exact` (eight members; the others keep their values).
- [ ] 6.16 RED same file → `Company_registration_ends_setup`.
- [ ] 6.17 VERIFY V1, V2, V3, V4, V5; commit `feat(identity): report setupRequired in the identity context`; push.

## Phase 7: Slice 3b — Strict SPA reader

- Scope: `setupRequired` is required and boolean on the three context reads; the MSW default is `false`. Specs: identity-context-contract "Strict SPA reader", "Account language handling is unchanged". Every SPA context fixture builds on `signedInContext`, so no other test changes. Depends on: 3a. Lines: 160–260.
- Focused: `cd src/Web/ClientApp && npx vitest run src/features/identity/api/identityClient.test.js src/features/identity/context/IdentityProvider.test.jsx src/i18n/spanish.test.jsx`
- Runtime harness: every journey reads the context through the strict reader; "A supported target language survives sign-in and full-page navigation" covers the language answer.
- Rollback: revert before 3a (the reader first; a reader without the member fails every read).
- Declared test edits: `identityClient.test.js`, `IdentityProvider.test.jsx`, `spanish.test.jsx`, `src/Web/ClientApp/src/test/identityServer.js`; none under `tests/`. V3: none.

- [ ] 7.1 RED `src/Web/ClientApp/src/features/identity/api/identityClient.test.js` → "fails a context whose setupRequired is the string false". An it.each of missing, "false", 0 and null on getContext, selectTenant and updatePreferredLanguage expects unreadable_response with status 0.
- [ ] 7.2 RED `src/Web/ClientApp/src/features/identity/context/IdentityProvider.test.jsx`: drift clears the context and keeps the problem; tenant-selection drift keeps the context. Approval cases: a true read reports true, and es with true selects es and writes the cookie.
- [ ] 7.3 RED `src/Web/ClientApp/src/i18n/spanish.test.jsx` → "shows unreadable_response in the language alert when the answer lacks setupRequired".
- [ ] 7.4 GREEN `src/Web/ClientApp/src/features/identity/api/identityClient.js` :25-58 per design §C, and `src/Web/ClientApp/src/test/identityServer.js` :7-19 with a setupRequired false default.
- [ ] 7.5 VERIFY V1, V2, V4, V5; commit `feat(identity): read setupRequired strictly`; push.

## Phase 8: Slice 4 — Form-reuse refactor

- Scope: export `OrganizationRegistrationForm` and `organizationFieldsFor` from `RegisterOrganizationPage.jsx`, and `AddPersonalContext` from `PersonalPages.jsx`. No DOM, id, label, string or behavior changes (design §E "Form reuse", D13). Depends on: 1. Lines: 110–190.
- Focused: `cd src/Web/ClientApp && npx vitest run src/features/identity/register/RegisterOrganizationPage.test.jsx src/features/identity/people/PersonalPages.test.jsx`
- Runtime harness: the four form journeys of slice 1.
- Rollback: only after 5 and 7.
- Declared test edits: `RegisterOrganizationPage.test.jsx`; none under `tests/`. V3: none.

- [ ] 8.1 SAFETY NET: the focused command is green (approval for both pages).
- [ ] 8.2 RED `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.test.jsx` → "renders the exported form and submits blank credentials" (signed in: CUIT and Legal name only; empty email and password sent).
- [ ] 8.3 GREEN `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx`: extract the form (state, validation, binding and focus from :57-93 and :125-183); publicEntry, default true, switches only the submit layout; the page composes the form.
- [ ] 8.4 GREEN `src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx` :330 exports AddPersonalContext (structural; triangulation skipped because there is no logic).
- [ ] 8.5 REFACTOR: diff review; no id, name, label, type, autoComplete, required, disabled expression, helper text or en string changed; focused command green.
- [ ] 8.6 VERIFY V1, V2, V4, V5; commit `refactor(identity): export the organization and personal forms`; push.

## Phase 9: Slice 5a-i — Setup shell

- Scope: `/identity/setup` behind `ProtectedRoute`, inside the shell. It covers the type choice, the step heading and change type over an empty step body (refinement 3), preselection from router state, arrival resume, and the keys `setup.title`, `setup.subtitle` and `setup.changeType`. Specs: account-setup "Setup screen" (4 scenarios); threat "redirect loop". Depends on: 3b. Lines: 230–340.
- Focused: `cd src/Web/ClientApp && npx vitest run src/features/identity/setup/AccountSetupPage.test.jsx src/AppRoutes.test.jsx`
- Runtime harness: N/A — no journey reaches `/identity/setup` before 6a-ii; V4 guards regressions.
- Rollback: only after 6 and 7; removes a route nothing links to.
- Declared test edits: `AccountSetupPage.test.jsx` (new), `AppRoutes.test.jsx`; none under `tests/`. V3: none.

- [ ] 9.1 RED `src/Web/ClientApp/src/features/identity/setup/AccountSetupPage.test.jsx` (new) → "opens on the type choice with the setup title and both options" (one h1, the subtitle, both titles and details).
- [ ] 9.2 RED same file → "chooses and changes the type without sending a request" (the path stays; only the h2 "An organization" and "Change account type"; back to the choice).
- [ ] 9.3 RED same file → "opens the chosen step for a preselected type and ignores an unknown one" (personal shows the h2 and change type; an unknown type shows the choice).
- [ ] 9.4 RED same file → "skips setup that is not required and resumes the return URL" (?returnUrl=%2Fmembers goes to /members; no step renders).
- [ ] 9.5 RED same file → "resolves a nested setup return URL to /identity".
- [ ] 9.6 RED `src/Web/ClientApp/src/AppRoutes.test.jsx` :55: add /identity/setup to the unauthenticated it.each.
- [ ] 9.7 GREEN `src/Web/ClientApp/src/features/identity/setup/accountTypes.js` (new): accountTypes and accountTypeOf.
- [ ] 9.8 GREEN `src/Web/ClientApp/src/features/identity/setup/AccountSetupPage.jsx` (new) per design §E rows Page, Resume, Type choice and Step heading, using safeReturnUrl from `src/Web/ClientApp/src/features/identity/login/LoginPage.jsx` (read-only); `src/Web/ClientApp/src/AppRoutes.jsx` routes it behind ProtectedRoute.
- [ ] 9.9 GREEN `src/Web/ClientApp/src/i18n/locales/en/identity.json` and `src/Web/ClientApp/src/i18n/locales/es/identity.json`: the setup block after register (design §I).
- [ ] 9.10 REFACTOR: composition per `.agents/skills/frontend-design-standards/references/ui-composition-rules.md` (read-only): one h1, no contained button on the choice, sx for layout only; focused command green.
- [ ] 9.11 VERIFY V1, V2, V4, V5; commit `feat(identity): add the account setup screen`; push.

## Phase 10: Slice 5a-ii — Personal step

- Scope: the Personal step reuses `AddPersonalContext`; after completion it reloads once, then resumes or returns to the choice. Specs: account-setup Personal reuse, "Completion and resume", "A lost session during completion"; threats "open redirect" and "antiforgery". The conflict and field-error scenarios stay proven by the reused form's tests (`PersonalPages.test.jsx` :342-374 and :376). Depends on: 5a-i, 4. Lines: 190–300.
- Focused: `cd src/Web/ClientApp && npx vitest run src/features/identity/setup/AccountSetupPage.test.jsx`
- Runtime harness: N/A until 6a-ii, whose setup journey drives this step; V4 guards regressions.
- Rollback: only after 6 and 7.
- Declared test edits: `AccountSetupPage.test.jsx`; none under `tests/`. V3: none.

- [ ] 10.1 RED `src/Web/ClientApp/src/features/identity/setup/AccountSetupPage.test.jsx` → "sends the personal claim, announces it and resumes the captured destination" (one POST with only documentNumber, fullName and displayName; the success status; /members).
- [ ] 10.2 RED same file → "returns to the type choice with the same return URL when setup is still required" (nothing resent).
- [ ] 10.3 RED same file → "reports no failure and resends nothing when the reload after a completion fails" (sign-in shows the network_unavailable words).
- [ ] 10.4 RED same file → "sends a lost session to sign in with the setup return URL" (401 invalid_session).
- [ ] 10.5 RED same file → "never leaves the origin after completion" (it.each over //evil.test, /\evil.test, https://evil.test and javascript:alert(1)).
- [ ] 10.6 RED same file → "sends the token and keeps the values after antiforgery_validation_failed".
- [ ] 10.7 GREEN `src/Web/ClientApp/src/features/identity/setup/AccountSetupPage.jsx`: the Personal branch per design §E. It passes the step heading as the notice; onPersonalAdded reloads once and resets the type while setup is still required.
- [ ] 10.8 REFACTOR: one navigation rule only (D12); focused command green.
- [ ] 10.9 VERIFY V1, V2, V4, V5; commit `feat(identity): add a personal account from setup`; push.

## Phase 11: Slice 5b — Company step

- Scope: the Company step composes the step heading, `ProblemMessage` and the exported form (signed in, not public entry); a `204` shows the registered card and reloads once. Specs: account-setup Company reuse, "Company completion resumes to the access page by default", "A CUIT that is already registered". Depends on: 5a-ii, 2b. Lines: 140–210.
- Focused: `cd src/Web/ClientApp && npx vitest run src/features/identity/setup/AccountSetupPage.test.jsx`
- Runtime harness: N/A — no journey drives the Company step (the setup journey uses Personal); V4 guards regressions.
- Rollback: only after 6 and 7.
- Declared test edits: `AccountSetupPage.test.jsx`; none under `tests/`. V3: none.

- [ ] 11.1 RED `src/Web/ClientApp/src/features/identity/setup/AccountSetupPage.test.jsx` → "sends the signed-in organization body from the Company step" (CUIT 30-12345678-1, "Northwind SA", empty email and password, one request).
- [ ] 11.2 RED same file → "announces the registered organization and resumes /identity by default".
- [ ] 11.3 RED same file → "keeps both values on the Company step after registration_conflict".
- [ ] 11.4 RED same file → "returns to the type choice when setup is still required after a registration".
- [ ] 11.5 GREEN `src/Web/ClientApp/src/features/identity/setup/AccountSetupPage.jsx`: the Company branch per design §E and its completion rules (a 202 renders the acknowledgement, never a failure).
- [ ] 11.6 VERIFY V1, V2, V4, V5; commit `feat(identity): register a company from setup`; push.

## Phase 12: Slice 6a-i — Exemption list, member fixtures

- Scope: `setupRoutes.js` with its test (unused until 6a-ii); the fixture `ConfirmedMemberAsync(organizations)`; journeys that assume a context seed plain memberships and stay green. Depends on: none (ordered after 5b). Lines: 70–110.
- Focused: `cd src/Web/ClientApp && npx vitest run setupRoutes`
- Runtime harness: "A person deactivates their account and returns through the delivered email without an automatic session", "A person ends another device from the list of the devices they hold", "A forgotten password is reset from the delivered link and then changed from inside", "Sign-in throttling is per account and recovers", "A supported target language survives sign-in and full-page navigation".
- Rollback: after 6a-ii; on its own it changes no product behavior.
- Declared test edits: `setupRoutes.test.js` (new) and the four `tests/Web.AcceptanceTests` files in 12.3–12.4. V3: none.

- [ ] 12.1 RED `src/Web/ClientApp/src/features/identity/setup/setupRoutes.test.js` (new) → "matches the nine routes exactly" (a frozen list; /IDENTITY/SETUP/ is exempt; /identity/profile, /identity/setup/x and /members are not).
- [ ] 12.2 GREEN `src/Web/ClientApp/src/features/identity/setup/setupRoutes.js` (new) per design §D.
- [ ] 12.3 `tests/Web.AcceptanceTests/IdentityAccessFixtures.cs`: ConfirmedMemberAsync(int organizations = 1) built from ConfirmedIdentityAsync, OrganizationAsync and PlainMembershipAsync.
- [ ] 12.4 Seed members at `tests/Web.AcceptanceTests/StepDefinitions/IdentityContinuationStepDefinitions.cs` :71, :183, :383, `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs` :289 and `tests/Web.AcceptanceTests/StepDefinitions/LocalizationStepDefinitions.cs` :28 (two organizations, so none is auto-selected).
- [ ] 12.5 VERIFY V1, V2, V4 (the five journeys green), V5; commit `feat(identity): add the setup exemption list and member fixtures`; push.

## Phase 13: Slice 6a-ii — Guard, setup journeys

- Scope: the guard in `ProtectedRoute` (design §D) and its tests; the journeys that now land on setup; the rewritten scenario. Specs: account-setup "One setup guard", "Closed exemption list", "A person who belongs to nothing sets up and resumes". Depends on: 5b, 6a-i. Lines: 200–310.
- Focused: `cd src/Web/ClientApp && npx vitest run AppRoutes setupRoutes`
- Runtime harness: "An identity that belongs to nothing finishes setting up its account and resumes", "A newcomer joins through an invitation exactly once", "An identity that already exists accepts an invitation without registering again", "Resending an invitation leaves one usable link, and withdrawing it leaves none".
- Rollback: first within slice 6, before 6a-i and 5; redirects stop and setup stays reachable.
- Declared test edits: `AppRoutes.test.jsx` and the four `tests/Web.AcceptanceTests` files in 13.5–13.7. V3: none.

- [ ] 13.1 RED `src/Web/ClientApp/src/AppRoutes.test.jsx` → "replaces /members?q=1 with setup" (/identity/setup?returnUrl=%2Fmembers%3Fq%3D1, history replaced).
- [ ] 13.2 RED same file → it.each "redirects %s to setup with its own return URL" over the spec's nine guarded routes.
- [ ] 13.3 GREEN `src/Web/ClientApp/src/components/api-authorization/ProtectedRoute.jsx` per design §D (one return-URL builder for both redirects).
- [ ] 13.4 TRIANGULATE same test file: the nine exempt routes, /confirm-email#token=…, a false signal with empty availableTenants, and a pending read all stay put. Each fails against a guard that ignores the list or the signal.
- [ ] 13.5 JOURNEY `tests/Web.AcceptanceTests/Features/IdentityAccess.feature` :13-16 becomes the design §K scenario.
- [ ] 13.6 JOURNEY `tests/Web.AcceptanceTests/Pages/IdentityAccessPages.cs` adds SignInToFinishSetupAsync (expects /identity/setup?returnUrl=%2Fidentity and the setup h1) and AccountSetupPage; `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs` adds the three steps and deletes the unused step at :168.
- [ ] 13.7 JOURNEY `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs` :232 and :240 and `tests/Web.AcceptanceTests/StepDefinitions/IdentityContinuationStepDefinitions.cs` :365 use SignInToFinishSetupAsync.
- [ ] 13.8 VERIFY V1, V2, V4, V5; commit `feat(identity): send a person without a context to setup`; push.

## Phase 14: Slice 6b — `/personal/register` redirect

- Scope: the loading gate and signed-in redirect on `PersonalRegisterPage` (design §F; account-setup "A signed-in person never reaches the personal signup", 3 scenarios). Depends on: 6a-ii. Lines: 80–135.
- Focused: `cd src/Web/ClientApp && npx vitest run PersonalPages`
- Runtime harness: "A newcomer sets up a personal account and their document is never shown in full" (the form renders after the 401 read).
- Rollback: before 6a-ii; the signup renders for everyone again.
- Declared test edits: `PersonalPages.test.jsx`; none under `tests/`. V3: none.

- [ ] 14.1 RED `src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx` → "sends a setup-required person from /personal/register to the Personal step" (no registerPersonal request).
- [ ] 14.2 RED same file → "sends a person with a context from /personal/register to their profile without rendering the form".
- [ ] 14.3 RED same file → "renders no field or Register button before the context read answers".
- [ ] 14.4 GREEN `src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx` PersonalRegisterPage (:95-147) per design §F.
- [ ] 14.5 Same test file, tests at :46-72, :73-93, :95-140 and :142-152: await the fields behind the gate; assertions unchanged.
- [ ] 14.6 VERIFY V1, V2, V4, V5; commit `feat(identity): keep signed-in people off the personal signup`; push.

## Phase 15: Slice 7a — Shared Google button

- Scope: `ContinueWithGoogle.jsx` with identical DOM on `/login` and before the anonymous forms of `/organizations/register` and `/personal/register`; none once signed in (account-creation-entry "Creating an account asks the type first"). Depends on: 6a-ii. Lines: 160–240.
- Focused: `cd src/Web/ClientApp && npx vitest run RegisterOrganizationPage PersonalPages identityFiles.contract AppRoutes`
- Runtime harness: "A pending registration becomes usable only after confirmation" and "A newcomer sets up a personal account and their document is never shown in full" (email forms below the button); Playwright cannot drive the provider leg.
- Rollback: alone.
- Declared test edits: `RegisterOrganizationPage.test.jsx`, `PersonalPages.test.jsx`, `identityFiles.contract.test.js`; none under `tests/`. V3: none.

- [ ] 15.1 RED `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.test.jsx` → "offers Continue with Google before the anonymous form and none once signed in".
- [ ] 15.2 RED `src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx` → "offers Continue with Google before the signup form and shows a refused start there" (a 429 keeps the visitor on the form).
- [ ] 15.3 RED `src/Web/ClientApp/src/test/identityFiles.contract.test.js` :17 names the new ContinueWithGoogle.jsx; still twelve catches.
- [ ] 15.4 GREEN `src/Web/ClientApp/src/features/identity/login/ContinueWithGoogle.jsx` (new), taken from `src/Web/ClientApp/src/features/identity/login/LoginPage.jsx` :52-59, :97-107 and :127-129 (identical DOM, prop onProblem); LoginPage renders it.
- [ ] 15.5 GREEN `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx` and `src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx`: the anonymous branches render it and a Divider before the form, and show problem ?? providerProblem.
- [ ] 15.6 TRIANGULATE: the login Google test in `src/Web/ClientApp/src/AppRoutes.test.jsx` (read-only) :77-88 stays green unchanged.
- [ ] 15.7 VERIFY V1, V2, V4, V5; commit `feat(identity): offer Google on both create-account screens`; push.

## Phase 16: Slice 7b — Type carry, return page

- Scope: `pendingAccountType.js`. A create-account start writes the type once the start succeeds, and `/login` clears it; `/external/return` takes it on `signed_in` with `setupRequired` true and clears it otherwise. Specs: account-creation-entry "The chosen type survives the Google round trip", "A Google sign-in clears a leftover type"; threat "carry tampering". Depends on: 7a, 5b. Lines: 280–425; fallback cut 7b-i = 16.1–16.7, 7b-ii = 16.8–16.13.
- Focused: `cd src/Web/ClientApp && npx vitest run pendingAccountType ExternalAccountsPage RegisterOrganizationPage PersonalPages AppRoutes`
- Runtime harness: N/A — Playwright cannot drive the Google round trip (GoogleOidcTests and MSW cover it); V4 guards regressions.
- Rollback: alone, 7b-ii before 7b-i; `/external/return` goes back to `/identity`.
- Declared test edits: `pendingAccountType.test.js` (new), `AppRoutes.test.jsx`, `RegisterOrganizationPage.test.jsx`, `PersonalPages.test.jsx`, `ExternalAccountsPage.test.jsx`; none under `tests/`. V3: none.

- [ ] 16.1 RED `src/Web/ClientApp/src/features/identity/setup/pendingAccountType.test.js` (new) → "writes, takes once and forgets a valid type".
- [ ] 16.2 RED same file → "reads null for an unknown, malformed or throwing record".
- [ ] 16.3 GREEN `src/Web/ClientApp/src/features/identity/setup/pendingAccountType.js` (new) per design §G, mirroring `src/Web/ClientApp/src/features/identity/useIdentityProof.js` (read-only).
- [ ] 16.4 RED `src/Web/ClientApp/src/AppRoutes.test.jsx` → "clears a leftover account type before leaving from sign-in".
- [ ] 16.5 RED `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.test.jsx` → "writes company only after the Google start succeeded".
- [ ] 16.6 RED `src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx` → "writes personal only after the Google start succeeded" (a 429 start writes nothing).
- [ ] 16.7 GREEN `src/Web/ClientApp/src/features/identity/login/ContinueWithGoogle.jsx` takes accountType (remember or forget, then leave); `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx` passes company and `src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx` passes personal (end of 7b-i).
- [ ] 16.8 RED `src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.test.jsx` → "opens the Company step when a signed-in return still needs setup" (record gone).
- [ ] 16.9 RED same file → "clears the record and lands on /identity when setup is not required".
- [ ] 16.10 RED same file → "clears the record on a refused, linked or proved outcome and on a failed completion" (the 409 external_login_conflict handling is unchanged).
- [ ] 16.11 RED same file → "opens the generic type choice when no record survives".
- [ ] 16.12 GREEN `src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.jsx` ExternalReturnPage (:252-284) per design §G "Return page".
- [ ] 16.13 VERIFY V1, V2, V4, V5; commit `feat(identity): carry the chosen account type through Google`; push.