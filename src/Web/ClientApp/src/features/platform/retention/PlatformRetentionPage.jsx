/* eslint-disable i18next/no-literal-string -- bounded wire field name, not display copy. */
import { useCallback, useState } from 'react';
import visuallyHidden from '@mui/utils/visuallyHidden';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
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
import { useIdentity } from '../../identity/context/IdentityProvider';
import { ProblemMessage } from '../../../components/ProblemMessage';
import { useRead } from '../../identity/useRead';
import { useSubmit } from '../../identity/useSubmit';
import { usePlatformClient } from '../invitations/PlatformInvitationPages';
import { PlatformStepUpForm } from '../shared/PlatformStepUpForm';
import { usePlatformStepUp } from '../shared/usePlatformStepUp';
import { useFormat, useTranslation } from '../../../i18n';
import { PermissionLabel } from '../../identity/PermissionLabel';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };
const readPermission = 'platform.retention.read';

/**
 * Two widths, and both are chosen rather than inherited. The policy itself is a table and takes the container it
 * is given; everything else on the page — a refusal, the wait, the step-up gate, both manage sections — is prose
 * or a form and is held to the standard's single 560 column. Stating the column once and building the padded
 * variants out of it is what keeps them the same width: the page used to step 1200 → 560 → 1200 → 560, so a
 * one-sentence refusal was set to the width of a five-column table and read as a banner rather than an answer.
 */
const column = { maxWidth: 560 };
const section = { p: { xs: 2, sm: 3 } };
const narrowSection = { ...section, ...column };
const notice = { p: 4, textAlign: 'center' };
const narrowNotice = { ...notice, ...column };
const leading = { alignSelf: 'flex-start' };
const confirmActions = { alignSelf: 'flex-start', flexWrap: 'wrap' };
/** A sentence keeps a reading measure even where the block around it spans the container. */
const supporting = { maxWidth: 640 };

/** Policy rows follow the 44px operational density, and the wait reserves the same stable space. */
const ROW_HEIGHT = 44;

const mobileValue = { minWidth: 0, overflowWrap: 'anywhere' };
const responsiveTable = (theme) => ({
  minWidth: 0,
  '& .MuiTableRow-root': { height: ROW_HEIGHT },
  [theme.breakpoints.up('sm')]: { minWidth: 880 },
  [theme.breakpoints.down('sm')]: {
    display: 'block',
    '& .MuiTableHead-root, & .MuiTableHead-root .MuiTableRow-root': {
      display: 'block',
      height: 0,
    },
    '& .MuiTableHead-root .MuiTableCell-root': { ...visuallyHidden },
    '& .MuiTableBody-root': { display: 'block', width: '100%' },
    '& .MuiTableBody-root .MuiTableRow-root': {
      display: 'block',
      width: '100%',
      height: 'auto',
      py: 1,
      borderBottom: 1,
      borderColor: 'divider',
    },
    '& .MuiTableBody-root .MuiTableRow-root:last-of-type': { borderBottom: 0 },
    '& .MuiTableBody-root .MuiTableCell-root': {
      display: 'block',
      minWidth: 0,
      px: 2,
      py: 0.75,
      borderBottom: 0,
      overflowWrap: 'anywhere',
    },
    '& .MuiTableBody-root .MuiTableCell-root:not([colspan])': {
      display: 'grid',
      gridTemplateColumns: 'minmax(112px, 38%) minmax(0, 1fr)',
      gap: 1,
      alignItems: 'center',
      textAlign: 'left',
    },
    '& .MuiTableBody-root .MuiTableCell-root[data-mobile-label]::before': {
      content: 'attr(data-mobile-label)',
      ...theme.typography.caption,
      color: 'text.secondary',
      fontWeight: 600,
    },
    '& .MuiTableBody-root .MuiChip-root': {
      minWidth: 0,
      maxWidth: '100%',
      height: 'auto',
      minHeight: 24,
      justifySelf: 'start',
    },
    '& .MuiTableBody-root .MuiChip-label': {
      whiteSpace: 'normal',
      overflowWrap: 'anywhere',
      py: 0.25,
    },
  },
});

