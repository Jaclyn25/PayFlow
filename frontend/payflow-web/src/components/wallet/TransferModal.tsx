import React, { useState, useEffect } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { transferApi, FeeQuoteResponse } from '@/api/transfer';
import { toast } from '@/components/ui/toast';
import { formatCurrency, formatDate } from '@/lib/utils';
import { useAuth } from '@/context/AuthContext';
import {
  Send,
  AlertCircle,
  ShieldCheck,
  CheckCircle2,
  Copy,
  Check,
  Zap,
  Lock,
  UserCheck,
} from 'lucide-react';
import type { Transaction } from '@/types';

interface TransferModalProps {
  isOpen: boolean;
  onClose: () => void;
  availableBalance?: number;
  currency?: string;
  onSuccess?: (tx: Transaction) => void;
}

export function generateUUID(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export const TransferModal: React.FC<TransferModalProps> = ({
  isOpen,
  onClose,
  availableBalance = 14500.5,
  currency = 'USD',
  onSuccess,
}) => {
  const { user } = useAuth();
  const queryClient = useQueryClient();

  const [idempotencyKey, setIdempotencyKey] = useState<string>(() => generateUUID());
  const [receiverEmail, setReceiverEmail] = useState('');
  const [amount, setAmount] = useState('');
  const [description, setDescription] = useState('');
  const [twoFactorCode, setTwoFactorCode] = useState('');
  const [error, setError] = useState<string | null>(null);

  const [feeQuote, setFeeQuote] = useState<FeeQuoteResponse | null>(null);
  const [isVerifyingRecipient, setIsVerifyingRecipient] = useState(false);
  const [recipientVerified, setRecipientVerified] = useState<string | null>(null);
  const [copiedKey, setCopiedKey] = useState(false);
  const [copiedTxId, setCopiedTxId] = useState(false);
  const [completedTx, setCompletedTx] = useState<Transaction | null>(null);

  // Generate a fresh idempotency key whenever the modal is freshly opened
  useEffect(() => {
    if (isOpen && !completedTx) {
      setIdempotencyKey(generateUUID());
    }
  }, [isOpen]);

  // Debounced recipient check
  useEffect(() => {
    if (!receiverEmail || !receiverEmail.includes('@')) {
      setRecipientVerified(null);
      setIsVerifyingRecipient(false);
      return;
    }

    setIsVerifyingRecipient(true);
    const timeout = setTimeout(async () => {
      const res = await transferApi.verifyRecipient(receiverEmail);
      setIsVerifyingRecipient(false);
      if (res.exists) {
        setRecipientVerified(res.name || receiverEmail);
      } else {
        setRecipientVerified(null);
      }
    }, 400);

    return () => clearTimeout(timeout);
  }, [receiverEmail]);

  // Live fee calculation
  useEffect(() => {
    const parsedAmount = parseFloat(amount);
    if (!parsedAmount || parsedAmount <= 0) {
      setFeeQuote(null);
      return;
    }

    let isSubscribed = true;
    transferApi.getFeeQuote(parsedAmount, currency).then((quote) => {
      if (isSubscribed) {
        setFeeQuote(quote);
      }
    });

    return () => {
      isSubscribed = false;
    };
  }, [amount, currency]);

  const transferMutation = useMutation({
    mutationFn: (data: {
      receiverEmail: string;
      amount: number;
      description?: string;
      twoFactorCode?: string;
    }) => transferApi.transfer(data, idempotencyKey),
    onSuccess: (tx) => {
      queryClient.invalidateQueries({ queryKey: ['wallet'] });
      queryClient.invalidateQueries({ queryKey: ['transactions'] });
      setCompletedTx(tx);
      toast.success(
        'Transfer Completed!',
        `Sent ${formatCurrency(parseFloat(amount), currency)} to ${receiverEmail}`
      );
      onSuccess?.(tx);
    },
    onError: (err: any) => {
      // NOTE: Retain idempotencyKey across failed attempts to prevent duplicate charging on retry
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.title ||
        err?.message ||
        'Failed to execute transfer. Please try again.';
      setError(msg);
    },
  });

  const parsedAmount = parseFloat(amount) || 0;
  const totalDebit = feeQuote ? feeQuote.grossAmount : parsedAmount;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!receiverEmail || parsedAmount <= 0) {
      setError('Please provide a valid receiver and amount.');
      return;
    }

    if (totalDebit > availableBalance) {
      setError(
        `Insufficient liquidity. Total required is ${formatCurrency(totalDebit, currency)}, available: ${formatCurrency(availableBalance, currency)}.`
      );
      return;
    }

    if (user?.twoFactorEnabled && !twoFactorCode) {
      setError('Two-factor authorization code is required for this transfer.');
      return;
    }

    setError(null);
    transferMutation.mutate({
      receiverEmail: receiverEmail.trim(),
      amount: parsedAmount,
      description: description.trim() || undefined,
      twoFactorCode: twoFactorCode.trim() || undefined,
    });
  };

  const handleResetForNewTransfer = () => {
    setCompletedTx(null);
    setReceiverEmail('');
    setAmount('');
    setDescription('');
    setTwoFactorCode('');
    setError(null);
    setIdempotencyKey(generateUUID());
  };

  const handleCopyIdempotencyKey = () => {
    navigator.clipboard.writeText(idempotencyKey);
    setCopiedKey(true);
    setTimeout(() => setCopiedKey(false), 2000);
  };

  const handleCopyTxReference = (ref: string) => {
    navigator.clipboard.writeText(ref);
    setCopiedTxId(true);
    setTimeout(() => setCopiedTxId(false), 2000);
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={completedTx ? 'Transfer Receipt' : 'Transfer Money'}
      description={
        completedTx
          ? 'Transaction recorded on immutable ledger.'
          : 'Instant settlement with cryptographic idempotency protection.'
      }
    >
      {completedTx ? (
        /* Success Receipt Screen */
        <div className="space-y-5">
          <div className="p-4 rounded-xl bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800 flex items-center space-x-3 text-emerald-800 dark:text-emerald-300">
            <CheckCircle2 className="w-8 h-8 flex-shrink-0 text-emerald-600 dark:text-emerald-400" />
            <div>
              <p className="font-semibold text-sm">Settlement Succeeded</p>
              <p className="text-xs text-emerald-600 dark:text-emerald-400">
                Transaction validated and committed via distributed ledger outbox.
              </p>
            </div>
          </div>

          <div className="space-y-2.5 rounded-lg border border-border p-4 bg-muted/30 text-xs">
            <div className="flex justify-between py-1 border-b">
              <span className="text-muted-foreground">Transaction ID:</span>
              <span className="font-mono text-foreground">{completedTx.id}</span>
            </div>
            <div className="flex justify-between items-center py-1 border-b">
              <span className="text-muted-foreground">Reference Code:</span>
              <div className="flex items-center space-x-1">
                <span className="font-mono text-foreground font-semibold">{completedTx.reference}</span>
                <button
                  type="button"
                  onClick={() => handleCopyTxReference(completedTx.reference)}
                  className="hover:text-foreground text-muted-foreground p-0.5"
                  title="Copy Reference"
                >
                  {copiedTxId ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                </button>
              </div>
            </div>
            <div className="flex justify-between py-1 border-b">
              <span className="text-muted-foreground">Recipient:</span>
              <span className="font-medium text-foreground">{receiverEmail}</span>
            </div>
            <div className="flex justify-between py-1 border-b">
              <span className="text-muted-foreground">Principal Amount:</span>
              <span className="font-bold text-foreground">{formatCurrency(completedTx.amount, completedTx.currency)}</span>
            </div>
            <div className="flex justify-between py-1 border-b">
              <span className="text-muted-foreground">Protocol Fee:</span>
              <span className="text-muted-foreground">{formatCurrency(completedTx.feeAmount, completedTx.currency)}</span>
            </div>
            <div className="flex justify-between py-1 pt-2 font-bold text-sm">
              <span>Total Debited:</span>
              <span className="text-primary">{formatCurrency(completedTx.grossAmount || completedTx.amount + completedTx.feeAmount, completedTx.currency)}</span>
            </div>
            <div className="flex justify-between py-1 text-muted-foreground text-[11px]">
              <span>Settled At:</span>
              <span>{formatDate(completedTx.createdAtUtc)}</span>
            </div>
          </div>

          <div className="flex justify-end space-x-2 pt-2">
            <Button variant="outline" size="sm" onClick={handleResetForNewTransfer}>
              Send Another
            </Button>
            <Button size="sm" onClick={onClose}>
              Done
            </Button>
          </div>
        </div>
      ) : (
        /* Transfer Input Form */
        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="p-3 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 flex items-center space-x-2 text-xs text-rose-800 dark:text-rose-300">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {/* Idempotency Key Banner */}
          <div className="p-2.5 rounded-lg bg-slate-100 dark:bg-slate-900/60 border border-slate-200 dark:border-slate-800 flex items-center justify-between text-xs">
            <div className="flex items-center space-x-1.5 overflow-hidden">
              <ShieldCheck className="w-3.5 h-3.5 text-primary shrink-0" />
              <span className="text-muted-foreground">Idempotency Guard:</span>
              <span className="font-mono text-foreground truncate max-w-[170px]" title={idempotencyKey}>
                {idempotencyKey}
              </span>
            </div>
            <button
              type="button"
              onClick={handleCopyIdempotencyKey}
              className="text-muted-foreground hover:text-foreground p-1 transition-colors"
              title="Copy Idempotency Key"
            >
              {copiedKey ? <Check className="w-3 h-3 text-emerald-500" /> : <Copy className="w-3 h-3" />}
            </button>
          </div>

          <div>
            <div className="flex justify-between items-center mb-1">
              <label className="block text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Recipient Email or Account Tag
              </label>
              {isVerifyingRecipient ? (
                <span className="text-xs text-muted-foreground animate-pulse">
                  Verifying recipient...
                </span>
              ) : recipientVerified ? (
                <span className="text-xs text-emerald-600 dark:text-emerald-400 font-medium flex items-center">
                  <UserCheck className="w-3 h-3 mr-1" />
                  Verified: {recipientVerified}
                </span>
              ) : null}
            </div>
            <Input
              type="email"
              placeholder="e.g. partner@payflow.local"
              value={receiverEmail}
              onChange={(e) => {
                setReceiverEmail(e.target.value);
                setError(null);
              }}
              required
              autoFocus
            />
          </div>

          <div>
            <div className="flex justify-between items-center mb-1">
              <label className="block text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Transfer Amount ({currency})
              </label>
              <span className="text-xs text-muted-foreground">
                Avail: {formatCurrency(availableBalance, currency)}
              </span>
            </div>
            <Input
              type="number"
              step="0.01"
              min="0.01"
              placeholder="0.00"
              value={amount}
              onChange={(e) => {
                setAmount(e.target.value);
                setError(null);
              }}
              leftIcon={<span className="text-sm font-semibold text-muted-foreground">$</span>}
              required
            />
          </div>

          <Input
            label="Memo / Reason for Transfer (Optional)"
            placeholder="e.g. Invoice #2045 or Dinner split"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />

          {user?.twoFactorEnabled && (
            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-1">
                Authenticator 2FA Code
              </label>
              <Input
                type="text"
                maxLength={6}
                placeholder="6-digit TOTP code"
                value={twoFactorCode}
                onChange={(e) => setTwoFactorCode(e.target.value.replace(/\D/g, ''))}
                leftIcon={<Lock className="w-4 h-4 text-muted-foreground" />}
                required
              />
            </div>
          )}

          {/* Live Fee Calculation Breakdown */}
          {feeQuote && parsedAmount > 0 && (
            <div className="p-3 bg-muted/40 rounded-lg space-y-1.5 text-xs border border-border">
              <div className="flex justify-between text-muted-foreground">
                <span>Transfer Amount:</span>
                <span className="font-medium text-foreground">{formatCurrency(feeQuote.amount, currency)}</span>
              </div>
              <div className="flex justify-between text-muted-foreground">
                <span className="flex items-center">
                  <span>Protocol Fee ({feeQuote.percentage}% + ${feeQuote.fixedFee}):</span>
                  <Zap className="w-3 h-3 text-amber-500 ml-1" />
                </span>
                <span className="font-medium text-foreground">{formatCurrency(feeQuote.feeAmount, currency)}</span>
              </div>
              <div className="flex justify-between font-bold pt-1 border-t text-foreground">
                <span>Total Liquidity Required:</span>
                <span className="text-primary">{formatCurrency(feeQuote.grossAmount, currency)}</span>
              </div>
            </div>
          )}

          <div className="flex justify-end space-x-2 pt-2">
            <Button
              type="button"
              variant="ghost"
              onClick={onClose}
              disabled={transferMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              isLoading={transferMutation.isPending}
              disabled={
                transferMutation.isPending ||
                !receiverEmail ||
                parsedAmount <= 0 ||
                totalDebit > availableBalance
              }
              className="gap-1.5"
            >
              <Send className="w-4 h-4" />
              Authorize & Send
            </Button>
          </div>
        </form>
      )}
    </Modal>
  );
};
