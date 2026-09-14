# Design: Offset Pagination Standard

Change `offset-pagination-standard` · baseline `main` at `2716aa6`.

Inputs:
- `proposal.md` (authoritative, including D01–D24 and PD-a, PD-b, PD-1–PD-7, with PD-5 as expanded by the user on 2026-09-13).
- The plan `docs/superpowers/plans/2026-09-13-cross-project-result-pagination-standard.md` (cited as `plan:<line>`). Its E1–E12, L1–L8, tasks and task order are accepted.
- `exploration.md` (evidence).

This design adds only what the plan leaves open, or what the code contradicts.

## Technical Approach

- **Application** owns two pure records: `PaginationQuery` (clamped input) and `PaginatedList<T>` (items plus offset metadata).
  - Handlers keep `Result<T>`/`ApplicationError`.
  - Ports return `PaginatedList<T>`.
- **Infrastructure** runs one EF helper: a count query, then an ordered `OFFSET`/`LIMIT` page. Stores re-wrap rows into views.
- **Web** binds `int? pageNumber, int? pageSize`.
  - It maps through the existing `ResultHttpExtensions.ToHttpResult<T>` into seven endpoint-specific page DTOs.
  - Failures stay RFC 9457. No `Result` or generic envelope crosses HTTP.
- **SPA:**
  - Shared problem infrastructure moves to `src/api` and `src/components`, including every module-neutral field-error helper (`src/components/problemFields.js`, PD-5).
  - `src/api/pagination.js` reads pages strictly (E7).
  - `useRead` carries a page.
  - Screens render MUI `TablePagination` with the four read states (E8–E11) and past-the-end correction (E4).

## Architecture Decisions

| # | Question | Options | Tradeoff | Decision and rationale |
| --- | --- | --- | --- | --- |
| AD1 | Where the pagination types live | Domain; Web DTO only; `Application/Common/Models` | Domain has no paging concept, and Infrastructure cannot reference Web | `Application/Common/Models`, beside `Result`: every layer that needs them can already see it |
| AD2 | Where queries execute (D02) | Application (EF is referenced, ADR-001); each store inline; `Infrastructure/Data/Pagination` | Inline copies drift; Application would mix execution into use cases | Infrastructure helper. Rationale reworded to "keep query execution in Infrastructure" |
| AD3 | Count and page | `Take(n+1)` (no total); `COUNT(*) OVER()` in one query; two queries | A window count loses the total on an empty page (E4) and fights EF projections. Two queries can disagree under concurrent writes | Two queries, no snapshot transaction. Directories are advisory reads; E4 and the next refresh absorb any lag |
| AD4 | Out-of-range input (PD-a) | Clamp; `400 validation_failed` | Confirmed | Clamp within Int32. Non-integer or beyond-Int32 input stays `400 invalid_request` through binding |
| AD5 | Ordering | Keep today's unique key; switch to created-at | A switch reorders every screen. UUID order lets inserts shift rows between offset pages | Keep each route's current order (see table below), each with a unique key. The shifting is accepted; walks search every page, and the catalogue deduplicates |
| AD6 | View re-wrap (D05) | `PaginatedList.Map`; explicit re-wrap | Roles and members build views in batch after paging, so a per-item `Map` fits only two of four | Explicit `new PaginatedList<TView>(views, page.PageNumber, page.PageSize, page.TotalCount)` |
| AD7 | Query property names (PD-3) | `Query` or `Pagination` everywhere | Renaming the Platform property churns its shape test | Platform keeps `Query` typed `PaginationQuery`; tenant queries use `Pagination` (plan:516) |
| AD8 | Scope of `ToHttpResult` migration (plan Task 2 Step 2) | Every endpoint that repeats the pattern; only the files this change already edits | plan:37 and plan:229 name every endpoint that repeats `result.IsSuccess ? … : problems.ToHttpResult(result.Error!)`. A narrower scope contradicts an accepted input; the cost is more files under the 400-line guard | Every repeat, per plan:229: 50 occurrences in 15 files under `src/Web/Endpoints`. The existing overloads take the 204 and `onSuccess` cases; exactly one new `ToAcceptedHttpResult` takes the eight bodyless 202s (plan:237-246). Statuses, headers and bodies stay identical. See "Endpoint migration (AD8)" |
| AD9 | `invalid_request` declaration (D08, D15) | Inline in `WithApiProblemDetails`; rely on `WithBodyBindingFailureCode` | The transformer deduplicates, so the inline copy is redundant | Rely on the binding metadata that already exists. `validation_failed` stays on `Directory`, and the rule-10 gap (declared, never emitted) is recorded |
| AD10 | Problem-infrastructure location (D19, PD-5 as expanded on 2026-09-13) | Leave under Identity; move only the four modules; also move three rendering helpers; move every module-neutral field-error helper | Leaving `fieldErrors.js` behind keeps an Identity dependency. Moving only three helpers leaves four Platform files importing `fieldError`, `firstInvalid`, `claimedFieldNames`, `fieldErrorText` and `selectFieldErrors` from Identity, against acceptance plan:1424 and the base-product capability gate | Move the four modules to `src/api` and `src/components`, and create `src/components/problemFields.js` holding the nine module-neutral field-error exports of `fieldErrors.js`, moved verbatim with their private helpers. `fieldErrors.js` keeps the four registration exports and imports nothing from `problemFields.js`, because no private helper serves both sets. Importers take the new path directly, with no re-export shim (plan Task 1 Step 2). `problemCatalogue.contract.test.js` stays in place, so there are exactly three test moves |
| AD11 | Client page reading | A `paged` copy per client (plan:907); a shared `sendPage` | A copy per client duplicates the E7 classification | Shared `sendPage` in `pagination.js`: failure classification lives in one place (error rule 16) |
| AD12 | Client bounds | Plan snippet (`0` → 25); mirror the server | The snippet contradicts PD-1 | Mirror `PaginationQuery`: a missing or non-integer value takes the default; an integer is clamped, so `0` means 1 |
| AD13 | Full role catalogue (PD-2) | One read at `pageSize=100`; a walk | 100 still truncates | `readEveryPage` walk until `hasNextPage` is false, deduplicated by `roleId`. Hard bound `MAX_WALK_PAGES` = 100 (10,000 roles at `pageSize` 100): if page 100 still has `hasNextPage` true, the walk throws `ClientFailure('unreadable_response')`. `listRoleCatalogue` runs inside the pickers' existing `useRead` loads (`MembersPage.jsx:131-137`, `InviteMemberPage.jsx:113-116`), so any walk failure reaches the same state a failed `listRoles` reaches today and renders in the same place: MembersPage's role editor (`:451-467`) and InviteMemberPage's roles fieldset (`:267-290`). `permission_denied` keeps its refused sentence; any other problem shows `ProblemMessage` plus "Try again" when retryable, which restarts the walk. No partial catalogue is offered |
| AD14 | RolesPage permission catalogue (D21) | Inside the page load; a separate `useRead` | Inside the load, every page change re-requests the catalogue | Separate read, loaded once per tenant. A mutation still refreshes both, as today. Today's `Promise.all` coupling (`RolesPage.jsx:134-140`) stays observable: the skeleton shows while either read is loading without data; the table renders only when both have data; one `ProblemMessage` at the list-read position (`:394`) shows the roles problem, else the catalogue problem; "Try again" refreshes only the read that errored (`refresh(requested)` or the catalogue's `refresh()`). `useRead` classifies the catalogue failure, so no `toProblem(error)` is added and the pinned counts stay: RolesPage 2 (`:171`, `:230`), MembersPage 2, InviteMemberPage 1, total 12 (`identityFiles.contract.test.js:8-18,117-128`) |
| AD15 | Page-change problem placement | One block above the table; keep the existing placement | A single block moves Roles/Members/Invitations messages away from the control | Keep it: `readTarget`/`invitationReadTarget` `'pagination'` renders the problem beside `TablePagination`. When a page change is refused, the data is cleared and `TablePagination` is hidden, but the `'pagination'` problem block still renders without data, as today (`RolesPage.jsx:432`: `readTarget === 'pagination' && read.problem`). PlatformIdentitiesPage and PlatformPanel keep their single section block |
| AD16 | Tasks 6–8 (D04) | One commit per task; one compile unit | Types break every project between steps | One compile unit. It opens with an HTTP/OpenAPI RED that compiles against today's types: parameter names, 200 body members, `?pageSize=0` → `pageSize: 1`. Test doubles and direct store calls are edited in the same unit |
| AD17 | Domain `CompareTo` and operators (D24) | Keep; remove | Their only visible consumers are the cursor filters that Task 7 deletes. EF could rely implicitly on `IComparable` for key comparison, although the `Guid` converters make that unlikely | Remove `IComparable<T>`, `CompareTo` and the four operators only if a repository-wide consumer search is empty after Task 7 **and** every .NET suite passes. Otherwise keep them and reword the keyset comments |
| AD18 | Helper integration test | New Infrastructure test file; functional tests | A new file falls outside the declared list | Functional tests against real PostgreSQL prove ordering and walks (audit pages 1–2, R6C walk) |

