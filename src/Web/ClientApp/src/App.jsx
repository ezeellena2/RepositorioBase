import { Route, Routes, useLocation } from 'react-router-dom';
import CssBaseline from '@mui/material/CssBaseline';
import { ThemeProvider as MaterialThemeProvider } from '@mui/material/styles';
import AppRoutes from './AppRoutes';
import { AppErrorBoundary } from './components/AppErrorBoundary';
import { Layout } from './components/Layout';
import { IdentityProvider } from './features/identity/context/IdentityProvider';
import { themeFor } from './theme';
import { useTranslation } from './i18n';

export default function App() {
  const { i18n } = useTranslation();
  const location = useLocation();
  return (
    <MaterialThemeProvider theme={themeFor(i18n.resolvedLanguage)}>
      <CssBaseline enableColorScheme />
      <AppErrorBoundary>
        <IdentityProvider>
          <Layout>
            <AppErrorBoundary key={location.pathname}>
              <Routes>
                {AppRoutes.map((route, index) => {
                  const { element, ...rest } = route;
                  return <Route key={index} {...rest} element={element} />;
                })}
              </Routes>
            </AppErrorBoundary>
          </Layout>
        </IdentityProvider>
      </AppErrorBoundary>
    </MaterialThemeProvider>
  );
}
