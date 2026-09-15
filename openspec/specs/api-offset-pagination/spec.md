# API Offset Pagination Specification

## Purpose

Seven list routes page by offset instead of cursor: they take `pageNumber`/`pageSize` and answer an endpoint-specific
offset page DTO. This spec covers defaults and clamping, response metadata, ordering, the past-the-end page, retirement
of the cursor types, and migration provenance for the retired identity-access documentation.

| Routes | Collection |
| --- | --- |
| `GET /api/platform/organizations`, `/identities`, `/admins`, `/audit` | Platform directories |
| `GET /api/tenants/{tenantId}/roles`, `/members`, `/invitations` | Tenant lists |

- Permissions, MFA gates and item allowlists (IA-REQ-044) are unchanged.
- Non-goals:
  - Unimplemented WhatsApp feature surfaces; any future change must adopt this baseline rather than create a parallel paging contract.
  - Historical change records and mockups.

## Requirements

### Requirement: Offset page parameters

Sources: plan Task 8, D08, L5.

- The seven list routes MUST accept optional `pageNumber` and `pageSize` query parameters, and MUST NOT bind `limit` or `cursor`.
- The served OpenAPI document MUST list both parameters on each route, with invariant English names and descriptions.

#### Scenario: OpenAPI names the offset parameters

- GIVEN the served `/openapi/v1.json`
- WHEN the `get` operation of each list route is read
- THEN its parameters include `pageNumber` and `pageSize`
- AND they exclude `limit` and `cursor`

#### Scenario: A requested page is served

- GIVEN a caller holding `roles.read`, whose active tenant has 30 roles
- WHEN the caller requests `/api/tenants/{tenantId}/roles?pageNumber=2&pageSize=25`
- THEN the answer is `200` with 5 items, `pageNumber` 2, `pageSize` 25 and `totalCount` 30

### Requirement: Offset page response

Sources: plan Tasks 4 and 8, IA-REQ-038.

A list route's `200` body MUST be its endpoint-specific DTO with exactly these members: `items`, `pageNumber`, `pageSize`,
`totalCount`, `totalPages`, `hasPreviousPage` and `hasNextPage`. It is a declared success DTO, not an envelope. It MUST
NOT carry `nextCursor`, `success`, `data`, `error` or `value`.

| Member | Value |
| --- | --- |
| `pageNumber`, `pageSize` | the effective values after clamping |
| `totalCount` | the number of rows in the whole collection |
| `totalPages` | `0` when `totalCount` is `0`, otherwise `ceil(totalCount / pageSize)` |
| `hasPreviousPage` | `pageNumber > 1` and `totalPages > 0` |
| `hasNextPage` | `pageNumber < totalPages` |

#### Scenario: A middle page reports both neighbours

- GIVEN a collection of 8 rows
- WHEN page 2 is requested with `pageSize` 3
- THEN `items` holds 3 rows and `totalPages` is 3
- AND `hasPreviousPage` and `hasNextPage` are both `true`

#### Scenario: An empty collection

- GIVEN a list route whose collection holds no rows
- WHEN page 1 is requested
- THEN `items` is `[]`, `totalCount` and `totalPages` are `0`, and both flags are `false`

#### Scenario: No cursor or envelope member

- GIVEN any list route answering `200`
- WHEN the body's members are listed
- THEN none of them is `nextCursor`, `success`, `data`, `error` or `value`

### Requirement: Defaults and clamping within Int32

Sources: E1, E2, D09, D15, PD-a, PD-1.

- On all seven routes, an omitted `pageNumber` MUST mean 1 and an omitted `pageSize` MUST mean 25.
- Out-of-range Int32 values MUST be clamped, never refused:
  - `pageSize` is clamped to 1–100, so a value of 0 or less means 1;
  - `pageNumber` is clamped to 1–`MaxPageNumber` (21,474,836, which is `int.MaxValue / 100`), so the row offset cannot overflow.
- A clamped request MUST answer `200`. It MUST NOT answer `500` or `validation_failed`.
- Non-integer and beyond-Int32 values are the binding refusal (`list-read-error-contract`).
- Tenant routes previously defaulted to 100 rows and treated a value of 0 or less as 100. That user-visible change (PD-1) goes in the delivery note.

| Query | Effective `pageNumber` | Effective `pageSize` |
| --- | --- | --- |
| none | 1 | 25 |
| `pageNumber=0&pageSize=0` | 1 | 1 |
| `pageNumber=-5&pageSize=-5` | 1 | 1 |
| `pageSize=500` | 1 | 100 |
| `pageNumber=2147483647&pageSize=2147483647` | 21474836 | 100 |

#### Scenario: Tenant lists default to 25 rows

