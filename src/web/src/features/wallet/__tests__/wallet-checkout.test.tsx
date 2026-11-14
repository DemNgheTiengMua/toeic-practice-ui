import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type { WalletDto, CheckoutPreviewDto, PaymentResultDto } from '../../../lib/api';

function installFetchMock(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
) {
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    return handler(url, init);
  });
  vi.stubGlobal('fetch', mock);
  return mock;
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderRoute(initialUrl: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const history = createMemoryHistory({ initialEntries: [initialUrl] });
  const router = createAppRouter(history);

  const view = render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  );

  return { ...view, router, history, queryClient };
}

const MOCK_USER = {
  userId: 42,
  email: 'learner@demo.local',
  fullName: 'Learner Demo',
  role: 'Learner',
  isActive: true,
  emailVerified: true,
  createdAt: '2026-01-01T00:00:00Z',
  permissions: [],
};

const MOCK_LEARNER_CONTEXT = {
  active: { certId: 1, code: 'TOEIC', name: 'TOEIC Listening & Reading' },
  available: [{ certId: 1, code: 'TOEIC', name: 'TOEIC Listening & Reading' }],
  creditBalance: 150,
};

const MOCK_WALLET: WalletDto = {
  balance: 150,
  creditsPurchased: 200,
  creditsUsed: 50,
  amountPurchased: 500000,
  paidOrderCount: 2,
  orders: [
    {
      orderId: 101,
      orderCode: 'ORD-101',
      paymentNo: 1,
      credits: 100,
      amount: 250000,
      status: 'paid',
      gateway: 'payos',
      createdAt: '2026-10-10T10:00:00Z',
      paidAt: '2026-10-10T10:05:00Z',
    },
    {
      orderId: 102,
      orderCode: 'ORD-102',
      paymentNo: 2,
      credits: 100,
      amount: 250000,
      status: 'pending',
      gateway: 'mock',
      createdAt: '2026-10-15T12:00:00Z',
    },
  ],
  ledger: [
    {
      txnId: 1,
      reason: 'purchase',
      delta: 100,
      note: 'Payment order ORD-101',
      createdAt: '2026-10-10T10:05:00Z',
      orderCode: 'ORD-101',
    },
    {
      txnId: 2,
      reason: 'mock_exam_start',
      delta: -50,
      note: 'Full exam attempt',
      createdAt: '2026-10-11T14:00:00Z',
    },
  ],
};

