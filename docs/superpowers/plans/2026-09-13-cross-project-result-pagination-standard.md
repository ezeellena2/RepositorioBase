# Cross-Project Result and Offset Pagination Standard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make RepositorioBase expose one cross-project API contract for expected failures and offset pagination: internal .NET `Result` / `Result<T>` with `ApplicationError`, external RFC 9457 Problem Details for failures, and consistent `pageNumber` / `pageSize` paginated responses for list endpoints.

**Architecture:** Preserve the existing architecture instead of replacing it. Application keeps `Result` / `Result<T>`, `ApplicationError`, and pure pagination models; Infrastructure owns EF pagination helpers; Web maps internal results to endpoint-specific successes or Problem Details; the SPA reads successes and failures through shared client infrastructure rather than Identity-specific modules.

**Tech Stack:** .NET, MediatR, EF Core, ASP.NET Core Minimal APIs, RFC 9457 Problem Details, React, Material UI, i18next, NUnit/Shouldly, Vitest.

---

## Required Skills

- `C:/Users/ezequ/source/repos/RepositorioBase/.agents/skills/engineering-standards/SKILL.md`
- `C:/Users/ezequ/source/repos/RepositorioBase/.agents/skills/error-handling-standards/SKILL.md`
- `C:/Users/ezequ/source/repos/RepositorioBase/.agents/skills/localization-standards/SKILL.md`
- If changing rendered SPA pagination controls: `C:/Users/ezequ/source/repos/RepositorioBase/.agents/skills/frontend-design-standards/SKILL.md`

## Non-Negotiable Contract

- Internal expected failures use `Result` / `Result<T>` carrying exactly one `ApplicationError`.
- Unexpected infrastructure or programmer failures remain exceptions.
- HTTP never serializes `Result`, `Result<T>`, or a universal `{ success, data, error }` envelope.
- HTTP failures use the existing RFC 9457 `application/problem+json` shape with stable `code`, `traceId`, matching `status`, optional safe `detail`, validation-only `errors`, and `Retry-After` only for 429/503.
- Success responses stay endpoint-specific DTOs or bodyless statuses.
- Offset pagination response DTOs are success DTOs, not Result envelopes.

## Current Cross-Project Gaps

- Shared client error infrastructure currently lives under Identity-specific paths:
  - `src/Web/ClientApp/src/features/identity/api/problemDetails.js`
  - `src/Web/ClientApp/src/features/identity/api/apiTransport.js`
  - `src/Web/ClientApp/src/features/identity/ProblemMessage.jsx`
  - `src/Web/ClientApp/src/features/identity/problemCodes.json`
- Platform and shell code already import Identity-owned problem UI/client code.
- Many endpoints manually repeat `result.IsSuccess ? ... : problems.ToHttpResult(...)`.
- Cursor pagination (`limit`, `cursor`, `nextCursor`) must be replaced with familiar offset pagination (`pageNumber`, `pageSize`, count metadata).

---

## Task 1: Make API Problem Handling Module-Neutral in the SPA

**Files:**
- Move: `src/Web/ClientApp/src/features/identity/api/problemDetails.js` -> `src/Web/ClientApp/src/api/problemDetails.js`
- Move: `src/Web/ClientApp/src/features/identity/api/apiTransport.js` -> `src/Web/ClientApp/src/api/apiTransport.js`
- Move: `src/Web/ClientApp/src/features/identity/problemCodes.json` -> `src/Web/ClientApp/src/api/problemCodes.json`
- Move: `src/Web/ClientApp/src/features/identity/ProblemMessage.jsx` -> `src/Web/ClientApp/src/components/ProblemMessage.jsx`
- Update imports under `src/Web/ClientApp/src/**`
- Update tests currently under `src/Web/ClientApp/src/features/identity/**` as needed.

- [ ] **Step 1: Write the failing import/contract tests**

Update existing tests so the module-neutral locations are required:

```js
// src/Web/ClientApp/src/test/identityFiles.contract.test.js
expect(source).toContain('api/problemDetails.js');
expect(source).not.toContain('features/identity/api/problemDetails.js');
```

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- identityFiles.contract.test.js problemCatalogue.contract.test.js
```

Expected: FAIL while files/imports still live under `features/identity`.

- [ ] **Step 2: Move files and update imports**

Use mechanical import updates only. Preserve behavior byte-for-byte except relative import paths.

Key import direction:

```js
import { createApiTransport } from '../../api/apiTransport';
import { ProblemMessage } from '../../components/ProblemMessage';
```

- [ ] **Step 3: Update fixture catalogue path**

Update:

```js
// src/Web/ClientApp/src/test/identityServer.js
import problemCodes from '../api/problemCodes.json';
```

- [ ] **Step 4: Run focused SPA contract tests**

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- problemDetails.test.js apiTransport.test.js ProblemMessage.test.jsx problemCatalogue.contract.test.js identityFiles.contract.test.js
```

