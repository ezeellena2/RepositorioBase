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
- `src/Web/ClientApp/src/features/identity/api/problemDetails.js:14` lists `items` and `nextCursor` in `FORBIDDEN_SUCCESS_KEYS`. Paginated reads must declare `items` explicitly, and `nextCursor` must stay forbidden so an old server shape is reported as drift instead of being half-read.
- `ResultHttpExtensions.ToHttpResult(Result, HttpContext, mapper)` (→ `204`) and `ToHttpResult<T>(Result<T>, HttpContext, mapper, onSuccess)` already exist and the Platform directories use them. Do not add a second helper family that does the same job.
- The `checked((PageNumber - 1) * PageSize)` originally proposed in Task 4 throws `OverflowException` for `pageNumber=2147483647`. That is an unexpected `500 internal_server_error` plus an `Error` log caused only by caller input.
- `docs/features/identity-access/SPEC.md` pins the cursor contract:
  - IA-REQ-038, closing sentence: "adds no pagination envelope to identity endpoints";
  - rows `:306`–`:309`;
  - scenario `:464`–`:465`;
  - rows `:1033` and `:1037`;
  - the amendment at `:1065`.

  Approved SPECs outrank convenience, so they are amended in the same change (Task 8A).
- Five "show more" catalog keys become unused in both `en` and `es`. `npm run i18n:unused` fails while they remain:
  - `platform:organizations.more`
  - `platform:identities.more`
  - `identity:members.showMore`
  - `identity:invitations.member.showMore`
  - `identity:roles.showMore`

## Error-Handling and Localization Decisions

These decisions bind every task below. They apply `error-handling-standards` and `localization-standards` to pagination. Do not reopen them task by task.

### Errors

| # | Situation | Decision | Proven by |
| --- | --- | --- | --- |
| E1 | `pageNumber` / `pageSize` out of range (`0`, negative, `500`) | **Clamped** by `PaginationQuery`, never refused. This keeps the existing policy (`PlatformDirectories.cs:17`: "Clamped rather than refused: a limit outside the range is a client bug, not a security event"). No new code, and no `validation_failed` for these members. | Task 4, Task 8 Step 5 |
| E2 | Huge `pageNumber` (`int.MaxValue`) | Clamped to `PaginationQuery.MaxPageNumber` so `Skip` can never overflow. Answers `200`, never `500`, and writes no `Error` log. | Task 4 Step 1, Task 8 Step 5 |
| E3 | Non-integer query value (`?pageNumber=abc`) | Framework binding failure → existing `400 invalid_request`. Every list route sets `.WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code)` **and** declares `ApiProblemMetadata.InvalidRequest` in `WithApiProblemDetails` (error rule 10). `RoleEndpoints.cs:30` and `MembershipEndpoints.cs` do not declare it today. | Task 8 Step 5 (OpenAPI assertion) |
| E4 | `pageNumber` past the last page (for example, the last row on it was removed) | `200` with `items: []`, the real `totalCount` / `totalPages`, and the requested `pageNumber` echoed. Never `404`, which stays reserved for another tenant's or a missing resource. The SPA moves to the real last page (Task 11A). | Task 8 Step 5, Task 11A |
| E5 | Codes a list route may emit | No change: `401 authentication_required` / `invalid_session`, `403 permission_denied`, `404 not_found` (tenant routes), `401 recent_mfa_required` (Platform organizations and identities), `400 invalid_request`, `500 internal_server_error`. `problemCodes.json` and `errors.json` gain and lose nothing. | `ErrorCatalogContractTests`, `OpenApiContractTests`, Task 12 Step 4 |
| E6 | Tempted to add `invalid_page`, `invalid_page_size` or `invalid_cursor` | Don't. A code is born once, with its factory, contract, catalogue entry, words in every language and test. This change needs none. Removing cursors retires no code, because the server never emitted one; the stale `invalid_cursor` fixture was already removed. | Task 12 Step 4 |
| E7 | An old server shape (`{ items, nextCursor }`) or malformed metadata reaching the new client | Drift, not data. `nextCursor` stays in `FORBIDDEN_SUCCESS_KEYS`. A missing member fails `readSuccess`. Non-integer or negative metadata fails `readPage`. Each case becomes `ClientFailure('unreadable_response')`. | Tasks 9 and 10 |
| E8 | Read states on a page change | `useRead`'s four states apply per page. `loading`: the rows already on screen stay, holding the layout. `errored` (status `0`, `429`, `>=500`): previous rows stay, with `ProblemMessage` and `common:actions.tryAgain`. `refused` (any other 4xx): rows are cleared and `ProblemMessage` shows in place. A `401` session loss is ended centrally by the transport listener, never per screen. | Task 11A |
| E9 | Which page the control shows | The pagination control reads `page` / `rowsPerPage` from the last **loaded** `data.pageNumber` / `data.pageSize`, never from the requested page. A failed page change leaves the control on the page whose rows are on screen. | Task 11A |
| E10 | Fast repeated clicks | `useRead` already aborts the previous request by generation. Only the last requested page may render. | Task 11A |
| E11 | A mutation, then a refresh of the current page | Error rule 25: if the mutation succeeded and the refresh fails, the refresh is shown as a read failure. The mutation is never reported as failed. | Existing mutation tests stay green |
| E12 | Logging | Clamping and empty pages are expected outcomes, so nothing is logged at `Error`. Pagination values never appear in an `Error` record. | Task 8 Step 5 |

### Localization

