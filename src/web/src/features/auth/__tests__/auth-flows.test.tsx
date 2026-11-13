import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';

type FetchMock = ReturnType<typeof vi.fn>;

function installFetchMock(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
): FetchMock {
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

  return { ...view, router, history };
}

describe('Auth flows (#295)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('never verifies email on initial GET mount and posts token only when user clicks confirm', async () => {
    const fetchMock = installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({ code: 'AUTH_REQUIRED', title: 'Unauthorized' }, 401);
      }
      if (url === '/api/auth/verify-email' && init?.method === 'POST') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/verify-email?token=one-time-token-42');

    const verifyButton = await screen.findByRole('button', { name: /verify my email/i });
    expect(verifyButton).toBeInTheDocument();

    const verifyCallsOnMount = fetchMock.mock.calls.filter(
      ([url]) => url === '/api/auth/verify-email',
    );
    expect(verifyCallsOnMount).toHaveLength(0);

    fireEvent.click(verifyButton);

    await waitFor(() => {
      const verifyCalls = fetchMock.mock.calls.filter(([url]) => url === '/api/auth/verify-email');
      expect(verifyCalls).toHaveLength(1);
      expect(verifyCalls[0]?.[1]?.method).toBe('POST');
      expect(JSON.parse(String(verifyCalls[0]?.[1]?.body))).toEqual({ token: 'one-time-token-42' });
    });

    expect(await screen.findByText(/email verified/i)).toBeInTheDocument();
  });

  it('shows invalid-link alert on verify-email when token is rejected or missing', async () => {
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      }
      if (url === '/api/auth/verify-email' && init?.method === 'POST') {
        return jsonResponse(
          { code: 'AUTH_TOKEN_INVALID', title: 'Link not valid', detail: 'Expired token' },
          400,
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/verify-email?token=expired-token');
    const verifyButton = await screen.findByRole('button', { name: /verify my email/i });
    fireEvent.click(verifyButton);

    expect(await screen.findByText(/this link no longer works/i)).toBeInTheDocument();
  });

  it('surfaces 401 and 429 alerts on sign-in without clearing form inputs', async () => {
    let attempt = 0;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      }
      if (url === '/api/auth/sign-in' && init?.method === 'POST') {
        attempt += 1;
        if (attempt === 1) {
          return jsonResponse(
            {
              code: 'AUTH_INVALID_CREDENTIALS',
              title: 'Sign-in failed',
              detail: 'Email or password is incorrect.',
            },
            401,
          );
        }
        return jsonResponse(
          {
            code: 'TOO_MANY_REQUESTS',
            title: 'Too many requests',
          },
          429,
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/sign-in?reset=1');

    expect(
      await screen.findByText(/your password has been reset successfully/i),
    ).toBeInTheDocument();

    const main = screen.getByRole('main');
    const emailInput = within(main).getByLabelText(/email/i) as HTMLInputElement;
    const passwordInput = within(main).getByLabelText(/password/i) as HTMLInputElement;
    fireEvent.change(emailInput, { target: { value: 'learner@demo.local' } });
    fireEvent.change(passwordInput, { target: { value: 'WrongPass1' } });

    fireEvent.click(within(main).getByRole('button', { name: /^sign in$/i }));

    const alert1 = await within(main).findByRole('alert');
    expect(alert1).toHaveTextContent(/email or password is incorrect/i);
    expect(emailInput.value).toBe('learner@demo.local');
    expect(passwordInput.value).toBe('WrongPass1');

    fireEvent.click(within(main).getByRole('button', { name: /^sign in$/i }));
    const alert2 = await within(main).findByRole('alert');
    expect(alert2).toHaveTextContent(/too many attempts/i);
    expect(emailInput.value).toBe('learner@demo.local');
    expect(passwordInput.value).toBe('WrongPass1');
  });

  it('rejects external returnUrl on sign-in and routes staff by held capabilities', async () => {
    let signedIn = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        if (!signedIn) {
          return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
        }
        return jsonResponse({
          userId: 99,
          email: 'staff@demo.local',
          fullName: 'Staff User',
          role: 'staff',
          emailVerified: true,
          permissions: ['operations.support.triage'],
        });
      }
      if (url === '/api/auth/sign-in' && init?.method === 'POST') {
        signedIn = true;
        return jsonResponse({
          token: 'cookie-issued',
          expiresAt: '2026-12-01T00:00:00Z',
          user: {
            userId: 99,
            email: 'staff@demo.local',
            fullName: 'Staff User',
            role: 'staff',
            emailVerified: true,
            permissions: ['operations.support.triage'],
          },
        });
      }
      return jsonResponse({}, 404);
    });

    const { router } = renderRoute('/sign-in?returnUrl=https://evil.com/phish');

    const main = await screen.findByRole('main');
    const emailInput = await within(main).findByLabelText(/email/i);
    const passwordInput = within(main).getByLabelText(/password/i);
    fireEvent.change(emailInput, { target: { value: 'staff@demo.local' } });
    fireEvent.change(passwordInput, { target: { value: 'Demo1234!' } });
    fireEvent.click(within(main).getByRole('button', { name: /^sign in$/i }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/admin');
    });
  });

  it('honors valid local returnUrl on register and preserves inputs on 400 validation error', async () => {
    let attempts = 0;
    let registered = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        if (!registered) return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
        return jsonResponse({
          userId: 7,
          email: 'new@demo.local',
          fullName: 'New Learner',
          role: 'learner',
          emailVerified: false,
          permissions: [],
        });
      }
      if (url === '/api/auth/register' && init?.method === 'POST') {
        attempts += 1;
        if (attempts === 1) {
          return jsonResponse(
            {
              code: 'AUTH_EMAIL_TAKEN',
              title: 'Email already registered',
              detail: 'That email address is already registered.',
            },
            409,
          );
        }
        registered = true;
        return jsonResponse({
          token: 'cookie-issued',
          expiresAt: '2026-12-01T00:00:00Z',
          user: {
            userId: 7,
            email: 'new@demo.local',
            fullName: 'New Learner',
            role: 'learner',
            emailVerified: false,
            permissions: [],
          },
        });
      }
      return jsonResponse({}, 404);
    });

    const { router } = renderRoute('/register?returnUrl=%2Fplacement');

    const nameInput = (await screen.findByLabelText(/full name/i)) as HTMLInputElement;
    const emailInput = screen.getByLabelText(/^email/i) as HTMLInputElement;
    const passwordInput = screen.getByLabelText(/^password/i) as HTMLInputElement;
    const termsCheckbox = screen.getByRole('checkbox');

    fireEvent.change(nameInput, { target: { value: 'New Learner' } });
    fireEvent.change(emailInput, { target: { value: 'new@demo.local' } });
    fireEvent.change(passwordInput, { target: { value: 'Demo1234!' } });
    fireEvent.click(termsCheckbox);

    fireEvent.click(screen.getByRole('button', { name: /create account/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/already registered/i);
    expect(nameInput.value).toBe('New Learner');
    expect(emailInput.value).toBe('new@demo.local');

    fireEvent.click(screen.getByRole('button', { name: /create account/i }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/placement');
    });
  });

  it('completes forgot-password and reset-password flows with validation and sign-in redirect', async () => {
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      }
      if (url === '/api/auth/forgot-password' && init?.method === 'POST') {
        return new Response(null, { status: 204 });
      }
      if (url === '/api/auth/reset-password' && init?.method === 'POST') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({}, 404);
    });

    const forgotView = renderRoute('/forgot-password');
    fireEvent.change(await screen.findByLabelText(/email/i), {
      target: { value: 'user@demo.local' },
    });
    fireEvent.click(screen.getByRole('button', { name: /send link/i }));

    expect(await screen.findByText(/link sent/i)).toBeInTheDocument();
    forgotView.unmount();

    const resetView = renderRoute('/reset-password?token=reset-tok-1');
    fireEvent.change(await screen.findByLabelText(/^new password/i), {
      target: { value: 'NewPass123!' },
    });
    fireEvent.change(screen.getByLabelText(/confirm new password/i), {
      target: { value: 'NewPass123!' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save new password/i }));

    await waitFor(() => {
      expect(resetView.router.state.location.pathname).toBe('/sign-in');
      expect(resetView.router.state.location.searchStr).toContain('reset=1');
    });
  });
});