Expected: PASS.

---

## Task 2: Make Backend Problem Metadata Cross-Project Friendly

**Files:**
- Modify: `src/Web/Infrastructure/ApiProblemMetadata.cs`
- Modify: `src/Web/Infrastructure/ResultHttpExtensions.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs`

- [ ] **Step 1: Pin that internal Result never crosses HTTP**

Extend or preserve runtime assertions:

```csharp
payload.TryGetProperty("success", out _).ShouldBeFalse();
payload.TryGetProperty("data", out _).ShouldBeFalse();
payload.TryGetProperty("error", out _).ShouldBeFalse();
payload.TryGetProperty("value", out _).ShouldBeFalse();
```

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter ProblemDetailsContractTests
```

Expected: existing behavior PASS before helper changes.

- [ ] **Step 2: Add ergonomic Result mapping helpers**

Keep `ResultHttpExtensions` as the only Result-to-HTTP helper. Add small helpers only where they remove repeated endpoint code without hiding endpoint-specific success DTOs.

Suggested shape:

```csharp
public static IResult ToNoContentOrProblem(this Result result, ApiProblemDetailsMapper mapper) =>
    result.IsSuccess ? Results.NoContent() : mapper.ToHttpResult(result.Error!);

public static IResult ToAcceptedOrProblem(this Result result, ApiProblemDetailsMapper mapper) =>
    result.IsSuccess ? Results.StatusCode(StatusCodes.Status202Accepted) : mapper.ToHttpResult(result.Error!);

public static IResult ToOkOrProblem<T>(this Result<T> result, ApiProblemDetailsMapper mapper, Func<T, object> project) =>
    result.IsSuccess ? Results.Ok(project(result.Value!)) : mapper.ToHttpResult(result.Error!);
```

Do not introduce a generic success envelope.

- [ ] **Step 3: Split metadata comments, not behavior**

Keep `ApiProblemMetadata` as the current source of Web contracts for now. If it is reorganized, split by static nested groups or region comments only; do not create competing registries in this task.

- [ ] **Step 4: Run problem contract tests**

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "ProblemDetailsContractTests|OpenApiContractTests|ErrorCatalogContractTests"
```

Expected: PASS.

---

## Task 3: Update Client and Backend Catalogue Paths

**Files:**
- Modify: `tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs`
- Modify: `src/Web/ClientApp/src/features/identity/problemCatalogue.contract.test.js` or move it to `src/Web/ClientApp/src/api/problemCatalogue.contract.test.js`
- Modify: `src/Web/ClientApp/src/i18n/catalog.contract.test.js`

- [ ] **Step 1: Make OpenAPI contract read the neutral catalogue**

Change:

```csharp
RepositoryFile("src/Web/ClientApp/src/api/problemCodes.json")
```

- [ ] **Step 2: Move or update Vitest problem catalogue test**

The test must still prove:

- every API code in `problemCodes.json` has a message in `en/errors.json` and `es/errors.json`;
- client-only failure codes do not appear in the API catalogue;
- MSW `problem(status, code)` rejects undeclared pairs.

- [ ] **Step 3: Run catalogue tests**

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "OpenApiContractTests|ErrorCatalogContractTests"
cmd /c npm.cmd test --prefix src/Web/ClientApp -- problemCatalogue.contract.test.js catalog.contract.test.js
```

Expected: PASS.

---

## Task 4: Add Shared Offset Pagination Models

**Files:**
- Create: `src/Application/Common/Models/PaginationQuery.cs`
- Create: `src/Application/Common/Models/PaginatedList.cs`
- Create: `tests/Application.UnitTests/Common/Models/PaginationQueryTests.cs`
- Create: `tests/Application.UnitTests/Common/Models/PaginatedListTests.cs`

- [ ] **Step 1: Write failing `PaginationQuery` tests**

Test defaults, lower bounds, upper bound, and skip calculation.

```csharp
[Test]
public void Defaults_to_first_page_with_twenty_five_items()
{
    var query = PaginationQuery.Default;

    query.PageNumber.ShouldBe(1);
    query.PageSize.ShouldBe(25);
    query.Skip.ShouldBe(0);
}