| # | Situation | Decision |
| --- | --- | --- |
| L1 | `TablePagination` strings ("Rows per page:", "1–25 of 120", "Go to next page") | They come from `@mui/material/locale` through `themeFor(language)` (`docs/features/localization/PLAN.md` §5.4). `esES` already formats `from` / `to` / `count` with a number formatter and localizes `getItemAriaLabel` ("Ir a la página siguiente") and `labelRowsPerPage` ("Filas por página:"). **Do not** override `labelRowsPerPage`, `labelDisplayedRows` or `getItemAriaLabel` with catalog keys, and never concatenate them. |
| L2 | A future supported language | It needs its entry in `muiLocaleByLanguage`; the existing MUI-mapping gate enforces this. There is no pagination-specific work. |
| L3 | The obsolete "show more" keys | Delete all five from `en/*.json` and `es/*.json` in the same change that removes their last `t()` call. |
| L4 | New visible text beyond MUI | Avoid it: E4 re-requests the last page silently. If a screen truly needs copy, add the `en` source and every `supported` language from `languages.json` in the same change, using named placeholders and `{{total, number}}`. Never name a placeholder `count` unless the key has plural forms. `count` triggers i18next plural resolution: `_one` / `_other` for `en`, and `_one` / `_many` / `_other` for `es`. |
| L5 | Invariant data | `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, `hasNextPage`, OpenAPI descriptions, log text and developer `Error` messages (`pagination.js`, `problemDetails.js`) stay English and never go through `t()`. |
| L6 | Error words | They stay in `errors.json`, keyed by code. Moving `ProblemMessage` to `src/components` keeps it reading `errors:<code>`, `errors:retryAfter` and `errors:reference` through `src/i18n`. Server `detail` is never rendered. |
| L7 | Backend resources | No server-delivered text changes, so no `.resx` change. |
| L8 | Test language | SPA tests stay deterministic in `en`. One assertion in `spanish.test.jsx` proves the MUI locale reaches the pagination control. Journeys keep `en` plus the generated `es` smoke journey. |

## Test Files This Functional Change Edits

`CLAUDE.md` allows a functional change to edit tests only where they assert the new behavior, and requires listing them. Each edit below replaces a cursor or "show more" assertion with its offset equivalent. None of them may be edited to make a regression pass.

- SPA:
  - `src/Web/ClientApp/src/features/identity/roles/RolesPage.test.jsx`
  - `src/Web/ClientApp/src/features/identity/members/MembersPage.test.jsx`
  - `src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.test.jsx`
  - `src/Web/ClientApp/src/features/identity/ExternalProofResume.test.jsx`
  - `src/Web/ClientApp/src/features/identity/IdentityAccessReviewRevalidation.test.jsx`
  - `src/Web/ClientApp/src/features/identity/useRead.test.jsx`
  - `src/Web/ClientApp/src/features/identity/api/identityClient.test.js`
  - `src/Web/ClientApp/src/features/identity/api/problemDetails.test.js` (moved by Task 1)
  - `src/Web/ClientApp/src/features/platform/PlatformPanel.test.jsx`
  - `src/Web/ClientApp/src/features/platform/identities/PlatformIdentitiesPage.test.jsx`
  - `src/Web/ClientApp/src/features/platform/platformClient.test.js`
  - `src/Web/ClientApp/src/features/platform/platformDirectory.test.js`
  - `src/Web/ClientApp/src/AppRoutes.test.jsx`
  - `src/Web/ClientApp/src/i18n/presentation.test.jsx`
  - `src/Web/ClientApp/src/i18n/spanish.test.jsx` (one added assertion)
- .NET:
  - `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformDirectoryContractTests.cs`
  - `tests/Application.FunctionalTests/IdentityAccess/Members/AdministrationDirectoryRevalidationTests.cs`
  - `tests/Application.FunctionalTests/IdentityAccess/Members/IndependentDevelopmentAdministrationReviewTests.cs`
  - `tests/Application.FunctionalTests/IdentityAccess/Members/MembershipAdministrationTests.cs`
  - `tests/Application.FunctionalTests/IdentityAccess/Members/MembershipAtomicityTests.cs`
  - `tests/Application.FunctionalTests/IdentityAccess/Members/OwnershipTransferTests.cs`
  - `tests/Application.FunctionalTests/IdentityAccess/Lifecycle/ConcurrentDeactivationFloorTests.cs`
  - `tests/Application.FunctionalTests/IdentityAccess/Roles/RoleAdministrationTests.cs`
  - `tests/Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs`
  - `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs` (inspect first; edit only if its matches are pagination)
- Journeys: no page object under `tests/Web.AcceptanceTests` references "show more" or cursors. If one turns out to depend on it, stop and report instead of editing it.

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

- [ ] **Step 5: Prove the move changed no words in any language (L6)**

`ProblemMessage` must keep importing `useTranslation` from the `src/i18n` facade. From `src/components/` that path is `'../i18n'`. It must also keep resolving `errors:<code>`, `errors:retryAfter` and `errors:reference`. Do not touch any value in `errors.json`.

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- ProblemMessage.test.jsx spanish.test.jsx presentation.test.jsx catalog.contract.test.js
cmd /c npx.cmd --prefix src/Web/ClientApp eslint src/Web/ClientApp/src/components/ProblemMessage.jsx
git diff --stat -- src/Web/ClientApp/src/i18n/locales
```

Expected: tests PASS, lint clean (`i18next/no-literal-string` included), and no diff under `locales`.

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

Keep `ResultHttpExtensions` as the only Result-to-HTTP helper. It already has `ToHttpResult(Result, HttpContext, mapper)`, which answers `204`, and `ToHttpResult<T>(Result<T>, HttpContext, mapper, Func<T, IResult> onSuccess)`. The Platform directories already use them. Migrate the endpoints that repeat `result.IsSuccess ? ... : problems.ToHttpResult(result.Error!)` to the existing overloads instead of adding `ToNoContentOrProblem` or `ToOkOrProblem`:

```csharp
// src/Web/Endpoints/Identity/RoleEndpoints.cs — List, after Task 8
var result = await sender.Send(new ListRolesQuery(TenantId.From(tenantId), new PaginationQuery(pageNumber ?? PaginationQuery.DefaultPageNumber, pageSize ?? PaginationQuery.DefaultPageSize)), context.RequestAborted);
return result.ToHttpResult(context, problems, page => Results.Ok(RolePageResponse.From(page, Describe)));
```

