# Design: Standard Material UI Migration

## Technical Approach

Reconcile the partial MUI foundation, then migrate the shell, Home, Identity, and Platform screens to direct Material UI components. `src/theme.jsx` remains the single theme, but only defines Manrope/Source Sans 3 and selected brand palette roles from read-only `tokens.css`. Standard MUI owns spacing, breakpoints, shape, elevation, interaction states, responsive behavior, generated tonal roles, and grey/common/info/warning palettes. All retained routes, behavior, copy, APIs, DOM contracts, tests, and 30 journeys remain unchanged.

## Architecture Decisions

| Decision | Choice and rationale |
| --- | --- |
| Standard component vocabulary | Screens import MUI components directly. Do not create `Presentation.jsx`, named visual recipes, generic wrappers, or a parallel design system; stock components already solve these presentation needs. |
| Minimal theme | Simplify the partial theme by removing custom neutral/action palettes, disabled ripples, zeroed shadows, overlays, and the `visual` extension. Use normal `createTheme({ colorSchemes, typography, palette })`; override only selected brand roles such as primary/error/success from `tokens.css`, plus the fonts. Do not import/copy mockup CSS, variables, gradients, radii, shadows, or transitions. |
| Color mode | One MUI `ThemeProvider` with `CssBaseline` owns light/dark/system behavior and persistence. `ThemeToggle` uses MUI `useColorScheme`; visible/accessible `auto` maps to `system`. Delete the competing Pico `ThemeContext`. |
| Semantic exceptions | Use stock `NativeSelect` with `InputLabel htmlFor` and original `id`/`name` for all five real selects. The only new adapter is `NativeDialog`: `forwardRef` over MUI System `Box component="dialog"`, preserving native `showModal()`/`close()` and close labels while its contents use direct MUI. It is behavioral, not a styling vocabulary; no MUI Modal portal replaces the native contract. |

## Direct Component Mapping

| Existing pattern | MUI target |
| --- | --- |
| Shell/navigation | `AppBar`, `Toolbar`, `Container`, `Box`, `Link`, `IconButton` |
| Forms/actions | `TextField`, `FormControl`, `InputLabel`, `NativeSelect`, `Checkbox`, `FormControlLabel`, `Button`, `Stack` |
| Page composition | `Typography`, `Card`/`Paper`, `Container`, `Stack`, `Grid`, `Box` using ordinary theme spacing |
| Collections/data | `List`, `ListItem`, `Table`, `TableHead`, `TableBody`, `TableRow`, `TableCell` with asserted `scope="col"` |
| Feedback | `Alert` with retained alert role; `Typography`/`Box` with retained status or busy role |

Centered authentication cards and hierarchy follow mockup composition only through ordinary MUI layout props and theme spacing.

## Data Flow

```text
main font imports -> App ThemeProvider/CssBaseline -> IdentityProvider -> AppBar/Container -> unchanged Routes
ThemeToggle -> useColorScheme.setMode -> MUI persistence/system resolution -> selected scheme
existing screen state/API -> direct MUI component -> preserved native role/id/name/element
```

## File Changes

| Action | Files |
| --- | --- |
| Reconcile | `src/theme.jsx`, `src/test/materialUiMigration.contract.test.js`, `src/main.jsx`, `src/App.jsx` |
| Modify | `src/components/{Layout,NavMenu,ThemeToggle,Home}.jsx` |
| Modify | Identity presentation: `ProblemMessage.jsx`; `context/IdentityContextPage.jsx`; login, register, people, lifecycle, credentials, invitations, tenants, sessions, roles, and members page JSX files |
| Modify | Platform presentation: `PlatformPanel.jsx`, `shared/PlatformStepUpForm.jsx`, identities, retention, invitation, and MFA page JSX files |
| Create | `src/components/NativeDialog.jsx` only |
| Final cleanup | Delete `src/components/ThemeContext.jsx` and `src/styles.scss`; use npm to remove Pico and unused Sass from `package.json`/lockfile. MUI, Emotion, and font packages are already installed and retained. |

`AppRoutes.jsx`, behavior providers/hooks/clients, existing tests/assertions, and acceptance page objects stay byte-for-byte unchanged.

## Strict TDD and Rollout

Revise the new migration contract test so it verifies selected branding, fonts, ThemeProvider/CssBaseline/color-mode integration, direct MUI usage, justified native adapters, and preserved contracts; it must not inspect or freeze MUI internals, generated palettes, channels, states, spacing, radii, or shadows. For each unit—foundation, shell/Home, Identity groups, Platform groups—add a focused new failing contract assertion, observe RED, migrate minimally to GREEN, then refactor. After every screen run the remaining legacy tests plus migration tests and lint; run the final build and 30 journeys when Docker is available.

Each unit is independently revertible. Remove Pico/theme context/styles only after every screen is on MUI; rollback restores the unit and, for final cleanup, those dependencies/files together. No data migration or feature flag.

## Threat Matrix

N/A — presentation composition changes no routing, shell command, subprocess, VCS/PR, executable-classification, or process-integration boundary.

## Open Questions

None.
