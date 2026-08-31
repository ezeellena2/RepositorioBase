# Identity Access Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a multitenant SaaS identity slice with safe PostgreSQL upgrades, Organization registration and confirmation, revocable sessions, active-tenant permissions, invitation onboarding, reliable email delivery, a one-time MFA-bound Platform owner bootstrap, and a reachable same-origin React client.

**Architecture:** ASP.NET Core Identity is a credential adapter. Domain/Application own lifecycle, tenants, memberships, roles, permissions, sessions, invitations, MFA, audit, and outbox rules. Platform uses the same active-tenant membership and permission evaluator, never a global bypass. PostgreSQL enforces UUID and tenant-local invariants; the BFF cookie references a revocable server session; React keeps antiforgery state only in memory.

**Tech Stack:** .NET 10, ASP.NET Core Identity, EF Core 10, PostgreSQL/Npgsql, Aspire, MediatR, FluentValidation, React 19, Vite, Vitest, MSW, NUnit, Shouldly, Reqnroll, and Playwright.

---

**Status:** In progress. Tasks 1 and 2 are complete: the template targets PostgreSQL only, the real PostgreSQL harness is active, `BaselinePostgreSql` is applied at startup with `MigrateAsync`, and destructive initialization/default identity seeding are removed. Task 3 is next.

## Review Workload Forecast

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

## Preconditions and Execution Rules

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

- [ ] **Step 1: Shape RED**

Reflect by fully qualified names for `Tenant`, `OrganizationProfile`, `TenantMembership`, `AuditEvent`, `TenantStatus.PendingConfirmation`, and `MembershipStatus.PendingConfirmation`.

Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter IdentityAccessContractShapeTests`  
Expected: FAIL at runtime because expected types/members are missing.

- [ ] **Step 2: Compile shells**

Add `BaseEntity<TId>` while keeping `BaseEntity : BaseEntity<int>`; add UUID ID structs, enums, and private-constructor shells with throwing transitions.

Run the shape command.  
Expected: PASS.

- [ ] **Step 3: Behavioral RED**

Test normalized CUIT, Organization-only profile, pending responsible membership, activation transitions, suspended/terminal rejection, authorization-version increments, and an `AuditEvent` factory that requires correlation ID and accepts only allowlisted non-secret scalar fields.

Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter "TenantLifecycleTests|OrganizationProfileTests|AuditEventTests"`  
Expected: FAIL at runtime with `NotImplementedException` or incorrect state/payload.

- [ ] **Step 4: GREEN, REFACTOR, commit**

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

- [ ] **Step 1: Metadata RED**

Inspect EF metadata by entity/property name for core entities, unique normalized email/slug/CUIT, membership uniqueness, composite tenant FKs, explicit deletes, concurrency tokens (including `TodoItem`), `Guid?` audit actors, and append-only audit mapping.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "IdentityAccessMappingTests|AuditPersistenceTests"`  
Expected: FAIL at runtime because entities/constraints are absent and actor IDs are strings.

- [ ] **Step 2: GREEN - complete Guid conversion and persistence**

Use `IdentityUser<Guid>`, `IdentityRole<Guid>`, and `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. Change `IUser.Id`, every `IIdentityService` input/result, behavior caller, `CurrentUser`, functional helper/mock, logger test, `BaseAuditableEntity.CreatedBy/LastModifiedBy`, interceptor assignment, and Todo audit assertion to `Guid`/`Guid?`. The Task 2 initializer must already contain no `RoleManager`; no integer-key or non-generic Identity role survives.

Map `OrganizationProfile` with a normalized unique CUIT; expose every core `DbSet` through `IApplicationDbContext`; configure composite tenant FKs and deletes. `AuditWriter` creates correlation-bearing allowlisted rows. The interceptor rejects tracked update/delete; the migration adds a PostgreSQL trigger rejecting raw SQL update/delete.

```powershell
rg -n "IdentityUser(?!<)|IdentityRole(?!<)|Task<\(Result Result, string UserId\)>|(GetUserNameAsync|IsInRoleAsync|AuthorizeAsync|DeleteUserAsync)\(string|string\? (CreatedBy|LastModifiedBy)" src tests --pcre2 --glob "*.cs"
dotnet build CleanArchitecture.slnx -v minimal
```

Expected: the scan returns no stale declarations; build PASS.

- [ ] **Step 3: Create the second migration**

```powershell
dotnet ef migrations add IdentityAccess --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet ef migrations script BaselinePostgreSql IdentityAccess --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj
```

Review conversion of baseline string Identity/audit actor IDs to UUID using validated explicit casts, then safely recreate affected PK/FK/index constraints. This is the second migration, never “initial.”

- [ ] **Step 4: RED/GREEN migration and audit proof**

