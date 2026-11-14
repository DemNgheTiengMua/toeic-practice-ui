import React, { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Label,
  Slider,
  Spinner,
} from '@heroui/react';
import {
  AlertCircle,
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  Clock,
  CreditCard,
  MailWarning,
  Package,
  ShieldCheck,
  Sparkles,
  Sliders,
  Wallet,
  XCircle,
} from 'lucide-react';
import { api, ApiClientError, type CreateOrderResponseDto } from '../../lib/api';
import { formatVnd, formatVndUnitPrice } from '../../lib/security';
import { PayOsEmbeddedModal } from './PayOsEmbeddedModal';

const FLAT_CREDIT_PRICE = 20_000;
const PRESET_CREDITS = [5, 10, 20];

export function useSearchParams() {
  const search = useSearch({ strict: false }) as {
    credits?: string | number;
    returnUrl?: string;
    packageId?: string | number;
  };
  const browserSearch = typeof window !== 'undefined' ? new URLSearchParams(window.location.search) : null;
  const credits = search?.credits ?? browserSearch?.get('credits') ?? undefined;
  const returnUrl = search?.returnUrl ?? browserSearch?.get('returnUrl') ?? undefined;
  const packageId = search?.packageId ?? browserSearch?.get('packageId') ?? undefined;
  return { credits, returnUrl, packageId };
}

