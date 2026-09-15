# Cross-Project Localization Specification

## Purpose

Define the current localization contract for the React SPA, ASP.NET APIs, account preferences, server-delivered
messages, automated tests, and CI. Human-readable system text resolves from a stable key and an explicit language;
machine contracts and user-authored content remain invariant.

The source language is `en`. Neutral professional `es` is supported. No translation platform or third language is
part of the current baseline. This specification implements
[ADR-007](../../decisions/ADR-007-Adopt-Cross-Project-Localization.md).

## Requirements

### Requirement: L10N-REQ-001 — SPA copy uses stable catalog keys

Every user-visible SPA string MUST come from a stable catalog key. `i18next/no-literal-string` MUST run at error
severity across the SPA. Suppressions MUST be narrow and justified for non-product test probes or invariant protocol
data.

#### Scenario: Product copy is added

- **WHEN** a user-visible string is added to the React SPA
- **THEN** the component resolves it through a semantic catalog key
- **AND** the global localization lint accepts no unapproved literal product copy.

### Requirement: L10N-REQ-002 — Supported catalogs are complete

Every supported language MUST contain every `en` semantic key with a non-empty value and identical named
placeholders. For pluralized keys, each language MUST provide every plural category its locale requires. Gaps in an
`inProgress` language MUST be reported but MUST NOT weaken supported-language enforcement or make that language
selectable.

#### Scenario: Catalog parity is checked

- **WHEN** catalog validation runs
- **THEN** missing keys, empty values, placeholder drift, and missing locale-specific plural categories fail for a supported language
- **AND** the same gaps are report-only for an `inProgress` language.

### Requirement: L10N-REQ-003 — Frontend and backend language registries agree

The SPA and backend registries MUST declare the same source language, configured default, supported languages, and
`inProgress` languages.

#### Scenario: A registry changes

- **WHEN** either runtime changes its language registry
- **THEN** registry-contract tests fail until both runtimes declare equal canonical sets.

### Requirement: L10N-REQ-004 — Active language follows one precedence contract

The SPA MUST resolve the active language from signed-in account preference, culture cookie, browser negotiation
(exact region before region-to-base), then configured default. The server MUST resolve each interactive request from
the culture cookie, `Accept-Language`, then default, without reading the account per request. After sign-in, the SPA
MUST write the account preference to the culture cookie. Only supported languages participate.

#### Scenario: A signed-in preference becomes active

- **WHEN** identity context returns a supported account preference
- **THEN** the SPA selects it and updates the culture cookie
- **AND** later server requests resolve the same language from that cookie.

#### Scenario: A regional browser tag has no exact match

- **WHEN** no account or cookie preference exists and the browser requests a regional tag whose supported base exists
- **THEN** the SPA selects the supported base language before falling back to the configured default.

### Requirement: L10N-REQ-005 — Document language follows the active language

The document `<html lang>` value MUST equal the active language.

#### Scenario: Language changes

- **WHEN** the active supported language changes
- **THEN** the document language changes to the same canonical tag.

### Requirement: L10N-REQ-006 — API language is invariant and structured

APIs MUST expose invariant codes, never display text, for errors, enum values, permissions, statuses, and validation
failures. Each validation detail MUST be `{ code, params }`, with a repository-owned stable snake_case code and only
safe allowlisted template metadata. Every FluentValidation rule MUST declare an approved code. Known correctable
rules MUST use specific codes; `invalid` is only the sanitized unknown or malformed fallback, and `password_policy`
is only the unknown/custom Identity-provider fallback. Provider descriptions and submitted values MUST NOT cross the
boundary.

#### Scenario: Validation fails

- **WHEN** an API rejects a request field
- **THEN** it returns a stable invariant code and safe allowlisted parameters
- **AND** it exposes neither provider prose nor the submitted value.

### Requirement: L10N-REQ-007 — Advertised problem codes are translatable

Every code advertised through `x-problem-codes` MUST have an `errors:<code>` entry in every supported SPA catalog.

#### Scenario: An endpoint advertises a new problem code

- **WHEN** OpenAPI problem-code coverage runs
- **THEN** the build fails until every supported SPA catalog contains its error key.

### Requirement: L10N-REQ-008 — Recipient messages use explicit culture

Server-delivered messages MUST resolve language from recipient account preference, then the invitation or delivery
intent snapshot, then configured default. Rendering MUST use an explicit recipient `CultureInfo`, not ambient worker,
request, or machine culture.

#### Scenario: A delivery is retried after preference changes

- **WHEN** a logical message was first prepared in one language and is retried after the account preference changes
- **THEN** the retry reuses the originally bound language and logical message identifier.

### Requirement: L10N-REQ-009 — Every message type supports every supported language

Every server-delivered message type MUST render in every supported language. Future-language expectations MUST be
derived from backend resources rather than treating every non-Spanish language as English.

#### Scenario: A supported language is promoted

- **WHEN** the message-rendering matrix includes the promoted language
- **THEN** every message type renders from that language's own resources with valid placeholders.

### Requirement: L10N-REQ-010 — Backend resources preserve parity

Every backend culture resource MUST contain every English source resource with a non-empty value and identical
placeholders.

#### Scenario: A backend resource changes

- **WHEN** an English resource key or placeholder changes
- **THEN** resource parity fails until every supported culture matches the key and placeholder contract.

