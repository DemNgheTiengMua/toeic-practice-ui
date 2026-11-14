import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../../context/AuthContext';
import { createAppRouter } from '../../../../router';
import { STAFF_PERMISSIONS } from '../../../../lib/security';
import type {
  AdminOrdersSummaryDto,
  AdminOrderDetailDto,
  AdminRefundsSummaryDto,
  AdminRefundDetailDto,
  AdminPackagesSummaryDto,
  AuthUserDto,
} from '../../../../lib/api';

let activeUser: AuthUserDto | null = null;

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

const SAMPLE_ORDERS_SUMMARY: AdminOrdersSummaryDto = {
  orders: [
    {
      orderId: 101,
      orderCode: 'DH-1001',
      paymentNo: 1001,
      userId: 5,
      userEmail: 'learner1@test.local',
      userFullName: 'Nguyễn Văn A',
      packageId: 1,
      packageName: 'Starter Pack',
      credits: 5,
      amount: 199000,
      status: 'paid',
      gateway: 'payos',
      gatewayTxnRef: 'TXN-9988',
      createdAt: '2026-11-01T10:00:00Z',
      paidAt: '2026-11-01T10:05:00Z',
    },
    {
      orderId: 102,
      orderCode: 'DH-1002',
      paymentNo: 1002,
      userId: 6,
      userEmail: 'learner2@test.local',
      userFullName: 'Trần Thị B',
      packageId: 2,
      packageName: 'Standard Pack',
      credits: 15,
      amount: 499000,
      status: 'reconciling',
      gateway: 'payos',
      createdAt: '2026-11-02T14:30:00Z',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 25,
  paidThisMonthCount: 1,
  paidThisMonthAmount: 199000,
  reconcilingCount: 1,
  failedOrExpiredCount: 0,
};

const SAMPLE_ORDER_DETAIL: AdminOrderDetailDto = {
  order: SAMPLE_ORDERS_SUMMARY.orders[1],
  events: [
    {
      eventId: 501,
      orderId: 102,
      source: 'webhook',
      reportedStatus: 'pending',
      signatureValid: true,
      wasApplied: true,
      note: 'Webhook received pending status',
      createdAt: '2026-11-02T14:31:00Z',
    },
  ],
};

const SAMPLE_REFUNDS_SUMMARY: AdminRefundsSummaryDto = {
  refunds: [
    {
      refundId: 201,
      userId: 7,
      learnerName: 'Lê Văn C',
      learnerEmail: 'learner3@test.local',
      method: 'money',
      status: 'approved',
      creditCount: 0,
      amount: 150000,
      reason: 'Mentor failed to attend session',
      orderId: 101,
      orderCode: 'DH-1001',
      createdAt: '2026-11-05T08:00:00Z',
      decidedAt: '2026-11-05T09:00:00Z',
    },
    {
      refundId: 202,
      userId: 8,
      learnerName: 'Phạm Thị D',
      learnerEmail: 'learner4@test.local',
      method: 'credit',
      status: 'requested',
      creditCount: 3,
      amount: 0,
      reason: 'AI grading inaccuracy dispute',
      createdAt: '2026-11-06T11:00:00Z',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 25,
  requestedCount: 1,
  approvedCount: 1,
  rejectedCount: 0,
  creditsRefundedTotal: 10,
};

const SAMPLE_REFUND_DETAIL_REQUESTED: AdminRefundDetailDto = {
  refundId: 202,
  userId: 8,
  learnerName: 'Phạm Thị D',
  learnerEmail: 'learner4@test.local',
  method: 'credit',
  status: 'requested',
  creditCount: 3,
  amount: 0,
  reason: 'AI grading inaccuracy dispute',
  createdAt: '2026-11-06T11:00:00Z',
  requestedByUserId: 1,
  requestedByUserName: 'Staff Support',
};

const SAMPLE_REFUND_DETAIL_APPROVED_MONEY: AdminRefundDetailDto = {
  refundId: 201,
  userId: 7,
  learnerName: 'Lê Văn C',
  learnerEmail: 'learner3@test.local',
  method: 'money',
  status: 'approved',
  creditCount: 0,
  amount: 150000,
  reason: 'Mentor failed to attend session',
  orderId: 101,
  orderCode: 'DH-1001',
  orderPaidAmount: 199000,
  createdAt: '2026-11-05T08:00:00Z',
  requestedByUserId: 1,
  requestedByUserName: 'Staff Support',
  decidedByUserId: 2,
  decidedByUserName: 'Admin Manager',
  decidedAt: '2026-11-05T09:00:00Z',
};

const SAMPLE_PACKAGES_SUMMARY: AdminPackagesSummaryDto = {
  packages: [
    {
      packageId: 1,
      name: 'Starter Pack',
      credits: 5,
      price: 199000,
      isActive: true,
      isFeatured: false,
      features: '5 practice sessions\n1 mock exam',
      displayOrder: 1,
      ordersSold: 42,
      totalRevenue: 8358000,
    },
    {
      packageId: 2,
      name: 'Standard Pack',
      credits: 15,
      price: 499000,
      isActive: true,
      isFeatured: true,
      features: '15 practice sessions\n3 mock exams\nPriority support',
      displayOrder: 2,
      ordersSold: 18,
      totalRevenue: 8982000,
    },
  ],
  packagesOnSale: 2,
  featuredCount: 1,
  totalOrdersSold: 60,
  totalRevenue: 17340000,
};

function renderRoute(initialUrl = '/admin/finance', user: AuthUserDto | null = null) {
  activeUser = user;
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

  return { ...view, router, history };
}

describe('AdminFinancePage (#304)', () => {
  let fetchMock: ReturnType<typeof installFetchMock>;
  let createObjectUrlMock: ReturnType<typeof vi.fn>;
  let revokeObjectUrlMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createObjectUrlMock = vi.fn().mockReturnValue('blob:test');
    revokeObjectUrlMock = vi.fn();
    if (typeof window !== 'undefined') {
      window.URL.createObjectURL = createObjectUrlMock;
      window.URL.revokeObjectURL = revokeObjectUrlMock;
      window.scrollTo = vi.fn();
      HTMLAnchorElement.prototype.click = vi.fn();
    }
    if (typeof Element !== 'undefined' && !Element.prototype.getAnimations) {
      Element.prototype.getAnimations = () => [];
    }
    (global as any).ResizeObserver = class ResizeObserver {
      observe() {}
      unobserve() {}
      disconnect() {}
    };

    fetchMock = installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        if (!activeUser) return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
        return jsonResponse(activeUser);
      }
      if (url === '/api/learner/context') {
        return jsonResponse({}, 404);
      }
      if (url.startsWith('/api/admin/orders/102/reconcile')) {
        return jsonResponse({
          ...SAMPLE_ORDERS_SUMMARY.orders[1],
          status: 'paid',
        });
      }
      if (url.startsWith('/api/admin/orders/102')) {
        return jsonResponse(SAMPLE_ORDER_DETAIL);
      }
      if (url.startsWith('/api/admin/orders')) {
        return jsonResponse(SAMPLE_ORDERS_SUMMARY);
      }
      if (url.startsWith('/api/admin/refunds/export')) {
        const csvText =
          '\uFEFFRefund ID,Requested on,Settled on,Learner email,Learner name,Order code,Method,Money amount,Credit count,Reason,Decided by,Exported at\r\n' +
          '201,2026-11-05,,learner3@test.local,Lê Văn C,DH-1001,money,150000.00,,Mentor failed to attend session,Admin Manager,2026-11-05 09:00\r\n';
        return new Response(csvText, {
          status: 200,
          headers: {
            'Content-Type': 'text/csv',
            'Content-Disposition': 'attachment; filename="refunds-2026-11-01-to-2026-11-07.csv"',
          },
        });
      }
      if (url.startsWith('/api/admin/refunds/202/approve')) {
        return jsonResponse({
          ...SAMPLE_REFUND_DETAIL_REQUESTED,
          status: 'approved',
          decidedAt: new Date().toISOString(),
        });
      }
      if (url.startsWith('/api/admin/refunds/202/reject')) {
        return jsonResponse({
          ...SAMPLE_REFUND_DETAIL_REQUESTED,
          status: 'rejected',
          decidedAt: new Date().toISOString(),
        });
      }
      if (url.startsWith('/api/admin/refunds/201/settle')) {
        return jsonResponse({
          ...SAMPLE_REFUND_DETAIL_APPROVED_MONEY,
          status: 'settled',
          settledAt: new Date().toISOString(),
        });
      }
      if (url.startsWith('/api/admin/refunds/202')) {
        return jsonResponse(SAMPLE_REFUND_DETAIL_REQUESTED);
      }
      if (url.startsWith('/api/admin/refunds/201')) {
        return jsonResponse(SAMPLE_REFUND_DETAIL_APPROVED_MONEY);
      }
      if (url.startsWith('/api/admin/refunds') && init?.method === 'POST') {
        return jsonResponse({
          ...SAMPLE_REFUND_DETAIL_REQUESTED,
          refundId: 203,
        });
      }
      if (url.startsWith('/api/admin/refunds')) {
        return jsonResponse(SAMPLE_REFUNDS_SUMMARY);
      }
      if (url.startsWith('/api/admin/packages/1/deactivate')) {
        return new Response(null, { status: 204 });
      }
      if (url.startsWith('/api/admin/packages') && init?.method === 'POST') {
        const body = JSON.parse(init.body as string);
        return jsonResponse({
          packageId: 3,
          ...body,
          ordersSold: 0,
          totalRevenue: 0,
        });
      }
      if (url.startsWith('/api/admin/packages')) {
        return jsonResponse(SAMPLE_PACKAGES_SUMMARY);
      }
      return jsonResponse({ error: 'Not found' }, 404);
    });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('renders access denied card when user lacks billing capabilities', async () => {
    renderRoute('/admin/finance', {
      userId: 99,
      email: 'unauth@test.local',
      fullName: 'No Perms',
      role: 'staff',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.ContentAuthor],
    });

    expect(await screen.findByText(/access restricted/i)).toBeInTheDocument();
  });

  it('allows SupportTriage to access refunds tab only', async () => {
    renderRoute('/admin/finance', {
      userId: 10,
      email: 'support@test.local',
      fullName: 'Support Staff',
      role: 'staff',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SupportTriage],
    });

    expect(await screen.findByRole('tab', { name: /refunds desk/i })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /orders/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /credit packages/i })).not.toBeInTheDocument();
  });

  it('renders orders tab with summary tiles, filters and reconcile action for SystemAdmin', async () => {
    renderRoute('/admin/finance', {
      userId: 1,
      email: 'admin@test.local',
      fullName: 'Super Admin',
      role: 'administrator',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SystemAdmin],
    });

    expect(await screen.findByText('DH-1001')).toBeInTheDocument();

    // Verify summary stats (vi-VN uses . as thousand separator)
    expect(screen.getAllByText(/199\.000/).length).toBeGreaterThan(0);
    expect(screen.getByText('DH-1002')).toBeInTheDocument();

    // Verify sentence-case status labels (no raw wire codes!)
    expect(screen.getAllByText('Confirming with the gateway').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Paid').length).toBeGreaterThan(0);

    // Open detail modal for reconciling order
    const viewButtons = screen.getAllByRole('button', { name: /view/i });
    fireEvent.click(viewButtons[1]);

    expect(await screen.findByText(/order details — dh-1002/i)).toBeInTheDocument();

    // Detail modal shows payment events timeline
    expect(await screen.findByText('Gateway webhook')).toBeInTheDocument();
    expect(screen.getByText(/Webhook received pending status/)).toBeInTheDocument();

    // Click Reconcile button
    const reconcileBtn = screen.getByRole('button', { name: /reconcile with gateway/i });
    fireEvent.click(reconcileBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/orders/102/reconcile'),
        expect.anything(),
      );
    });
  });

  it('handles refunds desk: validation, approval, rejection, and settlement', async () => {
    renderRoute('/admin/finance', {
      userId: 1,
      email: 'admin@test.local',
      fullName: 'Super Admin',
      role: 'administrator',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SystemAdmin, STAFF_PERMISSIONS.SupportTriage],
    });

    // Switch to Refunds desk tab
    const refundsTab = await screen.findByRole('tab', { name: /refunds desk/i });
    fireEvent.click(refundsTab);

    expect(await screen.findByText('Lê Văn C')).toBeInTheDocument();
    expect(screen.getByText('Phạm Thị D')).toBeInTheDocument();
    expect(screen.getByText('AI grading inaccuracy dispute')).toBeInTheDocument();

    // Verify status labels
    expect(screen.getAllByText('Requested').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Approved').length).toBeGreaterThan(0);

    // 1. Create refund modal validation
    const createBtn = screen.getByRole('button', { name: /new refund request/i });
    fireEvent.click(createBtn);

    expect(await screen.findByText(/create refund request/i)).toBeInTheDocument();

    // Click submit with empty fields -> surfaces validation
    const submitBtn = screen.getByRole('button', { name: /submit refund request/i });
    fireEvent.click(submitBtn);

    expect(await screen.findByText(/valid learner user id is required/i)).toBeInTheDocument();

    // Fill valid data and submit
    fireEvent.change(screen.getByLabelText(/learner user id/i), { target: { value: '12' } });
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: 'Technical disconnection during test' } });
    fireEvent.change(screen.getByLabelText(/credits to refund/i), { target: { value: '5' } });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/refunds'),
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // 2. Open requested refund detail and reject with reason
    const viewButtons = screen.getAllByRole('button', { name: /view/i });
    fireEvent.click(viewButtons[1]); // Refund 202 (requested)

    expect(await screen.findByText(/refund details — #202/i)).toBeInTheDocument();

    const rejectBtn = await screen.findByRole('button', { name: /reject refund/i });
    fireEvent.click(rejectBtn);

    // Rejection requires reason
    expect(await screen.findByText(/provide rejection reason/i)).toBeInTheDocument();
    const reasonInput = screen.getByLabelText(/rejection reason/i);
    fireEvent.change(reasonInput, { target: { value: 'Grading was verified correct by senior reviewer' } });

    const confirmRejectBtn = screen.getByRole('button', { name: /confirm rejection/i });
    fireEvent.click(confirmRejectBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/refunds/202/reject'),
        expect.anything(),
      );
    });

    // 3. Open approved money refund and settle
    fireEvent.click(viewButtons[0]); // Refund 201 (approved money)
    expect(await screen.findByText(/refund details — #201/i)).toBeInTheDocument();

    const settleBtn = await screen.findByRole('button', { name: /record bank settlement/i });
    fireEvent.click(settleBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/refunds/201/settle'),
        expect.anything(),
      );
    });
  });

  it('exports UTF-8 BOM CSV for approved money refunds', async () => {
    renderRoute('/admin/finance', {
      userId: 1,
      email: 'admin@test.local',
      fullName: 'Super Admin',
      role: 'administrator',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SupportTriage],
    });

    const exportBtn = await screen.findByRole('button', { name: /export csv/i });
    fireEvent.click(exportBtn);

    // Modal opens for date range selection
    expect(await screen.findByText(/export accounting csv/i)).toBeInTheDocument();
    const downloadBtn = screen.getByRole('button', { name: /download csv/i });
    fireEvent.click(downloadBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/refunds/export'),
        expect.anything(),
      );
      expect(createObjectUrlMock).toHaveBeenCalled();
    });
  });

  it('manages credit packages: creates package with unit price calculation and deactivates package', async () => {
    renderRoute('/admin/finance', {
      userId: 1,
      email: 'admin@test.local',
      fullName: 'Super Admin',
      role: 'administrator',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SystemAdmin],
    });

    // Switch to Credit packages tab
    const packagesTab = await screen.findByRole('tab', { name: /credit packages/i });
    fireEvent.click(packagesTab);

    expect(await screen.findByText('Starter Pack')).toBeInTheDocument();
    expect(screen.getByText('Standard Pack')).toBeInTheDocument();
    expect(screen.getByText(/39\.800 ₫\/credit/)).toBeInTheDocument(); // 199.000 / 5

    // Open create package modal
    const createPkgBtn = screen.getByRole('button', { name: /create package/i });
    fireEvent.click(createPkgBtn);

    expect(await screen.findByText(/create credit package/i)).toBeInTheDocument();

    // Type name, credits, and price -> check live unit price calculation
    fireEvent.change(screen.getByLabelText(/package name/i), { target: { value: 'Master Pack' } });
    fireEvent.change(screen.getByLabelText(/credits/i), { target: { value: '50' } });
    fireEvent.change(screen.getByLabelText(/price/i), { target: { value: '1500000' } });

    // 1,500,000 / 50 = 30,000 ₫/credit
    expect(screen.getByText(/30\.000 ₫\/credit/i)).toBeInTheDocument();

    const saveBtn = screen.getByRole('button', { name: /save package/i });
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/packages'),
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // Deactivate package triggers confirmation dialog
    const deactivateBtns = screen.getAllByRole('button', { name: /deactivate/i });
    fireEvent.click(deactivateBtns[0]);

    expect(await screen.findByText(/deactivate package\?/i)).toBeInTheDocument();
    const confirmDeactivate = screen.getByRole('button', { name: /confirm deactivation/i });
    fireEvent.click(confirmDeactivate);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/packages/1/deactivate'),
        expect.anything(),
      );
    });
  });
});

