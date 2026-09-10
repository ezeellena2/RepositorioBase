import { useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { ProblemMessage } from '../../identity/ProblemMessage';
import { useFragmentToken } from '../../identity/useFragmentToken';
import { useSubmit } from '../../identity/useSubmit';
import { createPlatformClient } from '../api/platformClient';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

/**
 * The public entrance is a single raised card; the screens inside the shell are sections under a heading.
 *
 * The card declares no width, deliberately. `src/components/Layout.jsx` renders every path in `publicEntryPaths`
 * — which is all three entrance screens in this file — inside `Container maxWidth="xs"`, about 444px, so a 560
 * cap written here could never bind. It used to be written here anyway, which told the next reader that this
 * file chose the width when in fact it inherits it.
 */
const card = { p: { xs: 3, sm: 4 } };
const pageWidth = { maxWidth: 560 };
const section = { p: { xs: 2, sm: 3 } };

/** An empty state is a centred block in a container of its own. */
const empty = { p: 4, textAlign: 'center' };

/** The definition list the shown-once values are stated in, as label/value pairs rather than sentences. */
const facts = { m: 0 };
const value = { m: 0, mt: 0.5 };
const nextFact = { mt: 2 };

/**
 * Both shown-once halves are set out the same way: a bordered block holding nothing except what has to be copied
 * down. `py: 1` rather than the `0.5` this carried — half a step is 4px, below the theme's smallest, and it gave
 * a block somebody transcribes from a tighter rhythm than anything else on the page.
 */
const codeBlock = { px: 2, py: 1 };

/** A one-time code is six characters wide, so the FORM is — and the field fills the form it was put in. */
const codeForm = { maxWidth: 240 };
const leading = { alignSelf: 'flex-start' };

export function usePlatformClient() {
  const identity = useIdentity();
  return useMemo(() => createPlatformClient(identity.client.transport), [identity]);
}

/**
 * The public entrance, composed once for the three screens that are one.
 *
 * Each of them used to write this preamble out per branch — the raised card, the `section` landmark it names, the
 * single h1 inside it, and the rhythm under it — five copies between them, which is how one of them drifts. The
 * heading id is a parameter because every one of these screens keeps its own, and each keeps exactly one h1.
 */
function EntranceCard({ headingId, title, children }) {
  return (
    <Paper component="section" elevation={3} aria-labelledby={headingId} sx={card}>
      <Stack spacing={3}>
        <Typography id={headingId} component="h1" variant="h5">{title}</Typography>
        {children}
      </Stack>
    </Paper>
  );
}

/**
 * Registering from a Platform invitation answers with a neutral bodyless 202 whether the token was live, dead, or
 * already belonged to an account, so this page says the same thing in every case. The password is used only when
 * the matching identity is missing; for one that already exists it is ignored and cannot take over the account.
 */
export function RegisterPlatformInviteePage() {
  const identity = useIdentity();
  const platform = usePlatformClient();
  const token = useFragmentToken();
  const [password, setPassword] = useState('');
  const [continuing, setContinuing] = useState(false);
  const { submit, problem, isBusy, result } = useSubmit((secret, chosen) => platform.registerFromInvitation(secret, chosen));

  return (
    <EntranceCard headingId="platform-register-heading" title="Set up your Platform account">
      {/* Gated on the branch that can raise it. The registration form unmounts the moment the ceremony starts, so
          `problem` can only ever be stale from here on — and the ceremony renders a refusal slot of its own. Two
          `Alert`s are two `role="alert"` elements, and this screen is read as having exactly one. */}
      {!continuing && <ProblemMessage problem={problem} />}

      {continuing ? (
        // The ceremony runs here rather than behind a link because the invitation token cannot travel to another
        // page without being written down somewhere: a URL would put it in history, and storage would outlive the
        // visit. It is already in this component's memory, so the last gate is rendered where the token already is.
        <PlatformSecondFactor token={token} />
      ) : result ? (
        <Alert severity="success" role="status">
          Check your email. If that invitation is still open, we have sent you what you need to continue. Confirm
          your address, sign in, then open this invitation link again to set up your second factor.
        </Alert>
      ) : (
        <>
          <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(token ?? '', password); }}>
            <TextField
              id="platform-password"
              label="Choose a password"
              type="password"
              autoComplete="new-password"
              required
              fullWidth
              slotProps={requiredField}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy}>Continue</Button>
          </Stack>
          {/* Signed in already means the account exists and the address is confirmed, so what is left of the
              invitation is its last gate. Offering it here is what makes the mailed link the whole journey rather
              than only its first step (IA-REQ-041). It sits under the password because it is the exception: the
              visitor this page is written for has no account yet. The check is also the only thing between an
              anonymous link-holder and the gates — this route is public — so it is never widened. */}
          {identity?.isAuthenticated && (
            <>
              <Divider />
              <Button
                type="button"
                variant="outlined"
                size="large"
                fullWidth
                disabled={!token}
                onClick={() => setContinuing(true)}
              >
                Set up your second factor
              </Button>
            </>
          )}
        </>
      )}
    </EntranceCard>
  );
}

