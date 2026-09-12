# UI composition rules

Use these rules with the [skill](../SKILL.md). The SPA is stock Material UI v9; MUI `sx` handles composition,
responsive behavior and evidenced accessibility exceptions, while the theme owns the visual system.

Localization is active in the current checkout. Every human-readable and accessibility string comes from the
catalog, and `themeFor(language)` composes the matching MUI locale while preserving the `appTheme` default.
Examples bind `useTranslation('identity')` for identity screens; feature keys are local to that namespace,
another feature binds its own namespace, and shared values use qualified `common:` keys. Preserve every `en`
source value during visual work. Translating another language is not a copy change; a genuine copy change updates
every supported language in the same change.

## Ownership and visual foundation

| Layer | Owns |
| --- | --- |
| `theme.jsx` | Palette, Manrope/Source Sans 3 typography, 8px spacing, focus, density, radii and component defaults. Preserve `appTheme` and any locale-aware factory already present. |
| `Layout.jsx` | Authenticated/public planes, toolbar offset, 1440px workspace cap and responsive gutters. |
| `NavMenu.jsx` | Dark drawer geometry, permitted routes, tenant/account operations, selected state and collapse behavior. |
| Route | Semantic hierarchy, width tier, local grid/stack, responsive display and contained table overflow. |

Do not redeclare this palette in route overrides:

| Role | Value |
| --- | --- |
| Primary | `#4F46E5` |
| Primary pressed/focus | `#4338CA` |
| Navigation | `#111A33` |
| Selected navigation | `#26346D` |
| Muted navigation/focus contrast | `#B7C1DA` |
| Canvas | `#F5F7FB` |
| Surface | `#FFFFFF` |
| Primary text | `#1A1F36` |
| Secondary text | `#475467` |
| Border | `#D0D5DD` |
| Error — tested compatibility contract | `#B3412A` |
| Error surface | `#FDECEA` |
| Success — tested compatibility contract | `#1F6B40` |
| Success surface | `#EAF7EF` |
| Warning / warning surface | `#945900` / `#FFF3D6` |
| Info / info surface | `#175CD3` / `#EAF2FF` |

Manrope owns headings; Source Sans 3 owns body and controls. Page titles are semantic `h1` elements rendered as
`variant="h5"`: 24/32 below `md` and 28/36 from `md`. The theme's standalone `h1` variant remains 40/48. Body
and navigation links are 14/20, table text 13/18, and overline/group labels 11/16.

The base spacing unit is 8px. Default controls are 40px; large buttons and text/chip-only operational table/list
rows have a 44px minimum. Action-bearing rows may grow to about 53px to preserve the 40px controls. Chips are
24px, and collection-shaped skeletons match their final row type. Controls use an 8px radius, ordinary surfaces
and popovers 12px, and dialogs 14px.

Use outlined ordinary surfaces and retain the default MUI outlined-input border. The white AppBar has zero
elevation and a 1px `#D0D5DD` bottom edge. Content focus uses a 3px ring with a 2px offset; dark navigation uses
the same geometry with `#B7C1DA`. Public cards/popovers use the theme's
`0 12px 32px -12px rgba(17, 26, 51, 0.22)` shadow; dialogs use
`0 24px 64px -20px rgba(17, 26, 51, 0.34)`. Do not copy these into route `sx`.

Do not add route-local colours, shadows or radii, custom CSS files or variables, gradients, textures, decorative
animation or bespoke transitions.

## Responsive geometry

| Viewport | Authenticated shell | Public entry |
| --- | --- | --- |
| 1440px / `lg` | 64px app bar, 264px expanded drawer by default, 32px gutters, content capped at 1440px | `minmax(0, 1fr) minmax(468px, 42%)`; the 420px card sits in the right plane |
| 1024px / `md` | 64px app bar, fixed 72px compact rail, 24px gutters | One centred 420px card |
| 375px / `xs` | 56px app bar, temporary 320px dark drawer capped at `calc(100vw - 24px)`, 16px gutters | One centred card constrained by the viewport |

Below `md` use the temporary drawer; at `md` keep the useful 72px rail; at `lg` allow the current 264/72px
expanded/collapsed choice. Public routes use the exact existing allowlist in `Layout.jsx`—never infer or expand it.

## Page header

Every screen starts here. The title is the only `h1`; the action sits on the same row and survives a narrow
viewport by wrapping.

```jsx
<Stack
  direction="row"
  spacing={2}
  sx={{ alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' }}
>
  <Box>
    <Typography id="members-heading" component="h1" variant="h5">{t('members.title')}</Typography>
    <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, maxWidth: 640 }}>
      {t('members.description')}
    </Typography>
  </Box>
  {primaryAction && (
    <Button variant="contained" component={RouterLink} to={primaryAction.to}>
      {t(primaryAction.labelKey)}
    </Button>
  )}
</Stack>
```

