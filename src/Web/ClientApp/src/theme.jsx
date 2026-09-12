import { createTheme } from '@mui/material/styles';
import { enUS, esES } from '@mui/material/locale';

// Quoted, because CSS will not take these unquoted. An unquoted family name is a sequence of identifiers and
// `3` is not one — it starts with a digit — so `Source Sans 3, sans-serif` is a parse error the browser drops
// whole, taking the fallback with it and leaving the body in the default serif.
const displayFont = '"Manrope", sans-serif';
const textFont = '"Source Sans 3", sans-serif';

// Direction C, chosen by the product owner on 2026-09-10 from three directions shown against the application's
// own header and sign-in card. It is a decision about surfaces and density as well as colour — a dark navigation
// plane, cool grey canvas, white outlined surfaces, and compact controls — so the shared policy reaches past the
// palette into the component defaults below.
const ink = '#1A1F36';
const secondaryInk = '#475467';
const hairline = '#D0D5DD';
const primary = '#4F46E5';
const focus = '#4338CA';
const navigation = '#111A33';
const navigationSelected = '#26346D';
const navigationMuted = '#B7C1DA';
const heading = { fontFamily: displayFont, fontWeight: 700, letterSpacing: '-0.02em' };
const focusRing = {
  outline: `3px solid ${focus}`,
  outlineOffset: 2,
};

/**
 * One scheme, so there is one appearance.
 *
 * A dark scheme is not only the toggle that used to choose it: while the theme declares one, Material UI resolves
 * the mode from `prefers-color-scheme` and the application turns dark for anybody whose operating system is,
 * whether or not there is a control on screen. Declaring light alone is what actually removes dark mode — there is
 * no second scheme left to switch to.
 */
