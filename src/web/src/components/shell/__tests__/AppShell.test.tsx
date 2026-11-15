import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

// #345 review: an encoding round-trip through this file once turned both patterns into mojibake, and
// a `not.toMatch` that can never match passes forever. They are constants so that the guards and the
// proof below cannot drift apart: `keeps_the_copy_guards_matching_the_strings_they_were_written_for`
// asserts each one against the string it is meant to catch.
const STAFF_SEPARATOR_GUARD = /Staff ·/i;
const VIETNAMESE_DIACRITICS_GUARD = /[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]/i;

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
    creditsUsedInPeriod: 100,
    creditsRefundedInPeriod: 0,
    creditsPerDayAverage: 5,
    revenueTodayVnd: 0,
    revenueInPeriodVnd: 0,
    paidOrdersInPeriodCount: 0,
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
  // #345 review: restored from a mojibake round-trip. A `not.toMatch` proves the copy is clean only if
  // the pattern can match the dirty string, and both here could not. This asserts the direction the
  // guard is used in is the direction it works in.
  it('keeps the copy guards matching the strings they were written for', () => {
    expect('Staff · Admin').toMatch(STAFF_SEPARATOR_GUARD);
    expect('Staff Studio').not.toMatch(STAFF_SEPARATOR_GUARD);

    // Every diacritic the guard claims to catch, plus the two that had silently dropped out.
    for (const word of ['à', 'đ', 'ả', 'ấ', 'ơ', 'ư', 'Học', 'Đặng']) {
      expect(word).toMatch(VIETNAMESE_DIACRITICS_GUARD);
    }
    expect("You haven't finished setting up").not.toMatch(VIETNAMESE_DIACRITICS_GUARD);
  });

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
    expect(header.textContent ?? '').not.toMatch(STAFF_SEPARATOR_GUARD);
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
    expect(header.textContent ?? '').not.toMatch(STAFF_SEPARATOR_GUARD);
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
    expect(screen.queryByText(/not verified yet/i)).not.toBeInTheDocument();

    await waitFor(() => {
      const walletLink = within(header).getByRole('link', { name: /25 credits/i });
      expect(walletLink).toHaveAttribute('href', '/wallet');
    });
    const topUpLink = within(header).getByRole('link', { name: /\+ top up/i });
    expect(topUpLink).toHaveAttribute('href', '/checkout');
  });

  it('truncates long learner names in the topbar header with a full-text title tooltip (#325)', async () => {
    const longName = 'A'.repeat(70);
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({
          userId: 7,
          email: 'long@demo.local',
          fullName: longName,
          role: 'learner',
          emailVerified: true,
          permissions: [],
        });
      }
      if (url.endsWith('/api/learner/context')) {
        return jsonResponse({
          active: { code: 'TOEIC', name: 'TOEIC' },
          available: [{ code: 'TOEIC', name: 'TOEIC' }],
          creditBalance: 10,
        });
      }
      if (url.endsWith('/api/learner/onboarding')) {
        return jsonResponse({ isOobeComplete: true });
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

    const header = await screen.findByRole('banner');
    const nameEl = await within(header).findByText(longName);
    expect(nameEl).toHaveClass('truncate');
    expect(nameEl.className).toContain('max-w-[140px]');
    expect(nameEl.className).toContain('sm:max-w-[200px]');
    expect(nameEl).toHaveAttribute('title', longName);
  });

  it('opens mobile navigation in a drawer, marks active destination, and closes on selection', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({
          userId: 2,
          email: 'learner@demo.local',
          fullName: 'Demo Learner',
          role: 'learner',
          emailVerified: true,
          permissions: [],
        });
      }
      if (url.endsWith('/api/learner/context')) {
        return jsonResponse({
          active: { code: 'TOEIC', name: 'Test of English for International Communication' },
          available: [{ code: 'TOEIC', name: 'Test of English for International Communication' }],
          creditBalance: 25,
        });
      }
      if (url.endsWith('/api/learner/dashboard')) {
        return jsonResponse({
          certificate: { code: 'TOEIC', name: 'Test of English for International Communication' },
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
      if (url.endsWith('/api/certificates')) {
        return jsonResponse([]);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/dashboard');

    const menuButton = await screen.findByRole('button', { name: /open navigation menu/i });
    expect(menuButton.className).toContain('min-h-11');
    expect(menuButton.className).toContain('min-w-11');

    fireEvent.click(menuButton);

    const dialog = await screen.findByRole('dialog');
    const drawerNav = within(dialog).getByRole('navigation', { name: /mobile navigation/i });

    const dashboardLink = within(drawerNav).getByRole('link', { name: 'Dashboard' });
    expect(dashboardLink).toHaveAttribute('aria-current', 'page');
    expect(dashboardLink.className).toContain('min-h-11');

    expect(within(drawerNav).getByRole('link', { name: 'Placement' })).toBeInTheDocument();
    expect(within(drawerNav).getByRole('link', { name: 'Support' })).toBeInTheDocument();
    expect(within(drawerNav).getByRole('link', { name: 'Certificates' })).toBeInTheDocument();
    expect(within(drawerNav).getByRole('link', { name: 'Pricing' })).toBeInTheDocument();
    expect(within(drawerNav).getByRole('link', { name: 'Saved questions' })).toBeInTheDocument();

    const certificatesLink = within(drawerNav).getByRole('link', { name: 'Certificates' });
    fireEvent.click(certificatesLink);

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('shows cert.code only in the certificate selector trigger and meets 44px touch target sizing', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({
          userId: 2,
          email: 'learner@demo.local',
          fullName: 'Demo Learner',
          role: 'learner',
          emailVerified: true,
          permissions: [],
        });
      }
      if (url.endsWith('/api/learner/context')) {
        return jsonResponse({
          active: { code: 'TOEIC', name: 'Test of English for International Communication' },
          available: [
            { code: 'TOEIC', name: 'Test of English for International Communication' },
            { code: 'IELTS', name: 'International English Language Testing System' },
          ],
          creditBalance: 25,
        });
      }
      if (url.endsWith('/api/learner/dashboard')) {
        return jsonResponse({
          certificate: { code: 'TOEIC', name: 'Test of English for International Communication' },
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

    const trigger = await screen.findByRole('button', { name: /active certificate/i });
    expect(trigger.textContent?.trim()).toBe('TOEIC');
    expect(trigger.textContent).not.toContain('Test of English for International Communication');
    expect(trigger.className).toContain('min-h-11');

    const header = screen.getByRole('banner');
    const themeBtn = within(header).getByRole('button', { name: /switch to/i });
    expect(themeBtn.className).toContain('min-h-11');
    expect(themeBtn.className).toContain('min-w-11');

    const signOutBtn = within(header).getByRole('button', { name: /sign out/i });
    expect(signOutBtn.className).toContain('min-h-11');
  });

  it('renders the onboarding return banner in English with zero Vietnamese strings', async () => {
    window.sessionStorage.setItem('ee_oobe_skip_session', '1');
    try {
      installFetchMock((url) => {
        if (url.endsWith('/api/auth/me')) {
          return jsonResponse({
            userId: 2,
            email: 'learner@demo.local',
            fullName: 'Demo Learner',
            role: 'learner',
            emailVerified: true,
            permissions: [],
          });
        }
        if (url.endsWith('/api/learner/onboarding-state')) {
          return jsonResponse({
            isOobeComplete: false,
            currentStep: 'goal',
            hasTargetGoal: false,
            hasPlacementAttempt: false,
            hasLearningPath: false,
          });
        }
        if (url.endsWith('/api/learner/context')) {
          return jsonResponse({
            active: { code: 'TOEIC', name: 'TOEIC' },
            available: [{ code: 'TOEIC', name: 'TOEIC' }],
            creditBalance: 25,
          });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/certificates');

      const banner = await screen.findByTestId('oobe-return-banner');
      expect(banner.textContent).not.toMatch(VIETNAMESE_DIACRITICS_GUARD);
      expect(within(banner).getByRole('link', { name: /continue setup/i })).toHaveAttribute(
        'href',
        '/onboarding',
      );
    } finally {
      window.sessionStorage.removeItem('ee_oobe_skip_session');
    }
  });

  it('renders unauthenticated Sign in and Register header links as single controls without nested buttons (#340)', async () => {
    installFetchMock((url) => {
      if (url.endsWith('/api/auth/me')) {
        return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      }
      if (url.endsWith('/api/certificates')) {
        return jsonResponse([]);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/pricing');

    const [header] = await screen.findAllByRole('banner');
    const signInLink = within(header).getByRole('link', { name: /^sign in$/i });
    const registerLink = within(header).getByRole('link', { name: /^register$/i });
    expect(signInLink).toHaveClass('button', 'button--secondary');
    expect(registerLink).toHaveClass('button', 'button--primary');
    for (const link of header.querySelectorAll('a')) {
      expect(link.querySelector('button, [role="button"]')).toBeNull();
    }
  });
});
