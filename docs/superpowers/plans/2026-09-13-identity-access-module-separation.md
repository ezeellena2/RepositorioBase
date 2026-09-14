# IdentityAccess Module Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make IdentityAccess a peer business module on RepositorioBase's shared foundation so future modules can use base mechanics without importing IdentityAccess internals.

**Architecture:** Preserve the current Clean Architecture/CQRS layers and IdentityAccess domain model while moving ownership to the consumer: shared pipeline and Web mechanics remain neutral, IdentityAccess owns its persistence and membership/session/permission semantics, and the physical `ApplicationDbContext` implements module-specific ports. Introduce only boundaries already demanded by current consumers; do not create a shared-kernel framework, a universal success envelope, or a second-module abstraction before a real second module proves its semantics.

**Tech Stack:** .NET 10, C#, MediatR, EF Core/PostgreSQL, ASP.NET Core Minimal APIs/OpenAPI, React 19, Vite, Vitest, Testing Library, i18next, NUnit, Shouldly.

---

## Execution contract

Load `@engineering-standards`, `@error-handling-standards`, `@localization-standards`, `@frontend-design-standards` for rendered SPA work, and `@codebase-design` before implementation. Apply RED -> GREEN -> REFACTOR for behavior and architecture tests.

**Plan status:** Three independent review rounds were exhausted. The final ordering correction below—creating the frontend dependency guard only after `offset-pagination-standard` has passed verification—has not received a fourth independent review. Execution requires a fresh, human-approved review checkpoint before Task 1 begins.

Preserve these invariants throughout:

- `Result`/`Result<T>`, `ApplicationError`, and `ApplicationErrorCategory` remain the internal failure model.
- `ApiProblemDetailsMapper` remains the single RFC 9457 response writer; unexpected failures remain a generic safe `500`.
- Success bodies remain endpoint-specific DTOs or bodyless statuses. Do not add a universal response envelope.
- Existing localization, permission, session, tenant, enumeration-safety, antiforgery, and security behavior must not change.
- IdentityAccess retains accounts, sessions, memberships, roles, invitations, credential lifecycle, and their domain rules.
- One physical `ApplicationDbContext` remains valid, but it implements module-specific persistence ports; future module `DbSet`s must not be added to a Common mega-interface.
- This is the repository's existing DDD/Clean Architecture philosophy adapted to current seams. Do not copy another architecture literally; the repository contains no governing “DDS” pattern.

### Dirty-worktree preservation

The working tree already contains unrelated user changes, including Gentle AI shared files, `AGENTS.md`, `CLAUDE.md`, engineering-standard edits, and untracked `openspec/` content.

- [ ] Before every slice, run `git status --short` and save the output in the task notes.
- [ ] Before every slice, also record `git diff --binary -- .agents .codex AGENTS.md CLAUDE.md | git hash-object --stdin` and a sorted `Get-ChildItem openspec -Recurse -File | Get-FileHash` listing. Compare those exact baselines after the slice; refresh them only after separately authorized prerequisite work.
- [ ] Stage only the paths named by that slice; never run `git add -A`, `git checkout --`, `git restore`, `git reset`, or a cleanup command against unrelated paths.
- [ ] If a target file already has user changes, inspect its diff and preserve them; stop if the intended edit cannot be isolated safely.
- [ ] Do not accept, edit, or implement `docs/features/whatsapp-bot/ADR-001.md`, `docs/features/whatsapp-bot/SPEC.md`, or any WhatsApp source.

## Dependency order

1. Task 1 adds only the non-overlapping backend characterization and dependency guard; it may start immediately.
2. Tasks 2 and 3 remove backend Common -> IdentityAccess dependencies and may proceed after Task 1.
3. Before Tasks 4-6, `openspec/changes/offset-pagination-standard` must be applied and verified. No frontend import allowlist or guard from this plan may exist before that verification passes, because the prerequisite's own moves would make such an allowlist stale.
4. Task 5 creates and baselines the frontend dependency guard against the verified post-prerequisite tree, then consumes its neutral client problem paths without repeating its pagination, shared-problem, or field-helper work.
5. Task 7 is the final cross-slice verification and documentation pass.

The offset prerequisite is satisfied only by applied code and a successful verification report, not by planned paths or by a status that merely says verification may begin. At the gate run:

```powershell
gentle-ai sdd-status offset-pagination-standard --cwd . --json --instructions
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~Pagination"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~Pagination|FullyQualifiedName~OpenApiContractTests"
Push-Location src/Web/ClientApp; npm test -- src/api src/components/ProblemMessage.test.jsx; Pop-Location
```