Add exactly one new overload, and only if at least two endpoints answer a bodyless `202`:

```csharp
public static IResult ToAcceptedHttpResult(this Result result, HttpContext httpContext, ApiProblemDetailsMapper mapper)
{
    ArgumentNullException.ThrowIfNull(result);
    return result.IsSuccess
        ? Results.StatusCode(StatusCodes.Status202Accepted)
        : mapper.ToHttpResult(result.Error!);
}
```

The failure branch must keep going through `mapper.ToHttpResult(result.Error!)`, the single RFC 9457 writer (error rule 8). No helper may write a problem body, pick a status or log. Do not introduce a generic success envelope.

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

- every API code in `problemCodes.json` has a non-empty message in `errors.json` for **every** language in `languages.json` `supported` (today `en` and `es`), read from the registry rather than hard-coded, so adding a language cannot silently skip this gate;
- the four client-only failure codes (`network_unavailable`, `request_timeout`, `unreadable_response`, `client_failure`) have words in every supported language and never appear in the API catalogue;
- MSW `problem(status, code)` rejects undeclared pairs;
- pagination adds no code: the sorted key list of `problemCodes.json` is byte-identical before and after this plan (E5, E6).

```js
// src/Web/ClientApp/src/api/problemCatalogue.contract.test.js
import languages from '../i18n/languages.json';
import catalogue from './problemCodes.json';

const CLIENT_CODES = ['network_unavailable', 'request_timeout', 'unreadable_response', 'client_failure'];
const errorsFor = (language) => import.meta.glob('../i18n/locales/*/errors.json', { eager: true, import: 'default' })[`../i18n/locales/${language}/errors.json`];

describe.each(languages.supported)('problem catalogue in %s', (language) => {
  it.each([...Object.keys(catalogue), ...CLIENT_CODES])('has words for %s', (code) => {
    expect(errorsFor(language)?.[code], `${language} errors:${code}`).toEqual(expect.stringMatching(/\S/));
  });
});

it('keeps client-only codes out of the API catalogue', () => {
  expect(CLIENT_CODES.filter((code) => Object.hasOwn(catalogue, code))).toEqual([]);
});
```

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

Test defaults, lower bounds, upper bounds, and the skip calculation. Out-of-range input is clamped, never refused (E1). No input may make `Skip` throw, because an exception here becomes a `500` caused only by the caller (E2).

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

[TestCase(-5, -5, 1, 1)]
[TestCase(0, 0, 1, 1)]
[TestCase(3, 10, 3, 10)]
public void Clamps_rather_than_refuses(int pageNumber, int pageSize, int expectedNumber, int expectedSize)
{
    var query = new PaginationQuery(pageNumber, pageSize);

    query.PageNumber.ShouldBe(expectedNumber);
    query.PageSize.ShouldBe(expectedSize);
}

[Test]
public void A_huge_page_number_is_clamped_so_skip_never_overflows()
{
    var query = new PaginationQuery(int.MaxValue, int.MaxValue);

    query.PageNumber.ShouldBe(PaginationQuery.MaxPageNumber);
    query.PageSize.ShouldBe(PaginationQuery.MaxPageSize);
    Should.NotThrow(() => query.Skip).ShouldBeGreaterThanOrEqualTo(0);
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

/// <summary>
/// One offset page request. Out-of-range values are clamped rather than refused: a page outside the range is a
/// client bug, not a security event, and no input may turn into an unexpected failure (E1, E2).
/// </summary>
public sealed record PaginationQuery(int PageNumber = 1, int PageSize = 25)
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>The largest page whose <see cref="Skip"/> still fits in an <see cref="int"/> at any page size.</summary>
    public const int MaxPageNumber = int.MaxValue / MaxPageSize;

    public static PaginationQuery Default { get; } = new();

    public int PageNumber { get; } = Math.Clamp(PageNumber, DefaultPageNumber, MaxPageNumber);

    public int PageSize { get; } = Math.Clamp(PageSize, 1, MaxPageSize);

    // `checked` stays as a tripwire: with both clamps above it cannot fire, and a regression becomes loud.
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
    : IRequest<Result<PaginatedList<RoleView>>>;
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

Keep endpoint-specific response names (`RolePageResponse`, `PlatformOrganizationDirectoryResponse`, …) and give each one a static factory, so the endpoint lambda passed to `ToHttpResult<T>` stays one line:

```csharp
public sealed record RolePageResponse(
    RoleResponse[] Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static RolePageResponse From(PaginatedList<RoleView> page, Func<RoleView, RoleResponse> describe) =>
        new(page.Items.Select(describe).ToArray(), page.PageNumber, page.PageSize, page.TotalCount,
            page.TotalPages, page.HasPreviousPage, page.HasNextPage);
}
```

- [ ] **Step 3: Preserve Problem Details behavior (E1–E5)**

Invalid query binding remains `400 invalid_request`. Out-of-range numeric values are clamped by `PaginationQuery`, not refused. A page past the end is `200` with `items: []`.

Every list route declares the binding code it sets (error rule 10). Add `ApiProblemMetadata.InvalidRequest` wherever it is missing. The Platform routes get it through their shared `Directory` contract array; the tenant routes get it inline:

```csharp
group.MapGet("/{tenantId:guid}/roles", List)
    .RequireAuthorization()
    .Produces<RolePageResponse>(StatusCodes.Status200OK)
    .WithApiProblemDetails(ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError)
    .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRequest.Code);
```

Do not add, remove or rename any other code. `invalid_request` is already in `problemCodes.json` and `errors.json`, so the catalogue does not change.

- [ ] **Step 4: Update OpenAPI contract tests**

OpenAPI must expose `pageNumber` and `pageSize`, not `limit` and `cursor`. Parameter names and descriptions are invariant English (L5).

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter OpenApiContractTests
```

Expected: PASS after endpoint migration.

- [ ] **Step 5: Pin the pagination error contract**

Add these tests to `tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformDirectoryContractTests.cs`. They reuse the file's own `OrganizationsAsync(count)` and `PathsAsync()` helpers, with the same arrangement as the existing clamp test at `:79`–`:95`:

```csharp
private static readonly string[] ListPaths =
[
    "/api/platform/organizations", "/api/platform/identities", "/api/platform/admins", "/api/platform/audit",
    "/api/tenants/{tenantId}/roles", "/api/tenants/{tenantId}/members", "/api/tenants/{tenantId}/invitations",
];

[Test]
public async Task Every_list_route_declares_offset_parameters_and_its_binding_refusal()
{
    var paths = await PathsAsync();
    foreach (var path in ListPaths)
    {
        var get = paths.GetProperty(path).GetProperty("get");
        var names = get.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray();
        names.ShouldContain("pageNumber", path);
        names.ShouldContain("pageSize", path);
        names.ShouldNotContain("limit", path);
        names.ShouldNotContain("cursor", path);
        get.GetProperty("responses").GetProperty("400").GetProperty("x-problem-codes").EnumerateArray()
            .Select(code => code.GetString()).ShouldContain("invalid_request", path);
    }
}

[Test]
public async Task Out_of_range_pages_are_clamped_answered_and_never_logged_as_errors()
{
    await OrganizationsAsync(3);
    TestApp.ResetCapturedLogs();

    var huge = await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(int.MaxValue, int.MaxValue)));
    var zero = await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(0, 0)));

    huge.IsSuccess.ShouldBeTrue();
    huge.Value!.PageNumber.ShouldBe(PaginationQuery.MaxPageNumber);
    huge.Value.PageSize.ShouldBe(PaginationQuery.MaxPageSize);
    huge.Value.Items.ShouldBeEmpty();
    zero.Value!.PageNumber.ShouldBe(1);
    zero.Value.PageSize.ShouldBe(1);
    TestApp.CapturedLogs.ShouldNotContain(entry => entry.StartsWith("[Error] CleanArchitecture.", StringComparison.Ordinal));
}