/** The definition list the policy facts are stated in, as label/value pairs rather than a table of two rows. */
const facts = { m: 0, flexWrap: 'wrap' };
const fact = { m: 0, mt: 0.5 };

/**
 * The personal-data mode is a closed set the deployment declares, so the colour is a lookup rather than a
 * condition. Real data is the mode that carries consequences; a mode this screen has not been taught stays
 * neutral instead of being guessed at.
 */
const neutralChipColor = 'default';
const warningChipColor = 'warning';
const personalDataColor = { Real: warningChipColor, Synthetic: neutralChipColor };

/**
 * What the policy does to a category once its period runs out is a closed set the server owns too, so it is
 * stated the same way its neighbours are: a chip whose colour is a lookup rather than a condition. Erasure is the
 * outcome nobody can undo, so it is the one named here; an action this screen has not been taught stays neutral
 * instead of being guessed at. The trigger beside it is a classification and not a severity, so it carries no
 * colour at all — it is a chip because it is a closed set, not because it is a warning.
 */
const actionColor = { Erase: warningChipColor };

/**
 * The shape the server accepts for both a reason code and a reference, mirrored so a value it would refuse never
 * becomes a request. It mirrors and never replaces: the server stays the authority, and a value this lets through
 * can still be refused there.
 */
const REFERENCE_FORMAT = /^[A-Za-z0-9._:-]{1,64}$/;
const retentionHeadingId = 'platform-retention-heading';
const retentionStepUpInputId = 'platform-retention-step-up';
const outlinedSubmitVariant = 'outlined';
const policyStatuses = { loading: 'loading', refused: 'refused', errored: 'errored' };

/**
 * Retention: what this deployment's policy says, and the legal holds that stop an erasure (IA-REQ-056, C7).
 *
 * There is no purge control here and there never will be. Erasure belongs to the maintenance worker, driven by
 * policy, with no endpoint and no permission behind it — so what an operator can do on this screen is read the
 * rules and stop a deletion, never order one.
 *
 * One problem code answers two different situations. The policy READ needs the second factor proved in this
 * session; a CHANGE needs it proved recently. Both are refused with `recent_mfa_required` and nothing on the
 * problem document tells them apart, so the screen decides from the context it already holds: a session that still
 * owes the factor is gated before it asks for anything, and a session that has proved it keeps everything it read
 * while it proves it again.
 */
