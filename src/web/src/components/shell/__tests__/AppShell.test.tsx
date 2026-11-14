import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import { STAFF_PERMISSIONS } from '../../../lib/security';

function installFetchMock(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
) {
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url =
      typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
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

  return { ...view, router, history };
}

function makeAdminDashboard() {
  return {
    activeLearnersCount: 10,
    newLearners7DaysCount: 2,
    creditsUsedThisMonth: 100,
    creditsRefundedThisMonth: 0,
    creditsPerDayAverage: 5,
    revenueTodayVnd: 0,
    revenueThisMonthVnd: 0,
    paidOrdersThisMonthCount: 0,
    mostAttemptedExams: [],
    needsAttention: {
      draftExamsCount: 0,
      stalePendingOrdersCount: 0,
      openComplaintsCount: 0,
    },
  };
}

function makeAdminRevision() {
  return {
    paidOrdersCount: 0,
    paidOrdersAmount: 0,
    complaintsOpen: 0,
    complaintsReviewing: 0,
    complaintsResolved: 0,
    refundsRequested: 0,
    refundsApproved: 0,
    refundsSettled: 0,
    refundsRejected: 0,
    settledMoneyRefundsCount: 0,
    settledMoneyRefundsAmount: 0,
  };
}

describe('AppShell layout and domain copy', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('pins the sidebar flush to the left edge with sticky desktop scroll and spans header full width', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({
          userId: 1,
          email: 'admin@demo.local',
          fullName: 'Admin User',
          role: 'admin',
          emailVerified: true,
          permissions: Object.values(STAFF_PERMISSIONS),
        });
      }
      if (url.endsWith('/api/admin/dashboard')) {
        return jsonResponse(makeAdminDashboard());
      }
      if (url.endsWith('/api/admin/dashboard/revision')) {
        return jsonResponse(makeAdminRevision());
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin');

    const aside = await screen.findByRole('complementary');
    const shellBodyWrapper = aside.parentElement;
    expect(shellBodyWrapper).not.toBeNull();
    expect(shellBodyWrapper?.className).toContain('w-full');
    expect(shellBodyWrapper?.className).not.toContain('max-w-[1440px]');
    expect(shellBodyWrapper?.className).not.toContain('mx-auto');

    expect(aside.className).toContain('lg:sticky');
    expect(aside.className).toContain('lg:top-14');
    expect(aside.className).toContain('lg:h-[calc(100vh-3.5rem)]');
    expect(aside.className).toContain('lg:overflow-y-auto');

    const header = screen.getByRole('banner');
    const headerInner = header.firstElementChild;
    expect(headerInner?.className).toContain('w-full');
    expect(headerInner?.className).not.toContain('max-w-[1440px]');
    expect(headerInner?.className).not.toContain('mx-auto');

    const main = screen.getByRole('main');
    const mainInner = main.firstElementChild;
    expect(mainInner?.className).toContain('max-w-[1440px]');
    expect(mainInner?.className).toContain('mx-auto');
  });

  it('displays clean "Admin" label for governance admin and never shows "caps" jargon', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({
          userId: 1,
          email: 'admin@demo.local',
          fullName: 'Admin User',
          role: 'admin',
          emailVerified: true,
          permissions: Object.values(STAFF_PERMISSIONS),
        });
      }
      if (url.endsWith('/api/admin/dashboard')) {
        return jsonResponse(makeAdminDashboard());
      }
      if (url.endsWith('/api/admin/dashboard/revision')) {
        return jsonResponse(makeAdminRevision());
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin');

    const header = await screen.findByRole('banner');
    await waitFor(() => {
      expect(within(header).getByText('Admin')).toBeInTheDocument();
    });
    expect(header.textContent ?? '').not.toMatch(/caps/i);
    expect(header.textContent ?? '').not.toMatch(/Staff ·/i);
  });

  it('displays clean "Staff" label for non-governance staff and never shows "caps" jargon', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({
          userId: 99,
          email: 'reviewer@demo.local',
          fullName: 'Reviewer User',
          role: 'Staff',
          emailVerified: true,
          permissions: [STAFF_PERMISSIONS.ContentReview],
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin');

    const header = await screen.findByRole('banner');
    await waitFor(() => {
      expect(within(header).getByText('Staff')).toBeInTheDocument();
    });
    expect(header.textContent ?? '').not.toMatch(/caps/i);
    expect(header.textContent ?? '').not.toMatch(/Staff ·/i);
  });

  it('renders learner sidebar left-pinned with learner navigation and no staff role chip', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({
          userId: 2,
          email: 'learner@demo.local',
          fullName: 'Demo Learner',
          role: 'learner',
          emailVerified: false,
          permissions: [],
        });
      }
      if (url.endsWith('/api/learner/context')) {
        return jsonResponse({
          active: { code: 'TOEIC', name: 'TOEIC' },
          available: [{ code: 'TOEIC', name: 'TOEIC' }],
          creditBalance: 25,
        });
      }
      if (url.endsWith('/api/learner/dashboard')) {
        return jsonResponse({
          certificate: { code: 'TOEIC', name: 'TOEIC' },
          nextAction: {
            eyebrow: 'NEXT UP',
            title: 'Take placement',
            description: 'Start with a free placement test.',
            actionLabel: 'Start placement',
            actionUrl: '/placement',
            severity: 'accent',
            cascadeStep: 'step_0',
          },
          goal: { hasNoMeasurement: true },
          skills: [],
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/dashboard');

    const aside = await screen.findByRole('complementary');
    expect(within(aside).getByRole('navigation', { name: /learner navigation/i })).toBeInTheDocument();
    expect(aside.parentElement?.className).not.toContain('max-w-[1440px]');

    const header = screen.getByRole('banner');
    expect(within(header).queryByText('Admin')).not.toBeInTheDocument();
    expect(within(header).queryByText('Staff')).not.toBeInTheDocument();
    expect(screen.getByText(/not verified yet/i)).toBeInTheDocument();
  });
});
