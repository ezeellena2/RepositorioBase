# SPA Pagination UI Specification

## Purpose

This spec covers how the SPA pages the seven collections:

- one MUI `TablePagination` per collection;
- the read states for each page;
- the past-the-end correction;
- bounded resume walks;
- whole role catalogues;
- labels localized by MUI.

This is functional and error-handling work, not a visual change. The declared `*.test.jsx` edits are therefore allowed
(CLAUDE.md), and every other visual-change contract still binds.

| Screen | Collections |
| --- | --- |
| `PlatformPanel` | organizations, administrators, audit |
| `PlatformIdentitiesPage` | identities |
| `RolesPage` | roles |
| `MembersPage` | members |
| `InviteMemberPage` | invitations |

`PlatformRetentionPage` has no paginated read (D24).

Non-goals:

- the general redesign of combos, tables and components (deferred);
- the WhatsApp bot SPEC (PD-b).

## Requirements

### Requirement: Page control on every collection

Sources: plan Task 11, D09, D12, D21, PD-1, PD-6.

- Each collection MUST render one `TablePagination`. Its `count`, `page` and `rowsPerPage` come from the last loaded
  page: `totalCount`, `pageNumber − 1` and `pageSize`. Its rows-per-page options are 10, 25, 50 and 100.
- Moving to the next or previous page MUST request `pageNumber ± 1` at the loaded `pageSize`.
- Changing rows per page MUST request page 1 at the chosen size.
- A collection whose `totalCount` is 0 MUST keep its empty state and render no control.
- The "show more" controls MUST be removed. Ids, headings, roles, permissions and action visibility MUST be preserved.
- A page change MUST NOT request the RolesPage permission catalogue again.
- RolesPage MUST keep today's coupling of the roles page and the permission catalogue (`RolesPage.jsx:134-140`):
  - while either read is loading without data, the layout holds and no role rows render;
  - if the catalogue read fails, the screen renders that problem where the roles-read problem renders today, and no
    role rows render without grantable codes;
  - "Try again" repeats only the read that failed.

#### Scenario: Tenant lists reach the 26th record

- GIVEN a roles, members or invitations collection of 26 records
- WHEN the screen loads and "Go to next page" is clicked
- THEN the screen requests `pageNumber=1&pageSize=25`, then `pageNumber=2&pageSize=25`
- AND it shows record 26 with the range "26–26 of 26"

#### Scenario: Changing rows per page restarts at page 1

- GIVEN page 2 of 60 roles is loaded
- WHEN 50 rows per page is chosen
- THEN the screen requests `pageNumber=1&pageSize=50` and shows "1–50 of 60"

#### Scenario: Administrators and audit are reachable past page 1

- GIVEN PlatformPanel shows page 1 of 30 audit events, or of 30 administrators
- WHEN "Go to next page" is clicked on that collection
- THEN the panel requests that directory with `pageNumber=2` and shows rows 26–30

#### Scenario: A single page and an empty collection

- GIVEN one collection of 3 rows and another with `totalCount` 0
- WHEN both are loaded
- THEN the first shows both navigation buttons disabled
- AND the second shows its empty state and no pagination control

#### Scenario: The permission catalogue loads once

- GIVEN RolesPage is loaded
- WHEN the person changes page twice
- THEN the permission catalogue has been requested exactly once

#### Scenario: A failed permission catalogue holds the roles back

- GIVEN RolesPage, whose roles page answers but whose permission catalogue read fails with a network error
- WHEN the screen settles
- THEN the alert starts "We could not reach the service.", and no role rows render
- AND "Try again" requests the permission catalogue again, and not the roles page

### Requirement: Read states per page

Sources: E8, E9, E10, D23, PD-7, error rule 22.

| State | Rows on screen | Shown | Control shows |
| --- | --- | --- | --- |
| `loading` after a page change | the previous rows stay and hold the layout; a skeleton appears only while there is no data | — | the last loaded page |
| `errored` (status `0`, `429`, `>= 500`) | the previous rows stay | `ProblemMessage` plus a retry button that requests the page asked for | the last loaded page |
| `refused` (any other `4xx`) | cleared | `ProblemMessage` in place, with no retry | no control |
| `loaded` | the requested page | — | that page |

