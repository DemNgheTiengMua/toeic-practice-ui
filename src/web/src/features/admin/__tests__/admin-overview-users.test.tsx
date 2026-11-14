import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';

type FetchMock = ReturnType<typeof vi.fn>;

function installFetchMock(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
): FetchMock {
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

function makeMe(overrides?: Partial<{ userId: number; email: string; role: string; permissions: string[] }>) {
  return {
    userId: overrides?.userId ?? 1,
    email: overrides?.email ?? 'admin@demo.local',
    fullName: 'Admin Operator',
    role: overrides?.role ?? 'Staff',
    permissions: overrides?.permissions ?? ['system.governance.admin', 'pool.content.review'],
    activeCertId: 1,
    activeCertCode: 'TOEIC',
    activeCertName: 'TOEIC',
    creditBalance: 0,
    availableCertificates: [],
    learningContext: {
      activeCertId: 1,
      activeCertCode: 'TOEIC',
      hasPlacementDone: false,
      hasActivePath: false,
      currentBand: null,
      targetBand: null,
      recommendedAction: 'placement',
    },
  };
}

function makeDashboard(overrides?: Record<string, unknown>) {
  return {
    activeLearnersCount: 42,
    newLearners7DaysCount: 7,
    creditsUsedThisMonth: 1200,
    creditsRefundedThisMonth: 150,
    creditsPerDayAverage: 40.5,
    revenueTodayVnd: 500000,
    revenueThisMonthVnd: 3250000,
    paidOrdersThisMonthCount: 14,
    mostAttemptedExams: [
      { examId: 101, examName: 'TOEIC Full Diagnostic 1', attemptsCount: 18, averageScore: 715 },
    ],
    needsAttention: {
      draftExamsCount: 2,
      stalePendingOrdersCount: 1,
      openComplaintsCount: 3,
    },
    ...overrides,
  };
}

function makeRevision(overrides?: Record<string, unknown>) {
  return {
    paidOrdersCount: 14,
    paidOrdersAmount: 3250000,
    complaintsOpen: 3,
    complaintsReviewing: 1,
    complaintsResolved: 9,
    refundsRequested: 1,
    refundsApproved: 0,
    refundsSettled: 2,
    refundsRejected: 0,
    settledMoneyRefundsCount: 2,
    settledMoneyRefundsAmount: 450000,
    ...overrides,
  };
}

describe('Admin Overview, Users, Roles, Credit Grants & Audit Queue (#302)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
  });

  afterEach(() => {
    vi.useRealTimers();
    cleanup();
    vi.unstubAllGlobals();
  });

  it('redirects unauthenticated visitors from /admin to /sign-in with returnUrl', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) return jsonResponse({ error: 'Unauthenticated' }, 401);
      return jsonResponse({}, 404);
    });

    const { router } = renderRoute('/admin');

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/sign-in');
      expect((router.state.location.search as any)?.returnUrl).toBe('/admin');
    });
  });

  it('completes admin sign-in from /admin -> /sign-in?returnUrl=%2Fadmin -> /admin without bouncing back even if /api/auth/me is delayed', async () => {
    let signedIn = false;
    let resolveDelayedMe: ((res: Response) => void) | null = null;
    let meCallCount = 0;

    installFetchMock((url, init) => {
      if (url.endsWith('/api/auth/me')) {
        meCallCount += 1;
        if (!signedIn) {
          return jsonResponse({ error: 'Unauthenticated' }, 401);
        }
        if (meCallCount === 2) {
          return new Promise<Response>((resolve) => {
            resolveDelayedMe = resolve;
          });
        }
        return jsonResponse(makeMe({ role: 'admin' }));
      }
      if (url.endsWith('/api/auth/sign-in') && init?.method === 'POST') {
        signedIn = true;
        return jsonResponse({
          token: 'cookie-issued',
          expiresAt: '2026-12-01T00:00:00Z',
          user: makeMe({ role: 'admin' }),
        });
      }
      if (url.endsWith('/api/admin/dashboard')) {
        return jsonResponse(makeDashboard());
      }
      if (url.endsWith('/api/admin/dashboard/revision')) {
        return jsonResponse(makeRevision());
      }
      return jsonResponse({}, 404);
    });

    const { router } = renderRoute('/admin');

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/sign-in');
      expect((router.state.location.search as any)?.returnUrl).toBe('/admin');
    });

    const main = await screen.findByRole('main');
    fireEvent.change(await within(main).findByLabelText(/email/i), {
      target: { value: 'admin@demo.local' },
    });
    fireEvent.change(within(main).getByLabelText(/password/i), {
      target: { value: 'Demo1234!' },
    });
    fireEvent.click(within(main).getByRole('button', { name: /^sign in$/i }));

    await waitFor(() => {
      expect(resolveDelayedMe).not.toBeNull();
    });

    // Resolve delayed /api/auth/me refetch cleanly
    resolveDelayedMe!(jsonResponse(makeMe({ role: 'admin' })));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/admin');
    });
    expect(await screen.findByRole('heading', { name: /command center/i })).toBeInTheDocument();
  });

  it('redirects learner accounts without staff capabilities to /access-denied on /admin and /admin/users', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse(makeMe({ role: 'Learner', permissions: [] }));
      }
      return jsonResponse({}, 404);
    });

    const { router } = renderRoute('/admin');
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/access-denied');
    });

    cleanup();
    const usersRun = renderRoute('/admin/users');
    await waitFor(() => {
      expect(usersRun.router.state.location.pathname).toBe('/access-denied');
    });
  });

  it('allows non-SystemAdmin staff on /admin with held-capability guide and redirects them from /admin/users', async () => {
    const fetchMock = installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse(makeMe({ role: 'Staff', permissions: ['pool.content.review'] }));
      }
      return jsonResponse({}, 403);
    });

    renderRoute('/admin');
    await waitFor(() => {
      expect(screen.getByText(/pool\.content\.review/i)).toBeInTheDocument();
      expect(screen.getByText(/system\.governance\.admin/i)).toBeInTheDocument();
    });

    // Must not call SystemAdmin-only dashboard endpoint when caller lacks system.governance.admin
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes('/api/admin/dashboard'))).toBe(
      false,
    );

    cleanup();
    const usersRun = renderRoute('/admin/users');
    await waitFor(() => {
      expect(usersRun.router.state.location.pathname).toBe('/access-denied');
    });
  });

  it('renders signed negative net revenue in text-danger with true minus, separates credits refunded, and polls revision every 5s', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let revCall = 0;
    let dashCall = 0;

    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) return jsonResponse(makeMe());
      if (url.endsWith('/api/admin/dashboard/revision')) {
        revCall += 1;
        if (revCall >= 3) {
          return jsonResponse(makeRevision({ paidOrdersCount: 15, paidOrdersAmount: 3750000 }));
        }
        return jsonResponse(makeRevision());
      }
      if (url.endsWith('/api/admin/dashboard')) {
        dashCall += 1;
        if (dashCall === 1) {
          return jsonResponse(
            makeDashboard({
              revenueTodayVnd: -500000,
              revenueThisMonthVnd: -1200000,
              creditsUsedThisMonth: 1200,
              creditsRefundedThisMonth: 150,
            }),
          );
        }
        return jsonResponse(
          makeDashboard({
            activeLearnersCount: 43,
            revenueTodayVnd: 500000,
            revenueThisMonthVnd: 3750000,
            paidOrdersThisMonthCount: 15,
          }),
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin');

    await waitFor(() => {
      expect(screen.getByTestId('revenue-today-value')).toBeInTheDocument();
    });

    const todayEl = screen.getByTestId('revenue-today-value');
    const monthEl = screen.getByTestId('revenue-month-value');
    expect(todayEl.textContent).toContain('\u2212');
    expect(todayEl.textContent).toContain('500');
    expect(todayEl.className).toContain('text-danger');
    expect(monthEl.textContent).toContain('\u2212');
    expect(monthEl.className).toContain('text-danger');

    // Credits used and credits refunded rendered separately (not netted to 1050)
    expect(screen.getByText(/1,200/)).toBeInTheDocument();
    expect(screen.getByText(/150/)).toBeInTheDocument();
    expect(screen.getByText('Revenue today (UTC+7)')).toBeInTheDocument();
    expect(screen.getByText('TOEIC Full Diagnostic 1')).toBeInTheDocument();

    // First 5s tick returns SAME revision -> no extra /api/admin/dashboard call
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(revCall).toBe(2);
    expect(dashCall).toBe(1);

    // Second 5s tick returns UPDATED revision -> triggers 1 dashboard refetch in place
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });

    await waitFor(() => {
      expect(dashCall).toBe(2);
      expect(screen.getByTestId('revenue-today-value').className).not.toContain('text-danger');
      expect(screen.getByTestId('revenue-today-value').textContent).not.toContain('\u2212');
    });
  });

  it('filters users, renders human-readable placement history, handles lock/resend errors, and sends exact { delta, reason } on credit grants', async () => {
    const adjustPayloads: unknown[] = [];
    const requestedUrls: string[] = [];

    installFetchMock(async (url, init) => {
      requestedUrls.push(url);
      if (url.endsWith('/api/auth/me')) return jsonResponse(makeMe());

      if (url.includes('/api/admin/users?') || url.endsWith('/api/admin/users')) {
        return jsonResponse({
          users: [
            {
              userId: 10,
              email: 'learner@demo.local',
              fullName: 'Nguyen Van An',
              phone: '0901234567',
              creditsLeft: 120,
              examsTaken: 3,
              bestScore: 785,
              lastLoginAt: '2026-11-12T08:00:00Z',
              isLocked: false,
              emailVerifiedAt: null,
              createdAt: '2026-10-01T08:00:00Z',
            },
            {
              userId: 11,
              email: 'root@demo.local',
              fullName: 'Root Governance',
              phone: null,
              creditsLeft: 0,
              examsTaken: 0,
              bestScore: null,
              lastLoginAt: null,
              isLocked: false,
              emailVerifiedAt: '2026-10-01T08:00:00Z',
              createdAt: '2026-10-01T08:00:00Z',
            },
          ],
          totalCount: 2,
          page: 1,
          pageSize: 25,
          activeCount: 1,
          lockedCount: 0,
          notActivatedCount: 1,
        });
      }

      if (url.endsWith('/api/admin/users/10')) {
        return jsonResponse({
          user: {
            userId: 10,
            email: 'learner@demo.local',
            fullName: 'Nguyen Van An',
            phone: '0901234567',
            creditsLeft: 120,
            examsTaken: 3,
            bestScore: 785,
            lastLoginAt: '2026-11-12T08:00:00Z',
            isLocked: false,
            emailVerifiedAt: null,
            createdAt: '2026-10-01T08:00:00Z',
          },
          placements: [
            {
              placementId: 501,
              certCode: 'TOEIC',
              examName: 'TOEIC Diagnostic',
              status: 'in_progress',
              scope: 'listening_and_reading',
              paid: false,
              creditsCharged: 0,
              totalScore: null,
              resultBandCode: null,
              takenAt: '2026-11-10T10:00:00Z',
            },
          ],
        });
      }

      if (url.endsWith('/api/admin/users/11/lock') && init?.method === 'POST') {
        return jsonResponse(
          {
            detail: 'System administrators cannot be locked.',
            code: 'cannot_lock_system_admin',
          },
          403,
        );
      }

      if (url.endsWith('/api/admin/users/10/resend-activation') && init?.method === 'POST') {
        return new Response(null, { status: 204 });
      }

      if (url.endsWith('/api/admin/users/10/adjust-credits') && init?.method === 'POST') {
        const body = JSON.parse(String(init.body));
        adjustPayloads.push(body);
        if (body.delta === -999) {
          return jsonResponse(
            { detail: 'Credit balance cannot drop below zero.', code: 'insufficient_credits' },
            409,
          );
        }
        return jsonResponse({
          txnId: 9001,
          userId: 10,
          newBalance: 170,
          delta: body.delta,
          reason: body.reason,
        });
      }

      return jsonResponse({}, 404);
    });

    renderRoute('/admin/users');

    await waitFor(() => {
      expect(screen.getByText('learner@demo.local')).toBeInTheDocument();
    });

    // Search passes q parameter
    const searchInput = screen.getByPlaceholderText(/search by email or name/i);
    fireEvent.change(searchInput, { target: { value: 'learner' } });
    fireEvent.submit(searchInput.closest('form')!);
    await waitFor(() => {
      expect(requestedUrls.some((u) => u.includes('q=learner'))).toBe(true);
    });

    // View detail modal renders human-readable placement labels (not raw snake_case tokens)
    const row10 = screen.getByText('learner@demo.local').closest('tr')!;
    fireEvent.click(within(row10).getByRole('button', { name: /details/i }));
    await waitFor(() => {
      expect(screen.getByText('TOEIC Diagnostic')).toBeInTheDocument();
      expect(screen.getByText('In progress')).toBeInTheDocument();
      expect(screen.getByText('Listening & Reading')).toBeInTheDocument();
    });
    expect(screen.queryByText('in_progress')).not.toBeInTheDocument();
    expect(screen.queryByText('listening_and_reading')).not.toBeInTheDocument();

    // Resend activation on unverified user
    fireEvent.click(screen.getByRole('button', { name: /resend activation/i }));
    await waitFor(() => {
      expect(requestedUrls.some((u) => u.endsWith('/api/admin/users/10/resend-activation'))).toBe(
        true,
      );
    });

    // Close detail modal & open Adjust credits modal
    fireEvent.click(screen.getByRole('button', { name: /close/i }));
    fireEvent.click(within(row10).getByRole('button', { name: /adjust credits/i }));

    await waitFor(() => {
      expect(screen.getByLabelText(/credit delta/i)).toBeInTheDocument();
    });

    // Reason required & delta != 0 validation
    const deltaInput = screen.getByLabelText(/credit delta/i);
    const reasonInput = screen.getByLabelText(/reason/i);
    fireEvent.change(deltaInput, { target: { value: '0' } });
    fireEvent.change(reasonInput, { target: { value: '   ' } });
    fireEvent.click(screen.getByRole('button', { name: /apply adjustment/i }));
    expect(adjustPayloads).toHaveLength(0);

    // Valid grant sends exact { delta, reason } (not amount)
    fireEvent.change(deltaInput, { target: { value: '50' } });
    fireEvent.change(reasonInput, { target: { value: 'Support goodwill compensation' } });
    fireEvent.click(screen.getByRole('button', { name: /apply adjustment/i }));

    await waitFor(() => {
      expect(adjustPayloads).toEqual([
        { delta: 50, reason: 'Support goodwill compensation' },
      ]);
      expect(within(row10).getByText(/170/)).toBeInTheDocument();
    });

    // Locking SystemAdmin surfaces server 403 reason cleanly
    const row11 = screen.getByText('root@demo.local').closest('tr')!;
    fireEvent.click(within(row11).getByRole('button', { name: /lock/i }));
    await waitFor(() => {
      expect(screen.getByText(/System administrators cannot be locked\./i)).toBeInTheDocument();
    });

    // Switch to Roles & capabilities tab and verify 5 composable capabilities appear
    fireEvent.click(screen.getByRole('tab', { name: /roles & capabilities/i }));
    await waitFor(() => {
      expect(screen.getByText('pool.content.author')).toBeInTheDocument();
      expect(screen.getByText('operations.support.triage')).toBeInTheDocument();
      expect(screen.getByText('assessment.blueprint.design')).toBeInTheDocument();
    });
  });

  it('enforces blind-solve -> reveal -> verdict -> decide flow on /admin/audit/$batchId', async () => {
    const postCalls: { url: string; body?: unknown }[] = [];
    let solved = false;
    let revealed = false;
    let verdict: string | null = null;
    let status = 'pending';

    installFetchMock(async (url, init) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse(makeMe({ permissions: ['pool.content.review'] }));
      }
      if (url.endsWith('/api/admin/audit/batches/7')) {
        return jsonResponse({
          batchId: 7,
          sourceName: 'TOEIC-2026-Import.csv',
          status: 'active',
          sampledTotal: 1,
          unsampledTotal: 12,
          strata: [
            {
              stratumId: 71,
              batchId: 7,
              topicCode: 'GRAMMAR_TENSE',
              cefrLevel: 'B1',
              modality: 'reading',
              itemCount: 13,
              sampleSize: 1,
              sampled: 1,
              unsampled: 12,
              status,
              quarantineReason: null,
              decidedAt: null,
              items: [
                {
                  sampleItemId: 701,
                  itemId: 8801,
                  stem: 'She ____ to Hanoi yesterday.',
                  optionA: 'went',
                  optionB: 'goes',
                  optionC: 'going',
                  optionD: 'gone',
                  solvedLabel: solved ? 'A' : null,
                  solvedAt: solved ? '2026-11-13T12:00:00Z' : null,
                  keyRevealedAt: revealed ? '2026-11-13T12:00:05Z' : null,
                  keyRevealed: revealed,
                  verdict,
                },
              ],
            },
          ],
        });
      }
      if (url.endsWith('/api/admin/audit/samples/701/solve') && init?.method === 'POST') {
        solved = true;
        postCalls.push({ url, body: JSON.parse(String(init.body)) });
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/api/admin/audit/samples/701/reveal') && init?.method === 'POST') {
        revealed = true;
        postCalls.push({ url });
        return jsonResponse({
          sampleItemId: 701,
          itemId: 8801,
          keyLabel: 'A',
          explanation: 'Past simple tense is required because of "yesterday".',
          solvedLabel: 'A',
          revealedAt: '2026-11-13T12:00:05Z',
        });
      }
      if (url.endsWith('/api/admin/audit/samples/701/verdict') && init?.method === 'POST') {
        verdict = JSON.parse(String(init.body)).verdict;
        postCalls.push({ url, body: { verdict } });
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/api/admin/audit/strata/71/decide') && init?.method === 'POST') {
        status = 'approved';
        postCalls.push({ url });
        return jsonResponse({
          stratumId: 71,
          status: 'approved',
          quarantineReason: null,
          sampledPromoted: 1,
          unsampledPromoted: 12,
          quarantined: 0,
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/audit/7');

    await waitFor(() => {
      expect(screen.getByText('She ____ to Hanoi yesterday.')).toBeInTheDocument();
    });

    // Blind-solve invariant: explanation & authored key must not appear before reveal
    expect(
      screen.queryByText(/Past simple tense is required because of "yesterday"/i),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reveal key/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /decide.*1 sampled.*12 unsampled/i })).toBeDisabled();

    // 1. Solve
    fireEvent.click(screen.getByLabelText(/A: went/i));
    fireEvent.click(screen.getByRole('button', { name: /submit solve/i }));
    await waitFor(() => {
      expect(postCalls.some((c) => c.url.endsWith('/samples/701/solve'))).toBe(true);
      expect(screen.getByRole('button', { name: /reveal key/i })).toBeEnabled();
    });

    // 2. Reveal
    fireEvent.click(screen.getByRole('button', { name: /reveal key/i }));
    await waitFor(() => {
      expect(
        screen.getByText(/Past simple tense is required because of "yesterday"/i),
      ).toBeInTheDocument();
    });

    // 3. Verdict
    fireEvent.click(screen.getByRole('button', { name: /pass/i }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /decide.*1 sampled.*12 unsampled/i })).toBeEnabled();
    });

    // 4. Decide stratum -> promotion sentence mentions both sampled and unsampled counts
    fireEvent.click(screen.getByRole('button', { name: /decide.*1 sampled.*12 unsampled/i }));
    await waitFor(() => {
      expect(screen.getByText(/1 reviewed.*12 unsampled/i)).toBeInTheDocument();
    });
  });
});