[Test]
public async Task A_page_past_the_end_is_an_empty_page_with_the_real_totals_not_a_refusal()
{
    await OrganizationsAsync(3);

    var result = await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PaginationQuery(5, 2)));

    result.IsSuccess.ShouldBeTrue("a page past the end is not a missing resource (E4)");
    result.Value!.Items.ShouldBeEmpty();
    result.Value.PageNumber.ShouldBe(5);
    result.Value.TotalCount.ShouldBeGreaterThanOrEqualTo(3);
    result.Value.TotalPages.ShouldBe((int)Math.Ceiling(result.Value.TotalCount / 2d));
    result.Value.HasNextPage.ShouldBeFalse();
}
```

In `RoleAdministrationTests.cs` and `MembershipAdministrationTests.cs`, keep the existing cross-tenant assertion unchanged: another tenant's list is still `404 not_found`, now requested with `pageNumber`/`pageSize` (E5).

Run:

```powershell
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj --filter "PlatformDirectoryContractTests|RoleAdministrationTests|MembershipAdministrationTests|ErrorCatalogContractTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Application src/Infrastructure src/Web/Endpoints tests/Application.UnitTests tests/Application.FunctionalTests
git commit -m "feat(pagination): offset pages with a clamped, declared error contract"
```

---

## Task 8A: Amend the SPEC in the Same Change

An approved SPEC outranks the code, and error-handling work amends a SPEC row that pins old behaviour in the same change. Shipping `pageNumber`/`pageSize` while the SPEC still says `limit`/`cursor` is a contract contradiction.

**Files:**
- Modify: `docs/features/identity-access/SPEC.md`
- Modify: `docs/features/identity-access/TRACEABILITY.md` (rows that cite the cursor shape, `:92` and `:99` today)

- [ ] **Step 1: Replace the pinned cursor rows**

| Location | Change |
| --- | --- |
| IA-REQ-038 (`:201`), sentence "adds no pagination envelope to identity endpoints" | Append: "A paginated directory answers an endpoint-specific offset page DTO `{ items, pageNumber, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }`; it is a declared success DTO, not an envelope, and carries no cursor. `pageNumber` (≥ 1) and `pageSize` (1–100, default 25) are clamped, never refused; a non-integer value is the route's `400 invalid_request`; a page past the end is `200` with empty `items`." |
| Rows `:306`–`:309` (Platform directories) | `with limit/cursor` → `with pageNumber/pageSize`; `{ items: …, nextCursor }` → `{ items: …, pageNumber, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }`. Keep every permission and the `401 recent_mfa_required` clause. |
| Scenario `:464`–`:465` | "an opaque cursor and bounded limit" → "a bounded `pageNumber` and `pageSize`"; "and `nextCursor`" → "and the offset page metadata". |
| Rows `:1033`, `:1037` (roles, members, invitations) | `limit (1–100) / cursor` → `pageNumber (≥ 1) / pageSize (1–100)`; `nextCursor` → the offset page metadata. Keep the `404` cross-tenant clause. |
| Amendment `:1065` | "The bounded `limit`/`cursor` shape on `/api/tenants/*`" → "The bounded offset `pageNumber`/`pageSize` shape on `/api/tenants/*` and `/api/platform/*` directories". |

`docs/features/whatsapp-bot/SPEC.md:295,304` describe routes that are not implemented yet. Do not edit them in this change. Report them in the delivery note as a follow-up alignment for that SPEC's owner.

- [ ] **Step 2: Prove no pinned cursor remains**

```powershell
rg -n 'nextCursor|limit.{0,3}/.{0,3}cursor|opaque cursor' docs/features/identity-access
```

Expected: no results.

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

```js
export const DEFAULT_PAGE_SIZE = 25;
export const MAX_PAGE_SIZE = 100; // mirrors PaginationQuery.MaxPageSize; the server clamps again
export const pageSizeOptions = Object.freeze([10, 25, 50, 100]);
export const DEFAULT_PAGE = Object.freeze({ pageNumber: 1, pageSize: DEFAULT_PAGE_SIZE });

const isCount = (value) => Number.isInteger(value) && value >= 0;

/**
 * Checks the declared offset-page contract. The message is developer-facing and stays invariant English (L5):
 * callers turn a throw into `unreadable_response`, whose words come from `errors.json`.
 */
