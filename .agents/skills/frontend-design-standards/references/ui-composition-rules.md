# UI composition rules

Concrete markup for the patterns the [skill](../SKILL.md) requires. Everything here is stock Material UI v9 with
`sx` used only for layout. Copy the composition, not the example keys: each screen uses its existing catalog keys,
and a visual change preserves their `en` source values.

Screen snippets below assume `const { t } = useTranslation('identity')`, imported from `src/i18n`, binds the feature
namespace. Feature keys are local to that namespace; shared values use qualified `common:` keys. A screen owned by a
different feature binds that feature's namespace instead.

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
  <Button variant="contained" component={RouterLink} to="/members/invite">
    {t('members.actions.invite')}
  </Button>
</Stack>
```

Rules: one `h1`; supporting copy capped at ~640px so it stays readable on a wide screen; the primary action is
the only `contained` button on the page.

## Screen frame

```jsx
<Stack spacing={3} sx={{ maxWidth: 560 }}>   {/* forms, single-object screens */}
<Stack spacing={3}>                          {/* directories, tables, dashboards */}
```

A `Paper` is a *section*, not the page. Wrap a form or a table in one; do not wrap the whole screen in one and
call it done. Sections use `<Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>` unless the screen is a
single centred card (the public auth entrance), which keeps `elevation={3}`.

## Collections

Prefer a table when rows share fields:

```jsx
<TableContainer component={Paper} variant="outlined">
  <Table size="small">
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

Row actions: at most one visible destructive action; the rest `variant="text" size="small"`. When a row grows
past three actions, the overflow goes behind an `IconButton` + `Menu` — and only if no test names those actions.

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
  const presentation = statusPresentation[status] ?? {
    color: 'default',
    labelKey: 'memberStatus.unknown',
  };

  return <Chip size="small" label={t(presentation.labelKey)} color={presentation.color} variant="outlined" />;
};
```

Keep the domain code invariant. Status keys are local to the bound `enums` namespace. Calling
`useTranslation('enums')` inside `StatusChip` defines `t` and makes the chip update when the active language changes;
never render the raw code as translated status copy.

## Empty state

```jsx
<Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
  <Typography variant="body2" color="text.secondary">
    {t('members.empty')}
  </Typography>
  <Button variant="outlined" sx={{ mt: 2 }} component={RouterLink} to="/members/invite">
    {t('members.actions.invite')}
  </Button>
</Paper>
```

Use the screen's catalog keys. Offer the action only when the viewer can actually perform it.

## Loading

Hold the shape of what is coming:

```jsx
{rows === null ? (
  <Stack spacing={1} role="status" aria-label={t('common:loading')}>
    {[0, 1, 2].map((n) => <Skeleton key={n} variant="rounded" height={56} />)}
  </Stack>
) : rows.length === 0 ? <EmptyState /> : <Table … />}
```

If an existing English test asserts a `role="status"` element containing "Loading…", preserve that `en` catalog
value as the accessible label and put the skeleton behind it. Never remove a role a test names.

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

## The shell

- Permanent `Drawer` at `md` and up, temporary below, one nav list, sections via `ListSubheader`.
- The permanent drawer is hidden with a `theme.breakpoints.down('md')` block so a base-CSS reader (jsdom) still
  sees the links. Do not gate it on `useMediaQuery`.
- The bar carries the context switcher, the person's name and the colour-mode toggle. It carries no page title
  and no product-name placeholder.
- Mark the current route: `ListItemButton selected={pathname === to}`.
- Public entry routes (`/login`, `/register`, recovery, mailed-token screens) render the centred card with no
  shell at all.

## Density and rhythm

| Gap | Use |
| --- | --- |
| `spacing={3}` | between page sections |
| `spacing={2}` | between fields in a form, between cards in a grid |
| `spacing={1}` | between a label and its value, between adjacent buttons |

Tables use `size="small"`. Lists that carry two lines per row use `dense`. Page padding comes from the
container, never from the screen.

## What to check before calling a screen done

1. Is there a page header with a title and, if the screen has one, a single primary action?
2. Is exactly one button `contained`?
3. Does the collection have a table or list structure, an empty state, and a loading state?
4. Is every domain status a `Chip`?
5. Does the form cap its width and left-align its submit?
6. At 375px: does anything overflow horizontally?
7. Does every human-readable and accessibility string come from a catalog key?
8. Do the screen's tests still pass without being edited by a visual change?
