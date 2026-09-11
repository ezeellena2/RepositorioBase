import { useMemo } from 'react';
import { useTranslation } from './index';

/** Language controls formatting; omitting timeZone preserves the browser's zone. */
export function useFormat() {
  const { i18n, t } = useTranslation('common');
  const locale = i18n.resolvedLanguage;
  return useMemo(() => {
    const dates = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' });
    const numbers = new Intl.NumberFormat(locale);
    return {
      formatDate: (value) => {
        const isDateInput = value instanceof Date || typeof value === 'number'
          || (typeof value === 'string' && value.trim() !== '');
        if (!isDateInput) return t('format.dateUnavailable');
        const date = new Date(value);
        return Number.isFinite(date.getTime()) ? dates.format(date) : t('format.dateUnavailable');
      },
      formatNumber: (value) => numbers.format(value),
    };
  }, [locale, t]);
}
