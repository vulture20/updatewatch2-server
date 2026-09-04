import { useTranslation } from 'react-i18next';
import { useTheme } from '../theme/ThemeProvider';

export function ThemeToggle() {
  const { theme, setTheme } = useTheme();
  const { t } = useTranslation();

  return (
    <button type="button" className="btn-ghost" onClick={() => setTheme(theme === 'light' ? 'dark' : 'light')}>
      {theme === 'light' ? t('theme.dark') : t('theme.light')}
    </button>
  );
}
