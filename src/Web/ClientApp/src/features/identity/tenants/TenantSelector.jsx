import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import LinearProgress from '@mui/material/LinearProgress';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useTranslation } from '../../../i18n';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/**
 * A width decided rather than inherited. This is a collection, and a collection's default is the whole container —
 * but a row here is one control carrying one name, and there is no second column to stand beside it: the kind of
 * context a tenant is, and the fact that one of them is current, are both things this screen has no words for yet.
 * Given the container, the section would be a wide band of empty paper with a short button against its left edge.
 * So the cap stays at 560, and it stays a decision: the day a row carries a second line, this is what to revisit.
 */
const page = { maxWidth: 560 };
const header = { alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' };
const section = { p: { xs: 2, sm: 3 } };
const empty = { p: 4, textAlign: 'center' };

/**
 * The active tenant is never derived from client state: selecting one is a request, and what comes back is the
 * context the server decided on (SPEC section 7). Selection does not change the authentication state, so the
 * antiforgery pair is kept rather than bootstrapped again.
 */
export function TenantSelector() {
  const { t } = useTranslation('identity');
  const identity = useIdentity();
  const { submit, problem, isBusy } = useSubmit((tenantId) => identity.selectTenant(tenantId));
  const tenants = identity?.context?.availableTenants ?? [];
  const activeId = identity?.context?.activeTenant?.id;

  return (
    <Stack component="section" aria-labelledby="tenants-heading" spacing={3} sx={page}>
      {/* The header row every screen shares, with its right slot deliberately left empty. Choosing is the only
          thing this screen does and the choices are the list below, so there is no second action to put there —
          and nothing may be invented to fill it, because an acceptance journey counts the controls inside this
          region and expects to find exactly the tenants on offer. */}
      <Stack direction="row" spacing={2} sx={header}>
        <Box>
          <Typography id="tenants-heading" component="h1" variant="h5">{t('tenants.title')}</Typography>
        </Box>
      </Stack>

      <ProblemMessage problem={problem} />

      {tenants.length === 0 ? (
        <Paper variant="outlined" sx={empty}>
          {/* A dead end reached by the people with the fewest ways forward: nothing to choose, and nothing said
              about how to get something. The standard would put the action that creates the first item here, and
              /register is it — as a LINK, never a button, because this region's buttons are counted. Its label
              would be a user-visible string this screen has never carried, so it is reported not written. */}
          <Typography variant="body2" color="text.secondary">{t('tenants.empty')}</Typography>
        </Paper>
      ) : (
        <Paper variant="outlined" sx={section}>
          <Stack spacing={2}>
            {/* Choosing is a round trip the server answers, so the wait is shown rather than only taken away:
                disabling every button and changing nothing else reads as a page that has stopped working. */}
            {isBusy && <LinearProgress />}

            {/* The list is a list. The reset and the density that a hand-rolled `ul` was carrying in `sx` are
                what `List`/`ListItem` already are, and nothing here locates a row by its element. */}
            <List disablePadding>
              {tenants.map((tenant) => (
                <ListItem key={tenant.id} disableGutters>
                  {/* Every choice is outlined, including the one in force. Being in a context is a state rather
                      than an action, and spending the page's one filled button on a disabled control said
                      "press this" about the only thing nobody can press.

                      The label is a contract, not copy: a tenant's own name is read back off this button and
                      compared ordinally, so its text has to stay exactly the name, plus the current marker when
                      it applies, and nothing else. Anything a row later wants to say — which kind of context
                      this is, that it is the current one — arrives as a sibling of this button, never inside
                      it. Nothing is a sibling yet because that wording does not exist on this screen. */}
                  <Button
                    type="button"
                    variant="outlined"
                    disabled={isBusy || tenant.id === activeId}
                    onClick={() => submit(tenant.id)}
                  >
                    {tenant.id === activeId ? t('tenants.current', { name: tenant.name }) : tenant.name}
                  </Button>
                </ListItem>
              ))}
            </List>
          </Stack>
        </Paper>
      )}
    </Stack>
  );
}
