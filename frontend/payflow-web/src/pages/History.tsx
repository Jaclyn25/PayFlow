import React, { useState, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { transactionApi } from '@/api/transaction';
import { TransactionDetailsModal } from '@/components/wallet/TransactionDetailsModal';
import { formatCurrency, formatDate } from '@/lib/utils';
import { toast } from '@/components/ui/toast';
import {
  Search,
  Download,
  ArrowUpRight,
  ArrowDownLeft,
  ChevronLeft,
  ChevronRight,
  RefreshCw,
  FileText,
  RotateCcw,
} from 'lucide-react';
import type { Transaction } from '@/types';

export const History: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();

  // Read initial query params
  const pageParam = parseInt(searchParams.get('page') || '1', 10);
  const typeParam = searchParams.get('type') || 'all';
  const statusParam = searchParams.get('status') || 'all';
  const searchParam = searchParams.get('search') || '';
  const fromParam = searchParams.get('from') || '';
  const toParam = searchParams.get('to') || '';

  const [page, setPage] = useState(pageParam);
  const [pageSize] = useState(10);
  const [typeFilter, setTypeFilter] = useState(typeParam);
  const [statusFilter, setStatusFilter] = useState(statusParam);
  const [searchInput, setSearchInput] = useState(searchParam);
  const [searchTerm, setSearchTerm] = useState(searchParam);
  const [fromDate, setFromDate] = useState(fromParam);
  const [toDate, setToDate] = useState(toParam);

  const [selectedTx, setSelectedTx] = useState<Transaction | null>(null);
  const [isDetailsOpen, setIsDetailsOpen] = useState(false);
  const [isExportingCsv, setIsExportingCsv] = useState(false);
  const [isExportingPdf, setIsExportingPdf] = useState(false);

  // Debounce search input
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearchTerm(searchInput);
      setPage(1);
    }, 350);
    return () => clearTimeout(timer);
  }, [searchInput]);

  // Sync state changes with URL query parameters
  useEffect(() => {
    const params = new URLSearchParams();
    if (page > 1) params.set('page', page.toString());
    if (typeFilter !== 'all') params.set('type', typeFilter);
    if (statusFilter !== 'all') params.set('status', statusFilter);
    if (searchTerm) params.set('search', searchTerm);
    if (fromDate) params.set('from', fromDate);
    if (toDate) params.set('to', toDate);

    setSearchParams(params, { replace: true });
  }, [page, typeFilter, statusFilter, searchTerm, fromDate, toDate, setSearchParams]);

  // Server-side paginated & filtered data query
  const { data, isLoading, isFetching, refetch } = useQuery({
    queryKey: ['transactions', page, pageSize, typeFilter, statusFilter, searchTerm, fromDate, toDate],
    queryFn: () =>
      transactionApi.getTransactions({
        page,
        pageSize,
        type: typeFilter,
        status: statusFilter,
        searchTerm,
        fromDate,
        toDate,
      }),
    staleTime: 1000 * 30,
  });

  const transactions = data?.items || [];
  const totalCount = data?.totalCount || 0;
  const totalPages = data?.totalPages || 1;

  const handleRowClick = (tx: Transaction) => {
    setSelectedTx(tx);
    setIsDetailsOpen(true);
  };

  const handleResetFilters = () => {
    setSearchInput('');
    setSearchTerm('');
    setTypeFilter('all');
    setStatusFilter('all');
    setFromDate('');
    setToDate('');
    setPage(1);
  };

  const handleExportStatement = async (format: 'csv' | 'pdf') => {
    if (format === 'csv') setIsExportingCsv(true);
    else setIsExportingPdf(true);

    try {
      await transactionApi.downloadStatement(format, fromDate, toDate);
      toast.success(
        'Statement Downloaded',
        `Your ${format.toUpperCase()} statement has been generated successfully.`
      );
    } catch {
      toast.error(
        'Export Failed',
        'Could not export statement at this time. Please try again.'
      );
    } finally {
      if (format === 'csv') setIsExportingCsv(false);
      else setIsExportingPdf(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header & Export Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
            Ledger & Transaction History
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            Audit-grade double-entry transaction record with server-side filtering and statement export.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            className="gap-1.5 text-xs"
            disabled={isFetching}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => handleExportStatement('csv')}
            isLoading={isExportingCsv}
            disabled={isExportingCsv}
            className="gap-1.5 text-xs"
          >
            <Download className="w-3.5 h-3.5" />
            <span>Export CSV</span>
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => handleExportStatement('pdf')}
            isLoading={isExportingPdf}
            disabled={isExportingPdf}
            className="gap-1.5 text-xs"
          >
            <FileText className="w-3.5 h-3.5 text-rose-500" />
            <span>Export PDF</span>
          </Button>
        </div>
      </div>

      <Card>
        <CardHeader>
          <div className="space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <div>
                <CardTitle>Transactions</CardTitle>
                <CardDescription>
                  Showing {transactions.length} of {totalCount} total entries (Page {page} of {totalPages})
                </CardDescription>
              </div>

              {(typeFilter !== 'all' || statusFilter !== 'all' || searchTerm || fromDate || toDate) && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleResetFilters}
                  className="gap-1 text-xs text-muted-foreground self-start sm:self-auto"
                >
                  <RotateCcw className="w-3 h-3" />
                  <span>Reset Filters</span>
                </Button>
              )}
            </div>

            {/* Filter Toolbar */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3 pt-2 border-t border-border/60">
              {/* Search Box */}
              <div className="lg:col-span-2">
                <Input
                  placeholder="Search by reference, description..."
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  leftIcon={<Search className="w-4 h-4 text-slate-400" />}
                />
              </div>

              {/* Type Filter */}
              <div>
                <select
                  value={typeFilter}
                  onChange={(e) => {
                    setTypeFilter(e.target.value);
                    setPage(1);
                  }}
                  className="w-full h-10 px-3 rounded-lg border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-500"
                >
                  <option value="all">All Types</option>
                  <option value="Transfer">Transfer</option>
                  <option value="Deposit">Deposit</option>
                  <option value="Withdrawal">Withdrawal</option>
                  <option value="Fee">Fee</option>
                  <option value="Reversal">Reversal</option>
                </select>
              </div>

              {/* Status Filter */}
              <div>
                <select
                  value={statusFilter}
                  onChange={(e) => {
                    setStatusFilter(e.target.value);
                    setPage(1);
                  }}
                  className="w-full h-10 px-3 rounded-lg border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-500"
                >
                  <option value="all">All Statuses</option>
                  <option value="Completed">Completed</option>
                  <option value="Pending">Pending</option>
                  <option value="Failed">Failed</option>
                  <option value="Reversed">Reversed</option>
                </select>
              </div>

              {/* Date Filters */}
              <div className="grid grid-cols-2 gap-2">
                <Input
                  type="date"
                  value={fromDate}
                  onChange={(e) => {
                    setFromDate(e.target.value);
                    setPage(1);
                  }}
                  title="From Date"
                />
                <Input
                  type="date"
                  value={toDate}
                  onChange={(e) => {
                    setToDate(e.target.value);
                    setPage(1);
                  }}
                  title="To Date"
                />
              </div>
            </div>
          </div>
        </CardHeader>

        <CardContent>
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse text-sm">
              <thead>
                <tr className="border-b border-slate-200 dark:border-slate-800 text-xs uppercase font-semibold text-slate-500 dark:text-slate-400 bg-muted/20">
                  <th className="py-3 px-4">Reference</th>
                  <th className="py-3 px-4">Date & Time</th>
                  <th className="py-3 px-4">Counterparty / Note</th>
                  <th className="py-3 px-4">Type</th>
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4 text-right">Fee</th>
                  <th className="py-3 px-4 text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-slate-800/60">
                {isLoading ? (
                  <tr>
                    <td colSpan={7} className="text-center py-10 text-muted-foreground">
                      <div className="flex items-center justify-center space-x-2">
                        <RefreshCw className="w-4 h-4 animate-spin text-primary" />
                        <span>Loading transactions...</span>
                      </div>
                    </td>
                  </tr>
                ) : transactions.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="text-center py-10 text-muted-foreground">
                      No transactions found matching your criteria.
                    </td>
                  </tr>
                ) : (
                  transactions.map((tx) => (
                    <tr
                      key={tx.id}
                      onClick={() => handleRowClick(tx)}
                      className="hover:bg-slate-50/80 dark:hover:bg-slate-900/60 cursor-pointer transition-colors"
                      title="Click to view audit details"
                    >
                      <td className="py-3.5 px-4 font-mono text-xs text-primary font-medium">
                        {tx.reference}
                      </td>
                      <td className="py-3.5 px-4 text-slate-500 dark:text-slate-400 whitespace-nowrap text-xs">
                        {formatDate(tx.createdAtUtc || tx.createdAt || '')}
                      </td>
                      <td className="py-3.5 px-4">
                        <p className="font-medium text-slate-900 dark:text-slate-100">
                          {tx.recipient || tx.sender || 'Direct'}
                        </p>
                        <p className="text-xs text-slate-500 truncate max-w-xs">
                          {tx.description || 'No description provided'}
                        </p>
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
                              <span>{tx.type}</span>
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
                        {(tx.feeAmount ?? tx.fee)
                          ? formatCurrency(tx.feeAmount ?? tx.fee ?? 0, tx.currency)
                          : '—'}
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
                  ))
                )}
              </tbody>
            </table>
          </div>

          {/* Pagination Controls */}
          <div className="mt-4 pt-4 border-t border-slate-200 dark:border-slate-800 flex items-center justify-between text-xs text-slate-500">
            <span>
              Page {page} of {totalPages} ({totalCount} items)
            </span>
            <div className="flex items-center space-x-2">
              <Button
                variant="outline"
                size="sm"
                disabled={page <= 1 || isLoading}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                <ChevronLeft className="w-4 h-4 mr-1" />
                Previous
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={page >= totalPages || isLoading}
                onClick={() => setPage((p) => p + 1)}
              >
                Next
                <ChevronRight className="w-4 h-4 ml-1" />
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Transaction Details Drawer / Sheet Modal */}
      <TransactionDetailsModal
        isOpen={isDetailsOpen}
        onClose={() => setIsDetailsOpen(false)}
        transaction={selectedTx}
      />
    </div>
  );
};
