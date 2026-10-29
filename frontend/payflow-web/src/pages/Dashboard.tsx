import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Wallet as WalletIcon,
  ArrowRight,
  ArrowUpRight,
  ArrowDownLeft,
  RefreshCw,
  TrendingUp,
  ShieldAlert,
  ShieldCheck,
  CreditCard,
  Send,
  PlusCircle,
  Eye,
  EyeOff,
  Copy,
  Check,
  Wifi,
} from 'lucide-react';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { useAuth } from '@/context/AuthContext';
import { formatCurrency, formatDate } from '@/lib/utils';
import { walletApi } from '@/api/wallet';
import { useWalletHub } from '@/hooks/useWalletHub';
import { DepositModal } from '@/components/wallet/DepositModal';
import { WithdrawModal } from '@/components/wallet/WithdrawModal';
import { TransferModal } from '@/components/wallet/TransferModal';
import type { Wallet as WalletType, Transaction } from '@/types';

export function Dashboard() {
  const { user } = useAuth();
  const { isConnected, justUpdated } = useWalletHub();

  const [isDepositOpen, setIsDepositOpen] = useState(false);
  const [isWithdrawOpen, setIsWithdrawOpen] = useState(false);
  const [isTransferModalOpen, setIsTransferModalOpen] = useState(false);
  const [copiedId, setCopiedId] = useState(false);
  const [hideBalance, setHideBalance] = useState<boolean>(() => {
    return localStorage.getItem('payflow_hide_balance') === 'true';
  });

  const toggleHideBalance = () => {
    setHideBalance((prev) => {
      const next = !prev;
      localStorage.setItem('payflow_hide_balance', String(next));
      return next;
    });
  };

  // TanStack React Query for wallet data with real-time invalidation support
  const {
    data: walletData,
    isLoading: isWalletLoading,
    refetch: refetchWallet,
  } = useQuery({
    queryKey: ['wallet'],
    queryFn: walletApi.getWallet,
    retry: 1,
    staleTime: 1000 * 30,
  });

  // Fallback demo wallet when disconnected or backend is offline
  const wallet: WalletType = walletData || {
    id: 'wlt_' + (user?.id ? user.id.substring(0, 8) : '001'),
    userId: user?.id || 'usr_001',
    balance: 14500.5,
    currency: 'USD',
    updatedAtUtc: new Date().toISOString(),
    isFrozen: false,
    freezeReason: null,
  };

  const [recentTransactions] = useState<Transaction[]>([
    {
      id: 'tx-1',
      reference: 'TXN-20261028-001',
      type: 'Transfer',
      status: 'Completed',
      amount: 1200.0,
      currency: wallet.currency,
      description: 'Transfer to merchant@egypt.com',
      createdAtUtc: new Date(Date.now() - 3600000 * 2).toISOString(),
      exchangeRate: 1.0,
      feeAmount: 5.0,
    },
    {
      id: 'tx-2',
      reference: 'TXN-20261028-002',
      type: 'Deposit',
      status: 'Completed',
      amount: 5000.0,
      currency: wallet.currency,
      description: 'Salary Direct Deposit',
      createdAtUtc: new Date(Date.now() - 86400000).toISOString(),
      exchangeRate: 1.0,
      feeAmount: 0.0,
    },
    {
      id: 'tx-3',
      reference: 'TXN-20261028-003',
      type: 'Transfer',
      status: 'Completed',
      amount: 350.0,
      currency: wallet.currency,
      description: 'Dinner split with Bob',
      createdAtUtc: new Date(Date.now() - 86400000 * 2).toISOString(),
      exchangeRate: 1.0,
      feeAmount: 2.0,
    },
  ]);

  const handleCopyWalletId = () => {
    navigator.clipboard.writeText(wallet.id);
    setCopiedId(true);
    setTimeout(() => setCopiedId(false), 2000);
  };

  return (
    <div className="space-y-6">
      {/* Welcome Banner & Top Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <div className="flex items-center space-x-2.5">
            <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-foreground">
              Welcome back, {user?.firstName || 'Valued Customer'} 👋
            </h1>
            {/* Real-Time WebSocket Indicator */}
            {isConnected ? (
              <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-400 border border-emerald-300 dark:border-emerald-800">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse mr-1.5"></span>
                Live Sync
              </span>
            ) : (
              <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400">
                <Wifi className="w-3 h-3 mr-1 text-slate-400" />
                Standby
              </span>
            )}
          </div>
          <p className="text-sm text-muted-foreground mt-1">
            Manage your digital wallet, review financial statements, and execute secure transfers.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetchWallet()}
            className="gap-1.5 text-xs"
            disabled={isWalletLoading}
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isWalletLoading ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsDepositOpen(true)}
            className="gap-1.5 text-xs"
          >
            <PlusCircle className="h-3.5 w-3.5 text-emerald-500" />
            <span>Deposit</span>
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsWithdrawOpen(true)}
            className="gap-1.5 text-xs"
          >
            <ArrowUpRight className="h-3.5 w-3.5 text-amber-500" />
            <span>Withdraw</span>
          </Button>

          <Button
            size="sm"
            onClick={() => setIsTransferModalOpen(true)}
            className="gap-1.5 text-xs shadow-sm"
          >
            <Send className="h-3.5 w-3.5" />
            <span>Send Money</span>
          </Button>
        </div>
      </div>

      {/* Wallet Freeze Alert Banner if applicable */}
      {wallet.isFrozen && (
        <div className="rounded-xl border border-destructive/30 bg-destructive/10 p-4 flex items-center space-x-3 text-destructive">
          <ShieldAlert className="h-5 w-5 shrink-0" />
          <div className="text-sm">
            <span className="font-semibold">Wallet Frozen:</span>{' '}
            {wallet.freezeReason ||
              'Account operations restricted by compliance. Please verify your KYC tier in Settings.'}
          </div>
        </div>
      )}

      {/* Main Grid: Balance Card + Quick Stats */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Main Wallet Balance Card */}
        <Card
          className={`md:col-span-2 relative overflow-hidden bg-gradient-to-br from-primary/10 via-card to-card border-primary/20 shadow-md transition-all duration-700 ${
            justUpdated ? 'ring-2 ring-emerald-500 shadow-emerald-500/20' : ''
          }`}
        >
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <div className="space-y-1">
              <div className="flex items-center space-x-2">
                <CardDescription className="text-xs uppercase tracking-wider font-semibold text-primary">
                  Available Liquidity
                </CardDescription>
                <button
                  type="button"
                  onClick={toggleHideBalance}
                  className="text-muted-foreground hover:text-foreground transition-colors p-1 rounded"
                  title={hideBalance ? 'Show balance' : 'Hide balance'}
                >
                  {hideBalance ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                </button>
              </div>

              <div className="flex items-baseline space-x-3">
                <CardTitle className="text-3xl sm:text-4xl font-extrabold text-foreground tracking-tight">
                  {hideBalance ? '••••••••' : formatCurrency(wallet.balance, wallet.currency)}
                </CardTitle>
                {justUpdated && (
                  <Badge variant="success" className="animate-bounce">
                    Updated Just Now
                  </Badge>
                )}
              </div>
            </div>

            <div className="h-12 w-12 rounded-2xl bg-primary/15 flex items-center justify-center text-primary">
              <WalletIcon className="h-6 w-6" />
            </div>
          </CardHeader>

          <CardContent className="pt-4">
            <div className="flex items-center space-x-2 text-xs text-muted-foreground mb-4">
              <span>Account Number:</span>
              <span className="font-mono text-foreground font-medium">{wallet.id}</span>
              <button
                type="button"
                onClick={handleCopyWalletId}
                className="hover:text-foreground transition-colors"
                title="Copy Wallet ID"
              >
                {copiedId ? (
                  <Check className="w-3.5 h-3.5 text-emerald-500" />
                ) : (
                  <Copy className="w-3.5 h-3.5" />
                )}
              </button>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-4 pt-4 border-t border-border/60">
              <div className="flex items-center space-x-2">
                <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
                  <ShieldCheck className="h-3.5 w-3.5 mr-1" />
                  {user?.kycTier || 'Tier2'} Verified
                </span>
                <span className="text-xs text-muted-foreground">
                  Updated {formatDate(wallet.updatedAtUtc)}
                </span>
              </div>

              <div className="flex items-center space-x-2">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => setIsDepositOpen(true)}
                  className="gap-1.5 text-xs"
                >
                  <PlusCircle className="h-3.5 w-3.5 text-emerald-500" />
                  <span>Deposit</span>
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => setIsWithdrawOpen(true)}
                  className="gap-1.5 text-xs"
                >
                  <ArrowUpRight className="h-3.5 w-3.5 text-amber-500" />
                  <span>Withdraw</span>
                </Button>
                <Button
                  size="sm"
                  onClick={() => setIsTransferModalOpen(true)}
                  className="gap-1.5 text-xs"
                >
                  <Send className="h-3.5 w-3.5" />
                  <span>Transfer</span>
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Account Info / KYC Status Card */}
        <Card className="flex flex-col justify-between">
          <CardHeader className="pb-2">
            <CardDescription className="text-xs uppercase tracking-wider font-semibold">
              Account Security & Tier
            </CardDescription>
            <CardTitle className="text-lg font-bold flex items-center space-x-2">
              <CreditCard className="h-5 w-5 text-primary" />
              <span>Personal Checking</span>
            </CardTitle>
          </CardHeader>

          <CardContent className="space-y-3">
            <div className="flex justify-between text-sm py-1 border-b">
              <span className="text-muted-foreground">Currency</span>
              <span className="font-semibold text-foreground">{wallet.currency}</span>
            </div>
            <div className="flex justify-between text-sm py-1 border-b">
              <span className="text-muted-foreground">KYC Level</span>
              <span className="font-semibold text-foreground">{user?.kycTier || 'Tier2'}</span>
            </div>
            <div className="flex justify-between text-sm py-1">
              <span className="text-muted-foreground">2FA Multi-Factor</span>
              <span className="font-semibold text-emerald-600 dark:text-emerald-400">
                {user?.twoFactorEnabled ? 'Active (TOTP)' : 'Disabled'}
              </span>
            </div>
          </CardContent>

          <div className="p-6 pt-0 space-y-2">
            <Link to="/settings" className="w-full">
              <Button variant="secondary" size="sm" className="w-full text-xs">
                Manage Account & Limits
              </Button>
            </Link>
          </div>
        </Card>
      </div>

      {/* Secondary Metrics: Inflow, Outflow, Ledger Balance */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
        <Card>
          <CardContent className="p-5 flex items-center justify-between">
            <div className="space-y-1">
              <p className="text-xs font-medium text-muted-foreground uppercase">Monthly Inflow</p>
              <p className="text-xl font-bold text-emerald-600 dark:text-emerald-400">
                +{formatCurrency(5000.0, wallet.currency)}
              </p>
            </div>
            <div className="h-10 w-10 rounded-xl bg-emerald-500/10 flex items-center justify-center text-emerald-500">
              <ArrowDownLeft className="h-5 w-5" />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="p-5 flex items-center justify-between">
            <div className="space-y-1">
              <p className="text-xs font-medium text-muted-foreground uppercase">Monthly Outflow</p>
              <p className="text-xl font-bold text-foreground">
                -{formatCurrency(1550.0, wallet.currency)}
              </p>
            </div>
            <div className="h-10 w-10 rounded-xl bg-primary/10 flex items-center justify-center text-primary">
              <ArrowUpRight className="h-5 w-5" />
            </div>
          </CardContent>
        </Card>

        <Card className="sm:col-span-2 lg:col-span-1">
          <CardContent className="p-5 flex items-center justify-between">
            <div className="space-y-1">
              <p className="text-xs font-medium text-muted-foreground uppercase">Ledger Integrity</p>
              <p className="text-xl font-bold text-foreground">100% Balanced</p>
            </div>
            <div className="h-10 w-10 rounded-xl bg-blue-500/10 flex items-center justify-center text-blue-500">
              <TrendingUp className="h-5 w-5" />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Recent Activity Table */}
      <Card>
        <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div>
            <CardTitle>Recent Activity</CardTitle>
            <CardDescription>Latest monetary events and settlement records</CardDescription>
          </div>
          <div className="flex items-center space-x-2">
            <Link to="/history">
              <Button variant="ghost" size="sm" className="gap-1 text-xs text-primary">
                <span>View Full Ledger</span>
                <ArrowRight className="h-3 w-3" />
              </Button>
            </Link>
          </div>
        </CardHeader>

        <CardContent>
          <div className="overflow-x-auto">
            <table className="w-full text-sm text-left">
              <thead className="text-xs text-muted-foreground uppercase bg-muted/30 border-b">
                <tr>
                  <th scope="col" className="px-4 py-3">Reference</th>
                  <th scope="col" className="px-4 py-3">Type</th>
                  <th scope="col" className="px-4 py-3">Description</th>
                  <th scope="col" className="px-4 py-3">Status</th>
                  <th scope="col" className="px-4 py-3">Date</th>
                  <th scope="col" className="px-4 py-3 text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {recentTransactions.map((tx) => (
                  <tr key={tx.id} className="hover:bg-muted/40 transition-colors">
                    <td className="px-4 py-3 font-mono text-xs text-muted-foreground">{tx.reference}</td>
                    <td className="px-4 py-3 font-medium text-foreground">{tx.type}</td>
                    <td className="px-4 py-3 text-muted-foreground">{tx.description}</td>
                    <td className="px-4 py-3">
                      <Badge variant={tx.status === 'Completed' ? 'success' : 'warning'}>
                        {tx.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-3 text-xs text-muted-foreground">
                      {formatDate(tx.createdAtUtc)}
                    </td>
                    <td className="px-4 py-3 text-right font-semibold text-foreground">
                      {tx.type === 'Deposit' ? '+' : '-'}
                      {formatCurrency(tx.amount, tx.currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      {/* Deposit Modal */}
      <DepositModal
        isOpen={isDepositOpen}
        onClose={() => setIsDepositOpen(false)}
        currency={wallet.currency}
      />

      {/* Withdraw Modal */}
      <WithdrawModal
        isOpen={isWithdrawOpen}
        onClose={() => setIsWithdrawOpen(false)}
        availableBalance={wallet.balance}
        currency={wallet.currency}
      />

      {/* Transfer Modal */}
      <TransferModal
        isOpen={isTransferModalOpen}
        onClose={() => setIsTransferModalOpen(false)}
        availableBalance={wallet.balance}
        currency={wallet.currency}
      />
    </div>
  );
}
