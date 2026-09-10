import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import logo from '../assets/logo.svg';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';

export function LoginPage() {
  const { t } = useTranslation();
  const { status, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (status === 'authenticated') {
    const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? '/agents';
    return <Navigate to={from} replace />;
  }

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(username, password);
      navigate('/agents', { replace: true });
    } catch (err) {
      if (err instanceof ApiError && err.status === 423) {
        setError(t('login.lockedOut'));
      } else if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError(t('login.genericError'));
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main className="login-page" aria-label={t('login.title')}>
      <form className="login-form card elev-lg" onSubmit={(event) => void handleSubmit(event)}>
        <div className="login-logo-block">
          <img src={logo} alt="" width={46} height={46} />
          <h1>UpdateWatch2</h1>
          <p className="card-body">{t('login.subtitle')}</p>
        </div>
        {error && <div role="alert" className="login-error">{error}</div>}
        <label>
          {t('login.username')}
          <input
            type="text"
            name="username"
            autoComplete="username"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            required
          />
        </label>
        <label>
          {t('login.password')}
          <input
            type="password"
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </label>
        <button type="submit" className="btn-accent btn-block" disabled={submitting}>
          {t('login.submit')}
          <svg width="14" height="14" viewBox="0 0 256 256" fill="currentColor" aria-hidden="true">
            <path d="M221.66,133.66l-72,72a8,8,0,0,1-11.32-11.32L196.69,136H40a8,8,0,0,1,0-16H196.69L138.34,61.66a8,8,0,0,1,11.32-11.32l72,72A8,8,0,0,1,221.66,133.66Z"></path>
          </svg>
        </button>
      </form>
    </main>
  );
}
