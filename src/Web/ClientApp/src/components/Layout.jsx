import Box from '@mui/material/Box';
import Container from '@mui/material/Container';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { useLocation } from 'react-router-dom';
import { useTranslation } from '../i18n';
import { NavMenu, shellToolbarSx } from './NavMenu';

/**
 * The routes somebody reaches before they hold a session. Every one of them is a way in — signing in, registering,
 * recovering a credential, spending a mailed token — and none of them is reached from a menu.
 *
 * They are listed rather than derived because being public is a property of the route, not of the visitor: a
 * confirmation link is opened by a browser with no session, and the same address later belongs to somebody who
 * has one. `/invitations/accept` and `/external/return` are deliberately absent, because both are answered by a
 * session that already exists and both hand straight over to a screen inside the shell.
 */
const publicEntryPaths = new Set([
  '/login',
  '/register',
  '/organizations/register',
  '/personal/register',
  '/credentials/forgot',
  '/credentials/reset',
  '/confirm-email',
  '/account/reactivation-request',
  '/account/reactivate',
  '/invitations/register',
  '/platform/invitations/register',
  '/platform/invitations/confirm',
  '/platform/bootstrap/recover',
]);

/**
 * Two compositions, chosen by where the visitor is.
 *
 * The application shell is a sidebar and a bar above it. The public entrance is neither: that navigation names
 * screens a session opens, so offering it to somebody who has come to get one is a menu of things they cannot do
 * yet. What they get instead is one entry card and, where the viewport has room, a quiet product-identity plane.
 */
export function Layout({ children }) {
  const { pathname } = useLocation();
  const { t } = useTranslation('common');

  if (publicEntryPaths.has(pathname)) {
    return (
      <Box
        sx={{
          minHeight: '100dvh',
          display: { xs: 'flex', lg: 'grid' },
          gridTemplateColumns: { lg: 'minmax(0, 1fr) minmax(468px, 42%)' },
          backgroundColor: 'background.default',
        }}
      >
        <Box
          aria-hidden
          sx={{
            display: { xs: 'none', lg: 'flex' },
            alignItems: 'flex-start',
            minWidth: 0,
            p: 6,
            backgroundColor: 'navigation.main',
            color: 'common.white',
          }}
        >
          <Typography component="div" variant="h5">
            {t('navigation.brand')}
          </Typography>
        </Box>
        <Box
          component="main"
          sx={{
            display: 'flex',
            flex: 1,
            alignItems: 'center',
            justifyContent: 'center',
            minWidth: 0,
            px: { xs: 2, sm: 3, lg: 6 },
            py: { xs: 4, sm: 6 },
          }}
        >
          <Box sx={{ width: '100%', maxWidth: 420 }}>
            {children}
          </Box>
        </Box>
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100dvh' }}>
      <NavMenu />
      <Box
        component="main"
        sx={{ flexGrow: 1, minWidth: 0, minHeight: '100dvh', backgroundColor: 'background.default' }}
      >
        {/* Sits under the fixed bar, so the first thing on a page is not hidden behind it. */}
        <Toolbar sx={shellToolbarSx} />
        <Container
          maxWidth={false}
          disableGutters
          sx={{ width: '100%', maxWidth: 1440, mx: 'auto', px: { xs: 2, sm: 3, lg: 4 }, py: 3 }}
        >
          {children}
        </Container>
      </Box>
    </Box>
  );
}