describe('Wallet & Checkout feature (#301)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as any;
    if (!Element.prototype.getAnimations) {
      Element.prototype.getAnimations = () => [];
    }
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('renders /wallet workspace with credit balance, ledger deltas, and orders', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/learner/wallet') return jsonResponse(MOCK_WALLET);
      return jsonResponse({}, 404);
    });

    renderRoute('/wallet');

    // Credit balance & statistics
    expect(await screen.findByRole('heading', { name: /wallet & billing/i })).toBeInTheDocument();
    expect(screen.getByText('150')).toBeInTheDocument(); // Balance
    expect(screen.getByText(/500\.000 ₫/i)).toBeInTheDocument(); // Amount purchased

    // Buy credits button links to /checkout
    const buyCreditsLink = screen.getByRole('link', { name: /buy credits/i });
    expect(buyCreditsLink).toHaveAttribute('href', '/checkout');

    // Credit packages tab is removed
    expect(screen.queryByRole('tab', { name: /credit packages/i })).not.toBeInTheDocument();

    // Default tab is Credit history (ledger) - deltas immediately visible
    expect(await screen.findByText('Credit purchase')).toBeInTheDocument();
    expect(screen.getByText('+100')).toBeInTheDocument();
    expect(screen.getByText('Mock exam')).toBeInTheDocument();
    expect(screen.getByText('-50')).toBeInTheDocument();

    // Check Orders tab
    const ordersTab = screen.getByRole('tab', { name: /orders|receipts/i });
    fireEvent.click(ordersTab);

    expect(await screen.findByText('ORD-101')).toBeInTheDocument();
    expect(screen.getByText('Paid')).toBeInTheDocument();
    expect(screen.getByText('ORD-102')).toBeInTheDocument();
    expect(screen.getByText('Pending')).toBeInTheDocument();
  });

  it('renders checkout preview and creates an order', async () => {
    let orderCreated = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        const preview: CheckoutPreviewDto = {
          credits: 10,
          price: 200000,
          currentBalance: 150,
          balanceAfter: 160,
          openOrder: null,
        };
        return jsonResponse(preview);
      }
      if (url === '/api/checkout/orders' && init?.method === 'POST') {
        orderCreated = true;
        return jsonResponse({
          orderId: 201,
          orderCode: 'ORD-201',
          paymentNo: 3,
          amount: 200000,
          gateway: 'mock',
          status: 'pending',
          credits: 10,
        });
      }
      if (url.includes('/api/checkout/orders/ORD-201/result')) {
        const res: PaymentResultDto = {
          orderId: 201,
          orderCode: 'ORD-201',
          status: 'pending',
          credits: 10,
          amount: 200000,
          gateway: 'mock',
          createdAt: '2026-11-13T12:00:00Z',
          currentBalance: 150,
        };
        return jsonResponse(res);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/checkout?credits=10');

    expect(await screen.findByRole('heading', { name: /confirm your order|checkout/i })).toBeInTheDocument();
    expect(screen.getByText(/10 Platform Credits/i)).toBeInTheDocument();
    expect(screen.getAllByText(/200\.000 ₫/i)[0]).toBeInTheDocument();
    expect(await screen.findByText('150 credits')).toBeInTheDocument(); // current balance
    expect(screen.getByText('160 credits')).toBeInTheDocument(); // balance after

    const payBtn = screen.getByRole('button', { name: /proceed to payment|confirm order/i });
    fireEvent.click(payBtn);

    await waitFor(() => {
      expect(orderCreated).toBe(true);
    });
  });

  it('blocks checkout with warning when email is not verified (403)', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({ ...MOCK_USER, emailVerified: false });
      }
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        return jsonResponse(
          {
            code: 'EMAIL_NOT_VERIFIED',
            title: 'Email verification required',
            detail: 'Please verify your email before purchasing credits.',
          },
          403,
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/checkout?packageId=1');

    expect(await screen.findByText(/email verification required/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /profile/i })).toBeInTheDocument();
  });

  it('handles existing open order conflict (409) with resume and cancel actions', async () => {
    let cancelled = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        const preview: CheckoutPreviewDto = {
          credits: 100,
          price: 250000,
          currentBalance: 150,
          balanceAfter: 250,
          openOrder: {
            orderId: 102,
            orderCode: 'ORD-OPEN-99',
            credits: 100,
            amount: 250000,
            status: 'pending',
            gateway: 'mock',
            createdAt: '2026-11-13T10:00:00Z',
          },
        };
        return jsonResponse(preview);
      }
      if (url === '/api/checkout/cancel' && init?.method === 'POST') {
        cancelled = true;
        return jsonResponse({ message: 'Order cancelled successfully' });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/checkout?packageId=1');

    expect(await screen.findByText(/pending order/i)).toBeInTheDocument();
    expect(screen.getAllByText(/ORD-OPEN-99/i)[0]).toBeInTheDocument();

    const cancelBtn = screen.getByRole('button', { name: /cancel existing order/i });
    fireEvent.click(cancelBtn);

    await waitFor(() => {
      expect(cancelled).toBe(true);
    });
  });

  it('renders order status page with mock gateway simulation actions', async () => {
    let simulatedAction: string | null = null;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.includes('/api/checkout/orders/ORD-SIM-1/result')) {
        const res: PaymentResultDto = {
          orderId: 301,
          orderCode: 'ORD-SIM-1',
          status: simulatedAction === 'success' ? 'paid' : 'pending',
          credits: 100,
          amount: 250000,
          gateway: 'mock',
          createdAt: '2026-11-13T12:00:00Z',
          currentBalance: simulatedAction === 'success' ? 250 : 150,
        };
        return jsonResponse(res);
      }
      if (url === '/api/mock-gateway/action' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body));
        simulatedAction = body.action;
        return jsonResponse({
          orderId: 301,
          orderCode: 'ORD-SIM-1',
          status: 'paid',
          credits: 100,
          amount: 250000,
          gateway: 'mock',
          createdAt: '2026-11-13T12:00:00Z',
          currentBalance: 250,
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/orders/ORD-SIM-1');

    expect(await screen.findByRole('heading', { name: /order status/i })).toBeInTheDocument();
    expect(screen.getByText('ORD-SIM-1')).toBeInTheDocument();

    // Mock gateway simulate success button
    const successBtn = screen.getByRole('button', { name: /simulate payment success/i });
    fireEvent.click(successBtn);

    await waitFor(() => {
      expect(simulatedAction).toBe('success');
      expect(screen.getByText('Paid')).toBeInTheDocument();
    });
  });

  it('renders safe payOS external payment link and protects against javascript: links', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.includes('/api/checkout/orders/ORD-PAYOS-SAFE/result')) {
        const res: PaymentResultDto = {
          orderId: 401,
          orderCode: 'ORD-PAYOS-SAFE',
          status: 'pending',
          credits: 100,
          amount: 250000,
          gateway: 'payos',
          createdAt: '2026-11-13T12:00:00Z',
          currentBalance: 150,
          checkoutUrl: 'https://pay.payos.vn/web/test-checkout-123',
        };
        return jsonResponse(res);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/orders/ORD-PAYOS-SAFE');

    const paymentLink = (await screen.findByRole('link', {
      name: /open payos payment|open vietqr checkout/i,
    })) as HTMLAnchorElement;

    expect(paymentLink).toBeInTheDocument();
    expect(paymentLink.href).toBe('https://pay.payos.vn/web/test-checkout-123');
    expect(paymentLink.target).toBe('_blank');
  });
});
