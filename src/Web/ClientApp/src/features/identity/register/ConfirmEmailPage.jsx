import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useSubmit } from '../useSubmit';

const card = { p: { xs: 3, sm: 4 } };
/** Supporting copy hangs off its title rather than standing as a section of its own. */
const supporting = { mt: 0.5 };

/**
 * The screen the confirmation mail opens (IA-REQ-005).
 *
 * Both confirmation messages the system sends — the one a registered organization gets and the one an invited
 * member gets — carry their token to this same link, because both consume the same endpoint. Without this screen
 * the delivered link opened the application shell and nothing consumed the token, so an address could be
 * confirmed only by writing to the database.
 *
 * It is one button rather than a field to paste into: the recipient followed a link out of their own mailbox, and
 * asking them to transcribe a code from it would be friction that proves nothing extra.
 */
export function ConfirmEmailPage() {
  const identity = useIdentity();
  const token = useFragmentToken();
  const { submit, problem, isBusy, result } = useSubmit((secret) => identity.client.confirmEmail(secret));

  return (
    <Paper component="section" elevation={3} aria-labelledby="confirm-email-heading" sx={card}>
      <Stack spacing={3}>
        {/* A link opened without its token is a broken link, so the screen says so where it says what it is,
            rather than under the button the missing token disabled. */}
        <Box>
          <Typography id="confirm-email-heading" component="h1" variant="h5">Confirm your email</Typography>
          {!token && (
            <Typography variant="body2" color="text.secondary" sx={supporting}>
              Open the link from the confirmation email; this page needs the token it carries.
            </Typography>
          )}
        </Box>
        <ProblemMessage problem={problem} />
        {/* Confirming is the whole of this screen, so once it has happened the only thing left is the step the
            acknowledgement names. It is the action of this state rather than a footnote under it — the confirm
            button is out of the tree by then, so `contained` is still spent exactly once — and the two sit a
            related-item gap apart instead of the section gap the rest of the card is built on. */}
        {result ? (
          <Stack spacing={1}>
            <Alert severity="success" role="status">Your address is confirmed. Sign in to continue.</Alert>
            <Button component={RouterLink} to="/login" variant="contained" size="large" fullWidth>
              Sign in
            </Button>
          </Stack>
        ) : (
          <Button
            type="button"
            variant="contained"
            size="large"
            fullWidth
            disabled={isBusy || !token}
            onClick={() => submit(token ?? '')}
          >
            Confirm my address
          </Button>
        )}
      </Stack>
    </Paper>
  );
}
