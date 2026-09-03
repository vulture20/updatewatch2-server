import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { AuthProvider } from '../auth/AuthContext';
import { LoginPage } from './LoginPage';

vi.mock('../api/endpoints', () => ({
  authApi: {
    me: vi.fn(),
    login: vi.fn(),
    logout: vi.fn(),
    changePassword: vi.fn(),
  },
}));

const mockedMe = vi.mocked(authApi.me);
const mockedLogin = vi.mocked(authApi.login);

function renderLoginPage() {
  return render(
    <MemoryRouter initialEntries={['/login']}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/agents" element={<div>agents page</div>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('LoginPage', () => {
  beforeEach(() => {
    mockedMe.mockReset().mockResolvedValue({ authenticated: false, username: null });
    mockedLogin.mockReset();
  });

  it('logs in and navigates to /agents on success', async () => {
    mockedLogin.mockResolvedValue({ username: 'admin' });
    const user = userEvent.setup();

    renderLoginPage();
    await screen.findByLabelText('Username');

    await user.type(screen.getByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'correct-password');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('agents page')).toBeInTheDocument();
    expect(mockedLogin).toHaveBeenCalledWith('admin', 'correct-password');
  });

  it('shows the server error message on invalid credentials', async () => {
    mockedLogin.mockRejectedValue(new ApiError(401, 'Invalid username or password.'));
    const user = userEvent.setup();

    renderLoginPage();
    await screen.findByLabelText('Username');

    await user.type(screen.getByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'wrong-password');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid username or password.');
  });

  it('shows a locked-out message on a 423 response', async () => {
    mockedLogin.mockRejectedValue(new ApiError(423, 'irrelevant body text'));
    const user = userEvent.setup();

    renderLoginPage();
    await screen.findByLabelText('Username');

    await user.type(screen.getByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'whatever');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many failed attempts');
  });
});
