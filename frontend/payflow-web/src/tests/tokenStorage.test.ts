import { describe, it, expect, beforeEach } from 'vitest';
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

describe('tokenStorage', () => {
  beforeEach(() => {
    tokenStorage.clear();
  });

  it('stores and retrieves access and refresh tokens', () => {
    tokenStorage.setTokens('access_123', 'refresh_456');

    expect(tokenStorage.getAccessToken()).toBe('access_123');
    expect(tokenStorage.getRefreshToken()).toBe('refresh_456');
  });

  it('clears all tokens and cached user data', () => {
    tokenStorage.setTokens('access_123', 'refresh_456');
    localStorage.setItem('payflow_user', JSON.stringify({ id: '1' }));

    tokenStorage.clear();

    expect(tokenStorage.getAccessToken()).toBeNull();
    expect(tokenStorage.getRefreshToken()).toBeNull();
    expect(localStorage.getItem('payflow_user')).toBeNull();
  });

  it('identifies expired tokens when expiration timestamp is in the past', () => {
    const pastTimestamp = Date.now() - 10000;
    tokenStorage.setTokens('access_123', 'refresh_456', pastTimestamp);

    expect(tokenStorage.isTokenExpired()).toBe(true);
  });

  it('identifies unexpired tokens when expiration is well into the future', () => {
    const futureTimestamp = Date.now() + 1000 * 60 * 60; // 1 hour
    tokenStorage.setTokens('access_123', 'refresh_456', futureTimestamp);

    expect(tokenStorage.isTokenExpired()).toBe(false);
  });

  it('correctly handles JWT payload expiry decoding', () => {
    const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
    const expiredPayload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) - 100 }));
    const validPayload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }));

    tokenStorage.setTokens(`${header}.${expiredPayload}.signature`);
    expect(tokenStorage.isTokenExpired()).toBe(true);

    tokenStorage.clear();
    tokenStorage.setTokens(`${header}.${validPayload}.signature`);
    expect(tokenStorage.isTokenExpired()).toBe(false);
  });
});