### Requirement: L10N-REQ-011 — Formatting is locale-aware while transport stays invariant

SPA dates and numbers MUST use the explicit active locale. APIs MUST exchange timestamps in UTC and numbers in
invariant machine form.

#### Scenario: A localized screen renders API data

- **WHEN** a screen displays a timestamp or number
- **THEN** it formats that value using the active locale
- **AND** the underlying API representation remains UTC or invariant machine data.

### Requirement: L10N-REQ-012 — Translation respects content ownership

User-authored content MUST NOT be translated. System-owned content MUST be translated by stable key.

#### Scenario: A screen mixes user and system content

- **WHEN** the SPA renders a user-provided value next to a system label
- **THEN** the user value remains unchanged and only the system label is resolved from the catalog.

### Requirement: L10N-REQ-013 — Operational data remains invariant

Logs, audit records, outbox payloads, codes, identifiers, URLs, and routes MUST remain invariant English or machine
data and MUST NOT contain localized display text.

#### Scenario: A localized action is audited and queued

- **WHEN** a person performs an action while using a non-source language
- **THEN** audit and outbox persistence contain only invariant codes, identifiers, and machine data.

### Requirement: L10N-REQ-014 — Localized journeys are registry-derived and deterministic

Reqnroll/Playwright journeys MUST run deterministically in `en`. `languages.json.journeys` MUST contain every
supported non-source language exactly once as a unique canonical tag, and Reqnroll.ExternalData MUST derive the
localized smoke journey from that dataset and catalog values.

#### Scenario: Journey coverage is generated

- **WHEN** acceptance tests discover localized journey data
- **THEN** English remains deterministic and each supported non-source language contributes exactly one smoke case.

### Requirement: L10N-REQ-015 — CI enforces the localization gates

CI MUST run all SPA tests, global localization lint at error severity, and the scoped static-unused gate on every push
to `main` and on every pull request when pull-request validation is enabled.

#### Scenario: A change reaches CI

- **WHEN** a main-branch or enabled pull-request build runs
- **THEN** SPA tests, global localization lint, and the scoped unused-key check all execute as required gates.

### Requirement: L10N-REQ-016 — Account language preference persists

A signed-in language choice MUST persist on the account and apply across devices. Null means no account choice; a
cookie, browser language, or configured default MUST NOT silently backfill it. Own-account mutation accepts only a
canonical supported tag and MUST NOT alter credentials, security version, sessions, active tenant, or authorization.

#### Scenario: A person chooses a supported language

- **WHEN** a signed-in person updates their language preference
- **THEN** identity context returns the persisted canonical tag on later devices
- **AND** no credential, session, tenant, or authorization state changes.

### Requirement: L10N-REQ-017 — Unused-key enforcement is bounded and read-only

Unused-key enforcement MUST be read-only and scan only the statically referenced `common`, `identity`, and `platform`
namespaces. `errors` and `enums` remain excluded because dedicated problem, validation, permission, enum, status, and
role gates govern their runtime-built keys. The scanner MUST remain report-only unless namespace scoping, dynamic-key
preservation, read-only behavior, and nonzero failure status are all proven.

#### Scenario: The static scanner runs

- **WHEN** the unused-key gate executes
- **THEN** it scans `common`, `identity`, and `platform` independently without rewriting catalogs
- **AND** it preserves the dynamic family `common:language.*`.

### Requirement: L10N-REQ-018 — Pseudo-language is development-only and ephemeral

The pseudo-language MUST activate only in development for the exact query `?lng=en-XA`. It MUST clone and transform
English catalog values in memory without changing keys, tokens, tags, whitespace, or source resources, and expand
visible literal text by approximately 35%. Production MUST ignore it. It MUST never enter registries, selectors,
cookies, accounts, backend values, delivery state, or database constraints, and account preference application MUST
NOT replace an active development override.

#### Scenario: The development override is requested

- **WHEN** a development build receives the exact `en-XA` query
- **THEN** the in-memory pseudo-resources become active without becoming selectable or persistent.

#### Scenario: The query reaches production

- **WHEN** a production build receives the same query
- **THEN** it ignores the override and uses normal supported-language resolution.

### Requirement: L10N-REQ-019 — Material UI locales are explicit

Every supported language MUST have exactly one explicit Material UI locale mapping in `theme.jsx`. No `inProgress`
or pseudo-language key may appear there. `themeFor` MUST retain the English fallback and `appTheme` semantics.

#### Scenario: A supported language lacks a UI locale mapping

- **WHEN** the MUI registry contract runs
- **THEN** it fails until that supported language has exactly one defined mapping.

## Current boundaries

The React SPA under `src/Web/ClientApp`, ASP.NET request culture, identity preference persistence, API contracts,
server-delivered messages, language registries, catalogs, resources, journeys, and CI are in scope. The legacy Angular
client, user-authored content translation, time-zone preferences, right-to-left layout, a translation platform, and a
third language are not part of this baseline.

Bundled SPA catalogs are discovered synchronously through one eager
`import.meta.glob('./locales/*/*.json', { eager: true, import: 'default' })` seam. Every supported language contains all
known namespace files. `en-XA` is assembled in memory from English resources only after its exact development query is
accepted; the selector continues to list supported languages only.
