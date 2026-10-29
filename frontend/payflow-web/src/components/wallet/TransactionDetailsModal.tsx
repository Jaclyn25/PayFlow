import React, { useState } from 'react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { formatCurrency, formatDate } from '@/lib/utils';
import {
  Copy,
  Check,
  ArrowDownLeft,
  ArrowUpRight,
  AlertCircle,
  ShieldCheck,
  Clock,
} from 'lucide-react';
import type { Transaction } from '@/types';

interface TransactionDetailsModalProps {
  isOpen: boolean;
  onClose: () => void;
  transaction: Transaction | null;
}

export const TransactionDetailsModal: React.FC<TransactionDetailsModalProps> = ({
  isOpen,
  onClose,
  transaction,
}) => {
  const [copiedField, setCopiedField] = useState<string | null>(null);

  if (!transaction) return null;

  const handleCopy = (field: string, text: string) => {
    navigator.clipboard.writeText(text);
    setCopiedField(field);
    setTimeout(() => setCopiedField(null), 2000);
  };

  const isPositive = transaction.type === 'Deposit';
  const totalAmount = transaction.grossAmount || transaction.amount + (transaction.feeAmount || 0);

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Transaction Audit Record"
      description="Cryptographically verified double-entry immutable ledger entry."
    >
      <div className="space-y-4">
        {/* Main Status & Amount Header */}
        <div className="p-4 rounded-xl bg-muted/40 border border-border flex items-center justify-between">
          <div className="space-y-1">
            <span className="text-xs uppercase tracking-wider text-muted-foreground font-semibold">
              Net Amount
            </span>
            <div className="flex items-center space-x-2">
              {isPositive ? (
                <ArrowDownLeft className="w-5 h-5 text-emerald-500" />
              ) : (
                <ArrowUpRight className="w-5 h-5 text-slate-500" />
              )}
              <span
                className={`text-2xl font-bold tracking-tight ${
                  isPositive
                    ? 'text-emerald-600 dark:text-emerald-400'
                    : 'text-foreground'
                }`}
              >
                {isPositive ? '+' : '-'}
                {formatCurrency(transaction.amount, transaction.currency)}
              </span>
            </div>
          </div>

          <Badge
            variant={
              transaction.status === 'Completed'
                ? 'success'
                : transaction.status === 'Pending'
                ? 'warning'
                : 'danger'
            }
          >
            {transaction.status}
          </Badge>
        </div>

        {/* Failure reason banner if applicable */}
        {transaction.failureReason && (
          <div className="p-3.5 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 flex items-center space-x-2 text-rose-800 dark:text-rose-300 text-xs">
            <AlertCircle className="w-4 h-4 flex-shrink-0" />
            <span>
              <strong>Failure Cause:</strong> {transaction.failureReason}
            </span>
          </div>
        )}

        {/* Ledger Breakdown Details */}
        <div className="space-y-2 rounded-lg border border-border p-4 bg-card text-xs">
          <div className="flex justify-between items-center py-1.5 border-b">
            <span className="text-muted-foreground">Reference:</span>
            <div className="flex items-center space-x-1.5">
              <span className="font-mono font-semibold text-foreground">
                {transaction.reference}
              </span>
              <button
                type="button"
                onClick={() => handleCopy('ref', transaction.reference)}
                className="hover:text-foreground text-muted-foreground p-0.5"
                title="Copy Reference"
              >
                {copiedField === 'ref' ? (
                  <Check className="w-3.5 h-3.5 text-emerald-500" />
                ) : (
                  <Copy className="w-3.5 h-3.5" />
                )}
              </button>
            </div>
          </div>

          <div className="flex justify-between items-center py-1.5 border-b">
            <span className="text-muted-foreground">Transaction ID:</span>
            <div className="flex items-center space-x-1.5">
              <span className="font-mono text-muted-foreground truncate max-w-[200px]" title={transaction.id}>
                {transaction.id}
              </span>
              <button
                type="button"
                onClick={() => handleCopy('id', transaction.id)}
                className="hover:text-foreground text-muted-foreground p-0.5"
                title="Copy ID"
              >
                {copiedField === 'id' ? (
                  <Check className="w-3.5 h-3.5 text-emerald-500" />
                ) : (
                  <Copy className="w-3.5 h-3.5" />
                )}
              </button>
            </div>
          </div>

          <div className="flex justify-between py-1.5 border-b">
            <span className="text-muted-foreground">Transaction Type:</span>
            <span className="font-medium text-foreground">{transaction.type}</span>
          </div>

          <div className="flex justify-between py-1.5 border-b">
            <span className="text-muted-foreground">Counterparty / Note:</span>
            <span className="font-medium text-foreground text-right max-w-xs truncate">
              {transaction.recipient || transaction.sender || transaction.description || 'Internal'}
            </span>
          </div>

          <div className="flex justify-between py-1.5 border-b">
            <span className="text-muted-foreground">Protocol Fee:</span>
            <span className="font-medium text-foreground">
              {transaction.feeAmount
                ? formatCurrency(transaction.feeAmount, transaction.currency)
                : 'Free ($0.00)'}
            </span>
          </div>

          <div className="flex justify-between py-1.5 border-b font-bold text-sm">
            <span>Total Debited / Credited:</span>
            <span className="text-primary">
              {formatCurrency(totalAmount, transaction.currency)}
            </span>
          </div>

          {transaction.exchangeRate && transaction.exchangeRate !== 1 && (
            <div className="flex justify-between py-1.5 border-b text-muted-foreground">
              <span>Exchange Rate:</span>
              <span>1 {transaction.currency} = {transaction.exchangeRate}</span>
            </div>
          )}

          {transaction.idempotencyKey && (
            <div className="flex justify-between items-center py-1.5 border-b">
              <div className="flex items-center space-x-1">
                <ShieldCheck className="w-3 h-3 text-primary" />
                <span className="text-muted-foreground">Idempotency Key:</span>
              </div>
              <span className="font-mono text-muted-foreground truncate max-w-[180px]" title={transaction.idempotencyKey}>
                {transaction.idempotencyKey}
              </span>
            </div>
          )}

          <div className="flex justify-between items-center py-1.5 pt-2 text-muted-foreground text-[11px]">
            <div className="flex items-center space-x-1">
              <Clock className="w-3 h-3" />
              <span>Timestamp:</span>
            </div>
            <span>{formatDate(transaction.createdAtUtc || transaction.createdAt || '')}</span>
          </div>
        </div>

        <div className="flex justify-end pt-2">
          <Button onClick={onClose}>Close</Button>
        </div>
      </div>
    </Modal>
  );
};
