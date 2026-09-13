import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import { apiClient, ApiError, setUnauthorizedHandler } from './client';

describe('apiClient', () => {
  afterEach(async () => {
    vi.unstubAllGlobals();
    setUnauthorizedHandler(null);
    await i18n.changeLanguage('en');
  });

  it('sends a GET request and returns the parsed JSON body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ hello: 'world' }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const result = await apiClient.get<{ hello: string }>('/api/health');

    expect(result).toEqual({ hello: 'world' });
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/health',
      expect.objectContaining({ credentials: 'include' }),
    );
  });

  it('serializes the body and sets method POST for apiClient.post', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await apiClient.post('/api/agents/host-1/approve');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/agents/host-1/approve',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('sets method DELETE for apiClient.delete', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await apiClient.delete('/api/agents/host-1');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/agents/host-1',
      expect.objectContaining({ method: 'DELETE' }),
    );
  });

  it('throws ApiError with the response status when the request fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    await expect(apiClient.get('/api/agents/missing')).rejects.toMatchObject(
      new ApiError(404, 'GET /api/agents/missing failed with 404'),
    );
  });

  it('uses the server-provided message when the error body has one', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ message: 'Invalid username or password.' }), { status: 401 }),
      ),
    );

    await expect(apiClient.get('/api/auth/me')).rejects.toMatchObject(
      new ApiError(401, 'Invalid username or password.'),
    );
  });

  it('joins ASP.NET Core model-validation errors into the message', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({ errors: ['LogLevel must be one of: DEBUG, INFO, WARNING, ERROR.', 'SmtpPort must be between 1 and 65535.'] }),
          { status: 400 },
        ),
      ),
    );

    await expect(apiClient.put('/api/admin/settings', {})).rejects.toMatchObject(
      new ApiError(400, 'LogLevel must be one of: DEBUG, INFO, WARNING, ERROR. SmtpPort must be between 1 and 65535.'),
    );
  });

  // updatewatch2-server#17 — a stable errorCode alongside the free-text
  // message lets the web UI translate it, falling back to the raw
  // server-supplied text (unchanged from before this mechanism existed)
  // for anything it doesn't recognize.
  it('translates a recognized errorCode via i18n instead of using the raw English message', async () => {
    await i18n.changeLanguage('de');
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ message: 'Invalid username or password.', errorCode: 'InvalidCredentials' }), { status: 401 }),
      ),
    );

    await expect(apiClient.get('/api/auth/me')).rejects.toMatchObject(
      new ApiError(401, 'Benutzername oder Passwort ungültig.'),
    );
  });

  it('falls back to the raw message for an errorCode this frontend build does not recognize', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ message: 'Something only a newer server knows about.', errorCode: 'SomeFutureCode' }), { status: 400 }),
      ),
    );

    await expect(apiClient.get('/api/agents')).rejects.toMatchObject(
      new ApiError(400, 'Something only a newer server knows about.'),
    );
  });

  it('interpolates errorDetail into the translated template for a dynamic-content code', async () => {
    await i18n.changeLanguage('de');
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({ message: 'Connection refused', errorCode: 'TestEmailFailed', errorDetail: 'Connection refused' }),
          { status: 502 },
        ),
      ),
    );

    await expect(apiClient.post('/api/admin/notifications/test-email', {})).rejects.toMatchObject(
      new ApiError(502, 'Test-E-Mail konnte nicht gesendet werden: Connection refused'),
    );
  });

  it('translates each item of a structured errors[] list independently and joins them', async () => {
    await i18n.changeLanguage('de');
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            errors: [
              { code: 'LogLevelInvalid', message: 'LogLevel must be one of: DEBUG, INFO, WARNING, ERROR.' },
              { code: 'SmtpPortInvalid', message: 'SmtpPort must be between 1 and 65535.' },
            ],
          }),
          { status: 400 },
        ),
      ),
    );

    await expect(apiClient.put('/api/admin/settings', {})).rejects.toMatchObject(
      new ApiError(
        400,
        'LogLevel muss einer der folgenden Werte sein: DEBUG, INFO, WARNING, ERROR. SmtpPort muss zwischen 1 und 65535 liegen.',
      ),
    );
  });

  it('calls the unauthorized handler on a 401 response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));
    const handler = vi.fn();
    setUnauthorizedHandler(handler);

    await expect(apiClient.get('/api/agents')).rejects.toThrow(ApiError);

    expect(handler).toHaveBeenCalledOnce();
  });

  it('does not call the unauthorized handler when skipUnauthorizedHandler is set', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));
    const handler = vi.fn();
    setUnauthorizedHandler(handler);

    await expect(
      apiClient.get('/api/auth/me', { skipUnauthorizedHandler: true }),
    ).rejects.toThrow(ApiError);

    expect(handler).not.toHaveBeenCalled();
  });
});