/**
 * Confirmation consumes the token the confirmation mail carried. The token arrives in the fragment, exactly as
 * the invitation token does and for the same reason, and is erased from the address bar on arrival.
 *
 * It is one button rather than a field to paste into: the recipient followed a link from their own mailbox, and
 * asking them to transcribe a code out of it would be friction that proves nothing extra. The offer being
 * confirmed comes from the envelope the server sealed, not from anything typed here.
 */
export function ConfirmPlatformInviteePage() {
  const platform = usePlatformClient();
  const confirmationToken = useFragmentToken();
  const { submit, problem, isBusy, result } = useSubmit((secret) => platform.confirmInvitation(secret));

  return (
    <EntranceCard headingId="platform-confirm-heading" title="Confirm your Platform address">
      <ProblemMessage problem={problem} />
      {result ? (
        <>
          <Alert severity="success" role="status">
            Your address is confirmed. Sign in, then open your invitation email again to set up your second factor.
          </Alert>
          {/* Confirming is done and its button is out of the tree, so the one filled button on this state is the
              step the sentence above names. It stays an anchor — `component={RouterLink}` renders `<a href>` —
              because it is read back as a link to /login, and its whole text is the name that is read. */}
          <Button variant="contained" size="large" fullWidth component={RouterLink} to="/login">Sign in</Button>
        </>
      ) : (
        <Button
          type="button"
          variant="contained"
          size="large"
          fullWidth
          disabled={isBusy}
          onClick={() => submit(confirmationToken ?? '')}
        >
          Confirm my address
        </Button>
      )}
    </EntranceCard>
  );
}

/**
 * The invitation-bound MFA ceremony: enrol, verify, acknowledge. The key and the recovery codes are shown once
 * and never again — the secret is stored encrypted and the codes only as hashes, so nothing can serve them a
 * second time. Only after acknowledgement does the Platform membership become active (IA-REQ-041).
 *
 * This route is behind a session and is not a public entrance, so it is composed as the shell composes a
 * single-object screen: the heading on the page, the ceremony in a section under it, and the width chosen here
 * rather than inherited from a container meant for a login card.
 */
export function PlatformMfaEnrollmentPage() {
  const identity = useIdentity();
  const token = useFragmentToken();

  if (!identity?.isAuthenticated) {
    return (
      <Stack component="section" aria-labelledby="platform-mfa-heading" spacing={3} sx={pageWidth}>
        <Box>
          <Typography id="platform-mfa-heading" component="h1" variant="h5">Set up your second factor</Typography>
        </Box>
        {/* Signing in is the thing that resolves this state and it is a thing the visitor can do, so the empty
            state offers the door instead of only describing it. The sentence stays whole and the way out is its
            sibling: a link put inside the sentence would break the clause a caller reads back in one piece. */}
        <Paper variant="outlined" sx={empty}>
          <Typography variant="body2" color="text.secondary">
            Sign in with the invited address, then open your invitation email again.
          </Typography>
          {/* The way out belongs here and /login is it, but its label would be a user-visible string this screen
              has never carried, so it is reported rather than written into a visual change. */}
        </Paper>
      </Stack>
    );
  }

  return (
    <Stack component="section" aria-labelledby="platform-mfa-heading" spacing={3} sx={pageWidth}>
      <Box>
        <Typography id="platform-mfa-heading" component="h1" variant="h5">Set up your second factor</Typography>
      </Box>
      <Paper variant="outlined" sx={section}>
        <PlatformSecondFactor token={token} />
      </Paper>
    </Stack>
  );
}

/**
 * The three gates themselves, given the invitation token by whoever still holds it. It is a component rather than
 * a page because the token cannot be handed from one page to another without writing it down: the page that read
 * it out of the mailed fragment is the only place it exists, so the ceremony is rendered there.
 *
 * One gate is on screen at a time, and the step in hand is the only thing carrying a filled button.
 *
 * It brings its own frame. It used to be a bare fragment that assumed whatever host mounted it had supplied a
 * `Stack` with the right rhythm, which made the ceremony's own spacing a property of two other components. The
 * surface still belongs to the host, and correctly so: on the public entrance this sits in the raised card, and
 * inside the shell it sits in an outlined section, because that difference is a fact about the route.
 */
