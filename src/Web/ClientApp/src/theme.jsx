import { createTheme } from '@mui/material/styles';

// Quoted, because CSS will not take these unquoted. An unquoted family name is a sequence of identifiers and
// `3` is not one — it starts with a digit — so `Source Sans 3, sans-serif` is a parse error the browser drops
// whole, taking the fallback with it and leaving the body in the default serif.
const displayFont = '"Manrope", sans-serif';
const textFont = '"Source Sans 3", sans-serif';

/**
 * One scheme, so there is one appearance.
 *
 * A dark scheme is not only the toggle that used to choose it: while the theme declares one, Material UI resolves
 * the mode from `prefers-color-scheme` and the application turns dark for anybody whose operating system is,
 * whether or not there is a control on screen. Declaring light alone is what actually removes dark mode — there is
 * no second scheme left to switch to.
 */
export const appTheme = createTheme({
  colorSchemes: {
    light: {
      palette: {
        primary: { main: '#0E5C66' },
        error: { main: '#B3412A' },
        success: { main: '#1F6B40' },
      },
    },
  },
  components: {
    // Material's uppercasing is a label style, and here the label is data: the organization chooser puts a
    // tenant's own name on its buttons, and a caller that reads the rendered text back gets `ACME-7F3` where the
    // application holds `acme-7f3`. Casing that changes what a name reads as is not presentation any more, so it
    // is turned off once, centrally, for every button. Nothing else about Button moves: its shape, elevation,
    // states, focus ring and ripple stay exactly as Material UI ships them.
    MuiButton: { styleOverrides: { root: { textTransform: 'none' } } },
  },
  typography: {
    fontFamily: textFont,
    h1: { fontFamily: displayFont },
    h2: { fontFamily: displayFont },
    h3: { fontFamily: displayFont },
    h4: { fontFamily: displayFont },
    h5: { fontFamily: displayFont },
    h6: { fontFamily: displayFont },
  },
});