export function readPage(body) {
  const valid = Array.isArray(body?.items)
    && Number.isInteger(body.pageNumber) && body.pageNumber >= 1
    && Number.isInteger(body.pageSize) && body.pageSize >= 1 && body.pageSize <= MAX_PAGE_SIZE
    && isCount(body.totalCount) && isCount(body.totalPages)
    && typeof body.hasPreviousPage === 'boolean' && typeof body.hasNextPage === 'boolean';
  if (!valid) throw new Error('The page metadata does not match the offset pagination contract.');
  return body;
}
```

Use `DEFAULT_PAGE_SIZE` and `MAX_PAGE_SIZE` inside `boundedPage` instead of the literals `25` and `100`.

- [ ] **Step 3: Update success drift policing (E7)**

`readSuccess` keeps rejecting undeclared envelope-like members. `items` stays in `FORBIDDEN_SUCCESS_KEYS` and is allowed only because paginated endpoints declare it through `paginationMembers`. `nextCursor` **stays** forbidden: after this plan, no endpoint declares it, so a server still answering the old shape is drift, not a page.

Add these tests to `src/Web/ClientApp/src/api/pagination.test.js` and `src/Web/ClientApp/src/api/problemDetails.test.js`:

```js
import { readPage } from './pagination';

it.each([
  ['a missing total', { items: [], pageNumber: 1, pageSize: 25, totalPages: 0, hasPreviousPage: false, hasNextPage: false }],
  ['a fractional page', { items: [], pageNumber: 1.5, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }],
  ['an oversized page', { items: [], pageNumber: 1, pageSize: 500, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }],
  ['string flags', { items: [], pageNumber: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: 'false', hasNextPage: false }],
])('refuses %s', (_, body) => {
  expect(() => readPage(body)).toThrow(/offset pagination contract/);
});

it('refuses the retired cursor shape even when items are declared', async () => {
  const response = new Response(JSON.stringify({ items: [], nextCursor: null }), { status: 200, headers: { 'Content-Type': 'application/json' } });
  await expect(readSuccess(response, paginationMembers)).rejects.toThrow(/nextCursor/);
});
```

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- pagination.test.js problemDetails.test.js
```

Expected: PASS.

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

Use one helper per client, so a malformed page is classified exactly like any other contract drift (E7). A plain `Error` thrown after `send` resolves would otherwise reach `toProblem` as `client_failure`, which carries the wrong words.

```js
import { ClientFailure } from '../../../api/apiTransport';
import { paginationMembers, paginationSearch, readPage } from '../../../api/pagination';

const paged = (send, path, page, signal) =>
  send(`${path}?${paginationSearch(page)}`, { expect: paginationMembers, signal }).then((body) => {
    try {
      return readPage(body);
    } catch {
      throw new ClientFailure('unreadable_response');
    }
  });

// identityClient
listRoles: (tenantId, page, options) => paged(send, `/api/tenants/${encodeURIComponent(tenantId)}/roles`, page, options?.signal),
```

Add this to `identityClient.test.js` and `platformClient.test.js`:

```js
it('reports a malformed page as an unreadable response, not as data or a generic failure', async () => {
  server.use(http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json({
    items: [], pageNumber: 0, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false,
  })));

  await expect(client.listRoles(TENANT, { pageNumber: 1, pageSize: 25 })).rejects.toMatchObject({
    problem: { code: 'unreadable_response', status: 0 },
  });
});
```

- [ ] **Step 1b: Carry a page, not a cursor, through `useRead`**

In `src/Web/ClientApp/src/features/identity/useRead.js`:

- rename `refresh(cursor, merge)` to `refresh(page)`;
- call `lifecycle.load({ page, signal: controller.signal })`;
- delete the `merge` branch, because offset pages replace and never append.

The retryable and refused semantics stay byte-for-byte: `errored` keeps `current.data`, `refused` clears it (E8). In `useRead.test.jsx`, keep the existing four-state, abort and stale-generation tests, changing only `cursor` to `page`. Delete only the append/merge test.

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
- Modify catalogs. Deletions only, in both languages (L3):
  - `src/Web/ClientApp/src/i18n/locales/en/platform.json` and `es/platform.json`: `organizations.more`, `identities.more`
  - `src/Web/ClientApp/src/i18n/locales/en/identity.json` and `es/identity.json`: `members.showMore`, `invitations.member.showMore`, `roles.showMore`
  - No new keys: `TablePagination` text comes from the MUI locale (L1).

- [ ] **Step 1: Replace cursor behavior with a requested page, and show the loaded one (E9)**

The read callback must **not** depend on the page. If it did, `useRead` would treat every page change as a new lifecycle, drop the rows and flash the skeleton, which breaks E8's "holds the layout". Pass the page through `refresh(page)` instead:

```jsx
import { DEFAULT_PAGE, pageSizeOptions } from '../../../api/pagination';

const load = useCallback(({ page, signal }) => identity.client.listRoles(tenantId, page ?? DEFAULT_PAGE, { signal }), [identity.client, tenantId]);
const read = useRead(load, tenantId !== null);
const [requested, setRequested] = useState(DEFAULT_PAGE);
const go = (page) => {
  setRequested(page);
  void read.refresh(page);
};
const retry = () => void read.refresh(requested);
```