Expected: native status reports no blocked reasons and `nextRecommended: archive` (or an archived report records a successful verify phase); pagination/OpenAPI tests pass; the neutral `src/api/problemDetails.js`, `src/api/apiTransport.js`, `src/api/problemCodes.json`, and `src/components/ProblemMessage.jsx` paths exist and their focused tests pass. A `verify` recommendation does **not** satisfy this gate. If any condition is absent, stop: the remaining tasks must not invent substitute paths.

## Ownership target

| Concern | Target owner | IdentityAccess responsibility |
| --- | --- | --- |
| `Result` and application error categories | `Application/Common` | Produces stable IdentityAccess codes/categories |
| Authorization declarations and MediatR pipeline | `Application/Common/Security` and `Application/Common/Behaviours` | Supplies membership/session-backed adapters |
| Current Identity tenant, membership, roles, sessions | `Application/IdentityAccess` | Sole domain owner |
| Identity persistence port | `Application/IdentityAccess/Persistence` | Defines only IdentityAccess data needs |
| Physical EF context | `Infrastructure/Data` | Implements each module port explicitly |
| RFC 9457 writing and generic endpoint metadata | `Web/Infrastructure` | Declares Identity-specific codes and neutral-flow rules |
| SPA transport/problem rendering | Neutral `src/api` and `src/components` paths from offset prerequisite | Keeps Identity API, provider, validators, and screens in `features/identity` |
| Localization catalogs and contract tests | Existing neutral i18n infrastructure | Owns Identity message keys while satisfying every-language contracts |
| Outbox/delivery | Current IdentityAccess behavior for this plan | No generalization until a second module proves shared semantics |

### Task 1: Characterize and guard the backend module boundary

**Files:**
- Create: `tests/Application.UnitTests/Architecture/ModuleBoundaryArchitectureTests.cs`
- Test: `tests/Application.UnitTests/Architecture/ModuleBoundaryArchitectureTests.cs`

- [ ] **Step 1 (RED): Add the backend source-boundary test without exceptions**

The test must locate the repository root from `CleanArchitecture.slnx`, enumerate `src/Application/Common/**/*.cs`, and fail when a file contains either `CleanArchitecture.Application.IdentityAccess` or `CleanArchitecture.Domain.IdentityAccess`. Anchor the scan by asserting that Common files were found.

- [ ] **Step 2: Prove the backend test fails for current debt**

Run:

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~ModuleBoundaryArchitectureTests"
```

Expected: FAIL naming only `AuthorizationBehaviour.cs`, `IApplicationDbContext.cs`, and `ISecurityDenialAuditWriter.cs` as current forbidden imports. Investigate any additional offender before proceeding.

- [ ] **Step 3 (GREEN): Add a named, exact temporary debt allowlist**

Allow only those three repository-relative paths. The assertion must still fail for any new offender and must fail when an allowlisted path no longer offends, forcing later tasks to delete stale exceptions.

- [ ] **Step 4 (REFACTOR): Make diagnostics deterministic**

Sort paths ordinally, print repository-relative paths, and reject stale backend allowlist entries so the guard shrinks as Tasks 2 and 3 remove each dependency.

Suggested commit boundary after GREEN (do not commit while writing this plan): `test: guard business module dependency boundaries`.

### Task 2: Neutralize authorization pipeline mechanics without moving Identity rules

**Files:**
- Create: `src/Application/Common/Security/IAuthorizationTenantContext.cs`
- Create: `src/Application/Common/Security/IPermissionAuthorizer.cs`
- Modify: `src/Application/Common/Behaviours/AuthorizationBehaviour.cs`
- Modify: `src/Application/Common/Interfaces/ISecurityDenialAuditWriter.cs`
- Modify: `src/Application/IdentityAccess/Authorization/IEffectivePermissionReader.cs`
- Delete: `src/Application/IdentityAccess/Authorization/IPermissionEvaluator.cs`
- Modify: `src/Infrastructure/Identity/CurrentTenant.cs`
- Modify: `src/Infrastructure/Identity/PermissionEvaluator.cs`
- Modify: `src/Infrastructure/Auditing/SecurityDenialAuditWriter.cs`
- Modify: `src/Infrastructure/DependencyInjection.cs`
- Modify: `tests/Application.UnitTests/Common/Behaviours/PermissionAuthorizationBehaviourTests.cs`
- Modify: `tests/Application.UnitTests/Architecture/ModuleBoundaryArchitectureTests.cs`

- [ ] **Step 1 (RED): Pin the neutral contracts in behavior tests**

Change the authorization-behavior tests to mock `IAuthorizationTenantContext` and `IPermissionAuthorizer`, with `Guid? TenantId` at the shared boundary. Add cases proving empty/missing tenant IDs fail closed and denial audit evidence carries the same tenant GUID.

Run:

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~PermissionAuthorizationBehaviourTests"
```

