import { Component } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { t } from '../i18n';

const alertSeverity = 'error';

/** Keeps a render failure on a neutral, styled screen without exposing the exception text. */
export class AppErrorBoundary extends Component {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    if (!this.state.failed) return this.props.children;

    return (
      <Stack spacing={2} sx={{ maxWidth: 560, mx: 'auto', my: 4 }}>
        <Alert severity={alertSeverity}>
          <Typography variant="body2">{t('errorBoundary.message')}</Typography>
        </Alert>
        <Button variant="contained" onClick={() => window.location.reload()} sx={{ alignSelf: 'flex-start' }}>
          {t('actions.reload')}
        </Button>
      </Stack>
    );
  }
}
