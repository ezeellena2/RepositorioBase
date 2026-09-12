# Localization — cross-project standard and delivery plan

| | |
|---|---|
| Status | **Phases 0–4 are complete and delivered to `main`, including the Phase 3 copy-review correction at `a271c97`, Phase 4 at `c62b48e`, and the post-delivery copy correction at `64e758ab`. Acceptance harness and Build workflow stabilization is complete and delivered by the commit containing this entry. P5.1–P5.3 are accepted as recommended; Phase 5 implementation is pending and next.** |
| Last updated | 2026-09-11 |
| Scope | Backend (.NET), React SPA (`src/Web/ClientApp`), outbox-delivered messages, tests, CI, and the repository's working rules |
| Next action | Implement Phase 5 (§11.7) using the accepted P5.1–P5.3 decisions. |

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
  is deferred.
- Phase 2 is delivered at `45ed009`. Its final local closure passed 1,334 tests and 32 isolated journeys. The
  push-triggered `spa` job passed; `build` had one CI-only acceptance timeout that did not reproduce locally and is
  tracked outside localization.
- P3.1–P3.7 are accepted and Phase 3 was delivered at `c9ecbea`. Both catalogs still contain the same 462 leaves;
  `es` is supported; and that delivery's complete local verification passed 343 SPA tests, 1,335 .NET tests and 33
  isolated journeys.
- A separate post-delivery Phase 3 copy-review correction is delivered at `a271c97`. At that commit, the six broad-context keys
  `common:navigation.changeOrganization`, `common:navigation.noOrganizationSelected`,
  `identity:context.activeOrganization`, `identity:context.noneInThisOrganization`, `identity:tenants.title` and
  `identity:tenants.empty` changed to Context/contexto because their consumers admit Personal and Platform as well
  as Organization tenants. That human-facing wording decision is superseded by the post-Phase 4 copy correction
  delivered at `64e758ab`: those six values use Workspace/Espacio de trabajo, while stable
  keys and internal Tenant/context terminology remain unchanged. Three reviewed Spanish enum terms are corrected, and 24
  English multiword enum entries now use readable display labels. Catalog keys, enum codes and wire values remain
  invariant; the 20 already-readable one-word enum values plus permission and system-role labels are unchanged. Its
  complete verification passed 343 SPA tests, 1,335 .NET tests and all 33 isolated journeys.
- P4.1–P4.6 are accepted as recommended and Phase 4 is delivered to `main` at exact commit `c62b48e`. The exact
  `a271c97` starting tree was the Phase 4 baseline: its fresh
  correction closure passed Vitest 343/343 in 32 files, ESLint with 0 errors/4 existing warnings, Vite with 2,557
  modules, .NET 1,335/1,335 (Domain 186, Application Unit 187, Infrastructure 302, Functional 627, Acceptance 33),
  isolated journeys 33/33, and `git diff --check`. A7 is written and approved as IA-REQ-059 in the identity-access
  SPEC.
- The delivered Phase 4 application implements steps 2–7 plus its bounded post-review correction: nullable
  account preference and four immutable request-language snapshots; migration
  `20260911161740_IdentityLanguagePreferences`; the shared Application language registry and Web request-language
  port; an authenticated, same-origin, antiforgery-protected preference endpoint; 32 parity-checked email resource
  entries per language covering every §6.3 delivery variant; retry-stable outbox language metadata; and the signed-in
  persistence/read-only account-display SPA behavior. Lifecycle notices now use three stable message types with an
  identifier-only payload, with safe migration of legacy pending/attempted rows; only the narrow malformed-payload
  exception is terminalized as `payload_invalid`. The PostgreSQL delivery gate executes all 17 variants in `en` and
  `es`, checks all 16 DI registrations and proves account → snapshot → configured-default fallback by family.
  Authenticated language writes are serialized, reconcile to the last acknowledged server value after a later
  refusal, preserve newer language across stale tenant/reload responses, and use the central session-loss transition.
  The `c62b48e` closure is green: Vitest 362/362; ESLint with no errors; Vite build; Domain 190/190; Application Unit
  188/188; Infrastructure Integration 315/315; Application Functional 640/640; Web Acceptance and isolated journeys
  33/33; and `git diff --check`. Three independent re-reviews report no remaining candidate-causal finding.
- The post-delivery copy correction is delivered at `64e758ab`. The six shared selector and
  access-summary values now use Workspace/Espacio de trabajo without renaming stable keys or internal Tenant/context
  concepts. Email variants #4, #8, #11 and #16 use the reviewed English and formal-`usted` Spanish copy, preserve
  their placeholders, and have exact-body coverage for those four matrix rows. Discriminating RED failed 2/33 SPA
  tests, while the sole email-matrix test failed; focused GREEN passed SPA 33/33, the matrix 1/1, catalog parity 12/12, backend
  localization contracts 4/4 and the affected acceptance categories 8/8.
- Acceptance harness and Build workflow stabilization is complete and delivered by the commit containing this entry.
  Startup now waits for PostgreSQL, the API antiforgery endpoint and the SPA root under one bounded diagnostic ceiling;
  connection-string resolution participates in each probe attempt and diagnostic log enumeration has its own bound;
  login and organization switching wait for positive destination/context readiness; and CI runs the .NET projects
  sequentially with Acceptance isolated and safe failure-only TRX/DCP artifacts. The deterministic harness contract
  failed 0/1 before the change and passes 1/1; the previously failing permission journey passes 1/1; and the full
  Acceptance suite passes 33/33 on the delivered candidate. Phase 5 remains accepted, pending and next.

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
  address. Nullable with no back-fill: empty means "never chose" (P4.2). Only first account creation initializes it
  from the supported request UI language; a flow that finds an existing account never overwrites it.
- A `Language` snapshot on every organization and platform invitation and every organization-registration and
  personal-registration intent, whether or not the recipient already has an account. It is captured from the trusted
  request UI language when that record is created, is not a request DTO field and never changes (D5: the inviter's
  current language for invitations).
