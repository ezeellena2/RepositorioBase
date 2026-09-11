# Localization — cross-project standard and delivery plan

| | |
|---|---|
| Status | **Phase 0 and the Phase 1 implementation are delivered to `main`. Revised [Build run 34559082676](https://github.com/ezeellena2/RepositorioBase/actions/runs/34559082676) is green in `spa` and `build`, including HTTPS trust and Test solution. The Phase 2 baseline also passed: Vitest 320/320 in 29 files, ESLint 0 errors/1,385 warnings, Vite 2,493 modules, and .NET 1,333/1,333 including Acceptance 32/32. P2.1–P2.4 are accepted: extract English verbatim one folder at a time, without editing existing tests or adding Spanish translations.** |
| Last updated | 2026-09-11 |
| Scope | Backend (.NET), React SPA (`src/Web/ClientApp`), outbox-delivered messages, tests, CI, and the repository's working rules |
| Next action | Complete Phase 2 work unit 1 for `src/Web/ClientApp/src/components/`, then run its separate functional verification. |

This document is self-contained: a new session that reads only this file must be able to continue. It is also the
source of truth — agent memories (Claude auto-memory, Engram) are not shared by every tool.

## Contents

0. Handoff — read this first
1. Outcome
2. Where the repository stands
3. The standard in seven rules
4. Languages and resolution
5. Frontend standard (SPA)
6. Backend standard (.NET)
7. Requirements
8. Enforcement
9. Rules amended and artifacts added
10. Decisions
11. Phases — decisions, steps and exit criteria
12. Execution facts
13. Out of scope
14. Risks
15. Log

---

## 0. Handoff — read this first

### 0.1 State

- Every standard-level and Phase 0 decision is accepted (§10). The Phase 0 rule amendments, localization skill and
  reference, ADR-007, localization SPEC, and PR template are committed at `afe6c92` and pushed to
  `origin/claude/translation-strategy-plan-ba3438`.
- Phase 0 changed no application code, tests, or configuration. The user replaced PR delivery with a direct update to
  `main`; remote `main` was advanced by fast-forward and verified at exact commit `afe6c92`. GitHub Issues remains
  disabled because direct delivery did not need it (P0.4).
- The user confirmed that a successful login without `returnUrl` lands on `/identity`. The non-localization baseline
  journey repair was committed separately at `f21103d`, before the Phase 1 commit.
- P1.1–P1.5 are accepted as recommended (§11.3). Phase 1 was committed at `ef19d44` and fast-forward pushed to
  `origin/main` after the complete local verification passed. Revised [Build run 34559082676](https://github.com/ezeellena2/RepositorioBase/actions/runs/34559082676)
  passed both `spa` and `build`, including HTTPS trust and Test solution.
- For the workflow-only certificate correction, the user declined a Docker image: `git diff --check` is sufficient
  locally and Linux CI is the functional proof. Test Templates is manual-only after unrelated template drift; CodeQL
  is deferred. Phase 2 decisions proceed without waiting for CI.

### 0.2 How to work with the user

1. **Language.** Reply to the user in Rioplatense Spanish (voseo, warm, without heavy slang). Write every repository
   artifact — code, comments, docs, `en` catalog values, commit messages — in English. Spanish catalog values are
   neutral, professional Spanish (D1) (`AGENTS.md` "Persona Scope" and "Language").
2. **Decisions come first, per phase.** At the start of every phase, present that phase's decision table (§11) in
   Spanish, one recommendation per row, and wait. The user answers "ok" or changes rows. Record the answers in §10
   and §15 **before** executing.
3. **One question at a time** (`AGENTS.md:32`). If something blocks, ask the one question that unblocks it and stop.
4. **Delivery.** One conventional commit per phase directly on `main`, then push to `origin/main`; no pull requests
   or feature branches. After verification passes, no recurring authorization is needed. Never commit or push a
   change whose verification failed or did not run (P0.4).
5. **Autonomy and approval boundaries.** Do not ask before committing and pushing verified work to `main`, installing
   dependencies declared by this plan, running suites, or fixing failures within the current phase's scope. Ask only
   for the product decisions in each phase's §11 table; changes to GitHub, account, or machine configuration (including
   WSL, Docker, and settings); data deletion or history rewrites; and anything with a cost. Every question includes a
   recommendation.
6. **Verify honestly.** Run the baseline before changing anything in a phase and the full verification after it
   (§12.1). Report failed, skipped or unavailable checks plainly; never claim a check that did not run.
7. **Amend a rule before working against it** (§9.1). If a new conflict with an existing rule appears, stop, propose
   the amendment — file and line, old text, new text, why — and wait for the OK.
8. **Keep this file current.** At the end of every phase update the status header, §10, and the log (§15).

### 0.3 Read before starting

- This file, entirely.
- `CLAUDE.md`, and the project-owned parts of `AGENTS.md` (lines 1–114).
- `.agents/skills/engineering-standards/SKILL.md` and `references/engineering-rules.md` — for any code or test work.
- `.agents/skills/frontend-design-standards/SKILL.md` and `references/ui-composition-rules.md` — for any SPA work.
- From Phase 1 on: `.agents/skills/localization-standards/SKILL.md` (written in Phase 0).

### 0.4 Kickoff prompt for a new chat

```
Leé completo docs/features/localization/PLAN.md y seguí el protocolo de su sección 0.
Continuá desde la fase que indica el encabezado del plan.
```

---

## 1. Outcome

The product can be used in more than one language, and **every future change ships in every supported language by
construction**: a change that adds text a person reads without all of its translations does not pass CI. Adding a
language is a data change — catalog files and one registry entry — not a code change.

## 2. Where the repository stands (verified 2026-09-10)

| Area | Finding | Evidence |
|---|---|---|
| SPA copy | No i18n library. All copy is English literals in JSX (~6,400 lines of non-test JSX); the document declares `lang="en"`. | `src/Web/ClientApp/index.html:2`, `package.json` |
| API errors | Already the right shape: a stable machine code, and the SPA owns the sentence. This is the pattern the standard generalizes. | `ApplicationError.cs:5-7`, `ProblemMessage.jsx:5-57` |
| Error catalogue | OpenAPI already publishes each response's codes as `x-problem-codes`, so completeness can be checked mechanically. | `src/Web/Infrastructure/ApiExceptionOperationTransformer.cs:28` |
| Validation | `ValidationException` becomes `validation_failed` carrying FluentValidation's English sentences as field errors; the SPA prints `field: message` with the raw property name. Only four files declare `RuleFor` rules. | `ProblemDetailsExceptionHandler.cs:19-22`, `ProblemMessage.jsx:71` |
| Emails | English subjects and bodies built in 13 places in the outbox handlers (§6.3). They are prepared in `OutboxWorker`, where there is no HTTP request and no `Accept-Language`. | `src/Infrastructure/Outbox/` |
| Recipient language | Nothing in the model records anyone's language: not the account, the tenant, the invitation, or the registration intent. | `src/Domain/**` |
| Formatting | Dates rendered with `toLocaleString()` and no locale, so the output depends on the machine. | `InviteMemberPage.jsx:210,316` |
| System catalogues | Permission codes shown raw as chip labels; "built in" and "retired" are literals. Built-in role names are English constants set in code and shown as stored. | `RolesPage.jsx:231-242`; `RegistrationInitialRoleProvisioner.cs:15`; `ConfiguredPlatformBootstrapper.cs:73-74` |
| SPA tests | Pages render with no i18n provider (`<IdentityProvider><InviteMemberPage /></IdentityProvider>`); jsdom reports `en-US`. | `InviteMemberPage.test.jsx:15` |
| Journeys | Playwright contexts pin no locale, so the browser inherits the machine's. | `tests/Web.AcceptanceTests/PlaywrightSetup.cs:26-30` |
| CI | `build.yml` runs `dotnet build` and `dotnet test` only. **`vitest` and `eslint` never run in CI**, so any SPA-side gate is unenforced today. | `.github/workflows/build.yml` |
| Working rules | Several rules loaded by every agent session assume a single-language product: copy is frozen, tests are never edited, UI copy is English (§9.2). | `CLAUDE.md`, `AGENTS.md`, `frontend-design-standards` |
| Next channel | The WhatsApp bot (ADR-005/006, Proposed) will author server-side messages: the email problem, larger. | `docs/features/whatsapp-bot/SPEC.md` |

## 3. The standard in seven rules

1. **The server speaks codes; the client speaks languages.** Every response the SPA renders carries codes — error
   codes, enum values, permission codes, statuses — never display text. The SPA turns codes into words from its own
   catalogs. This is `ProblemMessage.jsx`, generalized.
2. **Whoever delivers the text translates it.** Text the server delivers outside the SPA — email today, WhatsApp next,
   any document or notification later — is localized in the backend, in the **recipient's** language, passed
   explicitly. Never the ambient culture of a worker.
3. **One source language, every supported language complete.** `en` is the source: keys are authored in `en` first
   and the test suites run in `en`. Every **supported** language is 100% complete on `main`; a missing, empty or
   placeholder-mismatched translation fails the build.
4. **Language, format and time zone are different things.** The language picks the words; the locale formats numbers
   and dates (`Intl.*`, `CultureInfo`); the time zone is a separate, future preference. The API keeps carrying
   ISO-8601 UTC instants and invariant numbers, never formatted values.
5. **People's content is never translated.** Tenant names, custom role names and people's names are data. What the
   system defines — built-in roles, permissions, statuses, document kinds — is translated by key.
6. **Machines read invariant English.** Logs, audit records, outbox payloads, error codes, identifiers, URLs and route
   paths, developer-facing exception messages, and OpenAPI stay invariant. Localization is presentation only.
7. **A new language is a data change.** It enters as `inProgress` — catalogs and resources grow without being offered
   to anyone — and is promoted to `supported` when the gates report it complete. No code changes.

## 4. Languages and resolution

### 4.1 Registry

One registry per side, kept equal by a test (L10N-REQ-003):

- SPA: `src/Web/ClientApp/src/i18n/languages.json`
- Backend: a constant list in code (P1.4); only the default language is configuration (`Localization:DefaultLanguage`).

```json
{
  "source": "en",
  "default": "en",
  "supported": ["en"],
  "inProgress": ["es"]
}
```

- **`supported`** languages are negotiated, offered in the selector, used for email, and must be complete.
- **`inProgress`** languages are being translated: never negotiated, never offered, never used for delivery; the
  parity gate reports their gaps without failing.
- `es` stays `inProgress` through Phases 1–2 and is promoted to `supported` at the end of Phase 3.
- `default` is `en` (D2). A deployment can change the backend's `Localization:DefaultLanguage`; if it does, that
  build's `languages.json` `default` must change too — the registry test compares the shipped `appsettings.json`
  value with `languages.json`.

### 4.2 Resolution order (identical on both sides)

1. An explicit choice: a signed-in person's `PreferredLanguage`; otherwise the language cookie.
2. Browser negotiation — `navigator.languages` in the SPA, `Accept-Language` on the server — over `supported`
   languages, falling back from region to base language (`es-AR` → `es`).
3. The default language.

"Identical on both sides" means identical ordering semantics, not identical data access. The SPA can apply the
account preference obtained from the identity context directly. The server never reads the account on each request:
after sign-in, the SPA materializes that preference as the culture cookie, so the server's concrete order is culture
cookie → `Accept-Language` → configured default.

### 4.3 Transport

- The SPA writes the standard ASP.NET Core culture cookie, `.AspNetCore.Culture`, **only when a person chooses a
  language** (the selector, or the account preference applied after sign-in). Without a choice, both sides negotiate
  from the same browser settings and reach the same answer.
- The cookie's value is `c=es` and `uic=es` joined by a vertical bar — not a bare language code. The SPA parses and
  writes it itself in `src/i18n/index.js`; do not point a stock cookie detector at it.
- The backend reads it with `RequestLocalizationMiddleware`: cookie provider first, then `Accept-Language`; the
  query-string provider is removed.
- The backend sets **only the UI culture** from the request: `SupportedUICultures` = the `supported` list,
  `SupportedCultures` = `en`. Server-side parsing and formatting therefore never change with the visitor's language.
- Why a cookie rather than a request header: it also travels on full-page navigations — external-login callbacks,
  links opened from an email — which a header added to the SPA's `fetch` never reaches, and ASP.NET Core reads it
  natively. It is not an authentication artifact, so IA-REQ-025 does not restrict it, and it carries no PII
  (IA-REQ-029).

### 4.4 Persistence (Phase 4; an identity-access SPEC amendment, A7)

- `PreferredLanguage` on the identity account — the `Users` row the outbox handlers already read for the recipient's
  address. Nullable: empty means "never chose" (P4.2). Registration stores the request's UI language.
- A `Language` snapshot on everything addressed to somebody who has no account yet: organization and platform
  invitations, registration and personal intents — captured from the request that created it (D5: the inviter's
  current language).
- Outbox handlers resolve the recipient's culture at delivery: account preference, else snapshot, else default.
- After sign-in the SPA applies the account's `PreferredLanguage` from the identity context and writes the cookie, so
  a new device converges on the person's choice.

## 5. Frontend standard (SPA)

### 5.1 Library: `i18next` + `react-i18next`

Chosen on its merits:

- It needs no React provider (a module-level instance). No render — in a test or in production — needs a wrapper, and
  plain modules such as the error-code mapper can translate too.
- It initialises synchronously with bundled resources, so the first render already has its text and synchronous
  queries keep finding it. (The option is `initAsync: false` in recent i18next versions and `initImmediate: false` in
  older ones — check the installed version.)
- Plurals through `Intl.PluralRules`, escaped interpolation, namespaces, and `eslint-plugin-i18next` for literal
  detection come with it.

Rejected: **react-intl** (FormatJS) — `useIntl` requires an `IntlProvider` around every render, test renders included,
for no capability i18next lacks. **Lingui** — a compile-time macro plugin plus a provider. **A home-grown `t()`** — it
would re-implement fallback chains, plural selection, namespaces and the literal lint that the dependency already
provides. **`i18next-browser-languagedetector`** — the resolution order is ours (§4.2) and the cookie format is
ASP.NET Core's; twenty lines in `src/i18n/index.js` are clearer than configuring around both.

### 5.2 Layout

```
src/Web/ClientApp/src/i18n/
  index.js              instance, resolution (§4.2), cookie, <html lang>; re-exports useTranslation, Trans, t
  languages.json        registry (§4.1)
  useFormat.js          Intl date, number and relative-time formatters bound to the active language (Phase 3)
  catalog.contract.test.js
  locales/
    en/  common.json  errors.json  enums.json  identity.json  platform.json
    es/  the same files with the same keys
```

- One namespace per feature, plus `common`, `errors` and `enums`: the five catalog namespaces are `common`, `errors`,
  `enums`, `identity` and `platform`.
- Only catalog files under `src/i18n/locales/<language>/*.json` become i18next namespaces, registered synchronously
  in `index.js`; `identity.json`, for example, registers the `identity` namespace. `languages.json` is registry
  metadata and is never registered or exposed as a translation namespace.
- Every language is bundled statically while catalogs are small; switch to one lazy `import()` per language when a
  language's catalog passes ~100 KB or there are more than four languages.
- **Components import `useTranslation` and `Trans` from `src/i18n`; plain modules import its `t` facade. Neither
  imports from `react-i18next`.** Importing the facade initialises it, so every render — tests included — has a ready
  instance without a provider and without touching `src/test/setup.js`. Enforced with `no-restricted-imports`
  outside `src/i18n`.

### 5.3 Keys

- Bind a component's feature namespace — `const { t } = useTranslation('identity')` — and use local semantic keys
  such as `t('invite.submit')`, never the English sentence.
- Qualify a different namespace, including calls through the facade's plain-module translator, with standard i18next
  syntax: `t('common:actions.cancel')`, `t('errors:invitation_conflict')`,
  `t('enums:tenantStatus.suspended')`.
- Preserve protocol codes verbatim in these entries: `errors:<code>` for API errors;
  `errors:validation.<code>` for validation; `enums:<type>.<value>` for enums and statuses;
  `enums:permissions.<code>` for permissions; `enums:roles.system.<name>` for built-in roles.
- Interpolation uses named placeholders: `{{name}}`, `{{count}}`, `{{expiresAt}}`.

### 5.4 Rules

- No user-visible literal in JSX or in any prop that reaches a person: `label`, `placeholder`, `aria-label`, `title`,
  `helperText`, `alt`, `document.title`. Enforced by `i18next/no-literal-string` in JSX mode.
- Interpolate, never concatenate: `t('invite.sent', { expiresAt })` from the bound `identity` namespace, not
  `'It expires on ' + date`. Plurals
  through `count`. `<Trans>` only when markup sits inside the sentence.
- Dates and numbers through `useFormat()`; never `toLocaleString()` without a locale.
- `document.documentElement.lang` follows the active language.
- MUI's own strings (pagination, autocomplete and similar) come from `@mui/material/locale`. `theme.jsx` keeps
  exporting `appTheme` unchanged — `materialUiMigration.contract.test.js:18-86` asserts it — and adds
  `themeFor(language)`, built from the same options plus MUI's locale; `App.jsx` uses `themeFor`. `App.jsx` must never
  contain the string `ThemeContext` (the same test, line 165).
- A language selector in the shell for signed-in and anonymous visitors (P3.5), and the preference on the personal
  account screen (Phase 4), composed under `frontend-design-standards` and the journey contracts in §12.3.
- Developer-facing `Error` messages (for example `useIdentityProof.js:134`, `problemDetails.js`) stay invariant
  English (rule 6) — unless one is shown to a person, in which case it goes through a key.
- Built-in roles (`isSystem`) show `enums:roles.system.<name>`; custom roles show the stored name (P3.7).
- **During extraction every `en` value equals today's copy, character for character.** That is what lets the existing
  tests and journeys prove, unedited, that each extraction changed nothing.

## 6. Backend standard (.NET)

### 6.1 Mechanism

`Microsoft.Extensions.Localization` with `.resx`, already part of ASP.NET Core: satellite assemblies, culture fallback
(`es-AR` → `es` → neutral `en`), no new package. Rejected: third-party JSON localizers (no gain for the cost) and a
database catalog (runtime editing is not a requirement).

### 6.2 What the backend localizes — and nothing else

- **Outbox-delivered messages**: the emails now, WhatsApp replies later. Resources live in
  `src/Infrastructure/Localization/` (`Emails.resx`, `Emails.es.resx`), keyed by message type and part
  (`Invitation.Subject`, `Invitation.Body`). Handlers read them with the recipient's `CultureInfo` passed explicitly —
  for example `ResourceManager.GetString(name, culture)` — and nothing in the worker depends on
  `CultureInfo.CurrentUICulture`. Emails stay plain text (P4.4); when they become HTML, move to one template file per
  language and message type.
- **Nothing returned to the SPA.** `ApplicationError.Detail` is not shown to people, and `Title` stays the invariant
  HTTP reason phrase.
- **Request language** is used for one thing: snapshotting it where the model needs it (registration →
  `PreferredLanguage`; invitation and intents → `Language`). Application reads it through a narrow port implemented in
  Web over `IRequestCultureFeature` (UI culture).
- **Validation messages travel as codes (D3).** Validators declare codes (`WithErrorCode("too_long")`) instead of
  sentences; field errors travel as `{ code, params }` (P5.2); the SPA renders the field's own translated label beside
  the translated rule. Rejected: FluentValidation's built-in translations chosen by the request culture — they keep the
  wire shape but leave a second catalog of UI text on the server.

### 6.3 Email construction sites (all in `src/Infrastructure/Outbox/`)

| File | `new IdentityEmail(` sites | Notes |
|---|---|---|
| `AccountLifecycleDeliveryHandlers.cs` | 2 | Account reactivation; lifecycle notice with three outcomes (`self_deactivated`, `administratively_suspended`, `reactivated`) |
| `EmailConfirmationDeliveryHandler.cs` | 1 | Also sent by `InvitedConfirmationDeliveryHandler.cs`, which inherits it |
| `InvitationEmailDeliveryHandler.cs` | 1 | Organization invitation |
| `SignInNoticeDeliveryHandler.cs` | 1 | |
| `PasswordRecoveryDeliveryHandler.cs` | 1 | |
| `PersonalIntentDeliveryHandlers.cs` | 2 | |
| `RegistrationIntentDeliveryHandlers.cs` | 2 | |
| `PlatformDeliveryHandlers.cs` | 3 | |

## 7. Requirements

Phase 0 copies this table into `docs/features/localization/SPEC.md`, which becomes the normative home.

| ID | Requirement | Proved by | Phase |
|---|---|---|---|
| L10N-REQ-001 | The SPA renders no user-visible text that does not come from a catalog key. | `i18next/no-literal-string` at `error` | 1 `warn` → 2 per folder → 6 global |
| L10N-REQ-002 | Every `supported` language has every `en` semantic key, non-empty, with the same named placeholders and every plural form its own locale needs; plural categories may differ, and `inProgress` languages are reported, not enforced. | Catalog parity test | 1 |
| L10N-REQ-003 | The SPA and the backend declare the same source, default, supported and in-progress languages. | Registry-sync test | 1 |
| L10N-REQ-004 | The active language resolves from an explicit choice (account preference, then the culture cookie), then browser negotiation over `supported` languages with region → base fallback, then the default. | SPA resolver tests; backend request-culture test | 1; account in 4 |
| L10N-REQ-005 | `document.documentElement.lang` equals the active language. | SPA test; `es` smoke journey | 3 |
| L10N-REQ-006 | API responses carry codes — error, enum, permission, status and field-validation codes — never display text. | Review; validator architecture test | 5 |
| L10N-REQ-007 | Every problem code the API declares in `x-problem-codes` has an `errors:<code>` message in every supported language. | Error-code coverage test | 2 |
| L10N-REQ-008 | Server-delivered messages render in the recipient's language — account preference, else the snapshot captured with the request, else the default — with the culture passed explicitly. | Functional tests per message type | 4 |
| L10N-REQ-009 | Every server-delivered message type renders in every supported language. | Email-completeness test | 4 |
| L10N-REQ-010 | Every backend resource key exists, non-empty and with the same placeholders, in every supported culture. | Resource-parity test | 1 (vacuous until 4) |
| L10N-REQ-011 | Dates and numbers shown to people are formatted for the active language with an explicit locale; the API carries ISO-8601 UTC instants and invariant numbers. | `useFormat` tests; no `toLocaleString()` without a locale | 3 |
| L10N-REQ-012 | User-authored content is never translated; system-defined catalogs are translated by key. | SPA tests for roles and permissions | 3 |
| L10N-REQ-013 | Logs, audit records, outbox payloads, error codes, identifiers, URLs and route paths stay invariant. | Review; existing audit and outbox tests unchanged | All |
| L10N-REQ-014 | Acceptance journeys run in the source language on any machine; every supported language other than `en` has a smoke journey. | `PlaywrightSetup` `Locale`; per-language journey | 1; 3 |
| L10N-REQ-015 | CI runs the SPA tests and lint on pushes to `main` (and continues to support pull requests). | `build.yml` `spa` job | 1 |
| L10N-REQ-016 | A signed-in person's language choice persists on their account and applies on every device after sign-in. | Functional and SPA tests | 4 |

## 8. Enforcement

Documentation reminds; gates enforce. Every row fails the build once its phase lands.

| Gate | Where | Fails when | Phase |
|---|---|---|---|
| SPA checks run in CI | `build.yml` `spa` job: `npm ci`, `npx vitest run`, `npx eslint src/` | Prerequisite for every *SPA* row | 1 |
| Catalog parity *(SPA)* | `src/i18n/catalog.contract.test.js` | A `supported` language differs in semantic keys, has an empty value or different named placeholders, or lacks a plural category its own locale requires | 1 |
| Missing key at render *(SPA)* | i18next missing-key handler (P1.3) | A rendered component asks for a key that does not exist, in tests | 1 |
| No literals *(SPA)* | `eslint-plugin-i18next` `no-literal-string` | User-visible text is written in JSX | 1–6 |
| Error-code coverage | Backend test reading the OpenAPI document | Any `x-problem-codes` value has no entry in some supported language's `errors.json` | 2 |
| Resource parity | .NET architecture test | A `.resx` key is missing, empty, or has different `{n}` placeholders in some supported culture | 1 |
| Registry in sync | .NET test reading `languages.json` | The SPA and backend registries, or the default, differ | 1 |
| Deterministic journeys | `PlaywrightSetup.NewContextAsync` sets `Locale = "en-US"` | — pins journeys to the source language | 1 |
| One journey per extra language | Smoke journey: choose the language, sign in, open a screen | A raw key is visible, or `html[lang]` is wrong | 3 |
| Email completeness | .NET test rendering each outbox email type in each supported language | A message type cannot be rendered in a supported language | 4 |
| Validators declare codes | .NET architecture test over every validator | A rule has no code from the vocabulary | 5 |
| Unused keys | `i18next-cli` scan | A catalog key is used nowhere | 6 |
| Pseudo-locale (development only) | `?lng=en-XA`: accented, ~35% longer | — exposes unextracted strings and truncation by eye | 6 |

## 9. Rules amended and artifacts added

### 9.1 Principles

Decided 2026-09-10: where an existing rule stands in the way of this standard, the rule changes. Two conditions make
that safe:

- **Amend, never bypass.** The new wording lands in the file that holds the rule, before or together with the work
  that needs it. Every session loads `CLAUDE.md`, `AGENTS.md` and the skills, and an agent obeys the text it reads: a
  rule that says one thing while the code does another is worse than no rule.
- **Re-aim the protection, don't drop it.** Each rule below protects something real. The amendment keeps what it
  protects and changes only what no longer fits a multi-language product.

### 9.2 Amendments

| # | Where | Rule today | Amended rule | Why | Phase |
|---|---|---|---|---|---|
| A1 | `CLAUDE.md:30-32`, `AGENTS.md:114`, `.agents/skills/frontend-design-standards/SKILL.md:71` | A visual change never alters "any user-visible string" or "the text of a label, a button, a heading, or a message" | A visual change never alters the **`en` source value** of a key. Writing another language's value is translation, not a copy change. A copy change is a change of its own, made in every language | Keeps the protection — tests and people find controls by accessible name — and points it at the catalog | 0 |
| A2 | `CLAUDE.md:40-41`, `frontend-design-standards/SKILL.md:80` | Never edit a `*.test.jsx`, a page object under `tests/`, `AppRoutes.jsx`, or `features/*/api/` | Unchanged for **visual** changes. A **functional** change may edit them where they assert the new behavior, listed file by file in the change; never to make a regression pass | The rule was written for restyles. This standard needs a harness setting (`Locale`), new tests, and new API members | 0 |
| A3 | `CLAUDE.md:30,62`, `AGENTS.md:114`, `frontend-design-standards/SKILL.md:66` | Fixed counts ("306 SPA tests", "32 journeys") and no changes under `tests/` | "Every SPA test and journey passes", and no changes under `tests/` beyond those the change declares | New tests change the counts; a stale number reads as a regression | 0 |
| A4 | `AGENTS.md:56`, `AGENTS.md:239` | UI labels and copy "are in English" unless another language is requested | UI copy is **authored** in `en` and **delivered** in every supported language through the catalogs. Spanish values are neutral, professional Spanish, as `AGENTS.md:60` already requires | The rule chose the language an agent writes in; it never meant the product speaks one language | 0 |
| A5 | `frontend-design-standards/SKILL.md:25-27` | `theme.jsx` overrides component defaults only where one breaks something | It also composes MUI's locale for the active language (`themeFor`), and keeps exporting `appTheme` | Otherwise MUI's own strings stay in English | 0 |
| A6 | `frontend-design-standards/SKILL.md:55`; `references/ui-composition-rules.md` (lines 113, 130, 134 and every other literal in its examples) | Examples write copy inline (`helperText="Include the check digit."`) | Examples call `t()` | Agents copy the reference's markup; inline copy in the examples would reintroduce literals | 0 |
| A7 | `docs/features/identity-access/SPEC.md`: IA-REQ-038 and §7 "React context contract" | Field errors carry sentences; the identity context carries no language | Field errors carry `{ code, params }` (D3, P5.2); the identity context carries `preferredLanguage`; a preference endpoint exists | Rule 1 of this standard; the SPA needs the account's choice after sign-in | 4 and 5 |

A4 is written as an explicit override in the project-owned part of `AGENTS.md` — next to the existing project
standards, the way `frontend-design-standards` already overrides generic advice (`AGENTS.md:113`) — and **not** by
editing `AGENTS.md:56` or `AGENTS.md:239`. Line 239 sits inside the Gentle AI managed block
(`<!-- gentle-ai:sdd-orchestrator -->`), which a Gentle AI reinstall rewrites; an edit there would be silently undone.

### 9.3 Exact text for Phase 0

**`CLAUDE.md`, lines 30–32** — replace the paragraph that begins "The SPA has 306 tests" with:

```
The SPA's tests and Reqnroll/Playwright journeys find controls the way a person does. While restyling, never
change an `id`, `name`, `data-testid`, `role`, heading level, `type`, `autoComplete`, `required`, a `disabled`
expression, or the `en` source value of any user-visible string. Writing another language's value is translation,
not a copy change; a copy change is a change of its own, made in every supported language. Specifically:
```

**`CLAUDE.md`, lines 40–41** — replace the bullet that begins "Never edit a `*.test.jsx`" with:

```
- In a visual change, never edit a `*.test.jsx`, a page object under `tests/`, `AppRoutes.jsx`, or anything under
  `features/*/api/`; if a test fails after a redesign, fix the implementation. A functional change may edit them
  where they assert the new behavior, listed file by file in the change — never to make a regression pass.
```

**`CLAUDE.md`, lines 62–63** — replace the "Expected:" sentence with:

```
Expected: every SPA test passes, lint clean, build succeeds, every journey passes, and `git status` shows no changes
under `tests/` beyond those the change declares.
```

**`CLAUDE.md`** — add this section after "Contracts a visual change must never break":

```
## Localization: every language, every change

Anything a person reads — SPA copy, email, bot messages — and every new API error code, enum value, permission or
status follows [.agents/skills/localization-standards/SKILL.md](.agents/skills/localization-standards/SKILL.md).
Read it before writing the change. Plan, decisions and current phase:
[docs/features/localization/PLAN.md](docs/features/localization/PLAN.md).

- The server speaks codes; the client speaks languages. The API never returns display text.
- Whoever delivers the text translates it: server-delivered messages use the recipient's language, passed
  explicitly — never the worker's ambient culture.
- `en` is the source language, and every supported language is complete before merge. A missing translation fails
  CI.
- No user-visible literal in JSX: copy comes from `t()`, imported from `src/i18n`. Dates and numbers go through
  `useFormat()`.
- Logs, audit records, outbox payloads, error codes, identifiers and URLs stay invariant English.

Screens not yet extracted still hold literals; the plan's header says which phase is current. New or changed text
uses the catalogs from Phase 1 on.
```

**`AGENTS.md`, line 114** — replace with:

```
- The SPA's tests and the Reqnroll/Playwright journeys locate controls by role, label, id and accessible name. A visual change that alters an `id`, a label, a `role`, a heading level, the `en` source value of copy, or a `required` field's accessible name is a regression. Translating another language is not a copy change. The skill lists the exact contracts; honour them or do not make the change.
```

**`AGENTS.md`** — add after line 114, still outside every `<!-- gentle-ai:... -->` block:

```
### Project localization standards

- Register [localization-standards](.agents/skills/localization-standards/SKILL.md) as the local skill for any change that adds or changes text a person reads (SPA, email, bot messages) or adds an API error code, enum value, permission or status. Agents doing that work must read it and its [localization rules](.agents/skills/localization-standards/references/localization-rules.md) before starting.
- For each delegation that touches user-facing text, inject `C:/Users/ezequ/source/repos/RepositorioBase/.agents/skills/localization-standards/SKILL.md` under `Skills to load before work`. Pass the exact path, not copied rules.
- **This overrides the "default to English" rules for UI copy in Persona Scope and in the SDD Language Domain Contract.** UI copy is authored in `en`, the source language, and delivered in every supported language through the catalogs; the other languages are not optional. Spanish values use neutral, professional Spanish.
```

**`.agents/skills/frontend-design-standards/SKILL.md`**

- Lines 25–27: append to the `theme.jsx` bullet — "It also composes MUI's locale for the active language
  (`themeFor(language)`), and keeps exporting `appTheme`, which a contract test asserts."
- Line 55: replace `helperText="Include the check digit"` with `helperText={t('people.document.checkDigitHint')}`.
- Line 66: replace `306 SPA tests and 32 Reqnroll/Playwright journeys` with
  `the SPA tests and the Reqnroll/Playwright journeys`.
- Lines 71–72: replace the bullet with — "Never change the `en` source value of a label, a button, a heading, or a
  message. Translating another language is not a copy change. If copy is genuinely wrong, say so and leave it: a copy
  change is a change of its own, made in every language."
- Line 80: replace with — "In a visual change, never edit a `*.test.jsx`, a page object under `tests/`,
  `AppRoutes.jsx`, or anything under `features/*/api/`. Functional changes follow `CLAUDE.md`."
- Add hard rule 11 — "**Copy comes from the catalog.** Every string a person reads is `t('…')` from `src/i18n`,
  including `aria-label`, `helperText`, `title` and empty-state lines. See `localization-standards`."

**`.agents/skills/frontend-design-standards/references/ui-composition-rules.md`** — every example string becomes a
`t('…')` call with a plausible key.

**`.agents/skills/engineering-standards/SKILL.md`** — add to "Hard Rules":

```
- Product-facing system text delivered through the SPA, email, or bot ships with a key in every supported language; new API error codes, enum values, permissions, and statuses ship with their catalog entries. Documentation, logs, and developer diagnostics stay invariant under the linked contract. Follow [localization-standards](../localization-standards/SKILL.md).
```

**`docs/features/identity-access/SPEC.md`**

- §12 step 1 (line 444): "create or update a specification with requirements, invariants, contracts, errors, **the
  user-facing copy and messages it introduces,** and acceptance examples."
- §13, after "a React screen is included when behavior is user-visible;": add "copy, messages, error codes, enum
  values and permissions exist in every supported language;".

### 9.4 New artifacts written in Phase 0

**Skill** — `.agents/skills/localization-standards/SKILL.md`, the same shape as `frontend-design-standards`:

```
---
name: localization-standards
description: "Trigger: any change that adds or changes text a person reads (SPA, email, bot) or adds an API error code, enum value, permission or status. Apply the repository's cross-project localization standard."
license: Apache-2.0
metadata:
  author: repository-maintainers
  version: "1.0"
---
```

Sections: *Activation Contract* · *The standing decision* (the seven rules of §3) · *Hard rules — SPA* (§5.2–5.4) ·
*Hard rules — backend* (§6.2) · *Contracts that outrank convenience* (`en` values verbatim; tests run in `en`;
`appTheme` export; §12.3) · *Decision gates* (table below) · *Execution steps* · *Output contract* (which keys were
added, in which languages, which gates ran) · *References*.

| Situation | Action |
|---|---|
| Adding text a person reads | Add the key to `en` and to every supported language in the same change |
| Adding an API error code | Add `errors:<code>` in every supported language; the coverage test fails otherwise |
| Adding a validation code | Add `errors:validation.<code>` in every supported language |
| Adding an enum value or status | Add `enums:<type>.<value>` in every supported language |
| Adding a permission or built-in role | Add `enums:permissions.<code>` or `enums:roles.system.<name>` in every supported language |
| Text the server sends (email, bot) | A backend resource in every supported culture; the culture passed explicitly |
| Tempted to return display text from the API | Return a code; translate in the SPA |
| Unsure of the Spanish | Draft it and flag it for native review before pushing to `main`; never merge an empty value |
| English copy reads badly | A separate copy change, in every language |

`references/localization-rules.md`: file layout, key conventions, namespaces, examples of `t()`, `<Trans>`, plurals and
`useFormat`, backend resource use with an explicit culture, the gate list, and the add-a-key checklist.

**ADR** — `docs/decisions/ADR-007-Adopt-Cross-Project-Localization.md`, from `ADR-000-template.md`:

- Status **Accepted**, date 2026-09-10 (P0.1).
- Context: §2, condensed.
- Decision: the seven rules; `en` source and `es` target with staged languages; i18next + react-i18next;
  `Microsoft.Extensions.Localization` with `.resx`; the culture cookie with `Accept-Language`; `PreferredLanguage`
  plus snapshots; validation codes; CI gates; amended rules.
- Rationale — rejected: react-intl, Lingui, a home-grown `t()`, the stock language detector, JSON localizers, a
  database catalog, a request header, server-localized validation messages, an organization default language (for
  now), a translation platform (for now).
- Consequences — easier: a new language is data; one catalog for UI; email in the recipient's language; enforced by
  CI. Harder: every change carries its translations, a permanent cost per language; an extraction phase; two SPEC
  amendments; a slower CI.
- Add the row to `docs/decisions/README.md`:
  `| [ADR-007](ADR-007-Adopt-Cross-Project-Localization.md) | Adopt a Cross-Project Localization Standard | 2026-09-10 | Accepted |`

**SPEC** — `docs/features/localization/SPEC.md` (P0.2): objective; scope and non-goals (§13); domain language (source
language, supported, in progress, default, preferred language, snapshot, catalog, key, namespace); the normative
requirements of §7; a traceability table (requirement → gate → phase → evidence, evidence filled as phases land).

**PR template** — `.github/pull_request_template.md`:

```
## Summary

## Verification

## Localization

- [ ] Every supported language is updated, or this change has no user-facing text.
```

## 10. Decisions

### 10.1 Standard-level and delivery (accepted 2026-09-10, as recommended)

| # | Decision | Taken | Why |
|---|---|---|---|
| D1 | First target language | Neutral `es`; a regional file only for a real difference | One catalog; the `es-AR` → `es` fallback serves Argentine browsers; `AGENTS.md:60` already asks for neutral Spanish |
| D2 | Default for a visitor with no choice and no browser match | `en`, configurable per deployment (§4.1) | Negotiation already gives Spanish browsers `es`; the default only meets everyone else |
| D3 | Validation field errors | Codes | One catalog; the field's translated label instead of a property name; a checkable rule |
| D4 | Organization default language | Not now | Account preference plus snapshots cover every email; add it when a tenant needs it |
| D5 | Language of an invitation email | The inviter's current language | The best available proxy; a selector on the invite form can follow |
| D6 | Translation workflow | Files in the repository, reviewed before push to `main` | Revisit a translation platform (Crowdin, Lokalise, Weblate) at the third language or when non-developers translate |
| D7 | Existing rules that stand in the way | Amended (§9), including the `Locale` line in `PlaywrightSetup.cs` | A stale rule would make every later session work against the standard |
| D8 | Delivery policy | Work directly on `main`: conventional commit and push after verification, with no pull requests or feature branches | Verification is the delivery gate; failed or unrun verification is reported instead of committed or pushed |

### 10.2 Per phase

| Phase | State |
|---|---|
| 0 | Delivered directly to `main` at exact commit `afe6c92` after fast-forward and remote-ref verification — 2026-09-10 |
| 1 | Implemented, fully verified locally, committed at `ef19d44`, and fast-forward pushed to `origin/main` after the separate baseline journey repair at `f21103d`. The [first Build run](https://github.com/ezeellena2/RepositorioBase/actions/runs/34557253186) passed `spa`; its build compiled and passed Domain 186, Application Unit 187, Infrastructure Integration 302, and Application Functional 626, but Web Acceptance failed 3/32 because auxiliary `HttpClient` calls rejected the Ubuntu ASP.NET development certificate with `AuthenticationException UntrustedRoot`. The [rerun](https://github.com/ezeellena2/RepositorioBase/actions/runs/34558083319) passed `spa` but build failed in `Trust development HTTPS certificate` with exit 4 because OpenSSL lacked `SSL_CERT_DIR` wiring. Revised [Build run 34559082676](https://github.com/ezeellena2/RepositorioBase/actions/runs/34559082676) passed both `spa` and `build`, including HTTPS trust and Test solution. Test Templates run [34557253244](https://github.com/ezeellena2/RepositorioBase/actions/runs/34557253244) failed for unrelated template drift and is manual-only; CodeQL is deferred. Phase 2 decisions continue without awaiting CI. |
| 2 | Decisions P2.1–P2.4 accepted as recommended — 2026-09-11. Baseline passed with exit 0: Vitest 320/320 in 29 files; ESLint 0 errors/1,385 warnings; Vite 2,493 modules; .NET 1,333/1,333 (Domain 186, Application Unit 187, Infrastructure 302, Functional 626, Acceptance 32); isolated Acceptance 32/32. Docker 28.5.1 was healthy with no competing AppHost/dcp. Extract shell and `common`, errors, login and registration, remaining identity, and platform in that order. Each folder is committed and pushed directly to `main`; existing tests stay unedited and no Spanish values are added. |
| 3–6 | Pending |

## 11. Phases — decisions, steps and exit criteria

### 11.1 Overview

Every phase is one reviewable change with one conventional commit directly on `main`, pushed after verification
(P0.4). "No-op" means every existing SPA test and journey passes without being edited.

| Phase | Content | Users see |
|---|---|---|
| 0 — Decide and amend | Rule amendments, skill, ADR, SPEC, PR template | Nothing |
| 1 — Foundations and gates | CI runs the SPA; i18n plumbing with empty catalogs; the gates | Nothing (no-op) |
| 2 — Extract the SPA to `en` | Every literal moves to the `en` catalog verbatim | Nothing (no-op) |
| 3 — Spanish in the SPA | `es` catalogs, selector, formatting; `es` promoted to `supported` | The SPA in Spanish |
| 4 — Spanish outside the SPA | Preferred language, snapshots, localized emails | Email in Spanish; the choice follows the person |
| 5 — Validation codes | Field errors as codes | Translated field errors |
| 6 — Harden | Lint `error` everywhere, unused-key gate, pseudo-locale, runbook | Nothing |

### 11.2 Phase 0 — Decide and amend

**Decisions (accepted 2026-09-10)**

| # | Question | Answer | Why |
|---|---|---|---|
| P0.1 | ADR-007 status | Accepted, dated 2026-09-10 | D1–D7 are decided; `Proposed` would say they are not |
| P0.2 | A localization SPEC with `L10N-REQ-###`, or only the ADR and the skill | The SPEC | Repository practice (identity-access SPEC §12 step 1), and the gates need requirements to trace to |
| P0.3 | Wording of the amendments | A1–A6 exactly as in §9.3 | Each keeps what the rule protected |
| P0.4 | How each phase is delivered | One conventional commit per phase directly on `main`, pushed without a PR after verification | Verification is the delivery gate; a direct main commit keeps each phase independently reviewable |

**Steps**

1. `git status`; confirm the branch and that this file is present. Read the files in §0.3.
2. `CLAUDE.md`: apply A1–A3 and add the localization section (§9.3).
3. `AGENTS.md`: replace line 114 and add "Project localization standards" outside the managed blocks (§9.3).
4. `frontend-design-standards`: apply A1, A2, A3, A5, A6 and hard rule 11 to `SKILL.md`; convert the reference's
   examples to `t()` (§9.3).
5. `engineering-standards/SKILL.md`: add the hard rule (§9.3).
6. Write the skill and its reference (§9.4).
7. Write ADR-007 and its README row (§9.4).
8. Write `docs/features/localization/SPEC.md` (§7, §9.4).
9. identity-access `SPEC.md`: the §12 and §13 additions (§9.3).
10. Write `.github/pull_request_template.md` (§9.4).
11. Update this file's header, §0.1, §10.2, and §15 to the transitional status: Phase 0 is implemented locally,
    with its commit and direct push pending.

**Implementation-ready gate**: every changed file is read back; `git diff --stat` lists only documentation and skills;
no application code, tests, or configuration changed.

**Delivery gate**: after verification passes, first finalize this PLAN's header, §0.1, §10.2, and §15 to record
Phase 0 as delivered, then create the single conventional Phase 0 commit directly on `main` and push it to
`origin/main`; no PR or feature branch. Suggested message:
`docs(localization): adopt the cross-project localization standard`.

### 11.3 Phase 1 — Foundations and gates

**Decisions (accepted 2026-09-10)**

| # | Question | Recommendation | Why |
|---|---|---|---|
| P1.1 | Where the SPA checks run in CI | A separate `spa` job in `build.yml`, inside the template's `#if (!UseApiOnly)` guard | Runs beside the .NET job and fails under its own name; the guard keeps the API-only template output valid |
| P1.2 | If `vitest` or `eslint` is already red when CI starts running them | Fix that first, in its own change | A gate that starts red gets ignored |
| P1.3 | What happens when code asks for a key that does not exist | Throw in tests; console error in development; in production fall back to `en`, then to the key | A missing key fails the suite instead of reaching a person |
| P1.4 | Where the backend's supported languages live | In code; only the default language is configuration | Languages ship with compiled resources, so a configured language without catalogs would be a trap |
| P1.5 | Lint level while extracting | `warn` globally, `error` per finished folder | Visible progress without one giant change |

**Steps**

1. Present P1.1–P1.5 and record the answers.
2. Baseline (§12.1): SPA tests, lint and build; .NET tests; journeys. Record the counts in §15. If `vitest` or `eslint`
   is red, apply P1.2.
3. CI (P1.1): a `spa` job — checkout; `actions/setup-node` with Node 24.x and the npm cache on
   `src/Web/ClientApp/package-lock.json`; then, in `src/Web/ClientApp`, `npm ci`, `npx vitest run`, `npx eslint src/`.
   It needs no .NET build (§12.2). If a build step is added, use `npx vite build`, not `npm run build` (§12.4).
4. Dependencies: `i18next`, `react-i18next`; development: `eslint-plugin-i18next`.
5. `src/i18n/` (§5.2): `index.js` — instance; synchronous initialisation with bundled resources; `supportedLngs` from
   the registry; `fallbackLng: 'en'`; region → base; missing-key behavior (P1.3); resolution (§4.2) with the cookie
   parsed and written by hand (§4.3); `document.documentElement.lang`; re-exports. `languages.json` as in §4.1.
   `locales/en/*.json` and `locales/es/*.json` as `{}`. Import `./i18n` in `main.jsx`.
6. `src/i18n/catalog.contract.test.js` (a declared new test, A2): parity for `supported`, a report for `inProgress`,
   placeholders, plural forms, the registry's shape.
7. ESLint: `i18next/no-literal-string` with `mode: 'jsx-only'` at `warn` (P1.5); `no-restricted-imports` for
   `react-i18next` outside `src/i18n`.
8. Backend: the registry constant (P1.4); `Localization:DefaultLanguage` in `appsettings.json`; `AddLocalization()`;
   `UseRequestLocalization` configured as in §4.3, placed early in `src/Web/Program.cs`, before endpoint mapping.
9. .NET tests: registry sync (reads `languages.json` and the shipped default); resource parity (scans every `*.resx`
   under `src/` — vacuous until Phase 4).
10. `tests/Web.AcceptanceTests/PlaywrightSetup.cs`: `Locale = "en-US"` in `NewContextAsync` (D7).
11. Prove the gate: temporarily add `es` to `supported` with a missing key — the parity test fails — and revert.
    Record it in §15.

**Exit criteria**: every existing test and journey passes; the new tests pass; the `spa` and `build` jobs are green
on the push to `main`; the parity gate was seen failing once.

### 11.4 Phase 2 — Extract the SPA to `en`

**Decisions (accepted 2026-09-11)**

| # | Question | Recommendation | Why |
|---|---|---|---|
| P2.1 | Extraction order | Shell and `common` → errors → login and registration → the rest of identity → platform | Every screen uses the shell; errors are already a catalog |
| P2.2 | Size of each change | One feature folder per change | Reviewable diffs; each change flips its folder's lint to `error` |
| P2.3 | Fix awkward English while extracting | No — note it, and fix it later as a copy change in every language (A1) | Extraction must be a no-op so the unchanged tests can prove it |
| P2.4 | Permission codes shown raw | Keep the codes in Phase 2; decide human names in P3.4 | Changing them changes what tests read |

**Steps, per folder**

1. Move every user-visible string into the folder's namespace in `en`, **verbatim**, and replace it with `t()`. Keep
   element types, roles, ids and heading levels exactly as they are (§12.3).
2. `ProblemMessage.jsx` → `errors.json`: each code becomes `errors:<code>`; the fallback becomes `errors:unknown`
   ("That request could not be completed."); the retry line becomes `errors:retryAfter` with `{{seconds}}`.
3. The tenant chooser's `"{name} (current)"` becomes one key with `{{name}}` whose `en` value keeps
   `" (current)"` exactly — a page object strips that suffix (§12.3).
4. Leave `toLocaleString()` alone; Phase 3 replaces it.
5. Check `.js` hooks and helpers for strings that reach the screen; the JSX-only lint does not see them.
   Developer-facing `Error` messages stay (rule 6).
6. Flip the folder's lint to `error`; run the SPA tests, lint and build.
7. With `errors.json` populated, add the error-code coverage test (§8).
8. Run the journeys at least at the end of the phase.

**Exit criteria**: every feature folder at lint `error`; no test edited; journeys green; coverage test green.

### 11.5 Phase 3 — Spanish in the SPA

**Decisions (pending)**

| # | Question | Recommendation | Why |
|---|---|---|---|
| P3.1 | Register: *tú* or *usted* | *Tú* | The usual register of neutral Latin American software; *vos* would need `es-AR` (D1) |
| P3.2 | Terminology | A glossary first (`docs/features/localization/GLOSSARY.md`): organization, tenant, sign in, session, role, membership, invitation, Platform | The same term everywhere; translation and review check against it |
| P3.3 | Who translates and who reviews | Drafted in the repository; reviewed by a native speaker before push to `main` | D6: files in the repository, reviewed before push to `main` |
| P3.4 | Human names for permissions | A human label, with the code as secondary text — after checking `RolesPage.test.jsx` and the page objects | People read names; operators and tests read codes. Page objects read role names, not permission codes (verified 2026-09-10) |
| P3.5 | Language selector | In the shell: a native `select`, each language named in itself ("English", "Español"), no flags | Flags name countries, not languages; a native `select` is the repository's pattern |
| P3.6 | Date and time format | `Intl` medium date and short time, in the browser's time zone | No time-zone preference exists yet (§13) |
| P3.7 | Built-in role names | `isSystem` roles show `enums:roles.system.<name>`; custom roles show the stored name | System names are code constants; the `en` value keeps the name page objects check by |

**Steps**

1. Present P3.1–P3.7; write and approve the glossary.
2. Translate every `es` catalog.
3. Selector in the shell (P3.5): writes the cookie, switches language live, respects §12.3.
4. `html[lang]`; `themeFor(language)`; `useFormat()` replacing `toLocaleString()` (`InviteMemberPage.jsx:210,316`).
5. Permission labels (P3.4) and built-in role names (P3.7).
6. The `es` smoke journey — a new feature file, steps and page object (declared under A2): choose Español, sign in,
   open `/identity`; assert `html[lang="es"]` and that no raw key is visible.
7. Promote `es` to `supported` in `languages.json` and in the backend registry; the parity gate is now strict for it.

**Exit criteria**: `es` complete and promoted; parity strict and green; the `es` journey green; native review done.

### 11.6 Phase 4 — Spanish outside the SPA

**Decisions (pending)**

| # | Question | Recommendation | Why |
|---|---|---|---|
| P4.1 | Where `PreferredLanguage` lives | On the identity account | The outbox already reads that row for the recipient's address |
| P4.2 | Existing accounts | Nullable, no back-fill: empty means "never chose" and resolves to the default at delivery | Keeps chosen and inferred apart, and follows the default if it ever changes |
| P4.3 | Where a signed-in person changes it | The shell selector saves it to the account; the account screen shows it | One control, and the choice follows the person to other devices |
| P4.4 | Email format | Keep plain text; localize it as it is | HTML is a separate change (§6.2) |
| P4.5 | A language control on the invite form | Not now (D5) | The inviter's language covers it; add it on request |
| P4.6 | The identity-access SPEC amendment (A7) | Approve it as its own requirement block | Behavior is specified before code (SPEC §12) |

**Steps**

1. Present P4.1–P4.6; write the SPEC amendment (A7) and get it approved.
2. Migration: nullable `PreferredLanguage` on the account; `Language` on organization and platform invitations and
   on registration and personal intents.
3. The request-language port (Application) and its Web implementation (§6.2); capture on registration, invitations
   and intents.
4. `Emails.resx` and `Emails.es.resx` covering every site in §6.3; each handler resolves the culture (§4.4) and reads
   resources with it explicitly.
5. A preference endpoint (authenticated and antiforgery-protected, in the style of the identity-access HTTP contract)
   and `preferredLanguage` in the identity context response.
6. SPA: apply the preference after sign-in; the selector saves it when signed in (P4.3). A declared functional change
   under `features/identity/api/` (A2).
7. Tests: email completeness per language; functional tests (registration captures the language, invitation
   snapshots it, delivery uses preference → snapshot → default); the migration against real PostgreSQL.

**Exit criteria**: every email renders in every supported language; the new and existing tests green.

### 11.7 Phase 5 — Validation codes

**Decisions (pending)**

| # | Question | Recommendation | Why |
|---|---|---|---|
| P5.1 | Code vocabulary | Our own snake_case codes (`required`, `too_long`, `invalid_format`), not FluentValidation's validator names | The same style as `ApplicationError.Code`, and independent of the library |
| P5.2 | Wire shape of a field error | `{ code, params }`, e.g. `{ "code": "too_long", "params": { "max": 100 } }` | A bare code cannot state the limit |
| P5.3 | A code the SPA does not know | A generic "This value is not valid" beside the field | A raw code never reaches a person |

**Steps**

1. Present P5.1–P5.3; amend IA-REQ-038 (A7).
2. Every validator declares `WithErrorCode(...)` from the vocabulary; `ValidationException` carries the codes and
   placeholder values; `ProblemDetailsExceptionHandler` writes `{ code, params }`.
3. SPA: field errors show the field's translated label and `errors:validation.<code>` with its params.
4. An architecture test: every validator rule declares a vocabulary code.
5. `templates/ca-use-case`: the scaffolded validator shows `WithErrorCode`.

**Exit criteria**: no validator emits a sentence; field errors are translated; the contract change is recorded in the
SPEC.

### 11.8 Phase 6 — Harden

**Decisions (pending)**

| # | Question | Recommendation | Why |
|---|---|---|---|
| P6.1 | Fail CI on unused keys | Yes, with `i18next-cli` | Keeps the catalogs honest |
| P6.2 | Pseudo-locale | Development only, `?lng=en-XA` | Useful to developers, meaningless to users |
| P6.3 | Translation platform | Not yet (D6) | Revisit at the third language |
| P6.4 | A third language | Only on a business need; the runbook is ready | Every added language is a permanent cost on every later change |

**Steps**: lint `error` everywhere; the unused-key gate; the pseudo-locale; `docs/features/localization/ADDING-A-LANGUAGE.md`
(add as `inProgress` → translate → gates green → promote); a localization section in the WhatsApp SPEC; the full
verification.

## 12. Execution facts

### 12.1 Commands

```bash
# SPA (from the repository root). Unset PORT first — see §12.4.
cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npx vite build

# .NET, as CI runs it
dotnet test --filter "TestCategory!=IndependentDevelopmentReview"

# Journeys: need Docker and no AppHost already running; the harness starts its own
dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false

# Run the app
dotnet run --project src/AppHost
```

### 12.2 Harness (recorded 2026-09-08 and 2026-09-10; verify if something behaves differently)

- Build outputs go to `artifacts/` at the root (`Directory.Build.props` `ArtifactsPath`); a fresh worktree has not
  been restored.
- `Application.FunctionalTests` and `Infrastructure.IntegrationTests` start `TestAppHost` (Aspire) with a disposable
  PostgreSQL container per run. `Web.AcceptanceTests` starts the real AppHost against the developer's persistent
  container (`dbserver-<hash>`) in a throwaway `acceptance_<guid>` database it drops at the end.
- Never delete `dbserver-*` containers or databases you did not create; check for other `dotnet test` or `dcp.exe`
  processes before starting a suite.
- The real AppHost runs `npm start`, whose `prestart` runs NSwag and regenerates `src/Web/ClientApp/src/web-api-client.ts`
  (gitignored). No SPA source imports that file (only `nswag.json:72` and `eslint.config.js:10` name it), so `vitest`
  and `eslint` run without the .NET build.
- The Web build regenerates `src/Web/wwwroot/openapi/v1.json` unless `-p:OpenApiGenerateDocumentsOnBuild=false`;
  tests read `/openapi/v1.json` over HTTP at runtime.
- The PostgreSQL container has no data volume, so recreating it empties the database; on a fresh database EF logs one
  expected `Error` for the `__EFMigrationsHistory` probe.
- The NUnit category `IndependentDevelopmentReview` marks deliberate RED reproductions; baselines exclude it.
- CI runs only `dotnet build` and `dotnet test` until Phase 1.

### 12.3 What the tests pin — keep it while extracting and translating

Verified 2026-09-10:

- Page objects find controls by English text: `GetByLabel("Current password")`, `GetByLabel("Email", Exact)`
  (`IdentityContinuationPages.cs:12,24,40`); role checkboxes by the role's name, `Exact`
  (`IdentityContinuationPages.cs:155,244`; `IdentityAccessPages.cs:204`); `GetByText(/MFA-authenticated Platform
  administrator/)` (`PlatformOperationsPage.cs:147`).
- Tenant buttons are read back with `" (current)"` stripped (`IdentityAccessPages.cs:138`).
- Member rows are `li` elements filtered by email (`IdentityContinuationPages.cs:278`).
- The bootstrap recovery page shows no text matching `@` (`PlatformOperationsPage.cs:24`).
- Contract tests assert English copy: navigation names "Clean Architecture", "Home", "Counter"; "Continue with
  Google"; "Reactivate your account"; "Too many attempts. Wait a moment and try again."
  (`materialUiMigration.contract.test.js:184-260`).
- `appTheme` is imported and asserted (`materialUiMigration.contract.test.js:18-86`); `main.jsx` must not contain
  `styles.scss` and `App.jsx` must not contain `ThemeContext` (lines 164–165).

Recorded 2026-09-09, verify before relying on them:

- `MembersPage` rows stay `List`/`ListItem`, with the role editor inside the same `li`; turning them into a table
  breaks two journeys.
- On `/identity`, the second `dd` in the document is the active organization's name.
- `PersonalProfilePage` has exactly one `dl`, with "DNI" and a number masked with `•`.
- Inside the region "Choose an organization" there are exactly two elements with `role=button`; nothing else with
  that role may go there.
- `Page.Locator("h1")` is used strictly: the shell (`Layout`, `NavMenu`) renders no `h1`.

### 12.4 Gotchas

- `AGENTS.md` blocks between `<!-- gentle-ai:... -->` markers are rewritten by a Gentle AI reinstall; put project
  rules outside them. Never run raw `gentle-ai sync` (`AGENTS.md:12`).
- The `.AspNetCore.Culture` value is `c=<culture>` and `uic=<culture>` joined by a vertical bar, not a bare code.
- The outbox worker has no request culture: pass the `CultureInfo`.
- Setting the request's `CurrentCulture` would change server-side parsing and formatting per visitor; set only the UI
  culture (§4.3).
- SPA tests render pages without providers: components get i18n by importing from `src/i18n` (§5.2).
- `npm run build` triggers `prebuild` → NSwag, which needs the compiled Web project; `npx vite build` does not.
- `vite.config.ts` exports the development certificate with `dotnet dev-certs` only when `PORT` is set; unset `PORT`
  before running SPA tests or builds.

## 13. Out of scope

- `src/Web/ClientApp-Angular`: the template's alternative client, not the product's.
- A time-zone preference: needed as soon as emails show dates; the next preference after language.
- Translating user-generated content.
- Right-to-left languages: none planned. MUI supports them if one is ever added.

## 14. Risks

| Risk | Mitigation |
|---|---|
| Rules and code disagree | Amendments land in Phase 0, before the code that needs them; each names the file and line it replaces |
| An amendment drops a protection instead of re-aiming it | Each row of §9.2 states what the rule protected and how the new wording keeps it |
| Half-translated Spanish reaches people | `es` stays `inProgress` — never negotiated or offered — until the gates report it complete |
| An extraction silently changes copy | `en` values are copied verbatim, and the unchanged tests fail on any drift |
| The machine's locale leaks into tests | Playwright `Locale` pinned; `useFormat` takes the language explicitly |
| The worker formats in the wrong culture | The recipient's culture is passed explicitly; completeness tests render every language |
| Keys rot | The parity test rejects extras; the unused-key gate in Phase 6 |
| Bundle growth | A documented threshold for lazy loading per language |
| The standard exists only on paper | Gates run in CI; the plan is not done until Phase 1's CI step exists |

## 15. Log

| Date | Event |
|---|---|
| 2026-09-10 | Plan drafted from a verified survey of the repository (§2) |
| 2026-09-10 | The user decided that existing rules are amended where the standard needs it (D7, §9) |
| 2026-09-10 | D1–D7 accepted as recommended |
| 2026-09-10 | P0.1–P0.4 accepted as recommended. Plan completed for handoff; staged languages (`inProgress`) added so the parity gate stays strict for supported languages while `es` is translated |
| 2026-09-10 | Phase 0 rule amendments, the localization skill and reference, ADR-007, the localization SPEC, and the PR template were implemented locally; commit and PR are pending explicit authorization. Phase 1 decisions follow delivery |
| 2026-09-10 | Review corrections tightened outbox payload privacy, clarified server cookie → `Accept-Language` → default resolution, refined the ADR alternatives, and named the lint and validation-code gates; Phase 0 remains implemented locally with delivery pending |
| 2026-09-10 | The user authorized the Phase 0 commit. The PLAN state was finalized and the Phase 0 work was committed locally; push and PR remain pending explicit authorization. Phase 1 decisions are next |
| 2026-09-10 | P1.1–P1.5 accepted as recommended: separate guarded `spa` CI job; fix a red SPA baseline first; missing-key behavior by environment; backend languages in code with only the default configured; lint `warn` globally and `error` per completed folder |
| 2026-09-10 | Delivery authorized: push `claude/translation-strategy-plan-ba3438`, open the Phase 0 PR against `main`, and create the Phase 1 branch from `afe6c92`. Do not open the Phase 1 PR until the user confirms Phase 0 is merged; its diff must exclude Phase 0 |
| 2026-09-10 | Phase 0 was pushed at exact commit `afe6c92`. Its PR is blocked because GitHub Issues is disabled and `branch-pr` requires a linked approved issue. Local Phase 1 branch `feat/localization-phase-1-foundations` was created from `afe6c92`; it remains unpushed and its PLAN decision log remains uncommitted |
| 2026-09-10 | The user replaced the Phase 0 PR with direct delivery to `main` and authorized enabling GitHub Issues if needed. Direct delivery makes that repository setting change unnecessary; verify a fast-forward to exact commit `afe6c92` before pushing `main` |
| 2026-09-10 | `origin/main` was fast-forwarded from `497e087` to `afe6c92` and then verified remotely at full SHA `afe6c92a325d950a0a0db90bd956f882618c01d6`. No GitHub setting was changed and no PR was opened |
| 2026-09-10 | Phase 1 baseline: SPA 308/308 tests passed, ESLint 0 errors/0 warnings, and Vite built successfully. .NET Domain 186/186, Application Unit 187/187, Infrastructure Integration 300/300, and Application Functional 626/626 passed. Acceptance was 7 passed/25 failed both in the solution run and isolated journey run; the dominant timeout waits for the `Sign in` heading to disappear although the authenticated shell is already visible. P1.2 does not apply because Vitest and ESLint are green; diagnose before implementation |
| 2026-09-10 | Acceptance diagnosis: 24 failures share a stale post-login assumption — successful authentication lands on `/`, whose Home intentionally has no `h1`, while Playwright's negated heading assertion still requires one. The remaining throttling journey loses the existing neutral refusal because `IdentityProvider` suppresses `authentication_required` after both bootstrap and an explicit sign-in attempt. Repair production only: default successful sign-in to the existing headed `/identity` route, preserve explicit safe return URLs, and suppress anonymous context refusal only during bootstrap. Do not edit tests |
| 2026-09-10 | Phase 1 foundations implemented locally: separate guarded SPA CI job; i18next facade and empty bundled catalogs (`en` supported, `es` in progress); catalog contract; global JSX-only literal warnings and facade-only React i18next imports; backend registry and request-localization order; registry/resource-parity tests; and Playwright `en-US`. SPA Vitest passed 320/320 and `npx vite build` passed. ESLint exited 0 with 1,385 expected extraction warnings and no errors. The catalog proof temporarily promoted `es`, added an `en`-only key, and failed exactly once with `es/common` reporting `missing key gate`; the deliberate mutation was reverted. `dotnet test --filter "TestCategory!=IndependentDevelopmentReview"` passed Domain 186 and Application Unit 187, but Infrastructure Integration 302 and Application Functional 626 could not start because Docker Desktop was unavailable. The acceptance command is likewise unavailable until Docker starts. CI itself remains unrun locally. |
| 2026-09-10 | Fresh verification completed after Docker became available: Vitest 320/320; ESLint exit 0 with 0 errors and 1,385 expected P1.5 warnings; Vite build green; Domain 186/186; Application Unit 187/187; Infrastructure Integration 302/302; Application Functional 626/626; Web Acceptance 32/32 — 1,653 tests total and zero failures. `git diff --check` is green. The intentional catalog gate had already proven that temporary `es` promotion with a missing `gate` key fails as `es/common: missing key gate`, then reverted. The user confirmed default login lands on `/identity`; its `LoginPage.jsx` + `IdentityProvider.jsx` repair must be committed separately before Phase 1. Phase 1 is locally verified but not delivered or fully finished: explicit authorization is still required for both commits, push, and PR against `main`; actual green `spa` PR-job evidence is still pending. |
| 2026-09-10 | The user changed the current delivery policy: work directly on `main` with conventional commits and a push after verification; do not commit or push failed or unrun verification, and use no PR or feature branch. The user authorized the two ordered commits and the direct fast-forward push to `origin/main`; the non-localization baseline journey repair was committed first as `f21103d`. The Phase 1 commit, direct push, and push-triggered green `spa` and `build` results remain pending. |
| 2026-09-11 | Phase 1 was committed as `ef19d44` after the complete local verification and fast-forward pushed to `origin/main` after `f21103d`; no force push or PR was used. GitHub recorded the push but dispatched no workflow run. |
| 2026-09-11 | The user established fixed autonomy boundaries (§0.2): do not ask before verified commits/pushes to `main`, planned dependency installs, suite execution, or in-scope fixes; ask only for phase product decisions, GitHub/account/machine configuration, destructive data or history changes, and costs, always with a recommendation. |
| 2026-09-11 | GitHub Actions was enabled and restricted to GitHub-owned actions (`allowed_actions: selected`, GitHub-owned allowed, verified marketplace actions disallowed, no custom patterns). `Build` remains active; a fresh push is required because the earlier push is not dispatched retroactively. |
| 2026-09-11 | The [first Build run](https://github.com/ezeellena2/RepositorioBase/actions/runs/34557253186) passed `spa`. Its build compiled successfully and passed Domain 186, Application Unit 187, Infrastructure Integration 302, and Application Functional 626; Web Acceptance failed 3/32 because auxiliary `HttpClient` calls rejected the ASP.NET development certificate on Ubuntu with `AuthenticationException UntrustedRoot`. Playwright already ignores HTTPS errors. The smallest environment correction adds `dotnet dev-certs https --trust` immediately after `actions/setup-dotnet@v6`; its rerun is pending. |
| 2026-09-11 | The [Build rerun](https://github.com/ezeellena2/RepositorioBase/actions/runs/34558083319) passed `spa`, but `build` failed before restore in `Trust development HTTPS certificate` with exit 4. The runner diagnosed missing OpenSSL `SSL_CERT_DIR` wiring and prescribed `$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs`. The revised step exports that exact value, runs `dotnet dev-certs https --trust`, then persists it through `$GITHUB_ENV`; its rerun is pending. |
| 2026-09-11 | The user chose one `SSL_CERT_DIR` attempt with no Docker image: for this workflow-only change, `git diff --check` is sufficient locally and the Linux Build run is the functional proof. The same commit makes Test Templates manual-only by retaining `workflow_dispatch` and removing its push trigger after [run 34557253244](https://github.com/ezeellena2/RepositorioBase/actions/runs/34557253244) failed for unrelated template drift. CodeQL is deferred. Commit and push the work unit once; if Build remains red, report it and continue separately. Phase 2 decisions proceed without awaiting CI. |
| 2026-09-11 | P2.1–P2.4 accepted as recommended: extract shell and `common` → errors → login and registration → remaining identity → platform; one folder and one direct-main commit/push per work unit; preserve every English value verbatim; keep permission codes raw until P3.4. Do not edit existing tests or add Spanish translations. Continue without waiting for Build, and report a certificate failure in one line if it occurs. |
| 2026-09-11 | Revised [Build run 34559082676](https://github.com/ezeellena2/RepositorioBase/actions/runs/34559082676) passed both `spa` and `build`, including HTTPS trust and Test solution. Phase 2 baseline then passed with exit 0: Vitest 320/320 in 29 files; ESLint 0 errors/1,385 warnings; Vite built 2,493 modules; global .NET 1,333/1,333 (Domain 186, Application Unit 187, Infrastructure 302, Functional 626, Acceptance 32); isolated Acceptance 32/32. Docker 28.5.1 was available and no AppHost/dcp competed. |
