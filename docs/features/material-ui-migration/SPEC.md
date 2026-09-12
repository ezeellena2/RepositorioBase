# Material UI Visual System Specification

## Purpose

Define the SPA’s standard Material UI presentation while preserving behavior, routes, semantics, and locators. This is visual-only: it MUST NOT alter backend behavior, validation, or branding.

## Requirements

### Requirement: Standard MUI with minimal centralized branding

One MUI theme MUST centralize selected brand palette roles and Manrope and Source Sans 3 typography. Customization MUST be minimal: standard MUI spacing, breakpoints, radii, elevation, interaction states, grey/common/info/warning palettes, focus, and responsive behavior MUST remain available. Screens MUST NOT add brand literals, loose CSS, or wrappers recreating standard MUI components.

#### Scenario: Brand palette change propagates

- GIVEN routes render through the shared MUI theme
- WHEN a branded primary role changes
- THEN screens consuming it reflect the change without screen-local edits

#### Scenario: Framework defaults remain usable

- GIVEN a screen uses MUI components
- WHEN it renders across schemes and viewports
- THEN MUI spacing, states, elevation, and responsiveness remain available

### Requirement: Direct MUI vocabulary and standard shell

Screens MUST use MUI directly for Button, TextField, Card/Paper, AppBar, Container, Table, Alert, lists, and compatible dialogs/selects. Thin adapters MAY exist only for immutable native DOM or imperative contracts. A responsive MUI AppBar/Container shell MUST preserve all 33 paths, Home, demos, screens, and behavior.

#### Scenario: Standard components compose the shell

- GIVEN a user visits routes at narrow/wide viewports
- WHEN it renders
- THEN MUI components provide presentation without changing navigation

#### Scenario: Semantic adapter has a justified boundary

- GIVEN contract requires a native select or dialog lifecycle
- WHEN the screen is migrated
- THEN a thin adapter preserves that contract without recreating MUI

### Requirement: Semantic and locator contract preservation

The migration MUST preserve DOM/test contracts: five selects remain native <select> elements; submit buttons retain type and IDs; table headers retain scope="col"; roles, labels, names, headings, lists, and definition lists remain queryable.

#### Scenario: Form and table semantics survive

- GIVEN a migrated form or table is rendered
- WHEN role queries inspect it
- THEN selects, button attributes, scopes, labels, and names match

#### Scenario: Existing automated locators still resolve

- GIVEN unchanged suites execute
- WHEN they locate controls by existing roles, labels, IDs, or names
- THEN locators resolve without assertion changes

### Requirement: Behavior, typography, and reference boundaries

It MUST preserve API calls, states, copy, fields, validations, routes, IDs, accessible names, and login safeReturnUrl, Google, recovery, reactivation, and messages. It MUST retain lucide-react and approved fonts. docs/mockups/web/src/styles/tokens.css MUST remain read-only and MAY supply selected brand palette values only; mockups MAY guide centered-card composition, hierarchy, and spacing, but MUST NOT supply copy, fields, validation, flows, state, branding, or code. Backend changes, new flows, @mui/icons-material, and a document-dispute screen are out of scope; returnUrl MUST NOT become next, and no show-password may be added.

#### Scenario: Login behavior is unchanged

- GIVEN a user exercises successful, failed, recovery, reactivation, Google, and return-url login paths
- WHEN migrated login renders and submits
- THEN states, messages, fields, IDs, names, and safeReturnUrl remain unchanged without show-password

#### Scenario: Reference material cannot expand scope

- GIVEN implementation consults mockup or the dispute endpoint
- WHEN migration is delivered
- THEN approved visual guidance is used and no dispute screen or flow appears

### Requirement: One MUI color-mode mechanism and regression gate

MUI color-mode mechanism MUST be the only theme system; Pico’s dark bootstrap and competing path MUST be removed. Delivery MUST pass 293 unchanged SPA tests and 30 unchanged Reqnroll/Playwright journeys with zero assertion changes; backend and API behavior MUST remain unchanged.

#### Scenario: Legacy theme bootstrap is absent

- GIVEN the migrated application starts
- WHEN initialization is inspected
- THEN Pico scripting and competing systems are absent

#### Scenario: Regression gate passes

- GIVEN the complete migration candidate
- WHEN SPA and journey suites run
- THEN tests pass and existing assertions/page objects remain byte-for-byte unchanged