- On first delivery preparation, outbox handlers resolve the recipient's culture from account preference, else the
  snapshot, else the configured default. Retries keep that first prepared language even if the preference or default
  later changes.
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
- **Request language** reaches the model only through a narrow Application port implemented in Web over
  `IRequestCultureFeature` (UI culture). It initializes `PreferredLanguage` only when the request first creates the
  account and never when a flow finds an existing account; it also creates the immutable `Language` snapshot on every
  organization or platform invitation and organization-registration or personal-registration intent. None of those
  business request DTOs accepts a language field.
- **Validation messages travel as codes (D3).** Validators declare codes (`WithErrorCode("too_long")`) instead of
  sentences; field errors travel as `{ code, params }` (P5.2); the SPA renders the field's own translated label beside
  the translated rule. Rejected: FluentValidation's built-in translations chosen by the request culture — they keep the
  wire shape but leave a second catalog of UI text on the server.

### 6.3 Email construction sites (all in `src/Infrastructure/Outbox/`)

| File | `new IdentityEmail(` sites | Notes |
|---|---|---|
| `AccountLifecycleDeliveryHandlers.cs` | 2 | Account reactivation; three fixed lifecycle notice handlers with distinct stable message types and one identifier-only envelope |
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
| A7 | `docs/features/identity-access/SPEC.md`: IA-REQ-059 now; IA-REQ-038 in Phase 5 | The identity context carries no language; field errors carry sentences | IA-REQ-059 is written and approved for Phase 4: the identity context carries `preferredLanguage`, an own-account preference endpoint exists, and delivery uses the account preference and immutable snapshots. IA-REQ-038's field-error `{ code, params }` amendment remains Phase 5 work (D3, P5.2) | Rule 1 of this standard; the SPA needs the account's choice after sign-in without prematurely changing validation | 4 and 5 |

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
| D6 | Translation workflow | Files in the repository; Codex drafts Phase 3 and flags doubtful wording for the product owner's in-app review after delivery | The user explicitly chose the Phase 3 review handoff; revisit a translation platform (Crowdin, Lokalise, Weblate) at the third language or when non-developers translate |
| D7 | Existing rules that stand in the way | Amended (§9), including the `Locale` line in `PlaywrightSetup.cs` | A stale rule would make every later session work against the standard |
| D8 | Delivery policy | Work directly on `main`: conventional commit and push after verification, with no pull requests or feature branches | Verification is the delivery gate; failed or unrun verification is reported instead of committed or pushed |

### 10.2 Per phase

