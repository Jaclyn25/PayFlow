import React, { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { walletApi } from '@/api/wallet';
import { toast } from '@/components/ui/toast';
import { formatCurrency } from '@/lib/utils';
import { ArrowUpRight, AlertCircle, Building2 } from 'lucide-react';

interface WithdrawModalProps {
  isOpen: boolean;
  onClose: () => void;
  availableBalance: number;
  currency?: string;
  onSuccess?: () => void;
}

export const WithdrawModal: React.FC<WithdrawModalProps> = ({
  isOpen,
  onClose,
  availableBalance,
  currency = 'USD',
  onSuccess,
}) => {
  const queryClient = useQueryClient();
  const [amount, setAmount] = useState<string>('');
  const [destinationAccount, setDestinationAccount] = useState<string>('');
  const [description, setDescription] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  const withdrawMutation = useMutation({
    mutationFn: (data: { amount: number; description?: string }) =>
      walletApi.withdraw(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['wallet'] });
      queryClient.invalidateQueries({ queryKey: ['transactions'] });
      toast.success(
        'Withdrawal Initiated',
        `${formatCurrency(parseFloat(amount), currency)} is being routed to your destination account.`
      );
      setAmount('');
      setDestinationAccount('');
      setDescription('');
      setError(null);
      onClose();
      onSuccess?.();
    },
    onError: (err: any) => {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.title ||
        err?.message ||
        'Failed to process withdrawal. Please verify your available balance.';
      setError(msg);
    },
  });

  const handleMaxClick = () => {
    setAmount(availableBalance.toString());
    setError(null);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const parsed = parseFloat(amount);
    if (!parsed || parsed <= 0) {
      setError('Please enter an amount greater than 0.');
      return;
    }

    if (parsed > availableBalance) {
      setError(
        `Insufficient funds. Your available balance is ${formatCurrency(availableBalance, currency)}.`
      );
      return;
    }

    setError(null);
    withdrawMutation.mutate({
      amount: parsed,
      description: destinationAccount.trim()
        ? `Withdrawal to ${destinationAccount.trim()} - ${description}`.trim()
        : description.trim() || 'Direct Account Withdrawal',
    });
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Withdraw Funds"
      description="Withdraw balance from your digital wallet to your linked bank account."
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <div className="p-3 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 flex items-center space-x-2 text-xs text-rose-800 dark:text-rose-300">
            <AlertCircle className="w-4 h-4 flex-shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div className="p-3 rounded-lg bg-muted/60 border border-border flex justify-between items-center text-xs">
          <span className="text-muted-foreground">Available to Withdraw:</span>
          <span className="font-bold text-foreground">
            {formatCurrency(availableBalance, currency)}
          </span>
        </div>

        <div>
          <div className="flex justify-between items-center mb-1">
            <label className="block text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Withdrawal Amount ({currency})
            </label>
            <button
              type="button"
              onClick={handleMaxClick}
              className="text-xs font-bold text-primary hover:underline focus:outline-none"
            >
              Use Max
            </button>
          </div>
          <Input
            type="number"
            step="0.01"
            min="0.01"
            max={availableBalance}
            placeholder="0.00"
            value={amount}
            onChange={(e) => {
              setAmount(e.target.value);
              setError(null);
            }}
            leftIcon={<span className="text-sm font-semibold text-muted-foreground">$</span>}
            required
            autoFocus
          />
        </div>

        <Input
          label="Destination IBAN / Bank Account"
          placeholder="e.g. EG92000200000001234567890 or Checking ...8910"
          value={destinationAccount}
          onChange={(e) => setDestinationAccount(e.target.value)}
          leftIcon={<Building2 className="w-4 h-4 text-muted-foreground" />}
          required
        />

        <Input
          label="Internal Reference / Memo (Optional)"
          placeholder="e.g. Dividend transfer, Payroll payout"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />

        <div className="flex justify-end space-x-2 pt-2">
          <Button type="button" variant="ghost" onClick={onClose} disabled={withdrawMutation.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            isLoading={withdrawMutation.isPending}
            disabled={withdrawMutation.isPending || !amount || parseFloat(amount) <= 0}
            className="gap-1.5"
          >
            <ArrowUpRight className="w-4 h-4" />
            Authorize Withdrawal
          </Button>
        </div>
      </form>
    </Modal>
  );
};
