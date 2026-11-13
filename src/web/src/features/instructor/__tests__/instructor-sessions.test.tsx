import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import { STAFF_PERMISSIONS } from '../../../lib/security';
import type { AdminMentorListItemDto } from '../../../lib/api';

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

const MOCK_ADMIN_MENTORS: AdminMentorListItemDto[] = [
  {
    mentorId: 1,
    displayName: 'Sarah Jenkins',
    type: 'human',
    certId: 1,
    certCode: 'TOEIC',
    skillName: 'Listening & Reading',
    pricePerSlot: 100000,
    priceCredits: 20,
    sessionMinutes: 45,
    avgRating: 4.9,
    reviewCount: 18,
    totalSlotsCount: 12,
    openSlotsCount: 4,
    isActive: true,
  },
  {
    mentorId: 2,
    displayName: 'AI Fluency Coach',
    type: 'ai',
    certId: 1,
    certCode: 'TOEIC',
    pricePerSlot: 25000,
    priceCredits: 5,
    sessionMinutes: 20,
    avgRating: 4.8,
    reviewCount: 42,
    totalSlotsCount: 0,
    openSlotsCount: 0,
    isActive: true,
  },
];

describe('Instructor sessions workspace (#301)', () => {
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

  it('denies access cleanly for unauthorized learner', async () => {
    const mockLearner = {
      userId: 42,
      email: 'learner@demo.local',
      fullName: 'Learner Demo',
      role: 'Learner',
      isActive: true,
      emailVerified: true,
      permissions: [],
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(mockLearner);
      return jsonResponse({}, 404);
    });

    renderRoute('/instructor/sessions');

    expect(await screen.findByRole('heading', { name: /access denied/i })).toBeInTheDocument();
    expect(screen.getByText(/instructor or operations permissions/i)).toBeInTheDocument();
  });

  it('renders instructor sessions roster for support staff with SupportTriage capability', async () => {
    const mockStaff = {
      userId: 7,
      email: 'staff@demo.local',
      fullName: 'Operations Staff',
      role: 'Staff',
      isActive: true,
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SupportTriage],
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(mockStaff);
      if (url === '/api/admin/mentors') return jsonResponse(MOCK_ADMIN_MENTORS);
      return jsonResponse({}, 404);
    });

    renderRoute('/instructor/sessions');

    expect(await screen.findByRole('heading', { name: /instructor sessions/i })).toBeInTheDocument();
    expect(await screen.findByText('Sarah Jenkins')).toBeInTheDocument();
    expect(screen.getByText('4 open slots')).toBeInTheDocument();
  });

  it('renders instructor workbench for user with role="Instructor"', async () => {
    const mockInstructor = {
      userId: 15,
      email: 'instructor@demo.local',
      fullName: 'Dr. Instructor',
      role: 'Instructor',
      isActive: true,
      emailVerified: true,
      permissions: [],
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(mockInstructor);
      return jsonResponse({}, 404);
    });

    renderRoute('/instructor/sessions');

    expect(await screen.findByRole('heading', { name: /instructor sessions/i })).toBeInTheDocument();
    expect(screen.getByText(/instructor schedule workbench/i)).toBeInTheDocument();
  });
});
