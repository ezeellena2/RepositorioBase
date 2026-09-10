import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

const card = { p: { xs: 3, sm: 4 } };

/**
 * Registration answers with a neutral bodyless 202 whether or not the address is already taken, so the page says
 * the same thing in either case. Telling the visitor which one happened would answer a question the API
 * deliberately refuses to answer (SPEC section 6).
 */
export function RegisterOrganizationPage() {
  const identity = useIdentity();
  const [form, setForm] = useState({ email: '', password: '', legalName: '', cuit: '' });
  const { submit, problem, isBusy, result } = useSubmit((request) => identity.client.registerOrganization(request));
  const update = (field) => (event) => setForm((current) => ({ ...current, [field]: event.target.value }));

  return (
    <Paper component="section" elevation={3} aria-labelledby="register-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="register-heading" component="h1" variant="h5">Register an organization</Typography>
        {/* One frame, two states. The acknowledgement used to be a second copy of this card, which is how the two
            drifted: only the form branch ever rendered a refusal, so a failure that arrived after a success had
            nowhere to go. The heading, its id and the problem slot now belong to the card rather than to a
            branch, and what changes is only what sits under them. */}
        <ProblemMessage problem={problem} />
        {result ? (
          <Alert severity="success" role="status">
            If that address can register, we have sent it a confirmation link. Check the inbox.
          </Alert>
        ) : (
        <Stack component="form" spacing={3} onSubmit={(event) => { event.preventDefault(); submit(form); }}>
          {/* Two things are being registered at once — the company, and the person who will sign in for it — so
              the fields are asked for in those two groups rather than as one run of four. */}
          <Stack spacing={2}>
            <TextField
              id="register-legal-name"
              label="Legal name"
              required
              fullWidth
              slotProps={requiredField}
              value={form.legalName}
              onChange={update('legalName')}
            />
            <TextField
              id="register-cuit"
              label="CUIT"
              required
              fullWidth
              slotProps={requiredField}
              value={form.cuit}
              onChange={update('cuit')}
            />
          </Stack>
          <Stack spacing={2}>
            <TextField
              id="register-email"
              label="Email"
              type="email"
              autoComplete="username"
              required
              fullWidth
              slotProps={requiredField}
              value={form.email}
              onChange={update('email')}
            />
            <TextField
              id="register-password"
              label="Password"
              type="password"
              autoComplete="new-password"
              required
              fullWidth
              slotProps={requiredField}
              value={form.password}
              onChange={update('password')}
            />
          </Stack>
          <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy}>Register</Button>
        </Stack>
        )}
      </Stack>
    </Paper>
  );
}
