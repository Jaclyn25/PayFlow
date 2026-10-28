import React, { useState } from 'react';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Transaction } from '@/types';
import { formatCurrency, formatDate } from '@/lib/utils';
import { Search, Download, ArrowUpRight, ArrowDownLeft, ChevronLeft, ChevronRight } from 'lucide-react';

const MOCK_HISTORY: Transaction[] = [
  {
    id: 'tx_98124',
    reference: 'REF-TX-98124',
    amount: 1450.0,
    currency: 'USD',
    type: 'Transfer',
    status: 'Completed',
    recipient: 'Acme Cloud Hosting Corp',
    description: 'Monthly dedicated cluster infrastructure payment',
    createdAtUtc: new Date(Date.now() - 1000 * 60 * 30).toISOString(),
    feeAmount: 1.5,
    exchangeRate: 1.0,
  },
  {
    id: 'tx_98123',
    reference: 'REF-TX-98123',
    amount: 5200.0,
    currency: 'USD',
    type: 'Deposit',
    status: 'Completed',
    sender: 'Stripe Merchant Payout',
    description: 'Batch wire settlement #9012',
    createdAtUtc: new Date(Date.now() - 1000 * 60 * 60 * 4).toISOString(),
    feeAmount: 0,
    exchangeRate: 1.0,
  },
  {
    id: 'tx_98122',
    reference: 'REF-TX-98122',
    amount: 320.0,
    currency: 'USD',
    type: 'Transfer',
    status: 'Completed',
    recipient: 'Sarah Connor',
    description: 'Contractor frontend styling compensation',
    createdAtUtc: new Date(Date.now() - 1000 * 60 * 60 * 24).toISOString(),
    feeAmount: 0.5,
    exchangeRate: 1.0,
  },
  {
    id: 'tx_98121',
    reference: 'REF-TX-98121',
    amount: 10000.0,
    currency: 'USD',
    type: 'Transfer',
    status: 'Pending',
    recipient: 'Escrow Partner Ltd',
    description: 'Series funding deployment validation',
    createdAtUtc: new Date(Date.now() - 1000 * 60 * 60 * 30).toISOString(),
    feeAmount: 5.0,
    exchangeRate: 1.0,
  },
  {
    id: 'tx_98120',
    reference: 'REF-TX-98120',
    amount: 75.5,
    currency: 'USD',
    type: 'Transfer',
    status: 'Failed',
    recipient: 'Bad Account Identifier',
    description: 'Insufficient destination route mapping',
    createdAtUtc: new Date(Date.now() - 1000 * 60 * 60 * 48).toISOString(),
    feeAmount: 0,
    exchangeRate: 1.0,
  },
];

export const History: React.FC = () => {
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [currentPage, setCurrentPage] = useState(1);

  const filteredTransactions = MOCK_HISTORY.filter((tx) => {
    const matchesSearch =
      tx.id.toLowerCase().includes(searchTerm.toLowerCase()) ||
      tx.description?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      tx.recipient?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      tx.sender?.toLowerCase().includes(searchTerm.toLowerCase());

    const matchesStatus =
      statusFilter === 'all' || tx.status.toLowerCase() === statusFilter.toLowerCase();

    return matchesSearch && matchesStatus;
  });

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
            Ledger & Transaction History
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            Audit-grade double-entry transaction record with immutable ledger verification.
          </p>
        </div>

        <div className="flex items-center space-x-2">
          <Button variant="outline" size="sm" onClick={() => alert('Exporting CSV...')}>
            <Download className="w-4 h-4 mr-2" />
            Export CSV
          </Button>
        </div>
      </div>

      <Card>
        <CardHeader>
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <CardTitle>Transactions</CardTitle>
              <CardDescription>
                Showing {filteredTransactions.length} of {MOCK_HISTORY.length} recorded entries
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-3">
              <div className="w-64">
                <Input
                  placeholder="Search by ID, recipient, note..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  leftIcon={<Search className="w-4 h-4 text-slate-400" />}
                />
              </div>
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="h-10 px-3 rounded-lg border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-500"
              >
                <option value="all">All Statuses</option>
                <option value="completed">Completed</option>
                <option value="pending">Pending</option>
                <option value="failed">Failed</option>
              </select>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse text-sm">
              <thead>
                <tr className="border-b border-slate-200 dark:border-slate-800 text-xs uppercase font-semibold text-slate-500 dark:text-slate-400">
                  <th className="py-3 px-4">Transaction ID</th>
                  <th className="py-3 px-4">Date & Time</th>
                  <th className="py-3 px-4">Counterparty / Note</th>
                  <th className="py-3 px-4">Type</th>
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4 text-right">Fee</th>
                  <th className="py-3 px-4 text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-slate-800/60">
                {filteredTransactions.map((tx) => (
                  <tr
                    key={tx.id}
                    className="hover:bg-slate-50/50 dark:hover:bg-slate-900/50 transition-colors"
                  >
                    <td className="py-3.5 px-4 font-mono text-xs text-slate-600 dark:text-slate-300">
                      {tx.id}
                    </td>
                    <td className="py-3.5 px-4 text-slate-500 dark:text-slate-400 whitespace-nowrap">
                      {formatDate(tx.createdAtUtc || tx.createdAt || '')}
                    </td>
                    <td className="py-3.5 px-4">
                      <p className="font-medium text-slate-900 dark:text-slate-100">
                        {tx.recipient || tx.sender || 'Self'}
                      </p>
                      <p className="text-xs text-slate-500 truncate max-w-xs">{tx.description}</p>
                    </td>
                    <td className="py-3.5 px-4">
                      <span className="inline-flex items-center space-x-1 text-xs font-medium">
                        {tx.type === 'Deposit' ? (
                          <>
                            <ArrowDownLeft className="w-3.5 h-3.5 text-emerald-500" />
                            <span>Deposit</span>
                          </>
                        ) : (
                          <>
                            <ArrowUpRight className="w-3.5 h-3.5 text-slate-500" />
                            <span>Transfer</span>
                          </>
                        )}
                      </span>
                    </td>
                    <td className="py-3.5 px-4">
                      <Badge
                        variant={
                          tx.status === 'Completed'
                            ? 'success'
                            : tx.status === 'Pending'
                            ? 'warning'
                            : 'danger'
                        }
                      >
                        {tx.status}
                      </Badge>
                    </td>
                    <td className="py-3.5 px-4 text-right text-xs text-slate-500">
                      {(tx.feeAmount ?? tx.fee) ? formatCurrency(tx.feeAmount ?? tx.fee ?? 0, tx.currency) : '—'}
                    </td>
                    <td className="py-3.5 px-4 text-right font-semibold">
                      <span
                        className={
                          tx.type === 'Deposit'
                            ? 'text-emerald-600 dark:text-emerald-400'
                            : 'text-slate-900 dark:text-slate-100'
                        }
                      >
                        {tx.type === 'Deposit' ? '+' : '-'}
                        {formatCurrency(tx.amount, tx.currency)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="mt-4 pt-4 border-t border-slate-200 dark:border-slate-800 flex items-center justify-between text-xs text-slate-500">
            <span>Page {currentPage} of 1</span>
            <div className="flex items-center space-x-2">
              <Button
                variant="outline"
                size="sm"
                disabled={currentPage <= 1}
                onClick={() => setCurrentPage((p) => p - 1)}
              >
                <ChevronLeft className="w-4 h-4 mr-1" />
                Previous
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={true}
                onClick={() => setCurrentPage((p) => p + 1)}
              >
                Next
                <ChevronRight className="w-4 h-4 ml-1" />
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};