`PlatformIdentitiesPage.jsx` already names its read `data: page`. Rename that binding to `directory` so it does not shadow the page argument.

- [ ] **Step 2: Use MUI pagination controls**

Prefer `TablePagination` where the screen uses tables. Preserve one interactive DOM tree, roles, labels, ids, permissions, loading/refused/errored/empty states, and existing action visibility. The control's values come from the loaded page only. `sx` is layout only, and nothing is styled per screen.

```jsx
{read.data && read.data.totalCount > 0 && (
  <TablePagination
    component="div"
    count={read.data.totalCount}
    page={read.data.pageNumber - 1}
    rowsPerPage={read.data.pageSize}
    rowsPerPageOptions={pageSizeOptions}
    onPageChange={(_, index) => go({ pageNumber: index + 1, pageSize: read.data.pageSize })}
    onRowsPerPageChange={(event) => go({ pageNumber: 1, pageSize: Number(event.target.value) })}
  />
)}
```

Do not pass `labelRowsPerPage`, `labelDisplayedRows` or `getItemAriaLabel` (L1). An empty collection (`totalCount === 0`) keeps the screen's existing empty state and renders no control.

- [ ] **Step 3: Localize labels (L1–L5)**

1. Delete the five obsolete keys listed under **Files**, in `en` and `es`, in the same commit that removes their last `t()` call.
2. Add no catalog key for pagination.
3. If review demands new visible text, follow L4: `en` plus every `supported` language, named placeholders, `{{total, number}}`, and no bare `count` without plural forms.

Add one assertion to `src/Web/ClientApp/src/i18n/spanish.test.jsx`, reading the expected text from MUI's own locale rather than retyping it:

```jsx
it('gives the pagination control its Spanish names from the MUI locale', async () => {
  const { getItemAriaLabel, labelRowsPerPage } = esES.components.MuiTablePagination.defaultProps;
  render(
    <ThemeProvider theme={themeFor('es')}>
      <TablePagination component="div" count={120} page={0} rowsPerPage={25} onPageChange={() => {}} />
    </ThemeProvider>,
  );

  expect(screen.getByRole('button', { name: getItemAriaLabel('next') })).toBeEnabled();
  expect(screen.getByText(labelRowsPerPage)).toBeInTheDocument();
});
```

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- spanish.test.jsx catalog.contract.test.js presentation.test.jsx
cmd /c npm.cmd run i18n:unused --prefix src/Web/ClientApp
```

Expected: PASS. `i18n:unused` reports no unused key in `common`, `identity` or `platform`.

- [ ] **Step 4: Update UI tests**

Tests run in `en` (L8) and must prove:

- next page sends `pageNumber + 1`;
- previous page sends `pageNumber - 1`;
- a page-size change sends bounded `pageSize` and `pageNumber=1`;
- MUI disables the metadata-driven navigation buttons on the first and last page;
- existing error and refusal rendering still uses `ProblemMessage`.

The MSW fixtures move from `{ items, nextCursor: null }` to a full page:

```js
const pageOf = (items, { pageNumber = 1, pageSize = 25, totalCount = items.length } = {}) => {
  const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / pageSize);
  return { items, pageNumber, pageSize, totalCount, totalPages, hasPreviousPage: pageNumber > 1 && totalPages > 0, hasNextPage: pageNumber < totalPages };
};
const rolesAre = (items, meta) => http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json(pageOf(items, meta)));
```

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- RolesPage.test.jsx MembersPage.test.jsx InviteMemberPage.test.jsx PlatformPanel.test.jsx PlatformIdentitiesPage.test.jsx useRead.test.jsx
```

Expected: PASS.

---

## Task 11A: Pagination Error and Edge States in the SPA

**Files:**
- Modify: the six screens listed in Task 11
- Modify tests: `RolesPage.test.jsx`, `PlatformIdentitiesPage.test.jsx`, `ExternalProofResume.test.jsx`, `IdentityAccessReviewRevalidation.test.jsx`

- [ ] **Step 1: Write the failing error-state tests (E8–E10)**

Add these to `RolesPage.test.jsx` and mirror them in `PlatformIdentitiesPage.test.jsx`. Messages are asserted by their exact `en` catalog words, never by "an alert exists":