export function PlatformRetentionPage() {
  const { formatDate, formatNumber } = useFormat();
  const { t } = useTranslation('platform');
  const identity = useIdentity();
  const platform = usePlatformClient();

  const permissions = identity?.context?.permissions ?? [];
  const mayRead = permissions.includes(readPermission);
  // Reading the rules and stopping an erasure are separately trusted, and manage deliberately does not imply read.
  const mayManage = permissions.includes('platform.retention.manage');
  const owesFactor = identity?.context?.session?.requiresTwoFactor === true;

  // Nothing is asked for while the factor is owed or the permission is missing: both produce only refusals the
  // visitor can do nothing about from here.
  const policy = useRead(
    useCallback((options) => platform.readRetentionPolicy(options), [platform]),
    mayRead && !owesFactor,
  );

  const [subjectIdentityId, setSubjectIdentityId] = useState('');
  const [reasonCode, setReasonCode] = useState('');
  const [reference, setReference] = useState('');
  const [holdId, setHoldId] = useState('');
  const [pendingRelease, setPendingRelease] = useState(null);
  const [receipt, setReceipt] = useState(null);
  const [releaseNotice, setReleaseNotice] = useState(false);
  const [shapeRefusal, setShapeRefusal] = useState(null);

  const { submit, problem: actionProblem, isBusy } = useSubmit(async (action) => action());

  const refreshPolicy = policy.refresh;
  // What a proof is worth here is a fresh read, and nothing else. It is routed through the same submission that
  // holds the refusal, so the refusal the gate is made of is cleared by the act of proving rather than by a second
  // flag somebody has to remember to reset — and so that this callback can close over `refresh` alone. The refused
  // change is deliberately not in reach of it: a change replayed by the proof that unblocked it is one nobody
  // asked for twice, and the operator repeats it themselves.
  const onProved = useCallback(() => submit(() => refreshPolicy(undefined)), [submit, refreshPolicy]);
  const stepUp = usePlatformStepUp(onProved);
  const fieldOwnsStepUpProblem = stepUp.problem?.code === 'invalid_mfa_code';

  const run = async (action) => {
    setShapeRefusal(null);
    const outcome = await submit(async () => {
      const completed = await action();
      // The active hold count is part of what this screen states, so a change that moved it is read back before
      // another mutation can begin.
      await refreshPolicy(undefined);
      return completed;
    });
    if (outcome === undefined) {
      // Whatever was staged for confirmation is not the thing being answered any more. Tearing it down here is
      // what keeps a step-up gate from rendering behind a confirmation the operator never got an answer to.
      setPendingRelease(null);
      return undefined;
    }
    return outcome;
  };

  const placeHold = async () => {
    setReleaseNotice(false);
    if (!REFERENCE_FORMAT.test(reasonCode) || !REFERENCE_FORMAT.test(reference)) {
      setShapeRefusal(t('retention.place.validation', { shape: t('retention.place.referenceShape') }));
      return;
    }
    const placed = await run(() => platform.placeRetentionHold(subjectIdentityId, reasonCode, reference));
    if (placed) setReceipt(placed);
  };

  const releaseHold = async () => {
    setReceipt(null);
    const released = await run(() => platform.releaseRetentionHold(pendingRelease));
    if (released !== undefined) {
      setPendingRelease(null);
      setReleaseNotice(true);
    }
  };

  if (!mayRead) {
    return (
      <Stack component="section" aria-labelledby={retentionHeadingId} spacing={3}>
        <Typography id={retentionHeadingId} component="h1" variant="h5">{t('retention.title')}</Typography>
        <Paper variant="outlined" sx={narrowNotice}>
          <Typography variant="body2" color="text.secondary">
            {t('retention.accessDenied')}
          </Typography>
          <PermissionLabel code={readPermission} />
        </Paper>
      </Stack>
    );
  }

  // The entry gate. The read is refused to a session that has not proved the factor, so the screen asks for the
  // proof instead of asking for a policy it would only be refused. It renders in place of the data, not beside it.
  if (owesFactor) {
    return (
      <Stack component="section" aria-labelledby={retentionHeadingId} spacing={3}>
        <Typography id={retentionHeadingId} component="h1" variant="h5">{t('retention.title')}</Typography>
        {stepUp.problem && !fieldOwnsStepUpProblem && (
          <Box sx={column}><ProblemMessage problem={stepUp.problem} claimedFields={['code']} /></Box>
        )}
        <Paper variant="outlined" sx={narrowSection}>
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
              {t('retention.entryProof')}
            </Typography>
            {/* Deliberately left at the filled default. Here the ceremony IS the screen — nothing else is on it and
                nothing else can be done — so the step-up is this screen's primary action and carries its weight. */}
            <PlatformStepUpForm
              inputId={retentionStepUpInputId}
              code={stepUp.code}
              onCodeChange={stepUp.onCodeChange}
              isBusy={stepUp.isBusy}
              problem={stepUp.problem}
              onSubmit={stepUp.onSubmit}
            />
          </Stack>
        </Paper>
      </Stack>
    );
  }

  const page = policy.data;
  // Null members and no categories is a deployment with no policy at all, which is a loaded answer rather than an
  // empty one: an empty table would read as "no categories", and the truth is "nothing here will be erased".
  const hasNoPolicy = page !== null && page.policyId == null && (page.categories ?? []).length === 0;
  // The mutation gate. The session HAS proved the factor — that is what tells this apart from the entry gate — so
  // what is missing is a recent proof, and everything already read stays where it is.
  const changeNeedsRecentProof = actionProblem?.code === 'recent_mfa_required';
  const refusal = fieldOwnsStepUpProblem ? null : (stepUp.problem ?? actionProblem);
  const rules = page?.categories ?? [];

  return (
    <Stack component="section" aria-labelledby={retentionHeadingId} spacing={3}>
      <Typography id={retentionHeadingId} component="h1" variant="h5">{t('retention.title')}</Typography>

      {/* One refusal at a time, most recent first: a rejected step-up is what just happened, a value this screen
          would not send is what happened before it, and the server's refusal of the change is the oldest of the
          three. Two alerts saying different things is how somebody answers the wrong one. */}
      {(shapeRefusal || refusal) && (
        <Box sx={column}>
          {shapeRefusal
            ? <Alert severity="error" role="alert">{shapeRefusal}</Alert>
            : <ProblemMessage problem={refusal} claimedFields={stepUp.problem ? ['code'] : []} />}
        </Box>
      )}

      {changeNeedsRecentProof && (
        <Paper variant="outlined" sx={narrowSection}>
          <Stack spacing={2}>
            <Typography variant="body2">
              {t('retention.recentProof')}
            </Typography>
            {/* This gate interrupts a policy the operator is already reading and a change they already made, so it
                is not what the screen is for — the filled submit belongs to "Place hold" below it. A second filled
                button here would claim an emphasis an interruption does not have. */}
            <PlatformStepUpForm
              inputId={retentionStepUpInputId}
              code={stepUp.code}
              onCodeChange={stepUp.onCodeChange}
              isBusy={stepUp.isBusy}
              problem={stepUp.problem}
              onSubmit={stepUp.onSubmit}
              submitVariant={outlinedSubmitVariant}
            />
          </Stack>
        </Paper>
      )}

      {/* The wait holds the shape of the policy that is coming, so the rules do not arrive by pushing the rest of
          the screen down. It is deliberately NOT capped at the form column: what replaces it — the facts panel and
          the categories table — spans the container, so a 560px wait would hold the wrong shape and the policy
          would arrive by jumping sideways. The sentence stays because it is what a reader of the region is told,
          and it keeps the reading measure the rest of the page's prose has. The bars match the 44px minimum held
          by the loaded policy rows. */}
      {policy.status === 'loading' && page === null && (
        <Stack spacing={1} role="status">
          <Typography variant="body2" color="text.secondary" sx={supporting}>{t('retention.reading')}</Typography>
          {[0, 1, 2].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={ROW_HEIGHT} />)}
        </Stack>
      )}

      {page !== null && (
        <>
          {/* What the deployment declares, and what the policy under it names, are two statements and therefore two
              sections. They used to share one `Paper` with a `Divider` between them, and the seam showed: the facts
              carried the section's padding while the table below ran flush to the same border. Two outlined
              sections at the container's width put both edges on the same line and give each its own frame. */}
          <Paper variant="outlined" sx={section}>
            <Stack component="dl" direction="row" spacing={3} useFlexGap sx={facts}>
              <Box>
                <Typography component="dt" variant="body2" color="text.secondary">{t('retention.personalData')}</Typography>
                <Box component="dd" sx={fact} data-testid="retention-personal-data-mode">
                  <Chip
                    size="small"
                    variant="outlined"
                    label={t(`enums:personalDataMode.${page.personalDataMode}`)}
                    color={personalDataColor[page.personalDataMode] ?? neutralChipColor}
                  />
                </Box>
              </Box>
              <Box>
                <Typography component="dt" variant="body2" color="text.secondary">{t('retention.activeHolds')}</Typography>
                <Box component="dd" sx={fact} data-testid="retention-active-holds">
                  <Chip size="small" label={formatNumber(page.activeHoldCount)} />
                </Box>
              </Box>
            </Stack>
          </Paper>

          {hasNoPolicy ? (
            <Paper variant="outlined" sx={notice}>
              <Typography variant="body2" color="text.secondary">
                {t('retention.noPolicy')}
              </Typography>
            </Paper>
          ) : (
            <TableContainer
              component={Paper}
              variant="outlined"
              sx={{ overflowX: { xs: 'hidden', sm: 'auto' } }}
            >
              <Table
                size="small"
                sx={responsiveTable}
              >
                <TableHead>
                  <TableRow>
                    <TableCell component="th" scope="col">{t('retention.columns.category')}</TableCell>
                    <TableCell component="th" scope="col">{t('retention.columns.period')}</TableCell>
                    <TableCell component="th" scope="col">{t('retention.columns.trigger')}</TableCell>
                    <TableCell component="th" scope="col">{t('retention.columns.action')}</TableCell>
                    <TableCell component="th" scope="col">{t('retention.columns.evidenceRequired')}</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {rules.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={5} align="center">
                        <Typography variant="body2" color="text.secondary">
                          {t('retention.emptyCategories')}
                        </Typography>
                      </TableCell>
                    </TableRow>
                  ) : rules.map((rule) => (
                    <TableRow key={rule.category} hover>
                      {/* The category names the row and the period is a value, so both are read as text. The three
                          columns after them are closed sets the server owns, and all three are stated the same
                          way — a chip. Two of them being bare words next to a chipped third was the table
                          disagreeing with itself about which of its own answers count as domain state. */}
                      <TableCell data-mobile-label={t('retention.columns.category')}>
                        <Box component="span" sx={mobileValue}>{t(`enums:retentionCategory.${rule.category}`)}</Box>
                      </TableCell>
                      <TableCell data-mobile-label={t('retention.columns.period')}>
                        <Box component="span" sx={mobileValue}>{rule.retentionPeriod}</Box>
                      </TableCell>
                      <TableCell data-mobile-label={t('retention.columns.trigger')}>
                        <Chip size="small" variant="outlined" label={t(`enums:retentionTrigger.${rule.trigger}`)} />
                      </TableCell>
                      <TableCell data-mobile-label={t('retention.columns.action')}>
                        <Chip
                          size="small"
                          variant="outlined"
                          label={t(`enums:retentionAction.${rule.action}`)}
                          color={actionColor[rule.action] ?? neutralChipColor}
                        />
                      </TableCell>
                      <TableCell data-mobile-label={t('retention.columns.evidenceRequired')}>
                        <Chip
                          size="small"
                          variant="outlined"
                          label={rule.evidenceRequired ? t('retention.yes') : t('retention.no')}
                          color={rule.evidenceRequired ? warningChipColor : neutralChipColor}
                        />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </>
      )}

      {policy.status === policyStatuses.refused && <Box sx={column}><ProblemMessage problem={policy.problem} /></Box>}

      {/* A typed non-retryable refusal is answered by reading it, not by asking again. Transport failures,
          rate limits and server failures are the other screen, and that one is worth retrying. */}
      {policy.status === 'errored' && (
        <>
          <Box sx={column}><ProblemMessage problem={policy.problem} /></Box>
          <Button
            type="button"
            variant="outlined"
            onClick={() => policy.refresh(undefined)}
            sx={leading}
          >
            {t('retention.retry')}
          </Button>
        </>
      )}

      {mayManage && (
        <>
          <Paper variant="outlined" sx={narrowSection}>
            <Stack spacing={2}>
              <Typography component="h2" variant="subtitle1">{t('retention.place.title')}</Typography>
              <Stack
                component="form"
                spacing={2}
                aria-label={t('retention.place.formLabel')}
                onSubmit={(event) => { event.preventDefault(); placeHold(); }}
              >
                <TextField
                  id="retention-subject"
                  label={t('retention.place.subjectIdentity')}
                  type="text"
                  required
                  fullWidth
                  slotProps={requiredField}
                  value={subjectIdentityId}
                  onChange={(event) => setSubjectIdentityId(event.target.value)}
                />

                <TextField
                  id="retention-reason-code"
                  label={t('retention.place.reasonCode')}
                  type="text"
                  required
                  fullWidth
                  slotProps={requiredField}
                  helperText={t('retention.place.referenceShape')}
                  value={reasonCode}
                  onChange={(event) => setReasonCode(event.target.value)}
                />

                <TextField
                  id="retention-reference"
                  label={t('retention.place.reference')}
                  type="text"
                  required
                  fullWidth
                  slotProps={requiredField}
                  helperText={t('retention.place.referenceShape')}
                  value={reference}
                  onChange={(event) => setReference(event.target.value)}
                />

                <Button type="submit" variant="contained" disabled={isBusy} sx={leading}>
                  {t('retention.place.submit')}
                </Button>
              </Stack>

              {/* The only time a hold id is ever shown. No route lists holds, so an operator who does not keep this
                  has no way to name the hold again. */}
              {receipt && (
                <Alert severity="success" role="status">
                  {t('retention.place.receipt', { ...receipt, placedAt: formatDate(receipt.placedAt) })}
                </Alert>
              )}
            </Stack>
          </Paper>

          <Paper variant="outlined" sx={narrowSection}>
            <Stack spacing={2}>
              <Typography component="h2" variant="subtitle1">{t('retention.release.title')}</Typography>
              <Stack
                component="form"
                spacing={2}
                aria-label={t('retention.release.formLabel')}
                onSubmit={(event) => { event.preventDefault(); setPendingRelease(holdId); }}
              >
                <TextField
                  id="retention-hold-id"
                  label={t('retention.release.holdId')}
                  type="text"
                  required
                  fullWidth
                  slotProps={requiredField}
                  value={holdId}
                  onChange={(event) => setHoldId(event.target.value)}
                />
                {/* The section's own submit carries the section's weight. It used to be the quiet one while the
                    irreversible confirmation below it was the only filled button in this card — the loudest
                    control on the page was the one nobody should press by accident. */}
                <Button type="submit" variant="contained" disabled={isBusy} sx={leading}>
                  {t('retention.release.submit')}
                </Button>
              </Stack>

              {/* Confirmed rather than done on one click: releasing a hold is what lets the maintenance worker erase
                  the rows it was protecting, and clicking again does not put them back. */}
              {pendingRelease && (
                <Stack
                  component="form"
                  spacing={2}
                  aria-label={t('retention.release.confirmationLabel')}
                  onSubmit={(event) => { event.preventDefault(); releaseHold(); }}
                >
                  <Typography variant="body2">
                    {t('retention.release.prompt')}
                  </Typography>
                  {/* Answering the confirmation is the destructive half, so it is drawn in the error colour and
                      NOT filled: an irreversible act does not get the page's heaviest weight, and the way out of
                      it beside it is quieter still rather than its equal. */}
                  <Stack direction="row" spacing={1} useFlexGap sx={confirmActions}>
                    <Button type="submit" variant="outlined" color="error" disabled={isBusy}>{t('retention.release.confirm')}</Button>
                    <Button type="button" variant="text" onClick={() => setPendingRelease(null)}>{t('panel.cancel')}</Button>
                  </Stack>
                </Stack>
              )}

              {/* A statement about the resulting state, because that is the only thing the 204 said. Claiming this
                  request released it, or that the hold existed, would be the screen answering a question the route
                  deliberately does not answer. */}
              {releaseNotice && (
                <Alert severity="success" role="status">
                  {t('retention.release.notice')}
                </Alert>
              )}
            </Stack>
          </Paper>
        </>
      )}
    </Stack>
  );
}