[Test]
public void Clamps_page_size_to_one_hundred()
{
    var query = new PaginationQuery(0, 500);

    query.PageNumber.ShouldBe(1);
    query.PageSize.ShouldBe(100);
    query.Skip.ShouldBe(0);
}
```

Run:

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter PaginationQueryTests
```

Expected: FAIL because the type does not exist.

- [ ] **Step 2: Implement `PaginationQuery`**

```csharp
namespace CleanArchitecture.Application.Common.Models;

public sealed record PaginationQuery(int PageNumber = 1, int PageSize = 25)
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static PaginationQuery Default { get; } = new();

    public int PageNumber { get; } = Math.Max(DefaultPageNumber, PageNumber);

    public int PageSize { get; } = Math.Clamp(PageSize, 1, MaxPageSize);

    public int Skip => checked((PageNumber - 1) * PageSize);
}
```

- [ ] **Step 3: Write failing `PaginatedList<T>` tests**

```csharp
[Test]
public void Computes_total_pages_and_navigation_flags()
{
    var page = new PaginatedList<int>([4, 5, 6], pageNumber: 2, pageSize: 3, totalCount: 8);

    page.TotalPages.ShouldBe(3);
    page.HasPreviousPage.ShouldBeTrue();
    page.HasNextPage.ShouldBeTrue();
}
```

- [ ] **Step 4: Implement `PaginatedList<T>`**

Keep it pure Application code; no EF dependency.

```csharp
namespace CleanArchitecture.Application.Common.Models;

public sealed record PaginatedList<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1 && TotalPages > 0;

    public bool HasNextPage => PageNumber < TotalPages;
}
```

- [ ] **Step 5: Run unit tests**

Run:

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "PaginationQueryTests|PaginatedListTests"
```

Expected: PASS.

---

## Task 5: Add EF Pagination Helper in Infrastructure

**Files:**
- Create: `src/Infrastructure/Data/Pagination/PaginationExtensions.cs`
- Add/modify focused infrastructure tests if an existing suitable test project pattern is available.

- [ ] **Step 1: Implement infrastructure-owned EF helper**

Application must not depend on EF Core. Put `IQueryable` execution in Infrastructure.

```csharp
namespace CleanArchitecture.Infrastructure.Data.Pagination;

public static class PaginationExtensions
{
    public static async Task<PaginatedList<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> query,
        PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<T>(items, pagination.PageNumber, pagination.PageSize, totalCount);
    }
}
```

- [ ] **Step 2: Keep ordering outside the helper**

Callers must apply deterministic `OrderBy(...)` before the helper. Do not make the helper guess ordering.

---

## Task 6: Migrate Application Query Contracts

**Files:**
- Modify: `src/Application/IdentityAccess/Platform/Queries/PlatformDirectories.cs`
- Modify: `src/Application/IdentityAccess/Roles/RoleRequests.cs`
- Modify: `src/Application/IdentityAccess/Members/MembershipRequests.cs`
- Modify: related interfaces:
  - `src/Application/IdentityAccess/Roles/IRoleAdministrationStore.cs`
  - `src/Application/IdentityAccess/Members/IMembershipAdministrationStore.cs`

- [ ] **Step 1: Replace cursor query DTOs with `PaginationQuery`**

Examples:

```csharp
public sealed record ListRolesQuery(TenantId TenantId, PaginationQuery Pagination)
    : IRequest<Result<PaginatedList<RoleSummary>>>;
```

Platform directory queries should follow the same model.

- [ ] **Step 2: Delete obsolete cursor types only after all callers compile**

Remove or migrate:

- `PlatformDirectoryQuery`
- `PlatformDirectoryPage`
- local cursor value types/helpers
- `limit`, `cursor`, `nextCursor` response semantics

---

## Task 7: Migrate Stores and Readers

**Files:**
- Modify: `src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs`
- Modify: `src/Infrastructure/IdentityAccess/RoleAdministrationStore.cs`
- Modify: `src/Infrastructure/IdentityAccess/MembershipAdministrationStore.cs`

- [ ] **Step 1: Replace cursor reads**

Replace:

```csharp
Take(limit + 1)
```

and cursor decoding with:

```csharp
var page = await query
    .OrderBy(...)
    .ToPaginatedListAsync(pagination, cancellationToken);
```

- [ ] **Step 2: Preserve deterministic ordering**

Every paginated query must order by stable columns. Use tie-breakers where needed, usually `Id`.

- [ ] **Step 3: Run focused backend tests**

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformDirectoryContractTests|RoleAdministrationTests|MembershipAdministrationTests"
```

Expected: fail until Web/API response contracts are migrated, then PASS.

---

## Task 8: Migrate Web Endpoint Query Parameters and Responses

