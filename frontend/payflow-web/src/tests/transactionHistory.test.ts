import { describe, it, expect, beforeEach, vi } from 'vitest';
import { transactionApi } from '@/api/transaction';
import apiClient from '@/api/client';
import type { PagedResult, Transaction } from '@/types';

// Mock browser global environment if running under Node test runner
if (typeof (globalThis as unknown as { window?: unknown }).window === 'undefined') {
  (globalThis as unknown as { window: unknown }).window = {
    URL: {
      createObjectURL: vi.fn(),
      revokeObjectURL: vi.fn(),
    },
  };
}

if (typeof (globalThis as unknown as { document?: unknown }).document === 'undefined') {
  (globalThis as unknown as { document: unknown }).document = {
    body: {
      appendChild: vi.fn(),
      removeChild: vi.fn(),
    },
    createElement: vi.fn(),
  };
}

describe('Transaction History & Statement Engine', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('transactionApi.getTransactions', () => {
    it('constructs correct query parameters and calls apiClient.get', async () => {
      const mockResult: PagedResult<Transaction> = {
        items: [
          {
            id: 'tx_test_1',
            reference: 'REF-TX-TEST',
            amount: 500,
            currency: 'USD',
            type: 'Transfer',
            status: 'Completed',
            createdAtUtc: new Date().toISOString(),
            exchangeRate: 1.0,
            feeAmount: 1.5,
          },
        ],
        page: 2,
        pageSize: 15,
        totalCount: 1,
        totalPages: 1,
      };

      const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockResult });

      const result = await transactionApi.getTransactions({
        page: 2,
        pageSize: 15,
        type: 'Transfer',
        status: 'Completed',
        fromDate: '2026-01-01',
        toDate: '2026-01-31',
      });

      expect(getSpy).toHaveBeenCalledTimes(1);
      const url = getSpy.mock.calls[0][0];
      expect(url).toContain('/transaction?');
      expect(url).toContain('page=2');
      expect(url).toContain('pageSize=15');
      expect(url).toContain('type=Transfer');
      expect(url).toContain('status=Completed');
      expect(url).toContain('fromDate=2026-01-01');
      expect(url).toContain('toDate=2026-01-31');
      expect(result.items).toHaveLength(1);
      expect(result.items[0].id).toBe('tx_test_1');
    });

    it('omits all-filters when set to "all" in query params', async () => {
      const mockResult: PagedResult<Transaction> = {
        items: [],
        page: 1,
        pageSize: 10,
        totalCount: 0,
        totalPages: 0,
      };

      const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockResult });

      await transactionApi.getTransactions({
        page: 1,
        pageSize: 10,
        type: 'all',
        status: 'all',
      });

      const url = getSpy.mock.calls[0][0];
      expect(url).not.toContain('type=all');
      expect(url).not.toContain('status=all');
    });

    it('provides reliable fallback filtering when backend is offline', async () => {
      vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Connection failed'));

      // Filter by Deposit
      const result = await transactionApi.getTransactions({
        page: 1,
        pageSize: 10,
        type: 'Deposit',
      });

      expect(result.items.length).toBeGreaterThan(0);
      result.items.forEach((item) => {
        expect(item.type.toLowerCase()).toBe('deposit');
      });
    });

    it('filters fallback mock data by status and search term', async () => {
      vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Network error'));

      const result = await transactionApi.getTransactions({
        page: 1,
        pageSize: 10,
        searchTerm: 'Acme',
      });

      expect(result.items.length).toBeGreaterThan(0);
      expect(result.items.some((item) => item.recipient?.includes('Acme'))).toBe(true);
    });

    it('properly paginates fallback dataset', async () => {
      vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Network error'));

      const page1 = await transactionApi.getTransactions({
        page: 1,
        pageSize: 2,
      });

      const page2 = await transactionApi.getTransactions({
        page: 2,
        pageSize: 2,
      });

      expect(page1.items.length).toBe(2);
      expect(page2.items.length).toBe(2);
      expect(page1.items[0].id).not.toBe(page2.items[0].id);
      expect(page1.totalPages).toBeGreaterThan(1);
    });
  });

  describe('transactionApi.getTransactionById', () => {
    it('fetches single transaction details by ID', async () => {
      const mockTx: Transaction = {
        id: 'tx_details_123',
        reference: 'REF-TX-123',
        amount: 880.0,
        currency: 'USD',
        type: 'Transfer',
        status: 'Completed',
        createdAtUtc: new Date().toISOString(),
        exchangeRate: 1.0,
        feeAmount: 2.0,
        idempotencyKey: 'idem_key_123',
      };

      vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockTx });

      const tx = await transactionApi.getTransactionById('tx_details_123');

      expect(apiClient.get).toHaveBeenCalledWith('/transaction/tx_details_123');
      expect(tx.id).toBe('tx_details_123');
      expect(tx.reference).toBe('REF-TX-123');
      expect(tx.idempotencyKey).toBe('idem_key_123');
    });
  });

  describe('transactionApi.downloadStatement', () => {
    it('triggers blob download for CSV statement', async () => {
      const csvData = 'Date,Reference,Type,Amount,Status\n2026-10-29,TX-01,Transfer,100,Completed';
      vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: csvData });

      const createObjectURLMock = vi.fn().mockReturnValue('blob:http://localhost/dummy');
      const revokeObjectURLMock = vi.fn();
      window.URL.createObjectURL = createObjectURLMock;
      window.URL.revokeObjectURL = revokeObjectURLMock;

      const clickMock = vi.fn();
      vi.spyOn(document.body, 'appendChild').mockImplementation(() => document.body as unknown as Node);
      vi.spyOn(document.body, 'removeChild').mockImplementation(() => document.body as unknown as Node);

      vi.spyOn(document, 'createElement').mockReturnValue({
        set href(_val: string) {},
        set download(_val: string) {},
        click: clickMock,
      } as unknown as HTMLAnchorElement);

      await transactionApi.downloadStatement('csv', '2026-01-01', '2026-01-31');

      expect(apiClient.get).toHaveBeenCalledWith(
        expect.stringContaining('/wallets/statement?format=csv&from=2026-01-01&to=2026-01-31'),
        { responseType: 'blob' }
      );
      expect(clickMock).toHaveBeenCalled();
    });
  });
});
