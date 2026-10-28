import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Wallet,
  ArrowUpRight,
  ArrowDownLeft,
  RefreshCw,
  TrendingUp,
  ShieldAlert,
  ShieldCheck,
  CreditCard,
  Send,
  PlusCircle,
} from 'lucide-react';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { useAuth } from '@/context/AuthContext';
import { formatCurrency, formatDate } from '@/lib/utils';
import type { Wallet as WalletType, Transaction } from '@/types';

export function Dashboard() {
  const { user } = useAuth();

  // Demo fallback state while API connects
  const [wallet] = useState<WalletType>({
    id: 'wallet-001',
    userId: user?.id || 'user-001',
    balance: 14500.50,
    currency: 'EGP',
    updatedAtUtc: new Date().toISOString(),
    isFrozen: false,
  });

  const [recentTransactions] = useState<Transaction[]>([
    {
      id: 'tx-1',
      reference: 'TXN-20261028-001',
      type: 'Transfer',
      status: 'Completed',
      amount: 1200.00,
      currency: 'EGP',
      description: 'Transfer to merchant@egypt.com',
      createdAtUtc: new Date(Date.now() - 3600000 * 2).toISOString(),
      exchangeRate: 1.0,
      feeAmount: 5.00,
    },
    {
      id: 'tx-2',
      reference: 'TXN-20261028-002',
      type: 'Deposit',
      status: 'Completed',
      amount: 5000.00,
      currency: 'EGP',
      description: 'Salary Direct Deposit',
      createdAtUtc: new Date(Date.now() - 86400000).toISOString(),
      exchangeRate: 1.0,
      feeAmount: 0.00,
    },
    {
      id: 'tx-3',
      reference: 'TXN-20261028-003',
      type: 'Transfer',
      status: 'Completed',
      amount: 350.00,
      currency: 'EGP',
      description: 'Dinner split with Bob',
      createdAtUtc: new Date(Date.now() - 86400000 * 2).toISOString(),
      exchangeRate: 1.0,
      feeAmount: 2.00,
    },
  ]);

  return (
    <div className="space-y-6">
      {/* Welcome Banner */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-foreground">
            Welcome back, {user?.firstName || 'Valued Customer'} 👋
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            Manage your digital wallet, review financial statements, and execute secure transfers.
          </p>
        </div>

        <div className="flex items-center space-x-2.5">
          <Link to="/transfers">
            <Button className="gap-2 shadow-sm">
              <Send className="h-4 w-4" />
              <span>Send Money</span>
            </Button>
          </Link>
        </div>
      </div>

      {/* Wallet Freeze Alert Banner if applicable */}
      {wallet.isFrozen && (
        <div className="rounded-xl border border-destructive/30 bg-destructive/10 p-4 flex items-center space-x-3 text-destructive">
          <ShieldAlert className="h-5 w-5 shrink-0" />
          <div className="text-sm">
            <span className="font-semibold">Wallet Locked:</span> {wallet.freezeReason || 'Account operations restricted for security reasons. Contact support.'}
          </div>
        </div>
      )}

      {/* Main Grid: Balance Card + Quick Stats */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Main Wallet Balance Card */}
        <Card className="md:col-span-2 relative overflow-hidden bg-gradient-to-br from-primary/10 via-card to-card border-primary/20 shadow-md">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <div className="space-y-1">
              <CardDescription className="text-xs uppercase tracking-wider font-semibold text-primary">
                Available Liquidity
              </CardDescription>
              <CardTitle className="text-3xl sm:text-4xl font-extrabold text-foreground tracking-tight">
                {formatCurrency(wallet.balance, wallet.currency)}
              </CardTitle>
            </div>
            <div className="h-12 w-12 rounded-2xl bg-primary/15 flex items-center justify-center text-primary">
              <Wallet className="h-6 w-6" />
            </div>
          </CardHeader>

          <CardContent className="pt-4">
            <div className="flex flex-wrap items-center justify-between gap-4 pt-4 border-t border-border/60">
              <div className="flex items-center space-x-2">
                <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
                  <ShieldCheck className="h-3.5 w-3.5 mr-1" />
                  {user?.kycTier || 'Tier1'} Verified
                </span>
                <span className="text-xs text-muted-foreground">
                  Updated {formatDate(wallet.updatedAtUtc)}
                </span>
              </div>

              <div className="flex items-center space-x-2">
                <Link to="/transfers">
                  <Button size="sm" variant="outline" className="gap-1.5 text-xs">
                    <ArrowUpRight className="h-3.5 w-3.5 text-primary" />
                    <span>Transfer</span>
                  </Button>
                </Link>
                <Button size="sm" variant="outline" className="gap-1.5 text-xs">
                  <PlusCircle className="h-3.5 w-3.5 text-emerald-500" />
                  <span>Deposit</span>
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
              <span className="font-semibold text-foreground">{user?.kycTier || 'Tier1'}</span>
            </div>
            <div className="flex justify-between text-sm py-1">
              <span className="text-muted-foreground">2FA Verification</span>
              <span className="font-semibold text-emerald-600 dark:text-emerald-400">
                {user?.twoFactorEnabled ? 'Enabled' : 'Disabled'}
              </span>
            </div>
          </CardContent>

          <div className="p-6 pt-0">
            <Link to="/settings" className="w-full">
              <Button variant="secondary" size="sm" className="w-full text-xs">
                Manage Account & Limits
              </Button>
            </Link>
          </div>
        </Card>
      </div>

      {/* Secondary Metrics: Inflow, Outflow, Quick Performance */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
        <Card>
          <CardContent className="p-5 flex items-center justify-between">
            <div className="space-y-1">
              <p className="text-xs font-medium text-muted-foreground uppercase">Monthly Inflow</p>
              <p className="text-xl font-bold text-emerald-600 dark:text-emerald-400">+5,000.00 EGP</p>
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
              <p className="text-xl font-bold text-foreground">-1,550.00 EGP</p>
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
        <CardHeader className="flex flex-row items-center justify-between">
          <div>
            <CardTitle>Recent Activity</CardTitle>
            <CardDescription>Latest monetary events and settlement records</CardDescription>
          </div>
          <Link to="/history">
            <Button variant="ghost" size="sm" className="gap-1 text-xs text-primary">
              <span>View All</span>
              <RefreshCw className="h-3 w-3" />
            </Button>
          </Link>
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
                      <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
                        {tx.status}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-xs text-muted-foreground">{formatDate(tx.createdAtUtc)}</td>
                    <td className="px-4 py-3 text-right font-semibold text-foreground">
                      {tx.type === 'Deposit' ? '+' : '-'}{formatCurrency(tx.amount, tx.currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
