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

Read the [UI composition rules](references/ui-composition-rules.md) completely before presentation work.

## Hard rules

- Use standard Material UI v9 with Direction C · Indigo SaaS. Import MUI directly; add no wrapper layer, parallel
  design system, named recipes, custom CSS files or dependencies.
- Follow the reference's exact tokens, geometry and ownership. Theme owns global defaults, `Layout` the planes,
  `NavMenu` navigation, and routes semantic composition. Route `sx` may handle responsive layout, contained
  overflow and evidenced accessibility—not another palette, radius, shadow or animation system. Error remains
  `#B3412A` and success `#1F6B40`.
- Preserve every route, API, permission, security, MFA/proof, error, retry, confirmation and state-transition
  contract. Restyling never changes `id`, `name`, `data-testid`, `role`, label/accessibility text, heading level,
  `type`, `autoComplete`, `required`, `disabled` or existing `aria-*` relationships.
- Preserve required-label suppression, native-select association and `MuiButton.textTransform: 'none'` exactly as
  detailed in the reference. Invitation withdrawal and ownership transfer keep `window.confirm`; do not migrate
  them to dormant `NativeDialog` or MUI `Dialog` without separate authorization.
- Load `.agents/skills/localization-standards/SKILL.md` for every human-readable or accessibility string and use
  the current `src/i18n` catalog runtime. The theme is selected with `themeFor(language)` while `appTheme` remains
  the stable default export. Visual work preserves the `en` source value; translating another language is not a
  copy change, while a genuine copy change updates every supported language in the same change.
- Compose one `h1` per screen. Only an existing primary action receives contained emphasis, once within its page
  or independent section. Keep secondary, destructive and `Alert` treatments semantic. Forms/single objects cap
  at 560px; collections use the workspace.
- Collections remain one semantic table/list and one interactive route tree at every breakpoint. Preserve loading,
  empty, refused, errored, stale-data and route-specific proof continuation. Initial skeleton and terminal error
  are mutually exclusive.
- Do not invent dashboards, search, notifications, tabs, filters, pagination, actions or product copy. Do not add
  gradients, textures, decorative animation, bespoke transitions or generic visual wrappers.
- In a visual change, never edit `*.test.jsx`, page objects under `tests/`, `AppRoutes.jsx` or `features/*/api/`.
  Functional changes follow the visual/functional boundary in `CLAUDE.md`. If a test fails, fix implementation.

## Decision gates

| Situation | Action |
| --- | --- |
| A global token or default changes | Change it once in `theme.jsx`; do not hide an alternate system in route overrides. |
| A route needs responsive composition | Use local MUI `sx` on the existing semantic tree. |
| A new pattern appears | Compose it locally. Extract only after a real third occurrence, and only when semantics repeat. |
| Copy reads badly | Report it. Make a separate catalog change across every supported language. |
| A test fails after restyling | Fix the implementation. Never the test. |
| A mockup implies a control the product lacks | Omit it. Visual reference never authorizes behavior. |

## Execution steps

1. Read the current screen, frozen contracts and relevant theme/shell seam.
2. Assign ownership, then make the smallest MUI composition change on the existing semantic tree.
3. Inspect 1440px, 1024px and 375px when authorized. Run only authorized checks; prefer focused tests, then lint,
   and report unavailable proof.

## Output contract

Report changed screens/files, ownership decisions, preserved contracts, checks and results, and any remaining
risk or unavailable proof. Do not create a new plan document.

## References

- [UI composition rules](references/ui-composition-rules.md) — exact tokens, shell geometry, responsive
  composition, localization, forms, collections and operational states.
