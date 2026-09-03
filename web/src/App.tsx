import { useTranslation } from 'react-i18next';
import { Link, Navigate, Route, Routes } from 'react-router-dom';
import logo from './assets/logo.svg';
import { LanguageSwitcher } from './components/LanguageSwitcher';
import { SmtpWarningBanner } from './components/SmtpWarningBanner';
import { ThemeToggle } from './components/ThemeToggle';
import { useAuth } from './auth/AuthContext';
import { RequireAuth } from './auth/RequireAuth';
import { AdminPage } from './pages/AdminPage';
import { AgentDetailPage } from './pages/AgentDetailPage';
import { AgentsListPage } from './pages/AgentsListPage';
import { LoginPage } from './pages/LoginPage';

export default function App() {
  const { t } = useTranslation();
  const { username, logout } = useAuth();

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/*"
        element={
          <RequireAuth>
            <div className="app-shell">
              <header>
                <div className="app-shell-brand">
                  <img src={logo} alt="" width={28} height={28} />
                  <span>UpdateWatch2</span>
                </div>
                <nav>
                  <Link to="/agents">{t('nav.agents')}</Link>
                  <Link to="/admin">{t('nav.admin')}</Link>
                </nav>
                <div className="app-shell-controls">
                  <LanguageSwitcher />
                  <ThemeToggle />
                  {username && <span>{username}</span>}
                  <button type="button" onClick={() => void logout()}>
                    {t('nav.logout')}
                  </button>
                </div>
              </header>
              <SmtpWarningBanner />
              <Routes>
                <Route path="/" element={<Navigate to="/agents" replace />} />
                <Route path="/agents" element={<AgentsListPage />} />
                <Route path="/agents/:hostname" element={<AgentDetailPage />} />
                <Route path="/admin" element={<AdminPage />} />
              </Routes>
            </div>
          </RequireAuth>
        }
      />
    </Routes>
  );
}