- The retry label is `common:actions.tryAgain`, except on PlatformIdentitiesPage, which keeps
  `platform:identities.retry` (PD-7).
- Session loss (`401 invalid_session` or `authentication_required`) is ended centrally, never by the screen.
- `401 recent_mfa_required` keeps the existing Platform proof flow.
- When clicks outrun responses, only the last requested page MAY render.

#### Scenario: A failed page change keeps the rows and the control

- GIVEN roles page 1 of 30 is loaded
- WHEN "Go to next page" fails with a network error
- THEN the alert starts "We could not reach the service.", the page-1 rows stay, and the control shows "1–25 of 30"
- AND clicking "Try again" requests page 2

#### Scenario: A refused page clears the rows

- GIVEN roles page 1 of 30 is loaded
- WHEN page 2 answers `403 permission_denied`
- THEN the alert reads "You do not have permission to do that here."
- AND no role rows and no "Try again" button remain

#### Scenario: Only the last requested page renders

- GIVEN 100 roles, with slow answers for page 2
- WHEN the person clicks "Go to next page" and then chooses 50 rows per page
- THEN the control shows "1–50 of 100", and still does after the late page-2 answer arrives

#### Scenario: PlatformPanel retries the requested page

- GIVEN PlatformPanel has organizations page 1 loaded
- WHEN page 2 answers `500 internal_server_error` with a trace id
- THEN the alert shows "Something went wrong. Try again." and "Reference: <traceId>", and the page-1 rows stay
- AND "Try again" requests page 2, not page 1 (a deliberate rewrite of `PlatformPanel.test.jsx:425-456`)

### Requirement: Past-the-end correction

Sources: E4, L4.

When a loaded page has empty `items`, `totalPages` > 0 and `pageNumber` > `totalPages`, the screen MUST request page
`totalPages` at the same `pageSize`. It MUST do so once per answer and MUST add no visible text.

#### Scenario: A stale page moves to the real last page

- GIVEN roles are listed at page size 25
- WHEN the screen receives `{ items: [], pageNumber: 3, totalCount: 30 }` because rows were retired elsewhere
- THEN it requests page 2 once and shows "26–30 of 30", with no added message

#### Scenario: An empty collection is not corrected

- GIVEN an answer with `totalCount` 0 and `totalPages` 0
- WHEN the screen receives it
- THEN it sends no further request and shows its empty state

### Requirement: Mutation outcome survives a failed refresh

Sources: E11, error rule 25. (Amended 2026-09-14, decision C3-V01)

- After a successful retire, revoke, reissue or status change, the screen MUST refresh the loaded page, using the same
  `pageNumber` and `pageSize`.
- If that refresh fails, it MUST render as a read problem, and the mutation MUST NOT be reported as failed.
- Where the screen shows a `role="status"` confirmation for the mutation, that confirmation MUST stay. Where it shows
  none, the accepted mutation's effect MUST stand, and no message on the screen MUST report the mutation as failed.

#### Scenario: A failed refresh after a retirement

- GIVEN roles page 2 is loaded and a role on it is retired successfully
- WHEN the refresh of page 2 answers `500 internal_server_error`
- THEN the read alert appears as the only alert
- AND no message says the retirement failed
- AND the retirement is not repeated

### Requirement: Bounded resume walks

Sources: D11, D12.

- RolesPage and MembersPage resume an operation after the external-provider round trip. When the target is not on the
  loaded page, the screen MUST search the other pages: `pageNumber` 1 to `totalPages`, skipping the loaded one, at the
  loaded `pageSize`, stopping when the target is found.
- A failure during the walk MUST render through the screen's action problem, as it does today, and MUST NOT execute
  the operation.
- Leaving the screen stops the walk.
- Rewritten fixtures use the smallest counts that span two pages. Every existing "resumed or refused" assertion stays.

#### Scenario: A target on page 2 resumes exactly once

