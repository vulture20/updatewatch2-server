import { useTranslation } from 'react-i18next';
import { SmtpWarningBanner } from '../components/SmtpWarningBanner';

/**
 * No login backend exists yet (see updatewatch2-server#2 — local admin
 * account + AD integration). This is a static form, not wired to submit
 * anywhere.
 */
export function LoginPage() {
  const { t } = useTranslation();

  return (
    <main className="login-page">
      <SmtpWarningBanner />
      <form className="login-form" onSubmit={(event) => event.preventDefault()}>
        <h1>{t('login.title')}</h1>
        <label>
          {t('login.username')}
          <input type="text" name="username" autoComplete="username" />
        </label>
        <label>
          {t('login.password')}
          <input type="password" name="password" autoComplete="current-password" />
        </label>
        <button type="submit">{t('login.submit')}</button>
      </form>
    </main>
  );
}
