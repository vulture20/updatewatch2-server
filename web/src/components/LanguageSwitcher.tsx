import { useTranslation } from 'react-i18next';

const LANGUAGES = ['de', 'en'] as const;

export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();

  return (
    <div className="seg" role="radiogroup" aria-label={t('nav.language')}>
      {LANGUAGES.map((lng) => (
        <label key={lng} className="seg-opt">
          <input
            type="radio"
            name="lang"
            checked={i18n.resolvedLanguage === lng}
            onChange={() => void i18n.changeLanguage(lng)}
          />
          {lng.toUpperCase()}
        </label>
      ))}
    </div>
  );
}
