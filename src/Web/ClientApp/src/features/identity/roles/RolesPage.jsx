import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import FormLabel from '@mui/material/FormLabel';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import visuallyHidden from '@mui/utils/visuallyHidden';
import { toProblem } from '../api/apiTransport';
import { useIdentity } from '../context/IdentityProvider';
import { claimedFieldNames, fieldErrorText, selectFieldErrors } from '../fieldErrors';
import { ProblemMessage } from '../ProblemMessage';
import { PermissionLabel } from '../PermissionLabel';
import { useIdentityProof } from '../useIdentityProof';
import { useRead } from '../useRead';
import { roleName, useTranslation } from '../../../i18n';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

/** A section inside the shell: the outlined treatment, and the standard cap for anything that is one object. */
const section = { p: { xs: 2, sm: 3 }, maxWidth: 560 };
const frame = { maxWidth: 560 };
const supporting = { mt: 0.5, maxWidth: 640 };
const empty = { p: 4, textAlign: 'center' };
const chips = { flexWrap: 'wrap' };

/**
 * A role can hold two codes or thirty. Permission names and their secondary codes wrap inside a bounded column
 * with room for three two-line labels and their gaps; it scrolls once a role outgrows it. This
 * is layout only. Every code stays in the DOM, in the cell, in reading order; nothing moves behind a control.
 */
const permissionChips = { flexWrap: 'wrap', maxWidth: 360, maxHeight: 40 * 3 + 4 * 2, overflowY: 'auto' };

const rowActions = { flexWrap: 'wrap', justifyContent: { xs: 'flex-start', sm: 'flex-end' } };
const row = { flexWrap: 'wrap', alignItems: 'center' };
const start = { alignSelf: 'flex-start' };
/** Clears the legend the same way the checkbox rows it stands in for do. */
const catalogWait = { mt: 1 };
const responsiveTable = (theme) => ({
  '& .MuiTableHead-root, & .MuiTableHead-root .MuiTableRow-root': {
    [theme.breakpoints.down('sm')]: { display: 'block', height: 0 },
  },
  '& .MuiTableHead-root .MuiTableCell-root': {
    [theme.breakpoints.down('sm')]: visuallyHidden,
  },
  '& .MuiTableBody-root': { display: { xs: 'block', sm: 'table-row-group' } },
  '& .MuiTableBody-root .MuiTableRow-root': {
    display: { xs: 'grid', sm: 'table-row' },
    gridTemplateColumns: { xs: 'minmax(0, 1fr) auto' },
    gap: { xs: 1, sm: 0 },
    minHeight: 44,
    p: { xs: 2, sm: 0 },
  },
  '& .MuiTableBody-root .MuiTableRow-root:not(:last-of-type)': {
    borderBottom: { xs: 1, sm: 0 },
    borderColor: 'divider',
  },
  '& .MuiTableBody-root .MuiTableCell-root': {
    display: { xs: 'block', sm: 'table-cell' },
    height: { xs: 'auto', sm: 44 },
    p: { xs: 0, sm: '6px 12px' },
    borderBottom: { xs: 0, sm: 1 },
    borderColor: 'divider',
  },
  '& .MuiTableBody-root .MuiTableCell-root:nth-of-type(-n + 2)': {
    gridColumn: { xs: '1 / -1', sm: 'auto' },
  },
  '& .MuiTableBody-root .MuiTableCell-root:first-of-type': {
    fontWeight: { xs: 600, sm: 400 },
  },
});

/**
 * An action-bearing row grows to hold the theme's 40px control, the 6px cell padding above and below it, and the
 * 1px divider. The wait reserves that shape so the table arrives without pushing the page down.
 */
const ROW_HEIGHT = 6 + 40 + 6 + 1;

const EMPTY_DRAFT = { roleId: null, name: '', permissions: [], version: null };
const roleFields = ['name'];
const roleField = { name: 'name' };
const roleReadTarget = { list: 'list', pagination: 'pagination' };
const appendRoles = (current, loaded) => ({
  ...loaded,
  catalog: current.catalog,
  items: [...current.items, ...loaded.items],
});

