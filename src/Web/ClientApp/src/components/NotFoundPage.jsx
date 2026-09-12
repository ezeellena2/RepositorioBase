import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { Link as RouterLink } from 'react-router-dom';
import { useIdentity } from '../features/identity/context/IdentityProvider';

export function NotFoundPage() {
  const identity = useIdentity();
  if (!identity || identity.isLoading) return null;

  const destination = identity.isAuthenticated ? '/identity' : '/login';
  const label = identity.isAuthenticated ? 'Your access' : 'Sign in';

  return (
    <Stack component="section" aria-labelledby="not-found-heading" spacing={3}>
      <Typography id="not-found-heading" component="h1" variant="h5">
        That page does not exist
      </Typography>
      <Link component={RouterLink} to={destination}>{label}</Link>
    </Stack>
  );
}
