# ADR-007: Adopt a Cross-Project Localization Standard

## Status

Accepted

## Date

2026-09-10

## Context

The product presents system-owned text through a React SPA, ASP.NET APIs, email, and bot delivery. Those surfaces
do not currently share a language contract. The React application has no internationalization runtime and embeds
English literals in JSX. Server email templates contain English prose but do not carry or resolve the recipient's
language. Validation responses expose sentences instead of stable codes. SPA tests and lint are not required CI
gates on every pull request, and the repository's agent rules protect or prescribe single-language copy.

Adding a second language one screen at a time would create incompatible resolution rules, partially translated
experiences, and background messages formatted in the worker's culture. The system needs one standard that keeps
machine contracts invariant while making every human-facing system message complete and testable.

## Decision

Adopt these seven rules across projects and delivery channels:

1. The server speaks invariant codes and the SPA translates them.
2. The delivery owner translates server-delivered text using an explicitly resolved recipient culture.
3. English (`en`) is the source language. Every supported language is complete; an `inProgress` language is never
   selected, negotiated, or exposed.
4. Language, locale, and time zone are separate settings and resolution concerns.
5. User-authored content is never translated. System-owned content is translated from stable keys.
6. Logs, audits, outbox payloads, codes, identifiers, URLs, developer exceptions, and OpenAPI remain invariant
   English.
7. A new language is catalog, resource, and registry data, and becomes supported only after its completeness gates
   pass.

Implement the standard as follows:

- Use one React catalog per language with `i18next` and `react-i18next`. Author in `en`; add neutral professional
  Spanish as `es`, registered as `inProgress` until extraction and all promotion gates are complete.
- Use `Microsoft.Extensions.Localization` and `.resx` resources for backend-owned delivered text.
- Resolve interactive server request language from the ASP.NET culture cookie, then `Accept-Language` with
  region-to-base fallback, then the configured default. The server never reads the account on each request; after
  sign-in, the SPA obtains the account preference from the identity context and writes it to the culture cookie.
- Persist `PreferredLanguage` for accounts and immutable language snapshots on invitations and delivery intents so
  background work never depends on the current request or worker culture.
- Return stable codes for validation failures, errors, enum values, permissions, and statuses. Catalogs own their
  display values.
- Add CI gates for registry, key, resource, placeholder, plural, and problem-code parity; server-message rendering;
  deterministic English SPA tests; SPA lint on every pull request; and a smoke journey for each supported non-English
  language.
- Amend repository rules so translation is distinct from a copy change, every supported language changes together,
  and visual-regression protections preserve the English source and accessibility contracts.

## Rationale

`i18next` with `react-i18next` provides reactive language changes, plurals, interpolation, rich-text composition,
and JSON resources without introducing a compile-time extraction pipeline. It needs no provider and initializes
synchronously with bundled resources, so existing renders and tests receive translated text immediately. `.resx`
resources use the established .NET localization stack and allow the backend to render a message under an explicit
`CultureInfo`. Stable API codes keep transport contracts testable and let each client own presentation.

The following alternatives were rejected:

- **react-intl:** `useIntl` requires an `IntlProvider` around every render, including all existing tests, without
  providing a capability that i18next lacks.
- **Lingui:** its extraction and compilation toolchain adds a build-time workflow that is unnecessary for the first
  two repository-managed languages.
- **A home-grown `t` function:** it would recreate fallback, plural, interpolation, escaping, loading, and React
  subscription behavior poorly.
- **`i18next-browser-languagedetector` or another stock language detector:** its browser-centric precedence and
  cookie format cannot implement the repository-owned culture cookie → `Accept-Language` → default resolution or
  ASP.NET's `c=<tag>|uic=<tag>` cookie directly. Resolve language directly instead of layering a detector with
  different precedence and storage semantics.
- **JSON localizers on the backend:** they bypass the standard .NET resource model and its culture fallback/tooling
  without providing a needed benefit.
- **A database catalog:** runtime edits would let translations drift independently from reviewed code and make
  deployments depend on mutable data.
- **A custom header added by the SPA's `fetch`:** it does not travel on full-page navigations such as external-login
  callbacks or links opened from email, while ASP.NET Core reads its culture cookie natively.
- **Server-localized validation:** sentences in API responses couple clients to one presentation and prevent the SPA
  from composing translated field labels with stable validation codes.
- **An organization default language:** no current requirement justifies another precedence rule; account preference,
  snapshots, and the deployment default cover known recipients.
- **A translation platform:** repository files keep the initial review loop simple. Reconsider Crowdin, Lokalise,
  Weblate, or an equivalent when a third language or non-developer translators create a demonstrated need.

## Consequences

### Easier

- Adding a language is data-only catalog, resource, and registry work once its content is complete.
- One UI catalog spans the SPA behind one translation/formatting facade instead of feature-specific mechanisms.
- Email and other server-delivered messages use the recipient's language rather than request or worker culture.
- CI enforces completeness, protocol-code coverage, deterministic tests, and promotion readiness.

### Harder

- Every system-copy change carries a permanent translation cost for every supported language.
- Existing English literals and backend templates require a staged, verbatim extraction.
- The rollout requires two SPEC amendments, and later behavior changes must keep the localization and affected
  feature contracts synchronized.
- Additional parity, rendering, lint, and smoke-journey gates make CI slower.