- GIVEN a caller holding `members.read`, whose active tenant has 30 members
- WHEN `/api/tenants/{tenantId}/members` is requested without paging parameters
- THEN the answer is `200` with 25 items, `pageSize` 25, `totalCount` 30 and `hasNextPage` `true`

#### Scenario: A huge page number is clamped, not a fault

- GIVEN 3 organizations and a Platform owner whose session proved the second factor
- WHEN organizations are requested with `pageNumber=2147483647&pageSize=2147483647`
- THEN the answer is `200` with `pageNumber` 21474836, `pageSize` 100 and empty `items`

#### Scenario: A zero page size returns one row

- GIVEN a directory holding at least 2 rows
- WHEN it is requested with `pageNumber=0&pageSize=0`
- THEN the answer is `200` with `pageNumber` 1, `pageSize` 1 and exactly 1 item

### Requirement: Past-the-end page

Sources: E4.

A `pageNumber` greater than `totalPages` MUST answer `200` with empty `items`, the real `totalCount` and `totalPages`,
the requested `pageNumber` echoed, and `hasNextPage` `false`. It MUST NOT answer `404`, which stays reserved for a missing
resource or another tenant's.

#### Scenario: A page past the end is an empty page

- GIVEN a directory of exactly 3 rows
- WHEN page 5 is requested with `pageSize` 2
- THEN the answer is `200` with `items` `[]`, `pageNumber` 5, `totalCount` 3, `totalPages` 2 and `hasNextPage` `false`

#### Scenario: Another tenant's list stays not found

- GIVEN a caller holding `roles.read` and `members.read`, whose active tenant differs from the route's `tenantId`
- WHEN any page of that tenant's roles, members or invitations is requested
- THEN the answer is `404 not_found`

### Requirement: Deterministic page order

Sources: plan Tasks 5 and 7, D14.

- Each list route MUST keep its current row order; the audit directory stays newest first.
- Each route MUST end its ordering with a unique tie-breaker before paging.
- Walking the pages of unchanged data MUST return every row exactly once.

#### Scenario: Consecutive pages neither repeat nor skip

- GIVEN 30 rows that do not change
- WHEN pages 1 and 2 are read at `pageSize` 25
- THEN together they equal page 1 at `pageSize` 100: the same rows, in the same order, with none repeated

#### Scenario: Audit stays newest first across pages

- GIVEN audit events with distinct occurrence times, spanning two pages
- WHEN pages 1 and 2 are read
- THEN no event on page 2 occurred later than any event on page 1

### Requirement: Cursor pagination retired

Sources: plan Tasks 6 and 12, D01, D14, D24, PD-3.

- Application MUST expose one page model: `PaginationQuery` for the request and `PaginatedList<T>` for the result.
- EF query execution stays in Infrastructure.
- Each Platform list query MUST keep a property named `Query`, typed `PaginationQuery`.
- The following MUST be removed: `PlatformDirectoryQuery`, `PlatformDirectoryPage`, `RolePage`, `MemberPage`, `InvitationSummaryPage`, and the cursor encode and decode helpers.
- A Domain identifier's comparison members MAY remain only while a consumer other than a keyset cursor uses them.

#### Scenario: Platform list queries carry an offset request

- GIVEN the Application assembly
- WHEN each Platform list query type is inspected
- THEN it has a `Query` property of type `PaginationQuery`, and no `Cursor` or `Limit` property

#### Scenario: No cursor-pagination leftovers

- GIVEN the change is complete
- WHEN `src` and `tests` are searched case-insensitively for `nextCursor`, `cursor`, `PlatformDirectoryQuery`, `PlatformDirectoryPage`, `OpaqueCursor`, `BoundedLimit`, `MaximumLimit` and `MinimumLimit`, excluding `node_modules`, the generated OpenAPI document, `package-lock.json` and `*.scss`
- THEN no match refers to cursor pagination

## Migration provenance

The 2026-09-13 offset migration also updated the legacy identity-access documentation that existed at the time.
Those parallel documents were intentionally retired during the OpenSpec consolidation. The requirements and
scenarios above are now the normative pagination baseline; `src/` and `tests/` contain the implementation and
executable evidence, and ADR-004 remains the architectural decision record for the identity-access foundation.

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | No human-readable string changes. Parameter and member names are invariant English (L5). |
| New error code | None (E6). |
| Neutral public flows | N/A: no list route is a neutral public flow. |

## Traceability

| Id | Requirement |
| --- | --- |
| E1, E2 | Defaults and clamping within Int32 |
| E4 | Past-the-end page |
| L5 | Offset page parameters (OpenAPI names); primary entry in `spa-pagination-ui` |
| E3, E5, E6, E7, E12 | see `list-read-error-contract` |
| E8, E9, E10, E11, L1, L2, L3, L4, L8 | see `spa-pagination-ui` (E4's SPA correction is also there) |
| L6, L7 | see `shared-problem-infrastructure` |
