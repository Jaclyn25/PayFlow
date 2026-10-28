const ACCESS_TOKEN_KEY = 'payflow_access_token';
const REFRESH_TOKEN_KEY = 'payflow_refresh_token';
const EXPIRES_AT_KEY = 'payflow_token_expires_at';
const USER_KEY = 'payflow_user';

export const tokenStorage = {
  getAccessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY) || localStorage.getItem('payflow_token');
  },

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  },

  getExpiresAt(): number | null {
    const val = localStorage.getItem(EXPIRES_AT_KEY);
    return val ? parseInt(val, 10) : null;
  },

  setTokens(accessToken: string, refreshToken?: string, expiresAtUtc?: string | number): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    // Maintain backward-compatibility with previous key
    localStorage.setItem('payflow_token', accessToken);

    if (refreshToken) {
      localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    }

    if (expiresAtUtc) {
      const timestamp =
        typeof expiresAtUtc === 'number'
          ? expiresAtUtc
          : new Date(expiresAtUtc).getTime();
      localStorage.setItem(EXPIRES_AT_KEY, timestamp.toString());
    }
  },

  clear(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem('payflow_token');
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(EXPIRES_AT_KEY);
    localStorage.removeItem(USER_KEY);
  },

  isTokenExpired(): boolean {
    const expiresAt = this.getExpiresAt();
    if (!expiresAt) {
      // If no explicit expiration, check JWT expiration if token is a standard JWT
      const token = this.getAccessToken();
      if (!token || !token.includes('.')) return false;
      try {
        const payloadBase64 = token.split('.')[1];
        const decoded = JSON.parse(atob(payloadBase64));
        if (decoded.exp) {
          return Date.now() >= decoded.exp * 1000 - 30000; // 30s buffer
        }
      } catch {
        return false;
      }
      return false;
    }
    // Buffer by 30 seconds to prevent edge-case race conditions
    return Date.now() >= expiresAt - 30000;
  },
};
