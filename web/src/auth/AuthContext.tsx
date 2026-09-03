import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { authApi } from '../api/endpoints';
import { setUnauthorizedHandler } from '../api/client';

type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

interface AuthContextValue {
  status: AuthStatus;
  username: string | null;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [username, setUsername] = useState<string | null>(null);

  useEffect(() => {
    authApi
      .me()
      .then((me) => {
        setStatus(me.authenticated ? 'authenticated' : 'anonymous');
        setUsername(me.username);
      })
      .catch(() => {
        setStatus('anonymous');
        setUsername(null);
      });
  }, []);

  useEffect(() => {
    // Fires on a 401 from any API call — e.g. the session expired server-side
    // while this tab was open — so the app notices immediately rather than
    // only on the next unrelated /me poll.
    setUnauthorizedHandler(() => {
      setStatus('anonymous');
      setUsername(null);
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  const login = async (usernameInput: string, password: string) => {
    const result = await authApi.login(usernameInput, password);
    setStatus('authenticated');
    setUsername(result.username);
  };

  const logout = async () => {
    try {
      await authApi.logout();
    } finally {
      setStatus('anonymous');
      setUsername(null);
    }
  };

  return <AuthContext.Provider value={{ status, username, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
