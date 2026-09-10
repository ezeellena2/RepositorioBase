# Proposal: Standard Material UI Migration

## Intent

Replace Pico with stock Material UI across the React SPA. Preserve behavior, accessibility, and contracts; add no product flows.

## Scope

### In Scope
- Migrate all 33 paths, Home, shell, demos, and every screen/component required by unchanged suites.
- Use stock MUI components and a responsive `AppBar`/`Container` shell directly across routes.
- Centralize only selected palette roles referenced from read-only `docs/mockups/web/src/styles/tokens.css`, plus Manrope and Source Sans 3. Never import it.
- Retain standard MUI spacing, breakpoints, shape/radii, elevation/shadows, interaction states, focus, responsive behavior, generated colors, variants, and grey/common/info/warning palettes.
- Replace Pico dark-mode bootstrap with MUI color mode; keep `lucide-react` and use local `@fontsource` assets.
- Preserve APIs, routes, states, messages, fields, validations, IDs, names, DOM contracts, and semantics. Login retains `safeReturnUrl`, Google, recovery, reactivation, and all states/messages; add no show-password and never rename `returnUrl` to `next`.
- Permit thin behavior/semantic adapters only where immutable DOM or imperative contracts require them.

### Out of Scope
- Backend changes, new flows, the dispute-resolution screen, custom visual libraries, generic primitives, `Presentation.jsx`, and `@mui/icons-material`.
- Mockup copy, code, branding, fields, flows, validation, or state.
- Custom radii, shadows, spacing, focus, exhaustive color control, named visual tokens/recipes, or recreated MUI components.

## Capabilities

### New Capabilities
- `material-ui-visual-system`: Standard MUI presentation with minimal brand palette and typography configuration.

### Modified Capabilities
- None. Business requirements remain unchanged.

## Approach

Configure brand roles and fonts in one theme, then compose screens directly with MUI and ordinary layout props. Use thin adapters only for immutable native selects/dialog lifecycle or equivalent contracts. Remove Pico after every route uses MUI.

## Affected Areas

| Area | Impact | Description |
| --- | --- | --- |
| `src/Web/ClientApp` | Modified | Theme and SPA presentation |

## Risks

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| DOM/locator drift | High | Preserve elements, attributes, roles, names, and page-object contracts |
| Custom system re-emerges | Medium | Require direct MUI; allow only contract-driven thin adapters |
| Temporary dual systems | Medium | Remove Pico/bootstrap atomically after migration |

## Rollback Plan

Revert presentation slices and dependency cleanup together, restoring Pico provider, styles, shell, and bootstrap. No data rollback is required.

## Dependencies

- Material UI, Emotion, `@fontsource/manrope`, and `@fontsource/source-sans-3`; keep `lucide-react`.

## Success Criteria

- [ ] All 34 route entries use direct standard MUI with only approved palette/font branding.
- [ ] Pico and competing theme bootstrap are removed.
- [ ] Existing 293 SPA assertions and 32 acceptance journeys/page objects pass unchanged.
- [ ] Backend, APIs, product flows, login contracts, and mockup boundary remain unchanged.
