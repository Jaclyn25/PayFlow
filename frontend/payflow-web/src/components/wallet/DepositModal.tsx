import React, { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { walletApi } from '@/api/wallet';
import { toast } from '@/components/ui/toast';
import { formatCurrency } from '@/lib/utils';
import { ArrowDownLeft, AlertCircle, CreditCard } from 'lucide-react';

interface DepositModalProps {
  isOpen: boolean;
  onClose: () => void;
  currency?: string;
  onSuccess?: () => void;
}

const PRESET_AMOUNTS = [100, 500, 1000, 5000];

export const DepositModal: React.FC<DepositModalProps> = ({
  isOpen,
  onClose,
  currency = 'USD',
  onSuccess,
}) => {
  const queryClient = useQueryClient();
  const [amount, setAmount] = useState<string>('');
  const [description, setDescription] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  const depositMutation = useMutation({
    mutationFn: (data: { amount: number; description?: string }) =>
      walletApi.deposit(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['wallet'] });
      queryClient.invalidateQueries({ queryKey: ['transactions'] });
      toast.success(
        'Deposit Successful!',
        `${formatCurrency(parseFloat(amount), currency)} has been credited to your wallet.`
      );
      setAmount('');
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
        'Failed to process deposit. Please try again.';
      setError(msg);
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const parsed = parseFloat(amount);
    if (!parsed || parsed <= 0) {
      setError('Please enter a valid deposit amount greater than 0.');
      return;
    }

    setError(null);
    depositMutation.mutate({
      amount: parsed,
      description: description.trim() || 'Instant Test Deposit',
    });
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Deposit Funds"
      description="Add funds instantly to your digital ledger wallet account."
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <div className="p-3 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 flex items-center space-x-2 text-xs text-rose-800 dark:text-rose-300">
            <AlertCircle className="w-4 h-4 flex-shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div>
          <label className="block text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-1">
            Deposit Amount ({currency})
          </label>
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
            autoFocus
          />
        </div>

        {/* Quick Presets */}
        <div>
          <span className="text-xs text-muted-foreground mb-1.5 block">Quick select amount:</span>
          <div className="grid grid-cols-4 gap-2">
            {PRESET_AMOUNTS.map((val) => (
              <Button
                key={val}
                type="button"
                variant="outline"
                size="sm"
                className="text-xs h-8"
                onClick={() => {
                  setAmount(val.toString());
                  setError(null);
                }}
              >
                +${val.toLocaleString()}
              </Button>
            ))}
          </div>
        </div>

        <Input
          label="Reference Note (Optional)"
          placeholder="e.g. Account Top-Up, Testing"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />

        <div className="p-3 bg-muted/50 rounded-lg flex items-center space-x-2.5 text-xs text-muted-foreground">
          <CreditCard className="w-4 h-4 text-primary shrink-0" />
          <span>Settlement is instantaneous and idempotency guaranteed.</span>
        </div>

        <div className="flex justify-end space-x-2 pt-2">
          <Button type="button" variant="ghost" onClick={onClose} disabled={depositMutation.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            isLoading={depositMutation.isPending}
            disabled={depositMutation.isPending || !amount || parseFloat(amount) <= 0}
            className="gap-1.5"
          >
            <ArrowDownLeft className="w-4 h-4" />
            Confirm Deposit
          </Button>
        </div>
      </form>
    </Modal>
  );
};