`MigrationUpgradeTests` migrates (a) empty -> latest and (b) `BaselinePostgreSql` -> latest after inserting a Todo sentinel and parseable GUID-string Identity row. Assert sentinel preservation, UUID columns, core constraints, no pending migrations, and raw/tracked `AuditEvent` update/delete rejection. `ConcurrencyTests` uses two real PostgreSQL DbContexts reading one `TodoItem`; the first `UpdateTodoItemDetailCommand` write persists and the stale second conditional write fails.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "IdentityAccessMappingTests|AuditPersistenceTests|ConcurrencyTests|MigrationUpgradeTests"`
Expected initial RED: missing mapping/migration/append-only runtime assertions; after GREEN: both upgrade paths PASS.

- [ ] **Step 5: REFACTOR and commit**

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

- [ ] **Step 1: Shape RED, then shells**

Reflect for all five domain types and evaluator.  
Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter IdentityAccessContractShapeTests`  
Expected RED: runtime missing-type assertion. Add only shells; rerun PASS.

- [ ] **Step 2: Behavioral RED**

Test immutable `resource.action` codes, including distinct `platform.admins.read` and `platform.admins.manage`; system-role protection, normalized tenant-local names, allowed tenant types, one active membership as evaluator input, cross-tenant role/permission rejection, authorization-version increments, and exact `membership.changed`/`role.changed` audit events with correlation and allowlisted payload.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter RolePermissionTests
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter RoleMembershipAuditTests
```

Expected: FAIL at runtime on shell behavior/missing audit rows.

- [ ] **Step 3: GREEN - persistence and incremental migration**

Implement catalog synchronization that seeds `platform.admins.read` separately from `platform.admins.manage`, evaluator over explicit identity/tenant inputs, composite membership-role/role-permission FKs, version updates, and transactional audits. Do not add `UserSession`.

```powershell
dotnet ef migrations add TenantAuthorization --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "RolePermissionMappingTests|MigrationUpgradeTests"
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter RolePermissionTests
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter RoleMembershipAuditTests
```

Expected: empty-to-latest and baseline-to-latest preserve the sentinel, report no pending migrations, and all focused tests PASS.

- [ ] **Step 4: REFACTOR and commit**

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

- [ ] **Step 1: Compile-safe shape RED**

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

- [ ] **Step 2: Add shells and migrate current Result atomically**

Add the marker/exception/error/Result/Web shells. `ApplicationErrorCategory` contains only expected mappings (`Validation`, `Authentication`, `Authorization`, `NotFound`, `Conflict`, `RateLimited`); there is no `Unexpected` category. `ApplicationError` carries stable code/category, optional safe detail, and validation fields. `Result` and `Result<T>` carry success/value or one typed error.

In the same compile-safe change, replace current `Result.Errors` use: `IIdentityService.CreateUserAsync` returns `Result<Guid>` after Task 4's Guid conversion; `IdentityService`, `IdentityResultExtensions`, and `tests/Application.FunctionalTests/Infrastructure/TestApp.cs` use stable IdentityAccess errors rather than provider descriptions. Expose a factory-owned `HttpClient` from `FunctionalTestSetup`.

```powershell
dotnet build CleanArchitecture.slnx -v minimal
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "RequestAuthorizationMetadataTests|ResultContractShapeTests"
```

Expected: build and shape tests PASS; behavior remains deliberately unimplemented.

- [ ] **Step 3: Behavioral RED - authorization and Result semantics**

Test unmarked rejection before handler, invalid dual marking, anonymous public request, `401` invalid identity, `403` missing permission, suspended tenant/membership, one-membership isolation, cross-tenant `404`, and header/route spoof rejection. The architecture guard scans every `IRequest` in Application so present and future requests have exactly one marker; the inventory test proves the nine existing request classifications. Use a fake validated `ICurrentTenant`; its persisted-session adapter belongs to IA-007.

Test a denied mutation whose business transaction rolls back still commits one `authorization.denied` event through `SecurityDenialAuditWriter` using a separate DbContext/transaction. Its allowlist contains correlation ID, actor/session/tenant IDs, permission code, outcome, and timestamp only—no resource value, email, token, cookie, or provider text.

`ResultTests` proves typed success/value and expected failure code/category, prevents success-with-error/failure-with-value, and confirms no `Unexpected` result category.

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "ResultTests|RequestAuthorizationMetadataTests|PermissionAuthorizationBehaviourTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter PermissionMatrixTests
```

Expected: runtime assertions fail on shell Result behavior, authorization fall-through, or missing denial audit.

- [ ] **Step 4: Behavioral RED - runtime and OpenAPI**