- GIVEN a pending members or roles operation whose target is on page 2 of 2
- WHEN the screen returns from the provider
- THEN page 2 is requested by `pageNumber`, with no `cursor` or `limit`
- AND the operation runs once, with its original draft and version

#### Scenario: A failed walk reports and does not execute

- GIVEN a pending operation whose target is not on the loaded page
- WHEN the request for the next page fails with a network error
- THEN the action alert starts "We could not reach the service.", and the operation is not executed

### Requirement: Role pickers read the whole catalogue

Sources: PD-2, D10.

- The MembersPage and InviteMemberPage role pickers MUST read every role by requesting `pageNumber` 1, 2, … until an
  answer has `hasNextPage` `false`. They MUST offer every role by name.
- The walk MUST stop after 100 pages (`MAX_WALK_PAGES`). If page 100 still has `hasNextPage` `true`, the read MUST
  fail as `unreadable_response`.
- A failed walk MUST render exactly as a failed roles read renders today: inside the member's role editor on
  MembersPage (`MembersPage.jsx:451-467`), and in the roles fieldset on InviteMemberPage
  (`InviteMemberPage.jsx:267-290`). `permission_denied` keeps the existing refused sentence. Any other problem shows
  `ProblemMessage`, plus "Try again" when retryable, which restarts the walk at page 1. No partially read catalogue is
  offered.

#### Scenario: More than 25 roles are all offered

- GIVEN 30 roles, served at most 25 per page
- WHEN MembersPage or InviteMemberPage loads its role picker
- THEN all 30 role names are offered, and page 2 was requested
- AND no request follows the answer whose `hasNextPage` is `false`

#### Scenario: A single page needs one request

- GIVEN 3 roles
- WHEN a role picker loads
- THEN exactly one roles page is requested, and all 3 roles are offered

#### Scenario: A walk that never ends is drift

- GIVEN a roles route whose every answer has `hasNextPage` `true`
- WHEN a role picker loads
- THEN exactly 100 roles pages are requested, and none after
- AND the picker shows the `unreadable_response` problem with "Try again", and offers no role

#### Scenario: A failed walk renders like a failed roles read

- GIVEN 30 roles, served at most 25 per page, and a network error on page 2
- WHEN InviteMemberPage loads its role picker
- THEN the roles fieldset shows an alert starting "We could not reach the service." and "Try again", and offers no role
- AND clicking "Try again" requests page 1 again

### Requirement: Pagination labels come from the MUI locale

Sources: L1, L2, L8, D22.

`TablePagination` text MUST come from the MUI locale that `themeFor(language)` composes. This covers the rows-per-page
label, the displayed-rows range with locale-formatted numbers, and the `getItemAriaLabel` button names.

- Screens MUST NOT pass `labelRowsPerPage`, `labelDisplayedRows` or `getItemAriaLabel`, and MUST NOT build these
  strings from catalog keys.
- A future supported language needs only its `muiLocaleByLanguage` entry.
- SPA tests stay in `en`. `spanish.test.jsx` gains one assertion, on `getItemAriaLabel`. The existing
  `labelRowsPerPage` proofs stay: `App.localization.test.jsx:10-30` and `spanish.test.jsx:142-146`.

| Text | `en` (MUI defaults) | `es` (`esES`) |
| --- | --- | --- |
| Rows-per-page label | "Rows per page:" | "Filas por página:" |
| Next-page button name | "Go to next page" | "Ir a la página siguiente" |
| Displayed rows | "1–25 of 120" | the `esES` `labelDisplayedRows`, with `es-ES` number formatting |

#### Scenario: Spanish names reach the control

- GIVEN the theme for `es`
- WHEN a `TablePagination` with count 120, page 0 and 25 rows per page renders
- THEN the next-page button's name equals `esES` `getItemAriaLabel('next')`
- AND the rows-per-page label equals `esES` `labelRowsPerPage`

#### Scenario: No screen overrides MUI labels

- GIVEN the SPA sources
- WHEN they are searched for `labelRowsPerPage`, `labelDisplayedRows` and `getItemAriaLabel` props
- THEN no screen passes any of them

