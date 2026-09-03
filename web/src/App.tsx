import { useTranslation } from 'react-i18next';
import { Link, Navigate, Route, Routes } from 'react-router-dom';
import { LanguageSwitcher } from './components/LanguageSwitcher';
import { ThemeToggle } from './components/ThemeToggle';
import { AdminPage } from './pages/AdminPage';
import { AgentDetailPage } from './pages/AgentDetailPage';
import { AgentsListPage } from './pages/AgentsListPage';
import { LoginPage } from './pages/LoginPage';

export default function App() {
  const { t } = useTranslation();

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/*"
        element={
          <div className="app-shell">
            <header>
              <nav>
                <Link to="/agents">{t('nav.agents')}</Link>
                <Link to="/admin">{t('nav.admin')}</Link>
              </nav>
              <div className="app-shell-controls">
                <LanguageSwitcher />
                <ThemeToggle />
              </div>
            </header>
            <Routes>
              <Route path="/" element={<Navigate to="/agents" replace />} />
              <Route path="/agents" element={<AgentsListPage />} />
              <Route path="/agents/:hostname" element={<AgentDetailPage />} />
              <Route path="/admin" element={<AdminPage />} />
            </Routes>
          </div>
        }
      />
    </Routes>
  );
}
