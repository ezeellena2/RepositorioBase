# Shared Problem Infrastructure Specification

## Purpose

The SPA's problem transport, code catalogue, problem renderer and field-error helpers serve every feature, so they live
outside `features/identity`. On the server, the internal `Result` never crosses HTTP, and one mapper turns it into an HTTP
answer. This spec also covers the binding standards documents that name these paths.

Non-goals:

- relocating `useRead.js` or `useSubmit.js`, which stay in `features/identity`;
- relocating the Identity registration validators, which stay in `features/identity/fieldErrors.js`;
- changing any error code or error words.

## Requirements

### Requirement: Module-neutral problem modules

Sources: plan Task 1, D06, D19, PD-5 (expanded by the user on 2026-09-13), acceptance plan:1424.

These modules MUST live at the following paths:

| Module | Path |
| --- | --- |
| problem reader | `src/api/problemDetails.js` |
| transport | `src/api/apiTransport.js` |
| code catalogue | `src/api/problemCodes.json` |
| renderer | `src/components/ProblemMessage.jsx` |
| field-error helpers (see "Field-error helpers have one module-neutral owner") | `src/components/problemFields.js` |

- These modules MUST NOT import from `src/features/`.
- The move MUST change only import paths, never behaviour.
- Exactly three tests move with their modules: `problemDetails.test.js` and `apiTransport.test.js` to `src/api/`, and
  `ProblemMessage.test.jsx` to `src/components/`.
- `problemCatalogue.contract.test.js` keeps its path under `features/identity`. Apart from its import paths, its only
  edit is the words gate reading `languages.json` `supported` (plan:286).
- `identityFiles.contract.test.js` keeps its pinned catch counts.

#### Scenario: No retired import path remains

- GIVEN the change is complete
- WHEN `src/Web/ClientApp/src` is searched for `features/identity/api/problemDetails`, `features/identity/api/apiTransport`, `features/identity/problemCodes` and `features/identity/ProblemMessage`
- THEN nothing is found

#### Scenario: The renderer depends on no feature

- GIVEN `ProblemMessage.jsx`, `problemFields.js` and the `src/api` modules
- WHEN their imports are resolved
- THEN none of them resolves under `src/features/`
- AND `useRead.js`, `useSubmit.js` and the registration validators in `fieldErrors.js` are still in `features/identity`

#### Scenario: Platform renders a problem exactly as before

- GIVEN PlatformPanel receives `500 internal_server_error` with a trace id
- WHEN the problem renders
- THEN the one alert shows "Something went wrong. Try again." and "Reference: <traceId>"

### Requirement: Field-error helpers have one module-neutral owner

Sources: D19, PD-5 (expanded by the user on 2026-09-13), acceptance plan:1424, base-product capability gate
(`engineering-rules.md`), error rule 18.

- `src/components/problemFields.js` MUST export exactly the module-neutral field-error rendering and selection helpers
  that `features/identity/fieldErrors.js` exports at baseline `2716aa6`, with unchanged signatures and results:
  `validationDetailText`, `selectFieldErrors`, `claimedFieldNames`, `unclaimedFieldErrors`, `fieldIdFor`,
  `clearFieldError`, `fieldError`, `firstInvalid` and `fieldErrorText`.
- `features/identity/fieldErrors.js` MUST keep only the Identity registration rules: `organizationRegistrationFields`,
  `personalRegistrationFields`, `validateOrganizationRegistration` and `validatePersonalRegistration`, with their private
  email, password-shape, name, CUIT and DNI rules. It MUST NOT export or re-export any helper listed above.
- Every importer MUST import a moved helper from `src/components/problemFields.js` directly. No re-export shim is added.
- A module outside `src/features/identity` MUST NOT import `features/identity/fieldErrors.js`.

The search that proves the boundary:

```text
rg -n "identity/fieldErrors" src/Web/ClientApp/src --glob '!**/features/identity/**'
```

#### Scenario: No module outside Identity imports Identity field-error helpers

- GIVEN the change is complete
- WHEN the boundary search runs over `src/Web/ClientApp/src`, excluding `src/features/identity/`
- THEN nothing is found

#### Scenario: Identity keeps only its registration rules

- GIVEN `features/identity/fieldErrors.js` after the change
- WHEN its exports and imports are listed
- THEN it exports exactly `organizationRegistrationFields`, `personalRegistrationFields`, `validateOrganizationRegistration` and `validatePersonalRegistration`
- AND its only import is `./register/cuit`
- AND the only files that import it are `people/PersonalPages.jsx`, `register/RegisterOrganizationPage.jsx` and `fieldErrors.test.js`

#### Scenario: A Platform field error still renders on its field

- GIVEN MfaRecoveryPage, whose recovery request answers `400 validation_failed` with `errors.recoveryCode` holding `too_long` with `max` 64
- WHEN the person submits the form
- THEN the recovery-code field has `aria-invalid="true"`, the accessible description "Must be at most 64 characters." and focus
- AND the alert does not repeat that sentence

### Requirement: Error words unchanged by the move

Sources: L6, L7, D19.

`ProblemMessage` MUST keep resolving its words through `src/i18n`:

- `errors:<code>`
- `errors:unknown`
- `errors:retryAfter`
- `errors:reference`
- `errors:validation.fieldMessage`
- `errors:validation.fields.*`
- every other `errors:validation.*` detail key

It MUST NOT render server `detail`. No `errors.json` value changes in `en` or `es`. No server-delivered text changes,
so no backend `.resx` resource changes.

#### Scenario: Spanish words survive the move