#### Scenario: A language without a MUI locale fails the gate

- GIVEN a language promoted to `supported` without a `muiLocaleByLanguage` entry
- WHEN the MUI mapping gate runs
- THEN the gate fails

### Requirement: Retired show-more keys removed in every language

Sources: L3, D22, D23, PD-7.

- The five keys below MUST be deleted from `en` and `es` in the same change that removes their last call site.
- `platform:identities.retry` MUST stay.
- No pagination key is added.
- `presentation.test.jsx:116-119`, which asserts two of the deleted keys, is deleted.

| Key | `en` | `es` | Change |
| --- | --- | --- | --- |
| `platform:organizations.more` | More organizations | Mostrar más organizaciones | deleted |
| `platform:identities.more` | More accounts | Mostrar más cuentas | deleted |
| `identity:members.showMore` | Show more members | Mostrar más miembros | deleted |
| `identity:invitations.member.showMore` | Show more invitations | Mostrar más invitaciones | deleted |
| `identity:roles.showMore` | Show more roles | Mostrar más roles | deleted |
| `platform:identities.retry` | Try again | Volver a intentar | kept |

#### Scenario: Both catalog gates pass without the keys

- GIVEN the change is complete
- WHEN `npm run i18n:unused` (the `en` namespaces) and `catalog.contract.test.js` (`es` parity) run
- THEN both pass, and neither catalog contains any of the five keys

#### Scenario: Only the five deletions change the catalogs

- GIVEN the `en` and `es` catalogs at baseline
- WHEN they are compared with their post-change versions
- THEN the only differences are the five deleted keys
- AND `platform:identities.retry` still reads "Try again" and "Volver a intentar"

### Requirement: No new text and invariant paging data

Sources: L4, L5.

- Pagination MUST add no visible text beyond what MUI renders.
- If a screen later needs copy, it MUST add the `en` source and every supported language in the same change. That copy
  uses named placeholders and `{{total, number}}`, and never a bare `count` placeholder without plural forms.
- These MUST stay invariant English and MUST NOT pass through `t()`: `pageNumber`, `pageSize`, `totalCount`,
  `totalPages`, `hasPreviousPage`, `hasNextPage`, OpenAPI names and descriptions, log text, and developer `Error`
  messages in `pagination.js` and `problemDetails.js`.

#### Scenario: Localization lint stays clean

- GIVEN the change is complete
- WHEN `npx eslint src/` runs with `i18next/no-literal-string` at error severity
- THEN it reports no violation, and no new `i18next/no-literal-string` suppression exists
- AND any added `eslint-disable` line is a `react-hooks/exhaustive-deps` suppression on a past-the-end effect (plan:1211)

#### Scenario: Developer text stays English, and the person's words are localized

- GIVEN the active language is `es`
- WHEN the page reader rejects a malformed page
- THEN the developer error message is English
- AND the person sees the `es` value of `errors:unreadable_response`

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | The five keys are deleted from both languages, `platform:identities.retry` is kept in both, and no key is added. `TablePagination` labels come from MUI `enUS` defaults and `esES`; `getItemAriaLabel` coverage is the one new `spanish.test.jsx` assertion (D22). |
| New error code | None (E6). |
| Neutral public flows | N/A: no paginated screen is a neutral public flow. |

## Traceability

| Id | Requirement |
| --- | --- |
| E4 | Past-the-end correction |
| E8 | Read states per page |
| E9 | Read states per page; Page control on every collection |
| E10 | Read states per page |
| E11 | Mutation outcome survives a failed refresh |
| L1 | Pagination labels come from the MUI locale |
| L2 | Pagination labels come from the MUI locale |
| L3 | Retired show-more keys removed in every language |
| L4 | No new text and invariant paging data; Past-the-end correction |
| L5 | No new text and invariant paging data |
| L8 | Pagination labels come from the MUI locale |
| E1, E2 | see `api-offset-pagination` |
| E3, E5, E6, E7, E12 | see `list-read-error-contract` |
| L6, L7 | see `shared-problem-infrastructure` |
