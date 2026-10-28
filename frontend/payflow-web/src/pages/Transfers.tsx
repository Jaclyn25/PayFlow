import React, { useState } from 'react';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ArrowRight, CheckCircle2, ShieldCheck, Zap } from 'lucide-react';
import { formatCurrency } from '@/lib/utils';
import { useAuth } from '@/context/AuthContext';

export const Transfers: React.FC = () => {
  const { user } = useAuth();
  const [recipient, setRecipient] = useState('');
  const [amount, setAmount] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [note, setNote] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const parsedAmount = parseFloat(amount) || 0;
  const estimatedFee = parsedAmount > 0 ? Math.max(0.25, parsedAmount * 0.005) : 0;
  const totalDeduction = parsedAmount + estimatedFee;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!recipient || parsedAmount <= 0) return;

    setIsSubmitting(true);
    setSuccessMessage(null);

    // Mock transfer submission for foundation UI
    setTimeout(() => {
      setIsSubmitting(false);
      setSuccessMessage(
        `Successfully transferred ${formatCurrency(parsedAmount, currency)} to ${recipient}`
      );
      setRecipient('');
      setAmount('');
      setNote('');
    }, 1200);
  };

  return (
    <div className="space-y-6 max-w-4xl mx-auto">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
          Transfer Funds
        </h1>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Send money securely across domestic and international accounts with real-time settlement.
        </p>
      </div>

      {successMessage && (
        <div className="p-4 rounded-xl bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800 flex items-center space-x-3 text-emerald-800 dark:text-emerald-300">
          <CheckCircle2 className="w-5 h-5 flex-shrink-0" />
          <p className="text-sm font-medium">{successMessage}</p>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2">
          <Card>
            <CardHeader>
              <CardTitle>Transfer Details</CardTitle>
              <CardDescription>
                Enter recipient information and transaction amount.
              </CardDescription>
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
                        {user ? `${user.firstName} ${user.lastName}` : 'Primary Wallet'} (USD)
                      </p>
                      <p className="text-xs text-slate-500">Available: $24,580.50</p>
                    </div>
                    <span className="text-xs font-semibold px-2 py-1 bg-emerald-100 text-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-400 rounded-md">
                      Active
                    </span>
                  </div>
                </div>

                <Input
                  label="Recipient Email or Account Tag"
                  type="text"
                  placeholder="e.g. alice@payflow.local or user_928341"
                  value={recipient}
                  onChange={(e) => setRecipient(e.target.value)}
                  required
                />

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
                      <option value="GBP">GBP (£)</option>
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

                <Button
                  type="submit"
                  className="w-full mt-4"
                  isLoading={isSubmitting}
                  disabled={isSubmitting || !recipient || parsedAmount <= 0}
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
                    {formatCurrency(estimatedFee, currency)}
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
    </div>
  );
};
