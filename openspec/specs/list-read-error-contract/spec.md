# List Read Error Contract Specification

## Purpose

This spec covers the failures a paginated list read can produce, from each route's declared problem codes to the SPA's
strict page reader. It extends the existing error architecture (`Result`/`ApplicationError` and one RFC 9457 writer)
and adds no error code.

- Routes: the seven list routes defined in `api-offset-pagination`.
- Non-goal: the WhatsApp bot SPEC (PD-b). A client built to its cursor pages would get `unreadable_response`, so that
  SPEC must be aligned before its feature is implemented.

## Requirements

### Requirement: Paging parameter binding refusal

Sources: E3, D07, D08, D15, PD-a.

- A non-integer or beyond-Int32 `pageNumber` or `pageSize` MUST answer `400 invalid_request` on all seven routes. The
  answer is `application/problem+json` with `code`, `traceId` and a matching `status`.
- Each route MUST keep declaring `invalid_request` as its binding-failure code. All seven already declare it through
  `WithBodyBindingFailureCode`, so an inline declaration is optional.
- The emission test, renamed from `Every_numeric_limit_binding_refusal_is_emitted_only_as_declared`, MUST send each of
  `pageNumber=not-a-number`, `pageSize=not-a-number`, `pageNumber=2147483648` and `pageSize=2147483648` to all seven
  list routes, and each request MUST answer `400 invalid_request`.

#### Scenario: A non-integer page number is refused

- GIVEN a signed-in caller allowed to read a list route
- WHEN the route is requested with `?pageNumber=not-a-number`, and again with `?pageSize=not-a-number`
- THEN each answer is `400` `application/problem+json`, with `code` `invalid_request` and a `traceId`

#### Scenario: A beyond-Int32 paging value is refused, not clamped

- GIVEN a signed-in caller allowed to read a list route
- WHEN the route is requested with `?pageNumber=2147483648`, and again with `?pageSize=2147483648`
- THEN each answer is `400 invalid_request`, not a clamped `200`

#### Scenario: Every list route declares the refusal

- GIVEN the served OpenAPI document
- WHEN each list route's `400` response is read
- THEN its `x-problem-codes` include `invalid_request`

### Requirement: Declared and emitted codes are unchanged

Sources: E5 as corrected by D15, D08, PD-a.

Each list route's served `x-problem-codes` MUST be the same before and after the change:

| Code | Status | Platform directories (4) | Tenant lists (3) | Emitted by a list read |
| --- | --- | --- | --- | --- |
| `authentication_required` | 401 | declared | declared | yes |
| `invalid_session` | 401 | declared | declared | yes |
| `recent_mfa_required` | 401 | declared | — | yes, when the session has not proved the second factor |
| `permission_denied` | 403 | declared | declared | yes |
| `not_found` | 404 | — | declared | yes, for another tenant |
| `invalid_request` | 400 | declared (binding) | declared (binding) | yes |
| `validation_failed` | 400 | declared | — | no (rule-10 gap) |
| `internal_server_error` | 500 | declared | declared | only on an unexpected fault |

Recorded gap: error rule 10 says a route declares nothing it cannot emit. The Platform directories still declare
`validation_failed`, but no list read can emit it, because out-of-range values are clamped (PD-a). This change records
the gap; it does not close it.

#### Scenario: Declarations do not move

- GIVEN the served OpenAPI document before and after the change
- WHEN each list route's problem codes are compared
- THEN the two sets are identical and match the table

#### Scenario: Every Platform directory requires the second factor

- GIVEN a password-only Platform session holding each directory's read permission
- WHEN organizations, identities, admins or audit are requested with valid paging
- THEN each answers `401 recent_mfa_required`

#### Scenario: Out-of-range values never become `validation_failed`

- GIVEN a caller allowed to read a Platform directory
- WHEN it is requested with `pageSize=0`, and again with `pageSize=500`
- THEN both answer `200`, and neither answers `400 validation_failed`

### Requirement: Pagination adds no error code

Sources: E6, E5.

This change MUST NOT add, rename or retire an error code. It therefore needs no new factory, endpoint contract,
`problemCodes.json` entry, `errors.json` entry in `en` or `es`, client message or test.

- `invalid_page`, `invalid_page_size`, `invalid_cursor` and `page_out_of_range` MUST NOT exist.
- Removing cursors retires no code, because the server never emitted a cursor code.

#### Scenario: Catalogue and words are byte-identical

- GIVEN `problemCodes.json` and the `en` and `es` `errors.json` files at baseline
- WHEN they are compared with their post-change versions (the catalogue at its new path)
- THEN the sorted code-to-status map is identical, and neither `errors.json` has a diff

