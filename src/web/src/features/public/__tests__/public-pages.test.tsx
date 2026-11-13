import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type { CertificateDto, PackageDto } from '../../../lib/api';

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

  return { ...view, router, history };
}

const DEMO_CERTIFICATES: CertificateDto[] = [
  {
    code: 'DEMO',
    name: 'DEMO - Custom Assessment',
    description: 'Certificate-driven structure without hardcoded names.',
    skills: [
      {
        code: 'CMP',
        name: 'Comprehension',
        modality: 'listening',
        durationMinutes: 40,
        sections: [{ code: 'U1', name: 'Short dialogs', expectedQuestionCount: 25 }],
      },
      {
        code: 'EXP',
        name: 'Expression',
        modality: 'writing',
        durationMinutes: 35,
        sections: [{ code: 'U2', name: 'Structured essay', expectedQuestionCount: 2 }],
      },
    ],
    bands: [
      // Deliberately out of order to verify score-order sorting
      { code: 'D3', name: 'Proficient', minTotal: 200, maxTotal: 399 },
      { code: 'D1', name: 'Starter', minTotal: 0, maxTotal: 99 },
      { code: 'D4', name: 'Mastery', minTotal: 400, maxTotal: 500 },
      { code: 'D2', name: 'Developing', minTotal: 100, maxTotal: 199 },
    ],
  },
  {
    code: 'ALT',
    name: 'Alternate Certificate',
    description: 'Second certificate option.',
    skills: [
      {
        code: 'RD',
        name: 'Reading',
        modality: 'reading',
        durationMinutes: 50,
        sections: [{ code: 'S1', name: 'Passages', expectedQuestionCount: 30 }],
      },
    ],
    bands: [
      { code: 'B1', name: 'Intermediate', minTotal: 0, maxTotal: 100 },
    ],
  },
];

const SAMPLE_PACKAGES: PackageDto[] = [
  {
    packageId: 1,
    name: 'Pay as you go',
    credits: 1,
    price: 49000,
    isActive: true,
    isFeatured: false,
    features: '1 mock exam credit\nNever expires',
    displayOrder: 1,
    featureList: ['1 mock exam credit', 'Never expires'],
  },
  {
    packageId: 2,
    name: 'Exam Prep',
    credits: 5,
    price: 199000,
    isActive: true,
    isFeatured: true,
    features: '5 mock exam credits\nNever expires',
    displayOrder: 2,
    featureList: ['5 mock exam credits', 'Never expires'],
  },
];

