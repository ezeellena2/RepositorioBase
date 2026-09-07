# Identity Access Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**First-increment goal (Tasks 1–16):** Deliver a multitenant SaaS identity slice with safe PostgreSQL upgrades, Organization registration and confirmation, revocable sessions, active-tenant permissions, invitation onboarding, reliable email delivery, a one-time MFA-bound Platform owner bootstrap, and a reachable same-origin React client.

**Architecture:** ASP.NET Core Identity is a credential adapter. Domain/Application own lifecycle, tenants, memberships, roles, permissions, sessions, invitations, MFA, audit, and outbox rules. Platform uses the same active-tenant membership and permission evaluator, never a global bypass. PostgreSQL enforces UUID and tenant-local invariants; the BFF cookie references a revocable server session; React keeps antiforgery state only in memory.

**Tech Stack:** .NET 10, ASP.NET Core Identity, EF Core 10, PostgreSQL/Npgsql, Aspire, MediatR, FluentValidation, React 19, Vite, Vitest, MSW, NUnit, Shouldly, Reqnroll, and Playwright.

---

**Status:** Tasks 1–16 record the implemented Organization/Platform foundation and its historical checks. Their `Review` states and recorded limitations remain unchanged; implementation is not human acceptance or complete B2B/B2C coverage. Task 17 is partly taken: C1 was accepted on 2026-09-06, and [Task 18](#task-18-remove-the-registration-state-oracle-not-only-its-status-difference) is implemented and verified against real PostgreSQL on that approval alone. C3 and C7 were accepted on 2026-09-06 for synthetic data only, and [Task 19](#task-19-persist-personal-ownership-and-protected-ardni-atomically) and [Task 20](#task-20-deliver-personal-signup-own-profile-and-context-switching) are **done and verified** on them. C2 and C4 were accepted the same day on the same terms, which makes Tasks 21, 22 and 23 executable in that order. C5 and C6 remain proposals — A3 and A4 still apply to C6 — so [Tasks 24–28](#continuation-to-local-b2bb2c-functional-completion) are still blocked. Real personal data, production, §14.3's named residual, live provider registration and the reactivation half of C4's `Recovery` purpose were all withheld. This continuation was planned against `d6d1b0ab5cf2b66ccff2828b471d7977f540a958`; no verification run is claimed for any task after 18.

## First-increment Review Workload Forecast (historical)

- Decision needed before apply: No
- Chained PRs recommended: Yes
- Delivery decision: size:exception (maintainer accepted direct work on `main`; this is not a chain strategy, and the five work units remain commit, verification, and rollback boundaries)
- 400-line budget risk: High

Suggested review units:

1. stack/migrations/test harness
2. domain/persistence
3. authz/registration/session
4. invitations/outbox/MFA/Platform backend
5. React/Platform/E2E

The maintainer accepted `size:exception`; proceed in the defined work units with commit, verification, and rollback boundaries.

## First-increment Preconditions and Execution Rules (Tasks 1–16)

These exclusions, sequencing constraints, and delivery decisions describe the first increment only. They do not exclude the required continuation below or authorize its unapproved policy changes. The compile-safe protocol and architectural boundaries remain reusable; Task 17 must explicitly approve any changed contract before dependent implementation.

- [ ] Obtain human approval of [SPEC.md](../../features/identity-access/SPEC.md) and [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md) before Task 3; IA-002 and IA-003 are already `Complete`.
- [ ] Continue in this dependency order: IA-004 domain -> IA-004 persistence -> IA-005 roles -> IA-005 authorization -> IA-006 registration -> IA-007 sessions -> IA-008 invitations/outbox -> IA-012 Platform invitation persistence then MFA -> IA-014 bootstrap/operations -> IA-009 final React/E2E acceptance.
- [ ] Do not introduce `UserSession` before IA-007 or Invitation behavior before IA-008. IA-004 may establish reusable `AuditEvent`; IA-006 may establish confirmation `OutboxMessage`/`OutboxSecret`.
- [ ] Keep Personal/DNI, password recovery/change, Google OIDC, impersonation, destructive Platform actions, tenant-private business-data access, and production operations outside this increment.
- [ ] Use `@solid`, `@architecture-patterns`, `@postgresql-expert`, `@frontend-react-best-practices`, and `@verification-before-completion`.
- [ ] Execute one RED -> GREEN -> REFACTOR cycle at a time. Commits require explicit commit authority and imply neither push nor deployment.

### Compile-safe type-introduction protocol

For every task that introduces a production type or module:

1. write a shape RED that loads an assembly and calls `Assembly.GetType("fully.qualified.Name")`, inspects EF/endpoint metadata by string, or checks a file path without importing it;
2. run it and record a runtime assertion failure such as “expected non-null/file/route, was missing,” never a compilation/module-resolution failure;
3. add only minimal compile shells and rerun the shape test GREEN;
4. write behavioral tests against those shells and record a runtime assertion failure or deliberate `NotImplementedException`;
5. implement minimum behavior, rerun GREEN, REFACTOR, rerun, and only then commit.

### Normative contracts and ownership

- `IA-REQ-006..008` are owned by IA-005 and IA-007; `IA-REQ-026` by IA-005 through IA-008 and IA-014 for their respective events; `IA-REQ-030` and IA-REQ-038 are IA-005-only; `IA-REQ-033..036` are IA-004-only; `IA-REQ-037` is IA-003 and IA-004; IA-REQ-041 is IA-012; IA-REQ-039..040 and IA-REQ-042..046 are IA-014. IA-006..008/014 apply IA-REQ-038; IA-009 adds evidence only.
- Migration order starts `BaselinePostgreSql` (current template) -> `IdentityAccess` (core identity-access schema), followed by named incremental migrations. Every migration addition reruns empty-to-latest and BaselinePostgreSql-to-latest preservation.
- Sample entity keys remain `int`; identity-access and ASP.NET Identity keys are `Guid`/`uuid`. Composite foreign keys repeat `TenantId`.
- Every Application request implements exactly one of `IPublicRequest` or `[Authorize]`; the current inventory is in Task 6 and remains architecture-guarded.
- `UserSession.ActiveTenantId` is the sole tenant-context source. Headers, query strings, client state, claims, and route IDs never establish it.
- Authentication claims contain only `UserId` and opaque `SessionId`.
- Platform authority is an active `TenantType.Platform` membership plus explicit `platform.*` permission; `IsSuperAdmin`, global claims/roles, and context bypasses are forbidden.
- Antiforgery cookie is `__Host-XSRF-TOKEN`; request header is `X-CSRF-TOKEN`; no `X-CSRF-Refresh` header exists.
- Raw tokens/provider errors never enter outbox payloads, audit, logs, Problem Details, URLs sent to the server, or telemetry.
- Expected business failures are internal typed `Result`/`Result<T>` values; exceptions represent unexpected failures. Web never serializes Result or a universal `{ success, data, error }` envelope. All non-success responses use one RFC 9457 Problem Details writer; React knows only endpoint DTOs and that external error contract.

## Task 1: Establish the PostgreSQL harness and target stack

**Requirements:** IA-REQ-031  
**Tracking:** IA-002

**Files:**

- Modify: `tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj`
- Create: `tests/Infrastructure.IntegrationTests/InfrastructureTestSetup.cs`
- Create: `tests/Infrastructure.IntegrationTests/Infrastructure/TestServices.cs`
- Create: `tests/Infrastructure.IntegrationTests/Infrastructure/WebApiFactory.cs`
- Create: `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs`
- Modify: `.template.config/template.json`
- Modify: `Directory.Build.props`
- Modify: `src/AppHost/Program.cs`
- Modify: `src/AppHost/AppHost.csproj`
- Modify: `tests/TestAppHost/Program.cs`
- Modify: `tests/TestAppHost/TestAppHost.csproj`
- Modify: `src/Infrastructure/DependencyInjection.cs`
- Modify: `src/Infrastructure/Infrastructure.csproj`
- Modify: `src/Web/appsettings.json`
- Reconcile client paths so `src/Web/ClientApp` is the active React client and `src/Web/ClientApp-Angular` remains the alternate template client.

- [x] **Step 1: GREEN harness prerequisite**

Add `Shouldly`, `Aspire.Hosting.Testing`, and `Microsoft.AspNetCore.Mvc.Testing`; add project references to `src/Web/Web.csproj`, `src/Shared/Shared.csproj`, and `tests/TestAppHost/TestAppHost.csproj`. Mirror `tests/Application.FunctionalTests/FunctionalTestSetup.cs`: start TestAppHost, wait for `Services.Database`, obtain PostgreSQL's connection string, build the local factory, and expose `TestServices.CreateScope()`.

Run: `dotnet build tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj -v minimal`  
Expected: PASS; the first integration RED compiles and can start PostgreSQL.

- [x] **Step 2: RED - current provider**

With existing `ApplicationDbContext`, assert `Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL"`.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter StackBaselineTests`  
Recorded RED: the prior provider configuration did not satisfy the PostgreSQL contract.

- [x] **Step 3: GREEN - React/PostgreSQL**

Make Npgsql/Aspire the unconditional database stack, retain Angular/React/API-only client choices, and remove SQLite/SQL Server from active source paths and template metadata.

```powershell
# Historical completion evidence: the active React client and retained Angular client are now in their current paths.
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter StackBaselineTests
dotnet new install .
dotnet new ca-sln -cf react -db postgresql -o "$env:TEMP\ca-identity-smoke"
dotnet build "$env:TEMP\ca-identity-smoke\CleanArchitecture.slnx" -v minimal
```

Expected: PASS.

- [x] **Step 4: REFACTOR and commit**

```bash
git add .template.config Directory.Build.props src tests
git commit -m "build: target React and PostgreSQL"
```

## Task 2: Create BaselinePostgreSql and make startup non-destructive

**Requirements:** IA-REQ-032, IA-REQ-037  
**Tracking:** IA-003

**Files:**

- Create: `src/Infrastructure/Data/Migrations/<timestamp>_BaselinePostgreSql.cs`
- Create: `src/Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- Create: `tests/Infrastructure.IntegrationTests/Data/DatabaseInitialisationTests.cs`
- Modify: `src/Infrastructure/Data/ApplicationDbContextInitialiser.cs`
- Modify: `src/Web/Program.cs`
- Modify: `tests/Application.FunctionalTests/FunctionalTestSetup.cs`

- [x] **Step 1: RED - current destructive behavior**

Using only current types, initialize, insert a `TodoList` sentinel, initialize again, and assert the sentinel remains and no Identity user exists.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter DatabaseInitialisationTests`  
Recorded RED: destructive initialization or default identity seeding did not preserve the sentinel/no-user contract.

- [x] **Step 2: Create the current-template baseline**

```powershell
dotnet ef migrations add BaselinePostgreSql --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet ef migrations script 0 BaselinePostgreSql --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --idempotent
```

Expected: the migration contains only the current template model, not identity-access tables.

- [x] **Step 3: GREEN - migrate without seed/destruction**

`InitialiseAsync(CancellationToken)` calls `MigrateAsync`. Remove `RoleManager<IdentityRole>`, user/role/demo seeds, fixed credentials, `EnsureDeletedAsync`, and `EnsureCreatedAsync` before Guid work begins. Migrate before accepting traffic.

```powershell
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter DatabaseInitialisationTests
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj
```

Expected: PASS; restart preserves the Todo sentinel and creates no user/role.

- [x] **Step 4: REFACTOR and commit**

```bash
git add src/Infrastructure/Data src/Web/Program.cs tests
git commit -m "fix: establish safe PostgreSQL migrations"
```

## Task 3: Model core identity, pending organizations, memberships, and audit

**Requirements:** IA-REQ-001, IA-REQ-002; IA-004 foundation for IA-REQ-033..036  
**Tracking:** IA-004

**Files:**

- Modify: `src/Domain/Common/BaseEntity.cs`
- Create: `src/Domain/IdentityAccess/Identities/IdentityAccountStatus.cs`
- Create: `src/Domain/IdentityAccess/Tenants/Tenant.cs`
- Create: `src/Domain/IdentityAccess/Tenants/TenantId.cs`
- Create: `src/Domain/IdentityAccess/Tenants/TenantSlug.cs`
- Create: `src/Domain/IdentityAccess/Tenants/TenantType.cs`
- Create: `src/Domain/IdentityAccess/Tenants/TenantStatus.cs`
- Create: `src/Domain/IdentityAccess/Organizations/OrganizationProfile.cs`
- Create: `src/Domain/IdentityAccess/Organizations/NormalizedCuit.cs`
- Create: `src/Domain/IdentityAccess/Memberships/TenantMembership.cs`
- Create: `src/Domain/IdentityAccess/Memberships/MembershipId.cs`
- Create: `src/Domain/IdentityAccess/Memberships/MembershipStatus.cs`
- Create: `src/Domain/IdentityAccess/Auditing/AuditEvent.cs`
- Create: `tests/Domain.UnitTests/IdentityAccess/IdentityAccessContractShapeTests.cs`
- Create: `tests/Domain.UnitTests/IdentityAccess/TenantLifecycleTests.cs`
- Create: `tests/Domain.UnitTests/IdentityAccess/OrganizationProfileTests.cs`
- Create: `tests/Domain.UnitTests/IdentityAccess/AuditEventTests.cs`

- [x] **Step 1: Shape RED**

Reflect by fully qualified names for `Tenant`, `OrganizationProfile`, `TenantMembership`, `AuditEvent`, `TenantStatus.PendingConfirmation`, and `MembershipStatus.PendingConfirmation`.

Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter IdentityAccessContractShapeTests`  
Expected: FAIL at runtime because expected types/members are missing.

- [x] **Step 2: Compile shells**

Add `BaseEntity<TId>` while keeping `BaseEntity : BaseEntity<int>`; add UUID ID structs, enums, and private-constructor shells with throwing transitions.

Run the shape command.  
Expected: PASS.

- [x] **Step 3: Behavioral RED**

Test normalized CUIT, Organization-only profile, pending responsible membership, activation transitions, suspended/terminal rejection, authorization-version increments, and an `AuditEvent` factory that requires correlation ID and accepts only allowlisted non-secret scalar fields.

Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter "TenantLifecycleTests|OrganizationProfileTests|AuditEventTests"`  
Expected: FAIL at runtime with `NotImplementedException` or incorrect state/payload.

- [x] **Step 4: GREEN, REFACTOR, commit**

Implement invariants without EF/HTTP dependencies. Do not introduce roles, `UserSession`, invitations, or outbox types.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter IdentityAccess
git add src/Domain tests/Domain.UnitTests/IdentityAccess
git commit -m "feat: model core identity access"
```

Expected: PASS.

## Task 4: Persist core identity access and add IdentityAccess migration

**Requirements:** IA-REQ-001, IA-REQ-002, IA-REQ-033..037  
**Tracking:** IA-004

**Files:**

- Modify: `src/Infrastructure/Identity/ApplicationUser.cs`
- Modify: `src/Infrastructure/Identity/IdentityService.cs`
- Modify: `src/Infrastructure/DependencyInjection.cs`
- Modify: `src/Infrastructure/Data/ApplicationDbContext.cs`
- Modify: `src/Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/Application/Common/Interfaces/IIdentityService.cs`
- Modify: `src/Application/Common/Interfaces/IUser.cs`
- Modify: `src/Application/Common/Behaviours/LoggingBehaviour.cs`
- Modify: `src/Application/Common/Behaviours/PerformanceBehaviour.cs`
- Modify: `src/Application/Common/Behaviours/AuthorizationBehaviour.cs`
- Modify: `src/Web/Services/CurrentUser.cs`
- Modify: `src/Domain/Common/BaseAuditableEntity.cs`
- Modify: `src/Infrastructure/Data/Interceptors/AuditableEntityInterceptor.cs`
- Modify: `src/Infrastructure/Data/Configurations/TodoItemConfiguration.cs`
- Modify: `src/Application/TodoItems/Commands/UpdateTodoItemDetail/UpdateTodoItemDetail.cs`
- Modify: `src/Web/Endpoints/TodoItems.cs`
- Modify: `tests/Application.FunctionalTests/Infrastructure/TestApp.cs`
- Modify: `tests/Application.FunctionalTests/Infrastructure/WebApiFactory.cs`
- Modify: `tests/Application.UnitTests/Common/Behaviours/RequestLoggerTests.cs`
- Modify: `tests/Application.FunctionalTests/TodoLists/Commands/CreateTodoListTests.cs`
- Modify: `tests/Application.FunctionalTests/TodoLists/Commands/UpdateTodoListTests.cs`
- Modify: `tests/Application.FunctionalTests/TodoItems/Commands/CreateTodoItemTests.cs`
- Modify: `tests/Application.FunctionalTests/TodoItems/Commands/UpdateTodoItemTests.cs`
- Modify: `tests/Application.FunctionalTests/TodoItems/Commands/UpdateTodoItemDetailTests.cs`
- Create: `src/Application/Common/Interfaces/IAuditWriter.cs`
- Create: `src/Infrastructure/Auditing/AuditWriter.cs`
- Create: `src/Infrastructure/Data/Interceptors/AppendOnlyAuditInterceptor.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/ApplicationUserConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/TenantConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/OrganizationProfileConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/TenantMembershipConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/AuditEventConfiguration.cs`
- Create: `src/Infrastructure/Data/Migrations/<timestamp>_IdentityAccess.cs`
- Modify: `src/Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/IdentityAccessMappingTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/AuditPersistenceTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/ConcurrencyTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/Data/MigrationUpgradeTests.cs`

- [x] **Step 1: Metadata RED**

Inspect EF metadata by entity/property name for core entities, unique normalized email/slug/CUIT, membership uniqueness, composite tenant FKs, explicit deletes, concurrency tokens (including `TodoItem`), `Guid?` audit actors, and append-only audit mapping.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "IdentityAccessMappingTests|AuditPersistenceTests"`  
Expected: FAIL at runtime because entities/constraints are absent and actor IDs are strings.

- [x] **Step 2: GREEN - complete Guid conversion and persistence**

Use `IdentityUser<Guid>`, `IdentityRole<Guid>`, and `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. Change `IUser.Id`, every `IIdentityService` input/result, behavior caller, `CurrentUser`, functional helper/mock, logger test, `BaseAuditableEntity.CreatedBy/LastModifiedBy`, interceptor assignment, and Todo audit assertion to `Guid`/`Guid?`. The Task 2 initializer must already contain no `RoleManager`; no integer-key or non-generic Identity role survives.

Map `OrganizationProfile` with a normalized unique CUIT; expose every core `DbSet` through `IApplicationDbContext`; configure composite tenant FKs and deletes. `AuditWriter` creates correlation-bearing allowlisted rows. The interceptor rejects tracked update/delete; the migration adds a PostgreSQL trigger rejecting raw SQL update/delete.

```powershell
rg -n "IdentityUser(?!<)|IdentityRole(?!<)|Task<\(Result Result, string UserId\)>|(GetUserNameAsync|IsInRoleAsync|AuthorizeAsync|DeleteUserAsync)\(string|string\? (CreatedBy|LastModifiedBy)" src tests --pcre2 --glob "*.cs"
dotnet build CleanArchitecture.slnx -v minimal
```

Expected: the scan returns no stale declarations; build PASS.

- [x] **Step 3: Create the second migration**

```powershell
dotnet ef migrations add IdentityAccess --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet ef migrations script BaselinePostgreSql IdentityAccess --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj
```

Review conversion of baseline string Identity/audit actor IDs to UUID using validated explicit casts, then safely recreate affected PK/FK/index constraints. This is the second migration, never “initial.”

- [x] **Step 4: RED/GREEN migration and audit proof**

`MigrationUpgradeTests` migrates (a) empty -> latest and (b) `BaselinePostgreSql` -> latest after inserting a Todo sentinel and parseable GUID-string Identity row. Assert sentinel preservation, UUID columns, core constraints, no pending migrations, and raw/tracked `AuditEvent` update/delete rejection. `ConcurrencyTests` uses two real PostgreSQL DbContexts reading one `TodoItem`; the first `UpdateTodoItemDetailCommand` write persists and the stale second conditional write fails.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "IdentityAccessMappingTests|AuditPersistenceTests|ConcurrencyTests|MigrationUpgradeTests"`
Expected initial RED: missing mapping/migration/append-only runtime assertions; after GREEN: both upgrade paths PASS.

- [x] **Step 5: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: persist core identity access"
```

## Task 5: Model and persist roles and permissions

**Requirements:** IA-REQ-006..010, IA-REQ-013, IA-REQ-026  
**Tracking:** IA-005

**Files:**

- Create: `src/Domain/IdentityAccess/Authorization/Role.cs`
- Create: `src/Domain/IdentityAccess/Authorization/RoleId.cs`
- Create: `src/Domain/IdentityAccess/Authorization/Permission.cs`
- Create: `src/Domain/IdentityAccess/Authorization/RolePermission.cs`
- Create: `src/Domain/IdentityAccess/Authorization/MembershipRole.cs`
- Create: `src/Application/IdentityAccess/Authorization/Permissions.cs`
- Create: `src/Application/IdentityAccess/Authorization/IPermissionEvaluator.cs`
- Create: `src/Infrastructure/Identity/PermissionEvaluator.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/RoleConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/PermissionConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/RolePermissionConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/MembershipRoleConfiguration.cs`
- Create: `src/Infrastructure/Data/Migrations/<timestamp>_TenantAuthorization.cs`
- Modify: `src/Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `tests/Domain.UnitTests/IdentityAccess/IdentityAccessContractShapeTests.cs`
- Create: `tests/Domain.UnitTests/IdentityAccess/RolePermissionTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/RolePermissionMappingTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Auditing/RoleMembershipAuditTests.cs`

- [x] **Step 1: Shape RED, then shells**

Reflect for all five domain types and evaluator.  
Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter IdentityAccessContractShapeTests`  
Expected RED: runtime missing-type assertion. Add only shells; rerun PASS.

- [x] **Step 2: Behavioral RED**

Test immutable `resource.action` codes, including distinct `platform.admins.read` and `platform.admins.manage`; system-role protection, normalized tenant-local names, allowed tenant types, one active membership as evaluator input, cross-tenant role/permission rejection, authorization-version increments, and exact `membership.changed`/`role.changed` audit events with correlation and allowlisted payload.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter RolePermissionTests
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter RoleMembershipAuditTests
```

Expected: FAIL at runtime on shell behavior/missing audit rows.

- [x] **Step 3: GREEN - persistence and incremental migration**

Implement catalog synchronization that seeds `platform.admins.read` separately from `platform.admins.manage`, evaluator over explicit identity/tenant inputs, composite membership-role/role-permission FKs, version updates, and transactional audits. Do not add `UserSession`.

```powershell
dotnet ef migrations add TenantAuthorization --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "RolePermissionMappingTests|MigrationUpgradeTests"
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter RolePermissionTests
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter RoleMembershipAuditTests
```

Expected: empty-to-latest and baseline-to-latest preserve the sentinel, report no pending migrations, and all focused tests PASS.

- [x] **Step 4: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: add tenant authorization model"
```

## Task 6: Enforce authorization and the shared API contract

**Requirements:** IA-REQ-006..013, IA-REQ-026, IA-REQ-030, IA-REQ-038  
**Tracking:** IA-005

**Files:**

- Create: `src/Application/Common/Security/IPublicRequest.cs`
- Modify: `src/Application/Common/Security/AuthorizeAttribute.cs`
- Create: `src/Application/Common/Exceptions/AuthorizationMetadataMissingException.cs`
- Create: `src/Application/Common/Models/ApplicationErrorCategory.cs`
- Create: `src/Application/Common/Models/ApplicationError.cs`
- Modify: `src/Application/Common/Models/Result.cs`
- Create: `src/Application/IdentityAccess/Common/IdentityAccessErrors.cs`
- Modify: `src/Application/Common/Interfaces/IIdentityService.cs`
- Modify: `src/Application/Common/Behaviours/AuthorizationBehaviour.cs`
- Modify: `src/Application/TodoLists/Commands/CreateTodoList/CreateTodoList.cs`
- Modify: `src/Application/TodoLists/Commands/UpdateTodoList/UpdateTodoList.cs`
- Modify: `src/Application/TodoLists/Commands/DeleteTodoList/DeleteTodoList.cs`
- Modify: `src/Application/TodoLists/Queries/GetTodos/GetTodos.cs`
- Modify: `src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs`
- Modify: `src/Application/TodoItems/Commands/UpdateTodoItem/UpdateTodoItem.cs`
- Modify: `src/Application/TodoItems/Commands/UpdateTodoItemDetail/UpdateTodoItemDetail.cs`
- Modify: `src/Application/TodoItems/Commands/DeleteTodoItem/DeleteTodoItem.cs`
- Modify: `src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs`
- Create: `src/Application/IdentityAccess/Authorization/ICurrentTenant.cs`
- Create: `src/Application/Common/Interfaces/ISecurityDenialAuditWriter.cs`
- Modify: `src/Infrastructure/Identity/IdentityService.cs`
- Modify: `src/Infrastructure/Identity/IdentityResultExtensions.cs`
- Create: `src/Infrastructure/Auditing/SecurityDenialAuditWriter.cs`
- Create: `src/Web/Infrastructure/ApiProblemDetails.cs`
- Create: `src/Web/Infrastructure/ApiProblemDetailsMapper.cs`
- Create: `src/Web/Infrastructure/ResultHttpExtensions.cs`
- Create: `src/Web/Infrastructure/ApiProblemMetadata.cs`
- Create: `src/Web/Infrastructure/ApiAuthorizationMiddlewareResultHandler.cs`
- Modify: `src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs`
- Modify: `src/Web/Infrastructure/ApiExceptionOperationTransformer.cs`
- Modify: `src/Web/DependencyInjection.cs`
- Modify: `tests/Application.FunctionalTests/FunctionalTestSetup.cs`
- Modify: `tests/Application.FunctionalTests/Infrastructure/TestApp.cs`
- Create: `tests/Application.UnitTests/Common/Models/ResultContractShapeTests.cs`
- Create: `tests/Application.UnitTests/Common/Models/ResultTests.cs`
- Create: `tests/Application.UnitTests/Architecture/RequestAuthorizationMetadataTests.cs`
- Create: `tests/Application.UnitTests/Architecture/ExistingApplicationRequestAuthorizationTests.cs`
- Create: `tests/Application.UnitTests/Common/Behaviours/PermissionAuthorizationBehaviourTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Authorization/PermissionMatrixTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`

- [x] **Step 1: Compile-safe shape RED**

Reflect by name for `IPublicRequest`, `AuthorizationMetadataMissingException`, `ApplicationErrorCategory`, `ApplicationError`, generic `Result<T>`, and `AuthorizeAttribute.Permission/RequiresTenant`; check Web contract files by path without importing missing types. `ExistingApplicationRequestAuthorizationTests` asserts this current inventory; every row is `[Authorize]` with `RequiresTenant=false`, because its sole endpoint group calls `RequireAuthorization()` and current Todo entities have no `TenantId`. No existing request is public.

| Existing request | Permission | Tenant |
|---|---|---|
| `CreateTodoListCommand` | `todos.write` | no |
| `UpdateTodoListCommand` | `todos.write` | no |
| `DeleteTodoListCommand` | `todos.write` | no |
| `GetTodosQuery` | `todos.read` | no |
| `CreateTodoItemCommand` | `todos.write` | no |
| `UpdateTodoItemCommand` | `todos.write` | no |
| `UpdateTodoItemDetailCommand` | `todos.write` | no |
| `DeleteTodoItemCommand` | `todos.write` | no |
| `GetWeatherForecastsQuery` | `weather.read` | no |

The same future-request guard classifies `RegisterPlatformInvitee`, `ConfirmPlatformInvitee`, and `RecoverPendingPlatformOwnerInvitation` as `IPublicRequest`: each is limited by antiforgery, a bound token where applicable, server-derived recipient/state, and no membership/elevation. Only valid opaque business states receive the neutral response: antiforgery rejects as `400 antiforgery_validation_failed`, and rate-limit rejection is `429 rate_limit_exceeded` plus `Retry-After`. Registration accepts a PasswordOptions-valid password only for a missing matching identity; an existing identity ignores it and receives generic flow behavior. `BeginPlatformMfaEnrollment`, `VerifyPlatformMfaEnrollment`, and `AcknowledgePlatformRecoveryCodes` are `[Authorize]` with `RequiresTenant=false` because they require the authenticated confirmed matching identity and invitation token before Platform activation. All Platform operations after activation require `[Authorize]`, active Platform tenant, and their explicit `platform.*` permission.

Run: `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "RequestAuthorizationMetadataTests|ExistingApplicationRequestAuthorizationTests|ResultContractShapeTests"`
Expected: FAIL at runtime with missing type/member/file assertions, never compilation failure.

- [x] **Step 2: Add shells and migrate current Result atomically**

Add the marker/exception/error/Result/Web shells. `ApplicationErrorCategory` contains only expected mappings (`Validation`, `Authentication`, `Authorization`, `NotFound`, `Conflict`, `RateLimited`); there is no `Unexpected` category. `ApplicationError` carries stable code/category, optional safe detail, and validation fields. `Result` and `Result<T>` carry success/value or one typed error.

In the same compile-safe change, replace current `Result.Errors` use: `IIdentityService.CreateUserAsync` returns `Result<Guid>` after Task 4's Guid conversion; `IdentityService`, `IdentityResultExtensions`, and `tests/Application.FunctionalTests/Infrastructure/TestApp.cs` use stable IdentityAccess errors rather than provider descriptions. Expose a factory-owned `HttpClient` from `FunctionalTestSetup`.

```powershell
dotnet build CleanArchitecture.slnx -v minimal
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "RequestAuthorizationMetadataTests|ResultContractShapeTests"
```

Expected: build and shape tests PASS; behavior remains deliberately unimplemented.

- [x] **Step 3: Behavioral RED - authorization and Result semantics**

Test unmarked rejection before handler, invalid dual marking, anonymous public request, `401` invalid identity, `403` missing permission, suspended tenant/membership, one-membership isolation, cross-tenant `404`, and header/route spoof rejection. The architecture guard scans every `IRequest` in Application so present and future requests have exactly one marker; the inventory test proves the nine existing request classifications. Use a fake validated `ICurrentTenant`; its persisted-session adapter belongs to IA-007.

Test a denied mutation whose business transaction rolls back still commits one `authorization.denied` event through `SecurityDenialAuditWriter` using a separate DbContext/transaction. Its allowlist contains correlation ID, actor/session/tenant IDs, permission code, outcome, and timestamp only—no resource value, email, token, cookie, or provider text.

`ResultTests` proves typed success/value and expected failure code/category, prevents success-with-error/failure-with-value, and confirms no `Unexpected` result category.

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "ResultTests|RequestAuthorizationMetadataTests|PermissionAuthorizationBehaviourTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter PermissionMatrixTests
```

Expected: runtime assertions fail on shell Result behavior, authorization fall-through, or missing denial audit.

- [x] **Step 4: Behavioral RED - runtime and OpenAPI**

`ProblemDetailsContractTests`, through `FunctionalTestSetup.HttpClient`, cover semantic `200` DTO, existing `201` plus `Location`, empty `204`, and runtime `400/401/403/404/409/500`. Map the Task 4 stale `UpdateTodoItemDetailCommand` write to typed `Conflict` code `todo_item_concurrency_conflict`; its endpoint is RFC 9457 `409` with that stable code and opaque `traceId`. Every failure must be `application/problem+json` with matching status, stable `code`, opaque `traceId`, safe optional `detail`, validation-only field-indexed `errors`, and no stack, exception, provider, PII, or secret data. The generic `500` comes only from an unexpected exception. Assert neither internal Result fields nor universal `success/data/error` fields appear.

`OpenApiContractTests` rejects missing/mismatched success schemas, statuses, required headers, supported error statuses, Problem Details schema, or endpoint error codes. IA-006 later adds neutral `202`; IA-007 adds normalized `429` plus `Retry-After`; IA-008 adds identity `201 Location`/`200` evidence to these same suites.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: FAIL at runtime because current responses omit `code`/`traceId`, unexpected failures fall through, media types/metadata are incomplete, or internal/universal shapes leak.

- [x] **Step 5: GREEN - shared mapping, writer, and metadata**

Enforce exactly one of `IPublicRequest` or `[Authorize]`; remove role-name authorization; persist denial audit independently. Map expected Result categories to HTTP only in `ResultHttpExtensions`. `ApiProblemDetailsMapper` and `IProblemDetailsService` own RFC 9457 output. `ProblemDetailsExceptionHandler` uses them for known exceptions and a redacted generic `500`; `ApiAuthorizationMiddlewareResultHandler` uses the same writer for generated `401/403`. `ApiProblemMetadata` and `ApiExceptionOperationTransformer` publish endpoint-specific status/schema/header/code contracts. Never serialize Result.

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "ResultTests|RequestAuthorizationMetadataTests|PermissionAuthorizationBehaviourTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PermissionMatrixTests|ProblemDetailsContractTests|OpenApiContractTests"
dotnet build CleanArchitecture.slnx -v minimal
```

Expected: PASS; `401/403` and safe `500` use the shared writer. IA-REQ-030 and IA-REQ-038 remain owned only by IA-005.

- [x] **Step 6: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: enforce authorization and API contracts"
```

## Task 7: Register and confirm organizations

**Requirements:** IA-REQ-003..005, IA-REQ-026, IA-REQ-027, IA-REQ-029; applies IA-REQ-038  
**Tracking:** IA-006

**Files:**

- Create: `src/Domain/IdentityAccess/Outbox/OutboxMessage.cs`
- Create: `src/Domain/IdentityAccess/Outbox/OutboxSecret.cs`
- Create: `src/Domain/IdentityAccess/Outbox/OutboxSecretStatus.cs`
- Create: `src/Application/Common/Interfaces/IApplicationTransaction.cs`
- Create: `src/Application/Common/Interfaces/IIdentityAccountService.cs`
- Create: `src/Application/Common/Interfaces/ISecureTokenGenerator.cs`
- Create: `src/Application/Common/Interfaces/ITokenHasher.cs`
- Create: `src/Application/Common/Interfaces/IOutboxSecretWriter.cs`
- Create: `src/Application/IdentityAccess/Organizations/RegisterOrganization/IRegistrationIdempotencyStore.cs`
- Create: `src/Application/IdentityAccess/Sessions/IValidatedOptionalSession.cs`
- Create: `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganization.cs`
- Create: `src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmail.cs`
- Create: `src/Infrastructure/Security/SecureTokenGenerator.cs`
- Create: `src/Infrastructure/Security/VersionedTokenHasher.cs`
- Create: `src/Infrastructure/Outbox/OutboxSecretWriter.cs`
- Create: `src/Infrastructure/IdentityAccess/Organizations/RegistrationIdempotencyStore.cs`
- Create: `src/Domain/IdentityAccess/Organizations/RegistrationSubmission.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/RegistrationSubmissionConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/OutboxMessageConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/OutboxSecretConfiguration.cs`
- Create: `src/Infrastructure/Data/Migrations/<timestamp>_RegistrationMessaging.cs`
- Modify: `src/Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `src/Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/Infrastructure/Data/ApplicationDbContext.cs`
- Modify: `src/Web/DependencyInjection.cs`
- Modify: `src/Web/Program.cs`
- Create: `src/Web/Endpoints/Identity/AntiforgeryEndpoints.cs`
- Create: `src/Web/Endpoints/Identity/RegistrationEndpoints.cs`
- Create: `src/Web/Endpoints/Identity/Contracts/AntiforgeryResponse.cs`
- Create: `tests/Application.UnitTests/Architecture/RegistrationApplicationShapeTests.cs`
- Create: `tests/Application.UnitTests/IdentityAccess/Organizations/RegistrationIdempotencyTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegisterOrganizationTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Organizations/ConfirmEmailTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/AntiforgeryTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`

- [x] **Step 1: Shape RED, then shells**

Reflect for both requests, `IRegistrationIdempotencyStore`/durable `RegistrationSubmission`, outbox types and exact routes `GET /api/identity/antiforgery`, `POST /api/identity/organizations/register`, and `POST /api/identity/confirm-email`. Assert `OutboxMessage.AttemptCount`, `NextAttemptAt`, `FailureCode`; and `OutboxSecret.ExpiresAt`, terminal state/reason, ciphertext, receipt/evidence.

Run: `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter RegistrationApplicationShapeTests`  
Expected RED: runtime missing-type/member/route assertion. Add request/domain/endpoint shells; public requests implement `IPublicRequest`; rerun PASS.

- [x] **Step 2: Behavioral RED**

`RegistrationIdempotencyTests` first prove the Application service computes one canonical key from normalized caller scope and normalized registration intent, atomically claims a durable submission before effects, and returns its recorded neutral result; storage uniqueness coordinates claims but does not decide business idempotency. `RegisterOrganizationTests` prove sequential replay and contract-appropriate concurrent replay of equivalent anonymous registration each return the same bodyless `202` and leave exactly one organization, responsible membership, outbox-message/secret set, and audit-event set. They preserve true branches: existing identity remains neutral with no creation, authenticated mismatched email rejects, and invalid/revoked supplied session is `401`, never anonymous. Until IA-007 provides persisted sessions, the production optional-session adapter fails closed on any supplied cookie while functional tests inject a trusted adapter for the authenticated branch.

Confirmation tests cover atomic activation of identity/tenant/membership, injected rollback, idempotent replay, suspended/terminal rejection, and exact `organization.registration.requested`/`identity.confirmed` audit events. Outbox tests assert confirmation intent shares the transaction, payload has no token, and encrypted secret expires.

Before GREEN, extend shared contract tests: antiforgery is `200` with its endpoint DTO; registration is neutral bodyless `202`; confirmation is bodyless `204`; supported `400/401/409/500` responses use declared stable codes and Problem Details; runtime and OpenAPI contain no Result or universal envelope.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RegisterOrganizationTests|ConfirmEmailTests|ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: runtime shell/state/audit or status/schema/header drift failures.

- [x] **Step 3: GREEN - routes, CSRF, migration**

Configure antiforgery in Web DI and middleware in `Program.cs`. `GET /api/identity/antiforgery` emits the Secure, HttpOnly, SameSite=Lax, Path=/, host-only `__Host-XSRF-TOKEN` and returns `AntiforgeryResponse` with `Cache-Control: no-store`. POST routes require exact origin and `X-CSRF-TOKEN`. Generate 32-byte tokens; persist only versioned hash plus encrypted expiring envelope. `OutboxSecret` retains evidence after ciphertext clearing. Apply `ApiProblemMetadata` with exact statuses/codes; map handler Results through the shared Web boundary.

```powershell
dotnet ef migrations add RegistrationMessaging --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RegisterOrganizationTests|ConfirmEmailTests|ProblemDetailsContractTests|OpenApiContractTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "AntiforgeryTests|MigrationUpgradeTests"
```

Expected: focused tests and both migration paths PASS with no pending migrations.

- [x] **Step 4: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: register and confirm organizations"
```

## Task 8: Add revocable sessions, login controls, and active context

**Requirements:** IA-REQ-006..008, IA-REQ-019..026, IA-REQ-029, IA-REQ-031; applies IA-REQ-038  
**Tracking:** IA-007

**Files:**

- Create: `src/Domain/IdentityAccess/Sessions/UserSession.cs`
- Create: `src/Domain/IdentityAccess/Sessions/UserSessionId.cs`
- Create: `src/Application/IdentityAccess/Sessions/CreateSession/CreateSession.cs`
- Create: `src/Application/IdentityAccess/Sessions/RevokeCurrentSession/RevokeCurrentSession.cs`
- Create: `src/Application/IdentityAccess/Context/GetIdentityContext/GetIdentityContext.cs`
- Create: `src/Application/IdentityAccess/Context/SelectTenant/SelectTenant.cs`
- Create: `src/Infrastructure/Identity/CurrentSession.cs`
- Create: `src/Infrastructure/Identity/CurrentTenant.cs`
- Create: `src/Infrastructure/Identity/SessionCookieEvents.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/UserSessionConfiguration.cs`
- Create: `src/Infrastructure/Data/Migrations/<timestamp>_UserSessions.cs`
- Modify: `src/Infrastructure/DependencyInjection.cs`
- Create: `src/Web/Infrastructure/Identity/LoginRateLimitKeyMiddleware.cs`
- Create: `src/Web/Infrastructure/Identity/LoginRateLimitPartitioner.cs`
- Modify: `src/Web/DependencyInjection.cs`
- Modify: `src/Web/Program.cs`
- Create: `src/Web/Endpoints/Identity/SessionEndpoints.cs`
- Create: `src/Web/Endpoints/Identity/ContextEndpoints.cs`
- Create: `src/Web/Endpoints/Identity/Contracts/IdentityContextResponse.cs`
- Modify: `src/Web/Endpoints/Users.cs`
- Create: `tests/Application.UnitTests/Architecture/IdentitySessionShapeTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/IdentityOptionsTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/SessionCookieTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`
- Modify: `tests/Application.FunctionalTests/Application.FunctionalTests.csproj`

- [x] **Step 1: Shape RED, then shells**

Reflect for `UserSession`, commands, `CurrentTenant`, and exact routes: `POST /api/identity/sessions`, `DELETE /api/identity/sessions/current`, `GET /api/identity/context`, `PUT /api/identity/context/tenant`.

Run: `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter IdentitySessionShapeTests`  
Expected RED: runtime missing-type/route assertion. Add shells with exact `IPublicRequest`/`[Authorize]`; rerun PASS.

- [x] **Step 2: Behavioral RED - session and tenant**

Test idle/absolute expiry, revoke transition, neutral invalid credentials, confirmed/active account, cookie flags, CSRF/origin, and trusted optional-session registration. Exactly one active membership sets `ActiveTenantId`; zero/multiple leaves null. Selection validates active membership and persists it; revoked/expired session is `401`; headers cannot override it. Emit exact secret-free `signin.succeeded`, `signin.failed`, `session.created`, and `session.revoked` audit events.

Before GREEN, extend shared contract tests: create/revoke session are bodyless `204`; context GET and tenant selection are `200 IdentityContextResponse`; `400/401/403/404/409/500` use declared codes and the shared Problem Details writer; OpenAPI matches runtime.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "SessionTests|ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: runtime state, audit, Problem Details, or OpenAPI drift assertions.

- [x] **Step 3: RED - exact Identity and transport controls**

`IdentityOptionsTests` asserts:

- password length 12; uppercase, lowercase, digit, non-alphanumeric; four unique characters;
- confirmed account required;
- new users lockable, five failures, 15-minute Identity lockout.

`SessionTests`, using injected `TimeProvider`/`FakeTimeProvider`, independently exhausts:

- IP transport partition: 20 requests per 5 minutes, zero queue;
- normalized-account transport partition: 10 requests per 15 minutes, zero queue.

Use different accounts for the IP case and one unknown normalized email from different test IPs for the account case so Identity lockout does not mask transport limits. Both return the same generic credential response before the threshold; the next returns RFC 9457 `429` through the shared writer with stable code, opaque trace ID, and `Retry-After`. Separately prove Identity lockout persists after five failures and valid credentials recover only after advancing 15 minutes.

Extend `ProblemDetailsContractTests` and `OpenApiContractTests` before GREEN so both runtime and OpenAPI require the login endpoint's `429` Problem Details code/schema and `Retry-After` header.

```powershell
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter IdentityOptionsTests
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "SessionTests&Category=LoginControls"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "ProblemDetailsContractTests|OpenApiContractTests"
```

Expected: runtime option/threshold/recovery assertions fail before configuration.

- [x] **Step 4: GREEN - options, chained partitions, cookie session**

Configure exact `PasswordOptions`, `SignInOptions`, and `LockoutOptions` in `src/Infrastructure/DependencyInjection.cs`. In Web DI, register named `identity-login` as chained IP and normalized-account fixed-window partitions backed by injectable time. Middleware buffers only the login JSON body, normalizes the email, stores an opaque SHA-256 partition key, restores the body, and never logs IP/email/key. In `Program.cs`, run key extraction before `UseRateLimiter`, apply the named policy only to POST sessions, and write rejected leases through `IProblemDetailsService` with `Retry-After`. Transport throttles and Identity lockout remain distinct; IA-007 owns this `429` normalization.

Cookie `__Host-ia-auth` contains only user/session IDs. Validate the persisted row on every request. Rotate session ID and antiforgery on sign-in; revoke/delete/rotate on sign-out. Tenant selection does not rotate antiforgery. Remove `AllowAnyOrigin`, `MapIdentityApi<ApplicationUser>()`, and uncontracted Identity endpoints.

```powershell
dotnet ef migrations add UserSessions --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "IdentityOptionsTests|SessionCookieTests|MigrationUpgradeTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "SessionTests|ProblemDetailsContractTests|OpenApiContractTests"
```

Expected: all focused tests and both migration paths PASS; no pending migrations.

- [x] **Step 5: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: add controlled tenant sessions"
```

## Task 9: Model and persist invitations

**Requirements:** IA-REQ-014..018, IA-REQ-026, IA-REQ-027, IA-REQ-029  
**Tracking:** IA-008

**Files:**

- Create: `src/Domain/IdentityAccess/Invitations/Invitation.cs`
- Create: `src/Domain/IdentityAccess/Invitations/InvitationStatus.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/InvitationConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/InvitationRoleConfiguration.cs`
- Create: `src/Infrastructure/Data/Migrations/<timestamp>_Invitations.cs`
- Modify: `src/Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `tests/Domain.UnitTests/IdentityAccess/IdentityAccessContractShapeTests.cs`
- Create: `tests/Domain.UnitTests/IdentityAccess/InvitationTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/InvitationMappingTests.cs`

- [x] **Step 1: Shape RED, then shells**

Reflect for Invitation types, expiry/state/token-hash members, and tenant-bearing role association.  
Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter IdentityAccessContractShapeTests`  
Expected RED: runtime missing-type/member assertion. Add shells; rerun PASS.

- [x] **Step 2: Behavioral RED**

Test Organization-only recipient, normalized email, same-tenant initial roles, expiry, cancel, one-shot accept, reissue invalidation, replay idempotency, and concurrency.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter InvitationTests
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter InvitationMappingTests
```

Expected: runtime shell/constraint failures.

- [x] **Step 3: GREEN - mapping and migration**

Persist only token hash, concurrency token, explicit deletes, and composite tenant FKs. Add `Invitations`; rerun empty/latest and baseline/latest tests.

```powershell
dotnet ef migrations add Invitations --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "InvitationMappingTests|MigrationUpgradeTests"
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter InvitationTests
```

Expected: PASS with sentinel preserved and no pending migrations.

- [x] **Step 4: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: model secure invitations"
```

## Task 10: Orchestrate invitation onboarding through exact routes

**Requirements:** IA-REQ-014..018, IA-REQ-026, IA-REQ-027, IA-REQ-029, IA-REQ-047; applies IA-REQ-038\r
**Tracking:** IA-008

**Files:**

- Create: `src/Application/IdentityAccess/Invitations/InviteMember/InviteMember.cs`
- Create: `src/Application/IdentityAccess/Invitations/RegisterInvitedUser/RegisterInvitedUser.cs`
- Create: `src/Application/IdentityAccess/Invitations/AcceptInvitation/AcceptInvitation.cs`
- Create: `src/Application/IdentityAccess/Invitations/ResendInvitation/ResendInvitation.cs`
- Create: `src/Application/IdentityAccess/Invitations/CancelInvitation/CancelInvitation.cs`
- Create: `src/Web/Endpoints/Identity/InvitationEndpoints.cs`
- Create: `src/Web/Endpoints/Identity/Contracts/InvitationCreatedResponse.cs`
- Create: `src/Web/Endpoints/Identity/Contracts/InvitationAcceptanceResponse.cs`
- Create: `tests/Application.UnitTests/Architecture/InvitationApplicationShapeTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Invitations/InvitationTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Auditing/InvitationAuditTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`

- [x] **Step 1: Shape RED, then shells**

Reflect for requests and exact routes: `POST /api/tenants/{tenantId}/invitations`, `POST /api/invitations/register`, and `POST /api/invitations/accept`. Assert no preview or `/api/identity/invitations/**` route.

Run: `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter InvitationApplicationShapeTests`  
Expected RED: runtime missing-type/route assertion. Add shells; public registration and authenticated acceptance carry the correct marker; rerun PASS.

- [x] **Step 2: Behavioral RED**

Test `members.invite`, confirmed inviter, Organization-only, route `tenantId == ActiveTenantId` without establishing context, same-tenant roles, and atomic hash/outbox/secret creation. New invitee registration creates unconfirmed identity plus confirmation intent but no membership; existing identity ignores credential input and receives a generic notice. Only a confirmed, authenticated, matching email accepts once. Emit exact `invitation.issued` and `invitation.accepted` audit rows.

Before GREEN, extend shared contract tests: issue is `201 InvitationCreatedResponse` with required `Location`; invitee registration is neutral bodyless `202`; acceptance is idempotent `200 InvitationAcceptanceResponse`; declared `400/401/403/404/409/500` codes/shapes match runtime/OpenAPI and never expose Result or a universal envelope.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "InvitationTests|InvitationAuditTests|ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: runtime shell, tenant, audit, status/header/schema, or drift failures.

- [x] **Step 3: GREEN, REFACTOR, commit**

Tokens arrive only in JSON bodies; email links hold them in browser fragments. Persist raw token only as encrypted expiring `OutboxSecret`; never place it in outbox/audit/logs. Map typed handler Results only at Web, attach exact `ApiProblemMetadata`, and return each declared endpoint DTO/status/header.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "InvitationTests|InvitationAuditTests|ProblemDetailsContractTests|OpenApiContractTests"
git add src tests
git commit -m "feat: add invitation onboarding"
```

Expected: PASS.

**Evidence.** `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "InvitationTests|InvitationAuditTests|ProblemDetailsContractTests|OpenApiContractTests"` passes, and the whole affected surface passes as regression: Domain 134, Application unit 83, Infrastructure integration 143, Application functional 277, Web acceptance 6. Release build has no errors and the model has no pending migration.

IA-REQ-047 was added to the SPEC during this task and approved by the maintainer: an invitation may offer only roles whose permissions the inviter already holds, enforced on issue, on resend and on replacement.

Task 10 leaves the transactional message, the encrypted envelope and the invalidation of anything it supersedes. It does not dispatch: the worker, leases, CAS and backoff, decryption, rendering, sending, the email adapter and the test sink are Task 11, so IA-008 is not complete.

**Task 10 correction evidence (2026-09-04).** Invited-user confirmation now records a named, tenantless `identity.confirmed` audit in the same transaction as activation and confirmation consumption. `Invited_confirmation_is_audited_once_without_a_tenant_and_rollback_is_atomic` verifies rollback, successful retry, one audit, and replay without another audit. `Real_invitation_registration_messages_have_registered_delivery_handlers` exercises both actual registration branches through registered handlers and the dispatcher into an isolated sink, verifies settlement/replay, and rejects invitation/token content in a generic notice. The focused functional run passed **3/3**; evidence is `artifacts/current-fix/producer-dispatch-green.trx`. No membership gate or endpoint was added.

## Task 11: Dispatch outbox messages with deterministic backoff

**Requirements:** IA-REQ-018, IA-REQ-027..029  
**Tracking:** IA-008

**Files:**

- Create: `src/Application/Common/Interfaces/IOutboxSecretReader.cs`
- Create: `src/Application/Common/Interfaces/IIdentityEmailSender.cs`
- Create: `src/Infrastructure/Outbox/OutboxSecretReader.cs`
- Create: `src/Infrastructure/Outbox/OutboxDispatcher.cs`
- Create: `src/Infrastructure/Outbox/InvitationEmailDeliveryHandler.cs`
- Create: `src/Infrastructure/Outbox/EmailConfirmationDeliveryHandler.cs`
- Create: `src/Infrastructure/Email/IdentityEmailAdapter.cs`
- Create: `tests/Infrastructure.IntegrationTests/TestDoubles/TestEmailSink.cs`
- Create: `src/OutboxWorker/OutboxWorker.csproj`
- Create: `src/OutboxWorker/Program.cs`
- Create: `src/OutboxWorker/Worker.cs`
- Modify: `CleanArchitecture.slnx`
- Modify: `src/AppHost/Program.cs`
- Create: `tests/Infrastructure.IntegrationTests/Architecture/OutboxInfrastructureShapeTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/OutboxDeliveryTests.cs`

- [x] **Step 1: Shape RED, then shells**

Reflect for reader, dispatcher, handlers, adapter, worker, `TimeProvider`, and all retry/terminal members established in Task 7.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter OutboxInfrastructureShapeTests`  
Expected RED: runtime missing-type/member assertion. Add shells; rerun PASS.

- [x] **Step 2: Behavioral RED**

With fake time, test due-only claiming, `FOR UPDATE SKIP LOCKED` plus CAS generation, lease release, and message-ID idempotency. A transient failure increments `AttemptCount`, stores only an allowlisted redacted `FailureCode`, clears the lease, and sets `NextAttemptAt = now + min(30 seconds * 2^(attempt-1), 30 minutes)`; optional deterministic 0–20% message-ID jitter is disabled in tests. Before due time no claim occurs; after advancing time exactly one claim occurs. Eight exhausted attempts become permanent.

Also test acknowledged send followed by local-update failure, expired envelope, permanent provider failure, and success. Success atomically marks message delivered and clears ciphertext while retaining `OutboxSecret` status, reason, terminal timestamp, opaque provider receipt/hash evidence, expiry, and row. Expired/permanent cases clear ciphertext and retain evidence. Raw token and provider exception text never persist.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter OutboxDeliveryTests`  
Expected: runtime failures for early claims, duplicate delivery, missing delay/evidence, leaked provider text, or retained ciphertext.

- [x] **Step 3: GREEN, REFACTOR, commit**

Inject `TimeProvider`; decrypt only in memory; send with `OutboxMessage.Id` as idempotency key; reconcile adapter receipt before a retry. Production fails closed without wrapping-key/email configuration.

```powershell
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "OutboxInfrastructureShapeTests|OutboxDeliveryTests"
dotnet build CleanArchitecture.slnx -v minimal
git add src tests CleanArchitecture.slnx
git commit -m "feat: deliver identity outbox reliably"
```

Expected: PASS.

**Task 11 correction evidence (2026-09-04).** The generic worker shares complete application/infrastructure registration, including a null background actor and optional HTTP-server migration detection. Resend now performs bounded REST sends with absolute configured links and exact message-ID keys. Additive `OutboxDeliverySafety` persists first-attempt time and request fingerprints; claims increment the attempt budget before network access and lease one message at a time. Conditional owner/generation settlement updates message and secret in one local transaction. Every terminal failure clears pending ciphertext and preserves evidence. Replays stop before Resend's 24-hour retention boundary and changed requests stop without reusing the key for new content.

`dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "OutboxInfrastructureShapeTests|OutboxDeliveryTests|EmailConfigurationTests|Outbox_safety_upgrade"` passed **76/76**, including actual generic-host startup/seeded delivery, independent certificate-wrapped key providers, wrapping requirements outside explicit Development/Test/Testing environments, stale-worker settlement, delayed expiry, thrown-provider/unreadable/missing-handler exhaustion, required missing secrets, crashed attempts, prior-schema preservation, and real-adapter idempotency after acknowledged send/local rollback. Credential drift rejects an uncertain retry without a second HTTP send. The functional run above proves both actual registration purposes. Separate replay evidence passed **2/2** in `artifacts/current-fix/acknowledged-reconciliation.trx`; final focused evidence is `artifacts/current-fix/outbox-final-focused.trx`.

Independent final regression passed **734 applicable tests** (Domain 139, Application unit 83, Infrastructure 220, Functional 286, Acceptance 6), with no failures or skips. Release built with **0 errors** and two existing ASPIRE010 warnings; EF reported no pending model changes. Results and disclosed intermediate corrections are recorded in `artifacts/current-fix/implementation-evidence.md` and `artifacts/current-fix/independent-verification/`. These are local implementation checks, not formal approval or external email activation.

Resend account/domain/key activation and shared Production key/certificate provisioning remain operator prerequisites, documented in [EMAIL-SETUP.md](../../features/identity-access/EMAIL-SETUP.md). Default local delivery is explicitly disabled. The checks used isolated sinks/HTTP and PostgreSQL, with no real email or external provider mutation. Detailed RED/GREEN and final regression evidence is recorded in `artifacts/current-fix/implementation-evidence.md`; this correction record does not claim a commit or formal review approval.

## Task 12: Make every React identity journey reachable

**Requirements:** acceptance evidence, including IA-REQ-038; IA-009 owns no normative requirement  
**Tracking:** IA-009

**Files after Task 1 rename:**

- Modify: `src/Web/ClientApp/package.json`
- Modify: `src/Web/ClientApp/package-lock.json`
- Modify: `src/Web/ClientApp/vite.config.ts`
- Modify: `src/Web/ClientApp/src/App.jsx`
- Modify: `src/Web/ClientApp/src/AppRoutes.jsx`
- Modify: `src/Web/ClientApp/src/components/Layout.jsx`
- Modify: `src/Web/ClientApp/src/components/NavMenu.jsx`
- Replace: `src/Web/ClientApp/src/components/api-authorization/ProtectedRoute.jsx`
- Delete: `src/Web/ClientApp/src/components/api-authorization/AuthContext.jsx`
- Delete: `src/Web/ClientApp/src/components/api-authorization/LoginPage.jsx`
- Delete: `src/Web/ClientApp/src/components/api-authorization/RegisterPage.jsx`
- Create: `src/Web/ClientApp/src/test/setup.js`
- Create: `src/Web/ClientApp/src/test/server.js`
- Create: `src/Web/ClientApp/src/test/identityFiles.contract.test.js`
- Create: `src/Web/ClientApp/src/features/identity/api/identityClient.js`
- Create: `src/Web/ClientApp/src/features/identity/api/problemDetails.js`
- Create: `src/Web/ClientApp/src/features/identity/api/problemDetails.test.js`
- Create: `src/Web/ClientApp/src/features/identity/context/IdentityProvider.jsx`
- Create: `src/Web/ClientApp/src/features/identity/login/LoginPage.jsx`
- Create: `src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx`
- Create: `src/Web/ClientApp/src/features/identity/tenants/TenantSelector.jsx`
- Create: `src/Web/ClientApp/src/features/identity/invitations/InvitationPages.jsx`
- Create: `src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.jsx`
- Create: `src/Web/ClientApp/src/App.test.jsx`
- Create: `src/Web/ClientApp/src/AppRoutes.test.jsx`
- Create: `src/Web/ClientApp/src/features/identity/context/IdentityProvider.test.jsx`

- [x] **Step 1: Install MSW/test tooling and file-shape RED**

Install Vitest, jsdom, Testing Library, and MSW; configure `listen/resetHandlers/close`. A Node filesystem test checks modules/root files without importing missing modules.

Run: `npm test --prefix src/Web/ClientApp -- identityFiles.contract.test.js`  
Expected: FAIL at runtime because feature files are missing.

- [x] **Step 2: Add shells and route/root behavioral RED**

Add importable shells. `identityClient.js` is the sole fetch/response boundary; `problemDetails.js` validates only the external RFC 9457 shape and never imports or models internal Result. Mount `IdentityProvider` in `App.jsx`; define public `/login`, `/organizations/register`, `/invitations/register`, `/invitations/accept`; protected `/identity`, `/organizations/select`, and `/members/invite`; wrap protected elements with the replacement `ProtectedRoute`. Update `Layout`/`NavMenu` navigation and sign-out. Remove all legacy provider/page imports.

MSW/root tests render `App` with `MemoryRouter` and prove every route can be navigated/rendered, unauthenticated protected routes return to login with a safe return URL, signed-in navigation exposes tenant/invite actions, and logout clears context. Parser tests cover endpoint DTOs and `400/401/403/404/409/429/500` Problem Details, `Retry-After`, validation-only errors, safe diagnostics, and malformed/media-type drift. They reject internal Result and universal success/data/error envelopes; identity endpoints add no pagination wrapper.

```powershell
npm test --prefix src/Web/ClientApp -- App.test.jsx AppRoutes.test.jsx IdentityProvider.test.jsx problemDetails.test.js
```

Expected: runtime route/header/state assertions fail against shells.

- [x] **Step 3: GREEN - exact HTTP and antiforgery lifecycle**

Use exact SPEC API routes and `credentials: "same-origin"`. Parse every declared success DTO/status/header and every non-success through the sole boundary. Bootstrap `GET /api/identity/antiforgery` after initial load/page reload and successful sign-in/sign-out. On stable `antiforgery_validation_failed`, fetch a fresh pair and require mutation retry. Do not use `X-CSRF-Refresh`. Successful tenant selection keeps the pair because the server does not rotate it. Send `X-CSRF-TOKEN` only on mutations; keep request/invitation tokens in memory; strip invitation fragments with `history.replaceState`.

Keep Vite's same-origin `/api` proxy and add a test/config assertion that no absolute API origin or open CORS fallback exists. Delete legacy files and update every import.

```powershell
rg -n "AuthContext|components/api-authorization/(LoginPage|RegisterPage)|X-CSRF-Refresh|/api/identity/invitations|preview|result\.(succeeded|errors)|response\.(success|data|error)" src/Web/ClientApp/src
npm test --prefix src/Web/ClientApp
npm run lint --prefix src/Web/ClientApp
npm run build --prefix src/Web/ClientApp
```

Expected: scan returns no legacy contract/import; all commands PASS and every journey is reachable before Playwright.

- [x] **Step 4: REFACTOR and commit**

```bash
git add src/Web/ClientApp
git commit -m "feat: add reachable identity experience"
```

**Evidence.** `npm test` 55, `npm run lint` and `npm run build` clean, and the five .NET suites unchanged at 734
with Web.AcceptanceTests still 6/6.

**Resolved — antiforgery bootstrap on mount.** Implementing Step 3's bootstrap made an authenticated page load
answer `401` to its own `GET /api/identity/context`. The cause was the acceptance harness, not the server and not
the client: it injected the session cookie with `SetExtraHTTPHeadersAsync`, and Chromium discards that header as
soon as its own jar holds a cookie for the origin. The bootstrap's `Set-Cookie: __Host-XSRF-TOKEN` was the first
such cookie, so from that moment the browser rebuilt `Cookie` from the jar and the session was gone. Reading the
request headers with `AllHeadersAsync` — rather than `Request.Headers`, which omits what the network stack
adds — showed the context request carrying only `__Host-XSRF-TOKEN`.

The harness now stores the session in the browser's own cookie jar. A `__Host-` cookie may only be stored against
a secure URL and Aspire serves the test frontend over HTTP, so it is stored against `https://<host>`; cookies
ignore the port, and Chromium treats localhost as a secure context, so it is sent to the HTTP frontend. The
mount-time bootstrap is restored and pinned by `bootstraps the antiforgery pair on load without losing the
session`.

## Task 13: Verify the pre-Platform foundation

**Requirements:** pre-Platform acceptance evidence, including IA-REQ-038; normative ownership remains IA-002..IA-008
**Tracking:** IA-009 foundation only

**Files:**

- Create: `tests/Web.AcceptanceTests/Features/IdentityAccess.feature`
- Create: `tests/Web.AcceptanceTests/Pages/IdentityAccessPages.cs`
- Create: `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs`
- Create: `tests/Application.UnitTests/Architecture/IdentityAccessArchitectureTests.cs`
- Modify: `docs/features/identity-access/TRACEABILITY.md`
- Modify: `docs/features/identity-access/TASKS.md`

- [x] **Step 1: RED - executable journeys**

Cover pending registration -> confirmation activation -> sign-in; zero/one/multiple membership defaults; authenticated second Organization; switching; independent IP/account throttles and lockout recovery; new invitee token-aware registration -> confirmation -> sign-in -> one-shot accept; existing identity acceptance; tenant isolation; revoked session; baseline-to-latest restart; and runtime/OpenAPI/React contract-drift rejection.

Run: `dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --filter IdentityAccess`  
Expected: runtime scenario assertion failures until browser wiring is complete.

- [x] **Step 2: GREEN - architecture and full verification**

Architecture tests enforce dependencies, centralized permissions, exactly one of `IPublicRequest`/`[Authorize]`, no early `UserSession`/Invitation coupling, no endpoint EF access, and no legacy API routes.

```powershell
dotnet build CleanArchitecture.slnx -v minimal
dotnet test CleanArchitecture.slnx --no-build
npm test --prefix src/Web/ClientApp
npm run lint --prefix src/Web/ClientApp
npm run build --prefix src/Web/ClientApp
dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --no-build
```

Expected: every command exits 0.

- [x] **Step 3: REFACTOR traceability and commit**

Record exact pre-Platform evidence without transferring normative ownership to IA-009; move only IA-002..IA-008 to `Review`. Keep IA-009 `Blocked`: its final acceptance and any move to `Review` wait for Tasks 14–16 and verified Platform evidence.

```bash
git add tests docs/features/identity-access
git commit -m "test: verify identity access foundation"
```

**Resolved — the SPA could not mutate anything.** Nine of the ten journeys failed, and not for one reason.
The first was the harness: sign-in clicked and moved straight on, so the next navigation cancelled the
request before it was answered and the trace showed no `POST /api/identity/sessions` at all. Waiting for
that answer exposed the real fault — `400 antiforgery_validation_failed`, and a pair minted and used inside
the same page was refused too, so it was not staleness. The server's own reason was the scheme: the browser
reached the dev server over HTTP while the proxy reached the API over TLS, and exact-origin validation
compares the browser's Origin against the scheme the request arrived on. The design does not work over HTTP
in any case, since an insecure origin cannot store a `__Host-` cookie, so the dev server now presents the
ASP.NET development certificate. Behind that sat three more, each with its own cause: a seeded CUIT carrying
separators the eleven-digit column cannot hold, organizations named by a slug unrelated to their name when
the slug is what a tenant is known by, and a permission catalogue written as the application starts that a
fixture could outrun. Opening an invitation link twice changed only the fragment, which leaves the page
mounted, so the second acceptance was asserting against the first attempt's screen.

Two scenarios were added for what the plan listed and the ten did not cover: an authenticated identity
registering a second organization, and a lockout lifting once passed. Baseline-to-latest is proved by
`MigrationUpgradeTests` rather than through the browser, and is recorded there. `MigrationUpgradeTests`
itself held a flake that only the full-solution run surfaced: it asserted a one-microsecond gap that
PostgreSQL can round away, which is now truncated to whole microseconds.

## Task 14: Persist Platform invitations before MFA and model MFA invariants

**Requirements:** IA-REQ-039, IA-REQ-041; IA-REQ-042 onboarding foundation
**Tracking:** IA-012 and IA-014 foundation

**Files:**

- Modify: `src/Domain/IdentityAccess/Tenants/Tenant.cs`
- Modify: `src/Domain/IdentityAccess/Tenants/TenantType.cs`
- Modify: `src/Domain/IdentityAccess/Tenants/TenantStatus.cs`
- Create: `src/Domain/IdentityAccess/Platform/PlatformAdminInvitation.cs`
- Create: `src/Domain/IdentityAccess/Platform/PlatformAdminInvitationStatus.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/PlatformAdminInvitationConfiguration.cs`
- Create: `src/Infrastructure/Data/Migrations/<timestamp>_PlatformAdminInvitation.cs`
- Create: `src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInvitee.cs`
- Create: `src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInviteeValidator.cs`
- Create: `src/Application/IdentityAccess/Platform/Invitations/ConfirmPlatformInvitee.cs`
- Create: `src/Application/IdentityAccess/Platform/Invitations/ConfirmPlatformInviteeValidator.cs`
- Create: `src/Domain/IdentityAccess/Platform/PlatformMfaEnrollment.cs`
- Create: `src/Domain/IdentityAccess/Platform/PlatformMfaEnrollmentStatus.cs`
- Create: `src/Domain/IdentityAccess/Platform/PlatformRecoveryCode.cs`
- Create: `src/Application/IdentityAccess/Platform/Mfa/BeginPlatformMfaEnrollment.cs`
- Create: `src/Application/IdentityAccess/Platform/Mfa/VerifyPlatformMfaEnrollment.cs`
- Create: `src/Application/IdentityAccess/Platform/Mfa/AcknowledgePlatformRecoveryCodes.cs`
- Create: `src/Application/IdentityAccess/Platform/Mfa/StepUpPlatformMfa.cs`
- Create: `src/Application/IdentityAccess/Platform/IPlatformMfaVerifier.cs`
- Create: `src/Application/IdentityAccess/Platform/IRecentMfaVerifier.cs`
- Create: `src/Infrastructure/Security/PlatformTotpSecretProtector.cs`
- Create: `src/Infrastructure/Security/PlatformRecoveryCodeHasher.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/PlatformMfaEnrollmentConfiguration.cs`
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/PlatformRecoveryCodeConfiguration.cs`
- Create: `src/Infrastructure/Data/Migrations/<timestamp>_PlatformMfa.cs`
- Modify: `src/Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `src/Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/Infrastructure/Data/ApplicationDbContext.cs`
- Modify: `src/Infrastructure/DependencyInjection.cs`
- Modify: `src/Web/DependencyInjection.cs`
- Create: `src/Web/Endpoints/Platform/PlatformMfaEndpoints.cs`
- Create: `src/Web/Endpoints/Platform/PlatformInvitationEndpoints.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformInvitationRegistrationRequest.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformInvitationConfirmationRequest.cs`
- Create: `tests/Domain.UnitTests/IdentityAccess/PlatformMfaTests.cs`
- Create: `tests/Infrastructure.IntegrationTests/IdentityAccess/PlatformMfaMappingTests.cs`
- Create: `tests/Application.UnitTests/Architecture/PlatformMfaShapeTests.cs`
- Create: `tests/Application.UnitTests/Architecture/PlatformInvitationApplicationShapeTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformInvitationOnboardingTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformMfaTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformMfaAuthenticationTests.cs`

- [x] **Step 1: Platform invitation persistence RED, migration, then GREEN**

Extend `PlatformMfaShapeTests` and `PlatformMfaMappingTests` to first require `PlatformAdminInvitation`, status, hash/expiry/delivery state, DbSet/configuration, and its migration. Run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformMfaShapeTests` and `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter PlatformMfaMappingTests`; expect runtime missing-type/member RED. Add only invitation shells, run `dotnet ef migrations add PlatformAdminInvitation --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations`, then run `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "PlatformMfaMappingTests|MigrationUpgradeTests"`; expect PASS with empty/latest and baseline/latest upgrades before any MFA RED.

- [x] **Step 2: Platform invitation onboarding RED, then GREEN**

After Step 1 PASS, make `PlatformInvitationApplicationShapeTests` require `RegisterPlatformInvitee`/validator, `ConfirmPlatformInvitee`/validator, `POST /api/platform/invitations/register`, and `/confirm`; its registration DTO carries invitation token plus password. Run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformInvitationApplicationShapeTests` and expect route/type RED. The public token-aware registration request validates the submitted password against `PasswordOptions` only when it creates a missing matching identity; it uses Identity hashing, atomically binds `PlatformAdminInvitation`, and creates confirmation outbox/secret. For an existing matching identity, it ignores supplied credentials and returns only generic sign-in/confirmation behavior. Confirmation consumes its email and invitation tokens, marks the identity confirmed, and still creates no Platform membership. Run `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformInvitationOnboardingTests|ProblemDetailsContractTests|OpenApiContractTests"`; expect behavioral RED for password creation, existing-identity non-takeover, neutral `202`/`204`, outbox, and no early activation. Implement by generalizing existing credential/confirmation/outbox mechanics without treating Organization `Invitation` as `PlatformAdminInvitation` or generating a default password; rerun both commands and expect PASS.

- [x] **Step 3: MFA RED, migration, then GREEN after confirmed onboarding**

Only after Step 2 PASS, reflect for Platform tenant type, one-enrollment-per-user, recovery-code/encrypted-secret members, and no global administrator member; run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformMfaShapeTests` and `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformMfaTests|PlatformMfaAuthenticationTests"`; expect RED. MFA tests require a registered, confirmed, signed-in matching invitee and bound token, reject theft/mismatch/anonymous/token-only/invalid-session/pre-activation access, and prove no membership activation before TOTP, recovery acknowledgement, and MFA session. Persist encrypted TOTP/hashed codes, run `dotnet ef migrations add PlatformMfa --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations`, then run `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "PlatformMfaMappingTests|MigrationUpgradeTests"` and the two prior commands; expect PASS with no plaintext secret and ordered `PlatformAdminInvitation` then `PlatformMfa` migrations.

- [x] **Step 4: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: add platform MFA foundation"
```

## Task 15: Bootstrap and operate Platform through normal authority

**Requirements:** IA-REQ-039..046; applies IA-REQ-038
**Tracking:** IA-014

**Files:**

- Create: `src/Application/IdentityAccess/Platform/Bootstrap/BootstrapPlatformOwner.cs`
- Create: `src/Application/IdentityAccess/Platform/Bootstrap/IPlatformBootstrapOptions.cs`
- Create: `src/Application/IdentityAccess/Platform/Bootstrap/RecoverPendingPlatformOwnerInvitation.cs`
- Create: `src/Application/IdentityAccess/Platform/Bootstrap/RecoverPendingPlatformOwnerInvitationValidator.cs`
- Create: `src/Application/IdentityAccess/Platform/Bootstrap/IPlatformBootstrapRecoveryRateLimiter.cs`
- Create: `src/Application/IdentityAccess/Platform/Administrators/InvitePlatformAdministrator.cs`
- Create: `src/Application/IdentityAccess/Platform/Administrators/RevokePlatformAdministrator.cs`
- Create: `src/Application/IdentityAccess/Platform/Organizations/SuspendOrganizationTenant.cs`
- Create: `src/Application/IdentityAccess/Platform/Organizations/ReactivateOrganizationTenant.cs`
- Create: `src/Application/IdentityAccess/Platform/Queries/ListPlatformOrganizations.cs`
- Create: `src/Application/IdentityAccess/Platform/Queries/ListPlatformIdentities.cs`
- Create: `src/Application/IdentityAccess/Platform/Queries/ListPlatformAdministrators.cs`
- Create: `src/Application/IdentityAccess/Platform/Queries/ListPlatformAudit.cs`
- Create: `src/Application/IdentityAccess/Platform/Queries/PlatformDirectoryQuery.cs`
- Create: `src/Application/IdentityAccess/Platform/Queries/PlatformDirectoryPage.cs`
- Create: `src/Application/IdentityAccess/Platform/Queries/PlatformAdministratorProjection.cs`
- Create: `src/Application/IdentityAccess/Platform/IPlatformOperationalProjectionReader.cs`
- Create: `src/Infrastructure/Platform/ConfiguredPlatformBootstrapper.cs`
- Create: `src/Infrastructure/Platform/PlatformBootstrapRecoveryRateLimiter.cs`
- Create: `src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs`
- Create: `src/Web/HostedServices/PlatformBootstrapHostedService.cs`
- Create: `src/Web/Endpoints/Platform/PlatformEndpoints.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformOrganizationResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformIdentityResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformAdministratorResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformAuditEventResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformOrganizationDirectoryResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformIdentityDirectoryResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformAdministratorDirectoryResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformAuditDirectoryResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformMfaEnrollmentResponse.cs`
- Create: `src/Web/Endpoints/Platform/Contracts/PlatformTenantLifecycleRequest.cs`
- Modify: `src/Application/IdentityAccess/Authorization/Permissions.cs`
- Modify: `src/Infrastructure/DependencyInjection.cs`
- Modify: `src/Web/DependencyInjection.cs`
- Modify: `src/Web/Program.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`
- Create: `tests/Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformBootstrapTests.cs`
- Create: `tests/Application.UnitTests/IdentityAccess/Platform/RecoverPendingPlatformOwnerInvitationValidatorTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformBootstrapRecoveryTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformAdministrationTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformOperationsTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformProjectionTests.cs`
- Create: `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformDirectoryContractTests.cs`

- [x] **Step 1: Compile-safe shape RED**

Reflect for the bootstrap/options, `RecoverPendingPlatformOwnerInvitation` handler/validator/rate-limit port, protected `platform.*` requests, exact Platform routes including `POST /api/platform/bootstrap/recover` and `GET /api/platform/admins`, typed `PlatformDirectoryQuery`/`PlatformDirectoryPage`/`PlatformAdministratorProjection`, and allowlisted DTOs. Run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformApplicationShapeTests`; expect runtime missing-type/route/classification RED. Assert active-Platform requests are `[Authorize]` with Platform tenant required; recovery is bodyless `IPublicRequest` with antiforgery/rate-limit/server-derived recipient only, no identity/email/token input, activation, or elevation; and no endpoint is named impersonate/delete/context-bypass.

- [x] **Step 2: Behavioral RED**

With real PostgreSQL, run bootstrap twice with one configured email: only the first missing-Platform invocation atomically creates the singleton tenant, system owner role, pending invitation, outbox message/secret, and audit event. `POST /api/platform/bootstrap/recover` dispatches `RecoverPendingPlatformOwnerInvitation` as a bodyless same-origin antiforgery request with no identity, email, token, or replacement-recipient input. Its validator permits only expired or permanently delivery-failed pending invitations and derives the unchanged configured/pending recipient; its rate-limit port keys the pending invitation and transport source. The handler conditionally claims that invitation in one transaction, invalidates the old token, writes one replacement token/outbox/audit effect, and makes equivalent sequential/concurrent requests idempotent without membership activation/elevation. Test cold-start with no `ApplicationUser`, active/used invitation, unknown/ineligible opaque state, configuration change, and race branches: each valid state-obscuring outcome is neutral `202`, and only one current invitation/effect set survives. Test malformed/missing antiforgery separately as RFC 9457 `400` code `antiforgery_validation_failed`; test exhausted recovery limit separately as RFC 9457 `429` code `rate_limit_exceeded` plus `Retry-After`. Missing configuration creates nothing; completed first activation permanently closes recovery.

Test normal active-Platform membership plus distinct `platform.admins.read`/`platform.admins.manage`, `platform.tenants.manage`, `platform.organizations.read`, `platform.identities.read`, and `platform.audit.read`; reject stale/non-MFA/unauthorized/cross-context requests. The already-persisted `PlatformAdminInvitation` reuses the Invitation/Outbox token and delivery pattern without widening Organization invitations; invitation/revocation preserves one active owner. Two status writers prove a required allowlisted suspension reason and conditional concurrency: first Organization suspension persists; stale second returns typed `Conflict` and RFC 9457 `409` with `platform_tenant_concurrency_conflict`/`traceId`; Platform cannot be suspended/deleted. The evaluator immediately denies the suspended Organization.

`PlatformProjectionTests` proves exact fields: organization IDs/slug/type/status/timestamps/suspension metadata/version; identity/admin IDs, normalized email only with directory permission, confirmation/account/membership/MFA/owner status, and operational timestamps; audit IDs/type/time/correlation/actor-tenant IDs/outcome/reason-code only. It rejects CUIT, business rows, profile payloads, credentials, secrets, tokens, extra PII, and mutable audit payloads. `PlatformDirectoryContractTests` proves `/api/platform/organizations`, `/identities`, `/admins`, and `/audit` accept only `limit=1..100` plus opaque `cursor` and return each endpoint's typed `items`/`nextCursor` DTO; `/api/identity/*` remains unchanged. `PlatformAdministrationTests` lists administrators through the protected directory before inviting or revoking and uses only the allowlisted membership ID; it still blocks last-owner removal. Negative tests reject impersonation, destructive deletion, direct arbitrary elevation, global claims/booleans/roles, private tenant rows, credentials, secrets, raw tokens, and client-selected context. Contract tests require semantic DTOs/`202`/`204`, stable Problem Details codes, and no Result or universal envelope in runtime/OpenAPI.

Run `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformBootstrapTests|PlatformBootstrapRecoveryTests|PlatformAdministrationTests|PlatformOperationsTests|PlatformProjectionTests|PlatformDirectoryContractTests|ProblemDetailsContractTests|OpenApiContractTests"`; expect behavioral/contract RED before handlers/endpoints. Implement the normal authority flow, then rerun that command and `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformApplicationShapeTests`; expect PASS.

- [x] **Step 3: GREEN, REFACTOR, commit**

Reuse only confirmation/outbox mechanics where sound; `PlatformAdminInvitation` remains separate from Organization `Invitation`. Bootstrap uses deployment configuration only as a one-time email selector and never as authority after creation. Register no default password or admin. Run `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter MigrationUpgradeTests` and the prior functional/shape commands; expect PASS before commit.

```bash
git add src tests
git commit -m "feat: add controlled platform operations"
```

## Task 16: Deliver the MFA-bound Platform panel and acceptance evidence

**Requirements:** IA-REQ-044..046; IA-009 evidence applies IA-REQ-038
**Tracking:** IA-014 and IA-009

**Files:**

- Create: `src/Web/ClientApp/src/features/platform/api/platformClient.js`
- Create: `src/Web/ClientApp/src/features/platform/PlatformPanel.jsx`
- Create: `src/Web/ClientApp/src/features/platform/PlatformPanel.test.jsx`
- Create: `src/Web/ClientApp/src/features/platform/platformClient.test.js`
- Create: `src/Web/ClientApp/src/features/platform/platformDirectory.test.js`
- Create: `src/Web/ClientApp/src/features/platform/invitations/PlatformInvitationPages.jsx`
- Create: `src/Web/ClientApp/src/features/platform/invitations/PlatformInvitationPages.test.jsx`
- Modify: `src/Web/ClientApp/src/AppRoutes.jsx`
- Modify: `src/Web/ClientApp/src/components/NavMenu.jsx`
- Modify: `src/Web/ClientApp/src/features/identity/context/IdentityProvider.jsx`
- Create: `tests/Web.AcceptanceTests/Features/PlatformOperations.feature`
- Create: `tests/Web.AcceptanceTests/Pages/PlatformOperationsPage.cs`
- Create: `tests/Web.AcceptanceTests/StepDefinitions/PlatformOperationsStepDefinitions.cs`
- Modify: `tests/Application.UnitTests/Architecture/IdentityAccessArchitectureTests.cs`

- [x] **Step 1: Shape RED, then shells**

Filesystem/route shape tests require the Platform client, token-aware public invitation pages, panel, safe DTO parser, and protected route. Run `npm test --prefix src/Web/ClientApp -- PlatformPanel.test.jsx platformClient.test.js platformDirectory.test.js PlatformInvitationPages.test.jsx`; expect missing-module/route RED. Shells must not expose an impersonation control, delete action, unrestricted identity fields, or client tenant override.

- [x] **Step 2: Behavioral RED**

`platformClient.test.js`, `platformDirectory.test.js`, and `PlatformInvitationPages.test.jsx` prove public Platform-token registration/confirmation calls use antiforgery and neutral responses; a missing identity submits a PasswordOptions-valid password, while an existing identity's supplied credentials are ignored and cannot take over the account. After confirmation they hand off to normal password sign-in and cannot activate membership. They also assert recovery `400 antiforgery_validation_failed` and `429 rate_limit_exceeded`/`Retry-After`, while valid opaque recovery outcomes are `202`. The tests prove the client calls the protected typed administrator directory before invite/revoke, carries only the selected allowlisted membership ID into mutations, and follows only typed bounded `items`/`nextCursor` directories. MSW/React tests prove only a recent-MFA Platform context sees the panel; it renders allowlisted organization/identity/administrator/audit projections, handles typed `401/403/404/409/429` Problem Details, requires confirmation for suspension/revocation, and cannot render private data or bypass controls. Run the Step 1 command; expect behavioral RED. Browser journeys prove cold-start bootstrap → expired/failed delivery recovery with no identity → Platform token registration with submitted password/reuse without credential effect → confirmation → normal password sign-in → invitation-bound TOTP/recovery acknowledgement → activation → MFA step-up → admin listing/invitation → suspend/reactivate → audit; the later admin follows the same register/confirm/sign-in/MFA gates. Last-owner revocation and prohibited routes/actions fail.

- [x] **Step 3: GREEN, REFACTOR, commit**

Only after Tasks 14 and 15 have PASS evidence for Platform invitation persistence/onboarding, MFA, bootstrap recovery, ordered migrations, functional/PostgreSQL, and OpenAPI contracts, route all calls through the existing typed API boundary with same-origin credentials and antiforgery. Run `npm test --prefix src/Web/ClientApp -- PlatformPanel.test.jsx platformClient.test.js platformDirectory.test.js PlatformInvitationPages.test.jsx`, `npm run lint --prefix src/Web/ClientApp`, and `npm run build --prefix src/Web/ClientApp`; expect PASS. Then run `dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --filter PlatformOperations` and `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter IdentityAccessArchitectureTests`; expect PASS. Record traceability without changing normative ownership and only then move IA-009 from `Blocked` to `Review`.

```bash
git add src/Web/ClientApp tests docs/features/identity-access
git commit -m "test: verify platform operations"
```

## First-increment Exit Criteria (Tasks 1–16)

These are the historical first-increment criteria, not a claim that all are closed or the exit gate for Tasks 17–28. The continuation's distinct closure criteria are in Task 28.

- `BaselinePostgreSql`, `IdentityAccess`, and incremental migrations pass empty-to-latest and baseline-to-latest tests with sentinel preservation and no pending migration.
- No default administrator, startup deletion, stale string Identity key/audit actor, global bypass, Platform impersonation/delete capability, or pre-IA-007 `UserSession`/pre-IA-008 Invitation behavior remains.
- OrganizationProfile/CUIT, UUIDs, concurrency, explicit deletes, uniqueness, and composite tenant FKs are proven by PostgreSQL.
- Every Application request is exactly `IPublicRequest` or `[Authorize]`; IA-REQ-030 remains IA-005-only.
- Expected failures use typed Result internally and unexpected failures use exceptions. Result/universal envelopes never cross HTTP. Runtime, OpenAPI, and React agree on endpoint DTOs, semantic `200`/`201 + Location`/neutral `202`/bodyless `204`, RFC 9457 `400/401/403/404/409/429/500`, stable codes, trace IDs, validation errors, required headers, and safe diagnostics.
- Audit is append-only at EF and PostgreSQL boundaries, correlation-bearing, allowlisted, secret-free, and covers every IA-005..IA-008 event; denial audit survives rejected business transactions.
- Registration branches and confirmation transitions are atomic; login auto-selects only one active membership.
- Password/lockout values and independent IP/account transport limits are deterministic and tested with `Retry-After`.
- Active tenant comes only from validated, revocable `UserSession`; exact routes and CSRF names match SPEC.
- Outbox transient retries are due-time/CAS/idempotency safe. Delivered, expired, and permanently failed secret rows retain non-secret evidence with ciphertext cleared; evidence rows are never deleted.
- Root React composition makes every journey reachable, uses the same-origin proxy and MSW tests, and retains no reusable/raw token.
- Platform is a singleton active-tenant membership with encrypted TOTP, hashed recovery codes, recent step-up, last-owner protection, conditional Organization lifecycle, allowlisted projections, and append-only audit evidence.
- Deferred roadmap items remain IA-010, IA-011, IA-013, and IA-015 and are not presented as implemented.

## Continuation to local B2B/B2C functional completion

**Outcome:** finish a finite, usable local product: one identity can create its Personal context, create or join Organizations, manage its credentials and sessions, and administer permitted roles/members through React. Lifecycle and operating controls must have real implementation and synthetic evidence. Real PII, public deployment, and complete external-standard compliance have separate gates; a local demonstration cannot satisfy them.

**Authority:** [SPEC §2.3](../../features/identity-access/SPEC.md#23-required-roadmap-outside-the-first-increment) already includes Personal/DNI, recovery/change, Google, custom roles/full membership administration, lifecycle, retention, and operations. It does not settle every continuation contract. [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md) and SPEC still have proposed approval status. Authorization to write this plan is not acceptance of the proposals below. Task 17 is a bounded documentation/approval deliverable; dependent tasks become `Ready` only after their listed contracts are accepted. There is no automatic SDD/review lifecycle or new worktree implied by this plan.

**Reference pin:** repository `RepositorioBaseNet`, revision `052a39873ed74a3c66c502c521a2469dfbeb523d`, `docs/standards/identity-access/02-business-rules.md` and `05-authentication.md`. The reference files were clean at that revision. This is a source identifier, not a dependency on one developer's absolute filesystem path. Its mandatory recovery guarantees must be mapped explicitly; the local adoption of business semantics does not silently adopt its Azure/Entra/WORM implementation or waive those guarantees.

### Continuation map

| Task | Visible result | Tracking | Prerequisites |
|---|---|---|---|
| 17 | Approved local contracts and explicit reference-adoption boundary | IA-001, IA-010/011/013/015; existing normative owners retained | This plan and current SPEC/ADR |
| 18 | Registration cannot disclose an address through durable CUIT side effects | IA-006; IA-009 evidence | 17: C1 |
| 19 | Atomic Personal ownership and protected, unique AR/DNI persistence, and the shared attempt-budget store the first public Personal route will need | IA-010; IA-004 persistence | **Done 2026-09-06.** 17: C3 and C7; 18 registration seam |
| 20 | Personal signup, own profile, and mixed Personal/Organization context journey | IA-010; IA-009 evidence | **Done 2026-09-06.** 19, which carries the C3/C7 approval this task inherits and adds nothing to |
| 21 | Own-device session list/revocation and reusable recent reauthentication | IA-011; IA-007 sessions | 17: C2/C4, both accepted 2026-09-06 |
| 22 | Delivered password recovery and authenticated password change | IA-011; IA-009 evidence | 21; 17: C4 |
| 23 | Google login, explicit linking, and safe last-authenticator handling | IA-013; IA-009 evidence | 20–22; 17: C4 |
| 24 | Custom Organization roles through the existing permission evaluator | IA-005 continuation; IA-009 evidence | 17: C5 |
| 25 | Full Organization membership/ownership/invitation administration | IA-005/008 continuation; IA-009 evidence | 21, 24; 17: C5 |
| 26 | Bounded lifecycle, MFA recovery, retention execution, restore admission guard, and both halves of the documentary dispute | IA-011/012/015; existing event owners | 19, 21–25; 17: C6/C7, plus C3/C4 for the dispute, whose owner half needs C4's proof |
| 27 | The remaining budget scopes moved onto the shared store, deployment safeguards, and operational evidence | IA-015; IA-007/012/014 control owners | 26; 17: C6/C7; the budget port and adapter already exist from 19 |
| 28 | Fixed full-journey acceptance scope and an honest closure record | IA-009 evidence; all continuation owners | 18–27 for local closure; separate real-PII/deployment gates |

IA-010, IA-011, IA-013, and IA-015 remain `Proposed` and unimplemented. Expanding IA-005/008 does not rewrite the historical `Review` status of their first-increment work. A dependency below is an implementation dependency, not an assertion that a fresh approval or test exists.

### Shared execution contract for Tasks 18–28

- Work in the existing Clean Architecture boundaries: Domain invariants, Application use cases/ports, Infrastructure Identity/EF/cryptography adapters, Web HTTP/protocol adaptation, and React presentation. Reuse the existing transaction, permission evaluator, audit, outbox, and single frontend transport. Do not create a second B2C identity store or authorize from a role name.
- Preserve the existing role-access architecture guard: do not add bulk `TenantRoles`, `RolePermissions`, or `MembershipRoles` query/mutation surfaces to `IApplicationDbContext`. Extend narrow Application ports analogous to `IOfferableRoleReader`/`IInvitationRoleAssigner`; their Infrastructure adapters own EF access and participate in the existing transaction.
- `Modify` paths below exist at the stated source revision. `Create` paths are proposed files, not existing implementation or coverage. Before implementation, reconcile paths with the then-current tree. Every named new suite must actually discover tests; a zero-test exit is not GREEN.
- Apply the inherited compile-safe protocol only to missing types/modules: a concrete, non-vacuous file/type/route assertion must fail at runtime, minimal shells make it compile/import, and behavioral RED must then demonstrate the absent behavior. Existing seams need behavioral RED directly. Missing imports, global test counts, empty-write rollback, and reflection that asserts nothing cannot prove a requirement.
- Each task runs the commands it names once before behavior and again after implementation/refactoring. Record the failing assertion and passing result, real database effects, migration name when applicable, and remaining limitations. Do not widen testing or reopen review repeatedly once the scoped criteria pass; investigate an actual failure before a bounded retry.
- For every new Application request, update `tests/Application.UnitTests/Architecture/RequestAuthorizationMetadataTests.cs` and `ApplicationRequestInventory.cs` as applicable. Run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "RequestAuthorizationMetadataTests|IdentityAccessArchitectureTests"`; expected: every request has exactly one classification and inward dependencies remain intact.
- For each new HTTP contract, modify `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs` and `ProblemDetailsContractTests.cs`; run their combined filter on `Application.FunctionalTests`. Require endpoint DTO/status/header agreement, safe RFC 9457 errors, and no serialized internal Result. Mutation tests include exact origin/antiforgery; the explicitly approved OIDC callback is handled separately in Task 23.
- Protected tenant mutations use only the validated session's active tenant; route IDs must match it and queries filter tenant plus resource. Identity self-service uses the authenticated identity/session and `RequiresTenant=false`, never a caller-supplied owner. Include anonymous, missing permission, another identity/tenant, suspended/revoked state, stale version, and concurrent/replayed requests as applicable.
- Changes to persistence use additive migrations plus their generated designer and `src/Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`; never rewrite `BaselinePostgreSql` or previous migrations. `tests/Infrastructure.IntegrationTests/IdentityAccess/MigrationUpgradeTests.cs` covers empty-to-latest and upgrade from the previous deployed schema with meaningful preexisting rows, explicit FKs/deletes, and rollback behavior. Generated timestamps are chosen at execution, not fabricated as existing paths here.
- Reuse `tests/Application.FunctionalTests/Infrastructure/IdentityHttpHarness.cs`, the session/concurrency fixtures, `src/Web/ClientApp/src/test/identityServer.js`, `tests/Web.AcceptanceTests/AspireSetup.cs`, and delivered-mail helpers including `PlatformFixtures.DeliveredAsync`. Real PostgreSQL and actual HTTP/BFF cookies provide security evidence; MSW provides UI behavior, not database/protocol proof.
- Execution prerequisites: .NET 10, the Node version required by the current package lock (Node 20+ baseline), Docker for PostgreSQL/Aspire, trusted local HTTPS, and an isolated test mail sink. Google uses a controlled OIDC provider in CI. No real email, real DNI, or external-account setup is implicit. Build may regenerate the ignored API client; inspect any resulting changes before delivery.
- Record scoped results in TRACEABILITY and TASKS only after execution. `git diff --check` is the structural check for this planning change; no application tests were run to author this continuation. Commits/push/deployment require their own existing user authority and are not checklist shortcuts.

## Task 17: Approve the continuation contracts and reference scope

**State (2026-09-06): partly taken, not complete.** The package was written into SPEC §14 and ADR-004. C1, C2, C3, C4 and C7 have been accepted and left the proposal set, all for synthetic data only. C5 and C6 were not. Amendments A1–A5 were folded into C3, C4, C6 and C7 on 2026-09-06, along with the two contradictions C2/C4 and C5/C6 carried, so every entry states one contract. This task closes when the two remaining entries are answered.

**Visible outcome:** one reviewable contract package makes the following implementation steps executable without repeated product-discovery rounds. This task is documentation and human approval, with no production code.

**Tracking/source:** IA-001 and roadmap IA-010/011/013/015; SPEC §§2.1–2.3, 4, 11–13; ADR-004; the pinned reference rules below. Existing IA-REQ ownership remains unchanged; new normative IDs, if needed, are assigned only in the approved SPEC change.

**Files:** Modify `docs/features/identity-access/SPEC.md`, `docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md`, `docs/features/identity-access/TRACEABILITY.md`, `docs/features/identity-access/TASKS.md`, and this plan. Those edits belong to Task 17 execution, not this plan-authoring change.

**Inherited rules, not new questions:** one global email identity; no DNI on `ApplicationUser`; AR/DNI required when creating Personal only; a protected global documentary identity is unique among unpurged rows; at most one Personal per identity, one owner, no Personal invitations; no automatic document/email merge; Organization administration retains an effective administrator; Google never auto-links by matching email. Sources: BR-ID-001..007, BR-REG-001..005, BR-TEN-001..007, BR-AUT-001..008, BR-INV-003; local IA-REQ-001/002/006..017/047. Confirm these in the local contract rather than asking whether to remove them.

### Proposed decision register — no entry is accepted by this document

| ID | Recommendation to approve or amend | Exact dependent work |
|---|---|---|
| C1 — registration reservation | Require confirmed identity before any **exclusive durable CUIT or documentary reservation**. Anonymous initiation can retain a bounded, expiring protected registration intent and send a neutral confirmation, but reserves no exclusive CUIT/profile/document/tenant/membership/role. After proof, atomically create the tenant/profile/document when applicable, responsible membership, roles, audit, and outbox effects. Preserve idempotent replay and honest post-proof conflict handling. This changes IA-REQ-003/004 and the reference's pre-confirmation aggregate timing; record the explicit adoption deviation and upgrade behavior for existing pending registrations. | 18; registration portions of 19/20/23 |
| C2 — coexistence and revocation | Replace today's revoke-all-on-login behavior with at most **five active sessions per identity**, serialized at that identity; on a sixth sign-in revoke the oldest by creation time then ID. Keep 30-minute idle/12-hour absolute defaults and no remember-me. Reset revokes every persisted session. Authenticated password change revokes other sessions and rotates the current session; it never preserves its old identifier/proofs. These numbers are proposed product defaults, not a legal or external-standard claim. | 21/22/23 and their concurrency tests |
| C3 — own profile/document editing | Initial self-service fields: full name and display name; country/type remain AR/DNI for this slice. Store the recoverable number encrypted and compare keyed/versioned fingerprints. Ordinary profile edit may change names, never email, ownership, country/type, or document. Document correction requires a separately authorized verified process; until that process exists it is unavailable, with clear UI guidance. Expose only masked document status to its owner; Organization/Platform operators cannot edit or read the personal document. | 19/20; data contracts in 26/27 |
| C4 — credential and provider proof | Use a single-use proof bound to current identity, session, action, and security version, with a proposed five-minute lifetime; password or a fresh linked OIDC challenge supplies primary proof, never a session cookie alone. Proposed password-reset token lifetime: 30 minutes, superseded on reissue. Link requires explicit consent plus recent primary proof and verified provider email; Login and Link purposes cannot cross. Unlink requires recent proof and leaves at least one usable authenticator; adding a password to Google-only identity uses verified recovery, never a fictitious current password. StartLogin/StartLink are same-origin antiforgery mutations; the narrowly scoped server callback instead requires framework-validated state, nonce, correlation, PKCE, issuer/audience/signature/expiry. Approve its callback-only exception to the initial exact-origin/token-URL rule, with code/token log redaction and a clean final redirect. | 21/22/23; recovery portions of 26 |
| C5 — delegated administration | Deliver custom roles and full membership administration for **Organization** in this continuation. Personal's owner/system role stays protected; Platform still uses its existing invitation/MFA authority. An actor may delegate only permissions they currently hold and that the tenant type allows. Retiring a role withdraws its effective grants and refuses any change that would remove the last effective administrator. Ownership transfer requires a confirmed active same-tenant recipient, the current owner, and recent proof; it is atomic. Widening a role atomically cancels every pending offer referencing it and terminalizes its token envelope; an authorized inviter must explicitly review/reissue an offer, never automatically grant wider authority. Record the resulting change to IA-REQ-047's deferred-control note. | 24/25 and affected invitation acceptance |
| C6 — finite lifecycle/restore/operations scope | Adopt identity disable/reactivation, membership lifecycle, Platform factor recovery with fresh primary proof plus an unused recovery code, bounded expired-secret/session cleanup, and a fail-closed restore guard. Restore runs privately with public ingress and delivery stopped; restored sessions/one-use tokens cannot authorize or send, restored authenticators/grants require revalidation, and erased PII cannot silently return. Default when evidence is insufficient: quarantine and keep admission closed, including after restart. Pin an operator-controlled recovery admission record outside the restored database and its verification authority before code; a self-declared row inside the backup is insufficient. Map the reference's external RecoveryEpoch/ledger, immutable recovery evidence, and proofing guarantees explicitly; its specific Azure/Entra/WORM stack is **unadopted**, not waived or optional compliance. Live restore release/full-reference compliance remain blocked until that mapping and required external authority are accepted and proved. | 26/27; production/full-reference gate in 28 |
| C7 — PII and retention | Enable Personal only with synthetic fixtures until the responsible human/legal owner approves actual PII purpose, field scope, access, retention periods, legal holds, deletion evidence, backup/restore treatment, and documentary-uniqueness behavior after purge. Do not invent a jurisdictional period. Implement/test a configurable retention contract with synthetic time and an approved default of **no destructive real-data action when policy is missing**. Select the existing PostgreSQL stack for shared limit state as the proposed portable default; deployment storage, key/certificate ownership, numerical abuse budgets, and external recovery authority must be recorded per environment. Redis/Azure are not implied dependencies. | Synthetic design approval: 19/26/27; actual PII and deployment: separate gate in 28 |

- [ ] **Step 1 — draft the exact contract delta.** For C1–C7, add caller/state tables, allowed DTOs, endpoint methods/routes/statuses/codes, permission/public markers, invariants, transitions, concurrency winners, audit/outbox effects, and failure examples to SPEC/ADR. Reuse existing permission constants where accurate; explicitly propose new self-profile/credential/ownership/lifecycle codes and their scope. Record accepted numerical settings and environment prerequisites, not unspecified configurable behavior.
  C6 must explicitly name the reactivation actor, fresh proof available before ordinary session issuance, and the distinction between self-deactivation and an administrative suspension. Requiring an ordinary authenticated session from an identity whose password, Google and cookies are all denied is not an executable reactivation path; Task 26 remains conditional until that authority path is accepted.
- [ ] **Step 2 — map reference adoption.** Pin the revision above and a table of source rule/section → local adopted contract → task → planned evidence. Name current endpoint-combination/registration-timing and recovery-architecture differences explicitly. Separate inherited semantic guarantees, proposed portable implementations, and unapproved external dependencies; do not relabel a mandatory source guarantee as optional.
- [ ] **Step 3 — structural review, then one human decision.** Run `git diff --check` and read the contract/state tables against Tasks 18–28. Expected: all seven entries have a concrete recommendation and exact consumers; no proposed rule masquerades as coverage. Present the complete package for human approval or amendment. Record the decision, date, accepted fields and remaining external gates; do not mark SPEC/ADR accepted automatically.
- [ ] **Done:** approved local contracts make their dependent synthetic implementation tasks `Ready`; real-PII/production prerequisites remain explicit blockers where unresolved. A declined or materially changed entry blocks only its consumers. No recurring approval per unchanged task, new discovery phase, application test, or production mutation is required by this documentation task.

## Task 18: Remove the registration state oracle, not only its status difference

**Done 2026-09-06** on the accepted C1 (IA-REQ-048) alone. Evidence, exact commands and counts are in [TASKS.md](../../features/identity-access/TASKS.md#task-18--done-2026-09-06); the named limitation it does not close is recorded there and scoped to C6/C7 and Task 27.

**Visible outcome:** a caller cannot infer whether someone else's address exists by anonymously submitting it and then claiming the same fresh CUIT with their own authenticated identity.

**Tracking/source:** IA-006 and IA-009 evidence; approved C1; revised IA-REQ-003/004 plus 005/026..029/033/035/038. Dependency: Task 17 C1 accepted. Current known-email requests create no Organization; unknown-email requests durably reserve CUIT. Hiding the second request's `409` alone still leaks through the attacker's resulting context, owned Organization, mail, or durable claim.

**Files:**

- Modify: `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs`, `src/Application/IdentityAccess/Organizations/RegisterOrganization/IRegistrationSecurity.cs`, `src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs`.
- Modify: `src/Domain/IdentityAccess/Organizations/RegistrationSubmission.cs`, `src/Domain/IdentityAccess/Organizations/RegistrationSubmissionOutcome.cs`, `src/Infrastructure/IdentityAccess/RegistrationIdempotencyStore.cs`, `src/Infrastructure/IdentityAccess/RegistrationInitialRoleProvisioner.cs`.
- Modify: `src/Web/Endpoints/Identity.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/ConfirmEmailTests.cs`.
- Create: `src/Domain/IdentityAccess/Organizations/PendingRegistrationIntent.cs`, `src/Infrastructure/Data/Configurations/IdentityAccess/PendingRegistrationIntentConfiguration.cs`, `tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationPrivacySequenceTests.cs`; an additive `DeferredRegistrationReservation` migration and the shared model/context updates.

**Request/tenant boundary:** anonymous initiation and token confirmation remain explicitly public and origin/antiforgery protected; a supplied invalid session still fails closed. Confirmed creation proves its identity server-side; any client email must match it. CUIT is Organization-owned, never an identity/profile selector. No public result exposes whether another identity exists.

- [x] **RED:** add paired known/unknown-address sequences with the same otherwise fresh CUIT. After each anonymous probe, sign in as an independently controlled identity and submit that CUIT; compare HTTP, the attacker's context/Organization visibility and delivered mail, and exclusive durable claims. Inspect target delivery with an isolated sink as supporting state evidence, not attacker access. Add normalized replay, simultaneous claims, proof expiry, and old-pending-registration upgrade cases. Require a runtime difference on current behavior. *Done:* `RegistrationPrivacySequenceTests` runs the paired probe-then-claim sequence and compares the claim's status, the attacker's ownership and the durable CUIT claim; on the previous code the attacker's claim succeeded for a known address and returned `registration_conflict` for an unknown one. Replay, both concurrency directions and proof expiry were added to `RegistrationTests`/`ConfirmEmailTests`. No old pending-registration rows existed to upgrade, so that case was dropped rather than faked.
- [x] **GREEN:** implement the approved intent/proof/reservation transitions, durable idempotency, protected expiring intent and additive upgrade. At finalization, create the aggregate graph and audit/outbox in one `IApplicationTransaction`; inject failure after a real insert and prove no orphan/profile/role/audit/claim remains. A second confirmed competing claim has one winner with the approved safe conflict; anonymous probes themselves never win ownership. *Done:* `PendingRegistrationIntent` plus the `DeferredRegistrationReservation` migration carry the unproved phase; finalization writes the whole graph and its audit/outbox inside the existing `IApplicationTransaction`. Two confirmed competing claims leave one winner and settle the loser `Conflicted` on real PostgreSQL; an anonymous probe never wins ownership.
- [x] **REFACTOR/verify:** generalize only the reusable confirmation/registration seam needed by Task 19; preserve existing-account non-takeover and existing delivered links. Run both commands below before/after; expect the paired-sequence/state assertion RED, then equivalent observable behavior and atomic/replayed effects GREEN. *Done:* the confirmation seam now dispatches on message type, so Task 19 reuses it without a second endpoint; existing delivered links and existing-account non-takeover still hold. Both commands ran green after the change (37/37 and 11/11).

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RegistrationPrivacySequenceTests|RegistrationTests|ConfirmEmailTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter MigrationUpgradeTests
```

- [x] **Done:** both sequence directions and their concurrent/replayed variants pass against real PostgreSQL; TRACEABILITY closes the residual only on this state-level evidence and approved SPEC change, not neutral status alone. *Done:* Domain 162, Application.Unit 192, Infrastructure.Integration 236, Application.Functional 401, browser acceptance 21, client 106; Debug and Release builds clean. TRACEABILITY closes the IA-REQ-003 residual on that state-level evidence and the accepted SPEC change.

## Task 19: Persist Personal ownership and protected AR/DNI atomically

**Done 2026-09-06** on the accepted C3 and C7. Evidence, exact commands and counts are in [TASKS.md](../../features/identity-access/TASKS.md#task-19--done-2026-09-06).

**Visible outcome:** one global identity can own exactly one Personal tenant with its own protected profile/document, without creating another account or making its document visible to an Organization.

**Tracking/source:** IA-010; IA-004 persistence; SPEC §2.3, IA-REQ-001/002/006..013/026..029/033..038; BR-ID-003/006/007, BR-REG-001/002, BR-TEN-003/004, BR-INV-003. Dependencies: Task 18 (done 2026-09-06 on the accepted C1) plus **C3 and C7, accepted 2026-09-06 for synthetic data only**. C3 supplies the profile and the protected documentary identity. C7 supplies two things this task persists and no later task can retrofit: the server-derived `DataClassification` stamped on every profile and document row at creation, and the shared attempt-budget store that must already exist when Task 20 exposes Personal registration. Actual PII remains disabled.

**Files:**

- Modify: `src/Domain/IdentityAccess/Tenants/Tenant.cs`, `src/Domain/IdentityAccess/Memberships/TenantMembership.cs`, `src/Application/Common/Interfaces/IApplicationDbContext.cs`, `src/Infrastructure/Data/ApplicationDbContext.cs`, `src/Infrastructure/DependencyInjection.cs`.
- Create: `src/Domain/IdentityAccess/People/PersonProfile.cs`, `src/Domain/IdentityAccess/People/IdentityDocument.cs`, `src/Domain/IdentityAccess/People/PersonalTenantOwnership.cs`.
- Create: `src/Application/IdentityAccess/People/IIdentityDocumentProtector.cs`, `src/Application/IdentityAccess/People/IIdentityDocumentFingerprint.cs`, `src/Infrastructure/IdentityAccess/People/IdentityDocumentProtector.cs`, `src/Infrastructure/IdentityAccess/People/IdentityDocumentFingerprint.cs`.
- Create: `src/Infrastructure/Data/Configurations/IdentityAccess/PersonProfileConfiguration.cs`, `src/Infrastructure/Data/Configurations/IdentityAccess/IdentityDocumentConfiguration.cs`, `src/Infrastructure/Data/Configurations/IdentityAccess/PersonalTenantOwnershipConfiguration.cs`; additive `PersonalIdentity` migration.
- Create: `src/Application/IdentityAccess/Security/ISharedAttemptBudget.cs`, `src/Infrastructure/IdentityAccess/Security/PostgreSqlAttemptBudget.cs`; additive `SharedIdentityAttemptBudgets` migration. **Moved here from Task 27** so the budget exists before any public Personal route does; Task 27 keeps ownership of moving the remaining scopes onto it and of proving it across instances and restarts.
- Create: `tests/Domain.UnitTests/IdentityAccess/PersonalIdentityTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/PersonalIdentityMappingTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/PersonalDocumentProtectionTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/SharedAttemptBudgetTests.cs`.

**Boundary:** this task adds internal domain/persistence adapters, no public endpoint. Ownership derives from authenticated/proved identity, never client ownership fields. Personal has one owner membership and no invitations; document uniqueness applies globally across unpurged rows without making global document lookup a public capability. The budget lands as a port, an adapter and a table with no caller: its first scope, `personal.document.claim`, is consumed by Task 20, and the `429`/`503` answers are mapped there, because nothing here is reachable over HTTP. Recording a document is all this task does with it — correcting one is IA-REQ-058, which needs C4's recent proof and therefore lands in Task 26, so nothing here may half-build a correction route.

- [x] **RED shape/shells:** assert the exact new model and its ownership/document relationships by metadata; reuse `Tenant.CreatePersonal`. After shells, test AR/DNI normalization, sole owner, Personal-only profile, no document on Organization registration/invitation/login, and no automatic merge on duplicate document. *Done:* `PersonalIdentityMappingTests` names the entity types as strings rather than importing them, so the first run failed 5/5 with "The model does not map CleanArchitecture.Domain.IdentityAccess.People.PersonProfile" — a runtime RED rather than a build error. The Domain rules followed as a compile RED, then as 7 of 11 assertion failures once skeletons without guards existed.
- [x] **Behavioral RED:** real PostgreSQL races for two Personal creations by one identity and two identities claiming one normalized document must leave one valid graph. Prove soft deletion does not free the document and cross-tenant ownership/FK violations fail. After Task 18's separate identity-confirmation stage, inject failure after a real Personal-profile insert: roll back the new Personal/profile/document/owner/role/audit/outbox graph while preserving the already-confirmed identity and every preexisting Organization membership. Reject raw/default/unversioned protection material. Exercise the budget store on its own terms: parallel increments at the threshold admit exactly the budget and no more, a spent window stays spent when a second adapter instance reads it, and an unreachable store refuses rather than admits. That second instance shares this process: it is a persistence check, not distributed evidence, and Task 27 owns the cross-instance and restart proof for this scope as for every other. *Done:* `PersonalDocumentProtectionTests` and `SharedAttemptBudgetTests` were 8 of 16 failing before the adapters existed. The old-pending-row upgrade case does not apply — no `PersonProfile`, document or budget row existed to upgrade — so it was dropped rather than faked.
- [x] **GREEN:** store authenticated versioned ciphertext with purpose separation and keyed/versioned fingerprint lookup; enforce uniqueness across all retained key versions during rotation. Persist the explicit ownership key and constraints for one Personal per identity/one owner; an Application precheck alone is insufficient. Use additive migration and explicit delete behavior; never copy DNI/CUIT into Identity or audit/outbox payloads. Stamp `DataClassification` on every profile and document row from the deployment mode at creation — never from a request field, never backfilled later — and land the budget table and adapter with no route reading them yet. *Done:* uniqueness lives in `UX_IdentityDocumentFingerprints_Fingerprint` across every retained key version; one Personal per identity is the `PersonalTenantOwnerships` key and one identity per Personal is its unique index, neither an application precheck. `PersonalIdentity` and `SharedIdentityAttemptBudgets` are additive; every FK declares its delete behaviour; no DNI or CUIT reaches Identity, audit or an outbox payload.
- [x] **REFACTOR/verify:** test decrypt round-trip under the intended protector, wrong purpose/key rejection, simultaneous old/new fingerprint lookup, crash-safe rotation without duplicate admission, and safe redaction. Both migration paths preserve synthetic retained and purged-policy sentinels. Expected: runtime invariant/protection/constraint RED, then all focused behavior GREEN. *Done:* round-trip under the intended protector, a foreign-purpose payload answered `null` rather than read, one fingerprint for two spellings of one document, a fingerprint per retained key version with the current one leading, and absent, too-short, non-Base64 and unversioned key material all refused. `MigrationUpgradeTests.Personal_identity_and_shared_budget_schema_upgrade_in_order_and_preserve_preexisting_data` walks both migrations in order and probes the constraints.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter PersonalIdentityTests
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "PersonalIdentityMappingTests|PersonalDocumentProtectionTests|SharedAttemptBudgetTests|MigrationUpgradeTests"
```

- [x] **Done:** no race can create a second Personal or reuse an unpurged documentary identity; encrypted/lookup values are absent from exposed contracts/logs; every stored row carries its classification; and the budget store is shared, spendable and fail-closed before any public Personal route exists. Missing real-PII policy keeps real collection disabled; synthetic implementation evidence is named as such. *Done:* the duplicate claim, the claim after a suspension and the claim inside a rolled-back transaction all fail on the unique index, and only a purge frees the number. Domain 173, Application.Unit 192, Infrastructure.Integration 258, Application.Functional 401; Release build clean. Real collection stays disabled — every row in these tests is `Synthetic` and the deployment mode is not switchable by this task.

## Task 20: Deliver Personal signup, own profile, and context switching

**Done 2026-09-06** on the accepted C3 and C7. Evidence, exact commands and counts are in [TASKS.md](../../features/identity-access/TASKS.md#task-20--done-2026-09-06).

**Visible outcome:** React asks explicitly Personal or Organization; a new person completes delivered confirmation, and an existing B2B identity adds Personal without re-registering credentials. Both can inspect/edit permitted own-profile fields and switch contexts without mixed permissions.

**Tracking/source:** IA-010 and IA-009 evidence; Task 19, and the C3/C7 acceptance Task 19 already required — this task adds no further decision entry — SPEC §§2.1/2.3/7, IA-REQ-005..013/025..030/038, BR-REG-001/002, BR-ID-003/006, BR-TEN-003/004. No new approved IA-REQ number is assumed.

**Files:**

- Modify: `src/Application/IdentityAccess/Authorization/Permissions.cs`, `src/Application/IdentityAccess/Context/GetIdentityContext/GetIdentityContextHandler.cs`, `src/Web/Endpoints/Identity/Contracts/IdentityContextResponse.cs`, `src/Web/Endpoints/Identity.cs`.
- Modify: `src/Web/ClientApp/src/features/identity/api/identityClient.js`, `src/Web/ClientApp/src/features/identity/context/IdentityContextPage.jsx`, `src/Web/ClientApp/src/features/identity/tenants/TenantSelector.jsx`, `src/Web/ClientApp/src/AppRoutes.jsx`, `src/Web/ClientApp/src/components/NavMenu.jsx`.
- Create: `src/Application/IdentityAccess/People/RegisterPersonal/RegisterPersonal.cs`, `src/Application/IdentityAccess/People/RegisterPersonal/RegisterPersonalHandler.cs`, `src/Application/IdentityAccess/People/CreatePersonalContext/CreatePersonalContext.cs`, `src/Application/IdentityAccess/People/CreatePersonalContext/CreatePersonalContextHandler.cs`, `src/Application/IdentityAccess/People/Profile/PersonalProfileRequests.cs`, `src/Application/IdentityAccess/People/Profile/PersonalProfileHandlers.cs`, `src/Web/Endpoints/Identity/PersonalEndpoints.cs`.
- Create: `src/Web/ClientApp/src/features/identity/register/ChooseContextPage.jsx`, `src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx`, `src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx`, `tests/Application.FunctionalTests/IdentityAccess/People/PersonalJourneyTests.cs`.

**Request/tenant boundary:** proposed anonymous `RegisterPersonal` is `IPublicRequest` and uses the approved staged proof flow; authenticated `CreatePersonalContext` and own-profile queries/edits use explicit new self-service codes in `Permissions.SelfServiceCodes`, `RequiresTenant=false`, and identity ownership. The approved request inventory must name each code/route. Context selection retains its existing permission and validated membership; no endpoint lets Organization administrators select a profile owner. Personal registration is the first public route bounded by a shared budget: it spends `personal.document.claim` through the port Task 19 landed, and this task adds the answers — `429` `rate_limit_exceeded` for an exhausted budget and `503` `service_unavailable` with `Retry-After` when the store is unreachable, never `429` for an outage. Every refused claim answers `409` `personal_registration_conflict`, with nothing in the body, headers or status separating a duplicate document from an identity that already owns a Personal. `document.correctionAvailable` is in the response and reads `false` throughout this task, because the dispute route is IA-REQ-058 and does not exist until Task 26; no screen may offer a control this increment cannot serve.

- [x] **RED:** first use non-importing file/route assertions for absent modules, then behavioral MSW/HTTP cases: explicit context choice; Personal-only AR/DNI; existing identity reused; confirmed new graph; repeated submit; required field errors; collision-safe response; disabled real-PII configuration; masked profile read and only permitted edits. Prove a duplicate document and an already-owned Personal produce identical answers, that the claim after the daily budget is refused, and that an unreachable budget store answers `503` rather than `429`. Test Organization, Platform and another identity cannot read/edit the document. *Done:* `PersonalJourneyTests` failed to compile against three absent namespaces, then covered every case listed here except one. The `disabled real-PII configuration` case became an assertion that every stored row is `Synthetic` and that the context reports the mode, because the deployment mode is not switchable from a test and pretending otherwise would be a fake.
- [x] **GREEN:** orchestrate Task 19 entities through Task 18's proof/transaction seam, add typed DTOs and routes, and expose visible navigation to proposed `/register`, `/personal/register`, and `/identity/profile`. Reuse `IdentityProvider`, `useSubmit`, `ProblemMessage`, `useFragmentToken` and the sole API transport; keep document/token input only in memory and clear it on completion/navigation. *Done:* `RegisterPersonalCommandHandler` writes the unproved phase through the same submission/idempotency seam; `ConfirmEmailCommandHandler` gained a third branch that finalizes it in one `IApplicationTransaction`; `CreatePersonalContextCommandHandler` is the proved path. `/register`, `/personal/register` and `/identity/profile` are reachable from the navigation, built on `IdentityProvider`, `useSubmit`, `ProblemMessage` and the single transport, and the document and password are held only in component state.
- [x] **REFACTOR/verify:** switch Personal → Organization → Personal, invalidate cached UI state by context, and reject in-flight/stale mutations that would silently continue under another tenant. Prove real-write finalization rollback leaves no new Personal/profile/document/owner/audit/outbox partial graph, preserves the identity established by the proof stage and its prior memberships, and replay has one effect set. Expected: missing behavior/state RED, then the same global identity and isolated usable contexts GREEN. *Done:* `Switching_between_personal_and_organization_never_mixes_their_permissions` walks Personal → Organization → Personal and proves permissions belong to one membership. `PersonalContextFactory` is the single place the graph is built, so the proved and the mailed paths cannot drift. A failed claim leaves nothing: the conflict tests assert one profile and one tenant survive.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PersonalJourneyTests|IdentityContextPermissionTests"
npm test --prefix src/Web/ClientApp -- PersonalPages.test.jsx AppRoutes.test.jsx IdentityProvider.test.jsx
```

- [x] **Done:** both newcomer and already-authenticated journeys are reachable from the real root UI, use delivered confirmation and no SQL activation, and no Organization invitation creates Personal automatically. Browser-level combined proof is recorded in Task 28. *Done:* both journeys are reachable from the real navigation, the delivered link is the same `/confirm-email` screen and no test activates an account by SQL. Browser-level combined proof stays with Task 28.

## Task 21: Add own-session management and recent reauthentication

**Done 2026-09-06** on the accepted C2 and C4. Evidence, commands and counts are in [TASKS.md](../../features/identity-access/TASKS.md#task-21--done-2026-09-06).

**Visible outcome:** the identity sees its active devices/sessions, revokes one or all others, and proves itself again before a sensitive change without borrowing authority from a tenant or Platform MFA proof.

**Tracking/source:** IA-011 and IA-007; Task 17 C2/C4; IA-REQ-019..026/029/035/038; reference `05-authentication.md` session/revocation/password sections and BR-SEC-001..004. Current `CreateSessionHandler` revokes every live prior session, so multi-device UI requires the accepted coexistence change.

**Files:**

- Modify: `src/Application/IdentityAccess/Sessions/CreateSession/CreateSessionHandler.cs`, `src/Domain/IdentityAccess/Sessions/UserSession.cs`, `src/Infrastructure/Identity/SessionCookieEvents.cs`, `src/Infrastructure/Data/Configurations/IdentityAccess/UserSessionConfiguration.cs`, `src/Web/Endpoints/Identity/SessionEndpoints.cs`, `src/Application/IdentityAccess/Authorization/Permissions.cs`.
- Create: `src/Application/IdentityAccess/Sessions/SessionIssuer.cs`, `src/Application/IdentityAccess/Sessions/ManageSessions/SessionManagementRequests.cs`, `src/Application/IdentityAccess/Sessions/ManageSessions/SessionManagementHandlers.cs`, `src/Application/IdentityAccess/Credentials/IRecentIdentityProof.cs`, `src/Application/IdentityAccess/Credentials/Reauthenticate/Reauthenticate.cs`, `src/Application/IdentityAccess/Credentials/Reauthenticate/ReauthenticateHandler.cs`, `src/Infrastructure/IdentityAccess/RecentIdentityProofStore.cs`.
- Modify: `src/Web/ClientApp/src/features/identity/api/identityClient.js`, `src/Web/ClientApp/src/AppRoutes.jsx`, `src/Web/ClientApp/src/components/NavMenu.jsx`, `tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionConcurrencyTests.cs`.
- Create: `src/Web/ClientApp/src/features/identity/sessions/SessionsPage.jsx`, `src/Web/ClientApp/src/features/identity/sessions/SessionsPage.test.jsx`, `tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionManagementTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Sessions/ReauthenticationTests.cs`; additive `IdentityReauthentication` migration for durable proof/binding state.

**Request/tenant boundary:** list/revoke/revoke-others use existing `Permissions.IdentitySessionManage`, `[Authorize]`, `RequiresTenant=false`. Reauthentication has its explicit approved self-service code. Every lookup filters `IdentityId`; a UI-safe opaque revocation handle is never the BFF ticket/cookie session identifier. Show only current-device flag, bounded timestamps and a safe label; no raw tickets, provider tokens, detailed IP history, or cross-identity list.

- [x] **RED:** create two live sessions through HTTP, list only the current identity's sessions, revoke chosen/others and verify rejected cookies immediately. Test expiry, already-revoked replay, forged other-identity handle, concurrent sign-in/revoke/select, deterministic cap eviction and one audit per actual transition. Current revoke-all behavior must fail coexistence assertions. *Done:* `SessionManagementTests` was 9 of 10 failing, and the one that mattered most — `Signing_in_a_second_time_leaves_the_first_session_signed_in` — failed against the revoke-everything behaviour exactly as this step required. The forged-handle, already-revoked-replay, cap-eviction and audit-count cases are in the same file; expiry and the concurrent sign-in/revoke/select races live in `SessionTests` and `SessionConcurrencyTests`, whose four superseding assertions were rewritten to the accepted contract rather than deleted.
- [x] **GREEN:** extract a shared `SessionIssuer` reusable by password/Google, serialize issuance against the identity for the accepted cap, and reuse `IApplicationTransaction`, `ICurrentSession`, and `SessionWriteRetry`. Persist single-use identity/session/action/security-version proofs with accepted expiry and atomic consumption. Implement password reauthentication now and the provider-neutral proof port; Task 23 supplies linked-Google reauthentication. A new session, logout, credential change or compromise invalidates old proofs; Platform second-factor proof stays separately session-bound. *Done:* `SessionIssuer` is the one place a session is created, serialized by `SessionLock` in the two-argument advisory space, reusing `IApplicationTransaction` and `ICurrentSession`. `RecentIdentityProofStore` persists single-use identity/session/action/security-version proofs consumed by one conditional update. Password reauthentication is implemented and the provider-neutral method is on the port for Task 23; Platform step-up stays separately session-bound and neither proof satisfies the other.
- [x] **REFACTOR/verify:** current-session revoke clears cookie and frontend context; revoke-others keeps only the current accepted session. Replay cannot extend proof lifetime or duplicate revocation audit; rollback after an actual session mutation restores the full state. The current UI asks for password proof without storing it; linked-provider UI/proof behavior is explicitly deferred to Task 23, so this task has no dependency cycle. *Done:* revoke-others keeps exactly the acting session and a repeat writes no second audit row; a spent proof cannot be spent again and cannot be extended by waiting. The UI asks for the password, spends it against the reauthentication route and clears the field — `SessionsPage.test.jsx` proves the proof is sent before the revocation and that neither reaches browser storage. Linked-provider proof is deferred to Task 23, so there is no cycle.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "SessionManagementTests|ReauthenticationTests|SessionConcurrencyTests|SessionTests"
npm test --prefix src/Web/ClientApp -- SessionsPage.test.jsx IdentityProvider.test.jsx
```

- [x] **Done:** session list/revoke controls operate on persisted sessions through the BFF; proof theft, replay, stale session and simultaneous-cap races fail safely. Expected RED/GREEN must prove those behaviors, not merely that session rows exist. *Done:* the controls operate on persisted sessions through the BFF; a revoked cookie stops working on the next request, a forged reference is `404`, an unproved revoke is `401`, and six sign-ins leave five live sessions with one `evicted` audit row. Domain 173, Application.Unit 192, Infrastructure.Integration 258, Application.Functional 431, browser acceptance 21, client 116.

## Task 22: Complete password recovery and authenticated change

**Done 2026-09-06** on the accepted C2 and C4. Evidence, commands and counts are in [TASKS.md](../../features/identity-access/TASKS.md#task-22--done-2026-09-06).

**Visible outcome:** a person follows the actual delivered reset link to set a new password, or changes it from settings after fresh proof; old credentials/sessions stop working according to C2.

**Tracking/source:** IA-011 and IA-009 evidence; Tasks 17 C4 and 21; SPEC §2.3, IA-REQ-019..029/035/038; reference `05-authentication.md` recovery/change. Existing `IIdentityAccountService` supports lookup/create/confirm, not reset/change; Identity token providers are already registered. `SessionCookieEvents` does not validate Identity's SecurityStamp, so a stamp-only reset does not revoke this BFF.

**Files:**

- Create: `src/Application/IdentityAccess/Credentials/IIdentityCredentialService.cs`, `src/Infrastructure/IdentityAccess/IdentityCredentialService.cs`, `src/Application/IdentityAccess/Credentials/PasswordRecovery/PasswordRecoveryRequests.cs`, `src/Application/IdentityAccess/Credentials/PasswordRecovery/PasswordRecoveryHandlers.cs`, `src/Application/IdentityAccess/Credentials/ChangePassword/ChangePassword.cs`, `src/Application/IdentityAccess/Credentials/ChangePassword/ChangePasswordHandler.cs`.
- Create: `src/Web/Endpoints/Identity/PasswordEndpoints.cs`, `src/Infrastructure/Outbox/PasswordRecoveryDeliveryHandler.cs`, `src/Web/ClientApp/src/features/identity/credentials/PasswordPages.jsx`, `src/Web/ClientApp/src/features/identity/credentials/PasswordPages.test.jsx`, `tests/Application.FunctionalTests/IdentityAccess/Credentials/PasswordLifecycleTests.cs`.
- Modify: `src/Infrastructure/DependencyInjection.cs`, `src/Infrastructure/Outbox/WorkerRegistration.cs`, `src/Application/IdentityAccess/Authorization/Permissions.cs`, `src/Web/Endpoints/Identity.cs`, `src/Web/ClientApp/src/features/identity/api/identityClient.js`, `src/Web/ClientApp/src/features/identity/login/LoginPage.jsx`, `src/Web/ClientApp/src/AppRoutes.jsx`.
- Modify: `tests/Infrastructure.IntegrationTests/IdentityAccess/OutboxDeliveryTests.cs`; extend the durable credential-operation mapping additively if the accepted token-consumption design needs new state.

**Request/tenant boundary:** `RequestPasswordRecovery` and `ResetPassword` are explicit `IPublicRequest` requests protected by same-origin antiforgery and recovery-specific limits; only a valid purpose-bound proof can reset. `ChangePassword` is authorized self-service with `RequiresTenant=false`, current identity/session and recent action-bound proof. No tenant/operator reset for another identity is added.

- [x] **RED:** neutral known/unknown/disabled-account request behavior, actual reset-mail dispatch/link, invalid/expired/used/superseded/wrong-purpose token, new password-policy failures and simultaneous resets. Prove failed credential writes leave no consumed token, changed stamp, revoked sessions, audit or notification; inject the fault after real persistence. Google-only accounts cannot supply a made-up current password. *Done:* `PasswordLifecycleTests` did not compile against the absent `Credentials` domain slice, then drove the whole flow: the neutral known/unknown answer, the delivered link, the spent and superseded token, the policy refusal, and the change that needs a proof. The disabled-account case uses lockout, which is the restriction that exists today — administrative suspension is C6's and arrives with Task 26, and asserting against a state nothing can reach would have been a test of nothing.
- [x] **GREEN:** adapt ASP.NET Identity credential APIs behind the new port. Reuse secure token generation, versioned hashing, `OutboxSecret` and its writer with a distinct recovery purpose; never repurpose confirmation tokens. Atomically mutate credential/security version, consume token/proof, revoke the accepted persisted-session set and write audit/outbox. Reset revokes all; change replaces the current session and revokes others. Use deterministic Identity-level serialization for concurrent reset/change/sign-in so stale credentials cannot issue a surviving session. *Done:* `IdentityCredentialService` adapts ASP.NET Identity's own hasher and password validators, so the configured `PasswordOptions` stay the only policy. Recovery reuses `ISecureTokenGenerator`, `VersionedTokenHash`, `OutboxSecret` and its writer under a distinct message type; confirmation tokens are untouched. Each flow advances the security version, settles its token, revokes the accepted session set and writes its audit inside one `IApplicationTransaction`.
- [x] **REFACTOR/verify:** add login “Forgot password” and settings routes; reset pages read/erase the fragment and use the existing parser. Test delivered-before-reset and replay-after-success, wrong recipient, secret-free errors/logs/audits, unavailable delivery with bounded retries, and cookie rejection on the next request. Expected RED exposes absent recovery or still-live sessions; GREEN proves both password and persisted-session behavior. *Done:* the sign-in screen carries “Forgot your password?”, `/credentials/reset` reads the fragment through the existing `useFragmentToken` and erases it, and `/identity/password` buys the proof before it sends the change. `OutboxDeliveryTests` proves the delivered mail carries the link while the stored payload carries neither the token nor the address; the reset and change tests prove the cookies stop working on the very next request.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PasswordLifecycleTests|SessionConcurrencyTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter OutboxDeliveryTests
npm test --prefix src/Web/ClientApp -- PasswordPages.test.jsx
```

- [x] **Done:** actual delivered links and current-password/equivalent-proof change work end to end; resetting twice/racing cannot regain access or create duplicate security effects. Nothing relies solely on SecurityStamp invalidation or mocked email generation. *Done:* the delivered link and the proved change both work end to end, resetting twice cannot regain access, and reissuing kills the older link rather than leaving two. Nothing here relies on `SecurityStamp`: the security version is a Domain-owned row, and the sessions are revoked explicitly.

## Task 23: Add Google login and explicit secure linking

**Done 2026-09-06** on the accepted C2 and C4. Evidence, commands and counts are in [TASKS.md](../../features/identity-access/TASKS.md#task-23--done-2026-09-06).

**Visible outcome:** a person signs in with Google or explicitly links/unlinks it from their account settings; a matching email cannot take over an existing account, and no action removes the last usable authenticator.

**Tracking/source:** IA-013 and IA-009 evidence; Tasks 20–22 and accepted C4; SPEC §2.3, IA-REQ-001/002/019..029/038, BR-ID-005/006, reference `05-authentication.md` Google section. `AspNetUserLogins` already has unique provider-key storage in `BaselinePostgreSql`; use that credential adapter instead of inventing another account registry.

**Files:**

- Modify: `Directory.Packages.props`, `src/Web/Web.csproj`, `src/Web/DependencyInjection.cs`, `src/Web/Program.cs`, `src/Infrastructure/DependencyInjection.cs`.
- Continue the planned `src/Application/IdentityAccess/Sessions/SessionIssuer.cs` and `src/Application/IdentityAccess/Credentials/IRecentIdentityProof.cs` created in Task 21; supply linked-Google proof here, not in the earlier task.
- Create: `src/Application/IdentityAccess/ExternalLogins/IExternalIdentityService.cs`, `src/Application/IdentityAccess/ExternalLogins/ExternalLoginRequests.cs`, `src/Application/IdentityAccess/ExternalLogins/ExternalLoginHandlers.cs`, `src/Infrastructure/IdentityAccess/ExternalIdentityService.cs`, `src/Web/Infrastructure/Identity/GoogleOidcConfiguration.cs`, `src/Web/Endpoints/Identity/ExternalLoginEndpoints.cs`.
- Modify: `src/Web/ClientApp/src/features/identity/api/identityClient.js`, `src/Web/ClientApp/src/features/identity/login/LoginPage.jsx`, `src/Web/ClientApp/src/AppRoutes.jsx`.
- Create: `src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.jsx`, `src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.test.jsx`, `tests/Application.FunctionalTests/Infrastructure/ControlledOidcProvider.cs`, `tests/Application.FunctionalTests/IdentityAccess/ExternalLogins/GoogleOidcTests.cs`.

**Request/tenant boundary:** StartLogin is explicitly public; StartLink and unlink require authorized identity self-service, `RequiresTenant=false`, current session, recent proof and consent. Both starts use same-origin antiforgery. The server middleware callback cannot carry the SPA's antiforgery header or exact first-party Origin: its narrow approved protocol contract uses one-use state/nonce/correlation/PKCE, validated signature/issuer/audience/expiry and bound Login/Link purpose. No blanket CSRF disable or public business handler is introduced. Neither provider claims nor callback parameters grant tenant membership.

- [x] **RED:** use a controlled protocol provider through the real framework challenge/callback, not a mocked final identity. Reject wrong state/nonce/verifier/issuer/audience/signature, expired or replayed code, missing/unverified email, email-match takeover, mixed Login/Link purpose, linking a subject owned elsewhere, stale proof/session and concurrent link/unlink attempts. Include login to already-linked subject and explicit new-identity onboarding with no implicit tenant. *Done:* `GoogleOidcTests` drives the framework's own handler against `ControlledOidcProvider`, which publishes a discovery document and JWKS, mints codes bound to the nonce and the PKCE challenge, and refuses an exchange whose verifier, client credentials or redirect do not match — so a wrong verifier and a replayed code are refusals the provider makes, not assertions a stub was asked to make. Tampered state, impostor issuer, wrong audience, unpublished signing key, expired token and replayed nonce are one parameterized case; unverified email, email-match takeover, subject owned elsewhere, an address belonging to another local identity, a second link for one provider, a missing proof and the last authenticator are their own. *Two cases changed shape against the plan, and why:* “mixed Login/Link purpose” became a stronger assertion, because the single `/complete` route takes no purpose at all — a caller cannot ask for the crossing, so what is proved is that a sign-in completed while somebody else's session is open links nothing to that session. “Concurrent link/unlink” is proved as two identities racing for one provider account, which is the race the store's own key decides; a same-identity unlink race is bounded by the single-use proof before it reaches the count.
- [x] **GREEN:** configure mature ASP.NET Core OpenID Connect middleware and server-side code exchange/validation; do not implement bespoke OAuth/crypto. Keep provider tokens/correlation secrets out of React and revoke any unnecessary persisted provider tokens. Reuse Identity's provider-key uniqueness, Task 21 `SessionIssuer`, and Task 20 explicit Personal/Organization onboarding. New verified subject may establish identity only through the accepted no-conflict path; a matching local email redirects neutrally to login/recovery, never auto-links. *Done:* `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.11 configured in `GoogleOidcConfiguration` — code flow, `form_post`, PKCE `S256`, `SaveTokens = false`, `MapInboundClaims = false`, issuer/audience/lifetime validation on — and registered only when a client id and secret exist, so a deployment without one has no route rather than a route that fails late. No OAuth and no cryptography is written here. Links live in ASP.NET Identity's own `AspNetUserLogins`, with the additive `UX_AspNetUserLogins_LoginProvider_UserId` making “one link per provider per identity” a database rule, and `Handle`/`LinkedAt` added as database-defaulted columns so the framework's own writes need no knowledge of them. Sessions come from Task 21's `SessionIssuer`, and a provider-created identity gets no password, no tenant and no membership.
- [x] **REFACTOR/verify:** prove the unique link, recent action proof, security version and audit/outbox changes are atomic; rollback/replay cannot produce a second identity or detach the last authenticator. The authorization code necessarily reaches the server callback; redact its URL/query/body and provider errors, then redirect to a clean allowlisted local URL. Linking preserves Personal/Organization ownership and does not satisfy Platform's separate MFA gate. *Done:* every effect — the link, the spent proof, the security version, the session revocations and the audit — commits inside one `IApplicationTransaction`, under the per-identity advisory lock. The code never reaches a URL: `form_post` puts it in a body, and the one place that formatted it into a message was the handler's own Debug tracing, now pinned above Debug by configuration; `Nothing_the_callback_carried_is_written_to_a_log_or_returned_to_the_browser` asserts both halves. The redirect is `/external/return` with one word from a closed set. Linking touches no membership, so Personal and Organization ownership are untouched, and it writes nothing a Platform MFA gate reads.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "GoogleOidcTests|ReauthenticationTests|SessionManagementTests"
npm test --prefix src/Web/ClientApp -- ExternalAccountsPage.test.jsx AppRoutes.test.jsx
```

- [x] **Done:** real protocol negatives and explicit link/unlink semantics pass with the controlled provider. An operator-owned live Google smoke separately requires a consent project, OAuth client secret and exact redirect registration; no API key substitutes for authentication, and CI success does not claim live-provider activation. *Done:* 23/23 protocol and semantic cases pass against the controlled provider. **This is not a live-Google claim.** Activating the real provider needs an OAuth client, its secret and the exact redirect URI registered in the operator's own Google project; the steps are in [RUNNING-LOCALLY.md](../../features/identity-access/RUNNING-LOCALLY.md#signing-in-with-google) and only the account owner can take them.

## Task 24: Expose custom Organization roles through existing authorization

**Done 2026-09-06** on the accepted C5 with amendments D1–D4. Evidence, commands and counts are in [TASKS.md](../../features/identity-access/TASKS.md#task-24--done-2026-09-06).

**Visible outcome:** an authorized Organization administrator creates, renames, edits and retires custom roles, sees their permissions, and observes authorization changes immediately without role-name checks.

**Tracking/source:** IA-005 continuation and IA-009 evidence; Task 17 C5; SPEC §2.3, IA-REQ-005..013/026/029/033..038/047; BR-AUT-001..008. `Role.Create/Rename/Retire`, `RolePermission`, `MembershipRole`, the evaluator and version/audit interceptor already exist; extend them instead of replacing the model.

**Files:**

- Modify: `src/Domain/IdentityAccess/Authorization/Role.cs`, `src/Domain/IdentityAccess/Authorization/RolePermission.cs`, `src/Domain/IdentityAccess/Authorization/Permission.cs`, `src/Application/IdentityAccess/Authorization/Permissions.cs`, `src/Infrastructure/Identity/EffectivePermissionReader.cs`, `src/Infrastructure/Data/Configurations/IdentityAccess/RoleConfiguration.cs`, `src/Infrastructure/Data/Configurations/IdentityAccess/PermissionConfiguration.cs`.
- Create: `src/Application/IdentityAccess/Roles/RoleRequests.cs`, `src/Application/IdentityAccess/Roles/RoleHandlers.cs`, `src/Web/Endpoints/Identity/RoleEndpoints.cs`, `src/Web/ClientApp/src/features/identity/roles/RolesPage.jsx`, `src/Web/ClientApp/src/features/identity/roles/RolesPage.test.jsx`, `tests/Application.FunctionalTests/IdentityAccess/Roles/RoleAdministrationTests.cs`; additive `AssignableRoleCatalog` migration for accepted catalog metadata.
- Create: `src/Application/IdentityAccess/Roles/IRoleAdministrationStore.cs`, `src/Infrastructure/IdentityAccess/RoleAdministrationStore.cs`; expose bounded role/catalog operations through this port, not raw role-association DbSets.
- Modify: `src/Web/ClientApp/src/features/identity/api/identityClient.js`, `src/Web/ClientApp/src/AppRoutes.jsx`, `src/Web/ClientApp/src/components/NavMenu.jsx`, `tests/Domain.UnitTests/IdentityAccess/RolePermissionTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/RolePermissionMappingTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Auditing/RoleMembershipAuditTests.cs`.

**Request/tenant boundary:** list uses `roles.read`; create/rename/change permissions/retire use `roles.manage`, all `[Authorize]` with active Organization required and actor authority checked for every proposed grant. `platform.*`, application-scoped self-service codes, another tenant's roles and protected system-role changes are refused. Role names remain labels. C5's Organization scope is explicit; this task does not silently add Platform role editing.

- [x] **RED:** custom role permits a real operation; a lookalike display name grants nothing. Test allowed catalog metadata, normalized-name uniqueness, protected system role, cross-tenant references, retired role denial, permission removal visible in the next request, stale updates and simultaneous last-admin-affecting edits. Assert exact allowlisted audit fields and one authorization-version increment for the actual committed change. *Done:* `RoleAdministrationTests` drives all fourteen cases over the production HTTP pipeline, and the two that carry the contract — the grant ceiling and the administrator floor — were each checked by removing the rule and watching them fail. `OrganizationOwnerAuthorityTests` and `OrganizationOwnerCatalogTests` pin amendment D1 from both ends: what a registered owner actually holds, and that the set is a decision somebody wrote down.
- [x] **GREEN:** add assignable/system catalog distinctions deliberately, preserve existing rows via migration, expose request handlers and typed DTOs, and re-use the evaluator and `TenantAuthorizationAuditInterceptor`. Enforce C5's delegation ceiling and last-effective-administrator rule across permission edit/retirement, not only membership deletion. Widening a role atomically cancels affected pending offers and terminalizes their envelopes; only a later authorized, explicit reissue can offer the wider role. *Done:* the catalogue now records, per code, whether an `Organization` owner holds it, and `PermissionCatalogSynchronizer` backfills every existing owner — without which C5 is unsatisfiable, because an owner holding nothing can never grant anything. Role rows live behind `IRoleAdministrationStore`; the ceiling, the floor and the offer cancellation stay in the handlers. The evaluator, `Role`, `RolePermission` and the audit interceptor are extended, not replaced. **No `AssignableRoleCatalog` migration was needed**: D1's metadata is a code-owned catalogue answer, not a persisted column.
- [x] **REFACTOR/verify:** separate catalog queries from mutation orchestration, preserve the system owner role, and show a clear stale-update/last-admin refusal in React. Test a rollback after persisted role/permission changes also restores version/audit/offer effects; repeat an already-applied retirement according to the approved response contract. Expected runtime authorization/audit RED, then observable permission-change GREEN. *Done:* the floor is counted from flushed state inside the transaction and refuses by rolling the write back, which is the only way a check made after a write can leave the database as it found it. A stale `version` answers `role_concurrency_conflict` and changes nothing; a retired role stops granting on the very next request; widening cancels every offer that named the role while narrowing leaves them standing. React offers only the codes the caller could grant, and shows the floor's refusal rather than guessing at it.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter RolePermissionTests
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "RolePermissionMappingTests|MigrationUpgradeTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RoleAdministrationTests|RoleMembershipAuditTests"
npm test --prefix src/Web/ClientApp -- RolesPage.test.jsx
```

- [x] **Done:** custom Organization roles work through the existing permission pipeline and UI; races cannot remove the last effective administrator or cross tenant boundaries. Persistence tests prove the richer catalog upgrade without rewriting historical migrations. *Done:* custom roles work through the existing permission pipeline and the real UI. The last-administrator and delegation invariants hold, and no route reads a tenant from anywhere but the validated session.

## Task 25: Complete member administration, ownership transfer and invitation lifecycle

**Visible outcome:** the Organization administrator lists members and invitations, changes allowed roles/status, transfers ownership deliberately, and can resend/replace/cancel an invitation from the real interface.

**Tracking/source:** IA-005/008 continuation and IA-009 evidence; Tasks 21/24 and accepted C5; SPEC §2.3, IA-REQ-005..018/026..030/033..038/047; BR-TEN-004/006, BR-AUT-007, BR-INV-001..005.

**Files:**

- Modify: `src/Domain/IdentityAccess/Memberships/TenantMembership.cs`, `src/Domain/IdentityAccess/Authorization/MembershipRole.cs`, `src/Domain/IdentityAccess/Invitations/Invitation.cs`, `src/Application/IdentityAccess/Invitations/AcceptInvitation/AcceptInvitationHandler.cs`, `src/Application/IdentityAccess/Invitations/ResendInvitation/ResendInvitationHandler.cs`, `src/Application/IdentityAccess/Invitations/CancelInvitation/CancelInvitationHandler.cs`, `src/Web/Endpoints/Identity/InvitationEndpoints.cs`, `src/Web/Endpoints/TenantInvitations.cs`.
- Create: `src/Application/IdentityAccess/Members/MembershipRequests.cs`, `src/Application/IdentityAccess/Members/MembershipHandlers.cs`, `src/Application/IdentityAccess/Members/TransferOwnership/TransferOwnership.cs`, `src/Application/IdentityAccess/Members/TransferOwnership/TransferOwnershipHandler.cs`, `src/Web/Endpoints/Identity/MembershipEndpoints.cs`, `tests/Application.FunctionalTests/IdentityAccess/Members/MembershipAdministrationTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Members/OwnershipTransferTests.cs`.
- Create: `src/Application/IdentityAccess/Members/IMembershipAdministrationStore.cs`, `src/Infrastructure/IdentityAccess/MembershipAdministrationStore.cs`; keep assignment/transfer persistence behind the narrow port and the shared transaction.
- Modify: `src/Web/ClientApp/src/features/identity/api/identityClient.js`, `src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.jsx`, `src/Web/ClientApp/src/AppRoutes.jsx`, `src/Web/ClientApp/src/components/NavMenu.jsx`, `tests/Application.FunctionalTests/IdentityAccess/Invitations/ResendAndCancelInvitationTests.cs`, `tests/Domain.UnitTests/IdentityAccess/InvitationTests.cs`.
- Create: `src/Web/ClientApp/src/features/identity/members/MembersPage.jsx`, `src/Web/ClientApp/src/features/identity/members/MembersPage.test.jsx`; add a migration only if approved transfer/offer-version state needs persistence.

**Request/tenant boundary:** `members.read` lists; `members.manage` changes memberships/withdraws offers; `members.invite` issues/reissues within delegated authority. Transfer uses the approved explicit ownership capability plus current-owner and fresh-proof checks. All administration is active-Organization scoped; route IDs never switch context. Public invite registration and authenticated identity-scoped acceptance preserve existing gates. Personal cannot invite and Organization administrators cannot change another identity's credentials/global state.

- [x] **RED:** list/edit only same-tenant members; revoke/suspend permissions on the next request without deleting the identity's other memberships. Exercise two concurrent last-admin removals, role edit racing owner transfer, transfer to inactive/foreign member, replayed transfer and self-removal. Assert at least one effective administrator in the committed state, not merely one row named “admin.”
- [x] **GREEN:** serialize administrative invariant checks with the mutation; transfer old/new owner state, role assignments, authorization version and audit atomically. Reuse existing invite/reissue/cancel handlers and token-envelope invalidation. Present explicit confirmation for ownership transfer and withdrawal; stale state produces the approved conflict and a refresh path.
- [x] **REFACTOR/verify:** add direct `Invitation.Issue` and `Reissue` tests with default `VersionedTokenHash` to close the existing narrow gap. Race accept/cancel/reissue and role widening; exactly one approved outcome survives and superseded mail tokens cannot be delivered or accepted. Inject rollback after real assignment mutation; deny-audit still persists through its independent writer with an exact allowlist. Expected runtime state/audit RED, then atomic administration GREEN.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter "InvitationTests|RolePermissionTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "MembershipAdministrationTests|OwnershipTransferTests|ResendAndCancelInvitationTests|RoleMembershipAuditTests"
npm test --prefix src/Web/ClientApp -- MembersPage.test.jsx
```

- [x] **Done:** every named member/invitation action is reachable and tested, last-owner/admin and delegation invariants survive races, and no operation deletes global identity or modifies another tenant's membership.
- [x] **Remediation (2026-09-07):** a review of Tasks 21-25 showed that four of this task's own rules were stated
  but not enforced - the membership version did not move when only assignments changed, a revoked member could
  never return, `/members` needed `roles.read` it should not have needed, and no directory could be paged past
  its first hundred records. All are fixed against the review's own reproductions; see
  `docs/features/identity-access/TASKS.md`, "Tasks 21-25 review remediation".

## Task 26: Implement bounded lifecycle, MFA recovery, retention and restore guards

**Visible outcome:** compromised/disabled identities lose access, a legitimate Platform administrator can recover a factor under the accepted proof contract, and maintenance/restore cannot silently reactivate old authority or deleted PII.

**Tracking/source:** IA-011/012/015; Tasks 19/21–25 and accepted C6/C7, plus **C3 and C4** for the documentary dispute this task carries: IA-REQ-058's owner half requires a live C4 recent proof, so neither half of it can precede Task 22. SPEC §2.3/§11, IA-REQ-008/013/024/026..029/035..037/041/046; BR-SEC-003..005 and the pinned reference's MFA/restore sections. Mandatory source restore guarantees remain tracked even where its implementation architecture is unadopted.

**Files:**

- Modify: `src/Domain/IdentityAccess/Identities/IdentityAccountStatus.cs`, `src/Infrastructure/Identity/ApplicationUser.cs`, `src/Infrastructure/Identity/SessionCookieEvents.cs`, `src/Domain/IdentityAccess/Platform/PlatformMfaEnrollment.cs`, `src/Domain/IdentityAccess/Platform/PlatformRecoveryCode.cs`, `src/Application/IdentityAccess/Platform/Mfa/PlatformMfaHandlers.cs`, `src/Infrastructure/Outbox/OutboxDispatcher.cs`, `src/OutboxWorker/Program.cs`, `src/Web/Program.cs`.
- Create: `src/Application/IdentityAccess/Lifecycle/IdentityLifecycleRequests.cs`, `src/Application/IdentityAccess/Lifecycle/IdentityLifecycleHandlers.cs`, `src/Application/IdentityAccess/Lifecycle/IRetentionPolicy.cs`, `src/Application/IdentityAccess/Lifecycle/IRecoveryAdmission.cs`, `src/Infrastructure/IdentityAccess/Lifecycle/LifecycleMaintenanceService.cs`, `src/Infrastructure/IdentityAccess/Lifecycle/RecoveryAdmission.cs`, `src/Web/Infrastructure/Identity/RecoveryAdmissionMiddleware.cs`.
- Create: `src/Application/IdentityAccess/Platform/Mfa/RecoverPlatformMfa.cs`, `src/Application/IdentityAccess/Platform/Mfa/RecoverPlatformMfaHandler.cs`, `src/Web/ClientApp/src/features/platform/invitations/MfaRecoveryPage.jsx`, `src/Web/ClientApp/src/features/platform/invitations/MfaRecoveryPage.test.jsx`.
- Create: `tests/Application.FunctionalTests/IdentityAccess/Lifecycle/IdentityLifecycleTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/RetentionLifecycleTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/RestoreAdmissionTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformMfaRecoveryTests.cs`, `docs/features/identity-access/RECOVERY-AND-RETENTION.md`; additive `IdentityLifecycleMaintenance` migration.
- Create: `src/Application/IdentityAccess/People/Documents/DocumentDisputeRequests.cs`, `src/Application/IdentityAccess/People/Documents/DocumentDisputeHandlers.cs`, `src/Web/Endpoints/Identity/DocumentDisputeEndpoints.cs`, `tests/Application.FunctionalTests/IdentityAccess/People/DocumentDisputeTests.cs`; both halves of IA-REQ-058 land together, since a correction with no dispute behind it is the bypass the contract forbids.
- Modify: `src/Web/Endpoints/Platform/PlatformMfaEndpoints.cs`, `src/Web/ClientApp/src/AppRoutes.jsx`; use the existing Platform client/identity transport for the recovery UI.

**Request/tenant boundary:** self-service lifecycle requests require the accepted identity code and recent primary proof, `RequiresTenant=false`; no arbitrary-user disable/recovery endpoint is inferred. Existing Organization/Platform administration retains its tenant permissions and last-owner controls. Platform recovery requires the current authenticated identity, fresh primary proof and unused recovery material; mailbox/DNI/cookie alone cannot replace MFA. Maintenance is an internal worker, not a public purge/restore API; background authority is narrow and auditable. Both halves of the documentary dispute live here: the owner opens one over their own document under a live C4 proof and never writes a document value, and a Platform operator resolves it under `platform.identities.documents.resolve`, a recent step-up and an external evidence reference — never for their own identity, and never without a stored dispute. A legal hold stops erasure only: it changes no account state, refuses no sign-in and blocks no reactivation.

- [ ] **RED:** disable then exercise password/Google/cookie/tenant operations; none can bypass disabled state. Recover MFA with wrong/reused code, stale or other-session proof, and concurrent consumption; only one replacement factor becomes usable after acknowledgement. Revocation, security version, factor/token state, audits and applicable notifications roll back together on injected real-write failure.
- [ ] **GREEN lifecycle:** implement accepted disable/reactivation and factor-replacement transitions; rotate/retire old factor material and revoke the required sessions/proofs. Preserve at least one viable Platform owner/recovery path without introducing a default credential or automatic elevation. Expose only the approved recovery UI; recovery material is shown once and never logged/stored by React.
- [ ] **RED/GREEN retention:** with synthetic data and `TimeProvider`, test bounded batches, stable cursor/CAS, expired vs live records, legal hold, unknown policy, replay, cancellation and crash/resume. Terminalize secrets safely; preserve required audit/delivery evidence. Purge only approved data categories after policy eligibility, retaining documented uniqueness or erasure evidence as required. Missing policy means no destructive action. Real-data purge is outside local tests.
- [ ] **RED/GREEN dispute:** open a dispute with a stale, absent or other-session proof and with one already open; resolve one as the subject's own operator identity, without a stored dispute, and with an evidence reference outside the accepted shape. Prove a `corrected` resolution replaces ciphertext and every retained fingerprint row in one transaction or none, that it is refused when the claimed tuple belongs to another identity, that no claimed or recorded value reaches a log, audit record, outbox payload or response, and that a dispute changes nothing about sign-in, the Personal tenant or the recorded value until it resolves.
- [ ] **RED/GREEN restore:** use an isolated synthetic backup that predates session revocation, password/link/factor replacement, grant removal and documentary purge. Start with ingress/delivery closed. The admission adapter checks the accepted operator-controlled authority outside that restored database; missing/stale/invalid evidence fails closed on every restart. Quarantine restored credentials/grants, refuse old cookies/tokens and tokenized messages, and require approved reproof/reconciliation before explicit release. Insufficient external deletion evidence keeps Personal data quarantined. A flag restored from the same backup or stamp rotation alone cannot pass the test.
- [ ] **REFACTOR/verify:** separate eligibility policy, bounded executor and admission adapter; run the commands below. Expected runtime unsafe-resurrection/replay/retention RED, then safe transitions and persistent closed admission GREEN. Record required external authority and remaining deviations in the runbook; synthetic adapter proof is not live restore certification.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "IdentityLifecycleTests|PlatformMfaRecoveryTests|DocumentDisputeTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "RetentionLifecycleTests|RestoreAdmissionTests|MigrationUpgradeTests"
npm test --prefix src/Web/ClientApp -- MfaRecoveryPage.test.jsx
```

- [ ] **Done:** the accepted local lifecycle and recovery contract is implemented and tested; maintenance cannot outrun policy and restore remains closed until valid evidence. Real-PII purge, external recovery-authority provisioning and live restore release are recorded as separate blocked operations until approved/proved, not declared optional or completed by these tests.

## Task 27: Prove shared abuse controls, deployment safeguards and operations

**Visible outcome:** a staged operator can verify bounds across instances/restarts, key compatibility between Web/worker, and safe failures before exposing the service or real PII.

**Tracking/source:** IA-015 with IA-007/012/014 control owners; Task 26 and accepted C6/C7; SPEC §§2.3/8/11, IA-REQ-019/022/026..029/031/032/040/041, pinned reference session/secret/restore guarantees. Current login, MFA attempts and bootstrap recovery controls are process-local; their existing tests do not prove distributed limits.

**Files:**

- Modify: `src/Web/Infrastructure/Identity/LoginRateLimiting.cs`, `src/Web/Infrastructure/Identity/TimeProviderFixedWindowRateLimiter.cs`, `src/Infrastructure/Platform/PlatformMfaAttemptLimiter.cs`, `src/Infrastructure/Platform/ConfiguredPlatformBootstrapper.cs`, `src/Infrastructure/IdentityAccess/IdentityDataProtectionConfiguration.cs`, `src/Infrastructure/DependencyInjection.cs`, `src/Web/DependencyInjection.cs`, `src/Web/Program.cs`, `src/OutboxWorker/Program.cs`.
- Create: `src/Web/Infrastructure/Identity/IdentitySecurityHeaders.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/WebWorkerKeyCompatibilityTests.cs`, `tests/Application.FunctionalTests/IdentityAccess/Api/IdentityDeploymentGuardTests.cs`, `docs/features/identity-access/OPERATIONS.md`.
- Modify: `src/Application/IdentityAccess/Security/ISharedAttemptBudget.cs`, `src/Infrastructure/IdentityAccess/Security/PostgreSqlAttemptBudget.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/SharedAttemptBudgetTests.cs`. **The port, adapter, table and migration land in Task 19**, because Task 20 exposes a public route that needs them; what belongs here is moving login, MFA and bootstrap recovery onto them and proving the properties Task 19 only made possible.
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/ForwardedHeadersSecurityTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/EmailConfigurationTests.cs`, `tests/Infrastructure.IntegrationTests/IdentityAccess/OutboxDeliveryTests.cs`, `docs/features/identity-access/EMAIL-SETUP.md`.

**Request/tenant boundary:** public registration/recovery and login limits use opaque bounded keys without disclosing accounts; MFA/recovery limits derive authenticated identity or server-bound invitation as appropriate. The chosen client address follows only trusted proxy configuration. Deployment checks grant no application/Platform bypass. Web/worker startup fail closed for missing required keys, recovery admission or delivery configuration, and C7's `PersonalDataReadiness` hosted service belongs to that same set of startup guards: it lands here, not in Task 19, because Task 19 stamps rows and this task is where a deployment is stopped from starting in the wrong mode.

- [ ] **RED shared limits:** exhaust each applicable budget across two independently constructed service instances using one PostgreSQL store; restart one instance and show the budget remains spent. Include parallel requests at threshold, account spelling variants, independent IP/account limits, MFA new-session evasion, bootstrap/recovery replay, expiry and `Retry-After`. Set accepted numeric budgets explicitly; do not count per-instance memory as distributed proof. Task 19 landed the store and proved only that it persists a spent window; **every scope gets its cross-instance and restart evidence here**, `personal.document.claim` included, and the scopes arriving from process-local limiters additionally have to be moved onto the shared store before they can be proved at all.
- [ ] **GREEN:** move the remaining budgets onto the adapter Task 19 landed, and add bounded expiry cleanup; retain deterministic time tests and separate credential lockout from transport limits. Use existing PostgreSQL as the proposed deployment default after C7 approval; add neither Redis nor a cloud provider merely for this task. Include storage-unavailable and high-cardinality-abuse behavior with a documented fail-closed policy and safe telemetry.
- [ ] **RED/GREEN keys and transport:** construct independent Web/worker Data Protection providers over a temporary shared repository and wrapping certificate, encrypt in one/decrypt in the other, rotate/restart and repeat. Reject wrong discriminator/certificate/purpose and an unreadable envelope. Preserve existing Resend and explicit local-folder modes. Test exact-origin enforcement behind trusted/untrusted forwarding, CSP/security headers and absence of callback codes, session handles, documents, addresses and secrets in diagnostics.
- [ ] **REFACTOR/verify operations:** record bounded metrics/alerts for login/recovery denials, unreadable/expired envelopes, backlog age, delivery failures and maintenance/restore admission. Exercise loss of key/transport/state using isolated sinks; backlog recovery must respect original token expiry/idempotency, not resend a superseded credential. The operations guide separates synthetic smoke, operator configuration, external-account activation, and release evidence.

```powershell
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "SharedAttemptBudgetTests|WebWorkerKeyCompatibilityTests|EmailConfigurationTests|OutboxDeliveryTests|MigrationUpgradeTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "IdentityDeploymentGuardTests|ForwardedHeadersSecurityTests|PlatformMfaAttemptLimitTests|PlatformBootstrapRecoveryTests"
```

- [ ] **Done:** runtime tests prove cross-instance/restart budgets and actual cross-process-key compatibility; configuration validation alone is insufficient. Deployment evidence names key/PII/recovery owners and the still-blocked external prerequisites. No production, complete-standard or legal-compliance claim follows automatically.

## Task 28: Prove the complete local journeys and close only the achieved scope

**Visible outcome:** a fresh local checkout can run the documented B2B/B2C journeys with synthetic identities, delivered mail and controlled OIDC; the final record names exactly what works and what still prevents real-PII or public use.

**Tracking/source:** IA-009 evidence only; Tasks 18–27 and their approved requirements; SPEC §2.3/§9/§13, historical first-increment requirements and the approved Task 17 delta. This task adds evidence and corrections for the fixed matrix below, not new normative ownership or a new feature wishlist.

**Files:**

- Modify: `tests/Web.AcceptanceTests/Features/IdentityAccess.feature`, `tests/Web.AcceptanceTests/Features/PlatformOperations.feature`, `tests/Web.AcceptanceTests/Pages/IdentityAccessPages.cs`, `tests/Web.AcceptanceTests/Pages/PlatformOperationsPage.cs`, `tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs`, `tests/Web.AcceptanceTests/StepDefinitions/PlatformOperationsStepDefinitions.cs`, `tests/Web.AcceptanceTests/IdentityAccessFixtures.cs`, `tests/Web.AcceptanceTests/PlatformFixtures.cs`, `tests/Web.AcceptanceTests/AspireSetup.cs`.
- Create: `tests/Web.AcceptanceTests/Features/IdentityContinuation.feature`, `tests/Web.AcceptanceTests/Pages/IdentityContinuationPages.cs`, `tests/Web.AcceptanceTests/StepDefinitions/IdentityContinuationStepDefinitions.cs`.
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Authorization/PermissionMatrixTests.cs`, `tests/Domain.UnitTests/IdentityAccess/InvitationTests.cs`, `docs/features/identity-access/TRACEABILITY.md`, `docs/features/identity-access/TASKS.md`, `docs/features/identity-access/RUNNING-LOCALLY.md`, and this plan. These future guide changes are not part of current plan authoring.

**Fixed acceptance matrix:**

| Journey | Required observation |
|---|---|
| Personal newcomer → delivered confirmation → sign-in → own profile | One identity/Personal/owner; masked document; no raw token/document leaks |
| Existing B2B identity adds Personal → creates/joins Organization → switches | Same global identity; no automatic Personal from invitation; no permission/data union |
| Known/unknown email privacy sequence and confirmed CUIT competition | No anonymous durable-reservation oracle; one confirmed winner; safe replay |
| Device list → selected revoke → revoke others → reauthentication | Correct own sessions only; revoked cookies/proofs cannot return |
| Delivered reset → sign-in → password change | Real link and policy; old password/tokens/sessions rejected; correct current-session rotation |
| Google login → new explicit onboarding; existing-account link/unlink | Controlled real OIDC callback; no email auto-link; no last-authenticator lockout |
| Custom role → member assignment → permission removal → ownership transfer | Immediate scoped authority; stale/parallel last-admin changes refused |
| Invite → replace/resend/cancel → register/reuse → accept | Actual delivered links; one usable offer; correct roles; no duplicate membership |
| Later Platform admin and existing identity → invite/confirm/sign-in/MFA/activate | Both missing browser branches run through delivered mail and visible MFA controls, not SQL confirmation or constructed MFA URLs |
| Disable/recovery/retention/restore and deployment probes | Synthetic functional/integration evidence from 26/27; fail-closed admission and shared limits, without destructive real-data tests |

**Request/tenant boundary:** drive the same public/authorized routes users have, with real antiforgery/cookies. Fixture setup can provision isolated infrastructure and unrelated prerequisites, but cannot set the confirmation/MFA/membership transition being proved. Backend negative tests assert exact denial-audit field allowlists, cross-identity/tenant refusal, concurrent winner/replay behavior and direct `Invitation.Issue/Reissue(default)` rejection; client visibility alone is insufficient.

- [ ] **RED:** add only missing scenarios from the matrix, including later Platform administrator and existing-identity browser onboarding. Each fails on the missing user-observable step or unproved security state. Read actual delivered mail with the shared sink and `PlatformFixtures.DeliveredAsync`; use a controlled OIDC provider, never a forged final login. Close the exact denial-audit/default-hash test gaps without relying on global counts or vacuous shape assertions.
- [ ] **GREEN:** wire any missing accepted journey and narrowly fix demonstrated defects within this scope; preserve Tasks 1–16 historical records. Record application/protocol/database coverage separately from browser coverage. Replay/concurrency belongs in the real database/HTTP harness where a browser cannot prove it.
- [ ] **REFACTOR and final verification:** run the fixed commands below from the repository root after the scoped changes converge. Record exit codes, discovered/executed/passed/failed/skipped counts, warning classifications and unavailable prerequisites; never copy today's historical green counts. A build with unresolved applicable warnings or a required unavailable suite is a disclosed blocker, not a clean result.

```powershell
dotnet build CleanArchitecture.slnx -v minimal
dotnet build CleanArchitecture.slnx -c Release -v minimal
dotnet test CleanArchitecture.slnx --no-build
npm test --prefix src/Web/ClientApp
npm run lint --prefix src/Web/ClientApp
npm run build --prefix src/Web/ClientApp
git diff --check
```

For a focused failure use its actual project and test name; the five independently runnable projects are `tests/Domain.UnitTests/Domain.UnitTests.csproj`, `tests/Application.UnitTests/Application.UnitTests.csproj`, `tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj`, `tests/Application.FunctionalTests/Application.FunctionalTests.csproj`, and `tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj`. Expected: an observed initial behavioral RED where coverage was missing, then every applicable required command/suite passes with nonzero intended discovery. Do not equate `--no-build` with a fresh build unless the preceding build succeeded for the same candidate/configuration.

- [ ] **Local functional closure:** every row of the fixed matrix and the approved local controls in 18–27 has reproducible evidence; a fresh setup guide matches actual routes/configuration; no open scoped functional/security defect remains. Mark only evidenced tasks complete under the repository's actual approval policy. “Local B2B/B2C functional completion with synthetic data” is the permitted claim; planned tasks and unavailable evidence stay distinguishable.
- [ ] **Real-PII gate, separate:** the responsible owner has approved C7 purpose/access/retention/legal-hold/purge/restore contracts, key ownership is verified, and actual collection remains disabled until those gates are satisfied. Local synthetic success is sufficient to close its own scope while this gate remains explicitly blocked.
- [ ] **Production/reference gate, separate:** require accepted reference-adoption/deviation mapping, shared deployment controls, real key/mail/OIDC configuration, operator recovery authority, demonstrated restore admission/revalidation and no PII resurrection. Any unadopted mandatory external-standard control prevents a full-reference-compliance claim; any unresolved applicable deployment prerequisite prevents release. No unapproved Azure/Entra/WORM stack is installed to erase that distinction.
- [ ] **Stop:** once the fixed local acceptance scope passes and the separate blocked/approved gates are honestly recorded, hand off the result. Cosmetic preferences and unrelated improvements do not restart this plan; a new material requirement gets its own explicit scope decision, not another automatic verification/review loop.