#### Scenario: No pagination code is invented

- GIVEN the change is complete
- WHEN `src` and `tests` are searched for the four forbidden names, excluding `node_modules`
- THEN nothing is found

### Requirement: Expected paging outcomes are not errors

Sources: E12, error rule 28.

Clamped values, empty pages, past-the-end pages and the `400 invalid_request` binding refusal are expected outcomes.

- They MUST write no `Error` log record.
- No `Error` record MAY contain a paging value.

#### Scenario: Clamped and empty pages log nothing at Error

- GIVEN the captured logs are reset
- WHEN a directory is read with `pageNumber=2147483647&pageSize=2147483647`, and again with `pageNumber=0&pageSize=0`
- THEN no `[Error] CleanArchitecture.` record is captured

#### Scenario: A binding refusal logs nothing at Error

- GIVEN the captured logs are reset
- WHEN a list route is requested with `?pageNumber=not-a-number`
- THEN the answer is `400 invalid_request`, and no `[Error] CleanArchitecture.` record is captured

### Requirement: Strict client page reader

Sources: E7, plan Tasks 9 and 10.

The client MUST read a list success only against the declared offset members. Each drift below MUST reject as
`ClientFailure('unreadable_response')` with status `0`. A drift MUST NOT be treated as data or classified as
`client_failure`. The person sees the existing `errors:unreadable_response` words in `en` and `es`.

| Drift | Rule |
| --- | --- |
| `nextCursor` is present, even when `items` is declared | `nextCursor` stays in `FORBIDDEN_SUCCESS_KEYS` |
| A declared member is missing | the success reader rejects the response |
| `pageNumber` is not an integer ≥ 1 | the page reader rejects the response |
| `pageSize` is not an integer from 1 to 100 | the page reader rejects the response |
| `totalCount` or `totalPages` is not an integer ≥ 0 | the page reader rejects the response |
| `hasPreviousPage` or `hasNextPage` is not a boolean | the page reader rejects the response |

`items` stays forbidden on routes that do not declare it.

#### Scenario: The retired cursor shape is drift

- GIVEN a list route that answers `200` `{ "items": [], "nextCursor": null }`
- WHEN the client lists that collection
- THEN the call rejects with the problem `{ code: "unreadable_response", status: 0 }`

#### Scenario: Malformed metadata is drift, not a generic failure

- GIVEN a roles answer that is valid except that `pageNumber` is 0
- WHEN the client lists roles
- THEN the call rejects with `unreadable_response` and status `0`, not with `client_failure`

#### Scenario: A well-formed page is returned as sent

- GIVEN a list route that answers a page whose seven members are all valid
- WHEN the client lists that collection
- THEN the call resolves with those seven members unchanged

### Requirement: Null-safe bounded paging arguments

Sources: D10, D21, PD-2.

- Client list methods MUST accept a page that is `null`, `undefined` or missing some members.
- They MUST always send a bounded `pageNumber` (an integer ≥ 1, default 1) and a bounded `pageSize` (1–100, default 25).
  The server clamps the values again.
- The method signatures are `identityClient` `(tenantId, page, options)` and `platformClient` `(page, options)`. Both
  pass `options.signal` on to the request.

#### Scenario: A null page reads the default first page

- GIVEN a caller that passes `null` as the page, as the role pickers do today
- WHEN `listRoles` runs
- THEN the request carries `pageNumber=1&pageSize=25`, and no `client_failure` occurs

#### Scenario: Out-of-range and non-numeric values are bounded

- GIVEN the pages `{ pageNumber: 0, pageSize: 500 }` and `{ pageNumber: "abc" }`
- WHEN each is turned into query parameters
- THEN they become `pageNumber=1&pageSize=100` and `pageNumber=1&pageSize=25` respectively

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | No string is added or changed. `errors:unreadable_response` and every other `errors.json` value are unchanged in both languages. |
| New error code (factory, contract, `problemCodes.json`, message, test) | None exists, so none is specified (E6). |
| Neutral public flows | N/A: no list route is a neutral public flow. |

## Traceability

| Id | Requirement |
| --- | --- |
| E3 | Paging parameter binding refusal |
| E5 | Declared and emitted codes are unchanged; Pagination adds no error code |
| E6 | Pagination adds no error code |
| E7 | Strict client page reader |
| E12 | Expected paging outcomes are not errors |
| E1, E2, E4, L5 | see `api-offset-pagination` |
| E8, E9, E10, E11, L1, L2, L3, L4, L8 | see `spa-pagination-ui` |
| L6, L7 | see `shared-problem-infrastructure` |
