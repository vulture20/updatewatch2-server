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
    <main className="login-page">
      <form className="login-form" onSubmit={(event) => void handleSubmit(event)}>
        <img src={logo} alt="UpdateWatch2" width={64} height={64} className="login-logo" />
        <h1>{t('login.title')}</h1>
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
        <button type="submit" disabled={submitting}>
          {t('login.submit')}
        </button>
      </form>
    </main>
  );
}