```jsx
const pagesServed = (answer) => {
  const requested = [];
  server.use(http.get(`/api/tenants/${TENANT}/roles`, ({ request }) => {
    const url = new URL(request.url);
    const pageNumber = Number(url.searchParams.get('pageNumber'));
    requested.push(pageNumber);
    return answer(pageNumber);
  }));
  return requested;
};
const thirty = Array.from({ length: 30 }, (_, index) => role({ roleId: `role-${index}`, name: `Role ${index}` }));

it('keeps the rows and the control on the loaded page when the next page cannot be reached', async () => {
  server.use(catalogIs([{ code: 'members.read', grantable: true }]));
  const requested = pagesServed((pageNumber) => (pageNumber === 1
    ? HttpResponse.json(pageOf(thirty.slice(0, 25), { totalCount: 30 }))
    : HttpResponse.error()));
  renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('We could not reach the service.');
  expect(screen.getByRole('button', { name: 'Retire Role 0' })).toBeInTheDocument();
  expect(screen.getByText('1–25 of 30')).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
  await waitFor(() => expect(requested.at(-1)).toBe(2));
});

it('clears the rows and explains a refused page in place', async () => {
  server.use(catalogIs([]));
  pagesServed((pageNumber) => (pageNumber === 1
    ? HttpResponse.json(pageOf(thirty.slice(0, 25), { totalCount: 30 }))
    : problem(403, 'permission_denied')));
  renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('You do not have permission to do that here.');
  expect(screen.queryByRole('button', { name: 'Retire Role 0' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
});

it('moves to the real last page when the answered page is past the end', async () => {
  server.use(catalogIs([]));
  // The first answer is the stale page a person lands on after the last rows on it were retired elsewhere.
  const requested = pagesServed((pageNumber) => HttpResponse.json(pageNumber === 2
    ? pageOf(thirty.slice(25), { pageNumber: 2, totalCount: 30 })
    : pageOf([], { pageNumber: 3, totalCount: 30 })));
  renderPage();

  expect(await screen.findByText('26–30 of 30')).toBeInTheDocument();
  expect(requested).toEqual([1, 2]);
});

it('renders only the last page asked for when clicks outrun the network', async () => {
  server.use(catalogIs([]));
  const hundred = Array.from({ length: 100 }, (_, index) => role({ roleId: `r-${index}`, name: `Role ${index}` }));
  server.use(http.get(`/api/tenants/${TENANT}/roles`, async ({ request }) => {
    const url = new URL(request.url);
    const pageNumber = Number(url.searchParams.get('pageNumber'));
    const pageSize = Number(url.searchParams.get('pageSize'));
    if (pageNumber === 2) await new Promise((resolve) => setTimeout(resolve, 50));
    return HttpResponse.json(pageOf(hundred.slice((pageNumber - 1) * pageSize, pageNumber * pageSize), { pageNumber, pageSize, totalCount: 100 }));
  }));
  renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));
  await userEvent.click(screen.getByRole('combobox', { name: /rows per page/i }));
  await userEvent.click(screen.getByRole('option', { name: '50' }));

  expect(await screen.findByText('1–50 of 100')).toBeInTheDocument();
  await new Promise((resolve) => setTimeout(resolve, 80));
  expect(screen.getByText('1–50 of 100')).toBeInTheDocument();
});
```

