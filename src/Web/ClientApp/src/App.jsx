import { Route, Routes } from 'react-router-dom';
import CssBaseline from '@mui/material/CssBaseline';
import { ThemeProvider as MaterialThemeProvider } from '@mui/material/styles';
import AppRoutes from './AppRoutes';
import { Layout } from './components/Layout';
import { IdentityProvider } from './features/identity/context/IdentityProvider';
import { themeFor } from './theme';
import { useTranslation } from './i18n';

export default function App() {
  const { i18n } = useTranslation();
  return (
    <MaterialThemeProvider theme={themeFor(i18n.resolvedLanguage)}>
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
