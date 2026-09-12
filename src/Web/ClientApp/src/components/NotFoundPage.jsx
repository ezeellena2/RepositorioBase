import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { Link as RouterLink } from 'react-router-dom';
import { useIdentity } from '../features/identity/context/IdentityProvider';
import { useTranslation } from '../i18n';

export function NotFoundPage() {
  const identity = useIdentity();
  const { t } = useTranslation('common');
  if (!identity || identity.isLoading) return null;

  const destination = identity.isAuthenticated ? '/identity' : '/login';
  const label = identity.isAuthenticated ? t('navigation.yourAccess') : t('navigation.signIn');

  return (
    <Stack component="section" aria-labelledby="not-found-heading" spacing={3}>
      <Typography id="not-found-heading" component="h1" variant="h5">
        {t('errors.notFound')}
      </Typography>
      <Link component={RouterLink} to={destination}>{label}</Link>
    </Stack>
  );
}