**Files:**
- Modify: `src/Web/Endpoints/Identity/RoleEndpoints.cs`
- Modify: `src/Web/Endpoints/Identity/MembershipEndpoints.cs`
- Modify: `src/Web/Endpoints/Platform/PlatformEndpoints.cs`
- Modify response contracts under `src/Web/Endpoints/**/Contracts` if present.

- [ ] **Step 1: Replace route parameters**

Replace endpoint parameters:

```csharp
int? limit, string? cursor
```

with:

```csharp
int? pageNumber, int? pageSize
```

Build:

```csharp
var pagination = new PaginationQuery(pageNumber ?? 1, pageSize ?? 25);
```

- [ ] **Step 2: Return consistent metadata**

Response DTOs must include:

- `items`
- `pageNumber`
- `pageSize`
- `totalCount`
- `totalPages`
- `hasPreviousPage`
- `hasNextPage`

Keep endpoint-specific response names if useful.

- [ ] **Step 3: Preserve Problem Details behavior**

Invalid query binding remains `400 invalid_request`. Out-of-range numeric values are clamped by `PaginationQuery`, not refused.

- [ ] **Step 4: Update OpenAPI contract tests**

OpenAPI must expose `pageNumber` and `pageSize`, not `limit` and `cursor`.

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter OpenApiContractTests
```

Expected: PASS after endpoint migration.

---

## Task 9: Add Shared Client Pagination Helper

**Files:**
- Create: `src/Web/ClientApp/src/api/pagination.js`
- Create: `src/Web/ClientApp/src/api/pagination.test.js`
- Modify: `src/Web/ClientApp/src/api/problemDetails.js` if success drift members need updating.

- [ ] **Step 1: Write failing helper tests**

```js
import { paginationMembers, paginationSearch } from './pagination';

it('builds bounded page query parameters', () => {
  expect(paginationSearch({ pageNumber: 0, pageSize: 500 }).toString())
    .toBe('pageNumber=1&pageSize=100');
});

it('exports the standard response members', () => {
  expect(paginationMembers).toEqual([
    'items',
    'pageNumber',
    'pageSize',
    'totalCount',
    'totalPages',
    'hasPreviousPage',
    'hasNextPage',
  ]);
});
```

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- pagination.test.js
```

Expected: FAIL before helper exists.

- [ ] **Step 2: Implement helper**

```js
export const paginationMembers = Object.freeze([
  'items',
  'pageNumber',
  'pageSize',
  'totalCount',
  'totalPages',
  'hasPreviousPage',
  'hasNextPage',
]);

export function boundedPage({ pageNumber = 1, pageSize = 25 } = {}) {
  return {
    pageNumber: Math.max(1, Number.parseInt(pageNumber, 10) || 1),
    pageSize: Math.min(100, Math.max(1, Number.parseInt(pageSize, 10) || 25)),
  };
}

export function paginationSearch(page) {
  const bounded = boundedPage(page);
  const search = new URLSearchParams();
  search.set('pageNumber', String(bounded.pageNumber));
  search.set('pageSize', String(bounded.pageSize));
  return search;
}
```

- [ ] **Step 3: Update success drift policing**

`readSuccess` should continue rejecting undeclared envelope-like members. Once paginated success responses explicitly declare `items` and metadata through `paginationMembers`, they are allowed only for those endpoints.

---

## Task 10: Migrate SPA Clients

**Files:**
- Modify: `src/Web/ClientApp/src/features/identity/api/identityClient.js`
- Modify: `src/Web/ClientApp/src/features/platform/api/platformClient.js`
- Modify relevant tests:
  - `src/Web/ClientApp/src/features/identity/api/identityClient.test.js`
  - `src/Web/ClientApp/src/features/platform/platformClient.test.js`
  - `src/Web/ClientApp/src/features/platform/platformDirectory.test.js`

- [ ] **Step 1: Replace `limit` / `cursor` query construction**

Use:

```js
const search = paginationSearch(page);
transport.send(`/api/...?...${search}`, { expect: paginationMembers });
```

- [ ] **Step 2: Migrate list methods**

Update:

- `identityClient.listRoles`
- `identityClient.listMembers`
- `identityClient.listTenantInvitations`
- `platformClient.listOrganizations`
- `platformClient.listIdentities`
- `platformClient.listAdministrators`
- `platformClient.listAudit`