`ProblemDetailsContractTests`, through `FunctionalTestSetup.HttpClient`, cover semantic `200` DTO, existing `201` plus `Location`, empty `204`, and runtime `400/401/403/404/409/500`. Map the Task 4 stale `UpdateTodoItemDetailCommand` write to typed `Conflict` code `todo_item_concurrency_conflict`; its endpoint is RFC 9457 `409` with that stable code and opaque `traceId`. Every failure must be `application/problem+json` with matching status, stable `code`, opaque `traceId`, safe optional `detail`, validation-only field-indexed `errors`, and no stack, exception, provider, PII, or secret data. The generic `500` comes only from an unexpected exception. Assert neither internal Result fields nor universal `success/data/error` fields appear.

`OpenApiContractTests` rejects missing/mismatched success schemas, statuses, required headers, supported error statuses, Problem Details schema, or endpoint error codes. IA-006 later adds neutral `202`; IA-007 adds normalized `429` plus `Retry-After`; IA-008 adds identity `201 Location`/`200` evidence to these same suites.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: FAIL at runtime because current responses omit `code`/`traceId`, unexpected failures fall through, media types/metadata are incomplete, or internal/universal shapes leak.

- [ ] **Step 5: GREEN - shared mapping, writer, and metadata**

Enforce exactly one of `IPublicRequest` or `[Authorize]`; remove role-name authorization; persist denial audit independently. Map expected Result categories to HTTP only in `ResultHttpExtensions`. `ApiProblemDetailsMapper` and `IProblemDetailsService` own RFC 9457 output. `ProblemDetailsExceptionHandler` uses them for known exceptions and a redacted generic `500`; `ApiAuthorizationMiddlewareResultHandler` uses the same writer for generated `401/403`. `ApiProblemMetadata` and `ApiExceptionOperationTransformer` publish endpoint-specific status/schema/header/code contracts. Never serialize Result.

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "ResultTests|RequestAuthorizationMetadataTests|PermissionAuthorizationBehaviourTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PermissionMatrixTests|ProblemDetailsContractTests|OpenApiContractTests"
dotnet build CleanArchitecture.slnx -v minimal
```

Expected: PASS; `401/403` and safe `500` use the shared writer. IA-REQ-030 and IA-REQ-038 remain owned only by IA-005.

- [ ] **Step 6: REFACTOR and commit**

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

- [ ] **Step 1: Shape RED, then shells**

Reflect for both requests, `IRegistrationIdempotencyStore`/durable `RegistrationSubmission`, outbox types and exact routes `GET /api/identity/antiforgery`, `POST /api/identity/organizations/register`, and `POST /api/identity/confirm-email`. Assert `OutboxMessage.AttemptCount`, `NextAttemptAt`, `FailureCode`; and `OutboxSecret.ExpiresAt`, terminal state/reason, ciphertext, receipt/evidence.

Run: `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter RegistrationApplicationShapeTests`  
Expected RED: runtime missing-type/member/route assertion. Add request/domain/endpoint shells; public requests implement `IPublicRequest`; rerun PASS.

- [ ] **Step 2: Behavioral RED**

`RegistrationIdempotencyTests` first prove the Application service computes one canonical key from normalized caller scope and normalized registration intent, atomically claims a durable submission before effects, and returns its recorded neutral result; storage uniqueness coordinates claims but does not decide business idempotency. `RegisterOrganizationTests` prove sequential replay and contract-appropriate concurrent replay of equivalent anonymous registration each return the same bodyless `202` and leave exactly one organization, responsible membership, outbox-message/secret set, and audit-event set. They preserve true branches: existing identity remains neutral with no creation, authenticated mismatched email rejects, and invalid/revoked supplied session is `401`, never anonymous. Until IA-007 provides persisted sessions, the production optional-session adapter fails closed on any supplied cookie while functional tests inject a trusted adapter for the authenticated branch.

Confirmation tests cover atomic activation of identity/tenant/membership, injected rollback, idempotent replay, suspended/terminal rejection, and exact `organization.registration.requested`/`identity.confirmed` audit events. Outbox tests assert confirmation intent shares the transaction, payload has no token, and encrypted secret expires.

Before GREEN, extend shared contract tests: antiforgery is `200` with its endpoint DTO; registration is neutral bodyless `202`; confirmation is bodyless `204`; supported `400/401/409/500` responses use declared stable codes and Problem Details; runtime and OpenAPI contain no Result or universal envelope.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RegisterOrganizationTests|ConfirmEmailTests|ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: runtime shell/state/audit or status/schema/header drift failures.

- [ ] **Step 3: GREEN - routes, CSRF, migration**

Configure antiforgery in Web DI and middleware in `Program.cs`. `GET /api/identity/antiforgery` emits the Secure, HttpOnly, SameSite=Lax, Path=/, host-only `__Host-XSRF-TOKEN` and returns `AntiforgeryResponse` with `Cache-Control: no-store`. POST routes require exact origin and `X-CSRF-TOKEN`. Generate 32-byte tokens; persist only versioned hash plus encrypted expiring envelope. `OutboxSecret` retains evidence after ciphertext clearing. Apply `ApiProblemMetadata` with exact statuses/codes; map handler Results through the shared Web boundary.

```powershell
dotnet ef migrations add RegistrationMessaging --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "RegisterOrganizationTests|ConfirmEmailTests|ProblemDetailsContractTests|OpenApiContractTests"
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "AntiforgeryTests|MigrationUpgradeTests"
```

Expected: focused tests and both migration paths PASS with no pending migrations.

- [ ] **Step 4: REFACTOR and commit**

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

- [ ] **Step 1: Shape RED, then shells**

Reflect for `UserSession`, commands, `CurrentTenant`, and exact routes: `POST /api/identity/sessions`, `DELETE /api/identity/sessions/current`, `GET /api/identity/context`, `PUT /api/identity/context/tenant`.

Run: `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter IdentitySessionShapeTests`  
Expected RED: runtime missing-type/route assertion. Add shells with exact `IPublicRequest`/`[Authorize]`; rerun PASS.

- [ ] **Step 2: Behavioral RED - session and tenant**

Test idle/absolute expiry, revoke transition, neutral invalid credentials, confirmed/active account, cookie flags, CSRF/origin, and trusted optional-session registration. Exactly one active membership sets `ActiveTenantId`; zero/multiple leaves null. Selection validates active membership and persists it; revoked/expired session is `401`; headers cannot override it. Emit exact secret-free `signin.succeeded`, `signin.failed`, `session.created`, and `session.revoked` audit events.

Before GREEN, extend shared contract tests: create/revoke session are bodyless `204`; context GET and tenant selection are `200 IdentityContextResponse`; `400/401/403/404/409/500` use declared codes and the shared Problem Details writer; OpenAPI matches runtime.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "SessionTests|ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: runtime state, audit, Problem Details, or OpenAPI drift assertions.

- [ ] **Step 3: RED - exact Identity and transport controls**

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

- [ ] **Step 4: GREEN - options, chained partitions, cookie session**

Configure exact `PasswordOptions`, `SignInOptions`, and `LockoutOptions` in `src/Infrastructure/DependencyInjection.cs`. In Web DI, register named `identity-login` as chained IP and normalized-account fixed-window partitions backed by injectable time. Middleware buffers only the login JSON body, normalizes the email, stores an opaque SHA-256 partition key, restores the body, and never logs IP/email/key. In `Program.cs`, run key extraction before `UseRateLimiter`, apply the named policy only to POST sessions, and write rejected leases through `IProblemDetailsService` with `Retry-After`. Transport throttles and Identity lockout remain distinct; IA-007 owns this `429` normalization.

Cookie `__Host-ia-auth` contains only user/session IDs. Validate the persisted row on every request. Rotate session ID and antiforgery on sign-in; revoke/delete/rotate on sign-out. Tenant selection does not rotate antiforgery. Remove `AllowAnyOrigin`, `MapIdentityApi<ApplicationUser>()`, and uncontracted Identity endpoints.

```powershell
dotnet ef migrations add UserSessions --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "IdentityOptionsTests|SessionCookieTests|MigrationUpgradeTests"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "SessionTests|ProblemDetailsContractTests|OpenApiContractTests"
```

Expected: all focused tests and both migration paths PASS; no pending migrations.

- [ ] **Step 5: REFACTOR and commit**

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

- [ ] **Step 1: Shape RED, then shells**

Reflect for Invitation types, expiry/state/token-hash members, and tenant-bearing role association.  
Run: `dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter IdentityAccessContractShapeTests`  
Expected RED: runtime missing-type/member assertion. Add shells; rerun PASS.

- [ ] **Step 2: Behavioral RED**

Test Organization-only recipient, normalized email, same-tenant initial roles, expiry, cancel, one-shot accept, reissue invalidation, replay idempotency, and concurrency.

```powershell
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter InvitationTests
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter InvitationMappingTests
```

Expected: runtime shell/constraint failures.

- [ ] **Step 3: GREEN - mapping and migration**

Persist only token hash, concurrency token, explicit deletes, and composite tenant FKs. Add `Invitations`; rerun empty/latest and baseline/latest tests.

```powershell
dotnet ef migrations add Invitations --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "InvitationMappingTests|MigrationUpgradeTests"
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --filter InvitationTests
```

Expected: PASS with sentinel preserved and no pending migrations.

- [ ] **Step 4: REFACTOR and commit**

```bash
git add src tests
git commit -m "feat: model secure invitations"
```

## Task 10: Orchestrate invitation onboarding through exact routes

**Requirements:** IA-REQ-014..018, IA-REQ-026, IA-REQ-027, IA-REQ-029; applies IA-REQ-038  
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

- [ ] **Step 1: Shape RED, then shells**

Reflect for requests and exact routes: `POST /api/tenants/{tenantId}/invitations`, `POST /api/invitations/register`, and `POST /api/invitations/accept`. Assert no preview or `/api/identity/invitations/**` route.

Run: `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter InvitationApplicationShapeTests`  
Expected RED: runtime missing-type/route assertion. Add shells; public registration and authenticated acceptance carry the correct marker; rerun PASS.

- [ ] **Step 2: Behavioral RED**

Test `members.invite`, confirmed inviter, Organization-only, route `tenantId == ActiveTenantId` without establishing context, same-tenant roles, and atomic hash/outbox/secret creation. New invitee registration creates unconfirmed identity plus confirmation intent but no membership; existing identity ignores credential input and receives a generic notice. Only a confirmed, authenticated, matching email accepts once. Emit exact `invitation.issued` and `invitation.accepted` audit rows.

Before GREEN, extend shared contract tests: issue is `201 InvitationCreatedResponse` with required `Location`; invitee registration is neutral bodyless `202`; acceptance is idempotent `200 InvitationAcceptanceResponse`; declared `400/401/403/404/409/500` codes/shapes match runtime/OpenAPI and never expose Result or a universal envelope.

Run: `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "InvitationTests|InvitationAuditTests|ProblemDetailsContractTests|OpenApiContractTests"`  
Expected: runtime shell, tenant, audit, status/header/schema, or drift failures.

- [ ] **Step 3: GREEN, REFACTOR, commit**

Tokens arrive only in JSON bodies; email links hold them in browser fragments. Persist raw token only as encrypted expiring `OutboxSecret`; never place it in outbox/audit/logs. Map typed handler Results only at Web, attach exact `ApiProblemMetadata`, and return each declared endpoint DTO/status/header.

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "InvitationTests|InvitationAuditTests|ProblemDetailsContractTests|OpenApiContractTests"
git add src tests
git commit -m "feat: add invitation onboarding"
```

Expected: PASS.

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

- [ ] **Step 1: Shape RED, then shells**

Reflect for reader, dispatcher, handlers, adapter, worker, `TimeProvider`, and all retry/terminal members established in Task 7.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter OutboxInfrastructureShapeTests`  
Expected RED: runtime missing-type/member assertion. Add shells; rerun PASS.

- [ ] **Step 2: Behavioral RED**

With fake time, test due-only claiming, `FOR UPDATE SKIP LOCKED` plus CAS generation, lease release, and message-ID idempotency. A transient failure increments `AttemptCount`, stores only an allowlisted redacted `FailureCode`, clears the lease, and sets `NextAttemptAt = now + min(30 seconds * 2^(attempt-1), 30 minutes)`; optional deterministic 0–20% message-ID jitter is disabled in tests. Before due time no claim occurs; after advancing time exactly one claim occurs. Eight exhausted attempts become permanent.

Also test acknowledged send followed by local-update failure, expired envelope, permanent provider failure, and success. Success atomically marks message delivered and clears ciphertext while retaining `OutboxSecret` status, reason, terminal timestamp, opaque provider receipt/hash evidence, expiry, and row. Expired/permanent cases clear ciphertext and retain evidence. Raw token and provider exception text never persist.

Run: `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter OutboxDeliveryTests`  
Expected: runtime failures for early claims, duplicate delivery, missing delay/evidence, leaked provider text, or retained ciphertext.

- [ ] **Step 3: GREEN, REFACTOR, commit**

Inject `TimeProvider`; decrypt only in memory; send with `OutboxMessage.Id` as idempotency key; reconcile adapter receipt before a retry. Production fails closed without wrapping-key/email configuration.

```powershell
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "OutboxInfrastructureShapeTests|OutboxDeliveryTests"
dotnet build CleanArchitecture.slnx -v minimal
git add src tests CleanArchitecture.slnx
git commit -m "feat: deliver identity outbox reliably"
```

Expected: PASS.

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

- [ ] **Step 1: Install MSW/test tooling and file-shape RED**

Install Vitest, jsdom, Testing Library, and MSW; configure `listen/resetHandlers/close`. A Node filesystem test checks modules/root files without importing missing modules.

Run: `npm test --prefix src/Web/ClientApp -- identityFiles.contract.test.js`  
Expected: FAIL at runtime because feature files are missing.

- [ ] **Step 2: Add shells and route/root behavioral RED**

Add importable shells. `identityClient.js` is the sole fetch/response boundary; `problemDetails.js` validates only the external RFC 9457 shape and never imports or models internal Result. Mount `IdentityProvider` in `App.jsx`; define public `/login`, `/organizations/register`, `/invitations/register`, `/invitations/accept`; protected `/identity`, `/organizations/select`, and `/members/invite`; wrap protected elements with the replacement `ProtectedRoute`. Update `Layout`/`NavMenu` navigation and sign-out. Remove all legacy provider/page imports.

MSW/root tests render `App` with `MemoryRouter` and prove every route can be navigated/rendered, unauthenticated protected routes return to login with a safe return URL, signed-in navigation exposes tenant/invite actions, and logout clears context. Parser tests cover endpoint DTOs and `400/401/403/404/409/429/500` Problem Details, `Retry-After`, validation-only errors, safe diagnostics, and malformed/media-type drift. They reject internal Result and universal success/data/error envelopes; identity endpoints add no pagination wrapper.

```powershell
npm test --prefix src/Web/ClientApp -- App.test.jsx AppRoutes.test.jsx IdentityProvider.test.jsx problemDetails.test.js
```

Expected: runtime route/header/state assertions fail against shells.

- [ ] **Step 3: GREEN - exact HTTP and antiforgery lifecycle**

Use exact SPEC API routes and `credentials: "same-origin"`. Parse every declared success DTO/status/header and every non-success through the sole boundary. Bootstrap `GET /api/identity/antiforgery` after initial load/page reload and successful sign-in/sign-out. On stable `antiforgery_validation_failed`, fetch a fresh pair and require mutation retry. Do not use `X-CSRF-Refresh`. Successful tenant selection keeps the pair because the server does not rotate it. Send `X-CSRF-TOKEN` only on mutations; keep request/invitation tokens in memory; strip invitation fragments with `history.replaceState`.

Keep Vite's same-origin `/api` proxy and add a test/config assertion that no absolute API origin or open CORS fallback exists. Delete legacy files and update every import.

```powershell
rg -n "AuthContext|components/api-authorization/(LoginPage|RegisterPage)|X-CSRF-Refresh|/api/identity/invitations|preview|result\.(succeeded|errors)|response\.(success|data|error)" src/Web/ClientApp/src
npm test --prefix src/Web/ClientApp
npm run lint --prefix src/Web/ClientApp
npm run build --prefix src/Web/ClientApp
```

Expected: scan returns no legacy contract/import; all commands PASS and every journey is reachable before Playwright.

- [ ] **Step 4: REFACTOR and commit**

```bash
git add src/Web/ClientApp
git commit -m "feat: add reachable identity experience"
```

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

- [ ] **Step 1: RED - executable journeys**

Cover pending registration -> confirmation activation -> sign-in; zero/one/multiple membership defaults; authenticated second Organization; switching; independent IP/account throttles and lockout recovery; new invitee token-aware registration -> confirmation -> sign-in -> one-shot accept; existing identity acceptance; tenant isolation; revoked session; baseline-to-latest restart; and runtime/OpenAPI/React contract-drift rejection.

Run: `dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --filter IdentityAccess`  
Expected: runtime scenario assertion failures until browser wiring is complete.

- [ ] **Step 2: GREEN - architecture and full verification**

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

- [ ] **Step 3: REFACTOR traceability and commit**

Record exact pre-Platform evidence without transferring normative ownership to IA-009; move only IA-002..IA-008 to `Review`. Keep IA-009 `Blocked`: its final acceptance and any move to `Review` wait for Tasks 14–16 and verified Platform evidence.

```bash
git add tests docs/features/identity-access
git commit -m "test: verify identity access foundation"
```

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

- [ ] **Step 1: Platform invitation persistence RED, migration, then GREEN**

Extend `PlatformMfaShapeTests` and `PlatformMfaMappingTests` to first require `PlatformAdminInvitation`, status, hash/expiry/delivery state, DbSet/configuration, and its migration. Run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformMfaShapeTests` and `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter PlatformMfaMappingTests`; expect runtime missing-type/member RED. Add only invitation shells, run `dotnet ef migrations add PlatformAdminInvitation --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations`, then run `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "PlatformMfaMappingTests|MigrationUpgradeTests"`; expect PASS with empty/latest and baseline/latest upgrades before any MFA RED.

- [ ] **Step 2: Platform invitation onboarding RED, then GREEN**

After Step 1 PASS, make `PlatformInvitationApplicationShapeTests` require `RegisterPlatformInvitee`/validator, `ConfirmPlatformInvitee`/validator, `POST /api/platform/invitations/register`, and `/confirm`; its registration DTO carries invitation token plus password. Run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformInvitationApplicationShapeTests` and expect route/type RED. The public token-aware registration request validates the submitted password against `PasswordOptions` only when it creates a missing matching identity; it uses Identity hashing, atomically binds `PlatformAdminInvitation`, and creates confirmation outbox/secret. For an existing matching identity, it ignores supplied credentials and returns only generic sign-in/confirmation behavior. Confirmation consumes its email and invitation tokens, marks the identity confirmed, and still creates no Platform membership. Run `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformInvitationOnboardingTests|ProblemDetailsContractTests|OpenApiContractTests"`; expect behavioral RED for password creation, existing-identity non-takeover, neutral `202`/`204`, outbox, and no early activation. Implement by generalizing existing credential/confirmation/outbox mechanics without treating Organization `Invitation` as `PlatformAdminInvitation` or generating a default password; rerun both commands and expect PASS.

- [ ] **Step 3: MFA RED, migration, then GREEN after confirmed onboarding**

Only after Step 2 PASS, reflect for Platform tenant type, one-enrollment-per-user, recovery-code/encrypted-secret members, and no global administrator member; run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformMfaShapeTests` and `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformMfaTests|PlatformMfaAuthenticationTests"`; expect RED. MFA tests require a registered, confirmed, signed-in matching invitee and bound token, reject theft/mismatch/anonymous/token-only/invalid-session/pre-activation access, and prove no membership activation before TOTP, recovery acknowledgement, and MFA session. Persist encrypted TOTP/hashed codes, run `dotnet ef migrations add PlatformMfa --project src/Infrastructure/Infrastructure.csproj --startup-project src/Web/Web.csproj --output-dir Data/Migrations`, then run `dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj --filter "PlatformMfaMappingTests|MigrationUpgradeTests"` and the two prior commands; expect PASS with no plaintext secret and ordered `PlatformAdminInvitation` then `PlatformMfa` migrations.

- [ ] **Step 4: REFACTOR and commit**

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

- [ ] **Step 1: Compile-safe shape RED**

Reflect for the bootstrap/options, `RecoverPendingPlatformOwnerInvitation` handler/validator/rate-limit port, protected `platform.*` requests, exact Platform routes including `POST /api/platform/bootstrap/recover` and `GET /api/platform/admins`, typed `PlatformDirectoryQuery`/`PlatformDirectoryPage`/`PlatformAdministratorProjection`, and allowlisted DTOs. Run `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformApplicationShapeTests`; expect runtime missing-type/route/classification RED. Assert active-Platform requests are `[Authorize]` with Platform tenant required; recovery is bodyless `IPublicRequest` with antiforgery/rate-limit/server-derived recipient only, no identity/email/token input, activation, or elevation; and no endpoint is named impersonate/delete/context-bypass.

- [ ] **Step 2: Behavioral RED**

With real PostgreSQL, run bootstrap twice with one configured email: only the first missing-Platform invocation atomically creates the singleton tenant, system owner role, pending invitation, outbox message/secret, and audit event. `POST /api/platform/bootstrap/recover` dispatches `RecoverPendingPlatformOwnerInvitation` as a bodyless same-origin antiforgery request with no identity, email, token, or replacement-recipient input. Its validator permits only expired or permanently delivery-failed pending invitations and derives the unchanged configured/pending recipient; its rate-limit port keys the pending invitation and transport source. The handler conditionally claims that invitation in one transaction, invalidates the old token, writes one replacement token/outbox/audit effect, and makes equivalent sequential/concurrent requests idempotent without membership activation/elevation. Test cold-start with no `ApplicationUser`, active/used invitation, unknown/ineligible opaque state, configuration change, and race branches: each valid state-obscuring outcome is neutral `202`, and only one current invitation/effect set survives. Test malformed/missing antiforgery separately as RFC 9457 `400` code `antiforgery_validation_failed`; test exhausted recovery limit separately as RFC 9457 `429` code `rate_limit_exceeded` plus `Retry-After`. Missing configuration creates nothing; completed first activation permanently closes recovery.

Test normal active-Platform membership plus distinct `platform.admins.read`/`platform.admins.manage`, `platform.tenants.manage`, `platform.organizations.read`, `platform.identities.read`, and `platform.audit.read`; reject stale/non-MFA/unauthorized/cross-context requests. The already-persisted `PlatformAdminInvitation` reuses the Invitation/Outbox token and delivery pattern without widening Organization invitations; invitation/revocation preserves one active owner. Two status writers prove a required allowlisted suspension reason and conditional concurrency: first Organization suspension persists; stale second returns typed `Conflict` and RFC 9457 `409` with `platform_tenant_concurrency_conflict`/`traceId`; Platform cannot be suspended/deleted. The evaluator immediately denies the suspended Organization.

`PlatformProjectionTests` proves exact fields: organization IDs/slug/type/status/timestamps/suspension metadata/version; identity/admin IDs, normalized email only with directory permission, confirmation/account/membership/MFA/owner status, and operational timestamps; audit IDs/type/time/correlation/actor-tenant IDs/outcome/reason-code only. It rejects CUIT, business rows, profile payloads, credentials, secrets, tokens, extra PII, and mutable audit payloads. `PlatformDirectoryContractTests` proves `/api/platform/organizations`, `/identities`, `/admins`, and `/audit` accept only `limit=1..100` plus opaque `cursor` and return each endpoint's typed `items`/`nextCursor` DTO; `/api/identity/*` remains unchanged. `PlatformAdministrationTests` lists administrators through the protected directory before inviting or revoking and uses only the allowlisted membership ID; it still blocks last-owner removal. Negative tests reject impersonation, destructive deletion, direct arbitrary elevation, global claims/booleans/roles, private tenant rows, credentials, secrets, raw tokens, and client-selected context. Contract tests require semantic DTOs/`202`/`204`, stable Problem Details codes, and no Result or universal envelope in runtime/OpenAPI.

Run `dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformBootstrapTests|PlatformBootstrapRecoveryTests|PlatformAdministrationTests|PlatformOperationsTests|PlatformProjectionTests|PlatformDirectoryContractTests|ProblemDetailsContractTests|OpenApiContractTests"`; expect behavioral/contract RED before handlers/endpoints. Implement the normal authority flow, then rerun that command and `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PlatformApplicationShapeTests`; expect PASS.

- [ ] **Step 3: GREEN, REFACTOR, commit**

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

- [ ] **Step 1: Shape RED, then shells**

Filesystem/route shape tests require the Platform client, token-aware public invitation pages, panel, safe DTO parser, and protected route. Run `npm test --prefix src/Web/ClientApp -- PlatformPanel.test.jsx platformClient.test.js platformDirectory.test.js PlatformInvitationPages.test.jsx`; expect missing-module/route RED. Shells must not expose an impersonation control, delete action, unrestricted identity fields, or client tenant override.

- [ ] **Step 2: Behavioral RED**

`platformClient.test.js`, `platformDirectory.test.js`, and `PlatformInvitationPages.test.jsx` prove public Platform-token registration/confirmation calls use antiforgery and neutral responses; a missing identity submits a PasswordOptions-valid password, while an existing identity's supplied credentials are ignored and cannot take over the account. After confirmation they hand off to normal password sign-in and cannot activate membership. They also assert recovery `400 antiforgery_validation_failed` and `429 rate_limit_exceeded`/`Retry-After`, while valid opaque recovery outcomes are `202`. The tests prove the client calls the protected typed administrator directory before invite/revoke, carries only the selected allowlisted membership ID into mutations, and follows only typed bounded `items`/`nextCursor` directories. MSW/React tests prove only a recent-MFA Platform context sees the panel; it renders allowlisted organization/identity/administrator/audit projections, handles typed `401/403/404/409/429` Problem Details, requires confirmation for suspension/revocation, and cannot render private data or bypass controls. Run the Step 1 command; expect behavioral RED. Browser journeys prove cold-start bootstrap → expired/failed delivery recovery with no identity → Platform token registration with submitted password/reuse without credential effect → confirmation → normal password sign-in → invitation-bound TOTP/recovery acknowledgement → activation → MFA step-up → admin listing/invitation → suspend/reactivate → audit; the later admin follows the same register/confirm/sign-in/MFA gates. Last-owner revocation and prohibited routes/actions fail.

- [ ] **Step 3: GREEN, REFACTOR, commit**

Only after Tasks 14 and 15 have PASS evidence for Platform invitation persistence/onboarding, MFA, bootstrap recovery, ordered migrations, functional/PostgreSQL, and OpenAPI contracts, route all calls through the existing typed API boundary with same-origin credentials and antiforgery. Run `npm test --prefix src/Web/ClientApp -- PlatformPanel.test.jsx platformClient.test.js platformDirectory.test.js PlatformInvitationPages.test.jsx`, `npm run lint --prefix src/Web/ClientApp`, and `npm run build --prefix src/Web/ClientApp`; expect PASS. Then run `dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --filter PlatformOperations` and `dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter IdentityAccessArchitectureTests`; expect PASS. Record traceability without changing normative ownership and only then move IA-009 from `Blocked` to `Review`.

```bash
git add src/Web/ClientApp tests docs/features/identity-access
git commit -m "test: verify platform operations"
```

## Exit Criteria

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