Render an action only when it already exists in the route contract and the viewer can perform it. Use one `h1`
and cap supporting copy around 640px. The header's contained control is the page-level primary action; an
independent form section may own its own contained submit. Secondary actions are outlined/text. Destructive
actions use `color="error"` and do not sit beside the primary action. Feedback is an `Alert` with its existing
`severity` and `role`. Keep `MuiButton.textTransform: 'none'`; never uppercase a visible label or tenant name.

## Screen frame

```jsx
<Stack spacing={3} sx={{ maxWidth: 560 }}>   {/* forms and single-object screens */}
<Stack spacing={3}>                          {/* operational collections */}
```

A `Paper` is a *section*, not the page. Wrap a form or a table in one; do not wrap the whole screen in one and
call it done. Sections use `<Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>` unless the screen is a
single centred card (the public auth entrance), which keeps `elevation={3}`.

Use a 420px card for public-entry forms, 560px for authenticated forms and single-object screens, about 640px for
supporting prose, and the full bounded workspace for operational collections. Choose the width once on the
section or form; never cap every field independently.

## Collections

Prefer a table when rows share fields:

```jsx
<TableContainer component={Paper} variant="outlined" sx={{ overflowX: 'auto' }}>
  <Table size="small" sx={{ minWidth: 640, '& .MuiTableRow-root': { height: 44 } }}>
    <TableHead>
      <TableRow>
        <TableCell component="th" scope="col">{t('members.columns.member')}</TableCell>
        <TableCell component="th" scope="col">{t('members.columns.status')}</TableCell>
        <TableCell component="th" scope="col" align="right">{t('common:table.actions')}</TableCell>
      </TableRow>
    </TableHead>
    <TableBody>
      {rows.map((row) => (
        <TableRow key={row.id} hover>
          <TableCell>
            <Typography variant="body2">{row.displayName}</Typography>
            <Typography variant="caption" color="text.secondary">{row.normalizedEmail}</Typography>
          </TableCell>
          <TableCell><StatusChip status={row.status} /></TableCell>
          <TableCell align="right">
            <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>{/* actions */}</Stack>
          </TableCell>
        </TableRow>
      ))}
    </TableBody>
  </Table>
</TableContainer>
```

Use `List` + `ListItemText primary/secondary` when a row is really one thing with a subtitle. Never emit
`name · email · status · roles` as one paragraph.

Keep one semantic row/tree at every breakpoint: one `li` per list record and one table when cells/headers are a
contract. Recompose the existing cells with CSS grid when identity, state and action relationships would otherwise
become illegible at `xs`. The Platform identities and retention directories use that same-table recomposition at
`xs`, not horizontal scrolling. Never mount a hidden mobile copy of actions. Keep headers in the accessibility
tree, preserve cell order, and never replace current visible actions with an overflow menu during visual work.

Use 44px as the desktop text/chip row minimum. Action-bearing rows may grow to about 53px to preserve 40px
controls, and skeletons must match their final row type. Mobile rows may grow to stack content. Choose a table
`minWidth` from its real columns only when contained scrolling is necessary; page-level horizontal overflow is
never acceptable.

## Status

Declare the mapping once per file, at module scope:

```jsx
const statusPresentation = {
  Active: { color: 'success', labelKey: 'memberStatus.active' },
  Suspended: { color: 'warning', labelKey: 'memberStatus.suspended' },
  Revoked: { color: 'error', labelKey: 'memberStatus.revoked' },
  Closed: { color: 'error', labelKey: 'memberStatus.closed' },
};

const StatusChip = ({ status }) => {
  const { t } = useTranslation('enums');
  const item = statusPresentation[status] ?? { color: 'default', labelKey: 'memberStatus.unknown' };
  return <Chip size="small" label={t(item.labelKey)} color={item.color} variant="outlined" />;
};
```

Keep the domain code invariant. Status keys are local to the bound `enums` namespace. Calling
`useTranslation('enums')` inside the chip makes it react to language changes; never render the raw code as
translated status copy. Use theme semantic colours; never hide alternate error/success shades in a component override.

## Empty state

```jsx
<Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
  <Typography variant="body2" color="text.secondary">
    {t('members.empty')}
  </Typography>
  {emptyAction && (
    <Button variant="outlined" sx={{ mt: 2 }} component={RouterLink} to={emptyAction.to}>
      {t(emptyAction.labelKey)}
    </Button>
  )}
</Paper>
```

Use the screen's catalog keys. Offer the action only when the viewer can actually perform it.

## Loading

Hold the shape of what is coming. Use 44px for a text/chip-only row and about 53px when its final row carries a
40px action:

```jsx
{status === 'loading' && rows.length === 0 && (
  <Stack spacing={1} role="status" aria-label={t('common:loading')}>
    {[0, 1, 2].map((n) => <Skeleton key={n} variant="rounded" height={53} />)}
  </Stack>
)}
```

If an English test asserts a `role="status"` element containing "Loading…", preserve that English catalog value
and keep the skeleton in the region. Initial loading and terminal error must never render together.