- [ ] **Step 3: Run client tests**

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- identityClient.test.js platformClient.test.js platformDirectory.test.js pagination.test.js
```

Expected: PASS.

---

## Task 11: Migrate SPA Pagination UI

**Files:**
- Modify screens currently using “Load more” / `nextCursor`, likely under:
  - `src/Web/ClientApp/src/features/identity/roles/RolesPage.jsx`
  - `src/Web/ClientApp/src/features/identity/members/MembersPage.jsx`
  - `src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.jsx`
  - `src/Web/ClientApp/src/features/platform/PlatformPanel.jsx`
  - `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.jsx`
  - `src/Web/ClientApp/src/features/platform/retention/PlatformRetentionPage.jsx` if affected
- Modify catalogs:
  - `src/Web/ClientApp/src/i18n/locales/en/common.json`
  - `src/Web/ClientApp/src/i18n/locales/es/common.json`
  - feature catalogs only if route-specific labels are necessary.

- [ ] **Step 1: Replace cursor behavior with page state**

Use page state per read:

```js
const [page, setPage] = useState({ pageNumber: 1, pageSize: 25 });
```

- [ ] **Step 2: Use MUI pagination controls**

Prefer `TablePagination` where the screen uses tables. Preserve one interactive DOM tree, roles, labels, ids, permissions, loading/refused/errored/empty states, and existing action visibility.

- [ ] **Step 3: Localize labels**

Any new visible or accessibility text must be added to both `en` and `es` catalogs. Keep technical response members invariant.

- [ ] **Step 4: Update UI tests**

Tests must prove:

- next page sends `pageNumber + 1`;
- previous page sends `pageNumber - 1`;
- page size changes send bounded `pageSize`;
- metadata-driven disabled states work;
- existing error/refusal rendering still uses `ProblemMessage`.

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- RolesPage.test.jsx MembersPage.test.jsx PlatformPanel.test.jsx PlatformIdentitiesPage.test.jsx useRead.test.jsx
```

Expected: PASS.

---

## Task 12: Search Cleanup

**Files:**
- Entire repository.

- [ ] **Step 1: Remove cursor pagination leftovers**

Run:

```powershell
rg -n "nextCursor|cursor|limit|PlatformDirectoryQuery|PlatformDirectoryPage|OpaqueCursor|Cursor" src tests
```

Expected: no cursor-pagination leftovers. Keep unrelated uses of words only when verified not pagination.

- [ ] **Step 2: Remove Identity-specific shared error imports**

Run:

```powershell
rg -n "features/identity/api/problemDetails|features/identity/api/apiTransport|features/identity/problemCodes|features/identity/ProblemMessage" src/Web/ClientApp/src
```

Expected: no results.

- [ ] **Step 3: Verify no generic HTTP Result envelope**

Run:

```powershell
rg -n "success|succeeded|data|error|value" src/Web/Endpoints src/Web/Infrastructure tests/Application.FunctionalTests
```

Inspect results manually. Expected: no endpoint response uses a universal success/error envelope.

---

## Task 13: Full Verification

- [ ] **Step 1: Backend unit tests**

Run:

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --filter "Pagination|ResultTests|ResultContractShapeTests"
```

Expected: PASS.

- [ ] **Step 2: Backend functional contracts**

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformDirectoryContractTests|OpenApiContractTests|ProblemDetailsContractTests|ErrorCatalogContractTests|RoleAdministrationTests|MembershipAdministrationTests"
```

Expected: PASS.

- [ ] **Step 3: SPA focused tests**

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- pagination.test.js problemDetails.test.js apiTransport.test.js ProblemMessage.test.jsx identityClient.test.js platformClient.test.js useRead.test.jsx
```

Expected: PASS.

- [ ] **Step 4: SPA build**

Run:

```powershell
cmd /c npm.cmd run build --prefix src/Web/ClientApp
```

Expected: PASS.

- [ ] **Step 5: Solution build**

Run:

```powershell
dotnet build C:/Users/ezequ/source/repos/RepositorioBase/CleanArchitecture.slnx -v minimal
```

Expected: PASS.

---

## Acceptance Criteria

- No public API directory endpoint uses `cursor`, `nextCursor`, or pagination `limit`.
- Every paginated endpoint uses `pageNumber` and `pageSize`.
- Every paginated response includes `items`, `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, and `hasNextPage`.
- Page size is bounded consistently across backend and frontend.
- The internal .NET `Result` / `Result<T>` pattern remains the only expected-failure application contract.
- The public HTTP error contract remains RFC 9457 Problem Details.
- No internal `Result` shape or universal `{ success, data, error }` envelope crosses HTTP.
- Shared SPA error/problem transport and rendering no longer live under `features/identity`.
- OpenAPI, backend tests, frontend tests, and i18n catalogs agree.
- Existing authorization, neutral-response, Problem Details, localization, logging, and accessibility contracts remain unchanged.
