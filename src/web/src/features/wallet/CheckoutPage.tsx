import React, { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
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
  ArrowRight,
  CheckCircle2,
  CreditCard,
  MailWarning,
  Package,
  ShieldCheck,
  Sparkles,
  XCircle,
} from 'lucide-react';
import { api, ApiClientError } from '../../lib/api';
import { formatVnd, formatVndUnitPrice } from '../../lib/security';

export function CheckoutPage() {
  const search = useSearch({ strict: false }) as { packageId?: string | number };
  const rawId = search?.packageId;
  const packageId = rawId ? Number(rawId) : NaN;
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const previewQuery = useQuery({
    queryKey: ['finance', 'checkout-preview', packageId],
    queryFn: () => api.finance.checkoutPreview(packageId),
    enabled: !Number.isNaN(packageId) && packageId > 0,
    staleTime: 5_000,
    retry: false,
  });

  const createOrderMutation = useMutation({
    mutationFn: (pkgId: number) => api.finance.createOrder(pkgId),
    onSuccess: (order) => {
      void navigate({ to: `/orders/${order.orderCode}` });
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setErrorMessage(err.title || err.detail || 'Could not create order.');
      } else {
        setErrorMessage((err as Error).message || 'Failed to place order.');
      }
    },
  });

  const cancelOpenOrderMutation = useMutation({
    mutationFn: (code: string) => api.finance.cancelOrder(code),
    onSuccess: () => {
      setErrorMessage(null);
      void queryClient.invalidateQueries({ queryKey: ['finance', 'checkout-preview'] });
    },
    onError: (err) => {
      setErrorMessage((err as Error).message || 'Failed to cancel existing order.');
    },
  });

  if (Number.isNaN(packageId) || packageId <= 0) {
    return (
      <div className="max-w-2xl mx-auto py-12 px-4 sm:px-6">
        <Alert status="warning">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>No package selected</Alert.Title>
            <Alert.Description>
              Please select a credit package from our wallet or pricing page to proceed with checkout.
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <div className="mt-6">
          <Link to="/wallet" className="no-underline">
            <Button variant="secondary">
              <ArrowLeft className="w-4 h-4 mr-1.5" />
              Back to wallet
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  if (previewQuery.isLoading) {
    return (
      <div className="max-w-2xl mx-auto py-12 px-4 sm:px-6">
        <Card>
          <Card.Content className="flex items-center justify-center py-12 gap-3">
            <Spinner size="sm" />
            <span className="text-sm text-muted">Calculating checkout summary...</span>
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (previewQuery.isError) {
    const error = previewQuery.error as any;
    const isEmailNotVerified =
      error?.code === 'EMAIL_NOT_VERIFIED' || error?.status === 403;

    if (isEmailNotVerified) {
      return (
        <div className="max-w-2xl mx-auto py-12 px-4 sm:px-6 flex flex-col gap-6">
          <Alert status="warning">
            <Alert.Indicator>
              <MailWarning className="w-5 h-5" />
            </Alert.Indicator>
            <Alert.Content>
              <Alert.Title>Email verification required</Alert.Title>
              <Alert.Description>
                You must verify your email address before purchasing credits or subscribing.
                Please check your inbox or visit your profile settings to resend verification.
              </Alert.Description>
            </Alert.Content>
          </Alert>

          <div className="flex items-center gap-3">
            <Link to="/profile" className="no-underline">
              <Button variant="primary">
                Go to profile
              </Button>
            </Link>
            <Link to="/wallet" className="no-underline">
              <Button variant="secondary">
                Return to wallet
              </Button>
            </Link>
          </div>
        </div>
      );
    }

    return (
      <div className="max-w-2xl mx-auto py-12 px-4 sm:px-6 flex flex-col gap-6">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Package unavailable</Alert.Title>
            <Alert.Description>
              {error?.detail || error?.title || 'The requested package was not found or is currently inactive.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>

        <div>
          <Link to="/wallet" className="no-underline">
            <Button variant="secondary">
              <ArrowLeft className="w-4 h-4 mr-1.5" />
              Back to wallet
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  const preview = previewQuery.data;
  if (!preview) return null;

  const hasOpenOrder = Boolean(preview.openOrder);

  return (
    <div className="max-w-3xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-6">
      <div>
        <div className="flex items-center gap-2 mb-1">
          <CreditCard className="w-5 h-5 text-accent" />
          <span className="text-xs font-semibold uppercase tracking-wider text-muted">
            Order Checkout
          </span>
        </div>
        <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
          Confirm your order
        </h1>
        <p className="text-sm text-muted mt-1 mb-0">
          Review your package credits and payment total before creating the payment gateway invoice.
        </p>
      </div>

      {/* Error alert if creation failed */}
      {errorMessage && (
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Order creation blocked</Alert.Title>
            <Alert.Description>{errorMessage}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Open order conflict warning */}
      {hasOpenOrder && preview.openOrder && (
        <Alert status="warning">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Existing pending order detected</Alert.Title>
            <Alert.Description className="space-y-3">
              <p className="m-0">
                You already have an open order ({preview.openOrder.orderCode}) for{' '}
                <strong className="font-semibold text-foreground">{preview.openOrder.packageName}</strong>{' '}
                awaiting completion or cancellation.
              </p>
              <div className="flex flex-wrap items-center gap-3 pt-1">
                <Link to={`/orders/${preview.openOrder.orderCode}`} className="no-underline">
                  <Button size="sm" variant="primary">
                    Resume payment ({preview.openOrder.orderCode})
                  </Button>
                </Link>
                <Button
                  size="sm"
                  variant="secondary"
                  isPending={cancelOpenOrderMutation.isPending}
                  onPress={() => cancelOpenOrderMutation.mutate(preview.openOrder!.orderCode)}
                >
                  <XCircle className="w-4 h-4 mr-1 text-danger" />
                  Cancel existing order
                </Button>
              </div>
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Checkout Preview Details Card */}
      <Card className="shadow-surface">
        <Card.Header>
          <div className="flex items-center justify-between">
            <div>
              <Card.Title className="text-xl font-bold">{preview.packageName}</Card.Title>
              <Card.Description>Instant digital credit grant upon payment confirmation</Card.Description>
            </div>
            <Chip size="md" color="accent" variant="soft" className="font-mono-tabular font-bold">
              +{preview.credits} credits
            </Chip>
          </div>
        </Card.Header>

        <Card.Content className="space-y-6">
          <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-3">
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted">Package price:</span>
              <span className="font-mono-tabular font-bold text-foreground text-base">
                {formatVnd(preview.price)}
              </span>
            </div>
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted">Unit price:</span>
              <span className="font-mono-tabular text-xs text-muted">
                {formatVndUnitPrice(preview.price, preview.credits)}
              </span>
            </div>
            <div className="border-t border-separator pt-3 flex items-center justify-between text-sm">
              <span className="text-muted">Current credit balance:</span>
              <span className="font-mono-tabular font-semibold text-foreground">
                {preview.currentBalance} credits
              </span>
            </div>
            <div className="flex items-center justify-between text-sm font-semibold">
              <span className="text-foreground">Balance after purchase:</span>
              <span className="font-mono-tabular text-accent text-base font-bold">
                {preview.balanceAfter} credits
              </span>
            </div>
          </div>

          <div className="flex items-center gap-3 text-xs text-muted">
            <ShieldCheck className="w-4 h-4 text-success flex-shrink-0" />
            <span>
              Secure automated gateway checkout via VietQR / payOS with instant ledger settlement.
            </span>
          </div>

          <div className="pt-2 flex flex-col sm:flex-row items-center justify-between gap-4">
            <Link to="/wallet" className="no-underline order-2 sm:order-1">
              <Button variant="ghost">
                <ArrowLeft className="w-4 h-4 mr-1.5" />
                Change package
              </Button>
            </Link>

            <Button
              variant="primary"
              size="lg"
              className="w-full sm:w-auto order-1 sm:order-2"
              isDisabled={hasOpenOrder}
              isPending={createOrderMutation.isPending}
              onPress={() => createOrderMutation.mutate(packageId)}
            >
              <CreditCard className="w-4 h-4 mr-2" />
              Proceed to payment ({formatVnd(preview.price)})
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </div>
        </Card.Content>
      </Card>
    </div>
  );
}