function PlatformSecondFactor({ token }) {
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [enrollment, setEnrollment] = useState(null);
  const [code, setCode] = useState('');
  const [stage, setStage] = useState('start');
  const { submit, problem, isBusy } = useSubmit(async (action) => action());

  return (
    <Stack spacing={3}>
      <ProblemMessage problem={problem} />

      {stage === 'start' && (
        <Button
          type="button"
          variant="contained"
          disabled={isBusy}
          sx={leading}
          onClick={async () => {
            const details = await submit(() => platform.beginMfaEnrollment(token ?? ''));
            if (details) {
              setEnrollment(details);
              setStage('verify');
            }
          }}
        >
          Begin enrollment
        </Button>
      )}

      {/* Shown once and never again, so both halves are set out to be copied down rather than read past: the key
          in a block of its own, and the codes as a list somebody can tick their way through. */}
      {stage !== 'start' && enrollment && (
        <Box component="dl" sx={facts}>
          <Typography component="dt" variant="body2" color="text.secondary">Shared key</Typography>
          <Box component="dd" sx={value}>
            {/* The key is the one string in this product that is transcribed by hand, so it is given the weight
                of a value to copy instead of the weight of a sentence. What it is NOT given is grouping: a
                caller reads this element's own text and decodes it as Base32, so a space, a hyphen or a copy
                control sharing the element would corrupt the key silently rather than fail loudly. */}
            <Paper variant="outlined" sx={codeBlock}>
              {/* A key to transcribe, not a heading: `subtitle1` maps to an `h6` unless the element is stated. */}
              <Typography component="div" variant="subtitle1" data-testid="platform-shared-key">{enrollment.sharedKey}</Typography>
            </Paper>
          </Box>
          <Typography component="dt" variant="body2" color="text.secondary" sx={nextFact}>Recovery codes</Typography>
          <Box component="dd" sx={value}>
            <Paper variant="outlined" sx={codeBlock}>
              <List dense disablePadding aria-label="Recovery codes">
                {enrollment.recoveryCodes.map((recoveryCode, index) => (
                  <ListItem
                    key={recoveryCode}
                    disableGutters
                    divider={index < enrollment.recoveryCodes.length - 1}
                  >
                    <ListItemText primary={recoveryCode} />
                  </ListItem>
                ))}
              </List>
            </Paper>
          </Box>
        </Box>
      )}

      {stage === 'verify' && (
        <Stack
          component="form"
          spacing={2}
          sx={codeForm}
          onSubmit={async (event) => {
            event.preventDefault();
            const verified = await submit(() => platform.verifyMfaEnrollment(token ?? '', code));
            if (verified !== undefined) setStage('acknowledge');
          }}
        >
          <TextField
            id="platform-mfa-code"
            label="Code from your authenticator"
            type="text"
            required
            fullWidth
            slotProps={{ ...requiredField, htmlInput: { inputMode: 'numeric' } }}
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />
          <Button type="submit" variant="contained" disabled={isBusy} sx={leading}>Verify</Button>
        </Stack>
      )}

      {stage === 'acknowledge' && (
        <Button
          type="button"
          variant="contained"
          disabled={isBusy}
          sx={leading}
          onClick={async () => {
            const acknowledged = await submit(() => platform.acknowledgeRecoveryCodes(token ?? ''));
            if (acknowledged !== undefined) {
              setStage('done');
              // The membership only exists as of this moment, so the session that completed the ceremony still
              // has no active tenant. Selecting it here is what makes the ceremony end somewhere rather than
              // leaving the new administrator to work out that they must go and choose one.
              const reloaded = await identity.reload();
              const platformTenant = reloaded?.availableTenants?.find((tenant) => tenant.type === 'Platform');
              if (platformTenant) await identity.selectTenant(platformTenant.id);
            }
          }}
        >
          I have saved my recovery codes
        </Button>
      )}

      {stage === 'done' && <Alert severity="success" role="status">Your second factor is active.</Alert>}
    </Stack>
  );
}

/**
 * Bootstrap recovery, as a page anyone can reach before an owner exists. It sends nothing at all: no address, no
 * identity, no replacement recipient. Every valid state answers the same way, so the page says the same thing.
 *
 * Nothing may be added here that takes a recipient or names one — not a field, not an example address, not a
 * support line. That is a product requirement about what this page is allowed to know, not a style.
 */
export function RecoverPlatformBootstrapPage() {
  const platform = usePlatformClient();
  const { submit, problem, isBusy, result } = useSubmit(() => platform.recoverBootstrapInvitation());

  return (
    <EntranceCard headingId="platform-recover-heading" title="Resend the Platform owner invitation">
      <ProblemMessage problem={problem} />
      {result ? (
        <Alert severity="success" role="status">
          If an owner invitation is waiting and could not be delivered, a new one is on its way.
        </Alert>
      ) : (
        <Button
          type="button"
          variant="contained"
          size="large"
          fullWidth
          disabled={isBusy}
          onClick={() => submit()}
        >
          Resend
        </Button>
      )}
    </EntranceCard>
  );
}
