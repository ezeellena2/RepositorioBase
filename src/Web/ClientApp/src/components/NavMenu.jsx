import { useId, useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import {
  Archive,
  BadgeCheck,
  Building2,
  ChevronDown,
  ChevronUp,
  CircleUserRound,
  ContactRound,
  House,
  KeyRound,
  Link as LinkIcon,
  LogOut,
  Menu as MenuIcon,
  MonitorSmartphone,
  PanelLeftClose,
  PanelLeftOpen,
  PanelsTopLeft,
  ShieldCheck,
  UserPlus,
  UserRound,
  UsersRound,
} from 'lucide-react';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Collapse from '@mui/material/Collapse';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import NativeSelect from '@mui/material/NativeSelect';
import IconButton from '@mui/material/IconButton';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Popper from '@mui/material/Popper';
import Stack from '@mui/material/Stack';
import Toolbar from '@mui/material/Toolbar';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../features/identity/context/IdentityProvider';
import { ProblemMessage } from '../features/identity/ProblemMessage';
import { useSubmit } from '../features/identity/useSubmit';
import {
  isPseudoLanguageOverrideActive,
  sourceLanguage,
  supportedLanguages,
  useTranslation,
} from '../i18n';

export const drawerWidth = 264;
/** Two rows keep localized controls visible on phones; every fixed-bar spacer uses the same height. */
export const shellToolbarSx = { minHeight: { xs: 112, sm: 64 } };
const itemLabelSlotProps = { primary: { variant: 'body2', component: 'span', noWrap: true } };
const sectionLabelSlotProps = { primary: { variant: 'overline', component: 'span', noWrap: true } };
const toolbarSx = {
  ...shellToolbarSx,
  gap: 1,
  px: { xs: 2, sm: 3 },
  flexWrap: { xs: 'wrap', sm: 'nowrap' },
  py: { xs: 1, sm: 0 },
};
const collapsedDrawerWidth = 72;
const mobileDrawerWidth = 320;

const accountRoutes = new Set([
  '/identity',
  '/identity/profile',
  '/identity/account',
  '/identity/sessions',
  '/identity/password',
  '/identity/external',
]);
const organizationAdministrationRoutes = new Set(['/roles', '/members', '/members/invite']);
const platformOperationsRoutes = new Set(['/platform', '/platform/identities', '/platform/retention']);

/**
 * The sidebar is the product's navigation, so it keeps a permanent rail on a wide screen even when its links are
 * collapsed. Below `md` it becomes the temporary drawer the toggle opens.
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
const languageInputProps = { id: 'shell-language', name: 'language' };
const languageControlSize = 'small';

function LanguageSelector() {
  const identity = useIdentity();
  const { t, i18n } = useTranslation('common');
  const pseudoLanguageOverride = isPseudoLanguageOverrideActive();
  const displayedLanguage = pseudoLanguageOverride
    ? sourceLanguage
    : identity.pendingLanguage ?? i18n.resolvedLanguage;
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
          disabled={identity.isLoading || pseudoLanguageOverride}
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


const switcherLabel = { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' };

const brand = { color: 'inherit', textDecoration: 'none', whiteSpace: 'nowrap' };
const navigationButtonLayout = (collapsible, expanded) => ({
  mx: 1,
  width: 'auto',
  px: collapsible ? { md: 1, lg: expanded ? 1.5 : 1 } : 1.5,
  columnGap: collapsible ? { md: 0, lg: expanded ? 1.5 : 0 } : 1.5,
  justifyContent: collapsible ? { md: 'center', lg: expanded ? 'flex-start' : 'center' } : 'flex-start',
});
const navigationLabelLayout = (collapsible, expanded) => (
  collapsible ? { display: { md: 'none', lg: expanded ? 'block' : 'none' } } : undefined
);
const disclosureIconLayout = (collapsible, expanded) => (
  collapsible ? { display: { md: 'none', lg: expanded ? 'block' : 'none' }, flexShrink: 0 } : { flexShrink: 0 }
);

/**
 * A link says where it goes. `selected` is the only thing that says where you already are, and without it a
 * column of identical rows is a list of addresses rather than a place. It is presentation only — the row
 * stays a link with the same accessible name every caller looks it up by — so `aria-current` carries the same
 * fact to somebody who cannot see which row is filled.
 */
function NavItem({ to, label, icon: Icon, onNavigate, collapsible = false, expanded = true }) {
  const { pathname } = useLocation();
  const current = pathname === to;

  return (
    <ListItem disablePadding>
      <Tooltip title={label} placement="right" describeChild>
        <ListItemButton
          component={RouterLink}
          to={to}
          selected={current}
          aria-label={label}
          aria-current={current ? 'page' : undefined}
          onClick={onNavigate}
          sx={navigationButtonLayout(collapsible, expanded)}
        >
          <ListItemIcon sx={{ minWidth: 0, justifyContent: 'center', color: 'inherit' }}>
            <Icon aria-hidden="true" size={20} strokeWidth={2} />
          </ListItemIcon>
          <ListItemText
            primary={label}
            sx={navigationLabelLayout(collapsible, expanded)}
            slotProps={itemLabelSlotProps}
          />
        </ListItemButton>
      </Tooltip>
    </ListItem>
  );
}

function ExpandableGroup({ label, routes, icon: Icon, children, collapsible = false, navigationExpanded = true }) {
  const { key: locationKey, pathname } = useLocation();
  const panelId = useId();
  const [disclosure, setDisclosure] = useState({ expanded: true, collapsedLocationKey: null });
  const containsCurrentPage = routes.has(pathname);
  const routeForcesOpen = containsCurrentPage && disclosure.collapsedLocationKey !== locationKey;
  const expanded = disclosure.expanded || routeForcesOpen;

  const toggle = () => {
    setDisclosure((current) => {
      const currentlyExpanded = current.expanded
        || (containsCurrentPage && current.collapsedLocationKey !== locationKey);

      return currentlyExpanded
        ? { expanded: false, collapsedLocationKey: locationKey }
        : { expanded: true, collapsedLocationKey: null };
    });
  };

  return (
    <ListItem disablePadding sx={{ display: 'block' }}>
      <Tooltip title={label} placement="right" describeChild>
        <ListItemButton
          aria-label={label}
          aria-controls={panelId}
          aria-expanded={expanded}
          onClick={toggle}
          sx={navigationButtonLayout(collapsible, navigationExpanded)}
        >
          <ListItemIcon sx={{ minWidth: 0, justifyContent: 'center', color: 'inherit' }}>
            <Icon aria-hidden="true" size={20} strokeWidth={2} />
          </ListItemIcon>
          <ListItemText
            primary={label}
            sx={navigationLabelLayout(collapsible, navigationExpanded)}
            slotProps={sectionLabelSlotProps}
          />
          {expanded
            ? (
              <Box
                component={ChevronUp}
                aria-hidden="true"
                size={18}
                strokeWidth={2}
                sx={disclosureIconLayout(collapsible, navigationExpanded)}
              />
            )
            : (
              <Box
                component={ChevronDown}
                aria-hidden="true"
                size={18}
                strokeWidth={2}
                sx={disclosureIconLayout(collapsible, navigationExpanded)}
              />
            )}
        </ListItemButton>
      </Tooltip>
      <Collapse id={panelId} in={expanded} timeout="auto">
        <List
          dense
          disablePadding
          sx={collapsible ? { pl: { md: 0, lg: navigationExpanded ? 2 : 0 } } : { pl: 2 }}
        >
          {children}
        </List>
      </Collapse>
    </ListItem>
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
function NavContents({ onNavigate, collapsible = false, expanded = true }) {
  const identity = useIdentity();
  const navigate = useNavigate();
  const { t } = useTranslation('common');
  const [signOutTrigger, setSignOutTrigger] = useState(null);
  const { submit: submitSignOut, problem: signOutProblem, isBusy: isSigningOut } = useSubmit(() => identity.signOut());
  const permissions = identity.context?.permissions ?? [];
  const canReadRoles = permissions.includes('roles.read');
  const canReadMembers = permissions.includes('members.read');
  const canInviteMembers = permissions.includes('members.invite');
  const canReadPlatformOrganizations = permissions.includes('platform.organizations.read');
  const canReadPlatformIdentities = permissions.includes('platform.identities.read');
  const canReadPlatformRetention = permissions.includes('platform.retention.read');
  const organizationAdministration = canReadRoles || canReadMembers || canInviteMembers;
  const platformOperations = canReadPlatformOrganizations || canReadPlatformIdentities || canReadPlatformRetention;

  const handleSignOut = async (event) => {
    event.preventDefault();
    setSignOutTrigger(event.currentTarget);
    const signedOut = await submitSignOut();
    if (signedOut !== true) return;
    setSignOutTrigger(null);
    onNavigate();
    navigate('/login');
  };

  return (
    <>
      <List dense sx={{ py: 1 }}>
        <NavItem to="/" label={t('navigation.home')} icon={House} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
        <NavItem
          to="/organizations/select"
          label={t('navigation.organizations')}
          icon={Building2}
          onNavigate={onNavigate}
          collapsible={collapsible}
          expanded={expanded}
        />
        <NavItem
          to="/organizations/register"
          label={t('navigation.addOrganization')}
          icon={Building2}
          onNavigate={onNavigate}
          collapsible={collapsible}
          expanded={expanded}
        />

      <Divider component="li" />
      <ExpandableGroup
        label={t('navigation.accountSection')}
        routes={accountRoutes}
        icon={CircleUserRound}
        collapsible={collapsible}
        navigationExpanded={expanded}
      >
        <NavItem to="/identity" label={t('navigation.yourAccess')} icon={BadgeCheck} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
        <NavItem to="/identity/profile" label={t('navigation.yourProfile')} icon={UserRound} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
        <NavItem to="/identity/account" label={t('navigation.accountSection')} icon={CircleUserRound} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
        <NavItem to="/identity/sessions" label={t('navigation.yourDevices')} icon={MonitorSmartphone} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
        <NavItem to="/identity/password" label={t('navigation.yourPassword')} icon={KeyRound} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
        <NavItem to="/identity/external" label={t('navigation.signInProviders')} icon={LinkIcon} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
      </ExpandableGroup>

      {organizationAdministration && (
        <>
          <Divider component="li" />
          <ExpandableGroup
            label={t('navigation.organizations')}
            routes={organizationAdministrationRoutes}
            icon={Building2}
            collapsible={collapsible}
            navigationExpanded={expanded}
          >
            {canReadRoles && <NavItem to="/roles" label={t('navigation.roles')} icon={ShieldCheck} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />}
            {canReadMembers && <NavItem to="/members" label={t('navigation.members')} icon={UsersRound} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />}
            {canInviteMembers && <NavItem to="/members/invite" label={t('navigation.inviteMember')} icon={UserPlus} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />}
          </ExpandableGroup>
        </>
      )}

      {/* Offered only to a session already operating as Platform. It is a convenience, not a control: the
          panel and the API both reauthorize regardless of what the navigation shows. */}
      {platformOperations && (
        <>
          <Divider component="li" />
          <ExpandableGroup
            label={t('navigation.platform')}
            routes={platformOperationsRoutes}
            icon={PanelsTopLeft}
            collapsible={collapsible}
            navigationExpanded={expanded}
          >
            {canReadPlatformOrganizations && <NavItem to="/platform" label={t('navigation.platform')} icon={PanelsTopLeft} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />}
            {canReadPlatformIdentities && <NavItem to="/platform/identities" label={t('navigation.platformIdentities')} icon={ContactRound} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />}
            {canReadPlatformRetention && <NavItem to="/platform/retention" label={t('navigation.retention')} icon={Archive} onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />}
          </ExpandableGroup>
        </>
      )}

        <Divider component="li" />
        <ListItem disablePadding>
          <Tooltip title={t('navigation.logOut')} placement="right" describeChild>
            <ListItemButton
              component="a"
              href="/login"
              aria-label={t('navigation.logOut')}
              disabled={isSigningOut}
              onClick={handleSignOut}
              sx={navigationButtonLayout(collapsible, expanded)}
            >
              <ListItemIcon sx={{ minWidth: 0, justifyContent: 'center', color: 'inherit' }}>
                <LogOut aria-hidden="true" size={20} strokeWidth={2} />
              </ListItemIcon>
              <ListItemText
                primary={t('navigation.logOut')}
                sx={navigationLabelLayout(collapsible, expanded)}
                slotProps={itemLabelSlotProps}
              />
            </ListItemButton>
          </Tooltip>
        </ListItem>
      </List>
      {signOutProblem && (
        <Popper
          open
          anchorEl={signOutTrigger}
          placement="right-end"
          sx={(theme) => ({ zIndex: theme.zIndex.snackbar })}
        >
          <ProblemMessage problem={signOutProblem} />
        </Popper>
      )}
    </>
  );
}

function DrawerContents({
  collapsible = false,
  expanded = true,
  onNavigate,
  onToggle,
  activeOrganization = null,
  displayName = null,
}) {
  const contentId = useId();
  const { t } = useTranslation('common');

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar
        sx={{
          ...shellToolbarSx,
          px: collapsible ? { md: 1, lg: expanded ? 2 : 1 } : 2,
          justifyContent: collapsible ? { md: 'center', lg: expanded ? 'flex-start' : 'center' } : 'flex-start',
        }}
      >
        <Typography
          component="div"
          variant="h6"
          noWrap
          sx={navigationLabelLayout(collapsible, expanded)}
        >
          {t('navigation.brand')}
        </Typography>
      </Toolbar>
      {(activeOrganization !== null || displayName !== null) && (
        <Stack spacing={0.5} sx={{ px: 2, pb: 1.5, overflowWrap: 'anywhere' }}>
          {activeOrganization !== null && (
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{activeOrganization}</Typography>
          )}
          {displayName !== null && <Typography variant="caption">{displayName}</Typography>}
        </Stack>
      )}
      {collapsible && (
        <Box
          sx={{
            display: { md: 'none', lg: 'flex' },
            justifyContent: expanded ? 'flex-end' : 'center',
            px: 1,
            py: 0.5,
          }}
        >
          <IconButton
            aria-label={expanded ? t('navigation.collapseNavigation') : t('navigation.expandNavigation')}
            aria-controls={contentId}
            aria-expanded={expanded}
            onClick={onToggle}
          >
            {expanded
              ? <PanelLeftClose aria-hidden="true" size={20} strokeWidth={2} />
              : <PanelLeftOpen aria-hidden="true" size={20} strokeWidth={2} />}
          </IconButton>
        </Box>
      )}
      <Box id={contentId} sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
        <NavContents onNavigate={onNavigate} collapsible={collapsible} expanded={expanded} />
      </Box>
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
  const { t } = useTranslation('common');
  const [anchor, setAnchor] = useState(null);
  const [trigger, setTrigger] = useState(null);
  const { submit, problem, isBusy } = useSubmit((tenantId) => identity.selectTenant(tenantId));

  const available = identity?.context?.availableTenants ?? [];
  const active = identity?.context?.activeTenant ?? null;
  if (!identity?.isAuthenticated || available.length === 0) return null;

  const choose = (tenantId) => {
    setAnchor(null);
    if (tenantId !== active?.id) submit(tenantId);
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
        disabled={isBusy}
        onClick={(event) => {
          setTrigger(event.currentTarget);
          setAnchor(event.currentTarget);
        }}
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
      {problem && (
        <Popper
          open
          anchorEl={trigger}
          placement="bottom-start"
          sx={(theme) => ({ zIndex: theme.zIndex.snackbar })}
        >
          <ProblemMessage problem={problem} />
        </Popper>
      )}
    </>
  );
}

/**
 * The two ways in, in the place every site puts them. They are the bar's only actions before there is a session,
 * and they are the reason the sidebar is not rendered alongside them: the product navigation names screens a
 * session opens, so standing it next to `Log in` offers a menu of things the visitor cannot do yet.
 *
 * `Register` is the filled one because it is the invitation a front door exists to make; `Log in` is for somebody
 * who already accepted it. The bar is white, so the theme's primary reads on it at full strength — on the old teal
 * bar a filled primary button disappeared into the bar, which is why this used to be outlined.
 */
function VisitorActions() {
  const { t } = useTranslation('common');

  return (
    <Stack component="nav" aria-label={t('navigation.accountEntry')} direction="row" spacing={1}>
      <Button component={RouterLink} to="/login" color="inherit">{t('navigation.logIn')}</Button>
      <Button component={RouterLink} to="/register" variant="contained">{t('navigation.register')}</Button>
    </Stack>
  );
}

/**
 * Two bars, chosen by whether there is a session.
 *
 * A visitor gets the standard one: the product's name on the left, the two ways in on the right, and no second
 * navigation anywhere on the page. A session gets the application shell — the same bar above the sidebar its
 * product screens are reached from.
 */
export function NavMenu() {
  const identity = useIdentity();
  const { t } = useTranslation('common');
  const [mobileOpen, setMobileOpen] = useState(false);
  const [desktopExpanded, setDesktopExpanded] = useState(true);

  const loading = identity?.isLoading !== false;
  const signedIn = identity?.isAuthenticated === true;
  const activeOrganization = identity?.context?.activeTenant?.name ?? null;
  const displayName = identity?.context?.user?.displayName ?? null;
  const permanentWidth = desktopExpanded ? drawerWidth : collapsedDrawerWidth;

  return (
    <>
      <AppBar
        position="fixed"
        sx={(theme) => ({
          zIndex: signedIn ? theme.zIndex.appBar : theme.zIndex.drawer + 1,
          ...(signedIn && {
            [theme.breakpoints.up('md')]: {
              width: `calc(100% - ${collapsedDrawerWidth}px)`,
              ml: `${collapsedDrawerWidth}px`,
            },
            [theme.breakpoints.up('lg')]: {
              width: `calc(100% - ${permanentWidth}px)`,
              ml: `${permanentWidth}px`,
            },
          }),
        })}
      >
        <Toolbar sx={toolbarSx}>
          {/* The hamburger belongs to the drawer, so it is offered only where there is one to open. */}
          {signedIn && (
            <IconButton
              color="inherit"
              edge="start"
              aria-label={t('navigation.openNavigation')}
              onClick={() => setMobileOpen(true)}
              sx={hideAtWide}
            >
              <MenuIcon aria-hidden="true" size={22} strokeWidth={2} />
            </IconButton>
          )}
          {signedIn
            ? <ContextSwitcher />
            : <Typography component={RouterLink} to="/" variant="h6" sx={brand}>{t('navigation.brand')}</Typography>}
          <Box sx={{ flexGrow: 1 }} />
          <LanguageSelector />
          {signedIn && displayName !== null && (
            <Typography
              variant="body2"
              sx={{ display: { xs: 'none', sm: 'block' }, color: 'text.secondary', fontWeight: 600 }}
            >
              {displayName}
            </Typography>
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
              width: { md: collapsedDrawerWidth, lg: permanentWidth },
              flexShrink: 0,
              ...hideAtNarrow(theme),
              '& .MuiDrawer-paper': {
                width: { md: collapsedDrawerWidth, lg: permanentWidth },
                boxSizing: 'border-box',
              },
            })}
          >
            <DrawerContents
              collapsible
              expanded={desktopExpanded}
              onNavigate={() => setMobileOpen(false)}
              onToggle={() => setDesktopExpanded((current) => !current)}
            />
          </Drawer>

          <Drawer
            variant="temporary"
            component="nav"
            aria-label={t('navigation.primaryNavigation')}
            open={mobileOpen}
            onClose={() => setMobileOpen(false)}
            sx={(theme) => ({
              ...hideAtWide(theme),
              '& .MuiDrawer-paper': {
                width: mobileDrawerWidth,
                maxWidth: 'calc(100vw - 24px)',
                boxSizing: 'border-box',
              },
            })}
          >
            <DrawerContents
              activeOrganization={activeOrganization}
              displayName={displayName}
              onNavigate={() => setMobileOpen(false)}
            />
          </Drawer>
        </>
      )}
    </>
  );
}
