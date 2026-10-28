import apiClient from './client';
import type { LoginRequest, RegisterRequest, AuthResponse, User } from '@/types';

export const authApi = {
  async login(request: LoginRequest): Promise<AuthResponse> {
    const response = await apiClient.post<AuthResponse>('/auth/login', request);
    return response.data;
  },

  async register(request: RegisterRequest): Promise<AuthResponse> {
    const response = await apiClient.post<AuthResponse>('/auth/register', request);
    return response.data;
  },

  async refreshToken(refreshToken: string): Promise<AuthResponse> {
    const response = await apiClient.post<AuthResponse>('/auth/refresh', { refreshToken });
    return response.data;
  },

  async logout(refreshToken?: string): Promise<void> {
    if (refreshToken) {
      try {
        await apiClient.post('/auth/logout', { refreshToken });
      } catch (err) {
        // Fire-and-forget; client will still purge local tokens
        console.warn('Logout notification failed on backend:', err);
      }
    }
  },

  async getCurrentUser(): Promise<User> {
    const response = await apiClient.get<User>('/auth/me');
    return response.data;
  },
};

export default authApi;
