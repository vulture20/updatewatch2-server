import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';
import '@testing-library/jest-dom/vitest';
import './i18n';

// `globals: false` in vite.config.ts means @testing-library/react's
// automatic afterEach(cleanup) registration (which looks for a global
// `afterEach`) doesn't fire on its own — so it's wired up explicitly here.
afterEach(() => {
  cleanup();
});
