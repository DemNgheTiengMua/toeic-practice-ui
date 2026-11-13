import React, { useEffect, useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Spinner,
} from '@heroui/react';
import {
  AlertCircle,
  ArrowLeft,
  CheckCircle2,
  Clock,
  ExternalLink,
  QrCode,
  RefreshCw,
  Sparkles,
  Wallet,
  XCircle,
} from 'lucide-react';
import { api } from '../../lib/api';
import { formatVnd, safeExternalHref } from '../../lib/security';
import {
  formatDateTime,
  formatGatewayLabel,
  formatOrderStatus,
} from '../../lib/domain-labels';

export function OrderStatusPage() {
  const params = useParams({ strict: false }) as { orderCode?: string };
  const search = useSearch({ strict: false }) as { orderCode?: string };
  const orderCode = params?.orderCode || search?.orderCode || '';

  const queryClient = useQueryClient();
  const [actionError, setActionError] = useState<string | null>(null);

  const orderQuery = useQuery({
    queryKey: ['finance', 'order-result', orderCode],
    queryFn: () => api.finance.paymentResult(orderCode),
    enabled: Boolean(orderCode),
    refetchInterval: (query) => {
      const status = query.state.data?.status?.toLowerCase();
      return status === 'pending' || status === 'reconciling' ? 5000 : false;
    },
    staleTime: 2_000,
    retry: false,
  });

  const order = orderQuery.data;

  // Invalidate learner context balance when paid
  useEffect(() => {
    if (order?.status?.toLowerCase() === 'paid') {
      void queryClient.invalidateQueries({ queryKey: ['learner', 'context'] });
      void queryClient.invalidateQueries({ queryKey: ['finance', 'wallet'] });
    }
  }, [order?.status, queryClient]);

  const mockActionMutation = useMutation({
    mutationFn: (action: 'success' | 'failed' | 'expired' | 'pending') =>
      api.finance.mockGatewayAction({
        orderCode,
        action,
        errorCode: action === 'failed' ? '24' : undefined,
      }),
    onSuccess: (data) => {
      setActionError(null);
      queryClient.setQueryData(['finance', 'order-result', orderCode], data);
      void queryClient.invalidateQueries({ queryKey: ['finance', 'order-result', orderCode] });
      if (data.status?.toLowerCase() === 'paid') {
        void queryClient.invalidateQueries({ queryKey: ['learner', 'context'] });
        void queryClient.invalidateQueries({ queryKey: ['finance', 'wallet'] });
      }
    },
    onError: (err) => {
      setActionError((err as Error).message || 'Simulation action failed.');
    },
  });

  const cancelMutation = useMutation({
    mutationFn: () => api.finance.cancelOrder(orderCode),
    onSuccess: () => {
      setActionError(null);
      void queryClient.invalidateQueries({ queryKey: ['finance', 'order-result', orderCode] });
    },
    onError: (err) => {
      setActionError((err as Error).message || 'Failed to cancel order.');
    },
  });

  if (!orderCode) {
    return (
      <div className="max-w-2xl mx-auto py-12 px-4 sm:px-6">
        <Alert status="warning">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Missing order reference</Alert.Title>
            <Alert.Description>
              No order code was specified. Please choose an order from your wallet history.
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <div className="mt-4">
          <Link to="/wallet" className="no-underline">
            <Button variant="secondary">Back to wallet</Button>
          </Link>
        </div>
      </div>
    );
  }

  if (orderQuery.isLoading) {
    return (
      <div className="max-w-2xl mx-auto py-12 px-4 sm:px-6">
        <Card>
          <Card.Content className="flex items-center justify-center py-12 gap-3">
            <Spinner size="sm" />
            <span className="text-sm text-muted">Retrieving order status for {orderCode}...</span>
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (orderQuery.isError || !order) {
    return (
      <div className="max-w-2xl mx-auto py-12 px-4 sm:px-6 flex flex-col gap-6">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Order not found</Alert.Title>
            <Alert.Description>
              Could not retrieve payment information for order “{orderCode}”.
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <div>
          <Link to="/wallet" className="no-underline">
            <Button variant="secondary">Back to wallet</Button>
          </Link>
        </div>
      </div>
    );
  }

  const statusLower = (order.status || '').toLowerCase();
  const isPaid = statusLower === 'paid';
  const isPending = statusLower === 'pending' || statusLower === 'reconciling';
  const isFailed = statusLower === 'failed';
  const isExpired = statusLower === 'expired';
  const isMockGateway = (order.gateway || '').toLowerCase() === 'mock';

  const statusDisplay = formatOrderStatus(order.status);
  const safePayOsHref = order.checkoutUrl ? safeExternalHref(order.checkoutUrl) : null;

  return (
    <div className="max-w-3xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-6">
      <div>
        <div className="flex items-center gap-2 mb-1">
          <QrCode className="w-5 h-5 text-accent" />
          <span className="text-xs font-semibold uppercase tracking-wider text-muted">
            Payment confirmation
          </span>
        </div>
        <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
          Order status
        </h1>
        <p className="text-sm text-muted mt-1 mb-0">
          Order invoice <span className="font-mono-tabular font-semibold text-foreground">{order.orderCode}</span>
        </p>
      </div>

      {actionError && (
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Gateway action failed</Alert.Title>
            <Alert.Description>{actionError}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Main Order Card */}
      <Card className="shadow-surface">
        <Card.Header>
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div>
              <Card.Title className="text-xl font-bold">{order.packageName}</Card.Title>
              <Card.Description>
                Gateway: {formatGatewayLabel(order.gateway)}
              </Card.Description>
            </div>
            <div>
              <Chip
                size="md"
                color={statusDisplay.tone}
                variant="soft"
                className="font-semibold"
              >
                {statusDisplay.label}
              </Chip>
            </div>
          </div>
        </Card.Header>

        <Card.Content className="space-y-6">
          <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-3">
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted">Total amount:</span>
              <span className="font-mono-tabular font-bold text-foreground text-base">
                {formatVnd(order.amount)}
              </span>
            </div>
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted">Credits granted:</span>
              <span className="font-mono-tabular font-bold text-accent">
                +{order.credits} credits
              </span>
            </div>
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted">Placed at:</span>
              <span className="font-mono-tabular text-xs text-muted">
                {formatDateTime(order.createdAt)}
              </span>
            </div>
            {order.paidAt && (
              <div className="flex items-center justify-between text-sm">
                <span className="text-muted">Completed at:</span>
                <span className="font-mono-tabular text-xs text-success font-medium">
                  {formatDateTime(order.paidAt)}
                </span>
              </div>
            )}
            <div className="border-t border-separator pt-3 flex items-center justify-between text-sm">
              <span className="text-muted">Current credit balance:</span>
              <span className="font-mono-tabular font-bold text-foreground">
                {order.currentBalance} credits
              </span>
            </div>
          </div>

          {/* Pending Auto-polling banner */}
          {isPending && (
            <Alert status="accent">
              <Alert.Indicator>
                <RefreshCw className="w-4 h-4 animate-spin text-accent" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Awaiting payment confirmation</Alert.Title>
                <Alert.Description>
                  This invoice is automatically checking for settlement every 5 seconds.
                  As soon as the payment is confirmed, your credits will appear instantly.
                </Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          {/* Success banner */}
          {isPaid && (
            <Alert status="success">
              <Alert.Indicator>
                <CheckCircle2 className="w-5 h-5 text-success" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Payment settled successfully</Alert.Title>
                <Alert.Description>
                  {order.credits} credits have been deposited into your wallet ledger.
                </Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          {/* Failed banner */}
          {isFailed && (
            <Alert status="danger">
              <Alert.Indicator>
                <XCircle className="w-5 h-5 text-danger" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Payment was not completed</Alert.Title>
                <Alert.Description>
                  The transaction was rejected or canceled by the gateway.
                  {order.gatewayErrorCode ? ` Error code: ${order.gatewayErrorCode}.` : ''}
                </Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          {/* Expired banner */}
          {isExpired && (
            <Alert status="warning">
              <Alert.Indicator>
                <Clock className="w-5 h-5 text-warning" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Invoice expired</Alert.Title>
                <Alert.Description>
                  This invoice reached its time limit without receiving payment. Please order a new package from the wallet.
                </Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          {/* Safe External PayOS link */}
          {isPending && safePayOsHref && (
            <div className="pt-2">
              <a
                href={safePayOsHref}
                target="_blank"
                rel="noopener noreferrer"
                className="no-underline w-full block"
              >
                <Button variant="primary" size="lg" className="w-full justify-center">
                  <ExternalLink className="w-4 h-4 mr-2" />
                  Open VietQR checkout ({formatVnd(order.amount)})
                </Button>
              </a>
            </div>
          )}

          {/* Mock Gateway simulator panel */}
          {isMockGateway && isPending && (
            <div className="p-4 rounded-lg bg-surface-secondary border border-dashed border-border space-y-3">
              <div className="flex items-center gap-2">
                <Sparkles className="w-4 h-4 text-warning" />
                <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                  Mock Gateway Simulator
                </span>
              </div>
              <p className="text-xs text-muted m-0">
                You are testing with the mock gateway. You can trigger payment events immediately:
              </p>
              <div className="flex flex-wrap items-center gap-2 pt-1">
                <Button
                  size="sm"
                  variant="primary"
                  isPending={mockActionMutation.isPending}
                  onPress={() => mockActionMutation.mutate('success')}
                >
                  <CheckCircle2 className="w-4 h-4 mr-1 text-success" />
                  Simulate payment success
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  isPending={mockActionMutation.isPending}
                  onPress={() => mockActionMutation.mutate('failed')}
                >
                  <XCircle className="w-4 h-4 mr-1 text-danger" />
                  Simulate failed
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  isPending={mockActionMutation.isPending}
                  onPress={() => mockActionMutation.mutate('expired')}
                >
                  <Clock className="w-4 h-4 mr-1 text-warning" />
                  Simulate expired
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  isPending={cancelMutation.isPending}
                  onPress={() => cancelMutation.mutate()}
                >
                  Cancel order
                </Button>
              </div>
            </div>
          )}

          {/* Bottom actions */}
          <div className="pt-4 border-t border-separator flex items-center justify-between">
            <Link to="/wallet" className="no-underline">
              <Button variant="secondary">
                <Wallet className="w-4 h-4 mr-1.5" />
                Return to wallet
              </Button>
            </Link>

            {isPaid && (
              <Link to="/practice" className="no-underline">
                <Button variant="primary">
                  Start practicing
                </Button>
              </Link>
            )}
          </div>
        </Card.Content>
      </Card>
    </div>
  );
}
