import { useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import { Menu as MenuIcon, ChevronDown } from 'lucide-react';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import NativeSelect from '@mui/material/NativeSelect';
import IconButton from '@mui/material/IconButton';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import ListSubheader from '@mui/material/ListSubheader';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../features/identity/context/IdentityProvider';
import { ProblemMessage } from '../features/identity/ProblemMessage';
import { supportedLanguages, useTranslation } from '../i18n';

export const drawerWidth = 264;
/** Two rows keep localized controls visible on phones; every fixed-bar spacer uses the same height. */
export const shellToolbarSx = { minHeight: { xs: 112, sm: 64 } };
const toolbarSx = { ...shellToolbarSx, gap: 1, flexWrap: { xs: 'wrap', sm: 'nowrap' }, py: { xs: 1, sm: 0 } };

/**
 * The sidebar is the product's navigation, so it is always present on a wide screen rather than behind a
 * control. Below `md` it becomes the temporary drawer the toggle opens, which is the only place a hamburger
 * belongs: hiding a permanent menu behind one on a desktop is a mobile pattern applied where it costs a click
 * and buys nothing.
 */
const hideAtNarrow = (theme) => ({ [theme.breakpoints.down('md')]: { display: 'none' } });
const hideAtWide = (theme) => ({ [theme.breakpoints.up('md')]: { display: 'none' } });

/**
 * The bar is not where a tenant's full name is read, so the trigger truncates rather than pushing the person's
 * name and the account actions off the end of the toolbar. The ceiling is the drawer's own width: a name that fits
 * the navigation standing beside it fits here. The label needs `minWidth: 0` of its own, because a flex child
 * refuses to shrink below its text until it is told it may.
 */
const switcherTrigger = { maxWidth: drawerWidth };
const switcherLabel = { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' };
const navigationTextSlotProps = { primary: { variant: 'body2', component: 'span' } };
const languageInputProps = { id: 'shell-language', name: 'language' };
const languageControlSize = 'small';

function LanguageSelector() {
  const identity = useIdentity();
  const { t, i18n } = useTranslation('common');
  const displayedLanguage = identity.pendingLanguage ?? i18n.resolvedLanguage;
  return (
    <>
      <FormControl size={languageControlSize} sx={{ minWidth: 96, flexShrink: 0 }}>
        <InputLabel htmlFor={languageInputProps.id}>{t('language.label')}</InputLabel>
        <NativeSelect
          inputProps={{
            ...languageInputProps,
            'aria-busy': identity.pendingLanguage !== null ? true : undefined,
          }}
          value={displayedLanguage}
          disabled={identity.isLoading}
          onChange={(event) => {
            void identity.changeLanguage(event.target.value).catch(() => undefined);
          }}
        >
          {supportedLanguages.map((language) => (
            <option key={language} value={language}>{t(`language.${language}`)}</option>
          ))}
        </NativeSelect>
      </FormControl>
      {identity.languageProblem !== null && (
        <Box sx={{ position: 'fixed', left: '50%', bottom: 24, zIndex: 'snackbar', transform: 'translateX(-50%)' }}>
          <ProblemMessage problem={identity.languageProblem} autoFocus />
        </Box>
      )}
    </>
  );
}

const brand = { color: 'inherit', textDecoration: 'none', whiteSpace: 'nowrap' };

/**
 * A link says where it goes. `selected` is the only thing that says where you already are, and without it a
 * column of nineteen identical rows is a list of addresses rather than a place. It is presentation only — the row
 * stays a link with the same accessible name every caller looks it up by — so `aria-current` carries the same
 * fact to somebody who cannot see which row is filled.
 */
function NavItem({ to, children }) {
  const { pathname } = useLocation();
  const current = pathname === to;

  return (
    <ListItem disablePadding>
      <ListItemButton
        component={RouterLink}
        to={to}
        selected={current}
        aria-current={current ? 'page' : undefined}
      >
        <ListItemText primary={children} slotProps={navigationTextSlotProps} />
      </ListItemButton>
    </ListItem>
  );
}

function Section({ title, children }) {
  return (
    <List dense subheader={<ListSubheader disableSticky>{title}</ListSubheader>}>
      {children}
    </List>
  );
}

/**
 * What the navigation offers follows the session, and the permissions only decide what is worth showing. Every
 * action behind these links is authorized again by the server, so hiding one is a courtesy to the user rather
 * than a control (SPEC section 7).
 *
 * It is written for a session that exists, because that is the only state it is rendered in: a visitor is offered
 * the bar and nothing else.
 */
function NavContents({ onNavigate }) {
  const identity = useIdentity();
  const navigate = useNavigate();
  const { t } = useTranslation('common');

  const handleSignOut = async (event) => {
    event.preventDefault();
    await identity.signOut();
    navigate('/login');
  };

  const permissions = identity.context?.permissions ?? [];
  const platform = permissions.includes('platform.organizations.read')
    || permissions.includes('platform.identities.read')
    || permissions.includes('platform.retention.read');

  return (
    <Box onClick={onNavigate}>
      <Section title={t('navigation.accountSection')}>
        <NavItem to="/identity">{t('navigation.yourAccess')}</NavItem>
        <NavItem to="/identity/profile">{t('navigation.yourProfile')}</NavItem>
        <NavItem to="/identity/account">{t('navigation.yourAccount')}</NavItem>
        <NavItem to="/identity/sessions">{t('navigation.yourDevices')}</NavItem>
        <NavItem to="/identity/password">{t('navigation.yourPassword')}</NavItem>
        <NavItem to="/identity/external">{t('navigation.signInProviders')}</NavItem>
      </Section>

      <Divider />
      <Section title={t('navigation.organizationsSection')}>
        <NavItem to="/organizations/select">{t('navigation.organizations')}</NavItem>
        {permissions.includes('roles.read') && <NavItem to="/roles">{t('navigation.roles')}</NavItem>}
        {permissions.includes('members.read') && <NavItem to="/members">{t('navigation.members')}</NavItem>}
        {permissions.includes('members.invite') && <NavItem to="/members/invite">{t('navigation.inviteMember')}</NavItem>}
      </Section>

      {/* Offered only to a session already operating as Platform. It is a convenience, not a control: the
          panel and the API both reauthorize regardless of what the navigation shows. */}
      {platform && (
        <>
          <Divider />
          <Section title={t('navigation.platformSection')}>
            {permissions.includes('platform.organizations.read') && <NavItem to="/platform">{t('navigation.platform')}</NavItem>}
            {permissions.includes('platform.identities.read') && <NavItem to="/platform/identities">{t('navigation.platformIdentities')}</NavItem>}
            {permissions.includes('platform.retention.read') && <NavItem to="/platform/retention">{t('navigation.retention')}</NavItem>}
          </Section>
        </>
      )}

      <Divider />
      <Section title={t('navigation.examplesSection')}>
        <NavItem to="/">{t('navigation.home')}</NavItem>
        <NavItem to="/counter">{t('navigation.counter')}</NavItem>
      </Section>

      <Divider />
      <List dense>
        <ListItem disablePadding>
          <ListItemButton component="a" href="/login" onClick={handleSignOut}>
            <ListItemText primary={t('navigation.logOut')} slotProps={navigationTextSlotProps} />
          </ListItemButton>
        </ListItem>
      </List>
    </Box>
  );
}

/**
 * Moving between the contexts one identity holds, without going through the chooser screen first. A `Personal`
 * tenant and an `Organization` are the same person in two places, so the switch is presented as one list and each
 * entry says which kind it is — the difference between operating as yourself and operating as a company is the
 * thing somebody is actually choosing between.
 *
 * The trigger is labelled for what it does rather than for the tenant it shows, so that reading a tenant's name
 * off the screen still finds only the chooser's own controls.
 */
function ContextSwitcher() {
  const identity = useIdentity();
  const [anchor, setAnchor] = useState(null);
  const { t } = useTranslation('common');

  const available = identity?.context?.availableTenants ?? [];
  const active = identity?.context?.activeTenant ?? null;
  if (!identity?.isAuthenticated || available.length === 0) return null;

  const choose = async (tenantId) => {
    setAnchor(null);
    if (tenantId !== active?.id) await identity.selectTenant(tenantId);
  };

  return (
    <>
      {/* Outlined rather than plain, so the control reads as one. On the bar a `color="inherit"` button with no
          border is indistinguishable from the tenant name printed there, and the chevron alone was carrying the
          whole claim that this can be pressed. */}
      <Button
        variant="outlined"
        color="inherit"
        aria-label={t('navigation.changeOrganization')}
        aria-haspopup="menu"
        onClick={(event) => setAnchor(event.currentTarget)}
        endIcon={<ChevronDown size={18} strokeWidth={2} />}
        sx={switcherTrigger}
      >
        <Box component="span" sx={switcherLabel}>
          {active ? active.name : t('navigation.noOrganizationSelected')}
        </Box>
      </Button>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {available.map((tenant) => (
          <MenuItem key={tenant.id} selected={tenant.id === active?.id} onClick={() => choose(tenant.id)}>
            <ListItemText
              primary={tenant.name}
              secondary={tenant.type === 'Personal' ? t('navigation.personalAccount') : t('navigation.organization')}
            />
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}

/**
 * The two ways in, in the place every site puts them. They are the bar's only actions before there is a session,
 * and they are the reason the sidebar is not rendered alongside them: the product navigation names screens a
 * session opens, so standing it next to `Log in` offers a menu of things the visitor cannot do yet.
 *
 * `Register` is outlined and `Log in` is not, because one of the two has to read as the primary invitation and a
 * filled button in the theme's own primary would disappear into the bar it sits on.
 */
function VisitorActions() {
  const { t } = useTranslation('common');

  return (
    <Stack component="nav" aria-label={t('navigation.accountEntry')} direction="row" spacing={1}>
      <Button component={RouterLink} to="/login" color="inherit">{t('navigation.logIn')}</Button>
      <Button component={RouterLink} to="/register" color="inherit" variant="outlined">{t('navigation.register')}</Button>
    </Stack>
  );
}

/**
 * Two bars, chosen by whether there is a session.
 *
 * A visitor gets the standard one: the product's name on the left, the two ways in on the right, and no second
 * navigation anywhere on the page. A session gets the application shell — the same bar above the sidebar the
 * nineteen product screens are reached from.
 */
export function NavMenu() {
  const identity = useIdentity();
  const [open, setOpen] = useState(false);
  const { t } = useTranslation('common');

  const loading = identity?.isLoading !== false;
  const signedIn = identity?.isAuthenticated === true;
  const displayName = identity?.context?.user?.displayName ?? null;

  const contents = (
    <>
      <Toolbar sx={shellToolbarSx} />
      <NavContents onNavigate={() => setOpen(false)} />
    </>
  );

  return (
    <>
      <AppBar position="fixed" sx={(theme) => ({ zIndex: theme.zIndex.drawer + 1 })}>
        <Toolbar sx={toolbarSx}>
          {/* The hamburger belongs to the drawer, so it is offered only where there is one to open. */}
          {signedIn && (
            <IconButton
              color="inherit"
              edge="start"
              aria-label={t('navigation.openNavigation')}
              onClick={() => setOpen(true)}
              sx={hideAtWide}
            >
              <MenuIcon size={22} strokeWidth={2} />
            </IconButton>
          )}
          {signedIn
            ? <ContextSwitcher />
            : <Typography component={RouterLink} to="/" variant="h6" sx={brand}>{t('navigation.brand')}</Typography>}
          <Box sx={{ flexGrow: 1 }} />
          <LanguageSelector />
          {signedIn && displayName !== null && (
            <Typography variant="body2" sx={{ display: { xs: 'none', sm: 'block' } }}>{displayName}</Typography>
          )}
          {/* Held back until the session is known, so the bar does not offer `Log in` for a frame to somebody who
              is already signed in. */}
          {!signedIn && !loading && <VisitorActions />}
        </Toolbar>
      </AppBar>

      {signedIn && (
        <>
          <Drawer
            variant="permanent"
            component="nav"
            aria-label={t('navigation.primaryNavigation')}
            sx={(theme) => ({
              width: drawerWidth,
              flexShrink: 0,
              ...hideAtNarrow(theme),
              '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box' },
            })}
          >
            {contents}
          </Drawer>

          <Drawer
            variant="temporary"
            open={open}
            onClose={() => setOpen(false)}
            sx={(theme) => ({
              ...hideAtWide(theme),
              '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box' },
            })}
          >
            {contents}
          </Drawer>
        </>
      )}
    </>
  );
}
