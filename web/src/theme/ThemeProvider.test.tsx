import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { ThemeProvider, useTheme } from './ThemeProvider';

function ThemeConsumer() {
  const { theme, setTheme } = useTheme();
  return (
    <button type="button" onClick={() => setTheme(theme === 'light' ? 'dark' : 'light')}>
      current: {theme}
    </button>
  );
}

describe('ThemeProvider', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  it('defaults to light when nothing is stored and the system has no preference', () => {
    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    );

    expect(screen.getByRole('button')).toHaveTextContent('current: light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('reads a previously stored preference', () => {
    localStorage.setItem('updatewatch2.theme', 'dark');

    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    );

    expect(screen.getByRole('button')).toHaveTextContent('current: dark');
  });

  it('toggling updates the DOM attribute and persists the choice', async () => {
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    );

    await user.click(screen.getByRole('button'));

    expect(screen.getByRole('button')).toHaveTextContent('current: dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem('updatewatch2.theme')).toBe('dark');
  });
});
