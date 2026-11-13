import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type { ProfileDto } from '../../../lib/api';

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

const MOCK_PROFILE: ProfileDto = {
  fullName: 'Learner Demo',
  phone: '0901234567',
  timeZoneId: 'Asia/Ho_Chi_Minh',
};

const MOCK_LEARNER_CONTEXT = {
  active: { certId: 1, code: 'TOEIC', name: 'TOEIC Listening & Reading' },
  available: [
    { certId: 1, code: 'TOEIC', name: 'TOEIC Listening & Reading' },
    { certId: 2, code: 'IELTS', name: 'IELTS Academic' },
  ],
  certificates: [
    { certId: 1, code: 'TOEIC', name: 'TOEIC Listening & Reading' },
    { certId: 2, code: 'IELTS', name: 'IELTS Academic' },
  ],
  creditBalance: 150,
};

describe('Profile feature (#301)', () => {
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

  it('renders profile with user information, phone, and timezone', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/profile') return jsonResponse(MOCK_PROFILE);
      return jsonResponse({}, 404);
    });

    renderRoute('/profile');

    expect(await screen.findByRole('heading', { name: /profile & settings/i })).toBeInTheDocument();
    expect(await screen.findByDisplayValue('Learner Demo')).toBeInTheDocument();
    expect(screen.getByDisplayValue('0901234567')).toBeInTheDocument();
    expect(screen.getByText('Asia/Ho_Chi_Minh')).toBeInTheDocument();
  });

  it('shows error state with retry on failed load and never renders an empty form', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/profile') return jsonResponse({ message: 'Internal server error' }, 500);
      return jsonResponse({}, 404);
    });

    renderRoute('/profile');

    expect(await screen.findByText(/unable to load profile/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
    // Invariant: MUST NOT render the editable save form to prevent blank overwrites!
    expect(screen.queryByRole('button', { name: /save changes/i })).toBeNull();
  });

  it('updates profile information successfully (PUT /api/profile)', async () => {
    let putBody: any = null;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/profile') {
        if (init?.method === 'PUT') {
          putBody = JSON.parse(String(init.body));
          return jsonResponse({
            fullName: putBody.fullName,
            phone: putBody.phone,
            timeZoneId: putBody.timeZoneId,
          });
        }
        return jsonResponse(MOCK_PROFILE);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/profile');

    const nameInput = await screen.findByDisplayValue('Learner Demo');
    fireEvent.change(nameInput, { target: { value: 'Learner Updated' } });

    const saveBtn = screen.getByRole('button', { name: /save changes/i });
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(putBody).toEqual({
        fullName: 'Learner Updated',
        phone: '0901234567',
        timeZoneId: 'Asia/Ho_Chi_Minh',
      });
      expect(screen.getByText(/profile updated successfully/i)).toBeInTheDocument();
    });
  });

  it('renders unverified email warning with resend activation button', async () => {
    let resendCalled = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse({ ...MOCK_USER, emailVerified: false });
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/profile') return jsonResponse(MOCK_PROFILE);
      if (url === '/api/auth/resend-activation' && init?.method === 'POST') {
        resendCalled = true;
        return new Response(null, { status: 204 });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/profile');

    expect(await screen.findByText(/email verification pending/i)).toBeInTheDocument();

    const resendBtn = screen.getByRole('button', { name: /resend verification email/i });
    fireEvent.click(resendBtn);

    await waitFor(() => {
      expect(resendCalled).toBe(true);
      expect(screen.getByText(/verification email sent/i)).toBeInTheDocument();
    });
  });

  it('signs out and navigates to /sign-in?reset=1 upon successful password change', async () => {
    let passwordPayload: any = null;
    let signedOut = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/profile') return jsonResponse(MOCK_PROFILE);
      if (url === '/api/profile/password' && init?.method === 'POST') {
        passwordPayload = JSON.parse(String(init.body));
        return new Response(null, { status: 204 });
      }
      if (url === '/api/auth/sign-out' && init?.method === 'POST') {
        signedOut = true;
        return new Response(null, { status: 204 });
      }
      return jsonResponse({}, 404);
    });

    const { history } = renderRoute('/profile');

    expect(await screen.findByRole('heading', { name: /profile & settings/i })).toBeInTheDocument();

    const currentPwInput = screen.getByLabelText(/current password/i);
    const newPwInput = screen.getByLabelText(/^new password/i);
    const confirmPwInput = screen.getByLabelText(/confirm new password/i);

    fireEvent.change(currentPwInput, { target: { value: 'CurrentPass123!' } });
    fireEvent.change(newPwInput, { target: { value: 'NewSecret123!' } });
    fireEvent.change(confirmPwInput, { target: { value: 'NewSecret123!' } });

    const changePwBtn = screen.getByRole('button', { name: /change password/i });
    fireEvent.click(changePwBtn);

    await waitFor(() => {
      expect(passwordPayload).toEqual({
        currentPassword: 'CurrentPass123!',
        newPassword: 'NewSecret123!',
      });
      expect(signedOut).toBe(true);
      expect(history.location.pathname).toBe('/sign-in');
      expect(String(history.location.search)).toContain('reset');
    });
  });

  it('preserves typed inputs and displays error on password change failure (400)', async () => {
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/profile') return jsonResponse(MOCK_PROFILE);
      if (url === '/api/profile/password' && init?.method === 'POST') {
        return jsonResponse(
          {
            code: 'INVALID_CREDENTIALS',
            title: 'Incorrect current password',
            detail: 'The current password you provided is not correct.',
          },
          400,
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/profile');

    const currentPwInput = (await screen.findByLabelText(/current password/i)) as HTMLInputElement;
    const newPwInput = screen.getByLabelText(/^new password/i) as HTMLInputElement;
    const confirmPwInput = screen.getByLabelText(/confirm new password/i) as HTMLInputElement;

    fireEvent.change(currentPwInput, { target: { value: 'WrongPass!' } });
    fireEvent.change(newPwInput, { target: { value: 'NewSecret123!' } });
    fireEvent.change(confirmPwInput, { target: { value: 'NewSecret123!' } });

    const changePwBtn = screen.getByRole('button', { name: /change password/i });
    fireEvent.click(changePwBtn);

    expect(await screen.findByText(/incorrect current password/i)).toBeInTheDocument();
    // Inputs must NOT be wiped
    expect(currentPwInput.value).toBe('WrongPass!');
    expect(newPwInput.value).toBe('NewSecret123!');
  });
});