## Interfaces / Contracts

```csharp
// src/Application/Common/Models/PaginationQuery.cs — plan:395-412 plus From()
public sealed record PaginationQuery(int PageNumber = 1, int PageSize = 25)
{
    public const int DefaultPageNumber = 1, DefaultPageSize = 25, MaxPageSize = 100;
    public const int MaxPageNumber = int.MaxValue / MaxPageSize;          // 21,474,836; largest Skip 2,147,483,500 (Int32 scope, D15)
    public static PaginationQuery Default { get; } = new();
    public int PageNumber { get; } = Math.Clamp(PageNumber, DefaultPageNumber, MaxPageNumber);
    public int PageSize { get; } = Math.Clamp(PageSize, 1, MaxPageSize);    // <= 0 means 1 (PD-1)
    public int Skip => checked((PageNumber - 1) * PageSize);                // tripwire; cannot fire
    /// <summary>An omitted value is the default page, never every row. Replaces PlatformEndpoints.Page.</summary>
    public static PaginationQuery From(int? pageNumber, int? pageSize) => new(pageNumber ?? DefaultPageNumber, pageSize ?? DefaultPageSize);
}
// src/Application/Common/Models/PaginatedList.cs — plan:436-449 unchanged.
// D03: the tests call new PaginatedList<int>([4, 5, 6], PageNumber: 2, PageSize: 3, TotalCount: 8).
```

```csharp
// src/Infrastructure/Data/Pagination/PaginationExtensions.cs (D02)
using CleanArchitecture.Application.Common.Models;
using Microsoft.EntityFrameworkCore;
namespace CleanArchitecture.Infrastructure.Data.Pagination;
// ToPaginatedListAsync<T>(this IQueryable<T>, PaginationQuery, CancellationToken): CountAsync, then Skip/Take/ToListAsync.
// The caller orders the query first; the helper never guesses an order.
```

**Ports and requests (D01).** The `CleanArchitecture.Application.Common.Models` using is added to the three ports, the three Infrastructure files and the four Web files.

- `IRoleAdministrationStore.ListAsync(TenantId, PaginationQuery, CancellationToken)` returns `Task<PaginatedList<RoleView>>`. `RolePage` is deleted.
- `IMembershipAdministrationStore`:
  - `ListAsync(TenantId, PaginationQuery, CancellationToken)` returns `Task<PaginatedList<MemberView>>`;
  - `ListInvitationsAsync(TenantId, PaginationQuery, CancellationToken)` returns `Task<PaginatedList<InvitationSummaryView>>`;
  - `MemberPage` and `InvitationSummaryPage` are deleted.
- `IPlatformOperationalProjectionReader.Read{Organizations,Identities,Administrators,Audit}Async(PaginationQuery, CancellationToken)` returns `Task<PaginatedList<…Projection>>`.
- `PlatformDirectoryQuery` and `PlatformDirectoryPage<T>` are deleted, along with every cursor artefact:
  - `MaximumLimit` in both stores, `Cursor` in `RoleAdministrationStore` and `OpaqueCursor` in `MembershipAdministrationStore`;
  - the reader's `Bounded`, `Page`, `Decode*` and `Encode*` helpers, and its `System.Text` using.
- Tenant requests:
  - `ListRolesQuery(TenantId TenantId, PaginationQuery Pagination) : IRequest<Result<PaginatedList<RoleView>>>`;
  - `ListMembersQuery` and `ListTenantInvitationsQuery` follow the same pattern.
- Platform requests: `ListPlatform{Organizations,Identities,Administrators,Audit}Query(PaginationQuery Query)` (PD-3).
- Handler logic is unchanged apart from the types. `PlatformDirectoryGate.RefusalAsync<PaginatedList<T>>` still runs before the reader.

No test project imports that namespace globally: `tests/Application.FunctionalTests/GlobalUsings.cs:1-6` and `tests/Infrastructure.IntegrationTests/GlobalUsings.cs:1-4` omit it, `Application.UnitTests` has no `GlobalUsings.cs`, and no test `.csproj` declares it as a `<Using>`. These test files therefore each add the using:

- `Application.FunctionalTests/IdentityAccess/Platform/`: `PlatformDirectoryContractTests.cs`, `PlatformProjectionTests.cs`, `PlatformIdentityLifecycleTests.cs`, `PlatformDirectoryAccessTests.cs`, `PlatformAdministrationTests.cs` (each constructs `PaginationQuery`);
- `…/Lifecycle/ConcurrentDeactivationFloorTests.cs` (`:134` test double);
- `…/Members/IndependentDevelopmentAdministrationReviewTests.cs` (`:114`, `:180`, `:216-217`) and `…/Members/AdministrationDirectoryRevalidationTests.cs` (`:76-83` direct store calls);
- `Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs`, because `Require` resolves only under the Platform namespace (`:21`, `:25-26`), so the new `PaginationQuery` assertion names the type directly;
- the new `Application.UnitTests/Common/Models/PaginationQueryTests.cs` and `PaginatedListTests.cs`, like `ResultTests.cs:1`.

`ProblemDetailsContractTests.cs` already has it (`:4`). `OpenApiContractTests.cs` and the four files that only drop `NextCursor` from private records need none.

**Deterministic ordering, one per route, each ending in a unique key:**

| Route | Rows | ORDER BY |
| --- | --- | --- |
| `/api/platform/organizations` | `Tenants` where `Type = Organization` | `Id` |
| `/api/platform/identities` | `ApplicationUser` | `Id` |
| `/api/platform/admins` | Memberships of the Platform tenant | `Id` |
| `/api/platform/audit` | `AuditEvents` | `OccurredAt DESC, Id DESC` (newest first) |
| `/api/tenants/{id}/roles`, `/members`, `/invitations` | Tenant-scoped rows | `Id` |

