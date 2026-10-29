import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { formatCurrency, formatDate } from '@/lib/utils';
import { useAuth } from '@/context/AuthContext';
import { walletApi } from '@/api/wallet';
import { transferApi, FeeQuoteResponse } from '@/api/transfer';
import { TransferModal, generateUUID } from '@/components/wallet/TransferModal';
import { toast } from '@/components/ui/toast';
import {
  ArrowRight,
  CheckCircle2,
  ShieldCheck,
  Zap,
  Lock,
  Copy,
  Check,
  UserCheck,
  AlertCircle,
  ExternalLink,
} from 'lucide-react';
import type { Transaction } from '@/types';

export const Transfers: React.FC = () => {
  const { user } = useAuth();
  const queryClient = useQueryClient();

  const [recipient, setRecipient] = useState('');
  const [amount, setAmount] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [note, setNote] = useState('');
  const [twoFactorCode, setTwoFactorCode] = useState('');
  const [idempotencyKey, setIdempotencyKey] = useState<string>(() => generateUUID());
  const [copiedKey, setCopiedKey] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successReceipt, setSuccessReceipt] = useState<Transaction | null>(null);
  const [feeQuote, setFeeQuote] = useState<FeeQuoteResponse | null>(null);
  const [recipientName, setRecipientName] = useState<string | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);

  // Fetch live wallet balance
  const { data: walletData } = useQuery({
    queryKey: ['wallet'],
    queryFn: walletApi.getWallet,
    staleTime: 1000 * 30,
  });

  const availableBalance = walletData?.balance ?? 14500.5;

  // Debounced recipient verification
  useEffect(() => {
    if (!recipient || !recipient.includes('@')) {
      setRecipientName(null);
      return;
    }
    const timeout = setTimeout(async () => {
      const res = await transferApi.verifyRecipient(recipient);
      setRecipientName(res.exists ? res.name || recipient : null);
    }, 400);
    return () => clearTimeout(timeout);
  }, [recipient]);

  // Live fee quote
  const parsedAmount = parseFloat(amount) || 0;
  useEffect(() => {
    if (parsedAmount <= 0) {
      setFeeQuote(null);
      return;
    }
    let active = true;
    transferApi.getFeeQuote(parsedAmount, currency).then((quote) => {
      if (active) setFeeQuote(quote);
    });
    return () => {
      active = false;
    };
  }, [parsedAmount, currency]);

  const totalDeduction = feeQuote ? feeQuote.grossAmount : parsedAmount;

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
      setSuccessReceipt(tx);
      toast.success(
        'Transfer Completed!',
        `Sent ${formatCurrency(parsedAmount, currency)} to ${recipient}`
      );
      setRecipient('');
      setAmount('');
      setNote('');
      setTwoFactorCode('');
      setError(null);
      setIdempotencyKey(generateUUID());
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!recipient || parsedAmount <= 0) {
      setError('Please provide a valid recipient and amount.');
      return;
    }

    if (totalDeduction > availableBalance) {
      setError(
        `Insufficient funds. Available: ${formatCurrency(availableBalance, currency)}, Required: ${formatCurrency(totalDeduction, currency)}.`
      );
      return;
    }

    if (user?.twoFactorEnabled && !twoFactorCode) {
      setError('Two-factor authentication code is required.');
      return;
    }

    setError(null);
    transferMutation.mutate({
      receiverEmail: recipient.trim(),
      amount: parsedAmount,
      description: note.trim() || undefined,
      twoFactorCode: twoFactorCode.trim() || undefined,
    });
  };

  const handleCopyKey = () => {
    navigator.clipboard.writeText(idempotencyKey);
    setCopiedKey(true);
    setTimeout(() => setCopiedKey(false), 2000);
  };

  return (
    <div className="space-y-6 max-w-4xl mx-auto">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
            Transfer Funds
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            Send money securely across accounts with instant settlement and idempotency protection.
          </p>
        </div>

        <Button
          variant="outline"
          size="sm"
          onClick={() => setIsModalOpen(true)}
          className="gap-1.5 text-xs"
        >
          <ExternalLink className="w-3.5 h-3.5" />
          <span>Open Transfer Dialog</span>
        </Button>
      </div>

      {error && (
        <div className="p-4 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 flex items-center space-x-3 text-rose-800 dark:text-rose-300 text-sm">
          <AlertCircle className="w-5 h-5 flex-shrink-0" />
          <p className="font-medium">{error}</p>
        </div>
      )}

      {successReceipt && (
        <div className="p-4 rounded-xl bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800 space-y-2 text-emerald-800 dark:text-emerald-300">
          <div className="flex items-center space-x-2">
            <CheckCircle2 className="w-5 h-5 flex-shrink-0 text-emerald-600 dark:text-emerald-400" />
            <p className="font-semibold text-sm">
              Transfer Settled: {formatCurrency(successReceipt.amount, successReceipt.currency)}
            </p>
          </div>
          <div className="text-xs text-muted-foreground flex items-center space-x-4 pl-7">
            <span>Ref: {successReceipt.reference}</span>
            <span>ID: {successReceipt.id}</span>
            <span>Settled: {formatDate(successReceipt.createdAtUtc)}</span>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2">
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between">
                <div>
                  <CardTitle>Transfer Details</CardTitle>
                  <CardDescription>
                    Enter recipient information and transaction amount.
                  </CardDescription>
                </div>
                <div className="flex items-center space-x-1.5 text-xs text-muted-foreground bg-muted/60 px-2.5 py-1 rounded-md">
                  <ShieldCheck className="w-3.5 h-3.5 text-primary" />
                  <span className="font-mono truncate max-w-[120px]" title={idempotencyKey}>
                    {idempotencyKey}
                  </span>
                  <button
                    type="button"
                    onClick={handleCopyKey}
                    className="hover:text-foreground p-0.5"
                    title="Copy Idempotency Key"
                  >
                    {copiedKey ? <Check className="w-3 h-3 text-emerald-500" /> : <Copy className="w-3 h-3" />}
                  </button>
                </div>
              </div>
            </CardHeader>
            <CardContent>
              <form onSubmit={handleSubmit} className="space-y-4">
                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                    Source Wallet
                  </label>
                  <div className="p-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/50 flex justify-between items-center">
                    <div>
                      <p className="text-sm font-medium text-slate-800 dark:text-slate-200">
                        {user ? `${user.firstName} ${user.lastName}` : 'Primary Wallet'} ({currency})
                      </p>
                      <p className="text-xs text-slate-500">
                        Available: {formatCurrency(availableBalance, currency)}
                      </p>
                    </div>
                    <span className="text-xs font-semibold px-2 py-1 bg-emerald-100 text-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-400 rounded-md">
                      Active
                    </span>
                  </div>
                </div>

                <div>
                  <div className="flex justify-between items-center mb-1">
                    <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                      Recipient Email or Account Tag
                    </label>
                    {recipientName && (
                      <span className="text-xs text-emerald-600 dark:text-emerald-400 font-medium flex items-center">
                        <UserCheck className="w-3 h-3 mr-1" />
                        Verified: {recipientName}
                      </span>
                    )}
                  </div>
                  <Input
                    type="email"
                    placeholder="e.g. alice@payflow.local"
                    value={recipient}
                    onChange={(e) => setRecipient(e.target.value)}
                    required
                  />
                </div>

                <div className="grid grid-cols-3 gap-3">
                  <div className="col-span-2">
                    <Input
                      label="Amount"
                      type="number"
                      step="0.01"
                      min="0.01"
                      placeholder="0.00"
                      value={amount}
                      onChange={(e) => setAmount(e.target.value)}
                      required
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                      Currency
                    </label>
                    <select
                      value={currency}
                      onChange={(e) => setCurrency(e.target.value)}
                      className="w-full h-10 px-3 rounded-lg border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-500"
                    >
                      <option value="USD">USD ($)</option>
                      <option value="EUR">EUR (€)</option>
                      <option value="EGP">EGP (E£)</option>
                    </select>
                  </div>
                </div>

                <Input
                  label="Description / Reference (Optional)"
                  type="text"
                  placeholder="Invoice #1042 or Dinner split"
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                />

                {user?.twoFactorEnabled && (
                  <div>
                    <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                      Two-Factor Authentication Code
                    </label>
                    <Input
                      type="text"
                      maxLength={6}
                      placeholder="6-digit code"
                      value={twoFactorCode}
                      onChange={(e) => setTwoFactorCode(e.target.value.replace(/\D/g, ''))}
                      leftIcon={<Lock className="w-4 h-4 text-slate-400" />}
                      required
                    />
                  </div>
                )}

                <Button
                  type="submit"
                  className="w-full mt-4"
                  isLoading={transferMutation.isPending}
                  disabled={
                    transferMutation.isPending ||
                    !recipient ||
                    parsedAmount <= 0 ||
                    totalDeduction > availableBalance
                  }
                >
                  Confirm and Authorize Transfer
                  <ArrowRight className="w-4 h-4 ml-2" />
                </Button>
              </form>
            </CardContent>
          </Card>
        </div>

        <div>
          <Card>
            <CardHeader>
              <CardTitle>Transfer Summary</CardTitle>
              <CardDescription>Live fee calculation</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2 text-sm">
                <div className="flex justify-between py-1 text-slate-600 dark:text-slate-400">
                  <span>Transfer Amount</span>
                  <span className="font-semibold text-slate-900 dark:text-white">
                    {formatCurrency(parsedAmount, currency)}
                  </span>
                </div>
                <div className="flex justify-between py-1 text-slate-600 dark:text-slate-400">
                  <span className="flex items-center space-x-1">
                    <span>Protocol Fee</span>
                    <Zap className="w-3.5 h-3.5 text-amber-500" />
                  </span>
                  <span className="font-semibold text-slate-900 dark:text-white">
                    {formatCurrency(feeQuote ? feeQuote.feeAmount : 0, currency)}
                  </span>
                </div>
                <div className="border-t border-slate-200 dark:border-slate-800 pt-2 flex justify-between font-bold text-slate-900 dark:text-white">
                  <span>Total Debit</span>
                  <span className="text-primary-600 dark:text-primary-400">
                    {formatCurrency(totalDeduction, currency)}
                  </span>
                </div>
              </div>

              <div className="p-3 bg-slate-50 dark:bg-slate-900/60 rounded-lg space-y-2 border border-slate-200 dark:border-slate-800">
                <div className="flex items-center space-x-2 text-xs text-slate-600 dark:text-slate-300">
                  <ShieldCheck className="w-4 h-4 text-emerald-500 flex-shrink-0" />
                  <span>Idempotency-protected transaction guarantee.</span>
                </div>
                <div className="flex items-center space-x-2 text-xs text-slate-600 dark:text-slate-300">
                  <Zap className="w-4 h-4 text-primary-500 flex-shrink-0" />
                  <span>Settles in &lt; 250ms via Outbox message bus.</span>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>

      {/* Transfer Dialog Modal */}
      <TransferModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        availableBalance={availableBalance}
        currency={currency}
      />
    </div>
  );
};