/**
 * Custom roles inside the organization the session is operating in (IA-REQ-053).
 *
 * Two rules shape this screen and both belong to the server. The **ceiling** — you can only grant what you hold —
 * is why the catalogue is asked for rather than assumed: a code this administrator cannot grant is not offered,
 * so nobody composes a role that will be refused without being told which code was the problem. The **floor** —
 * an organization always keeps an administrator — is why a refusal here is shown rather than worked around: only
 * the server can count, and it counts after the change it is about to reject.
 *
 * Every write buys a proof first (amendment D2), and which proof depends on what this identity has: a password
 * typed into a field that is cleared the moment it is used and is never written anywhere, or a round trip to the
 * provider it signed in with (IA-REQ-025, IA-REQ-051).
 *
 * The list is shown a page at a time, because an organization can hold more roles than one page carries and a
 * screen that silently stops at a hundred is a screen that lies about what the organization has.
 */
const RolesPath = '/roles';

export function RolesPage() {
  const { t } = useTranslation('identity');
  const permissionDescriptionPrefix = useId();
  const identity = useIdentity();
  const proof = useIdentityProof();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  const [draft, setDraft] = useState(EMPTY_DRAFT);
  const [password, setPassword] = useState('');
  const [actionProblem, setActionProblem] = useState(null);
  const [actionTarget, setActionTarget] = useState(null);
  const [readTarget, setReadTarget] = useState('list');
  const [clearedServerFields, setClearedServerFields] = useState([]);
  const [isBusy, setIsBusy] = useState(false);
  const load = useCallback(async ({ cursor, signal }) => {
    const [listed, entries] = await Promise.all([
      identity.client.listRoles(tenantId, cursor, { signal }),
      identity.client.listPermissionCatalog(tenantId, { signal }),
    ]);
    return { ...listed, catalog: entries };
  }, [identity.client, tenantId]);
  const read = useRead(load, tenantId !== null);
  const roles = read.data?.items ?? null;
  const nextCursor = read.data?.nextCursor ?? null;
  const catalog = read.data?.catalog;
  const editorProblem = actionTarget === 'role-editor' ? actionProblem : null;
  const fieldErrors = selectFieldErrors(
    editorProblem,
    roleFields.filter((field) => !clearedServerFields.includes(field)),
  );

  useEffect(() => {
    if (selectFieldErrors(editorProblem, roleFields).name) document.getElementById('role-name')?.focus();
  }, [editorProblem]);

  const run = async (target, action, act, intent = null) => {
    setIsBusy(true);
    setActionTarget(target);
    setActionProblem(null);
    setClearedServerFields([]);
    try {
      // The proof is bought immediately before the change and spent by it. It is single-use, so each change
      // asks again — which is what "recent" has to mean to be worth anything. A provider proof leaves for the
      // provider rather than answering, so the change waits for the round trip instead of being sent now.
      if (action !== null && !await proof.prove(action, password, intent)) return;
      await act();
      setPassword('');
      setDraft(EMPTY_DRAFT);
      setReadTarget('list');
      await read.refresh(undefined);
    } catch (error) {
      setActionProblem(toProblem(error));
    } finally {
      setIsBusy(false);
    }
  };

  const save = () => run(
    'role-editor',
    'roles.change',
    () => (draft.roleId === null
      ? identity.client.createRole(tenantId, draft.name, draft.permissions)
      : identity.client.updateRole(tenantId, draft.roleId, draft.name, draft.permissions, draft.version)),
    { returnTo: RolesPath, operation: draft.roleId === null ? 'create' : 'update', draft });

  const retire = (role) => run(
    `retire:${role.roleId}`,
    'roles.change',
    () => identity.client.retireRole(tenantId, role.roleId, role.version),
    { returnTo: RolesPath, operation: 'retire', draft: { roleId: role.roleId, version: role.version } });

  // The edit this screen left behind, resumed once the server accepted the provider round trip. The draft is
  // replayed with the version the person actually read, so a role somebody else changed in the meantime is
  // refused by the server exactly as it would have been without the detour.
  const waiting = proof.resumable(RolesPath);
  useEffect(() => {
    if (waiting === null || roles === null || !proof.isReady) return;
    let cancelled = false;
    void Promise.resolve().then(async () => {
      if (cancelled) return;
      setIsBusy(true);
      const target = waiting.operation === 'retire' ? `retire:${waiting.draft?.roleId}` : 'role-editor';
      setActionTarget(target);
      setActionProblem(null);
      try {
        const pending = waiting.draft ?? {};
        if (waiting.operation === 'create') {
          proof.forget();
          await run('role-editor', null, () => identity.client.createRole(tenantId, pending.name, pending.permissions));
          return;
        }

        // The role may have been selected on a later page before leaving for the provider.
        let role = roles.find((candidate) => candidate.roleId === pending.roleId);
        let cursor = nextCursor;
        while (role === undefined && cursor !== null) {
          const page = await identity.client.listRoles(tenantId, cursor);
          if (cancelled) return;
          role = page.items.find((candidate) => candidate.roleId === pending.roleId);
          cursor = page.nextCursor ?? null;
        }
        if (cancelled || proof.resumable(RolesPath) !== waiting) return;
        proof.forget();
        if (role === undefined || role.isSystem || role.isRetired) return;
        if (waiting.operation === 'retire') {
          await run(target, null, () => identity.client.retireRole(tenantId, pending.roleId, pending.version));
        } else if (waiting.operation === 'update') {
          await run('role-editor', null, () => identity.client.updateRole(tenantId, pending.roleId, pending.name, pending.permissions, pending.version));
        }
      } catch (error) {
        if (!cancelled) setActionProblem(toProblem(error));
      } finally {
        if (!cancelled) setIsBusy(false);
      }
    });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [waiting, roles, nextCursor, proof.isReady, tenantId]);

  // A continuation appends rather than replaces: what the reader has already seen stays on screen, and every
  // page the server hands out is disjoint from the last, so nothing can appear twice.
  const showMore = async () => {
    setIsBusy(true);
    try {
      setReadTarget('pagination');
      await read.refresh(nextCursor, appendRoles);
    } finally {
      setIsBusy(false);
    }
  };

  const toggle = (code) => setDraft((current) => ({
    ...current,
    permissions: current.permissions.includes(code)
      ? current.permissions.filter((held) => held !== code)
      : [...current.permissions, code],
  }));

  // The table and the editor are one component, so every keystroke in the name field asks React to redraw both.
  // The table is by far the expensive half — a hundred roles is four hundred cells and two hundred controls — and
  // not one cell of it is about the draft being typed, so it is rebuilt only when something it actually shows has
  // moved. Nothing here changes what is drawn; it changes how often it is drawn again.
  const canProve = proof.canProve;
  const canRetire = proof.canBegin(password);
  const roleRows = useMemo(() => roles?.map((role) => (
    <TableRow key={role.roleId} hover>
      {/* The name is the cell. `TableCell` already sets `body2`, so wrapping one string in a `Typography` that
          asks for the size it is already being drawn at is a node per role and nothing else. */}
      <TableCell>{roleName(role, t)}</TableCell>
      <TableCell>
        {role.permissions.length === 0 ? (
          <Typography variant="caption" color="text.secondary">{t('roles.noPermissions')}</Typography>
        ) : (
          <Stack direction="row" spacing={0.5} useFlexGap sx={permissionChips}>
            {role.permissions.map((code) => <Box key={code}><PermissionLabel code={code} /></Box>)}
          </Stack>
        )}
      </TableCell>
      <TableCell>
        {/* A built-in role is the organization's own scaffolding and a retired one is spent: both are states the
            server owns, so each is shown as what it is rather than argued for in a sentence. A role can be both,
            and a role that is neither says nothing here rather than holding an empty row open. */}
        {(role.isSystem || role.isRetired) && (
          <Stack direction="row" spacing={0.5} useFlexGap sx={chips}>
            {role.isSystem && <Chip size="small" variant="outlined" label={t('roles.builtIn')} />}
            {role.isRetired && <Chip size="small" variant="outlined" color="error" label={t('roles.retired')} />}
          </Stack>
        )}
      </TableCell>
      <TableCell align="right">
        {!role.isSystem && !role.isRetired && (
          <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
            <ProblemMessage
              problem={actionTarget === `retire:${role.roleId}` ? actionProblem : null}
              autoFocus
            />
            <Button
              type="button"
              size="small"
              disabled={isBusy}
              onClick={() => {
                setActionProblem(null);
                setActionTarget(null);
                setDraft({ roleId: role.roleId, name: role.name, permissions: [...role.permissions], version: role.version });
              }}
            >
              {t('roles.edit', { name: role.name })}
            </Button>
            {canProve && (
              <Button
                type="button"
                size="small"
                color="error"
                disabled={isBusy || !canRetire}
                onClick={() => retire(role)}
              >
                {t('roles.retire', { name: role.name })}
              </Button>
            )}
          </Stack>
        )}
      </TableCell>
    </TableRow>
  )),
  // `retire` is a new closure on every render, but it captures exactly what is listed here, so listing these
  // lists it. The password is listed by value and not as the boolean above it, because a retirement spends the
  // password that was in the field at the moment it was pressed.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  [roles, isBusy, canProve, canRetire, password, identity, tenantId, actionProblem, actionTarget]);

  // The editor is always drawn, so a refusal aimed at it always reports. A retirement is drawn in the role's own
  // row, which exists only while that role is on a page that has been read — and a resumed retirement comes back
  // to page one. What no row can carry is said for the screen rather than lost.
  const claimedByRegion = actionTarget === 'role-editor'
    || (roles ?? []).some((role) => actionTarget === `retire:${role.roleId}` && !role.isSystem && !role.isRetired);
  const unclaimedProblem = claimedByRegion ? null : actionProblem;

  if (tenantId === null) {
    // Reached inside the shell, so it is composed as a screen and not as the raised card the public entrance
    // uses. The heading is the same heading: this is the roles screen in the one state where it has no roles to
    // be about.
    return (
      <Stack component="section" aria-labelledby="roles-heading" spacing={3} sx={frame}>
        <Box>
          <Typography id="roles-heading" component="h1" variant="h5">{t('common:navigation.roles')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('roles.noOrganization')}
          </Typography>
        </Box>
        {/* A dead end with exactly one way out, and no control offering it: the label would be a user-visible
            string this screen has never carried, so it is reported rather than written. */}
      </Stack>
    );
  }

  const grantable = (catalog ?? []).filter((entry) => entry.grantable);

  return (
    <Stack component="section" aria-labelledby="roles-heading" spacing={3}>
      <Box>
        <Typography id="roles-heading" component="h1" variant="h5">{t('common:navigation.roles')}</Typography>
        <Typography variant="body2" color="text.secondary" sx={supporting}>
          {t('roles.description')}
        </Typography>
      </Box>

      {/* What every change on this screen is bought with, in a section of its own. The field used to float on the
          page background between the refusal and the table while silently gating both the Retire buttons and the
          submit; framing it says that it belongs to all of them rather than to whatever it happens to sit above. */}
      {(proof.hasPassword || proof.provider !== null) && (
        <Paper variant="outlined" sx={section}>
          {proof.hasPassword ? (
            <TextField
              id="roles-password"
              label={t('login.password')}
              type="password"
              autoComplete="current-password"
              fullWidth
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          ) : (
            <Typography variant="body2" color="text.secondary">
              {t('roles.providerProof', { provider: proof.provider })}
            </Typography>
          )}
        </Paper>
      )}

      {/* The wait keeps the shape of what is coming, so the table does not arrive by pushing the editor down. The
          word stays, and stays visible: it is what a reader of the status region is told, and a live region whose
          only content is three skeletons announces nothing when it changes. */}
      <ProblemMessage problem={unclaimedProblem} autoFocus />

      {readTarget === 'list' && <ProblemMessage problem={read.problem} />}
      {readTarget === 'list' && read.status === 'errored' && (
        <Button
          type="button"
          variant="outlined"
          onClick={() => { setReadTarget(roleReadTarget.list); read.refresh(undefined); }}
          sx={start}
        >
          {t('common:actions.tryAgain')}
        </Button>
      )}
      {read.status === 'loading' && read.data === null ? (
        <Stack spacing={1} role="status">
          <Typography variant="body2" color="text.secondary">{t('roles.loading')}</Typography>
          {[0, 1, 2].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={ROW_HEIGHT} />)}
        </Stack>
      ) : roles === null ? null : roles.length === 0 ? (
        <Paper variant="outlined" sx={empty}>
          <Typography variant="body2" color="text.secondary">
            {t('roles.empty')}
          </Typography>
        </Paper>
      ) : (
        <TableContainer component={Paper} variant="outlined">
          <Table size="small" sx={responsiveTable}>
            <TableHead>
              <TableRow>
                <TableCell component="th" scope="col">{t('roles.columns.role')}</TableCell>
                <TableCell component="th" scope="col">{t('roles.columns.permissions')}</TableCell>
                <TableCell component="th" scope="col">{t('roles.columns.state')}</TableCell>
                <TableCell component="th" scope="col" align="right">{t('roles.columns.actions')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>{roleRows}</TableBody>
          </Table>
        </TableContainer>
      )}

      {(nextCursor !== null || (readTarget === 'pagination' && read.problem)) && (
        <Stack spacing={1} sx={start}>
          {readTarget === 'pagination' && <ProblemMessage problem={read.problem} />}
          {readTarget === 'pagination' && read.status === 'errored' ? (
            <Button type="button" variant="outlined" disabled={isBusy} onClick={showMore} sx={start}>
              {t('common:actions.tryAgain')}
            </Button>
          ) : nextCursor !== null ? (
            <Button type="button" variant="outlined" disabled={isBusy} onClick={showMore} sx={start}>
              {t('roles.showMore')}
            </Button>
          ) : null}
        </Stack>
      )}

      <Paper
        variant="outlined"
        component="form"
        onSubmit={(event) => { event.preventDefault(); save(); }}
        sx={section}
      >
        <Stack spacing={2}>
          {/* A section under an h5 page title, so the weight of a section heading. The level is the level it
              already was: it is still the second heading of this document, whatever size it is drawn at. */}
          <Typography component="h2" variant="subtitle1">
            {draft.roleId === null ? t('roles.new') : t('roles.editing', { name: draft.name })}
          </Typography>

          <ProblemMessage
            problem={editorProblem}
            claimedFields={claimedFieldNames(editorProblem, roleFields)}
            autoFocus={!fieldErrors.name}
          />

          <TextField
            id="role-name"
            label={t('roles.name')}
            required
            fullWidth
            slotProps={requiredField}
            value={draft.name}
            onChange={(event) => {
              setDraft({ ...draft, name: event.target.value });
              setClearedServerFields((current) => current.includes(roleField.name)
                ? current
                : [...current, roleField.name]);
            }}
            error={Boolean(fieldErrors.name)}
            helperText={fieldErrorText(fieldErrors, roleField.name, t) || undefined}
          />

          <FormControl component="fieldset">
            <FormLabel component="legend">{t('roles.permissionsToGrant')}</FormLabel>
            {/* The wait holds two checkbox rows so the group does not arrive by pushing the submit down, and the
                sentence waits for the server to have actually said it. A catalogue still in flight and a
                catalogue that came back empty are different facts and must not read alike. */}
            {catalog === undefined ? (
              read.status === 'loading' && read.data === null ? (
                <Stack spacing={1} sx={catalogWait}>
                  {[0, 1].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={44} />)}
                </Stack>
              ) : null
            ) : grantable.length === 0 && (
              <Typography variant="body2" color="text.secondary">{t('roles.noneGrantable')}</Typography>
            )}
            <FormGroup>
              {grantable.map((entry) => (
                <FormControlLabel
                  key={entry.code}
                  htmlFor={`permission-${entry.code}`}
                  control={(
                    <Checkbox
                      id={`permission-${entry.code}`}
                      slotProps={{ input: { 'aria-label': entry.code, 'aria-describedby': `${permissionDescriptionPrefix}-${entry.code}` } }}
                      checked={draft.permissions.includes(entry.code)}
                      onChange={() => toggle(entry.code)}
                    />
                  )}
                  label={<PermissionLabel code={entry.code} primaryId={`${permissionDescriptionPrefix}-${entry.code}`} />}
                />
              ))}
            </FormGroup>
          </FormControl>

          <Stack direction="row" spacing={1} useFlexGap sx={row}>
            <Button type="submit" variant="contained" disabled={isBusy || !proof.canProve || !proof.canBegin(password)}>
              {draft.roleId === null ? t('roles.create') : t('roles.save')}
            </Button>
            {draft.roleId !== null && (
              <Button
                type="button"
                variant="outlined"
                disabled={isBusy}
                onClick={() => {
                  setActionProblem(null);
                  setActionTarget(null);
                  setDraft(EMPTY_DRAFT);
                }}
              >
                {t('roles.cancel')}
              </Button>
            )}
          </Stack>
        </Stack>
      </Paper>
    </Stack>
  );
}
