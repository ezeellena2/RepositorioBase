import Box from '@mui/material/Box';
import { PermissionLabel } from '../PermissionLabel';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useTranslation } from '../../../i18n';
import { useIdentity } from './IdentityProvider';

const page = { maxWidth: 560 };
const section = { p: { xs: 2, sm: 3 } };

/**
 * The header keeps the shape every screen opens with even though its right slot is empty, because the emptiness
 * is the decision: this screen reports what the session already holds and there is nothing here to do, so it has
 * no primary action to right-align. It must not borrow one either — the shell's own "Invite a member" link is
 * counted from this route, and a second element with that name would make the count wrong.
 */
const header = { alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' };

/**
 * A pair reads across, not down. Each `dt` claims a fixed label column and its `dd` takes the rest, so the 560
 * this screen deliberately caps at is spent on the answers instead of leaving the right half of every row blank.
 * The label column is shared by both lists, which is what keeps the values on one vertical line across the two
 * sections; `max-content` is its floor so a longer label grows the column rather than wrapping inside it.
 *
 * The `dt` and `dd` are the grid's own children — the wrapper each pair used to carry is exactly what forced the
 * value underneath its label — and their document order is unchanged, which matters: a caller reads the active
 * organization back as the second `dd` on the page. A phone is too narrow for two columns, so there the pair
 * stacks and the rhythm tightens with it.
 */
const facts = {
  display: 'grid',
  gridTemplateColumns: { xs: '1fr', sm: 'minmax(max-content, 10rem) 1fr' },
  columnGap: 3,
  rowGap: { xs: 1, sm: 2 },
  m: 0,
};

/** A `dd` carries a browser indent. `Typography` already resets its own margin; a `Stack` does not. */
const chips = { m: 0, flexWrap: 'wrap' };

/**
 * What the session actually grants, as the server reports it. The permissions are shown for orientation only —
 * every operation is authorized again server-side, so this page never gates anything by itself.
 */
export function IdentityContextPage() {
  const identity = useIdentity();
  const context = identity?.context;
  const { t } = useTranslation('identity');

  // Codes share a prefix per area, so ordering them puts an area's permissions beside each other and a long list
  // becomes scannable without a heading naming each group. Sorted on a copy: the context is not this page's to
  // rearrange.
  const permissions = [...(context?.permissions ?? [])].sort((left, right) => left.localeCompare(right));

  return (
    <Stack component="section" aria-labelledby="identity-heading" spacing={3} sx={page}>
      <Stack direction="row" spacing={2} sx={header}>
        <Box>
          <Typography id="identity-heading" component="h1" variant="h5">{t('common:navigation.yourAccess')}</Typography>
        </Box>
      </Stack>

      {/* Who the session is and where it is standing: two answers to the same question, so they share a section
          and are read as one pair of facts. */}
      <Paper variant="outlined" sx={section}>
        <Box component="dl" sx={facts}>
          <Typography component="dt" variant="subtitle2" color="text.secondary">{t('context.signedInAs')}</Typography>
          <Typography component="dd" variant="body1">
            {context?.user?.displayName ?? t('context.unknown')}
          </Typography>
          <Typography component="dt" variant="subtitle2" color="text.secondary">{t('context.activeOrganization')}</Typography>
          <Typography component="dd" variant="body1">
            {context?.activeTenant?.name ?? t('context.noneSelected')}
          </Typography>
        </Box>
      </Paper>

      {/* What standing there grants is a collection, not a third fact of the same size: it wraps, it grows with
          the roles held, and it belongs to the organization above rather than to the identity. Its own section
          gives the wrap room and stops a wall of codes from crowding the two lines that name the session. */}
      <Paper variant="outlined" sx={section}>
        <Box component="dl" sx={facts}>
          <Typography component="dt" variant="subtitle2" color="text.secondary">{t('context.permissions')}</Typography>
          {permissions.length === 0
            ? (
              <Typography component="dd" variant="body2" color="text.secondary">
                {t('context.noneInThisOrganization')}
              </Typography>
            )
            : (
              <Stack component="dd" direction="row" spacing={1} useFlexGap sx={chips}>
                {permissions.map((permission) => (
                  <Box key={permission}><PermissionLabel code={permission} /></Box>
                ))}
              </Stack>
            )}
        </Box>
      </Paper>
    </Stack>
  );
}
