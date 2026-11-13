import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../../context/AuthContext';
import { createAppRouter } from '../../../../router';
import { STAFF_PERMISSIONS } from '../../../../lib/security';

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

const mockCoverage = {
  cells: [
    {
      topicId: 1,
      modality: 'reading',
      topicCode: 'R-GIST',
      topicName: 'Main Idea and Gist',
      cefrLevel: 'B1',
      targetItemCount: 20,
      activeItemCount: 15,
      placementEligibleItemCount: 5,
      deficit: 5,
    },
    {
      topicId: 2,
      modality: 'listening',
      topicCode: 'L-RESP',
      topicName: 'Question-Response',
      cefrLevel: 'B2',
      targetItemCount: 25,
      activeItemCount: 25,
      placementEligibleItemCount: 10,
      deficit: 0,
    },
  ],
};

const mockIngestionBatches = {
  batches: [
    {
      batchId: 901,
      sourceName: 'ETS_2026_Reading_Pool.json',
      status: 'completed',
      uploadedBy: 1,
      uploadedByEmail: 'author@demo.local',
      createdAt: '2026-11-10T08:00:00Z',
      completedAt: '2026-11-10T08:02:00Z',
      records: 50,
      accepted: 48,
      discarded: 2,
    },
  ],
};

const mockBatchDetail = {
  batch: {
    batchId: 901,
    sourceName: 'ETS_2026_Reading_Pool.json',
    status: 'completed',
    uploadedBy: 1,
    uploadedByEmail: 'author@demo.local',
    createdAt: '2026-11-10T08:00:00Z',
    completedAt: '2026-11-10T08:02:00Z',
    records: 50,
    accepted: 48,
    discarded: 2,
  },
  records: [
    {
      line: 12,
      attempt: 1,
      accepted: false,
      itemId: null,
      refusals: [
        {
          rule: 'gate1.options.rationale_required',
          message: 'All 4 options require clear author rationale before validation.',
        },
      ],
      recordJson: '{"stem":"Sample flawed question without rationale"}',
      discarded: false,
      note: 'Pending author update',
      message: 'All 4 options require clear author rationale before validation.',
      rule: 'gate1.options.rationale_required',
    },
  ],
  ruleGroups: [
    {
      rule: 'gate1.options.rationale_required',
      records: 1,
    },
  ],
  staged: [
    {
      itemId: 801,
      topicCode: 'R-GIST',
      cefrLevel: 'B1',
      modality: 'reading',
      lifecycleStatus: 'draft',
      versionNumber: 1,
    },
  ],
};

const mockLessons = [
  {
    lessonId: 301,
    certId: 1,
    certCode: 'TOEIC',
    skillId: 11,
    skillCode: 'READING',
    skillName: 'Reading',
    categoryId: 1,
    categoryCode: 'STRATEGY',
    categoryName: 'Strategy',
    title: 'Skimming & Scanning Mastery',
    summary: 'Essential reading speed techniques for TOEIC Part 7.',
    estimatedMinutes: 15,
    displayOrder: 1,
    cardCount: 4,
    createdAt: '2026-10-25T10:00:00Z',
    topicId: 1,
    topicCode: 'R-GIST',
    topicName: 'Main Idea and Gist',
    cefrLevel: 'B1',
  },
];

const mockStimulusOptions = {
  topics: [{ topicId: 1, code: 'R-GIST', name: 'Main Idea and Gist' }],
  levels: ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'],
  itemTypes: ['mcq', 'mcq_3'],
  quotas: [
    {
      level: 'B1',
      itemType: 'mcq',
      locked: 3,
      minimum: 2,
      maximum: 4,
      allowedChildLevels: ['A2', 'B1', 'B2'],
      minimumAtLevel: 2,
    },
  ],
  spanUnit: 'words',
};

const mockProductiveOptions = {
  topics: [{ topicId: 10, code: 'W-ARG', name: 'Argumentative Essay' }],
  levels: ['B1', 'B2', 'C1', 'C2'],
  types: [
    {
      code: 'W-ARG',
      visual: 'optional',
      minimumPrepSeconds: 0,
      maximumPrepSeconds: 60,
      minimumResponseSeconds: 600,
      maximumResponseSeconds: 1800,
      minimumWords: 250,
      maximumWords: 400,
    },
  ],
};

