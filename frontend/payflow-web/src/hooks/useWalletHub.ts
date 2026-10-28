import { useEffect, useState, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { tokenStorage } from '@/lib/tokenStorage';
import { useAuth } from '@/context/AuthContext';
import { toast } from '@/components/ui/toast';
import { formatCurrency } from '@/lib/utils';

export interface BalanceUpdatedEvent {
  walletId: string;
  userId: string;
  newBalance: number;
  currency: string;
  updatedAtUtc: string;
}

export interface TransferReceivedEvent {
  transactionId: string;
  reference: string;
  senderUserId: string;
  receiverUserId: string;
  amount: number;
  currency: string;
  occurredAtUtc: string;
}

export interface TransferSentEvent {
  transactionId: string;
  reference: string;
  senderUserId: string;
  receiverUserId: string;
  amount: number;
  feeAmount: number;
  currency: string;
  occurredAtUtc: string;
}

export function useWalletHub() {
  const { isAuthenticated } = useAuth();
  const queryClient = useQueryClient();
  const [isConnected, setIsConnected] = useState(false);
  const [justUpdated, setJustUpdated] = useState(false);
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const flashTimeoutRef = useRef<any>(null);

  const triggerFlash = () => {
    setJustUpdated(true);
    if (flashTimeoutRef.current) clearTimeout(flashTimeoutRef.current);
    flashTimeoutRef.current = setTimeout(() => {
      setJustUpdated(false);
    }, 3500);
  };

  useEffect(() => {
    if (!isAuthenticated) {
      if (connectionRef.current) {
        connectionRef.current.stop();
        connectionRef.current = null;
        setIsConnected(false);
      }
      return;
    }

    const hubUrl = import.meta.env.VITE_HUB_URL || '/hubs/wallet';

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => tokenStorage.getAccessToken() || '',
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.on('BalanceUpdated', (data: BalanceUpdatedEvent) => {
      queryClient.invalidateQueries({ queryKey: ['wallet'] });
      triggerFlash();
      toast.info(
        'Balance Updated',
        `New balance: ${formatCurrency(data.newBalance, data.currency)}`
      );
    });

    connection.on('TransferReceived', (data: TransferReceivedEvent) => {
      queryClient.invalidateQueries({ queryKey: ['wallet'] });
      queryClient.invalidateQueries({ queryKey: ['transactions'] });
      triggerFlash();
      toast.success(
        'Payment Received!',
        `+${formatCurrency(data.amount, data.currency)} received (Ref: ${data.reference.substring(0, 12)}...)`
      );
    });

    connection.on('TransferSent', (data: TransferSentEvent) => {
      queryClient.invalidateQueries({ queryKey: ['wallet'] });
      queryClient.invalidateQueries({ queryKey: ['transactions'] });
      triggerFlash();
      toast.info(
        'Transfer Settled',
        `-${formatCurrency(data.amount, data.currency)} debited`
      );
    });

    connection.onreconnecting(() => {
      setIsConnected(false);
    });

    connection.onreconnected(() => {
      setIsConnected(true);
      queryClient.invalidateQueries({ queryKey: ['wallet'] });
    });

    connection.onclose(() => {
      setIsConnected(false);
    });

    // Start connection
    connection
      .start()
      .then(() => {
        setIsConnected(true);
      })
      .catch((err) => {
        // Expected when backend hub is unreachable during disconnected test runs
        console.warn('SignalR wallet hub connection deferred:', err.message);
        setIsConnected(false);
      });

    return () => {
      if (flashTimeoutRef.current) clearTimeout(flashTimeoutRef.current);
      connection.stop().catch(() => {});
    };
  }, [isAuthenticated, queryClient]);

  return {
    isConnected,
    justUpdated,
    triggerFlash,
  };
}
