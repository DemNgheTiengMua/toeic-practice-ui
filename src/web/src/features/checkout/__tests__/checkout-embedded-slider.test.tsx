import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type { CheckoutPreviewDto, CreateOrderResponseDto, PaymentResultDto } from '../../../lib/api';

let capturedPayOSConfig: any = null;
const mockOpen = vi.fn();
const mockExit = vi.fn();

vi.mock('@payos/payos-checkout', () => ({
  usePayOS: (config: any) => {
    capturedPayOSConfig = config;
    return {
      open: mockOpen,
      exit: mockExit,
    };
  },
}));

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
  creditBalance: 25,
};

describe('PayOS Embedded Checkout & Interactive Credit Slider (#317)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    capturedPayOSConfig = null;
    mockOpen.mockClear();
    mockExit.mockClear();
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

  it('renders interactive slider with default 5 credits, dynamic 20k/credit pricing, and quick preset chips', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        const preview: CheckoutPreviewDto = {
          credits: 5,
          price: 100000,
          currentBalance: 25,
          balanceAfter: 30,
        };
        return jsonResponse(preview);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/checkout');

    expect(await screen.findByRole('heading', { name: /confirm your order|checkout/i })).toBeInTheDocument();
    expect(await screen.findByText('25 credits')).toBeInTheDocument(); // current balance
    expect(screen.getByText('5 Platform Credits')).toBeInTheDocument();
    expect(screen.getAllByText(/100\.000 ₫/i)[0]).toBeInTheDocument();
    expect(screen.getByText('30 credits')).toBeInTheDocument(); // balance after

    // Custom number input is synchronized
    const numberInput = screen.getByLabelText(/custom credits count/i) as HTMLInputElement;
    expect(numberInput.value).toBe('5');

    // Click quick preset chip "20 credits"
    const preset20Btn = screen.getByRole('button', { name: /^20 credits$/i });
    fireEvent.click(preset20Btn);

    // Number input and heading update to 20
    expect(numberInput.value).toBe('20');
    expect(screen.getByText('20 Platform Credits')).toBeInTheDocument();
    // 20 * 20,000 = 400,000 VND
    expect(screen.getAllByText(/400\.000 ₫/i)[0]).toBeInTheDocument();
    expect(screen.getByText('45 credits')).toBeInTheDocument(); // balance after

    // Type 15 into custom number input
    fireEvent.change(numberInput, { target: { value: '15' } });
    expect(screen.getByText('15 Platform Credits')).toBeInTheDocument();
    expect(screen.getAllByText(/300\.000 ₫/i)[0]).toBeInTheDocument();
    expect(screen.getByText('40 credits')).toBeInTheDocument(); // balance after
  });

  it('honors query params ?credits=10&returnUrl=/practice and creates custom credit order', async () => {
    let capturedBody: any = null;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        const preview: CheckoutPreviewDto = {
          credits: 10,
          price: 200000,
          currentBalance: 25,
          balanceAfter: 35,
        };
        return jsonResponse(preview);
      }
      if (url === '/api/checkout/orders' && init?.method === 'POST') {
        capturedBody = JSON.parse(String(init.body));
        const res: CreateOrderResponseDto = {
          orderId: 901,
          orderCode: 'ORD-PAYOS-901',
          paymentNo: 1,
          amount: 200000,
          gateway: 'payos',
          checkoutUrl: 'https://pay.payos.vn/web/test-link-901',
          status: 'pending',
          credits: 10,
        };
        return jsonResponse(res);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/checkout?credits=10&returnUrl=/practice');

    expect(await screen.findByRole('heading', { name: /confirm your order|checkout/i })).toBeInTheDocument();
    expect(screen.getByText('10 Platform Credits')).toBeInTheDocument();
    expect(screen.getAllByText(/200\.000 ₫/i)[0]).toBeInTheDocument();

    const payBtn = screen.getByRole('button', { name: /proceed to payment/i });
    fireEvent.click(payBtn);

    await waitFor(() => {
      expect(capturedBody).toEqual({ credits: 10 });
    });

    // PayOS embedded modal opens with container
    expect(await screen.findByRole('heading', { name: /vietqr \/ payos checkout/i })).toBeInTheDocument();
    const embeddedContainer = document.getElementById('payos-embedded-container');
    expect(embeddedContainer).toBeInTheDocument();
    expect(embeddedContainer?.style.minHeight).toBe('350px');
  });

  it('handles PayOS onSuccess: displays positive confirmation, invalidates wallet, and navigates to returnUrl', async () => {
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        const preview: CheckoutPreviewDto = {
          credits: 5,
          price: 100000,
          currentBalance: 25,
          balanceAfter: 30,
        };
        return jsonResponse(preview);
      }
      if (url === '/api/checkout/orders' && init?.method === 'POST') {
        const res: CreateOrderResponseDto = {
          orderId: 902,
          orderCode: 'ORD-PAYOS-902',
          paymentNo: 2,
          amount: 100000,
          gateway: 'payos',
          checkoutUrl: 'https://pay.payos.vn/web/test-link-902',
          status: 'pending',
          credits: 5,
        };
        return jsonResponse(res);
      }
      return jsonResponse({}, 404);
    });

    const { history } = renderRoute('/checkout?credits=5&returnUrl=/practice');

    expect(await screen.findByRole('heading', { name: /confirm your order|checkout/i })).toBeInTheDocument();
    const payBtn = screen.getByRole('button', { name: /proceed to payment/i });
    fireEvent.click(payBtn);

    expect(await screen.findByRole('heading', { name: /vietqr \/ payos checkout/i })).toBeInTheDocument();
    expect(capturedPayOSConfig).not.toBeNull();
    expect(capturedPayOSConfig.CHECKOUT_URL).toBe('https://pay.payos.vn/web/test-link-902');
    expect(capturedPayOSConfig.embedded).toBe(true);

    // Simulate PayOS payment success callback
    capturedPayOSConfig.onSuccess({ id: 'test-link-902', status: 'PAID' });

    // Displays positive confirmation in modal
    expect(await screen.findByRole('heading', { name: /payment successful!/i })).toBeInTheDocument();

    // After 1.5s timeout, navigates to returnUrl
    await waitFor(
      () => {
        expect(history.location.pathname).toBe('/practice');
      },
      { timeout: 3000 },
    );
  });

  it('provides explicit Cancel button in PayOS modal that exits and calls cancelOrder', async () => {
    let orderCancelled = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        const preview: CheckoutPreviewDto = {
          credits: 5,
          price: 100000,
          currentBalance: 25,
          balanceAfter: 30,
        };
        return jsonResponse(preview);
      }
      if (url === '/api/checkout/orders' && init?.method === 'POST') {
        const res: CreateOrderResponseDto = {
          orderId: 903,
          orderCode: 'ORD-PAYOS-903',
          paymentNo: 3,
          amount: 100000,
          gateway: 'payos',
          checkoutUrl: 'https://pay.payos.vn/web/test-link-903',
          status: 'pending',
          credits: 5,
        };
        return jsonResponse(res);
      }
      if (url === '/api/checkout/cancel' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body));
        if (body.orderCode === 'ORD-PAYOS-903') {
          orderCancelled = true;
        }
        return jsonResponse({ message: 'Order cancelled successfully' });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/checkout?credits=5');

    expect(await screen.findByRole('heading', { name: /confirm your order|checkout/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /proceed to payment/i }));

    expect(await screen.findByRole('heading', { name: /vietqr \/ payos checkout/i })).toBeInTheDocument();

    const cancelModalBtn = screen.getByRole('button', { name: /cancel \/ close/i });
    fireEvent.click(cancelModalBtn);

    await waitFor(() => {
      expect(orderCancelled).toBe(true);
      expect(mockExit).toHaveBeenCalled();
      expect(screen.queryByRole('heading', { name: /vietqr \/ payos checkout/i })).not.toBeInTheDocument();
    });
  });

  it('keeps Mock Simulator panel on checkout page when gateway is mock and navigates after 1.5s on simulate success', async () => {
    let simulatedAction: string | null = null;

    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/checkout/preview')) {
        const preview: CheckoutPreviewDto = {
          credits: 10,
          price: 200000,
          currentBalance: 25,
          balanceAfter: 35,
        };
        return jsonResponse(preview);
      }
      if (url === '/api/checkout/orders' && init?.method === 'POST') {
        const res: CreateOrderResponseDto = {
          orderId: 904,
          orderCode: 'ORD-MOCK-904',
          paymentNo: 4,
          amount: 200000,
          gateway: 'mock',
          status: 'pending',
          credits: 10,
        };
        return jsonResponse(res);
      }
      if (url === '/api/mock-gateway/action' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body));
        simulatedAction = body.action;
        const res: PaymentResultDto = {
          orderId: 904,
          orderCode: 'ORD-MOCK-904',
          status: 'paid',
          packageName: '10 Platform Credits',
          credits: 10,
          amount: 200000,
          gateway: 'mock',
          createdAt: '2026-11-14T10:00:00Z',
          currentBalance: 35,
          packageId: null,
        };
        return jsonResponse(res);
      }
      return jsonResponse({}, 404);
    });

    const { history } = renderRoute('/checkout?credits=10&returnUrl=/practice');

    expect(await screen.findByRole('heading', { name: /confirm your order|checkout/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /proceed to payment/i }));

    // Mock Simulator appears on page
    expect(await screen.findByText(/mock gateway simulator/i)).toBeInTheDocument();
    expect(screen.getByText('ORD-MOCK-904')).toBeInTheDocument();

    const simulateSuccessBtn = screen.getByRole('button', { name: /simulate payment success/i });
    fireEvent.click(simulateSuccessBtn);

    await waitFor(() => {
      expect(simulatedAction).toBe('success');
    });

    expect(await screen.findByText(/payment settled successfully!/i)).toBeInTheDocument();

    // After 1.5s timeout, navigates to returnUrl
    await waitFor(
      () => {
        expect(history.location.pathname).toBe('/practice');
      },
      { timeout: 3000 },
    );
  });
});