Expected: compile/test failure because the neutral ports do not exist.

- [ ] **Step 2 (GREEN): Add the two consumer-oriented Common ports**

`IAuthorizationTenantContext` exposes only the validated `Guid? TenantId`. `IPermissionAuthorizer` exposes application-scoped and tenant-scoped checks using identity GUID, optional tenant GUID only on the tenant overload, stable permission code, and cancellation token. Do not expose memberships, roles, claims, sessions, or EF types.

- [ ] **Step 3: Adapt IdentityAccess without weakening its typed domain**

Keep `ICurrentTenant`, `TenantId`, `Permissions`, `IEffectivePermissionReader`, membership/role queries, and session validation in IdentityAccess. Make `CurrentTenant` implement both the typed IdentityAccess port and the neutral authorization context, converting `TenantId` to/from `Guid` only at the adapter edge. Make `PermissionEvaluator` implement `IPermissionAuthorizer` and convert the neutral tenant GUID to `TenantId` before evaluating the existing IA-owned catalog and membership rules.

- [ ] **Step 4: Remove IdentityAccess types from Common denial evidence**

Change `SecurityDenialAudit.TenantId` to `Guid?`; convert it back to `TenantId` inside `SecurityDenialAuditWriter` immediately before creating the IdentityAccess audit event. Preserve null semantics, timeout, allowlisted fields, and correlation/session behavior.

- [ ] **Step 5: Wire both interfaces to the existing scoped adapters**

Register the concrete `CurrentTenant` once and map both interfaces to that scoped instance. Register `IPermissionAuthorizer` to the existing `PermissionEvaluator`. Do not change cookie/session validation or permission catalog contents.

- [ ] **Step 6 (REFACTOR): Remove the two resolved backend debt exceptions**

Delete `AuthorizationBehaviour.cs` and `ISecurityDenialAuditWriter.cs` from the Task 1 allowlist and rerun:

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~ModuleBoundaryArchitectureTests|FullyQualifiedName~PermissionAuthorizationBehaviourTests|FullyQualifiedName~RequestAuthorizationMetadataTests"
```

Expected: PASS; Common authorization code imports no IdentityAccess namespace. IdentityAccess request metadata and existing permission behavior remain unchanged.

Suggested commit boundary: `refactor: isolate shared authorization pipeline from identity`.

### Task 3: Move the Identity-shaped persistence port to IdentityAccess

**Files:**
- Create: `src/Application/IdentityAccess/Persistence/IIdentityAccessDbContext.cs`
- Delete: `src/Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/Infrastructure/Data/ApplicationDbContext.cs`
- Modify: `src/Infrastructure/DependencyInjection.cs`
- Modify: all exact consumers in Appendix A
- Test: `tests/Application.UnitTests/Architecture/ModuleBoundaryArchitectureTests.cs`
- Test: `tests/Application.UnitTests/Architecture/IdentityGuidContractTests.cs`
- Test: `tests/Application.FunctionalTests/IdentityAccess/Api/EndpointShapeTests.cs`
- Test: `tests/Infrastructure.IntegrationTests/Architecture/OutboxInfrastructureShapeTests.cs`

- [ ] **Step 1 (RED): Add persistence ownership assertions**

Assert that `Application/Common` declares no interface containing a `DbSet<>` of a business-module type, `IIdentityAccessDbContext` exists under the IdentityAccess namespace, and `ApplicationDbContext` implements it. Run the module-boundary and identity-guid tests; expected RED because the current Common interface owns IdentityAccess sets.

- [ ] **Step 2 (GREEN): Relocate and rename the port without changing its contract**

Copy the exact members of `IApplicationDbContext` to `IIdentityAccessDbContext` under `Application/IdentityAccess/Persistence`, update Appendix A consumers, and make the physical context implement the new interface. Register `IIdentityAccessDbContext` to the same scoped `ApplicationDbContext` instance.

- [ ] **Step 3: Delete the Common interface and its final debt exception**

Run `rg -n "IApplicationDbContext|Application.Common.Interfaces.IApplicationDbContext" src tests`; expected: no matches. Delete the old file and the last backend allowlist entry.

- [ ] **Step 4: Prove this is not a data-model change**

Run:

```powershell
dotnet build CleanArchitecture.slnx --no-restore
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~ModuleBoundaryArchitectureTests|FullyQualifiedName~IdentityGuidContractTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~OutboxInfrastructureShapeTests"
git status --short -- src/Infrastructure/Data/Migrations
```

Expected: build/tests PASS and no new or modified migration/snapshot caused by this relocation. Existing unrelated migration changes, if any, must remain untouched and be identified from the initial status snapshot.

- [ ] **Step 5 (REFACTOR): Document the next-module rule in the interface XML docs**

State that another module defines its own consumer-oriented persistence interface and the same physical context may implement it. Do not add placeholder WhatsApp sets or a generic repository.

Suggested commit boundary: `refactor: give identity access ownership of its persistence port`.

### Task 4: Separate shared Web contract mechanics from IdentityAccess declarations

**Prerequisite:** offset-pagination-standard applied and verified.

**Files:**
- Create: `src/Web/Infrastructure/ApiEndpointContractExtensions.cs`
- Create: `src/Web/Infrastructure/IRequestIntegrityValidator.cs`
- Create: `src/Web/Infrastructure/RequestIntegrity.cs`
- Create: `src/Web/Endpoints/Identity/IdentityProblemMetadata.cs`
- Create: `src/Web/Endpoints/Identity/IdentityRequestIntegrity.cs`
- Modify: `src/Web/Infrastructure/ApiProblemMetadata.cs`
- Modify: `src/Web/Infrastructure/NeutralBodyBindingInvocationMiddleware.cs`
- Modify: `src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs`
- Modify: `src/Web/DependencyInjection.cs`
- Modify: `src/Web/Endpoints/Identity.cs`
- Modify: `src/Web/Endpoints/Identity/AccountLifecycleEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/ContextEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/DocumentDisputeEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/ExternalLoginEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/InvitationEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/MembershipEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/PasswordEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/PersonalEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/RoleEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/SessionEndpoints.cs`
- Modify: `src/Web/Endpoints/Platform/PlatformEndpoints.cs`
- Modify: `src/Web/Endpoints/Platform/PlatformInvitationEndpoints.cs`
- Modify: `src/Web/Endpoints/Platform/PlatformMfaEndpoints.cs`
- Modify: `src/Web/Endpoints/Platform/PlatformRetentionEndpoints.cs`
- Test: `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs`
- Test: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`

