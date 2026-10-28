import apiClient from './client';
import type { Wallet, Transaction } from '@/types';

export interface DepositPayload {
  amount: number;
  description?: string;
}

export interface WithdrawPayload {
  amount: number;
  description?: string;
}

export const walletApi = {
  async getWallet(): Promise<Wallet> {
    const response = await apiClient.get<Wallet>('/wallets/me');
    return response.data;
  },

  async deposit(payload: DepositPayload): Promise<Transaction> {
    const response = await apiClient.post<Transaction>('/wallets/deposit', payload);
    return response.data;
  },

  async withdraw(payload: WithdrawPayload): Promise<Transaction> {
    const response = await apiClient.post<Transaction>('/wallets/withdraw', payload);
    return response.data;
  },

  async getRecentTransactions(): Promise<Transaction[]> {
    try {
      const response = await apiClient.get<any>('/transaction/history?page=1&pageSize=5');
      return response.data.items || [];
    } catch {
      return [];
    }
  },
};

export default walletApi;
