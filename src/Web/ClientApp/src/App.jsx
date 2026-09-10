import { Route, Routes } from 'react-router-dom';
import CssBaseline from '@mui/material/CssBaseline';
import { ThemeProvider as MaterialThemeProvider } from '@mui/material/styles';
import AppRoutes from './AppRoutes';
import { Layout } from './components/Layout';
import { IdentityProvider } from './features/identity/context/IdentityProvider';
import { appTheme } from './theme';

export default function App() {
  return (
    <MaterialThemeProvider theme={appTheme}>
      <CssBaseline enableColorScheme />
      <IdentityProvider>
        <Layout>
          <Routes>
            {AppRoutes.map((route, index) => {
              const { element, ...rest } = route;
              return <Route key={index} {...rest} element={element} />;
            })}
          </Routes>
        </Layout>
      </IdentityProvider>
    </MaterialThemeProvider>
  );
}
