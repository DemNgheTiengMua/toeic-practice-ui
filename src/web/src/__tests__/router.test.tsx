import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { Toast } from '@heroui/react';
import { AuthProvider } from '../context/AuthContext';
import { createAppRouter } from '../router';

const { throwState } = vi.hoisted(() => ({
  throwState: { shouldThrow: false },
}));

vi.mock('../features/public/TermsPage', async () => {
  const actual = await vi.importActual<typeof import('../features/public/TermsPage')>(
    '../features/public/TermsPage',
  );
  return {
    ...actual,
    TermsPage: () => {
      if (throwState.shouldThrow) {
        throw new Error('Unexpected render crash');
      }
      return React.createElement(actual.TermsPage);
    },
  };
});

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
        <Toast.Provider />
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  );

  return { ...view, router, history };
}

describe('Router deep links, OobeGate & error boundary (#342)', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('gates /weakness-analysis behind OobeGate when OOBE is incomplete', async () => {
    const fetchMock = installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          userId: 101,
          email: 'new@demo.local',
          fullName: 'New Learner',
          role: 'Learner',
          emailVerified: true,
        });
      }
      if (url === '/api/learner/context') {
        return jsonResponse({
          active: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
          certificates: [{ code: 'TOEIC', name: 'TOEIC Listening & Reading' }],
          creditBalance: 20,
          examinedSkills: [],
          examDate: null,
        });
      }
      if (url === '/api/learner/onboarding-state') {
        return jsonResponse({
          hasCompletedPlacement: false,
          activePlacementAttemptId: null,
          activePlacementId: null,
          latestPlacementId: null,
          hasTargetGoal: false,
          hasActiveLearningPath: false,
          isOobeComplete: false,
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/weakness-analysis');

    expect(await screen.findByText(/continue oobe setup/i)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /weakness analysis/i })).not.toBeInTheDocument();
  });

  it('renders product error screen with retry and return home on a thrown render', async () => {
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    throwState.shouldThrow = true;

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse({}, 401);
      if (url === '/api/public/certificates') return jsonResponse([]);
      return jsonResponse({}, 404);
    });

    renderRoute('/terms');

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
    expect(screen.getByText(/unexpected render crash/i)).toBeInTheDocument();
    const retryBtn = screen.getByRole('button', { name: /try again/i });
    const homeLink = screen.getByRole('link', { name: /return home/i });
    expect(homeLink).toHaveAttribute('href', '/');
    expect(homeLink.querySelector('button')).toBeNull();

    throwState.shouldThrow = false;
    fireEvent.click(retryBtn);

    expect(await screen.findByRole('heading', { level: 1, name: /terms of use/i })).toBeInTheDocument();
    consoleErrorSpy.mockRestore();
  });
});
