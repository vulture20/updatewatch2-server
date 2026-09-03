import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../api/endpoints';
import { AuthProvider } from './AuthContext';
import { RequireAuth } from './RequireAuth';

vi.mock('../api/endpoints', () => ({
  authApi: {
    me: vi.fn(),
    login: vi.fn(),
    logout: vi.fn(),
    changePassword: vi.fn(),
  },
}));

const mockedMe = vi.mocked(authApi.me);

function renderProtected() {
  return render(
    <MemoryRouter initialEntries={['/agents']}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<div>login page</div>} />
          <Route
            path="/agents"
            element={
              <RequireAuth>
                <div>protected agents page</div>
              </RequireAuth>
            }
          />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('RequireAuth', () => {
  beforeEach(() => {
    mockedMe.mockReset();
  });

  it('redirects to /login when not authenticated', async () => {
    mockedMe.mockResolvedValue({ authenticated: false, username: null });

    renderProtected();

    expect(await screen.findByText('login page')).toBeInTheDocument();
  });

  it('renders the protected content once authenticated', async () => {
    mockedMe.mockResolvedValue({ authenticated: true, username: 'admin' });

    renderProtected();

    expect(await screen.findByText('protected agents page')).toBeInTheDocument();
  });

  it('renders nothing while the initial auth check is still in flight', () => {
    mockedMe.mockReturnValue(new Promise(() => {})); // never resolves

    renderProtected();

    expect(screen.queryByText('login page')).not.toBeInTheDocument();
    expect(screen.queryByText('protected agents page')).not.toBeInTheDocument();
  });
});
