# CLAUDE.md

Project instructions for this repository. `AGENTS.md` holds the full agent protocol; this file is the short
version Claude Code loads on every session.

## Frontend: standard Material UI, composed well

Anything that renders under `src/Web/ClientApp/src` — a screen, the shell, navigation, a form, a table, a
loading or empty state, `src/theme.jsx` — must follow
[.agents/skills/frontend-design-standards/SKILL.md](.agents/skills/frontend-design-standards/SKILL.md) and its
[UI composition rules](.agents/skills/frontend-design-standards/references/ui-composition-rules.md). **Read them
before writing the markup, not after.**

The decision the standard encodes:

- **Standard Material UI, branded centrally.** Import components directly. No wrapper layer, no parallel design
  system, no named visual recipes, no CSS files or custom CSS variables, and no hand-written hover/focus states,
  gradients or transitions. `sx` is for layout only — width, centring, spacing and alignment. `src/theme.jsx` is
  the only visual policy layer. It carries Direction C · Indigo SaaS: `#4F46E5` primary, `#1A1F36` ink,
  `#4F566B` secondary text, `#F6F7FB` canvas, white paper and `#E3E6F0` dividers, with Manrope + Source Sans 3
  and light mode only. The approved centralized defaults are 8px controls, 12px rounded Paper, flat pill Buttons,
  a white hairline AppBar, and a soft `elevation3` for public-entry cards. Outside those defaults, do not invent
  shadow or radius recipes per screen, and do not apply the divider colour to input outlines.
- **Quality comes from composition, not decoration.** Every screen gets a page header with a title and one
  primary action; exactly one `contained` button per screen; collections are tables or two-line lists, never
  fields joined with `·`; domain status is a `Chip` with a semantic colour; every collection has an empty state;
  every async read has a loading state that holds the layout; forms cap their width and left-align submit.
- **This overrides generic design guidance.** Global advice that asks for distinctive fonts, gradients,
  textures or bespoke animation does not apply to this SPA.

## Contracts a visual change must never break

The SPA's tests and Reqnroll/Playwright journeys find controls the way a person does. While restyling, never
change an `id`, `name`, `data-testid`, `role`, heading level, `type`, `autoComplete`, `required`, a `disabled`
expression, or the `en` source value of any user-visible string. Writing another language's value is translation,
not a copy change; a copy change is a change of its own, made in every supported language. Specifically:

- `TextField` with `required` **must** pass `slotProps={{ inputLabel: { required: false } }}` — MUI otherwise
  appends `" *"` to the label and renames the field.
- Native `<select>` stays native (`FormControl` + `InputLabel htmlFor` + `NativeSelect inputProps={{ id, name }}`).
- Native `<dialog>` keeps its lifecycle through `src/components/NativeDialog.jsx`; do not swap in MUI `Dialog`.
- `MuiButton` sets `textTransform: 'none'` in the theme on purpose: a tenant's own name is rendered on a button
  and an unchanged page object reads that text back and compares it ordinally.
- In a visual change, never edit a `*.test.jsx`, a page object under `tests/`, `AppRoutes.jsx`, or anything under
  `features/*/api/`; if a test fails after a redesign, fix the implementation. A functional change may edit them
  where they assert the new behavior, listed file by file in the change — never to make a regression pass.

## Localization: every language, every change

Follow [.agents/skills/localization-standards/SKILL.md](.agents/skills/localization-standards/SKILL.md) and the
[localization delivery plan](docs/features/localization/PLAN.md). The API returns invariant codes and the client
translates them; server-delivered text uses the recipient's explicitly resolved culture. English (`en`) is the
source language, and every supported language must be complete before merge. JSX gets human-readable text from
`t()` in `src/i18n` and formats dates and numbers through `useFormat()`. Invariant technical data stays English.
Until extraction is complete, every new or changed human-readable string uses the catalogs from Phase 1 onward.

## Backend and architecture

Feature work, bug fixes, refactoring, architecture, code review and tests follow
[.agents/skills/engineering-standards/SKILL.md](.agents/skills/engineering-standards/SKILL.md). The identity and
access domain is specified in [docs/features/identity-access/SPEC.md](docs/features/identity-access/SPEC.md);
approved SPECs and accepted ADRs outrank convenience.
## Error handling: one contract, end to end

Anything that creates, maps, shows, logs or tests a failure — a command or validator, a value-object rule, an
error code, an endpoint's problem contract, response-writing middleware, the SPA transport, `useSubmit`,
`ProblemMessage`, a screen that renders a refusal, a background loop — must follow
[.agents/skills/error-handling-standards/SKILL.md](.agents/skills/error-handling-standards/SKILL.md) and its
[error-handling rules](.agents/skills/error-handling-standards/references/error-handling-rules.md).

- **Extend the architecture, never replace it.** Expected failures are `Result`/`ApplicationError` with a stable
  code and category; unexpected ones stay exceptions. One RFC 9457 writer, a generic safe `500` with a `traceId`,
  and the strict client reader (IA-REQ-038).
- **A code is born once**: factory, endpoint contract, `problemCodes.json`, client message and test in the same
  change. A code without words, or words without a code, is not done.
- **Input-only problems are field errors** — `400 validation_failed` with camelCase keys — shown on the field,
  with focus moved there. Operation codes are for refusals that depend on state.
- **Enumeration safety outranks helpfulness.** Neutral public flows never distinguish account or token state.
  Input-only field errors are allowed on them only when they pass the skill's three-part test and a parity test
  proves it. A neutral outcome still gets a next step that is true for everyone.
- **Record each unexpected exception once, safely** — type chain, frames, `traceId`, `SqlState`, never
  `Exception.Message` (IA-REQ-029). Expected refusals are not logged as errors.

Error-handling work may edit `features/*/api/`, add routes such as the not-found page, add tests and update the
ones that pin a behaviour it deliberately changes, and amend the SPEC in the same change when a SPEC row pins the
old behaviour. The "Contracts a visual change must never break" section still binds any restyling done alongside.

## Verification before calling frontend work done

```bash
cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npx vite build
```

For the journeys (needs Docker, and no AppHost already running — the harness starts its own):

```bash
dotnet test tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj --disable-build-servers -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false
```

Expected: every SPA test passes, lint clean, build succeeds, every journey passes, and `git status` shows no changes
under `tests/` beyond those the change declares.

## Running the app locally

`dotnet run --project src/AppHost`. Mailed links are written to the folder in
`dotnet user-secrets --project src/AppHost list` instead of being sent. Details in
[docs/features/identity-access/RUNNING-LOCALLY.md](docs/features/identity-access/RUNNING-LOCALLY.md).

Note: the Postgres container is `ContainerLifetime.Persistent` but has no data volume, so recreating the
container empties the database. On a fresh database EF logs one `Error` for the
`SELECT … FROM "__EFMigrationsHistory"` probe before creating the schema — that log line is expected, not a
fault.

## Delivery

Work directly on `main`; no pull requests or feature branches. After a change passes the verification in this file, commit it with a conventional message and push it to `origin/main`. Never commit or push a change whose verification failed or did not run; report it instead.