- [ ] **Step 1 (RED): Characterize metadata and request-integrity behavior**

Add reflection/HTTP assertions proving generic extension/metadata types live under `Web.Infrastructure`, Identity-specific codes and optional-session/neutral-flow declarations live under Identity endpoint ownership, every declared code reaches OpenAPI, same-origin antiforgery remains fail-closed, and enumeration-sensitive routes retain their exact neutral outcomes.

- [ ] **Step 2 (GREEN): Split mechanics from declarations**

Move only reusable `ApiProblemContract`, metadata record types, `WithApiProblemDetails`, `WithCreatedLocation`, and generic body-binding mechanics into `ApiEndpointContractExtensions.cs`. Keep `ApiNeutralBodyBindingFailureMetadata` and `ApiNeutralBodyBindingExecutionState` neutral because the shared invocation middleware and exception handler consume them. Keep only genuinely cross-project codes in `ApiProblemMetadata`; move Identity-specific codes, `BoundedAttempt` grouping, optional-session refusal, and neutral-flow choices to `IdentityProblemMetadata`/Identity endpoint extensions.

- [ ] **Step 3: Extract reusable antiforgery mechanics**

Move exact-origin validation plus raw antiforgery validation to `RequestIntegrity`. Define `IRequestIntegrityValidator.ValidateAsync(HttpContext, Endpoint?)` as the shared consumer port. `IdentityRequestIntegrity` implements it by preserving optional-session authentication/refusal before delegating to `RequestIntegrity`; it also retains authentication-cookie names and token rotation/deletion. Inject the neutral port into `ProblemDetailsExceptionHandler`, register the Identity adapter in `Web/DependencyInjection.cs`, and update `NeutralBodyBindingInvocationMiddleware` only for relocated neutral metadata namespaces. No shared infrastructure file may call `Identity.ValidateAntiforgery`. Endpoint success DTOs/statuses and error code/status mappings must remain byte-for-byte equivalent in served OpenAPI.