**Re-wrap (D05).** Roles and members page the row projection, then build the views. Invitations page the anonymous row, then build the role lookup. Audit pages the anonymous row, then maps the allowlisted metadata. All four finish the same way:

```csharp
var page = await Project(query.OrderBy(role => role.Id)).ToPaginatedListAsync(pagination, cancellationToken);
return new PaginatedList<RoleView>(await ViewsAsync(tenantId, page.Items, cancellationToken), page.PageNumber, page.PageSize, page.TotalCount);
```

The organization, identity and administrator projections return `ToPaginatedListAsync` directly.

**Endpoints (seven routes).**

- Parameters are `int? pageNumber, int? pageSize`, passed as `PaginationQuery.From(pageNumber, pageSize)`, followed by `result.ToHttpResult(context, problems, page => Results.Ok(XResponse.From(page, …)))`.
- The DTO names stay: `RolePageResponse`, `MemberPageResponse`, `InvitationSummaryPageResponse`, and `Platform{Organization,Identity,Administrator,Audit}DirectoryResponse`.
- Each takes the shape `(IReadOnlyList<TItem> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)`, with a static `From` (plan:615-621).
- The declared problem codes are unchanged (E5 as corrected by D15).
- The doc comments that justify keysets are rewritten in the same files: `PlatformOperationalProjectionReader.cs:22-25`, `PlatformDirectories.cs:7-10,21` and `PlatformDirectoryContracts.cs:48-52`.

**Endpoint migration (AD8, plan:229).** On `2716aa6` the pattern occurs 50 times in 15 files under `src/Web/Endpoints`: 38 single-line occurrences in 14 files, plus 12 split over three lines (including `DocumentDisputeEndpoints.cs`, the 15th file). That is 51 routes, because `InvitationEndpoints.cs:129` (`OfferAsync`) serves both resend and cancel.

| Success today | Overload | Occurrences |
| --- | --- | --- |
| `Results.NoContent()` | `ToHttpResult(context, problems)` | 24: `Identity.cs:72`; `Identity/AccountLifecycleEndpoints.cs:97`, `ExternalLoginEndpoints.cs:151,174`, `InvitationEndpoints.cs:129`, `MembershipEndpoints.cs:74,110`, `PasswordEndpoints.cs:74`, `PersonalEndpoints.cs:86`, `RoleEndpoints.cs:105`, `SessionEndpoints.cs:66,74,82`; `Platform/PlatformEndpoints.cs:210,224,248,268,291,325`, `PlatformInvitationEndpoints.cs:74`, `PlatformMfaEndpoints.cs:106,120,150`, `PlatformRetentionEndpoints.cs:98` |
| `Results.Ok(…)` or `Results.Created(…)` from `result.Value` | `ToHttpResult(context, problems, onSuccess)`, as `InvitationEndpoints.cs:106` already does | 18: `Identity/ContextEndpoints.cs:40,52`, `DocumentDisputeEndpoints.cs:56`, `ExternalLoginEndpoints.cs:157`, `MembershipEndpoints.cs:80,88,101`, `PasswordEndpoints.cs:52`, `PersonalEndpoints.cs:92,106`, `RoleEndpoints.cs:60,68,76,85,96`, `SessionEndpoints.cs:58`; `Platform/PlatformRetentionEndpoints.cs:64,80`. Three are the tenant lists Task 8 rewrites (`MembershipEndpoints.cs:80,88`, `RoleEndpoints.cs:68`) |
| `Results.StatusCode(StatusCodes.Status202Accepted)` | new `ToAcceptedHttpResult(context, problems)` | 8: `Identity.cs:64`; `Identity/AccountLifecycleEndpoints.cs:86`, `InvitationEndpoints.cs:147`, `PasswordEndpoints.cs:64`, `PersonalEndpoints.cs:78`; `Platform/PlatformEndpoints.cs:145,309`, `PlatformInvitationEndpoints.cs:54` |

