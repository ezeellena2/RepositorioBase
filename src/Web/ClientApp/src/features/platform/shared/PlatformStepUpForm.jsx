import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';

const form = { maxWidth: 360 };

/**
 * The one form every Platform screen asks the second factor with. It is fully controlled and owns no state: the
 * code lives with whoever renders it, so it survives a screen going from gated to loaded instead of being dropped
 * when this component unmounts.
 *
 * `inputId` is a parameter because two of these can be on one page and an id may not repeat; the label points at
 * whichever one it belongs to.
 *
 * `submitVariant` is a parameter for the same reason and defaults to the gate's own weight. Where the ceremony is
 * the whole screen it is the primary action and stays `contained`; where it sits above a directory the screen's
 * primary action is elsewhere, and a second filled button would claim an emphasis this form does not have.
 */
export function PlatformStepUpForm({ inputId, code, onCodeChange, onSubmit, isBusy, submitVariant = 'contained' }) {
  return (
    <Stack
      component="form"
      spacing={2}
      aria-label="Step up"
      sx={form}
      onSubmit={(event) => { event.preventDefault(); onSubmit(); }}
    >
      <TextField
        id={inputId}
        label="Authenticator code"
        type="text"
        required
        fullWidth
        slotProps={{ inputLabel: { required: false }, htmlInput: { inputMode: 'numeric' } }}
        value={code}
        onChange={(event) => onCodeChange(event.target.value)}
      />
      <Button type="submit" variant={submitVariant} disabled={isBusy} sx={{ alignSelf: 'flex-start' }}>Step up</Button>
    </Stack>
  );
}