describe('Public catalog, pricing & legal pages (#295)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
    if (typeof globalThis.ResizeObserver === 'undefined') {
      globalThis.ResizeObserver = class ResizeObserver {
        observe() {}
        unobserve() {}
        disconnect() {}
      };
    }
    if (typeof Element !== 'undefined' && typeof Element.prototype.getAnimations !== 'function') {
      Element.prototype.getAnimations = () => [];
    }
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('renders landing page from live certificates without hardcoding TOEIC/IELTS/VSTEP and degrades gracefully on API error', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      if (url === '/api/certificates') return jsonResponse([DEMO_CERTIFICATES[0]]);
      return jsonResponse({}, 404);
    });

    const first = renderRoute('/');
    expect(await screen.findByText(/know where you stand\./i)).toBeInTheDocument();
    expect(await screen.findByText(/Comprehension · Expression/i)).toBeInTheDocument();
    expect(screen.getAllByText(/0–500/).length).toBeGreaterThan(0);

    const bodyText = document.body.textContent ?? '';
    expect(bodyText).not.toContain('TOEIC');
    expect(bodyText).not.toContain('IELTS');
    expect(bodyText).not.toContain('VSTEP');
    first.unmount();

    // Marketing page stays up when /api/certificates fails
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      if (url === '/api/certificates') return jsonResponse({ title: 'Down' }, 503);
      return jsonResponse({}, 404);
    });
    renderRoute('/');
    expect(await screen.findByText(/know where you stand\./i)).toBeInTheDocument();
    expect(screen.getByText(/find your level/i)).toBeInTheDocument();
  });

  it('renders certificate catalog with sorted proportional band strip, non-duplicated headings, and learner switch action', async () => {
    const fetchMock = installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          userId: 10,
          email: 'learner@demo.local',
          fullName: 'Demo Learner',
          role: 'learner',
          emailVerified: true,
          permissions: [],
        });
      }
      if (url === '/api/learner/context') {
        return jsonResponse({
          active: { code: 'DEMO', name: 'DEMO - Custom Assessment', sectionNoun: 'Unit' },
          available: [
            { code: 'DEMO', name: 'DEMO - Custom Assessment', sectionNoun: 'Unit' },
            { code: 'ALT', name: 'Alternate Certificate', sectionNoun: 'Section' },
          ],
          creditBalance: 8,
        });
      }
      if (url === '/api/certificates') return jsonResponse(DEMO_CERTIFICATES);
      if (url === '/api/learner/active-certificate' && init?.method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({}, 404);
    });

    const { router } = renderRoute('/certificates');

    expect(await screen.findByText('2 certificates, one account')).toBeInTheDocument();
    // Name already starts with DEMO, so heading must NOT say "DEMO - DEMO - Custom Assessment"
    expect(screen.getByRole('heading', { name: 'DEMO - Custom Assessment' })).toBeInTheDocument();
    expect(screen.queryByText(/DEMO - DEMO/i)).not.toBeInTheDocument();

    // Band strip sorted in score order (D1, D2, D3, D4) with proportional widths
    const strip = screen.getByRole('img', { name: /DEMO score bands from D1 at 0 to D4 at 500/i });
    const bandLabels = Array.from(strip.querySelectorAll('[data-band-code]')).map((el) =>
      el.getAttribute('data-band-code'),
    );
    expect(bandLabels).toEqual(['D1', 'D2', 'D3', 'D4']);
    expect(strip).toHaveStyle({ gridTemplateColumns: '99fr 99fr 199fr 100fr' });

    // Current certificate badge on active, Switch button on inactive
    expect(await screen.findByText(/current certificate/i)).toBeInTheDocument();
    const switchBtn = screen.getByRole('button', { name: /switch to ALT/i });
    fireEvent.click(switchBtn);

    await waitFor(() => {
      const putCalls = fetchMock.mock.calls.filter(
        ([url, init]) => url === '/api/learner/active-certificate' && init?.method === 'PUT',
      );
      expect(putCalls).toHaveLength(1);
      expect(JSON.parse(String(putCalls[0]?.[1]?.body))).toEqual({ code: 'ALT' });
      expect(router.state.location.pathname).toBe('/dashboard');
    });
  });

  it('renders certificate detail route /certificates/$slug with skills, sections, and score bands', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      if (url === '/api/certificates') return jsonResponse(DEMO_CERTIFICATES);
      return jsonResponse({}, 404);
    });

    renderRoute('/certificates/demo');

    expect(await screen.findByRole('heading', { name: 'DEMO - Custom Assessment' })).toBeInTheDocument();
    expect(screen.getByText('Short dialogs')).toBeInTheDocument();
    expect(screen.getByText('25')).toBeInTheDocument();

    const bandsTab = screen.getByRole('tab', { name: /score bands/i });
    fireEvent.click(bandsTab);
    expect(await screen.findByText('Mastery')).toBeInTheDocument();
  });

  it('renders pricing page with VND packages, free diagnostic clarity, learner balance, and FAQ accordion', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          userId: 10,
          email: 'learner@demo.local',
          fullName: 'Demo Learner',
          role: 'learner',
          emailVerified: true,
          permissions: [],
        });
      }
      if (url === '/api/learner/context') {
        return jsonResponse({
          active: { code: 'DEMO', name: 'DEMO - Custom Assessment', sectionNoun: 'Unit' },
          available: [{ code: 'DEMO', name: 'DEMO - Custom Assessment', sectionNoun: 'Unit' }],
          creditBalance: 12,
        });
      }
      if (url === '/api/packages') return jsonResponse(SAMPLE_PACKAGES);
      return jsonResponse({}, 404);
    });

    renderRoute('/pricing');

    const main = await screen.findByRole('main');
    expect(await within(main).findByText('Exam Prep')).toBeInTheDocument();
    expect(within(main).getByText('Popular')).toBeInTheDocument();
    expect(within(main).getByText(/199\.000/)).toBeInTheDocument();
    expect(within(main).getByText(/12 credits/i)).toBeInTheDocument();
    expect(within(main).getByText(/do credits expire/i)).toBeInTheDocument();
  });

  it('renders /terms, /privacy, and neutral capability-safe /access-denied routes', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
      return jsonResponse({}, 404);
    });

    const terms = renderRoute('/terms');
    expect(await screen.findByRole('heading', { name: /terms of use/i })).toBeInTheDocument();
    terms.unmount();

    const privacy = renderRoute('/privacy');
    expect(await screen.findByRole('heading', { name: /privacy policy/i })).toBeInTheDocument();
    privacy.unmount();

    renderRoute('/access-denied');
    expect(await screen.findByText(/access restricted/i)).toBeInTheDocument();
    expect(screen.getByText(/not available to your account/i)).toBeInTheDocument();
    expect(document.body.textContent ?? '').not.toMatch(/requires administrator privileges/i);
  });
});
