import { useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import { Menu as MenuIcon, ChevronDown } from 'lucide-react';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
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

export const drawerWidth = 264;

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
        <ListItemText primary={children} slotProps={{ primary: { variant: 'body2', component: 'span' } }} />
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
      <Section title="Your account">
        <NavItem to="/identity">Your access</NavItem>
        <NavItem to="/identity/profile">Your profile</NavItem>
        <NavItem to="/identity/account">Your account</NavItem>
        <NavItem to="/identity/sessions">Your devices</NavItem>
        <NavItem to="/identity/password">Your password</NavItem>
        <NavItem to="/identity/external">Sign-in providers</NavItem>
      </Section>

      <Divider />
      <Section title="Organizations">
        <NavItem to="/organizations/select">Organizations</NavItem>
        {permissions.includes('roles.read') && <NavItem to="/roles">Roles</NavItem>}
        {permissions.includes('members.read') && <NavItem to="/members">Members</NavItem>}
        {permissions.includes('members.invite') && <NavItem to="/members/invite">Invite a member</NavItem>}
      </Section>

      {/* Offered only to a session already operating as Platform. It is a convenience, not a control: the
          panel and the API both reauthorize regardless of what the navigation shows. */}
      {platform && (
        <>
          <Divider />
          <Section title="Platform">
            {permissions.includes('platform.organizations.read') && <NavItem to="/platform">Platform</NavItem>}
            {permissions.includes('platform.identities.read') && <NavItem to="/platform/identities">Platform identities</NavItem>}
            {permissions.includes('platform.retention.read') && <NavItem to="/platform/retention">Retention</NavItem>}
          </Section>
        </>
      )}

      <Divider />
      <Section title="Examples">
        <NavItem to="/">Home</NavItem>
        <NavItem to="/counter">Counter</NavItem>
      </Section>

      <Divider />
      <List dense>
        <ListItem disablePadding>
          <ListItemButton component="a" href="/login" onClick={handleSignOut}>
            <ListItemText primary="Log out" slotProps={{ primary: { variant: 'body2', component: 'span' } }} />
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
        aria-label="Change organization"
        aria-haspopup="menu"
        onClick={(event) => setAnchor(event.currentTarget)}
        endIcon={<ChevronDown size={18} strokeWidth={2} />}
        sx={switcherTrigger}
      >
        <Box component="span" sx={switcherLabel}>
          {active ? active.name : 'No organization selected'}
        </Box>
      </Button>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {available.map((tenant) => (
          <MenuItem key={tenant.id} selected={tenant.id === active?.id} onClick={() => choose(tenant.id)}>
            <ListItemText
              primary={tenant.name}
              secondary={tenant.type === 'Personal' ? 'Personal account' : 'Organization'}
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
  return (
    <Stack component="nav" aria-label="Account entry" direction="row" spacing={1}>
      <Button component={RouterLink} to="/login" color="inherit">Log in</Button>
      <Button component={RouterLink} to="/register" color="inherit" variant="outlined">Register</Button>
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

  const loading = identity?.isLoading !== false;
  const signedIn = identity?.isAuthenticated === true;
  const displayName = identity?.context?.user?.displayName ?? null;

  const contents = (
    <>
      <Toolbar />
      <NavContents onNavigate={() => setOpen(false)} />
    </>
  );

  return (
    <>
      <AppBar position="fixed" sx={(theme) => ({ zIndex: theme.zIndex.drawer + 1 })}>
        <Toolbar sx={{ gap: 1 }}>
          {/* The hamburger belongs to the drawer, so it is offered only where there is one to open. */}
          {signedIn && (
            <IconButton
              color="inherit"
              edge="start"
              aria-label="Open navigation"
              onClick={() => setOpen(true)}
              sx={hideAtWide}
            >
              <MenuIcon size={22} strokeWidth={2} />
            </IconButton>
          )}
          {signedIn
            ? <ContextSwitcher />
            : <Typography component={RouterLink} to="/" variant="h6" sx={brand}>Clean Architecture</Typography>}
          <Box sx={{ flexGrow: 1 }} />
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
            aria-label="Primary navigation"
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