describe('Question Bank Studio (/admin/question-bank) (#303)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
    if (typeof window.ResizeObserver === 'undefined') {
      window.ResizeObserver = class {
        observe() {}
        unobserve() {}
        disconnect() {}
      } as any;
    }
    if (!Element.prototype.getAnimations) {
      Element.prototype.getAnimations = () => [] as any;
    }
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('refuses access to unauthorized learners without catalog management permissions', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 42,
          email: 'learner@demo.local',
          fullName: 'Learner User',
          role: 'learner',
          permissions: [],
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/question-bank');

    expect(await screen.findByText(/access denied/i)).toBeInTheDocument();
  });

  it('renders coverage matrix and displays authoritative deficit figures without recalculation', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Content Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.ContentAuthor],
        });
      }
      if (url === '/api/admin/coverage') {
        return jsonResponse(mockCoverage);
      }
      if (url === '/api/admin/ingestion') {
        return jsonResponse(mockIngestionBatches);
      }
      if (url.startsWith('/api/admin/lessons')) {
        return jsonResponse(mockLessons);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/question-bank');

    expect(await screen.findByRole('heading', { name: /question bank studio/i })).toBeInTheDocument();
    expect(screen.getByText('R-GIST')).toBeInTheDocument();
    expect(screen.getByText('Main Idea and Gist')).toBeInTheDocument();
    expect(screen.getByText('L-RESP')).toBeInTheDocument();

    // Deficit cell renders authoritative deficit count
    expect(screen.getByText('Deficit: 5')).toBeInTheDocument();
  });

  it('supports stimulus studio analysis and bundle save', async () => {
    let bundleSaved = false;

    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Content Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.ContentAuthor],
        });
      }
      if (url === '/api/admin/coverage') {
        return jsonResponse(mockCoverage);
      }
      if (url.startsWith('/api/admin/studio/stimulus/options')) {
        return jsonResponse(mockStimulusOptions);
      }
      if (url === '/api/admin/studio/stimulus/analyse' && init?.method === 'POST') {
        return jsonResponse({
          refusals: [],
          unrunChecks: [],
          gauges: [
            { label: 'Flesch-Kincaid Grade Level', value: '7.8', target: '7.0 - 9.0', inside: true },
          ],
          levelVerdict: 'Fits CEFR B1 band',
          overlaps: [],
          gaps: [],
          fittingBand: 'B1',
          selectedBand: 'B1',
          acceptable: true,
        });
      }
      if (url === '/api/admin/studio/stimulus' && init?.method === 'POST') {
        bundleSaved = true;
        return jsonResponse({
          stimulusId: 44,
          stimulusLevel: 'B1',
          children: [],
        });
      }
      if (url === '/api/admin/ingestion') {
        return jsonResponse(mockIngestionBatches);
      }
      if (url.startsWith('/api/admin/lessons')) {
        return jsonResponse(mockLessons);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/question-bank');

    // Switch to Stimulus Studio tab
    const stimulusTab = await screen.findByRole('tab', { name: /stimulus studio/i });
    fireEvent.click(stimulusTab);

    expect(await screen.findByRole('heading', { name: /stimulus & multi-item bundle studio/i })).toBeInTheDocument();

    // Click Analyse Bundle
    const analyseBtn = screen.getByRole('button', { name: /analyse stimulus bundle/i });
    fireEvent.click(analyseBtn);

    expect(await screen.findByText(/fits cefr b1 band/i)).toBeInTheDocument();
    expect(screen.getByText('Flesch-Kincaid Grade Level')).toBeInTheDocument();

    // Click Save Bundle
    const saveBtn = screen.getByRole('button', { name: /save stimulus bundle/i });
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(bundleSaved).toBe(true);
      expect(screen.getByText(/stimulus bundle saved successfully/i)).toBeInTheDocument();
    });
  });

  it('renders ingestion workbench, reports per-line errors, and gates Clear button with pool.content.review', async () => {
    let clearCalled = false;

    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          userId: 2,
          email: 'reviewer@demo.local',
          fullName: 'Content Reviewer',
          role: 'staff',
          emailVerified: true,
          // Has ContentAuthor AND ContentReview
          permissions: [STAFF_PERMISSIONS.ContentAuthor, STAFF_PERMISSIONS.ContentReview],
        });
      }
      if (url === '/api/admin/coverage') {
        return jsonResponse(mockCoverage);
      }
      if (url === '/api/admin/ingestion') {
        return jsonResponse(mockIngestionBatches);
      }
      if (url === '/api/admin/ingestion/901/items/801/clear' && init?.method === 'POST') {
        clearCalled = true;
        return new Response(null, { status: 204 });
      }
      if (url.startsWith('/api/admin/ingestion/901')) {
        return jsonResponse(mockBatchDetail);
      }
      if (url.startsWith('/api/admin/lessons')) {
        return jsonResponse(mockLessons);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/question-bank');

    // Switch to Ingestion Workbench tab
    const ingestTab = await screen.findByRole('tab', { name: /manifest import & ingestion/i });
    fireEvent.click(ingestTab);

    expect(await screen.findByText('ETS_2026_Reading_Pool.json')).toBeInTheDocument();

    // Open batch 901
    const openBatchBtn = screen.getByRole('button', { name: /open workbench for batch 901/i });
    fireEvent.click(openBatchBtn);

    // Verify per-line refusal message is displayed
    expect(await screen.findByText(/all 4 options require clear author rationale/i)).toBeInTheDocument();
    expect(screen.getByText('Line 12')).toBeInTheDocument();
    expect(screen.getByText('gate1.options.rationale_required')).toBeInTheDocument();

    // Verify batch audit queue link (#32)
    const auditLink = screen.getByRole('link', { name: /open batch audit queue/i });
    expect(auditLink).toHaveAttribute('href', '/admin/audit/901');

    // Click Clear item button (gated by pool.content.review)
    const clearBtn = screen.getByRole('button', { name: /clear item 801/i });
    fireEvent.click(clearBtn);

    await waitFor(() => {
      expect(clearCalled).toBe(true);
    });
  });
});
