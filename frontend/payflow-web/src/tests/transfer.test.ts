import { describe, it, expect, beforeEach, vi } from 'vitest';
import { transferApi } from '@/api/transfer';
import { generateUUID } from '@/components/wallet/TransferModal';
import apiClient from '@/api/client';

describe('Transfer & Idempotency Engine', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('generateUUID', () => {
    it('generates a valid RFC 4122 compliant UUIDv4 string', () => {
      const uuid = generateUUID();
      const uuidV4Regex =
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
      expect(uuid).toMatch(uuidV4Regex);
    });

    it('generates distinct idempotency keys across consecutive calls', () => {
      const key1 = generateUUID();
      const key2 = generateUUID();
      expect(key1).not.toBe(key2);
    });
  });

  describe('transferApi', () => {
    it('attaches Idempotency-Key header on outbound transfer POST request', async () => {
      const mockTx = {
        id: 'tx_uuid_999',
        reference: 'TXN-TRANSFER-001',
        type: 'Transfer',
        status: 'Completed',
        amount: 250.0,
        currency: 'USD',
        createdAtUtc: new Date().toISOString(),
        exchangeRate: 1.0,
        feeAmount: 1.5,
      };

      const idempotencyKey = generateUUID();
      const transferRequest = {
        receiverEmail: 'bob@payflow.local',
        amount: 250.0,
        description: 'Testing Idempotency Transfer',
      };

      vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: mockTx });

      const result = await transferApi.transfer(transferRequest, idempotencyKey);

      expect(apiClient.post).toHaveBeenCalledWith('/transfer', transferRequest, {
        headers: {
          'Idempotency-Key': idempotencyKey,
        },
      });
      expect(result.id).toBe('tx_uuid_999');
      expect(result.amount).toBe(250.0);
    });

    it('retrieves fee quote breakdown with calculated protocol fees', async () => {
      const mockQuote = {
        amount: 1000.0,
        feeAmount: 5.25,
        grossAmount: 1005.25,
        netAmount: 1000.0,
        currency: 'USD',
        percentage: 0.5,
        fixedFee: 0.25,
        capAmount: 50.0,
      };

      vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockQuote });

      const quote = await transferApi.getFeeQuote(1000.0, 'USD');

      expect(apiClient.get).toHaveBeenCalledWith('/transaction/fee-quote?amount=1000&currency=USD');
      expect(quote.feeAmount).toBe(5.25);
      expect(quote.grossAmount).toBe(1005.25);
    });

    it('falls back to standard fee formula when fee-quote endpoint is unreachable', async () => {
      vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Network error'));

      const quote = await transferApi.getFeeQuote(200.0, 'USD');

      // 0.5% of 200 = 1.00
      expect(quote.feeAmount).toBe(1.0);
      expect(quote.grossAmount).toBe(201.0);
      expect(quote.currency).toBe('USD');
    });

    it('verifies recipient format and extracts counterparty name', async () => {
      const validRes = await transferApi.verifyRecipient('jane.smith@payflow.local');
      expect(validRes.exists).toBe(true);
      expect(validRes.name).toBe('jane smith');

      const invalidRes = await transferApi.verifyRecipient('invalid-recipient');
      expect(invalidRes.exists).toBe(false);
    });
  });
});
