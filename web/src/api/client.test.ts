import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiClient, ApiError, setUnauthorizedHandler } from './client';

describe('apiClient', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    setUnauthorizedHandler(null);
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