- **Identical answers.** Every migrated endpoint keeps identical status codes, headers and bodies. Each overload returns the same success expression the endpoint returns today, and the same `mapper.ToHttpResult(result.Error!)` failure (`ResultHttpExtensions.cs:8-23`). Antiforgery checks, early guards and pre-send effects keep their order (for example `ExternalHandoffCookie.Clear` at `ExternalLoginEndpoints.cs:150`). No `Produces`, `WithApiProblemDetails` or binding declaration changes.
- **Not this pattern, so unchanged.** Sign-in and sign-out (`SessionEndpoints.cs:85-112`, `:114` onwards) branch on `IsFailure` with cookie effects. Early guards such as `problems.ToHttpResult(IdentityAccessErrors.InvalidInvitation())` (`InvitationEndpoints.cs:100,126`) are not the pattern either.
- **Neutral public flows stay byte-identical.** Organization registration (`Identity.cs:64`), personal registration (`PersonalEndpoints.cs:78`), invitation registration (`InvitationEndpoints.cs:147`), Platform invitation registration (`PlatformInvitationEndpoints.cs:54`), password recovery (`PasswordEndpoints.cs:64`), reactivation requests (`AccountLifecycleEndpoints.cs:86`), Platform administrator invitation (`PlatformEndpoints.cs:309`) and bootstrap recovery (`PlatformEndpoints.cs:145`) all move to `ToAcceptedHttpResult`. Its success is the same bodyless `202`, and its failure is the same writer call. Sign-in is not migrated.
- **Proof, with no new test** (the plan adds only Task 2 Step 1's no-envelope assertions):
  - declarations: `OpenApiContractTests` `Registration_endpoints_declare_bodyless_success_and_problem_details_contracts` (`:62`), `Identity_session_contracts_declare_bodyless_success_and_exact_problem_codes` (`:311`) and `Every_api_route_declares_the_failure_codes_its_shared_pipeline_can_emit` (`:595`); `InvitationRouteContractTests`; `ErrorCatalogContractTests`;
  - neutral answers: `RegistrationHttpValidationTests` (`:210`, `:269`, `:310`), `RegistrationTests` (`:378`), `InvitationHttpContractTests` (`:142`, `:400`, `:420`), `RegisterInvitedUserTests` (`:339`, `:362`), `PasswordLifecycleTests` (`:189`), `IdentityLifecycleTests` (`:217`), `PlatformInvitationOnboardingTests` (`:239`), `PlatformBootstrapRecoveryTests` (`:185`, `:200`), `PlatformAdministrationTests` (`:33`), `SessionTests` (`:931`), and `ProblemDetailsContractTests` (`:286`);
  - the journeys.

**SPA — `src/api/pagination.js`.** Developer messages stay invariant English (L5).

```js
export const paginationMembers;                 // plan:804-812
export const DEFAULT_PAGE_SIZE = 25, MAX_PAGE_SIZE = 100, MAX_WALK_PAGES = 100;
export const pageSizeOptions = [10, 25, 50, 100], DEFAULT_PAGE = { pageNumber: 1, pageSize: 25 };
export function boundedPage(page);              // null-safe (D10): page ?? {}; a non-integer member takes the default; integers clamp (pageNumber >= 1, pageSize 1–100)
export function paginationSearch(page);         // always sets both parameters
export function readPage(body);                 // plan:842-850; throws on drift
export function sendPage(send, path, page, { signal } = {});
  // send(`${path}?${paginationSearch(page)}`, { expect: paginationMembers, signal }).then(readPage)
  // a readPage throw becomes ClientFailure('unreadable_response'); ApiProblem/ClientFailure pass through
export async function readEveryPage(readOne, keyOf, { signal } = {});
  // pageNumber 1.. at MAX_PAGE_SIZE; Map by keyOf; stops when !hasNextPage;
  // pageNumber reaching MAX_WALK_PAGES with hasNextPage still true → ClientFailure('unreadable_response')
```

**Client methods.**

- `identityClient`:
  - `listRoles(tenantId, page, options)`, `listMembers(tenantId, page, options)` and `listTenantInvitations(tenantId, page, options)` each call `sendPage`;
  - new `listRoleCatalogue(tenantId, options)` wraps `readEveryPage` around `listRoles` and serves the MembersPage (`:131-137`) and InviteMemberPage (`:113-116`) pickers;
  - `continued` and its comment are deleted.
- `platformClient.list{Organizations,Identities,Administrators,Audit}(page, options)` (D21). `DIRECTORY`, the limit constants and `page()` are deleted.

**`useRead` (stays in `features/identity`).**

- Signature and read states:
  - `refresh(cursor, merge)` becomes `refresh(page)`, and `load({ page, signal })` receives the page; the merge branch is deleted.
  - `loading` keeps the data; `errored` (status `0`, `429`, `>=500`) keeps it; `refused` clears it.
  - The generation counter aborts a stale request (E10), and the hook never rethrows.
- How screens use it:
  - Screens hold `requested`; `go(page)` sets `requested` and calls `refresh(page)`.
  - Retry calls `refresh(requested)` (E8).
  - A mutation, proof or concurrency refresh re-requests the loaded page, or `DEFAULT_PAGE` when nothing has loaded (E11).
  - `TablePagination` reads `data.pageNumber - 1` and `data.pageSize` only, and renders only when `data.totalCount > 0` (E9, plan:1006-1020).
  - It sits inside the collection's section with no label overrides (L1), and the E4 effect follows plan:1204-1213.
- PlatformPanel: organizations, administrators and audit each get a `TablePagination` (PD-6).
- PlatformIdentitiesPage: `data: page` is renamed `directory`, and `platform:identities.retry` stays (D23, PD-7).

**Problem infrastructure (Task 1, D19).**

| From (`src/Web/ClientApp/src/…`) | To |
| --- | --- |
| `features/identity/api/problemDetails.js` | `api/problemDetails.js` (schema import depth 6 → 4) |
| `features/identity/api/apiTransport.js` | `api/apiTransport.js` |
| `features/identity/problemCodes.json` | `api/problemCodes.json`, bytes unchanged |
| `features/identity/ProblemMessage.jsx` | `components/ProblemMessage.jsx` (imports `../i18n` and `./problemFields`) |
| The nine module-neutral field-error exports of `fieldErrors.js`, with the private `REQUIRED_FIELD_KEYS`, `sameFieldName` and `detailsForField` (PD-5, expanded 2026-09-13) | `components/problemFields.js`, moved verbatim; its only import is `../api/problemDetails` (`VALIDATION_CODE_SCHEMA`). `fieldErrors.js` keeps the four registration exports, and its only import becomes `./register/cuit` |

**`src/components/problemFields.js` (PD-5, expanded 2026-09-13).** Bodies move verbatim from `features/identity/fieldErrors.js` at `2716aa6` (cited as `:<line>`); signatures and results are unchanged.

```js
import { VALIDATION_CODE_SCHEMA } from '../api/problemDetails';
// private: REQUIRED_FIELD_KEYS (:7-24), sameFieldName (:87-91), detailsForField (:93-97)
export const validationDetailText = (error, t, field) => …;        // :26-38
export function selectFieldErrors(problem, fields);                 // :117-125
export function claimedFieldNames(problem, fields);                 // :127-130
export function unclaimedFieldErrors(problem, claimedFields);       // :132-135
export function fieldIdFor(fieldIds, serverField);                  // :137-140
export function clearFieldError(errors, field);                     // :142-148
export function fieldError(problem, name, t);                       // :150-159
export const firstInvalid = (problem, names) => …;                  // :161-163
export const fieldErrorText = (errors, field, t) => …;              // :165-167
```

```js
// src/features/identity/fieldErrors.js after the move: Identity registration rules only
import { cuitError } from './register/cuit';
// private: detail, addError (:4-5); validateEmail, validatePasswordShape, validateName, validateCuit, validateDocument (:40-82)
export const organizationRegistrationFields;                                        // :84
export const personalRegistrationFields;                                            // :85
export function validateOrganizationRegistration(form, includeCredentials = true);  // :99-106
export function validatePersonalRegistration(form);                                 // :108-115
```

Classification evidence:

- Each moved export reads only a problem, a field-error map, field names, field ids or `t`. None reads a form or encodes a registration rule.
- `REQUIRED_FIELD_KEYS` names form fields, but it is a lowercase index into the shared catalogue keys `errors:validation.requiredFields.*` (`locales/en/errors.json:84-101`), which already serve Platform fields (`code`, `recoveryCode`) as well as Identity ones. It aligns with the catalogue and states no validation rule, so it moves with `validationDetailText`, as the first PD-5 scope already confirmed.
- `clearFieldError` has only Identity importers today (`PersonalPages.jsx`, `RegisterOrganizationPage.jsx`), but it only drops a field's entries from a field-error map, case-insensitively, so it is module-neutral.
- No export mixes both kinds, so nothing is flagged. No private helper serves both sets: the validators use only `detail` and `addError`, and the moved helpers use only `sameFieldName`, `detailsForField` and `REQUIRED_FIELD_KEYS`.

**Importer edits (import paths only, Task 1 Step 2).** Paths are relative to `src/Web/ClientApp/src/`:

| File | Today | After |
| --- | --- | --- |
| `features/platform/PlatformPanel.jsx:27` | `{ claimedFieldNames, fieldError }` from `'../identity/fieldErrors'` | same names from `'../../components/problemFields'` |
| `features/platform/shared/PlatformStepUpForm.jsx:6` | `{ fieldError }` from `'../../identity/fieldErrors'` | from `'../../../components/problemFields'` |
| `features/platform/invitations/MfaRecoveryPage.jsx:14` | `{ fieldError, firstInvalid }` from `'../../identity/fieldErrors'` | from `'../../../components/problemFields'` |
| `features/platform/invitations/PlatformInvitationPages.jsx:15-21` | `{ claimedFieldNames, fieldError, fieldErrorText, firstInvalid, selectFieldErrors }` from `'../../identity/fieldErrors'` | from `'../../../components/problemFields'` |
| `features/identity/ProblemMessage.jsx:7` (moves to `components/`) | `{ fieldIdFor, unclaimedFieldErrors, validationDetailText }` from `'./fieldErrors'` | from `'./problemFields'` |
| `features/identity/credentials/PasswordPages.jsx:14-18` | `{ claimedFieldNames, fieldErrorText, selectFieldErrors }` from `'../fieldErrors'` | from `'../../../components/problemFields'` |
| `features/identity/invitations/InvitationPages.jsx:13` | the same three names from `'../fieldErrors'` | from `'../../../components/problemFields'` |
| `features/identity/invitations/InviteMemberPage.jsx:27` | the same three names from `'../fieldErrors'` | from `'../../../components/problemFields'` |
| `features/identity/roles/RolesPage.jsx:25` | the same three names from `'../fieldErrors'` | from `'../../../components/problemFields'` |
| `features/identity/people/PersonalPages.jsx:18-25` | six names from `'../fieldErrors'` | `{ claimedFieldNames, clearFieldError, fieldErrorText, selectFieldErrors }` from `'../../../components/problemFields'`; `{ personalRegistrationFields, validatePersonalRegistration }` stay on `'../fieldErrors'` |
| `features/identity/register/RegisterOrganizationPage.jsx:11-18` | six names from `'../fieldErrors'` | `{ claimedFieldNames, clearFieldError, fieldErrorText, selectFieldErrors }` from `'../../../components/problemFields'`; `{ organizationRegistrationFields, validateOrganizationRegistration }` stay on `'../fieldErrors'` |

`fieldErrors.test.js:2` imports only `validatePersonalRegistration` and is unchanged.

**Searches (Tasks 1 and 12).**

- Task 1 Step 2 is complete when `rg -n "fieldErrors'" src/Web/ClientApp/src` lists only `PersonalPages.jsx`, `RegisterOrganizationPage.jsx` and `fieldErrors.test.js`.
- Task 12 Step 2 covers the helpers and `.agents/skills` (D19, D20). Each search must return nothing:

```text
rg -n "features/identity/api/problemDetails|features/identity/api/apiTransport|features/identity/problemCodes|features/identity/ProblemMessage" src/Web/ClientApp/src .agents/skills
rg -n "identity/fieldErrors" src/Web/ClientApp/src --glob '!**/features/identity/**'
rg -n "features/identity/fieldErrors" .agents/skills
```

## Data Flow

(a) A paged list read with the strict reader:

```mermaid
sequenceDiagram
  participant S as Screen
  participant R as useRead
  participant P as api/pagination.sendPage
  participant T as api/apiTransport.send
  participant E as Endpoint
  participant H as Handler
  participant St as Store/Reader
  participant DB as PostgreSQL
  S->>R: refresh({pageNumber, pageSize})
  R->>P: client.listX(…, page, {signal})
  P->>T: GET path?pageNumber&pageSize (expect paginationMembers)
  T->>E: HTTP GET
  E->>E: bind int? (non-integer or beyond Int32 → 400 invalid_request)
  E->>H: ListXQuery(PaginationQuery.From(...)) — clamped
  H->>H: pipeline authorize; tenant scope / MFA gate → Result failure → Problem Details
  H->>St: ListAsync(scope, pagination)
  St->>DB: SELECT COUNT(*)
  St->>DB: ORDER BY unique key OFFSET skip LIMIT pageSize
  St-->>H: PaginatedList<View>
  H-->>E: Result.Success
  E-->>T: 200 XPageResponse {items, pageNumber, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage}
  T->>T: readSuccess — nextCursor forbidden, members required
  T-->>P: body
  P->>P: readPage — else ClientFailure(unreadable_response)
  P-->>R: page
  R-->>S: loaded; control shows data.pageNumber
```

(b) Past-the-end correction (E4), then a failed page change and its retry (E8–E11):

```mermaid
sequenceDiagram
  participant S as Screen
  participant R as useRead
  participant A as Client + API
  S->>R: refresh({pageNumber:3, pageSize:25})
  A-->>R: 200 {items:[], pageNumber:3, totalPages:2}
  R-->>S: loaded
  S->>S: effect: empty, totalPages>0, pageNumber>totalPages
  S->>R: go({pageNumber:2}) — no new text (L4)
  A-->>R: 200 page 2
  S->>R: go({pageNumber:3}) (requested)
  A--xR: network / 429 / 5xx
  R-->>S: errored; page 2 rows and control kept (E9)
  S->>S: ProblemMessage + Try again (identities.retry on PlatformIdentitiesPage)
  S->>R: retry → refresh(requested page 3)
  alt refused (other 4xx)
    A-->>R: 403 permission_denied
    R-->>S: refused; rows cleared; no retry
  end
  Note over R,A: 401 session loss ends centrally; a newer go() aborts the older request (E10)
```

(c) The RolesPage and MembersPage resume walk (D11):

```mermaid
sequenceDiagram
  participant Pf as useIdentityProof
  participant S as RolesPage / MembersPage
  participant C as identityClient
  Pf-->>S: resumable intent; loaded page L
  S->>S: find target in L.items
  loop next = 1..L.totalPages, skip L.pageNumber, until found
    S->>C: listRoles / listMembers(tenantId, {pageNumber: next, pageSize: L.pageSize})
    C-->>S: page or throw
    opt cancelled (screen left, or the effect re-ran)
      S->>S: return — no forget(), no mutation, no problem
    end
  end
  alt cancelled, or the resumable intent changed
    S->>S: return
  else found and still eligible
    S->>Pf: forget()
    S->>C: replay through run(target, null, …), then refresh(loaded page)
  else not found or ineligible
    S->>Pf: forget(); no mutation
  else walk throws
    S->>S: catch → if not cancelled: setActionProblem(toProblem(error)) → ProblemMessage at the action target
  end
```

The bound `L.totalPages` is fixed when the page loads, so the walk always ends. Leaving the screen stops it (D11, plan:1228):

- the effect cleanup sets `cancelled` (`MembersPage.jsx:215`, `RolesPage.jsx:235`);
- each iteration returns right after its request when `cancelled` is set (`MembersPage.jsx:196`, `RolesPage.jsx:217`);
- after the walk, a cancelled or superseded intent returns before `forget()` (`MembersPage.jsx:200`, `RolesPage.jsx:221`);
- a failure after cancellation is not rendered (`MembersPage.jsx:210`, `RolesPage.jsx:230`).

The effect dependency `nextCursor` becomes `L.pageNumber`/`L.totalPages`.

## File Changes

Totals: **Create 8 · Move 7 (edited) · Modify 102 · Delete 0 files.** Removed types, test methods and keys are modifications. The count is the earlier 90, plus the 12 endpoint files AD8 adds, plus `PlatformStepUpForm.jsx`, which the expanded PD-5 adds, less `fieldErrors.test.js`, which stays declared but needs no edit. `StackBaselineTests.cs` is inspected and not counted.

| File | Action | Change |
| --- | --- | --- |
| `src/Application/Common/Models/PaginationQuery.cs`, `PaginatedList.cs` | Create | AD1, contracts above |
| `src/Application/IdentityAccess/Platform/Queries/PlatformDirectories.cs` | Modify | Delete `PlatformDirectoryQuery`/`PlatformDirectoryPage<T>`; `Query` typed `PaginationQuery`; doc comment |
| `…/Platform/Queries/PlatformDirectoryHandlers.cs`, `…/Platform/IPlatformOperationalProjectionReader.cs` | Modify | D01 types |
| `…/Roles/RoleRequests.cs`, `RoleHandlers.cs`, `IRoleAdministrationStore.cs` | Modify | D01; delete `RolePage` |
| `…/Members/MembershipRequests.cs`, `MembershipHandlers.cs`, `IMembershipAdministrationStore.cs` | Modify | D01; delete `MemberPage`, `InvitationSummaryPage` |
| `src/Infrastructure/Data/Pagination/PaginationExtensions.cs` | Create | AD2 |
| `src/Infrastructure/IdentityAccess/RoleAdministrationStore.cs`, `MembershipAdministrationStore.cs`, `src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs` | Modify | Offset reads, re-wrap, cursor code deleted |
| `src/Domain/IdentityAccess/Tenants/TenantId.cs`, `Authorization/RoleId.cs`, `Memberships/MembershipId.cs`, `Invitations/InvitationId.cs` | Modify | AD17 (remove, or reword the comment) |
| `src/Web/Endpoints/Identity/RoleEndpoints.cs`, `MembershipEndpoints.cs` | Modify | Parameters, page DTOs, `ToHttpResult` (AD8) |
| `src/Web/Endpoints/Platform/PlatformEndpoints.cs`, `Contracts/PlatformDirectoryContracts.cs` | Modify | Parameters, DTOs; delete `Page()`; `ToHttpResult` for six 204s and `ToAcceptedHttpResult` for two 202s (AD8) |
| `src/Web/Endpoints/Identity.cs`; `Identity/{AccountLifecycle,Context,DocumentDispute,ExternalLogin,Invitation,Password,Personal,Session}Endpoints.cs`; `Platform/{PlatformInvitation,PlatformMfa,PlatformRetention}Endpoints.cs` | Modify | AD8 only: move each repeat to its overload, with identical answers |
| `src/Web/Infrastructure/ResultHttpExtensions.cs` | Modify | Add `ToAcceptedHttpResult` (plan:240-246), with eight callers (AD8) |
| `ClientApp/src/features/identity/api/{problemDetails,apiTransport}.js`, `features/identity/problemCodes.json`, `features/identity/ProblemMessage.jsx` | Move | To `src/api/*` and `src/components/ProblemMessage.jsx` |
| `features/identity/api/problemDetails.test.js`, `features/identity/api/apiTransport.test.js`, `features/identity/ProblemMessage.test.jsx` | Move | Exactly these three tests move: the first two to `src/api/`, the third to `src/components/`. `problemDetails.test.js` adds the retired-cursor refusal test (plan:873-876) |
| `ClientApp/src/api/pagination.js`, `src/components/problemFields.js` | Create | AD10–AD13, D10, D19; `problemFields.js` holds the nine moved field-error helpers (PD-5, expanded) |
| `ClientApp/src/api/pagination.test.js` | Create | Declared test file, created by plan Task 9 (plan:767) |
| `features/identity/fieldErrors.js`, `useRead.js`, `api/identityClient.js`, `features/platform/api/platformClient.js` | Modify | PD-5: only the four registration exports remain, importing only `./register/cuit`; page semantics; `sendPage`; `listRoleCatalogue`; `(page, options)` |
| `features/identity/roles/RolesPage.jsx`, `members/MembersPage.jsx`, `invitations/InviteMemberPage.jsx`, `features/platform/PlatformPanel.jsx`, `platform/identities/PlatformIdentitiesPage.jsx` | Modify | `TablePagination`, E4, E8–E11, walks (D11), pickers (PD-2), catalogue once (D21), PD-6, PD-7; RolesPage, InviteMemberPage and PlatformPanel also import their field-error helpers from `problemFields` (PD-5 importer table) |
| Mechanical importers (17): `components/NavMenu.jsx`; `features/identity/{people/PersonalPages,login/LoginPage,lifecycle/AccountLifecyclePages,register/ConfirmEmailPage,register/RegisterOrganizationPage,sessions/SessionsPage,tenants/TenantSelector,invitations/InvitationPages,context/IdentityProvider,credentials/ExternalAccountsPage,credentials/PasswordPages}.jsx`, `features/identity/useSubmit.js`; `features/platform/{retention/PlatformRetentionPage,invitations/MfaRecoveryPage,invitations/PlatformInvitationPages,shared/PlatformStepUpForm}.jsx` | Modify | Import paths only. `PlatformStepUpForm.jsx` is added by the expanded PD-5; it and six others change their `fieldErrors` import per the PD-5 importer table |
| `ClientApp/src/i18n/locales/{en,es}/platform.json` | Modify | Delete `organizations.more`, `identities.more`; keep `identities.retry` (D23, PD-7) |
| `ClientApp/src/i18n/locales/{en,es}/identity.json` | Modify | Delete `members.showMore`, `invitations.member.showMore`, `roles.showMore` |
| SPA tests (plan list): `RolesPage.test.jsx`, `MembersPage.test.jsx`, `InviteMemberPage.test.jsx`, `ExternalProofResume.test.jsx`, `IdentityAccessReviewRevalidation.test.jsx`, `useRead.test.jsx`, `identityClient.test.js`, `PlatformPanel.test.jsx`, `PlatformIdentitiesPage.test.jsx`, `platformClient.test.js`, `platformDirectory.test.js`, `AppRoutes.test.jsx`, `spanish.test.jsx` | Modify | Testing strategy |
| `ClientApp/src/i18n/presentation.test.jsx` | Modify | `:25` `pageOf` fixture; **delete the test at `:116-119`** (D22) |
| D06 SPA: `test/identityFiles.contract.test.js` | Modify | `:28` entry only; the catch counts at `:8-18` and `:117-128` stay unchanged (AD14) |
| D06 SPA: `features/identity/problemCatalogue.contract.test.js` | Modify | Keeps its path. Its imports change (`./problemCodes.json` becomes `../../api/problemCodes.json`), and its words gate (`:33-41`) iterates `languages.supported` from `i18n/languages.json` instead of hard-coded `en`/`es`, through an `import.meta.glob` catalogue lookup (plan:286). It adds one guard test, "reads every supported language, the source included, from the registry for the words gate", so the gate cannot loop zero times (accepted by the user, A1-03, 2026-09-13) |
| D06 SPA: `i18n/catalog.contract.test.js`, `test/identityServer.js` | Modify | Import paths only |
| D06 SPA: `features/identity/fieldErrors.test.js` | Declared, no edit expected | It imports only `validatePersonalRegistration` (`:2`), which stays in `fieldErrors.js` (PD-5 as expanded) |
| `tests/Application.UnitTests/Common/Models/PaginationQueryTests.cs`, `PaginatedListTests.cs` | Create | Unit RED |
| `tests/Application.UnitTests/Architecture/PlatformApplicationShapeTests.cs` | Modify | PD-3 |
| `tests/Application.FunctionalTests/IdentityAccess/Platform/{PlatformDirectoryContractTests,PlatformProjectionTests,PlatformIdentityLifecycleTests,PlatformDirectoryAccessTests,PlatformAdministrationTests}.cs` | Modify | D13, D14, D06 |
| `…/Members/{AdministrationDirectoryRevalidationTests,IndependentDevelopmentAdministrationReviewTests,MembershipAdministrationTests,MembershipAtomicityTests,OwnershipTransferTests}.cs`, `…/Lifecycle/ConcurrentDeactivationFloorTests.cs`, `…/Roles/RoleAdministrationTests.cs` | Modify | D09, D01, stale `NextCursor` rows |
| `…/Api/OpenApiContractTests.cs`, `…/Api/ProblemDetailsContractTests.cs` | Modify | D04a, D07, Task 2 Step 1 |
| `tests/Infrastructure.IntegrationTests/Architecture/StackBaselineTests.cs` | Inspect | `cursor` is a string index; no edit expected |
| `docs/features/identity-access/SPEC.md` | Modify | D16–D18 (the details follow this table) |
| `docs/features/identity-access/TRACEABILITY.md` | Modify | `:86`, `:92` (binding-test name, stale `usePlatformRead.test.jsx` and "More accounts" citations), `:99`, plus a dated evidence paragraph |
| `docs/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` | Modify | Decision 17 (`:36`): "bounded/cursor" becomes "bounded offset" (PD-4) |
| `.agents/skills/error-handling-standards/SKILL.md`, `references/error-handling-rules.md` | Modify | D20: `:109`; `:288,328,400,476` point to `src/api/*`; `:594` names `src/components/problemFields.js` (PD-5, expanded) |
| `.agents/skills/frontend-design-standards/references/ui-composition-rules.md` | Modify | D20: `:317` names offset `TablePagination` controls instead of cursor controls |

`SPEC.md` edits (D16–D18):
- `:201`: indented IA-REQ-038 sub-paragraph.
- `:239`: IA-REQ-045 moves to offset, keeping its `/api/identity/*` no-envelope sentence.
- `:306-309`: edited per row (`recent_mfa_required` only on `:306-307`; `:308` untyped).
- `:464-465`: the scenario.
- `:1033` and `:1037`: no default clause is added.
- `:1065`: text unchanged, plus a dated C5-history note.

No mirror copies of the standards exist: a read-only glob of `.claude/skills` and `.codex/skills` found no `*-standards` folder.

## Testing Strategy

Strict TDD: each behaviour starts RED at the narrowest sufficient layer.

| Layer | What | How |
| --- | --- | --- |
| Application unit | `PaginationQuery`: defaults, clamps, `pageSize <= 0` → 1, `MaxPageNumber`, `Skip` never throws. `PaginatedList`: totals, flags, `TotalCount 0`, past the end | NUnit + Shouldly; plan tests with the D03 named arguments |
| Application functional (HTTP, real PostgreSQL) | Offset parameters on all seven routes, 200 body members, clamping without an `Error` log, past-the-end `200`, cross-tenant `404` unchanged, audit newest-first | HTTP RED first (AD16); `TestApp.CapturedLogs` |
| OpenAPI contract (D08) | `pageNumber`/`pageSize` present, `limit`/`cursor` absent: RED. `invalid_request` declared: already green | `ListPaths` test (plan:657-678) |
| SPA Vitest + MSW | Reader, clients, `useRead`, screens, locale, catalogues | Tests stay in `en` (L8); MSW `problem()` guard |
| Journeys | English plus the generated Spanish smoke; no page-object edits | CLAUDE.md command |

**How the existing .NET tests change:**

- `PlatformDirectoryContractTests.cs` (D13, D14):
  - `:21-31`: expects `["pageNumber","pageSize"]`.
  - `:37-51`: renamed `…_typed_offset_page`.
  - `:54-73`: also forbids `pageNumber`/`pageSize` on `/api/identity/*`.
  - `:75-90` (method header at `:79`): becomes a `pageSize` clamp test: `(1, 10_000)` returns 4 items with `PageSize` 100; `(1, 0)` returns 1 item with `PageSize` 1.
  - `:96-115` and `:118-127`: deleted.
  - `:134-146`: pages 1 and 2 at size 2, newest first, no overlap.
  - Three new tests: the `ListPaths` test, then the clamp and past-the-end tests, which both call `await PlatformScenario.ActiveOwnerAsync()` before seeding. The file gains the `Common.Models` using.
- `OpenApiContractTests.cs`:
  - `:491` points at `src/api/problemCodes.json` (Task 1).
  - `:567-592` is renamed `Every_page_parameter_binding_refusal_is_emitted_only_as_declared`. It sends `pageNumber=not-a-number`, `pageSize=not-a-number`, `pageNumber=2147483648` and `pageSize=2147483648` to all seven routes, and each answers `400 invalid_request`. It is the emission proof for E3 (D07).
  - The same test also asserts that no binding refusal writes an `Error` record (E12). It follows the existing capture pattern: `TestApp.ResetCapturedLogs()` before the requests, then no `TestApp.CapturedLogs` entry starting with `[Error] ` (`TestApp.cs:212,216`, filled by `TestLogCaptureProvider` at `WebApiFactory.cs:144,167`). The precedent is `ProblemDetailsContractTests.Expected_validation_authentication_and_authorization_refusals_write_no_error_record` (`:459-503`, helper `ErrorRecords()` at `:680-683`). This is one added assertion on a declared file, with no new test infrastructure.
- `ProblemDetailsContractTests.cs`: a list 200 body carries the seven members and no `success`/`data`/`error`/`value`/`nextCursor`.
- `PlatformApplicationShapeTests.cs`:
  - Drop the TestCase at `:41`.
  - `:130-144` asserts that each List* query has exactly `Query` typed `PaginationQuery`, and that `PaginationQuery`'s public properties are `Default`, `PageNumber`, `PageSize`, `Skip`.
- The four other Platform test files: `new PlatformDirectoryQuery(n, null)` becomes `new PaginationQuery(1, n)`; the assertions are unchanged.
- `AdministrationDirectoryRevalidationTests.cs` R6C:
  - Becomes a `pageNumber` walk: first page 25 ("default page size is 25", PD-1), then pages up to `totalPages`, with no overlap, the union equal to the seed, and `hasNextPage` false on the last page.
  - The seed shrinks to the smallest count spanning two pages.
  - The diagnostic store call becomes `new PaginationQuery(2, 25)`.
- Test doubles `ConcurrentDeactivationFloorTests.cs:134` and `IndependentDevelopmentAdministrationReviewTests.cs:216-217` take the new signatures. At `:114` and `:180` that file deserializes `PaginatedList<RoleView>` and `PaginatedList<MemberView>`.
- `RoleAdministrationTests.cs:337`, `MembershipAdministrationTests.cs:320`, `MembershipAtomicityTests.cs:160` and `OwnershipTransferTests.cs:325` drop `NextCursor` from their private row records.

**How the SPA tests change:**

- `pagination.test.js`:
  - `boundedPage(null)`, `paginationSearch({0, 500})` and the `readPage` refusals;
  - the `readEveryPage` bound, deduplication, and a 26-role walk over 25-row pages.
- `problemCatalogue.contract.test.js` (plan:286): the words gate (`:33-41`) runs once for each language in `languages.json` `supported`, read from the registry instead of the hard-coded English and Spanish pair, so a promoted language cannot skip it. A guard test proves that the registry lookup yields every supported language, the source included (A1-03). Its other tests keep their assertions.
- `RolesPage.test.jsx`: plan:1094-1167 (E8–E10, E4), plus exactly one catalogue request across two page changes (D21). It also covers a failed catalogue read: the problem renders, no role rows render, and "Try again" requests only the catalogue (AD14). `PlatformIdentitiesPage.test.jsx` mirrors the page-state tests.
- `MembersPage.test.jsx` and `InviteMemberPage.test.jsx`: navigation, and the 26th role offered when MSW answers `pageSize: 25` (PD-2). They also cover a failed walk, which renders where a failed roles read renders today (AD13).
- `PlatformPanel.test.jsx`: `:425-456` is deliberately rewritten so retry re-requests the requested page. Administrators and audit get pagination tests (PD-6).
- `ExternalProofResume.test.jsx` and `IdentityAccessReviewRevalidation.test.jsx` (D12):
  - Recorded reads, continuation clicks and `limit`/`CURSOR` expectations move to offset equivalents.
  - Every resumed-or-refused assertion is kept.
  - Fixtures use 26 rows, the smallest count that spans two pages.
- `useRead.test.jsx`: `cursor` becomes `page`; the merge test is deleted.
- `identityClient.test.js`: keeps its mocked `send` style. It asserts the URL, a null page, and a malformed body answering `unreadable_response`.
- `spanish.test.jsx`: adds only the `getItemAriaLabel('next')` assertion. `labelRowsPerPage` is already proven by `App.localization.test.jsx:10-30` and `spanish.test.jsx:142-146` (D22).
- Expanded PD-5 (`problemFields.js`) adds and edits no test file:
  - No test imports a moved helper. The only test that imports `fieldErrors.js` is `fieldErrors.test.js` (`:2`, `validatePersonalRegistration`, which stays).
  - `identityFiles.contract.test.js` names no `fieldErrors` path, and import edits leave its `toProblem(error)` counts unchanged.
  - Behaviour stays proven by existing tests, run unchanged: the moved `ProblemMessage.test.jsx` (unclaimed field errors); field marking on Platform forms at `PlatformPanel.test.jsx:105-106,493-494`, `PlatformInvitationPages.test.jsx:174-175,389-390` and `MfaRecoveryPage.test.jsx:121-122`; and the screen tests of the Identity importers (`PasswordPages`, `InvitationPages`, `InviteMemberPage`, `RolesPage`, `PersonalPages`, `RegisterOrganizationPage`).
  - Task 1 Step 4 adds `PlatformPanel.test.jsx`, `MfaRecoveryPage.test.jsx`, `PlatformInvitationPages.test.jsx` and `fieldErrors.test.js` to its focused run. A stale import of a name that `fieldErrors.js` no longer exports fails the build as a missing export, and fails that importer's tests.

**Speed and baselines:**

- The two known slow tests (D12) are rewritten with minimal fixtures, so they run fast and green. No test timeout is raised: `testTimeout` stays at 15000 ms.
- Recorded before batch A, and not regressions:
  - 3 Vitest tests exceed the timeout under parallel load and pass when run alone.
  - 3 `IndependentDevelopmentRetentionReviewTests` fail on purpose.
  - The `IndependentDevelopmentAdministrationReviewTests` state is unknown, so run `--filter TestCategory=IndependentDevelopmentReview` before and after.

**Gates:**

- Each .NET project runs in its own process with `TestCategory!=IndependentDevelopmentReview`.
- SPA: `npx vitest run && npx eslint src/ && npm run i18n:unused && npx vite build`.
- The journeys run.
- `git status --short -- tests` must list only the declared files.
- The PD-5 boundary searches (Task 12 Step 2, listed under "Problem infrastructure (Task 1, D19)") return nothing.
- The reviewed-exclusion search (D18) runs over the amended documents: `rg -n -i 'cursor|nullable-limit|numeric_limit|limits, cursors|bounded limit|with `limit`|limit`? ?\(1' docs/features/identity-access`. It must show only these matches: the sessions negative statement (`SPEC.md:695` at baseline), `TASKS.md:936` and `:940`, and the C5 history with its dated note (`api-offset-pagination` spec, "Only reviewed exclusions remain").

## Threat Matrix

N/A.

- The change alters HTTP query parameters and response DTOs on seven existing routes.
- It adds no route, and changes no route matching or dispatch.
- It involves no shell, subprocess, VCS/PR automation, executable-file classification or process integration.
- Authorization (the pipeline's `[Authorize]`, tenant scope, MFA gate) runs unchanged before any read.
- `totalCount` exposes nothing a permitted caller could not already count by walking the pages.

## Migration / Rollout

- **No migration:** no data or schema change, no EF migration and no persisted cursor. A revert needs no database step.
- **Delivery:**
  - Work goes directly to `main`; no branches or PRs (CLAUDE.md).
  - Apply batches: A (Tasks 1–8A), B (9–10), C (11–11A), D (12). Task 13 gates everything.
  - The user commits only after sdd-verify passes.
- **Coupling:**
  - Batch A changes the wire shape; B and C teach the SPA to read it.
  - The client reader is strict (E7): `nextCursor` is forbidden, and the members are required. A build of batch A alone serves pages that the old client reports as `unreadable_response` on all seven screens. MSW-based Vitest runs do not show this; the journeys do.
  - A–C must reach `origin/main` together, and revert together (D reverts alone).

## Open Questions

- [ ] **sdd-tasks (delivery):** commit granularity and push timing.
  - Recommended: one commit per batch, held locally, then one push after Task 13, because of the E7 coupling.
  - The alternative is a single squashed commit.
- [ ] **sdd-tasks (delivery):** map batches A–D to the 400-line review guard under `delivery_strategy` (default `ask-on-risk`; direct to `main`, so `size:exception` or per-batch commits). Also decide whether `openspec/` is committed, and in which batch.
- [ ] **Follow-up, out of scope (it predates this change):** `.agents/skills/error-handling-standards/references/error-handling-rules.md:362` and `:823` name `features/identity/problemMessages.js`, which does not exist. The client's words live in `i18n/locales/*/errors.json`. This is not added to the D20 edits. `:387` stays accurate, because `problemCatalogue.contract.test.js` does not move. The snippet beneath it already imports the missing module (`:389`), and its `./problemCodes.json` import (`:388`) goes stale when the catalogue moves; both belong to the same follow-up.

## Risks

- `DelegatedAdministrationRevalidationTests.cs:258,279` reads page one of `/members` and is in neither declared list.
  - It compiles unchanged, because its private record has `Items` only.
  - It breaks only if it seeds more than 25 members (D09). Verify it; never edit it to make it pass.
- AD17: EF's key comparer may rely on `IComparable` without any visible consumer. The removal is gated by the full .NET suites.
- Every page read costs a `COUNT(*)` plus an `OFFSET` scan, growing linearly with `AuditEvents`. This is accepted by the plan; page size bounds each response, and there is no index work in scope.
- Count and page can disagree under concurrent writes, and inserts shift rows under UUID order (AD3, AD5). E4, the per-page searches and deduplication absorb it.
- The plan's `boundedPage` snippet (`0` → 25) contradicts PD-1. AD12 resolves it.
- The exploration's `PlatformStepUpForm.jsx:6` citation is accurate: that file imports `fieldError` from `../../identity/fieldErrors`. It imports none of the four moved modules, so the count of 24 source importers holds; the expanded PD-5 adds it as the one extra modified file.
  - **Resolved by the expanded PD-5 (2026-09-13):** `fieldError` and every other module-neutral field-error helper move to `src/components/problemFields.js`. Platform (`PlatformPanel.jsx:27`, `PlatformStepUpForm.jsx:6`, `MfaRecoveryPage.jsx:14`, `PlatformInvitationPages.jsx:15-21`) then no longer depends on `features/identity/fieldErrors.js` (acceptance plan:1424).
  - The boundary is proven by searches (Task 12 Step 2), as the four retired paths are, not by a contract test, so a later import from Identity would not fail CI.
  - `REQUIRED_FIELD_KEYS` moves verbatim and still names Identity and Platform fields. A new module's required-field sentence needs an entry there and in `errors:validation.requiredFields` in every supported language, as it does today.
- AD8: the Platform administrator invitation (`PlatformEndpoints.cs:309`) has no HTTP-level neutral test.
  - `PlatformAdministrationTests.cs:33` goes through the handler, `InvitationRouteContractTests.cs:24` proves only that the route is declared, and the journeys drive it (`PlatformOperationsPage.cs:237`).
  - Its identical bodyless `202` rests on `ToAcceptedHttpResult` returning the same expression (plan:240-246). The plan requires no new test, so none is added.
- AD8 adds 12 endpoint files and 50 call sites to batch A, which weighs on the 400-line guard decision under Open Questions.
- `frontend-design-standards` `SKILL.md:42` forbids invented pagination. PD-6 is a user-confirmed functional change, and D20 updates `ui-composition-rules.md:317`.
