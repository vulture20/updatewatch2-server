// Exported so a non-JSON, browser-navigated URL (e.g. the CA certificate
// download link in AdminPage's Certificates tab) can be built without
// duplicating this resolution logic — apiClient's own get/post/put/delete
// helpers below are for JSON calls only, not file downloads.
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * Set by AuthProvider so a 401 from any API call (e.g. a session that
 * expired while the tab was open) clears auth state immediately, rather
 * than only being noticed the next time /api/auth/me happens to be
 * checked. Session-establishing/-checking calls (login, me) are excluded
 * by their callers to avoid tripping this on an ordinary failed login.
 */
let onUnauthorized: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler;
}

async function handleResponse<T>(
  response: Response,
  method: string,
  path: string,
  options?: { skipUnauthorizedHandler?: boolean },
): Promise<T> {
  if (!response.ok) {
    if (response.status === 401 && !options?.skipUnauthorizedHandler) {
      onUnauthorized?.();
    }

    const message = await readErrorMessage(response, method, path);
    throw new ApiError(response.status, message);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function request<T>(path: string, init?: RequestInit & { skipUnauthorizedHandler?: boolean }): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });

  return handleResponse<T>(response, init?.method ?? 'GET', path, init);
}

/**
 * A multipart/form-data POST (agentUpdatesApi.upload's manual agent-binary
 * upload) — deliberately not routed through request()'s JSON path: setting
 * a Content-Type header by hand for a FormData body omits the multipart
 * boundary the browser would otherwise add itself, which silently breaks
 * the upload server-side. Shares request()'s own response handling
 * (unauthorized/error/204 behavior) via handleResponse.
 */
async function requestForm<T>(path: string, formData: FormData): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST',
    credentials: 'include',
    body: formData,
  });

  return handleResponse<T>(response, 'POST', path);
}

async function readErrorMessage(response: Response, method: string, path: string): Promise<string> {
  try {
    const body: unknown = await response.clone().json();
    if (body && typeof body === 'object') {
      if ('message' in body && typeof body.message === 'string') {
        return body.message;
      }
      // ASP.NET Core model-validation failures (e.g. AdminController's PUT)
      // come back as { errors: string[] } rather than a single message.
      if ('errors' in body && Array.isArray(body.errors)) {
        const errors = body.errors.filter((e): e is string => typeof e === 'string');
        if (errors.length > 0) {
          return errors.join(' ');
        }
      }
    }
  } catch {
    // response body wasn't JSON (or was empty) — fall through to the generic message
  }
  return `${method} ${path} failed with ${response.status}`;
}

export const apiClient = {
  get: <T>(path: string, options?: { skipUnauthorizedHandler?: boolean }) =>
    request<T>(path, options),
  post: <T>(path: string, body?: unknown, options?: { skipUnauthorizedHandler?: boolean }) =>
    request<T>(path, {
      method: 'POST',
      body: body === undefined ? undefined : JSON.stringify(body),
      ...options,
    }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
  postForm: <T>(path: string, formData: FormData) => requestForm<T>(path, formData),
};
