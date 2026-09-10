---
name: frontend-design-standards
description: "Trigger: any change to src/Web/ClientApp — screens, layout, navigation, forms, tables, states, theme. Apply the repository's UI composition standard on top of standard Material UI."
license: Apache-2.0
metadata:
  author: repository-maintainers
  version: "1.0"
---

## Activation Contract

Load before creating or changing anything under `src/Web/ClientApp/src`: a screen, the shell, navigation, a form,
a table, a loading or empty state, or `src/theme.jsx`. Exclude pure logic changes that render nothing —
`api/`, hooks with no markup, and test-only edits.

## The standing decision

The SPA uses **standard Material UI**. That decision is settled and this skill does not reopen it:

- Import MUI components directly (`import Button from '@mui/material/Button'`). No wrapper layer, no
  `Presentation.jsx`, no parallel design system, no named visual recipes.
- Do not invent CSS for buttons, inputs, cards, tables or states. No custom CSS variables, shadow recipes,
  radii, gradients, transitions, or hand-written hover/focus states.
- `sx` is for composition only: width, max-width, centring, layout, spacing, alignment, ordering.
- `src/theme.jsx` stays a normal, minimal MUI theme. It carries the brand palette roles, the two fonts, and
  changes to component defaults only where a default actively breaks something — each one commented with what
  it broke.

**Quality does not come from decoration here. It comes from composition**: what is on the page, in what order,
at what weight, and what the screen says when it has nothing to show. A screen that is a bare `Paper` with a
heading and a form is not "clean" — it is unfinished. The rules below are what finishes it.

## Hard rules

1. **Every screen opens with a page header.** Title (`Typography component="h1" variant="h5"`), one line of
   supporting copy when the screen needs explaining, and the screen's primary action on the same row, right
   aligned. Never bury the primary action at the bottom of a list of equal-weight buttons.
2. **One primary action per screen or per section.** `variant="contained"` is spent once. Everything else is
   `variant="outlined"` or `variant="text"`. Destructive actions are `color="error"` and are never adjacent to
   the primary action.
3. **Collections are tables or lists, never sentences.** Do not join fields with `·` into a paragraph. A row has
   columns, or it has a primary line and a secondary line (`ListItemText` `primary`/`secondary`). Header cells
   keep `component="th" scope="col"`.
4. **Status is a `Chip`, not a word.** Map the domain state to a semantic colour once per screen
   (`Active` → `success`, `Suspended` → `warning`, `Revoked`/`Closed` → `error`, pending → `default`).
   Ownership, roles and counts are `Chip size="small"` too.
5. **Every collection has an empty state.** Not the sentence "No invitations has been sent yet" floating in the
   layout — a centred block inside the container with one line saying what would be here and, when there is one,
   the action that creates the first item.
6. **Every asynchronous read has a loading state that holds the layout.** `Skeleton` shaped like the content it
   replaces, or `CircularProgress` centred in the region. The word "Loading…" is not a loading state. Keep any
   `role="status"` the tests rely on.
7. **Forms have discipline.** One column. `maxWidth` on the form, not on each field. Related fields grouped
   under a section heading when a form has more than about five of them. Helper text carries the rule
   (`helperText="Include the check digit"`), not a paragraph above the form. Submit is left-aligned under the
   last field, not stretched across the card.
8. **Feedback is `Alert`.** Errors `severity="error"`, confirmations `severity="success"`, context
   `severity="info"`. Preserve the existing `role` — `role="alert"` and `role="status"` are read by tests.
9. **Page width is chosen, not inherited.** Forms and single-object screens cap at `maxWidth: 560`. Directories,
   tables and dashboards use the full container. Never let a four-field form stretch to 1200px.
10. **Vertical rhythm is the theme's spacing scale.** `Stack spacing={3}` between page sections, `spacing={2}`
    within a form, `spacing={1}` between tightly related items. No arbitrary pixel margins.

## Contracts that outrank aesthetics

This application is covered by 306 SPA tests and 32 Reqnroll/Playwright journeys that locate controls the way a
person does. A redesign that breaks them is a regression, not a redesign.

- Never change an `id`, `name`, `data-testid`, `role`, heading level, `type`, `autoComplete`, `required`, or a
  `disabled` expression while restyling.
- Never change the text of a label, a button, a heading, or a message. If copy is genuinely wrong, say so and
  leave it.
- `TextField` with `required` **must** pass `slotProps={{ inputLabel: { required: false } }}`. MUI otherwise
  appends `" *"` to the label, which renames the field and breaks every label query.
- Native `<select>` stays native: `FormControl` + `InputLabel htmlFor` + `NativeSelect inputProps={{ id, name }}`.
- Native `<dialog>` keeps its lifecycle through `src/components/NativeDialog.jsx`. Do not replace it with
  MUI `Dialog`.
- Button labels do not uppercase. `MuiButton` sets `textTransform: 'none'` in the theme because a tenant's own
  name is rendered on a button and a page object reads that text back.
- Never edit a `*.test.jsx`, a page object under `tests/`, `AppRoutes.jsx`, or anything under `features/*/api/`.

## Decision gates

| Situation | Action |
| --- | --- |
| Reaching for custom CSS | Stop. Find the MUI component that already solves it. If none exists, the design is wrong, not MUI. |
| A screen needs a new visual pattern | Compose it from MUI primitives on that screen. Do not extract a shared wrapper until the third occurrence. |
| A default genuinely breaks a contract | Change it once in `theme.jsx`, with a comment naming what broke. |
| Copy reads badly | Report it. Do not rewrite product copy inside a visual change. |
| A test fails after restyling | Fix the implementation. Never the test. |

## Execution steps

1. Open the screen in the running application before changing it. Name what is actually wrong with it —
   missing header, no empty state, undifferentiated actions — rather than restyling by reflex.
2. Apply the hard rules. Keep every contract above intact.
3. Run the screen's own tests, then `npx eslint src/`.
4. Look at the screen again, at a wide viewport and at 375px.

## Output contract

Report which screens changed, what was wrong with each, the test command run and its result, and anything left
unfixed with the reason. No new plan documents.

## References

- [UI composition rules](references/ui-composition-rules.md) — per-pattern markup for headers, tables, empty
  states, loading, forms, and the shell.