| Phase | State |
|---|---|
| 0 | Delivered directly to `main` at exact commit `afe6c92` after fast-forward and remote-ref verification — 2026-09-10 |
| 1 | Implemented, fully verified locally, committed at `ef19d44`, and fast-forward pushed to `origin/main` after the separate baseline journey repair at `f21103d`. The [first Build run](https://github.com/ezeellena2/RepositorioBase/actions/runs/34557253186) passed `spa`; its build compiled and passed Domain 186, Application Unit 187, Infrastructure Integration 302, and Application Functional 626, but Web Acceptance failed 3/32 because auxiliary `HttpClient` calls rejected the Ubuntu ASP.NET development certificate with `AuthenticationException UntrustedRoot`. The [rerun](https://github.com/ezeellena2/RepositorioBase/actions/runs/34558083319) passed `spa` but build failed in `Trust development HTTPS certificate` with exit 4 because OpenSSL lacked `SSL_CERT_DIR` wiring. Revised [Build run 34559082676](https://github.com/ezeellena2/RepositorioBase/actions/runs/34559082676) passed both `spa` and `build`, including HTTPS trust and Test solution. Test Templates run [34557253244](https://github.com/ezeellena2/RepositorioBase/actions/runs/34557253244) failed for unrelated template drift and is manual-only; CodeQL is deferred. Phase 2 decisions continue without awaiting CI. |
| 2 | **Complete and delivered by the commit containing this entry — 2026-09-11.** Decisions P2.1–P2.4 were applied. Baseline passed; work units were delivered one folder/commit/push at a time through platform invitations `4553b74`, with retention in this final commit. Final closure passed: Vitest 320/320 in 29 files; ESLint 0 errors/4 expected test warnings; Vite 2,493 modules; .NET 1,334/1,334 (Domain 186, Application Unit 187, Infrastructure 302, Functional 627, Acceptance 32); isolated journeys 32/32. All 39 production component/identity/platform files enforce lint severity 2; existing tests stay at severity 1 and were not edited; the new error-catalog contract passes; every Spanish catalog remains empty. |
| 3 | **Complete and delivered at `c9ecbea`, with the separate copy-review correction delivered at `a271c97` — 2026-09-11.** P3.1–P3.7 were applied. Both languages contain 462 catalog leaves (common 38, errors 56, enums 75, identity 175, platform 118); `es` is promoted to `supported`; permissions retain readable names plus visible codes; only system roles are translated; and timestamps use explicit-locale `Intl` formatting in the browser time zone. The original closure passed Vitest 343/343 in 32 files, ESLint with 0 errors/4 expected test warnings, Vite with 2,557 modules, .NET 1,335/1,335, and isolated journeys 33/33. The follow-up correction at `a271c97` changed six broad English source values from organization to context and the corresponding Spanish values to contexto; that specific human-facing terminology is superseded by the post-Phase 4 copy correction delivered at `64e758ab`, which uses Workspace/Espacio de trabajo while retaining stable keys and internal Tenant/context terminology. The same `a271c97` correction fixed three reviewed Spanish enum terms and made 24 English multiword enum display entries readable. Its fresh closure repeated Vitest 343/343, ESLint 0 errors/4 expected test warnings, Vite 2,557 modules, .NET 1,335/1,335, isolated journeys 33/33, and `git diff --check`; specification and quality reviews found no remaining issue. |
| 4 | **Complete and delivered to `main` at exact commit `c62b48e`; the post-delivery copy correction is delivered at `64e758ab`; Acceptance harness and Build workflow stabilization is delivered by the commit containing this entry — 2026-09-11.** P4.1–P4.6 and A7/IA-REQ-059 are applied. Phase 4 adds the nullable account preference, immutable invitation/intent snapshots, migration `20260911161740_IdentityLanguagePreferences`, shared registry/request-language port, protected own-account endpoint, explicit-culture `en`/`es` email resources, retry-stable outbox delivery language, and authenticated SPA persistence/display behavior. The bounded review correction gives lifecycle notices three stable identifier-only message contracts and migrates legacy rows without changing ids/fingerprints or back-filling language; narrows permanent payload classification; restores the invited-confirmation snapshot; removes the endpoint's fallible post-write projection; adds invariant validator messages; and closes tenant/reload/write/session-loss races plus refusal focus. The real delivery matrix renders 17 variants × two languages, covers all 16 DI message types and proves account → snapshot → configured-default/legacy-null resolution by family. The `c62b48e` closure passed Vitest 362/362 in 33 files; ESLint with 0 errors/10 test-only warnings; Vite with 2,557 modules; Domain 190/190; Application Unit 188/188; Infrastructure Integration 315/315; Application Functional 640/640; Web Acceptance and isolated journeys 33/33; and `git diff --check`. The later copy correction moves the six human-facing broad-tenant values to Workspace/Espacio de trabajo and revises email variants #4, #8, #11 and #16 in both languages with exact-body matrix coverage; its focused GREEN passed SPA 33/33, matrix 1/1, catalog parity 12/12, backend localization contracts 4/4 and affected acceptance categories 8/8. The separate harness stabilization replaces fixed Aspire readiness with bounded PostgreSQL/API/SPA conditions, carries each probe token into connection-string resolution, bounds diagnostic log enumeration, removes the post-login reload race, confirms authenticated destination and organization-switch completion, and runs CI .NET suites sequentially with Acceptance diagnostics; its current GREEN passes the structural contract 1/1, focused permission journey 1/1 and full Acceptance 33/33. Phase 5 remains approved, unimplemented and next. |
| 5 | **P5.1–P5.3 accepted as recommended — 2026-09-11; implementation and verification pending, and Phase 5 is next.** Use repository-owned snake_case validation codes, `{ code, params }` field-error objects, and a generic localized fallback for unknown codes. |
| 6 | Pending |

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

**Decisions (accepted 2026-09-11)**

| # | Question | Answer | Why |
|---|---|---|---|
| P3.1 | Register: *tú* or *usted* | *Usted* in sentences; buttons and labels use the infinitive (for example, `Iniciar sesión`, `Guardar`) | The form of address appears only where a complete sentence needs it; action copy stays concise and consistent |
| P3.2 | Terminology | Write `GLOSSARY.md` with Organización for Organization/Tenant, Plataforma, Iniciar sesión/Cerrar sesión, Sesión, Rol/Permiso, Miembro/Membresía, Propietario/Administrador, Invitación, Segundo factor, and invariant DNI/CUIT; decide missing terms by the same criterion and report them | The same neutral, professional term is used everywhere; verify whether Platform's tenant model includes personal contexts before treating both terms as Organización |
| P3.3 | Who translates and who reviews | Codex translates; the final handoff lists doubtful wording and explains how to review the Spanish UI | The user explicitly chose an in-app product-owner review after verified delivery instead of a blocking pre-push native-speaker checkpoint |
| P3.4 | Human names for permissions | A readable localized name with the invariant code as secondary text, kept visible for operators and tests | People read names; operators and tests can still identify the exact permission code |
| P3.5 | Language selector | In the shell: a native `select`, each language named in itself (`English`, `Español`), no flags | Flags name countries, not languages; a native `select` is the repository's pattern |
| P3.6 | Date and time format | `Intl` medium date and short time, in the browser's time zone | No time-zone preference exists yet (§13) |
| P3.7 | Built-in role names | `isSystem` roles show `enums:roles.system.<name>`; custom roles show the stored name | System names are product vocabulary; organization-defined names are user-authored data |

**Steps**

1. Present P3.1–P3.7; write and approve the glossary.
2. Translate every `es` catalog.
3. Selector in the shell (P3.5): writes the cookie, switches language live, respects §12.3.
4. `html[lang]`; `themeFor(language)`; `useFormat()` replacing `toLocaleString()` (`InviteMemberPage.jsx:210,316`).
5. Permission labels (P3.4) and built-in role names (P3.7).
6. The `es` smoke journey — a new feature file, steps and page object (declared under A2): choose Español, sign in,
   open `/identity`; assert `html[lang="es"]` and that no raw key is visible.
7. Promote `es` to `supported` in `languages.json` and in the backend registry; the parity gate is now strict for it.

**Exit criteria**: `es` complete and promoted; parity strict and green; the `es` journey green; the final handoff lists
the wording awaiting product-owner review and explains how to inspect the Spanish UI.

### 11.6 Phase 4 — Spanish outside the SPA

**Decisions (accepted 2026-09-11)**

| # | Question | Answer | Why |
|---|---|---|---|
| P4.1 | Where `PreferredLanguage` lives | On the identity account | The outbox already reads that row for the recipient's address |
| P4.2 | Existing accounts | Nullable, no back-fill: empty means "never chose" and delivery falls through to a usable snapshot, then the default | Keeps chosen and inferred apart, preserves the captured request language where one exists, and follows the default if it ever changes |
| P4.3 | Where a signed-in person changes it | The shell selector saves it to the account; the account screen shows it | One control, and the choice follows the person to other devices |
| P4.4 | Email format | Keep plain text; localize it as it is | HTML is a separate change (§6.2) |
| P4.5 | A language control on the invite form | Not now (D5) | The inviter's language covers it; add it on request |
| P4.6 | The identity-access SPEC amendment (A7) | Approve it as its own requirement block | Behavior is specified before code (SPEC §12) |

**Steps**

1. **Completed 2026-09-11:** P4.1–P4.6 were accepted and A7 was written and approved as IA-REQ-059. The
   IA-REQ-038 field-error `{ code, params }` amendment remains Phase 5 work.
2. **Implemented locally:** migration `20260911161740_IdentityLanguagePreferences` adds nullable
   `PreferredLanguage`, four nullable immutable `Language` snapshots, and nullable outbox `DeliveryLanguage`, with
   supported-canonical-language checks and no fabricated back-fill. It also maps legacy lifecycle rows from the old
   type-plus-`Outcome` envelope to three stable message types with identifier-only payloads, preserving message ids,
   delivery state and fingerprints; downgrade restores the predecessor contract.
3. **Implemented locally:** the Application request-language port and Web adapter capture the negotiated request UI
   language on new accounts, invitations and intents; interactive request culture remains cookie →
   `Accept-Language` → configured default and never reads the account per request.
4. **Implemented locally:** `Emails.resx` and `Emails.es.resx` contain the same 32 subject/body entries and cover all
   17 §6.3 delivery variants. Rendering receives an explicit culture after resolving bound retry language, account
   preference, snapshot and configured default in that order; payloads remain identifiers only. Invited confirmation
   consumes its optional invitation id and snapshot, and only the typed malformed-payload exception is permanently
   classified as `payload_invalid`; missing resources, EF failures and programmer failures remain retryable worker
   failures. The executable PostgreSQL matrix invokes the real registered handlers for 17 variants × `en`,`es`, checks
   all 16 DI message types, placeholders, recipient and bound language, and exercises account → snapshot → default plus
   legacy-null fallback for every snapshot family.
5. **Implemented locally:** `preferredLanguage` is returned by identity context and the authenticated
   `PUT /api/identity/context/language` endpoint enforces exact origin, antiforgery and supported canonical input,
   updates only the caller's preference, and returns the updated context without rotating security/session state. Its
   response projection is resolved before mutation, so a projection failure cannot report failure after a durable
   preference write; validation uses explicit invariant-English messages without echoing input.
6. **Implemented locally:** the shell keeps the native `English`/`Español` selector as the only editor, persists an
   authenticated choice before switching the local catalog, keeps anonymous choice local, presents failures, rejects
   stale language/session responses, serializes server writes in choice order, continues after a rejected write,
   cancels queued-but-unsent writes on session transition, preserves a newer language across a delayed successful
   tenant selection or failed reload, reconciles to the last acknowledged server language if the latest queued write
   fails, routes authentication loss through the central transition, focuses the visible refusal alert, and shows the
   preference read-only on the account screen.
7. **Complete verification green:** Domain 190/190; Application Unit 188/188; Infrastructure Integration 315/315;
   Application Functional 640/640; Web Acceptance and isolated journeys 33/33; full Vitest 362/362 in 33 files;
   ESLint 0 errors/10 expected test-only warnings; Vite 2,557 modules; and final `git diff --check`. The direct
   project-local Vitest command was used because the machine's global `npm` shim could not find `npm-cli.js`; it ran
   the complete suite successfully. Three independent final reviews found no remaining candidate-causal issue.

**Exit criteria**: every email renders in every supported language; the new and existing tests green.

**Current state:** Phase 4 and every exit criterion were delivered to `main` at exact commit `c62b48e`.

### 11.7 Phase 5 — Validation codes (implementation next)

**Decisions (P5.1–P5.3 accepted as recommended on 2026-09-11; implementation pending)**

| # | Question | Recommendation | Why |
|---|---|---|---|
| P5.1 | Code vocabulary | Our own snake_case codes (`required`, `too_long`, `invalid_format`), not FluentValidation's validator names | The same style as `ApplicationError.Code`, and independent of the library |
| P5.2 | Wire shape of a field error | `{ code, params }`, e.g. `{ "code": "too_long", "params": { "max": 100 } }` | A bare code cannot state the limit |
| P5.3 | A code the SPA does not know | A generic "This value is not valid" beside the field | A raw code never reaches a person |

**Steps**

1. **Decision gate complete 2026-09-11:** P5.1–P5.3 were accepted as recommended. Amend IA-REQ-038 (A7)
   before implementation.
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
- Inside the region "Choose a workspace" there are exactly two elements with `role=button`; nothing else with
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
| 2026-09-11 | The focused `ErrorCatalogContractTests` RED run compiled and started the functional harness successfully, then failed 1/1 at `ErrorCatalogContractTests.cs:47`: `en/errors.json` lacked advertised `antiforgery_validation_failed` (test 416 ms; command 34.217 s). After extraction, GREEN passed: the new contract 1/1, Vitest 320/320 in 29 files, ESLint 0 errors/1,273 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. The served OpenAPI advertises 50 distinct codes and every supported `en` entry is present; 43 existing messages remain byte-for-byte unchanged and unknown/unmapped codes retain the exact generic fallback. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified errors work unit was delivered at `1d16fd6`. `features/identity/login` is now the current one-folder Phase 2 work unit; its extraction awaits separate verification before delivery. |
| 2026-09-11 | Login extraction preserved all seven English values and its complete structural/accessibility contract. Its first full Vitest run had one unrelated failure in `PlatformInvitationPages.test.jsx`: the test synchronously queried a button that appears only after asynchronous identity-context bootstrap. The exact focused test passed 1/1, and one unchanged full-suite confirmation passed 320/320 in 29 files. ESLint passed with 0 errors/1,251 warnings, Vite built 2,493 modules, and `git diff --check` passed. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified login work unit was delivered at `46f2519`. `features/identity/register` is now the current one-folder Phase 2 work unit; its extraction awaits separate verification before delivery. |
| 2026-09-11 | Registration extraction preserves all 17 English values, shared `Register`, neutral-flow semantics, form keys and complete structural/accessibility contracts. The first lint gate correctly exposed four invariant field names inside JSX handlers; they moved to a module-level field map without weakening the rule. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/1,190 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified registration work unit was delivered at `16ea18f`. `features/identity/context` is now the current one-folder Phase 2 work unit; its extraction awaits separate verification before delivery. |
| 2026-09-11 | Identity context extraction preserves six English values plus shared `Your access`, the definition-list/accessibility structure, and raw permission codes under P2.4. Its first lint run exposed four unchanged literals in a colocated test; the production-only severity-2 override now leaves test/spec files on the global warning policy without ignoring them. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/1,154 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified identity context work unit was delivered at `22765aa`. `features/identity/credentials` is now the current one-folder Phase 2 work unit; its extraction awaits separate verification before delivery. |
| 2026-09-11 | Credentials extraction preserves 24 exact English values, rich-link punctuation, provider interpolation, token guards, neutral recovery and complete accessibility/behavior contracts. Its first lint run exposed the invariant `/identity` route inside a JSX callback; a module-level constant removed that false positive without weakening the rule. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/1,047 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified credentials work unit was delivered at `aaa55cd`. `features/identity/invitations` is now the current one-folder Phase 2 work unit; its extraction awaits separate verification before delivery. |
| 2026-09-11 | Identity invitations extraction preserves 31 exact English values, native-confirmation and email/date interpolation, neutral flows and complete accessibility/behavior contracts; both existing `toLocaleString()` calls remain unchanged for Phase 3. The first lint run exposed structural `scope` and `align` attributes; the shared narrow exclusion now classifies only those attribute names without suppressing visible text. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/921 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified invitations work unit was delivered at `c8daeee`. `features/identity/lifecycle` is now the current one-folder Phase 2 work unit; its extraction awaits separate verification before delivery. |
| 2026-09-11 | Identity lifecycle extraction preserves 18 exact English values plus four reused keys, rich-link/provider rendering, neutral states and complete accessibility/behavior contracts. Routes and lifecycle operation codes remain invariant constants. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/837 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified lifecycle work unit was delivered at `5a0cc88`. `features/identity/members` is now the current one-folder Phase 2 work unit; its extraction awaits separate verification before delivery. |
| 2026-09-11 | Identity members extraction preserves 19 exact English values plus reused `Members`/`Password`, all name/provider interpolation, native confirmation, and each member's `li`/role-editor/accessibility contracts. Role, permission and server-data values remain raw. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/754 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified members work unit was delivered at `fc6c0f4b`. `features/identity/people` is implemented as the current one-folder Phase 2 work unit and awaits separate verification before delivery; `features/identity/roles` follows. |
| 2026-09-11 | Identity people extraction preserves 18 new English values plus five reused keys, all personal-registration and profile copy byte-for-byte, neutral registration behavior, the profile definition list and complete accessibility/error-ownership contracts. Raw document, status and profile data remain untranslated. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/636 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified people work unit was delivered at `724a899`. `features/identity/roles` is implemented as the current one-folder Phase 2 work unit and awaits separate verification before delivery; `features/identity/sessions` follows. |
| 2026-09-11 | Identity roles extraction preserves 23 new English values plus two reused keys, all name/provider interpolation and complete table, identity-proof, accessibility and refusal contracts. Custom and built-in role names and permission codes remain raw under P2.4/P3.7. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/548 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified roles work unit was delivered at `c9646ed`. `features/identity/sessions` is implemented as the current one-folder Phase 2 work unit and awaits separate verification before delivery; `features/identity/tenants` follows. |
| 2026-09-11 | Identity sessions extraction preserves nine new English values plus two reused keys, rich-link/provider rendering, timestamp interpolation and complete list/current-device/accessibility/refusal contracts; raw session data and existing formatting remain unchanged for Phase 3. The first lint gate exposed five invariant action, operation and structural values inside JSX; they moved to exact module constants without widening exclusions. Corrected final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/501 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified sessions work unit was delivered at `8f13ea3`. `features/identity/tenants` is implemented as the current one-folder Phase 2 work unit and awaits separate verification before delivery; `PlatformPanel` follows. |
| 2026-09-11 | Identity tenants extraction preserves three exact English values, including one interpolation that resolves exactly to `{{name}} (current)`, all raw tenant names/data, and the chooser's exact two-button/accessibility/refusal contracts. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/488 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified tenants work unit was delivered at `10e0826`. `PlatformPanel` is implemented as the current one-file Phase 2 work unit and awaits separate verification before delivery; `features/platform/shared` follows. |
| 2026-09-11 | PlatformPanel extraction preserves 32 new English values plus `common:navigation.platform`, all six interpolations, MFA/confirmation/form behavior, and complete accessibility/refusal/error-ownership contracts. Statuses, reasons, permission/API codes, emails, slugs and outcomes remain raw. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/361 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified PlatformPanel work unit was delivered at `42af0a3`. `features/platform/shared` is implemented as the current one-folder Phase 2 work unit and awaits separate verification before delivery; `features/platform/identities` follows. |
| 2026-09-11 | Platform shared extraction adds two exact English keys for the step-up form while preserving all MFA/recovery field ownership, controlled state, numeric input, ARIA and consumer behavior. Developer-only errors in `usePlatformRead` and `usePlatformStepUp` remain invariant and untouched. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/355 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified platform shared work unit was delivered at `ff859b3`. `features/platform/identities` is implemented as the current one-folder Phase 2 work unit and awaits separate verification before delivery; `features/platform/invitations` follows. |
| 2026-09-11 | Platform identities extraction preserves 22 new English values plus two reused keys, all interpolations, MFA/no-replay/acknowledgement behavior and complete table/accessibility/error-ownership contracts. Emails, statuses, reasons, permissions, IDs and codes remain raw. The first lint gate exposed structural `submitVariant="outlined"`; the exact value moved to a module constant without widening exclusions. Corrected final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/253 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. |
| 2026-09-11 | The verified platform identities work unit was delivered at `cb24e89`. `features/platform/invitations` is implemented as the current one-folder Phase 2 work unit and awaits separate verification before delivery; `features/platform/retention` follows. |
| 2026-09-11 | Platform invitations extraction preserves 28 exact English values across registration, confirmation, MFA enrollment, bootstrap recovery and MFA replacement, together with token-in-memory/no-replay sequencing, routes, form structure and accessibility. Raw emails, shared/recovery codes, tokens, statuses, reasons and outcomes remain untranslated. Final gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/132 warnings, Vite 2,493 modules, and `git diff --check`, all exit 0. No existing test or Spanish catalog changed. The extraction preserves the pre-existing page-level `ProblemMessage` presentation in these two files; field-owned rendering is outside this no-op phase. |
| 2026-09-11 | The verified platform invitations work unit was delivered at `4553b74`. `features/platform/retention` is implemented as the final one-folder Phase 2 work unit and awaits its final folder gate and complete Phase 2 closure. Its manual audit included the JavaScript-built local validation refusal and placed-hold receipt; raw hold fields and date text remain unchanged. |
| 2026-09-11 | Platform retention extraction preserves 34 exact English values plus reused cancellation copy, including JavaScript-built validation and all four receipt placeholders. Raw reasons, operations, hold IDs, permissions, statuses, categories and date text remain invariant; MFA/no-replay, refusal priority, forms, table and accessibility contracts are unchanged. Its final SPA gate passed: Vitest 320/320 in 29 files, ESLint 0 errors/4 expected warnings from an existing test, Vite 2,493 modules, and `git diff --check`, all exit 0. |
| 2026-09-11 | The Phase 2 lint closure found and corrected two configuration gaps without touching source behavior: tests/specs under components, login and register now remain on the global warning policy, while identity/platform API files and identity root helpers receive production severity 2. Final audit covered 39 production files at severity 2, 24 existing tests at severity 1, 140 production probes rejected as errors and 24 test/spec probes retained as warnings. Every Spanish catalog remains `{}`. |
| 2026-09-11 | Phase 2 complete closure passed with no tracked-file drift: .NET 1,334/1,334 — Domain 186, Application Unit 187, Infrastructure Integration 302, Application Functional 627 (including `ErrorCatalogContractTests`), Web Acceptance 32 — and the isolated Reqnroll/Playwright journeys 32/32. Docker Desktop 28.5.1 was healthy and no competing AppHost/dcp/testhost was present. The verified retention work unit and this completion record are delivered by the commit containing this entry. |
| 2026-09-11 | P3.1–P3.7 accepted: formal *usted* only in sentences with infinitive labels/actions; the specified neutral-Spanish glossary plus consistent decisions for missing terms; Codex translation with doubtful wording and in-app review instructions in the final handoff; readable localized permission names with visible invariant codes; a native `English`/`Español` shell selector without flags; `Intl` medium date and short time in the browser time zone; translated system roles and unchanged organization-defined role names. This explicitly replaces the blocking pre-push native-speaker checkpoint for Phase 3 with the user's requested post-delivery product-owner review handoff. |
| 2026-09-11 | Phase 3 baseline passed before application changes: Vitest 320/320 in 29 files; ESLint 0 errors/4 expected test warnings; Vite 2,493 modules; .NET 1,334/1,334 (Domain 186, Application Unit 187, Infrastructure 302, Functional 627, Acceptance 32); isolated journeys 32/32; `git diff --check` green. The first sandboxed SPA invocation could not read the user-level npm CLI, and the first sandboxed .NET invocations could not read the user NuGet configuration; unchanged elevated reruns passed without installing or changing the machine. |
| 2026-09-11 | Phase 3 implemented with discriminating RED evidence: the first Spanish runtime tests failed three times before the selector/live language behavior existed; review corrections failed four presentation cases before broad Contexto terminology and readable denied-state permissions were added; accessibility/date tests failed twice before localized checkbox descriptions and unavailable-date handling; and the strengthened 375 px journey exposed a real clipped toolbar before responsive MUI composition fixed it. The Spanish smoke then passed real sign-in, HTTP 204, `/identity`, full-page cookie persistence, `html[lang="es"]`, localized context, raw-key absence and complete mobile control bounds. |
| 2026-09-11 | Phase 3 final closure passed on the reviewed candidate: strict catalogs contain 462 matching leaves per language (common 38, errors 56, enums 75, identity 175, platform 118), with placeholder and rich-tag parity and all 383 prior English leaves unchanged. Vitest passed 343/343 in 32 files; ESLint passed with 0 errors and the same four test warnings; Vite built 2,557 modules; .NET passed 1,335/1,335 (Domain 186, Application Unit 187, Infrastructure 302, Functional 627, Acceptance 33); isolated journeys passed 33/33; `git diff --check` passed. Independent specification and code-quality reviews found no remaining issue. `es` is promoted to `supported`; Phase 4 decisions are next. |
| 2026-09-11 | A separate Phase 3 copy-review correction is complete and delivered at exact commit `a271c97`; Phase 4 remains unstarted. At that point six broad-context keys presented Context/contexto because the shared context summary, shell switcher and chooser admit Personal and Platform as well as Organization tenants; that human-facing terminology decision is superseded by the post-Phase 4 Workspace/Espacio de trabajo correction recorded below, while internal Tenant/context terminology remains. Spanish review changed `OutboxSecrets` to “Secretos de mensajes salientes”, `PlatformMfaMaterial` to “Datos del segundo factor de la Plataforma”, and `SelfDeactivated` to “Desactivada por su titular”; the glossary adds account holder → Titular. Exactly 24 English multiword enum entries now have readable display labels, while every key/code, raw fixture/API/domain/database value, the 20 one-word enum values, and permission/system-role labels remain invariant. Copy assertions changed only in `AppRoutes.test.jsx` (chooser heading), `i18n/presentation.test.jsx` (six source/context values, three reviewed Spanish terms and the 24-label English map), `PlatformRetentionPage.test.jsx` (visible Audit events/Session records), `IdentityAccessPages.cs` (chooser region accessible name), and `PlatformOperationsStepDefinitions.cs` (visible administrative-status label). The elevated focused SPA RED failed exactly 9/63 copy expectations with 54 passing; after the catalogs changed, the unchanged command passed 63/63. The catalog contract passed 12/12, the Organization-plus-Personal chooser journey passed 1/1, and the focused PlatformOperations acceptance feature passed 7/7. Fresh complete closure passed Vitest 343/343 in 32 files, ESLint with 0 errors/4 expected test warnings, Vite with 2,557 modules, .NET 1,335/1,335, isolated journeys 33/33, and `git diff --check`; independent specification and quality reviews found no remaining issue. |
| 2026-09-11 | P4.1–P4.6 accepted as recommended: nullable `PreferredLanguage` on the identity account with no back-fill; the signed-in shell selector persists it and the account screen shows it; emails stay plain text; invitations keep the inviter's current-language snapshot without a new invite-form control; and A7 is approved as a dedicated identity-access SPEC requirement block before implementation. |
| 2026-09-11 | Phase 4 baseline on the exact `a271c97` starting tree is green from the fresh correction closure: Vitest 343/343 in 32 files; ESLint 0 errors/4 existing warnings; Vite 2,557 modules; .NET 1,335/1,335 (Domain 186, Application Unit 187, Infrastructure 302, Functional 627, Acceptance 33); isolated journeys 33/33; `git diff --check` green. |
| 2026-09-11 | A7 is written and approved as the dedicated IA-REQ-059 identity-access requirement. It specifies Phase 4 account preference, request-language snapshots, explicit delivery-culture resolution, stable retry language, identifier-only outbox payloads, invariant technical surfaces and enumeration-neutral behavior. IA-REQ-038's `{ code, params }` validation amendment remains Phase 5; Phase 4 application implementation is next and has not started. |
| 2026-09-11 | Phase 4 application implementation is complete in the working tree. `LocalizationRegistry` and its validated default setting now live in Application so Web and Infrastructure share one architectural source; Web still negotiates each interactive request strictly from culture cookie → `Accept-Language` → default. Migration `20260911161740_IdentityLanguagePreferences` adds six nullable columns with canonical-language checks and no back-fill: account `PreferredLanguage`, four invitation/intent `Language` snapshots, and outbox `DeliveryLanguage`. Only first account creation initializes a preference; every invitation/intent captures an immutable request-language snapshot, including reissue preservation and safe legacy-null fallback. The own-account `PUT /api/identity/context/language` route is authenticated, exact-origin and antiforgery protected, returns the updated identity context, and changes no session, security stamp or token. |
| 2026-09-11 | Every registered §6.3 identity-email path now renders plain-text English or formal-`usted` Spanish from 32 parity-checked resources per language, using an explicit culture. First preparation resolves account preference → invitation/intent snapshot → configured default and binds the selected language beside the fingerprint before sending; retries keep that language and the outbox message id, while legacy already-attempted rows bind historical English so their existing fingerprint remains valid. Outbox payloads gained only required identifiers and still contain no rendered prose, template arguments or personal data. The SPA persists a signed-in choice before changing its catalog/cookie, keeps anonymous choices local, visibly presents failures, merges the updated preference into context, rejects stale reload/tenant/session responses, serializes authenticated writes in choice order and cancels queued writes after a session transition, and exposes the account preference read-only outside the shell selector. |
| 2026-09-11 | Phase 4 focused verification is green: Domain 186/186; Application Unit 188/188; Infrastructure localization/model/migration/outbox slice 50/50 against real PostgreSQL; preference HTTP/functional cases 10/10, including exact-origin refusal; Phase 4 SPA slice 33/33 before the final concurrency review; post-review provider/shell slice 20/20; current full Vitest 353/353 in 32 files; ESLint 0 errors/10 expected test-only warnings; Vite 2,557 modules; Web build 0 warnings/errors. RED evidence was limited and intentional: the new Application command first failed the authorization inventory 1/187 and the new domain property first failed the shape contract 1/186; both existing assertions were updated to the declared contract. The first SPA focus passed 23/25 before the MUI test transition and ambiguous selector assertion were corrected; the first SPA audit found stale full-context and sign-out races, whose three regressions pass; a second audit found that concurrent PUT completion order could diverge from local intent, so authenticated writes now run serially, recover after a refusal and skip queued work from a superseded session. The two outbox tests that simulate legacy/follow-up state now update only their own invitation/account rows and pass 2/2. Existing tests changed only where Phase 4 deliberately changes contracts or wiring: identity authorization inventory; domain shape/snapshot preservation; password-lifecycle account-service wrapper; outbox constructor wiring and delivery/admission behavior; SPA context, selector, account display and Spanish behavior. New tests cover resources, persistence, endpoint security, account/snapshot initialization, precedence and retry stability, including a legacy attempted message. Final complete .NET/acceptance/journey closure remains pending. |
| 2026-09-11 | Three pre-existing findings stay outside Phase 4: `identity.ownership.transferred.notice.requested` has no registered handler and its producer promises two recipients although one message prepares one email, so it requires a separate delivery design; the context switcher's non-`Personal` type-label fallback still labels every other tenant type as Organization; and `GET /api/identity/context` omits `permission_denied`/403 from its OpenAPI metadata even though its Application authorization can emit that refusal. None of those paths was changed. |
| 2026-09-11 | The bounded Phase 4 review correction is implemented. Lifecycle delivery no longer persists the `Outcome` template selector: self-deactivation, administrative suspension and reactivation use distinct stable message types with `{IdentityId}` only, and the IA-REQ-054 outbox inventory is aligned to those contracts; the Phase 4 migration upgrades and reverses representative legacy pending/attempted rows without changing ids, fingerprints or fabricating language. Payload errors now cross a narrow typed boundary; a structurally invalid JSON envelope or missing identifier becomes permanent `payload_invalid`, while resource/EF/programmer failures escape to retry-safe worker handling. Invited confirmation reads its optional invitation snapshot. The PostgreSQL gate resolves the production DI registry and renders all 17 §6.3 variants in both supported languages, additionally proving account → snapshot → configured default and legacy-null behavior for the four snapshot families plus account-only delivery. |
| 2026-09-11 | The same correction removes the preference endpoint's fallible post-write context read and pins an unchanged preference when the pre-write projection fails; validator prose is explicit invariant English. SPA races are covered deterministically: a tenant response begun before a language choice may update tenant/permissions without reverting language, a stale reload failure cannot clear the newer context, an acknowledged earlier write becomes the reconciliation point if the latest queued write fails, and protected `authentication_required`/`invalid_session` responses use the central session-loss transition and cancel queued work. The visible language-refusal alert receives focus. |
| 2026-09-11 | Correction-focused closure is green: Domain 190/190; Application Unit 188/188; Application Functional lifecycle/preference 42/42; PostgreSQL outbox, 17×2 delivery matrix and migration round trip 39/39 plus resource/admission contracts 12/12; full Vitest 361/361 in 33 files; ESLint 0 errors/10 test-only warnings; Vite 2,557 modules; Web build 0 warnings/errors; final `git diff --check` green. The ordinary `npm test` launcher could not start because the machine's global npm shim lacks `npm-cli.js`; the project-local Vitest binary executed the complete suite successfully. Full Application Functional, Infrastructure, Web Acceptance and isolated journeys still remain for final Phase 4 delivery closure. |
| 2026-09-11 | The final bounded Phase 4 correction invalidates the cached session-bound antiforgery token on every recognized `401 authentication_required`/`invalid_session`, while keeping only the context endpoint's duplicate-notification suppression; the next mutation bootstraps a fresh pair and no failed mutation is replayed. Language writes now retain one queue tail across session transitions, so an old in-flight write settles before a new-session choice while queued stale work still self-cancels by session revision. The two regressions produced a discriminating Vitest RED (4 failed, 20 passed) and then GREEN 24/24. IA-REQ-059 proof now covers two simultaneous live HTTP sessions plus a later sign-in, valid-CSRF unauthenticated refusal with zero mutation, every missing personal/invited/Platform/external/bootstrap capture and existing-account non-overwrite path, Platform reissue/recovery snapshot preservation, and unchanged session/credential/security state; the focused Application Functional selection passed 117/117. The real PostgreSQL 17×2 delivery matrix now runs under conflicting `fr-FR` current culture and `ja-JP` UI culture, proves both remain unchanged, and passed 1/1. Two envelope comments now state that no token or invitation details are exposed and, alongside the recipient `IdentityId`, only the invitation's `InvitationId` is retained for snapshot-language resolution; `git diff --check` passed. |
| 2026-09-11 | The fresh complete .NET gate exposed one final host-composition omission and one stale A7 assertion: Infrastructure passed 313/315 because the generic outbox-worker host had no `IRequestLanguage` for five Application handlers, while Application Functional passed 639/640 because the context response-shape test still expected six properties instead of the approved seventh `preferredLanguage`; Domain remained 190/190 and Application Unit 188/188. A focused RED reproduced both worker failures. Infrastructure now supplies a scoped non-HTTP request-language adapter that returns the validated `Localization:DefaultLanguage`; Web's later scoped registration remains the interactive request-culture override. The actual worker host pins and resolves configured Spanish, and the response-shape assertion now includes `preferredLanguage`. Focused GREEN passed the two worker tests 2/2, the context-shape plus real HTTP `Accept-Language` override tests 2/2, and the localization architecture contracts 4/4; `git diff --check` passed. |
| 2026-09-11 | Phase 4 final closure is green on the corrected and independently re-reviewed candidate. The complete SPA gate passed Vitest 362/362 in 33 files, ESLint with 0 errors and 10 expected test-only literal warnings, and Vite with 2,557 modules; the complete .NET gate passed 1,333/1,333 non-acceptance tests — Domain 190, Application Unit 188, Infrastructure Integration 315 and Application Functional 640 — and Web Acceptance/isolated Reqnroll-Playwright journeys passed 33/33. `git diff --check` passed. Infrastructure and Functional emitted only the existing non-blocking `ASPIRE010`; Vite emitted only the existing chunk-size warning. The ordinary npm shim remains broken on this machine, so the already-installed project-local Vitest, ESLint and Vite binaries supplied the valid SPA evidence. Contract, SPA/copy and outbox/migration re-reviews all passed with no candidate-causal finding. |
| 2026-09-11 | Phase 4 was committed and delivered to `main` at exact commit `c62b48e`. The post-delivery copy correction is complete and delivered at `64e758ab`: the six shared selector/access-summary values use Workspace/Espacio de trabajo, superseding the earlier human-facing Context/contexto decision without renaming stable keys or internal Tenant/context concepts; email #4 aligns its opening with the Platform existing-account notice, #8 tells the recipient to set up the personal account after signing in, #11 says all sessions were closed, and #16 names the Platform email address explicitly in both languages. The four matrix rows now assert their complete rendered bodies and preserve `{0}`/`{1}`. Discriminating RED failed SPA 2/33 with 31 passing, while the sole email-matrix test failed; focused GREEN passed SPA 33/33, email matrix 1/1, catalog parity 12/12, backend localization contracts 4/4 and affected `AccountLifecycle`/`PlatformOperations` acceptance categories 8/8. The initial npm and .NET attempts did not start tests because the known global npm shim is broken and sandbox access to user NuGet configuration was denied; unchanged direct/elevated reruns supplied the reported evidence. |
| 2026-09-11 | P5.1–P5.3 were accepted as recommended: repository-owned snake_case validation codes, `{ code, params }` field-error objects, and a generic localized fallback when the SPA does not know a code. Phase 5 implementation and verification remain pending; Phase 5 is next. |
| 2026-09-11 | Acceptance harness and Build workflow stabilization is complete and delivered by the commit containing this entry. Build #22 and #24 are the external RED evidence: #24 attempt 1 completed the login POST with 204, then the redundant `/identity` reload raced the authenticated destination and timed out waiting for `Your access`; attempt 2 failed all 33 one-time setups on Aspire/DCP's fixed 20-second resource-readiness path while the solution-level test command ran Aspire-hosting suites concurrently. The original local deterministic RED failed the new architecture contract 0/1 because condition-based readiness was absent. Startup now polls PostgreSQL `SELECT 1`, `/api/identity/antiforgery` and the SPA root under one eight-minute diagnostic ceiling with two-second probe bounds; each database attempt passes that token through connection-string resolution, and metadata-only DCP log enumeration has an independent two-second cancellation bound so failure construction cannot hang. Failures identify the condition/resource, elapsed time, process id, required resource names and safe Web API log counts without exposing URLs, headers, cookies, tokens, bodies, payloads or personal data. Login waits for the exact authenticated destination and its positive heading, the redundant common-step reload is removed, and organization switching waits for its exact PUT 200 plus the visible current marker. Build runs Domain Unit, Application Unit, Infrastructure Integration and Application Functional explicitly and sequentially, then runs Web Acceptance in its own step with TRX/DCP diagnostics and a failure-only GitHub-owned `actions/upload-artifact@v4` upload. The bounded review correction produced a second discriminating architecture RED, 0/1 in 42 ms, specifically because `GetConnectionStringAsync` did not receive the attempt token. Current GREEN on the exact delivered candidate: the strengthened architecture contract passes 1/1 in 13 ms, `PermissionsDoNotCrossOrganizations` passes 1/1 in 4 seconds, and the complete existing Acceptance suite passes 33/33 with zero failed or skipped in 1 minute 5 seconds; all commands exited 0 and emitted only the existing `ASPIRE010` warning. The first sandboxed structural attempt did not start tests because user NuGet configuration was inaccessible; the unchanged elevated run supplied the RED evidence. Linux CI remains the post-push proof; Phase 5 behavior is unchanged and remains next. |