- GIVEN the active language is `es`, and a language change is refused with `400 validation_failed` whose `errors.language` holds `unsupported_value`
- WHEN the moved `ProblemMessage` renders that refusal in the shell (`NavMenu.jsx`; proven by `spanish.test.jsx:98-114`)
- THEN the alert shows the `es` words "Idioma: Este valor no es compatible."
- AND `catalog.contract.test.js` still finds every `es` `errors` value, `errors:permission_denied` and `errors:reference` included, non-empty with the `en` placeholders, and `locales/es/errors.json` has no diff

#### Scenario: Unclaimed field errors still render

- GIVEN a `400 validation_failed` problem with an error for a field that no form field claims
- WHEN `ProblemMessage` renders it
- THEN it lists that error with the `errors:validation.fieldMessage` words, and never the server `detail`

#### Scenario: No word or resource file changes

- GIVEN the change is complete
- WHEN `locales/en/errors.json`, `locales/es/errors.json` and the backend `.resx` resources are diffed against baseline
- THEN there is no difference

### Requirement: One catalogue, same gates

Sources: plan Task 3, D04, E5, E6.

`problemCodes.json` MUST keep identical content at its new path, and every existing gate MUST read it there:

- the backend check that served-OpenAPI codes equal the catalogue;
- the words test, which runs for every language in `languages.json` `supported` (`en` and `es` today);
- the check that the four client-only codes stay out of the catalogue;
- the MSW `problem(status, code)` guard.

#### Scenario: The served catalogue equals the checked-in one

- GIVEN the served `/openapi/v1.json`
- WHEN the union of its `x-problem-codes`, plus the pre-routing `recovery_admission_closed`, is compared with `src/api/problemCodes.json`
- THEN the two code-to-status maps are equal

#### Scenario: Every code has words in every supported language

- GIVEN each catalogue code plus `network_unavailable`, `request_timeout`, `unreadable_response` and `client_failure`
- WHEN each supported language's `errors.json` is read
- THEN every code has a non-empty value in `en` and in `es`
- AND none of the four client-only codes is a key of `problemCodes.json`

#### Scenario: An undeclared fixture pair is refused

- GIVEN a test fixture that calls `problem(400, "invalid_page")`
- WHEN it runs
- THEN the helper throws, because the API never answers that pair

### Requirement: Result stays internal behind one mapper

Sources: plan Task 2, IA-REQ-038, error rule 8.

- Expected failures MUST stay `Result`/`Result<T>` carrying one `ApplicationError`.
- `ResultHttpExtensions` MUST remain the only Result-to-HTTP mapper. Its failure branch MUST use the single RFC 9457
  writer.
- Endpoints MAY move to the existing overloads.
- At most one new overload, for a bodyless `202`, MAY be added.
- No helper MAY write a problem body, choose a failure status or log.
- No success body MAY carry `success`, `data`, `error` or `value`. A page DTO is a success DTO, not an envelope.

#### Scenario: No Result or envelope crosses HTTP

- GIVEN successful list, create and bodyless responses
- WHEN their bodies are inspected
- THEN none has a `success`, `data`, `error` or `value` member

#### Scenario: A refusal goes through the writer

- GIVEN a list read that is refused with `permission_denied`
- WHEN the response is read
- THEN it is `403` `application/problem+json`, with `type`, `title`, `status`, `instance`, `code` and `traceId`

### Requirement: Standards documents match the new contract

Sources: D20, PD-5 (expanded by the user on 2026-09-13).

- `.agents/skills/error-handling-standards/SKILL.md` rule 14 (`:109`) MUST name the module-neutral paths.
- `references/error-handling-rules.md` `:288`, `:328`, `:400` and `:476` MUST name the module-neutral paths:
  `src/api/problemCodes.json` and `src/api/apiTransport.js`.
- `references/error-handling-rules.md:594` MUST name `src/components/problemFields.js` as the home of the field helper.
- `.agents/skills/frontend-design-standards/references/ui-composition-rules.md:317` MUST preserve real offset page
  controls where a route owns them, instead of cursor controls. The paragraph still forbids inventing pagination.
- Any mirrored copy found at sdd-tasks MUST match.

#### Scenario: No standards document names a retired path

- GIVEN the change is complete
- WHEN `.agents/skills` is searched for the four retired import paths and for `features/identity/fieldErrors`
- THEN nothing is found

#### Scenario: The composition rules speak of offset controls

- GIVEN `ui-composition-rules.md` after the change
- WHEN it is searched case-insensitively for `cursor`
- THEN nothing is found
- AND the prohibited-additions paragraph still forbids invented pagination

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | No string is added or changed. `errors.json` stays identical in both languages (L6), and no `.resx` resource changes (L7). |
| New error code | None, so no factory, contract, catalogue entry, message or test (E6). |
| Neutral public flows | N/A: the move changes no status, body, timing or words on any neutral flow, including the registration, invitation and password forms whose field-error imports change. |

## Traceability

| Id | Requirement |
| --- | --- |
| L6 | Error words unchanged by the move; Module-neutral problem modules; Field-error helpers have one module-neutral owner |
| L7 | Error words unchanged by the move |
| D19, PD-5 (expanded 2026-09-13) | Module-neutral problem modules; Field-error helpers have one module-neutral owner; Standards documents match the new contract (`error-handling-rules.md:594`) |
| E5, E6 | One catalogue, same gates (catalogue content); primary entries in `list-read-error-contract` |
| E1, E2, E4, L5 | see `api-offset-pagination` |
| E3, E7, E12 | see `list-read-error-contract` |
| E8, E9, E10, E11, L1, L2, L3, L4, L8 | see `spa-pagination-ui` |
