import { describe, it, expect, beforeEach, vi } from 'vitest';
import axios from 'axios';
import { apiClient } from '@/api/client';
import { tokenStorage } from '@/lib/tokenStorage';

class LocalStorageMock {
  private store: Record<string, string> = {};

  clear() {
    this.store = {};
  }

  getItem(key: string) {
    return this.store[key] ?? null;
  }

  setItem(key: string, value: string) {
    this.store[key] = String(value);
  }

  removeItem(key: string) {
    delete this.store[key];
  }
}

if (typeof globalThis.localStorage === 'undefined') {
  globalThis.localStorage = new LocalStorageMock() as any;
}

describe('apiClient', () => {
  beforeEach(() => {
    tokenStorage.clear();
    vi.restoreAllMocks();
  });

  it('automatically attaches Authorization Bearer header when access token is present', async () => {
    tokenStorage.setTokens('valid_test_access_token');

    const testConfig: any = { headers: {} };
    const requestInterceptor = (apiClient.interceptors.request as any).handlers[0].fulfilled;

    const modifiedConfig = requestInterceptor(testConfig);
    expect(modifiedConfig.headers.Authorization).toBe('Bearer valid_test_access_token');
  });

  it('omits Authorization header when no access token is stored', async () => {
    const testConfig: any = { headers: {} };
    const requestInterceptor = (apiClient.interceptors.request as any).handlers[0].fulfilled;

    const modifiedConfig = requestInterceptor(testConfig);
    expect(modifiedConfig.headers.Authorization).toBeUndefined();
  });

  it('transparently executes token refresh and retries failed requests on 401', async () => {
    tokenStorage.setTokens('expired_token', 'valid_refresh_token');

    // Mock axios.post for the refresh endpoint
    const postSpy = vi.spyOn(axios, 'post').mockResolvedValueOnce({
      data: {
        accessToken: 'new_fresh_access_token',
        refreshToken: 'new_refresh_token_2',
        accessTokenExpiresAtUtc: new Date(Date.now() + 3600000).toISOString(),
      },
    });

    const mockOriginalRequest: any = {
      headers: {},
      url: '/wallet/balance',
      baseURL: 'http://localhost:5000/api',
      adapter: async () => ({
        data: { balance: 5000 },
        status: 200,
        statusText: 'OK',
        headers: {},
        config: mockOriginalRequest,
      }),
    };

    const mock401Error: any = {
      response: { status: 401 },
      config: mockOriginalRequest,
    };

    const responseInterceptor = (apiClient.interceptors.response as any).handlers[0].rejected;

    const retryResult = await responseInterceptor(mock401Error);

    expect(postSpy).toHaveBeenCalled();
    expect(tokenStorage.getAccessToken()).toBe('new_fresh_access_token');
    expect(tokenStorage.getRefreshToken()).toBe('new_refresh_token_2');
    expect(mockOriginalRequest.headers.Authorization).toBe('Bearer new_fresh_access_token');
    expect(retryResult.data.balance).toBe(5000);
  });
});
