import { useTranslation } from 'react-i18next';

const LANGUAGES = ['en', 'de'] as const;

export function LanguageSwitcher() {
  const { i18n } = useTranslation();

  return (
    <select
      aria-label="Language"
      value={i18n.resolvedLanguage}
      onChange={(event) => void i18n.changeLanguage(event.target.value)}
    >
      {LANGUAGES.map((lng) => (
        <option key={lng} value={lng}>
          {lng.toUpperCase()}
        </option>
      ))}
    </select>
  );
}