Run:

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- RolesPage.test.jsx PlatformIdentitiesPage.test.jsx
```

Expected: FAIL until Steps 2–4 land.

- [ ] **Step 2: Render the four read states around the pagination control**

```jsx
{/* RolesSkeleton / RolesTable stand for the markup each screen already renders for loading rows and the table;
    keep that markup, its ids and headings exactly as they are and only move it inside these conditions. */}
{read.status === 'loading' && read.data === null && <RolesSkeleton />}
{(read.status === 'errored' || read.status === 'refused') && (
  <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
    <ProblemMessage problem={read.problem} />
    {read.status === 'errored' && (
      <Button type="button" variant="outlined" onClick={retry}>{t('common:actions.tryAgain')}</Button>
    )}
  </Stack>
)}
{read.data && read.status !== 'refused' && (
  <>
    <RolesTable rows={read.data.items} />
    {/* TablePagination from Task 11 Step 2 */}
  </>
)}
```

Retry is offered from `read.status`, which `useRead` derives from `isRetryable(problem)`, never from whether a problem document arrived (error rule 22). A `401 invalid_session` / `authentication_required` needs no code here: the transport's session-lost listener ends the session centrally. `401 recent_mfa_required` on the Platform directories keeps its existing proof flow unchanged.

- [ ] **Step 3: Correct a page past the end once per answer (E4)**

```jsx
useEffect(() => {
  const data = read.data;
  if (read.status !== 'loaded' || data === null) return;
  if (data.items.length === 0 && data.totalPages > 0 && data.pageNumber > data.totalPages) {
    go({ pageNumber: data.totalPages, pageSize: data.pageSize });
  }
  // eslint-disable-next-line react-hooks/exhaustive-deps
}, [read.status, read.data]);
```

It fires once per server answer. The corrected request can only loop if rows keep disappearing between answers, and each iteration then reflects new server truth. Add no visible text for it (L4).

- [ ] **Step 4: Keep mutations and resumed flows honest (E11)**

- After a successful retire, revoke, reissue or status change, call `read.refresh({ pageNumber: read.data.pageNumber, pageSize: read.data.pageSize })`. A failed refresh renders through Step 2 as a read problem. The mutation's own `role="status"` confirmation stays, and the mutation is never reported as failed.
- `RolesPage.jsx:212-220` walks `nextCursor` to find a role selected before the provider round trip. Replace the walk with a bounded page walk. A failure while walking abandons the resume silently, as today, and never shows a mutation error:

```jsx
let pageNumber = read.data.pageNumber;
let role = read.data.items.find((candidate) => candidate.roleId === pending.roleId);
for (let next = 1; role === undefined && next <= read.data.totalPages; next += 1) {
  if (next === pageNumber) continue;
  const page = await identity.client.listRoles(tenantId, { pageNumber: next, pageSize: read.data.pageSize });
  if (cancelled) return;
  role = page.items.find((candidate) => candidate.roleId === pending.roleId);
}
```

- In `ExternalProofResume.test.jsx` and `IdentityAccessReviewRevalidation.test.jsx`, change only the fixtures from cursor bodies to `pageOf(...)`. Their assertions about what is resumed and what is refused stay as they are.

- [ ] **Step 5: Run the SPA gates for this task**

```powershell
cmd /c npm.cmd test --prefix src/Web/ClientApp -- RolesPage.test.jsx MembersPage.test.jsx InviteMemberPage.test.jsx PlatformPanel.test.jsx PlatformIdentitiesPage.test.jsx ExternalProofResume.test.jsx IdentityAccessReviewRevalidation.test.jsx useRead.test.jsx spanish.test.jsx
cmd /c npx.cmd --prefix src/Web/ClientApp eslint src/Web/ClientApp/src
```

Expected: PASS, and lint clean, including `i18next/no-literal-string`.

- [ ] **Step 6: Commit**

```powershell
git add src/Web/ClientApp/src
git commit -m "feat(pagination): offset page controls with localized MUI labels and honest read states"
```

---

## Task 12: Search Cleanup

**Files:**
- Entire repository.

- [ ] **Step 1: Remove cursor pagination leftovers**

Run:

```powershell
rg -n -i 'nextCursor|cursor|PlatformDirectoryQuery|PlatformDirectoryPage|OpaqueCursor|BoundedLimit|MaximumLimit|MinimumLimit' src tests --glob '!**/node_modules/**' --glob '!src/Web/wwwroot/openapi/**'
rg -n '\blimit\b' src/Web/Endpoints src/Web/ClientApp/src/features src/Application/IdentityAccess src/Infrastructure/IdentityAccess src/Infrastructure/Platform
```

Expected: no cursor-pagination leftovers. The second search is scoped because `limit` also means rate limits (`rate_limit_exceeded`, `ForwardLimit`, attempt budgets). Keep those. The generated `wwwroot/openapi/v1.json` is rebuilt by the solution build and is checked in Task 13 Step 5.

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

- [ ] **Step 4: Prove the error catalogue did not move (E5, E6)**

Run:

```powershell
git diff --stat -- src/Web/ClientApp/src/api/problemCodes.json src/Web/ClientApp/src/i18n/locales/en/errors.json src/Web/ClientApp/src/i18n/locales/es/errors.json src/Application/IdentityAccess/Common/IdentityAccessErrors.cs
rg -n 'invalid_page|invalid_page_size|invalid_cursor|page_out_of_range' src tests --glob '!**/node_modules/**'
```

Expected: the only diff is the `problemCodes.json` path move from Task 1, with identical content. There are no new factories, and the second search finds nothing. If a fixture uses an undeclared code or status, fix the fixture; never declare the fixture's code.

- [ ] **Step 5: Prove no obsolete or orphan UI text remains (L3)**

Run:

```powershell
rg -n "organizations\.more|identities\.more|members\.showMore|invitations\.member\.showMore|roles\.showMore" src/Web/ClientApp/src
cmd /c npm.cmd run i18n:unused --prefix src/Web/ClientApp
```

Expected: no results, and the unused-key gate passes for `common`, `identity` and `platform`.

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

- [ ] **Step 6: Full SPA, localization and lint gates**

The focused runs above are not enough to call frontend work done (`CLAUDE.md`).

Run:

```powershell
cd src/Web/ClientApp; npx vitest run; npx eslint src/; npm run i18n:unused; npx vite build; cd ../../..
```

Expected:
- every Vitest test passes, including `catalog.contract.test.js` (key, placeholder and plural parity for every supported language), `problemCatalogue.contract.test.js`, `spanish.test.jsx` and `presentation.test.jsx`;
- ESLint is clean, with `i18next/no-literal-string` at error severity and no new suppression;
- the unused-key gate passes;
- the build succeeds.

- [ ] **Step 7: Full .NET error and catalogue contracts**

Run:

```powershell
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj
```

Expected: PASS, including `ErrorCatalogContractTests` (served `x-problem-codes` equals `problemCodes.json`), the per-route declaration test, the localization registry and backend-resource parity tests, and the logging tests (exactly one safe `Error` for a forced fault, none for expected refusals or clamped pages).

- [ ] **Step 8: Journeys, English and the generated Spanish smoke**

Needs Docker and no AppHost already running.

Run:

```powershell
dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false
git status --short -- tests
```

Expected: every journey passes, including the `languages.json.journeys` Spanish smoke. `git status` under `tests/` shows only the files listed in **Test Files This Functional Change Edits**. If a journey fails, fix the implementation; never edit a page object for it. If Docker is unavailable, report the journeys as not run and do not commit.

- [ ] **Step 9: Delivery note**

Report, per both skills' output contracts:

- **Error paths touched:** producer, code, status, where each is shown and the test that proves it (E1–E12). What was left neutral, and why (no neutral route is paginated). The SPEC rows amended in Task 8A. Commands run and their results. Anything unverified.
- **Localization:** keys removed (the five "show more" keys, `en` and `es`), keys added (none), MUI locale behavior (L1), the checks run, and translations awaiting native review (none, because no new Spanish copy).
- **Follow-ups:** `docs/features/whatsapp-bot/SPEC.md:295,304` still say `{ items, nextCursor }`.

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

### Error handling

- Out-of-range `pageNumber` / `pageSize`, including `int.MaxValue`, answers `200` with clamped metadata. It never produces `500`, `validation_failed` or an `Error` log (E1, E2, E12).
- A non-integer page parameter answers `400 invalid_request` problem+json, and every list route declares `invalid_request` in OpenAPI (E3).
- A page past the end answers `200` with empty `items` and real totals, and the SPA moves to the real last page without new text (E4).
- `problemCodes.json`, `errors.json` and `IdentityAccessErrors` gain no pagination code, and served `x-problem-codes` still equals the catalogue (E5, E6).
- An old `{ items, nextCursor }` body or malformed page metadata surfaces as `unreadable_response` with its catalog words, never as data (E7).
- On a page change, a retryable failure keeps the rows on screen and offers "Try again". A refusal clears them and explains in place. The control always shows the loaded page, and only the last requested page renders (E8–E10).
- A successful mutation is never reported as failed because the follow-up page refresh failed (E11).
- `docs/features/identity-access/SPEC.md` and `TRACEABILITY.md` describe offset pagination, and no row pins `limit`, `cursor` or `nextCursor` (Task 8A).

### Localization

- Pagination controls show MUI's locale strings with locale-formatted numbers in every supported language, proven for `es` by `spanish.test.jsx` (L1, L8).
- The five obsolete "show more" keys are gone from `en` and `es`, no pagination key was added, and `npm run i18n:unused` passes (L3).
- Catalog parity, placeholder parity, plural parity, `i18next/no-literal-string`, the problem-code coverage gate and the Spanish smoke journey all pass.
- Page metadata, query parameter names, OpenAPI text, logs and developer error messages stay invariant English (L5).
