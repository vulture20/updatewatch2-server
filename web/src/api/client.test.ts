import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiClient, ApiError } from './client';

describe('apiClient', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
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
});
