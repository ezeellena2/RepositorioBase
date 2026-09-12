# Cross-Project Localization Specification

**Status:** Accepted

**Date:** 2026-09-10

**Related decision:** [ADR-007](../../decisions/ADR-007-Adopt-Cross-Project-Localization.md)

## 1. Objective

Establish one enforceable localization contract for the React SPA, ASP.NET APIs, account preferences,
server-delivered messages, automated tests, and CI. Human-readable system text resolves from a stable key and an
explicit language while machine contracts and user-authored content remain invariant.

## 2. Scope

- the React SPA under `src/Web/ClientApp`;
- ASP.NET request culture and language preference persistence;
- API errors, validation, enum values, statuses, and permissions;
- email, bot, and other server-delivered system messages;
- language registries, SPA catalogs, backend resources, journeys, and CI gates.

The initial source language is `en`. Neutral professional `es` is the first target and remains in progress until
all supported-language promotion gates pass.

## 3. Non-goals

- localizing the legacy Angular client;
- translating user-authored content;
- adding a time-zone preference;
- right-to-left layout support;
- adding a third language without a demonstrated business need.

## 4. Domain language

| Term | Definition |
| --- | --- |
| Source language | `en`: the language in which system copy is authored and existing copy is extracted verbatim. |
| Default language | The deployment-configured fallback used only when no supported account, cookie, or browser choice resolves. |
| Supported language | A complete language that is selectable, negotiable, and subject to all enforcement gates. |
| In-progress language | A registered target whose gaps are reported but not enforced; it is never selected, negotiated, or exposed. |
| Preferred language | The supported language stored for a signed-in account and reused across devices. |
| Language snapshot | An immutable language captured on an invitation or delivery intent for later recipient delivery. |
| Catalog | The complete set of SPA key/value entries for one language. |
| Key | A stable semantic identifier for system-owned text, independent from its current wording. |
| Namespace | An i18next resource partition named by its catalog JSON filename; cross-namespace keys use `namespace:key` syntax. |

Language selects words. Locale selects date and number conventions. Time zone selects the displayed instant. They
are distinct even when one initially helps derive another.

## 5. Normative requirements and traceability

The requirements are normative. Evidence cells reflect the current delivery state: Phases 1–4 are delivered,
Phase 5 is implemented and verified locally, and later phases remain planned.

| Requirement | Gate | Phase | Evidence |
| --- | --- | --- | --- |
| **L10N-REQ-001** — Every user-visible SPA string MUST come from a stable catalog key. | `i18next/no-literal-string` (`warn` → `error`) | 1 warn → 2 per folder → 6 global | Delivered — extraction, usage, and lint evidence. |
| **L10N-REQ-002** — Every supported language MUST contain every `en` semantic key with a non-empty value and identical named placeholders. For each pluralized semantic key, every language MUST provide every plural category its own locale requires; plural categories need not match across languages. Gaps in `inProgress` languages MUST be reported but MUST NOT fail supported-language enforcement. | Catalog parity | 1 | Delivered — strict catalog and placeholder parity. |
| **L10N-REQ-003** — SPA and backend registries MUST declare equal source, default, supported, and `inProgress` language sets. | Registry parity | 1 | Delivered — registry contract. |
| **L10N-REQ-004** — Active language MUST follow these ordering semantics: signed-in account preference, culture cookie, browser negotiation with exact-region then region-to-base matching, then configured default. On the server, each interactive request MUST resolve from the culture cookie, `Accept-Language`, then default; the server MUST NOT read the account per request. After sign-in, the SPA MUST obtain the account preference from identity context and write the culture cookie so subsequent server requests receive that preference through the cookie. Only supported languages participate. | Resolution matrix | 1; account preference in 4 | Delivered — resolver, request-culture, and account-preference matrices. |
| **L10N-REQ-005** — The document `<html lang>` value MUST equal the active language. | Document-language check | 3 | Delivered — DOM and Spanish smoke evidence. |
| **L10N-REQ-006** — APIs MUST expose invariant codes, never display text, for errors, enum values, permissions, statuses, and validation failures. Each validation detail MUST be `{ code, params }`, with a repository-owned stable snake_case code and only safe allowlisted template metadata; every current FluentValidation rule MUST declare an approved code. | API contract and catalog-parity tests; validator architecture test | 5 | Implemented and verified locally — coordinator delivery is pending. |
| **L10N-REQ-007** — Every code advertised through `x-problem-codes` MUST have an `errors:<code>` entry in every supported SPA catalog. | Problem-code coverage | 2 | Delivered — OpenAPI/catalog contract. |
| **L10N-REQ-008** — Server-delivered messages MUST resolve language in order from recipient account preference, the invitation or delivery-intent snapshot, and the configured default, and MUST render under an explicit recipient `CultureInfo`. | Delivery-language matrix | 4 | Delivered — precedence and retry-stability matrix. |
| **L10N-REQ-009** — Every server-delivered message type MUST render successfully in every supported language. | Message rendering matrix | 4 | Delivered — all registered message types in both supported languages. |
| **L10N-REQ-010** — Every backend culture resource MUST contain every English source resource with a non-empty value and identical placeholders. | Backend resource parity | 1 (vacuous until 4) | Delivered — resource and placeholder parity. |
| **L10N-REQ-011** — SPA dates and numbers MUST use the explicit active locale. APIs MUST exchange timestamps in UTC and numbers in invariant machine form. | Formatting contract | 3 | Delivered — locale formatting and invariant API evidence. |
| **L10N-REQ-012** — User-authored content MUST NOT be translated; system-owned content MUST be translated by stable key. | Ownership-boundary review | 3 | Delivered — presentation boundary coverage. |
| **L10N-REQ-013** — Logs, audit records, outbox payloads, codes, identifiers, URLs, and routes MUST remain invariant English or machine data and MUST NOT contain localized display text. | Invariant-data contract | All | Delivered through Phase 4 and extended locally by Phase 5 validation-boundary tests. |
| **L10N-REQ-014** — Reqnroll/Playwright journeys MUST run deterministically in `en`; every supported non-English language MUST have a smoke journey. | Journey language matrix | 1; 3 | Delivered — deterministic English journeys and Spanish smoke journey. |
| **L10N-REQ-015** — CI MUST run all SPA tests and SPA lint on every pull request. | Pull-request CI | 1 | Delivered — Build workflow SPA test and lint jobs. |
| **L10N-REQ-016** — A signed-in language choice MUST persist on the account and apply across devices. | Preference persistence | 4 | Delivered — account, session, and later-sign-in coverage. |
