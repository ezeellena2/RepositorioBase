import Typography from '@mui/material/Typography';
import { useTranslation } from '../../i18n';

const primaryProps = { component: 'span', variant: 'body2', sx: { display: 'block' } };
const secondaryProps = { component: 'span', variant: 'caption', color: 'text.secondary', sx: { display: 'block' } };

/** Readable system name first; the exact authorization code remains visible for operators. */
export function PermissionLabel({ code, primaryId }) {
  const { t } = useTranslation('enums');
  return (
    <>
      <Typography {...primaryProps} id={primaryId}>{t(`permissions.${code}`)}</Typography>
      <Typography {...secondaryProps}>{code}</Typography>
    </>
  );
}