const themeOptions = {
  colorSchemes: {
    light: {
      palette: {
        primary: { main: primary, dark: focus },
        // These two values remain the established application contract. Their proposed replacements require a
        // separate contract decision, so the visual refresh does not disguise alternates in component overrides.
        error: { main: '#B3412A', light: '#FDECEA' },
        success: { main: '#1F6B40', light: '#EAF7EF' },
        warning: { main: '#945900', light: '#FFF3D6' },
        info: { main: '#175CD3', light: '#EAF2FF' },
        text: { primary: ink, secondary: secondaryInk },
        background: { default: '#F5F7FB', paper: '#FFFFFF' },
        divider: hairline,
        navigation: {
          main: navigation,
          selected: navigationSelected,
          muted: navigationMuted,
        },
      },
    },
  },
  spacing: 8,
  shape: { borderRadius: 8 },
  components: {
    MuiCssBaseline: {
      styleOverrides: { 'a:focus-visible': focusRing },
    },
    // Material's uppercasing is a label style, and here the label is data: the organization chooser puts a
    // tenant's own name on its buttons, and a caller that reads the rendered text back gets `ACME-7F3` where the
    // application holds `acme-7f3`. Casing that changes what a name reads as is not presentation any more, so it
    // is turned off once, centrally, for every button.
    //
    // Controls stay compact and rectangular; depth belongs to the few surfaces that are intentionally raised.
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: {
          minHeight: 40,
          borderRadius: 8,
          paddingInline: 16,
          textTransform: 'none',
          '&.Mui-focusVisible': focusRing,
        },
        sizeLarge: { minHeight: 44 },
      },
    },
    MuiIconButton: {
      styleOverrides: {
        root: {
          width: 40,
          height: 40,
          borderRadius: 8,
          '&.Mui-focusVisible': focusRing,
        },
      },
    },
    MuiLink: {
      styleOverrides: {
        root: {
          borderRadius: 4,
          '&:focus-visible': focusRing,
        },
      },
    },
    MuiTextField: {
      defaultProps: { size: 'small' },
    },
    MuiFormControl: {
      defaultProps: { size: 'small' },
    },
    MuiOutlinedInput: {
      styleOverrides: { root: { minHeight: 40, borderRadius: 8 } },
    },
    MuiCheckbox: {
      styleOverrides: { root: { '&.Mui-focusVisible': focusRing } },
    },
    MuiRadio: {
      styleOverrides: { root: { '&.Mui-focusVisible': focusRing } },
    },
    // The bar is a white surface with a hairline under it, not a slab of the primary colour. `color: 'inherit'`
    // stops Material UI painting it primary; the background is set explicitly because `inherit` does not give a
    // fixed bar a background of its own, and a transparent fixed bar shows the page scrolling underneath it.
    MuiAppBar: {
      defaultProps: { elevation: 0, color: 'inherit' },
      styleOverrides: { root: { backgroundColor: '#FFFFFF', color: ink, borderBottom: `1px solid ${hairline}` } },
    },
    MuiToolbar: {
      styleOverrides: {
        root: {
          minHeight: 56,
          '@media (min-width:0px) and (orientation: landscape)': { minHeight: 56 },
          '@media (min-width:600px)': { minHeight: 64 },
        },
      },
    },
    MuiDrawer: {
      styleOverrides: {
        paper: {
          overflowX: 'hidden',
          backgroundColor: navigation,
          color: '#FFFFFF',
          borderRight: 0,
          '& .MuiDivider-root': { borderColor: 'rgba(183, 193, 218, 0.24)' },
          '& .MuiIconButton-root': { color: navigationMuted },
          '& .MuiIconButton-root.Mui-focusVisible, & .MuiListItemButton-root.Mui-focusVisible': {
            outline: `3px solid ${navigationMuted}`,
            outlineOffset: 2,
          },
          '& .MuiListItemButton-root': {
            position: 'relative',
            color: navigationMuted,
            '&:hover': { backgroundColor: 'rgba(255, 255, 255, 0.08)', color: '#FFFFFF' },
            '&.Mui-selected': {
              backgroundColor: navigationSelected,
              color: '#FFFFFF',
              boxShadow: `inset 0 0 0 1px ${navigationMuted}`,
              '&::before': {
                content: '""',
                position: 'absolute',
                insetBlock: 8,
                insetInlineStart: 0,
                width: 3,
                borderRadius: '0 2px 2px 0',
                backgroundColor: primary,
              },
            },
            '&.Mui-selected:hover': { backgroundColor: navigationSelected },
          },
        },
      },
    },
    MuiPaper: {
      styleOverrides: {
        // Cards are rounder than the controls inside them, so a field never reads as the same shape as its card.
        rounded: { borderRadius: 12 },
        // The public entrance cards — sign in, register, recovery — are `elevation={3}` in every page that draws
        // one. Softened here, once, rather than in each page: a hairline for the edge and a long, faint shadow for
        // the lift, where Material's stock elevation reads as a card from 2016.
        elevation3: {
          border: `1px solid ${hairline}`,
          boxShadow: '0 12px 32px -12px rgba(17, 26, 51, 0.22)',
        },
        outlined: { borderColor: hairline },
      },
    },
    MuiPopover: {
      styleOverrides: {
        paper: {
          border: `1px solid ${hairline}`,
          borderRadius: 12,
          boxShadow: '0 12px 32px -12px rgba(17, 26, 51, 0.22)',
        },
      },
    },
    MuiDialog: {
      styleOverrides: {
        paper: {
          border: `1px solid ${hairline}`,
          borderRadius: 14,
          boxShadow: '0 24px 64px -20px rgba(17, 26, 51, 0.34)',
        },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: { height: 24 },
        label: { paddingInline: 8 },
      },
    },
    MuiTable: {
      defaultProps: { size: 'small' },
    },
    MuiTableRow: {
      styleOverrides: { root: { height: 44 } },
    },
    MuiTableCell: {
      styleOverrides: {
        root: {
          height: 44,
          padding: '6px 12px',
          fontSize: 13,
          lineHeight: '18px',
        },
        head: { fontWeight: 600, color: ink },
      },
    },
    MuiListItemButton: {
      styleOverrides: {
        root: {
          minHeight: 40,
          borderRadius: 8,
          '&.Mui-focusVisible': focusRing,
        },
      },
    },
    MuiMenuItem: {
      styleOverrides: {
        root: {
          minHeight: 40,
          '&.Mui-focusVisible': focusRing,
        },
      },
    },
    MuiListSubheader: {
      styleOverrides: {
        root: {
          fontSize: 11,
          fontWeight: 600,
          lineHeight: '16px',
          letterSpacing: '0.06em',
          textTransform: 'uppercase',
        },
      },
    },
    MuiAlert: {
      styleOverrides: { root: { borderRadius: 12 } },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: {
          backgroundColor: navigation,
          fontSize: 12,
          lineHeight: '16px',
        },
      },
    },
  },
  typography: {
    fontFamily: textFont,
    fontSize: 14,
    h1: { ...heading, fontSize: 40, lineHeight: '48px' },
    h2: { ...heading, fontSize: 34, lineHeight: '42px' },
    h3: { ...heading, fontSize: 30, lineHeight: '38px' },
    h4: { ...heading, fontSize: 26, lineHeight: '34px' },
    h5: {
      ...heading,
      fontSize: 24,
      lineHeight: '32px',
      '@media (min-width:900px)': { fontSize: 28, lineHeight: '36px' },
    },
    h6: { ...heading, fontSize: 18, lineHeight: '26px' },
    subtitle1: { fontSize: 15, fontWeight: 600, lineHeight: '22px' },
    body1: { fontSize: 14, lineHeight: '20px' },
    body2: { fontSize: 14, lineHeight: '20px' },
    button: { fontSize: 14, fontWeight: 600, lineHeight: '20px' },
    caption: { fontSize: 12, lineHeight: '16px' },
    overline: { fontSize: 11, fontWeight: 600, lineHeight: '16px', letterSpacing: '0.06em' },
  },
};

export const appTheme = createTheme(themeOptions);
const localizedThemes = {
  en: createTheme(themeOptions, enUS),
  es: createTheme(themeOptions, esES),
};
export const themeFor = (language) => localizedThemes[language] ?? localizedThemes.en;