export function CheckoutPage() {
  const { credits: queryCredits, returnUrl, packageId: queryPackageId } = useSearchParams();
  const packageId = queryPackageId ? Number(queryPackageId) : NaN;
  const hasPackageId = !Number.isNaN(packageId) && packageId > 0;

  // If credits query param is given, use it; otherwise default to 5
  const initialCredits = queryCredits
    ? Math.min(100, Math.max(1, Number(queryCredits) || 5))
    : 5;

  const [credits, setCredits] = useState<number>(initialCredits);
  const [useCustomSlider, setUseCustomSlider] = useState<boolean>(!hasPackageId || Boolean(queryCredits));
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Active created order and settlement state
  const [activeOrder, setActiveOrder] = useState<CreateOrderResponseDto | null>(null);
  const [isPayOsModalOpen, setIsPayOsModalOpen] = useState(false);
  const [settlementSuccess, setSettlementSuccess] = useState(false);
  const [mockActionError, setMockActionError] = useState<string | null>(null);

  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const isPackageMode = hasPackageId && !useCustomSlider;

  // Learner context for wallet balance fallback
  const learnerContextQuery = useQuery({
    queryKey: ['learner', 'context'],
    queryFn: () => api.learner.context(),
    staleTime: 10_000,
  });

  // Preview query: either for fixed package or custom credits
  const previewQuery = useQuery({
    queryKey: ['finance', 'checkout-preview', isPackageMode ? `pkg-${packageId}` : `credits-${credits}`],
    queryFn: () => {
      if (isPackageMode) {
        return api.finance.checkoutPreview(packageId);
      }
      return api.finance.getCheckoutPreview({ credits });
    },
    staleTime: 5_000,
    retry: false,
  });

  const createOrderMutation = useMutation({
    mutationFn: (payload: { packageId?: number; credits?: number }) => api.finance.createOrder(payload),
    onSuccess: (order) => {
      setErrorMessage(null);
      setActiveOrder(order);

      // Legacy compatibility: package checkout without credits or returnUrl navigates to /orders/:code
      if (isPackageMode && !queryCredits && !returnUrl) {
        void navigate({ to: `/orders/${order.orderCode}` });
        return;
      }

      if (order.gateway?.toLowerCase() === 'payos' && order.checkoutUrl) {
        setIsPayOsModalOpen(true);
      }
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

  const mockActionMutation = useMutation({
    mutationFn: (action: 'success' | 'failed' | 'expired') => {
      if (!activeOrder?.orderCode) throw new Error('No active order to simulate');
      return api.finance.mockGatewayAction({
        orderCode: activeOrder.orderCode,
        action,
        errorCode: action === 'failed' ? '24' : undefined,
      });
    },
    onSuccess: (_data, action) => {
      setMockActionError(null);
      if (action === 'success') {
        setSettlementSuccess(true);
        void queryClient.invalidateQueries({ queryKey: ['learner', 'context'] });
        void queryClient.invalidateQueries({ queryKey: ['finance', 'wallet'] });
        void queryClient.invalidateQueries({ queryKey: ['finance', 'checkout-preview'] });
        setTimeout(() => {
          void navigate({ to: returnUrl || '/pricing' });
        }, 1500);
      }
    },
    onError: (err) => {
      setMockActionError((err as Error).message || 'Simulation action failed.');
    },
  });

  const handleSliderChange = (v: number | number[]) => {
    const val = typeof v === 'number' ? v : v[0];
    if (typeof val === 'number' && !Number.isNaN(val)) {
      setCredits(Math.min(100, Math.max(1, Math.round(val))));
    }
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = parseInt(e.target.value, 10);
    if (Number.isNaN(val)) {
      setCredits(1);
    } else {
      setCredits(Math.min(100, Math.max(1, val)));
    }
  };

  const handleCreateOrder = () => {
    if (isPackageMode) {
      createOrderMutation.mutate({ packageId });
    } else {
      createOrderMutation.mutate({ credits });
    }
  };

  // Loading state while preview is calculating (only for initial package preview)
  if (isPackageMode && previewQuery.isLoading) {
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

  // Error states for unverified email or invalid package
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
              <Button variant="primary">Go to profile</Button>
            </Link>
            <Link to="/wallet" className="no-underline">
              <Button variant="secondary">Return to wallet</Button>
            </Link>
          </div>
        </div>
      );
    }

    if (isPackageMode) {
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
  }

  const preview = previewQuery.data;
  const currentBalance =
    preview?.currentBalance ?? learnerContextQuery.data?.creditBalance ?? 0;
  const creditsToGrant = isPackageMode && preview ? preview.credits : credits;
  const totalPrice = isPackageMode && preview
    ? preview.price
    : credits * FLAT_CREDIT_PRICE;
  const balanceAfter = currentBalance + creditsToGrant;
  const hasOpenOrder = Boolean(preview?.openOrder);

  return (
    <div className="max-w-3xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-6">
      {/* Header */}
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
          {isPackageMode
            ? 'Review your selected package credits and payment total before creating the payment link.'
            : 'Select the exact amount of credits you need. Flat rate of 20.000 ₫ per credit.'}
        </p>
      </div>

      {/* Mode toggle when a package was provided in query */}
      {hasPackageId && (
        <div className="flex items-center gap-3">
          <Button
            size="sm"
            variant={!useCustomSlider ? 'primary' : 'secondary'}
            onPress={() => setUseCustomSlider(false)}
          >
            <Package className="w-4 h-4 mr-1.5" />
            Selected Package
          </Button>
          <Button
            size="sm"
            variant={useCustomSlider ? 'primary' : 'secondary'}
            onPress={() => setUseCustomSlider(true)}
          >
            <Sliders className="w-4 h-4 mr-1.5" />
            Custom Credit Slider
          </Button>
        </div>
      )}

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
      {hasOpenOrder && preview?.openOrder && (
        <Alert status="warning">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Existing pending order detected</Alert.Title>
            <Alert.Description className="space-y-3">
              <p className="m-0">
                You already have an open order ({preview.openOrder.orderCode}) for{' '}
                <strong className="font-semibold text-foreground">
                  {preview.openOrder.packageName || `${preview.openOrder.credits} credits`}
                </strong>{' '}
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

      {/* Settlement Success Message */}
      {settlementSuccess && (
        <Alert status="success">
          <Alert.Indicator>
            <CheckCircle2 className="w-5 h-5 text-success" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Payment settled successfully!</Alert.Title>
            <Alert.Description>
              +{creditsToGrant} credits have been deposited into your wallet. Redirecting you to your destination...
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Main Checkout Card */}
      <Card className="shadow-surface">
        <Card.Header>
          <div className="flex items-center justify-between">
            <div>
              <Card.Title className="text-xl font-bold">
                {isPackageMode ? preview?.packageName || 'Credit Package' : `${credits} Platform Credits`}
              </Card.Title>
              <Card.Description>Instant digital credit grant upon payment confirmation</Card.Description>
            </div>
            <Chip size="md" color="accent" variant="soft" className="font-mono-tabular font-bold">
              +{creditsToGrant} credits
            </Chip>
          </div>
        </Card.Header>

        <Card.Content className="space-y-6">
          {/* Interactive Credit Slider Panel (when in custom credits mode) */}
          {!isPackageMode && (
            <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-4">
              <div className="flex items-center justify-between">
                <Label htmlFor="custom-credits-input" className="text-sm font-semibold text-foreground">
                  Credits to purchase
                </Label>
                <div className="flex items-center gap-2">
                  <input
                    id="custom-credits-input"
                    type="number"
                    min={1}
                    max={100}
                    value={credits}
                    onChange={handleInputChange}
                    aria-label="Custom credits count"
                    className="w-20 px-2 py-1 text-right font-mono font-bold rounded-md border border-border bg-background text-foreground text-sm focus:outline-none focus:ring-1 focus:ring-focus"
                  />
                  <span className="text-xs text-muted">credits</span>
                </div>
              </div>

              {/* HeroUI v3 Slider */}
              <div className="pt-2">
                <Slider
                  aria-label="Credit slider"
                  minValue={1}
                  maxValue={100}
                  step={1}
                  value={credits}
                  onChange={handleSliderChange}
                >
                  <div className="flex items-center justify-between text-xs text-muted mb-1">
                    <span>1 credit</span>
                    <Slider.Output className="font-mono-tabular font-semibold text-foreground" />
                    <span>100 credits</span>
                  </div>
                  <Slider.Track>
                    <Slider.Fill />
                    <Slider.Thumb />
                  </Slider.Track>
                </Slider>
              </div>

              {/* Quick preset chips */}
              <div className="flex items-center gap-2 pt-1">
                <span className="text-xs text-muted mr-1">Presets:</span>
                {PRESET_CREDITS.map((preset) => (
                  <Button
                    key={preset}
                    size="sm"
                    variant={credits === preset ? 'primary' : 'secondary'}
                    onPress={() => setCredits(preset)}
                    onClick={() => setCredits(preset)}
                    className="font-mono text-xs px-2.5 h-7 min-w-0"
                  >
                    {preset} credits
                  </Button>
                ))}
              </div>
            </div>
          )}

          {/* Pricing Breakdown Summary */}
          <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-3">
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted">Total amount:</span>
              <span className="font-mono-tabular font-bold text-foreground text-base">
                {formatVnd(totalPrice)}
              </span>
            </div>
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted">Unit price:</span>
              <span className="font-mono-tabular text-xs text-muted">
                {isPackageMode && preview
                  ? formatVndUnitPrice(totalPrice, creditsToGrant)
                  : `${formatVnd(FLAT_CREDIT_PRICE)} / credit`}
              </span>
            </div>
            <div className="border-t border-separator pt-3 flex items-center justify-between text-sm">
              <span className="text-muted">Current credit balance:</span>
              <span className="font-mono-tabular font-semibold text-foreground">
                {currentBalance} credits
              </span>
            </div>
            <div className="flex items-center justify-between text-sm font-semibold">
              <span className="text-foreground">Balance after purchase:</span>
              <span className="font-mono-tabular text-accent text-base font-bold">
                {balanceAfter} credits
              </span>
            </div>
          </div>

          {/* Security notice */}
          <div className="flex items-center gap-3 text-xs text-muted">
            <ShieldCheck className="w-4 h-4 text-success flex-shrink-0" />
            <span>
              Secure automated gateway checkout via VietQR / PayOS with instant ledger settlement.
            </span>
          </div>

          {/* Mock Simulator Panel (kept on checkout page when gateway is mock) */}
          {activeOrder && activeOrder.gateway?.toLowerCase() === 'mock' && !settlementSuccess && (
            <div className="p-4 rounded-lg bg-surface-secondary border border-dashed border-border space-y-3">
              <div className="flex items-center gap-2">
                <Sparkles className="w-4 h-4 text-warning" />
                <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                  Mock Gateway Simulator
                </span>
                <Chip size="sm" variant="soft" color="warning" className="font-mono text-xs">
                  {activeOrder.orderCode}
                </Chip>
              </div>
              <p className="text-xs text-muted m-0">
                You are testing with the mock gateway. You can trigger payment events immediately:
              </p>

              {mockActionError && (
                <p className="text-xs text-danger m-0">{mockActionError}</p>
              )}

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
                  onPress={() => {
                    if (activeOrder?.orderCode) {
                      void api.finance.cancelOrder(activeOrder.orderCode);
                    }
                    setActiveOrder(null);
                  }}
                >
                  Cancel order
                </Button>
              </div>
            </div>
          )}

          {/* Action Footer */}
          <div className="pt-2 flex flex-col sm:flex-row items-center justify-between gap-4">
            <Link to="/wallet" className="no-underline order-2 sm:order-1">
              <Button variant="ghost">
                <ArrowLeft className="w-4 h-4 mr-1.5" />
                Return to wallet
              </Button>
            </Link>

            <Button
              variant="primary"
              size="lg"
              className="w-full sm:w-auto order-1 sm:order-2"
              isDisabled={hasOpenOrder || Boolean(activeOrder && activeOrder.gateway === 'mock' && !settlementSuccess)}
              isPending={createOrderMutation.isPending}
              onPress={handleCreateOrder}
            >
              <CreditCard className="w-4 h-4 mr-2" />
              Proceed to payment ({formatVnd(totalPrice)})
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </div>
        </Card.Content>
      </Card>

      {/* PayOS Embedded VietQR Modal */}
      {isPayOsModalOpen && activeOrder && (
        <PayOsEmbeddedModal
          isOpen={isPayOsModalOpen}
          orderCode={activeOrder.orderCode}
          checkoutUrl={activeOrder.checkoutUrl || ''}
          amount={activeOrder.amount}
          credits={activeOrder.credits ?? creditsToGrant}
          returnUrl={returnUrl}
          onClose={() => {
            setIsPayOsModalOpen(false);
            setActiveOrder(null);
          }}
          onSuccess={() => {
            setSettlementSuccess(true);
          }}
        />
      )}
    </div>
  );
}