- [ ] **Step 4 (REFACTOR): Update endpoints and prove contract parity**

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~ProblemDetailsContractTests|FullyQualifiedName~OpenApiContractTests|FullyQualifiedName~ForwardedHeadersSecurityTests"
```

Expected: PASS with the same response statuses, `application/problem+json`, `x-problem-codes`, Retry-After behavior, neutral public flows, and safe `500` behavior.

Suggested commit boundary: `refactor: separate web problem mechanics from identity contracts`.

### Task 5: Consume neutral SPA paths and remove cross-feature internal imports

**Prerequisite:** use the paths created by offset-pagination-standard; do not recreate or rename its problem transport, catalog, renderer, pagination, or field-helper artifacts.

**Files:**
- Create: `src/Web/ClientApp/src/api/useRead.js`
- Create: `src/Web/ClientApp/src/api/useRead.test.jsx`
- Create: `src/Web/ClientApp/src/api/useSubmit.js`
- Create: `src/Web/ClientApp/src/api/useSubmit.test.jsx`
- Create: `src/Web/ClientApp/src/components/PermissionLabel.jsx`
- Create: `src/Web/ClientApp/src/hooks/useFragmentToken.js`
- Create: `src/Web/ClientApp/src/features/identity/public.js`
- Create: `src/Web/ClientApp/src/features/identity/routes.js`
- Create: `src/Web/ClientApp/src/features/platform/routes.js`
- Delete: `src/Web/ClientApp/src/features/identity/useRead.js`
- Delete: `src/Web/ClientApp/src/features/identity/useRead.test.jsx`
- Delete: `src/Web/ClientApp/src/features/identity/useSubmit.js`
- Delete: `src/Web/ClientApp/src/features/identity/useSubmit.test.jsx`
- Delete: `src/Web/ClientApp/src/features/identity/PermissionLabel.jsx`
- Delete: `src/Web/ClientApp/src/features/identity/useFragmentToken.js`
- Modify: `src/Web/ClientApp/src/AppRoutes.jsx`
- Modify: `src/Web/ClientApp/src/AppRoutes.test.jsx`
- Modify: `src/Web/ClientApp/src/App.jsx`
- Modify: `src/Web/ClientApp/src/components/Home.jsx`
- Modify: `src/Web/ClientApp/src/components/NavMenu.jsx`
- Modify: `src/Web/ClientApp/src/components/NavMenu.test.jsx`
- Modify: `src/Web/ClientApp/src/components/NotFoundPage.jsx`
- Modify: `src/Web/ClientApp/src/components/api-authorization/ProtectedRoute.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/PlatformPanel.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/PlatformPanel.test.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.test.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/invitations/MfaRecoveryPage.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/invitations/MfaRecoveryPage.test.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/invitations/PlatformInvitationPages.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/invitations/PlatformInvitationPages.test.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/retention/PlatformRetentionPage.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/retention/PlatformRetentionPage.test.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/shared/PlatformStepUpForm.jsx`
- Modify: `src/Web/ClientApp/src/features/platform/shared/usePlatformStepUp.js`
- Modify: `src/Web/ClientApp/src/test/identityFiles.contract.test.js`
- Modify: `src/Web/ClientApp/src/test/materialUiMigration.contract.test.js`
- Create: `src/Web/ClientApp/src/test/moduleBoundaries.contract.test.js`
- Modify: `src/Web/ClientApp/src/i18n/catalog.contract.test.js`
- Modify: `src/Web/ClientApp/src/i18n/presentation.test.jsx`
- Modify: `src/Web/ClientApp/src/i18n/pseudoLanguageOverride.test.jsx`
- Modify: `src/Web/ClientApp/src/i18n/spanish.test.jsx`

- [ ] **Step 1 (RED): Create the frontend import guard on the verified post-prerequisite tree**

Only after the prerequisite gate has passed, recursively scan every JavaScript/JSX source and test under `src/`, including feature tests, shared components, and `src/test`, and enforce this explicit policy: production code inside `features/<module>` may import its own internals but not sibling feature internals; production code outside `features/` may consume a feature only through its intentional `public.js` or `routes.js`; a test inside a feature may import the internals of that same feature; integration/i18n tests must use published entrypoints; no code or test may obtain shared transport, problem, field, or state infrastructure from a business feature. Neutral `src/api`/`src/components`/`src/hooks`/`src/i18n` imports remain allowed. First run with no migration exceptions.

- [ ] **Step 2: Prove the frontend guard fails and baseline only post-prerequisite debt**

Run:

```powershell
Push-Location src/Web/ClientApp
npm test -- src/test/moduleBoundaries.contract.test.js
Pop-Location
```

Expected RED: only imports that violate the policy in the verified post-prerequisite tree are reported; no prerequisite-owned old path is baselined. Add only those exact current importer/importee pairs, require stale entries to fail, and rerun; expected GREEN. This migration allowlist is removed by the remaining Task 5 steps.

- [ ] **Step 3 (RED): Retarget hook tests to neutral ownership**

Move the existing hook tests first and change imports to `src/api/useRead.js` and `src/api/useSubmit.js`. Run them; expected RED because the neutral hook files do not exist.

- [ ] **Step 4 (GREEN): Move hooks without changing their state machines**

Move the existing implementations, update them to import the prerequisite's neutral transport/problem modules, and preserve current loading, stale-data, retryability, refused, errored, and mutation behavior. No visible message may be introduced outside i18n.

- [ ] **Step 5: Publish only the Identity UI contract that external consumers need**

`features/identity/public.js` may re-export `IdentityProvider`, `useIdentity`, and the existing external-navigation contract; it must not re-export API clients, validators, screens, generic hooks, or problem internals. `features/identity/routes.js` and `features/platform/routes.js` are the separate composition entrypoints: they export only the page components already mounted by `AppRoutes.jsx`. `AppRoutes`, its test, and integration/i18n presentation tests consume these published entrypoints. A feature-local test may continue importing its own subject internals. Identity and Platform screens remain in their owning feature directories.

- [ ] **Step 6: Move proven shared presentation and update consumers**

Move `PermissionLabel` to `src/components` and the feature-agnostic URL-fragment reader to `src/hooks/useFragmentToken.js`. Replace Platform imports of generic problem rendering/field helpers with the exact neutral modules delivered by offset-pagination-standard. Retarget `catalog.contract.test.js` to the prerequisite's neutral validation/problem module. Keep Identity validators in `features/identity/fieldErrors.js`; if the prerequisite did not deliver the generic helper a Platform consumer needs, stop and amend/finish that prerequisite rather than duplicating it here.

- [ ] **Step 7 (REFACTOR): Remove every frontend debt exception**

Run:

```powershell
Push-Location src/Web/ClientApp
npm test -- src/api/useRead.test.jsx src/api/useSubmit.test.jsx src/components/ProblemMessage.test.jsx src/test/identityFiles.contract.test.js src/test/moduleBoundaries.contract.test.js
npm run lint
Pop-Location
```

Expected: PASS; the migration allowlist is empty. Cross-feature consumers use only `features/identity/public.js`; route composition and integration/i18n screen tests use only module-owned `routes.js`; shared infrastructure comes only from neutral paths. `NavMenu.jsx` imports no feature-internal file.

Suggested commit boundary: `refactor: move shared spa mechanics out of identity`.

### Task 6: Put cross-project contract tests and fixtures under neutral ownership

**Files:**
- Create: `src/Web/ClientApp/src/test/problemResponse.js`
- Modify: `src/Web/ClientApp/src/test/identityServer.js`
- Create: `tests/Application.FunctionalTests/Architecture/ApiFailureContractTests.cs`
- Create: `tests/Application.FunctionalTests/Architecture/LocalizedProblemCatalogContractTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`
- Delete: `tests/Application.FunctionalTests/IdentityAccess/Api/ErrorCatalogContractTests.cs`
- Modify: `src/Web/ClientApp/src/test/moduleBoundaries.contract.test.js`

- [ ] **Step 1 (RED): Pin neutral fixture/test ownership**

Add a SPA contract asserting generic RFC 9457 fixtures do not import a feature. Add functional test classes under `Architecture` for: every `/api/*` route declares pipeline-emittable errors; problem codes keep one HTTP status; every advertised code exists in every supported language.

- [ ] **Step 2 (GREEN): Extract, do not duplicate**

Move only the generic problem-response builder from `identityServer.js` to `problemResponse.js`; keep cookies, sessions, Identity routes, and Identity seed behavior in `identityServer.js`. Move the three global OpenAPI/localization assertions to the neutral functional-test classes and leave endpoint-specific IA assertions in `IdentityAccess/Api/OpenApiContractTests.cs`.

- [ ] **Step 3 (REFACTOR): Prove one source of truth**

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~ApiFailureContractTests|FullyQualifiedName~LocalizedProblemCatalogContractTests|FullyQualifiedName~OpenApiContractTests"
Push-Location src/Web/ClientApp
npm test -- src/test src/api src/components/ProblemMessage.test.jsx
Pop-Location
```

Expected: PASS; no copied global assertion remains in the IdentityAccess test namespace, all supported languages cover served codes, and feature-specific fixtures import the neutral problem builder rather than owning it.

Suggested commit boundary: `test: neutralize shared api contract harnesses`.

### Task 7: Final boundary, behavior, and documentation verification

**Files:**
- Create: `docs/architecture/module-boundaries.md`
- Modify only if the final test proves a gap: `tests/Application.UnitTests/Architecture/ModuleBoundaryArchitectureTests.cs`
- Modify only if the final test proves a gap: `src/Web/ClientApp/src/test/moduleBoundaries.contract.test.js`

- [ ] **Step 1: Document the implemented boundary, not a future framework**

Record: IdentityAccess is one peer business module; Common cannot import a business module; features consume neutral infrastructure or another feature's deliberately tiny `public.js`; IdentityAccess retains account/session/membership/role/invitation/credential rules; the shared HTTP philosophy is RFC 9457 failures plus endpoint-specific successes, not a universal envelope; future module persistence ports are consumer-owned and may share one physical context.

- [ ] **Step 2: Record explicit follow-ups and non-goals**

State that outbox/delivery generalization is deferred until a real second module proves shared delivery semantics. Do not define `IMessageBus`, a generic outbox aggregate, WhatsApp tables, WhatsApp permissions, or a WhatsApp adapter in this change.

- [ ] **Step 3: Run the final targeted suite**

```powershell
dotnet build CleanArchitecture.slnx --no-restore
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "FullyQualifiedName~Architecture|FullyQualifiedName~PermissionAuthorizationBehaviourTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~Architecture|FullyQualifiedName~LocalizationContractTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "FullyQualifiedName~ApiFailureContractTests|FullyQualifiedName~LocalizedProblemCatalogContractTests|FullyQualifiedName~OpenApiContractTests|FullyQualifiedName~ProblemDetailsContractTests"
Push-Location src/Web/ClientApp
npm test
npm run lint
npm run build
Pop-Location
```

Expected: all commands PASS; both dependency allowlists are empty; no new EF migration exists; generated OpenAPI and frontend catalogs remain consistent.

- [ ] **Step 4: Verify scope and preservation**

Run:

```powershell
git status --short
git diff --name-only -- docs/features/whatsapp-bot openspec/changes/offset-pagination-standard .agents .codex AGENTS.md CLAUDE.md
```

Expected: names may still appear because they were dirty before this work. The binary-diff hash and sorted OpenSpec file-hash listing match the baselines captured immediately before this plan's first slice (or the refreshed baseline after separately authorized prerequisite work); no protected path changed because of this plan.

Suggested commit boundary: `docs: define peer business module boundaries`.

## Acceptance criteria

- [ ] `Application/Common` imports no `Application.IdentityAccess` or `Domain.IdentityAccess` type.
- [ ] `AuthorizationBehaviour` uses only neutral Common ports; IdentityAccess adapters still own session, membership, role, tenant, and permission evaluation rules.
- [ ] `IIdentityAccessDbContext` belongs to IdentityAccess, and `ApplicationDbContext` implements it without a migration or schema change.
- [ ] Adding a future module does not require adding its `DbSet`s to Common.
- [ ] Shared RFC 9457 writing, endpoint metadata mechanics, request-integrity mechanics, SPA transport, `ProblemMessage`, hooks, i18n, and global contract tests are not owned by `features/identity` or Identity endpoint classes.
- [ ] Identity-specific error codes, neutral public-flow rules, domain validators, API clients, provider, and screens remain IdentityAccess-owned.
- [ ] Every endpoint declares and tests its failure contract; every advertised code has localized text in every supported language.
- [ ] Success responses remain endpoint-specific; no universal envelope or copied external architecture is introduced.
- [ ] Backend Common -> business-module and frontend cross-feature-internal imports are enforced by tests with empty debt allowlists.
- [ ] WhatsApp remains unimplemented and its Proposed ADR/SPEC remain untouched.
- [ ] Outbox/delivery generalization remains a documented follow-up pending a second real consumer.

## Rollback notes

- Roll back by slice, in reverse order. Each suggested commit boundary is intended to be independently reversible.
- File moves must be behavior-preserving; if a parity test fails, revert that slice rather than introducing compatibility aliases in Common.
- Never roll back by restoring the whole working tree because it contains unrelated user work.
- Persistence-port rollback changes C# ownership only; it must never create or reverse a database migration.
- If offset-pagination-standard changes its planned neutral client paths before application, rebase Tasks 4-6 onto its verified output and update this plan before implementation; do not create duplicate compatibility modules.

## Review workload and delivery slicing

Deliver in the suggested slices: boundary guards; authorization; persistence; Web contracts; SPA mechanics; test ownership; documentation. Review each slice against its focused tests and its dependency diff. Split a slice further when ownership and behavior cannot be reviewed together safely; do not use a universal changed-line threshold. The offset-pagination-standard change remains a separately reviewed prerequisite rather than being folded into this work.

## Appendix A: Exact `IApplicationDbContext` consumer inventory

Update these paths to `IIdentityAccessDbContext` in Task 3 (refresh with `rg -l "\bIApplicationDbContext\b" src tests` immediately before editing and stop if new non-Identity consumers appear):

- `src/Application/IdentityAccess/Context/GetIdentityContext/GetIdentityContextHandler.cs`
- `src/Application/IdentityAccess/Context/SelectTenant/SelectTenantHandler.cs`
- `src/Application/IdentityAccess/Credentials/ChangePassword/ChangePasswordHandler.cs`
- `src/Application/IdentityAccess/Credentials/CredentialSessionEffects.cs`
- `src/Application/IdentityAccess/Credentials/PasswordRecovery/PasswordRecoveryHandlers.cs`
- `src/Application/IdentityAccess/ExternalLogins/ExternalCallbackRecorder.cs`
- `src/Application/IdentityAccess/ExternalLogins/ExternalLoginHandlers.cs`
- `src/Application/IdentityAccess/Invitations/AcceptInvitation/AcceptInvitationHandler.cs`
- `src/Application/IdentityAccess/Invitations/CancelInvitation/CancelInvitationHandler.cs`
- `src/Application/IdentityAccess/Invitations/InvitationDelivery.cs`
- `src/Application/IdentityAccess/Invitations/InviteMember/InviteMemberHandler.cs`
- `src/Application/IdentityAccess/Invitations/IOfferableRoleReader.cs`
- `src/Application/IdentityAccess/Invitations/RegisterInvitedUser/RegisterInvitedUserHandler.cs`
- `src/Application/IdentityAccess/Invitations/ResendInvitation/ResendInvitationHandler.cs`
- `src/Application/IdentityAccess/Lifecycle/IdentityLifecycleEffects.cs`
- `src/Application/IdentityAccess/Lifecycle/IdentityLifecycleHandlers.cs`
- `src/Application/IdentityAccess/Members/IMembershipAdministrationStore.cs`
- `src/Application/IdentityAccess/Members/MembershipHandlers.cs`
- `src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs`
- `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs`
- `src/Application/IdentityAccess/People/CreatePersonalContext/CreatePersonalContextHandler.cs`
- `src/Application/IdentityAccess/People/Documents/DocumentDisputeHandlers.cs`
- `src/Application/IdentityAccess/People/PersonalContextFactory.cs`
- `src/Application/IdentityAccess/People/Profile/PersonalProfileHandlers.cs`
- `src/Application/IdentityAccess/People/RegisterPersonal/RegisterPersonalHandler.cs`
- `src/Application/IdentityAccess/Platform/Administrators/PlatformAdministrators.cs`
- `src/Application/IdentityAccess/Platform/Bootstrap/BootstrapPlatformOwner.cs`
- `src/Application/IdentityAccess/Platform/Bootstrap/RecoverPendingPlatformOwnerInvitationHandler.cs`
- `src/Application/IdentityAccess/Platform/Identities/PlatformIdentityLifecycle.cs`
- `src/Application/IdentityAccess/Platform/Invitations/ConfirmPlatformInviteeHandler.cs`
- `src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInviteeHandler.cs`
- `src/Application/IdentityAccess/Platform/Mfa/PlatformMfaGate.cs`
- `src/Application/IdentityAccess/Platform/Mfa/PlatformMfaHandlers.cs`
- `src/Application/IdentityAccess/Platform/Mfa/RecoverPlatformMfaHandler.cs`
- `src/Application/IdentityAccess/Platform/Organizations/OrganizationTenantLifecycle.cs`
- `src/Application/IdentityAccess/Platform/PlatformInvitationDelivery.cs`
- `src/Application/IdentityAccess/Platform/Retention/PlatformRetention.cs`
- `src/Application/IdentityAccess/Roles/IRoleAdministrationStore.cs`
- `src/Application/IdentityAccess/Roles/RoleHandlers.cs`
- `src/Application/IdentityAccess/Sessions/CreateSession/CreateSessionHandler.cs`
- `src/Application/IdentityAccess/Sessions/ManageSessions/SessionManagementHandlers.cs`
- `src/Application/IdentityAccess/Sessions/RevokeCurrentSession/RevokeCurrentSessionHandler.cs`
- `src/Application/IdentityAccess/Sessions/SessionIssuer.cs`
- `tests/Application.FunctionalTests/IdentityAccess/Api/EndpointShapeTests.cs`
- `tests/Application.UnitTests/Architecture/IdentityGuidContractTests.cs`
- `tests/Application.UnitTests/IdentityAccess/EmailConfirmationRequiredProducerTests.cs`
- `tests/Infrastructure.IntegrationTests/Architecture/OutboxInfrastructureShapeTests.cs`
