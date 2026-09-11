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

- **Standard Material UI.** Import components directly. No wrapper layer, no parallel design system, no named
  visual recipes, no invented CSS for buttons, inputs, cards, tables or states, no custom CSS variables,
  gradients, shadow recipes, radii or transitions. `sx` is for layout only — width, centring, spacing,
  alignment. `src/theme.jsx` stays a normal minimal theme: brand palette roles, Manrope + Source Sans 3, and a
  component default overridden only where a default broke something, with a comment saying what.
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