| Read state | Presentation |
| --- | --- |
| Initial loading, no data | Shape-holding skeleton and existing accessible loading contract |
| Loaded, zero records | Explicit empty state only after success |
| Refused | `ProblemMessage`; preserve the existing no-retry behavior |
| Errored | `ProblemMessage` plus the existing outlined retry, when that screen has one |
| Refresh with data | Keep stale content visible; use its reserved progress slot and adjacent refusal/error |
| Mutation result | Keep separate from read state and preserve that route's proof continuation: some refresh and require a repeat; others resume one unsent intent |

## Forms

```jsx
<Paper variant="outlined" component="form" onSubmit={…} sx={{ p: { xs: 2, sm: 3 }, maxWidth: 560 }}>
  <Stack spacing={2}>
    <Typography component="h2" variant="subtitle1">{t('registration.companyDetails')}</Typography>
    <TextField
      id="register-cuit"
      label={t('people.document.cuitLabel')}
      required
      fullWidth
      slotProps={{ inputLabel: { required: false } }}
      helperText={t('people.document.checkDigitHint')}
      value={form.cuit}
      onChange={update('cuit')}
    />
    <Button type="submit" variant="contained" disabled={isBusy} sx={{ alignSelf: 'flex-start' }}>
      {t('registration.actions.submit')}
    </Button>
  </Stack>
</Paper>
```

`fullWidth` on fields, `maxWidth` on the form. Submit is left-aligned and never `fullWidth` — except on the
public auth cards, where the card *is* the form and a full-width button is the convention.

Preserve every field's `id`, `name`, label, `type`, `autoComplete`, `required` and `disabled` expression.
Required `TextField` controls keep `slotProps={{ inputLabel: { required: false } }}` so MUI does not append an
asterisk to the accessible label. Native selects remain `FormControl` + `InputLabel htmlFor` + `NativeSelect
inputProps={{ id, name }}`.

Keep destructive confirmations inline when they are inline today. Invitation withdrawal and organization
ownership transfer retain `window.confirm` until separately authorized; do not migrate them to the dormant
`NativeDialog` or MUI `Dialog` as part of presentation work.

## The shell

- The app bar is 56px below `sm` and 64px from `sm` upward. It uses the white surface, zero elevation and hairline
  border above. Keep real tenant/user context; do not add search or notifications.
- Navigation is dark in expanded, compact and temporary modes. At `md` it is a 72px icon rail; at `lg` it uses
  the current 264/72px choice. The temporary drawer exposes the same permitted destinations.
- A selected destination uses background plus an accent/indicator and `aria-current`; colour alone is insufficient.
- Preserve these exact accessible names: “Primary navigation”, “Account entry”, “Choose an organization”,
  “Open navigation”, “Collapse navigation”, “Expand navigation”, and “Change workspace”. Preserve
  `aria-expanded`, `aria-controls` and `aria-haspopup="menu"`.
- Keep tenant/account operations, permission visibility and collapse behavior unchanged. Do not invent a route,
  product title, colour-mode toggle or overflow action.
- Route content must keep one responsive interactive tree. The existing permanent/temporary drawer pair is a
  shell implementation detail, not permission to duplicate a route's forms or actions.
- Public-entry routes use the existing allowlist, omit authenticated navigation/banner, and render a 420px card.
  At `lg` the card occupies the right plane; below `lg` it is centred in one plane. Add no fabricated product copy.

## Density and rhythm

| Gap | Use |
| --- | --- |
| `spacing={3}` | between page sections |
| `spacing={2}` | between fields in a form, between cards in a grid |
| `spacing={1}` | between a label and its value, between adjacent buttons |

Tables use `size="small"`. Lists that carry two lines per row use `dense`. Page padding comes from the
container, never from the screen.

## Prohibited additions

Do not add a dashboard, search, notifications, tabs, filters, pagination, controls or copy merely because a
reference image contains them. Preserve real cursor controls where the route already owns them. Do not add
generic visual wrappers; repeat MUI composition until a third occurrence proves shared semantics.

## What to check before calling a screen done

1. Is there a page header with a title and, if the screen has one, a single primary action?
2. Does each screen or independent section spend `contained` emphasis only on its primary action?
3. Does the collection have a table or list structure, an empty state, and a loading state?
4. Is every domain status a `Chip`?
5. Are secondary, destructive and feedback treatments semantically correct?
6. Are initial skeleton, empty, refused, errored and stale-data states mutually coherent?
7. Does the form cap its width, preserve every field contract and left-align its submit?
8. At 375px, is route content one interactive DOM with no page-level overflow?
9. Are all existing accessible names, roles, relationships, selectors and confirmation mechanisms unchanged?
10. Does copy follow the active checkout's localization contract without changing the English source value?
11. Did the change avoid invented product controls, decorative styling and generic wrappers?
12. Do focused tests and visual checks pass without editing tests, or is unavailable proof reported honestly?
