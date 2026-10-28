import { describe, it, expect, beforeEach, vi } from 'vitest';
import { walletApi } from '@/api/wallet';
import apiClient from '@/api/client';

describe('walletApi', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches current user wallet from /wallets/me', async () => {
    const mockWallet = {
      id: 'wlt_123',
      userId: 'usr_456',
      balance: 14500.5,
      currency: 'USD',
      updatedAtUtc: new Date().toISOString(),
      isFrozen: false,
      freezeReason: null,
    };

    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockWallet });

    const result = await walletApi.getWallet();

    expect(apiClient.get).toHaveBeenCalledWith('/wallets/me');
    expect(result.id).toBe('wlt_123');
    expect(result.balance).toBe(14500.5);
  });

  it('submits a deposit payload to /wallets/deposit', async () => {
    const mockTransaction = {
      id: 'tx_dep_01',
      reference: 'TXN-DEP-001',
      type: 'Deposit',
      status: 'Completed',
      amount: 500,
      currency: 'USD',
      createdAtUtc: new Date().toISOString(),
      exchangeRate: 1.0,
      feeAmount: 0,
    };

    vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: mockTransaction });

    const result = await walletApi.deposit({ amount: 500, description: 'Test Top-Up' });

    expect(apiClient.post).toHaveBeenCalledWith('/wallets/deposit', {
      amount: 500,
      description: 'Test Top-Up',
    });
    expect(result.id).toBe('tx_dep_01');
    expect(result.amount).toBe(500);
  });

  it('submits a withdrawal payload to /wallets/withdraw', async () => {
    const mockTransaction = {
      id: 'tx_wdr_01',
      reference: 'TXN-WDR-001',
      type: 'Withdrawal',
      status: 'Completed',
      amount: 250,
      currency: 'USD',
      createdAtUtc: new Date().toISOString(),
      exchangeRate: 1.0,
      feeAmount: 0,
    };

    vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: mockTransaction });

    const result = await walletApi.withdraw({ amount: 250, description: 'Bank Payout' });

    expect(apiClient.post).toHaveBeenCalledWith('/wallets/withdraw', {
      amount: 250,
      description: 'Bank Payout',
    });
    expect(result.id).toBe('tx_wdr_01');
    expect(result.amount).toBe(250);
  });
});
