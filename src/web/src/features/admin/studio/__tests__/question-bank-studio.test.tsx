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

    // #310: Only 2 flat tabs (Question Bank & Lessons)
    expect(screen.getByRole('tab', { name: /question bank/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /lessons/i })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /stimulus studio/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /productive tasks/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /manifest import/i })).not.toBeInTheDocument();
  });

  it('creates a pool item with TargetBands selector and immediately active status', async () => {
    let savedBody: any = null;

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
      if (url.startsWith('/api/admin/lessons')) {
        return jsonResponse(mockLessons);
      }
      if (url === '/api/admin/items' && init?.method === 'POST') {
        savedBody = JSON.parse(init.body as string);
        return jsonResponse({
          itemId: 501,
          itemVersionId: 1,
          versionNumber: 1,
          versionCount: 1,
          modality: savedBody.modality,
          topicId: savedBody.topicId,
          topicCode: 'R-GIST',
          cefrLevelCode: savedBody.cefrLevelCode,
          itemType: savedBody.itemType,
          contentTier: 'production',
          lifecycleStatus: 'active',
          placementEligible: false,
          stimulusId: null,
          stem: savedBody.stem,
          optionA: savedBody.optionA,
          optionB: savedBody.optionB,
          optionC: savedBody.optionC,
          optionD: savedBody.optionD,
          keyLabel: savedBody.keyLabel,
          explanation: savedBody.explanation,
          evidenceStart: null,
          evidenceEnd: null,
          evidenceUnit: null,
          evidenceQuote: null,
          referenceText: null,
          referenceKind: null,
          createdAt: new Date().toISOString(),
          targetBands: savedBody.targetBands,
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/question-bank');

    const newItemBtn = await screen.findByRole('button', { name: /new pool item/i });
    fireEvent.click(newItemBtn);

    const stemInput = await screen.findByPlaceholderText(/enter clear, concise question prompt/i);
    fireEvent.change(stemInput, { target: { value: 'What is the main idea of the memo?' } });

    // Select multi-band TargetBands (uncheck ALL or click B1, B2)
    const bandB1 = screen.getByRole('checkbox', { name: /^B1$/i });
    const bandB2 = screen.getByRole('checkbox', { name: /^B2$/i });
    fireEvent.click(bandB1);
    fireEvent.click(bandB2);

    const createBtn = screen.getByRole('button', { name: /create item/i });
    fireEvent.click(createBtn);

    await waitFor(() => {
      expect(savedBody).not.toBeNull();
      expect(savedBody.targetBands).toBe('B1,B2');
    });
  });

  it('displays Lessons tab with TargetBands chips', async () => {
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
      if (url.startsWith('/api/admin/lessons')) {
        return jsonResponse([
          { ...mockLessons[0], targetBands: 'B1,B2' },
        ]);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/question-bank');

    const lessonsTab = await screen.findByRole('tab', { name: /lessons/i });
    fireEvent.click(lessonsTab);

    expect(await screen.findByText('Skimming & Scanning Mastery')).toBeInTheDocument();
    expect(screen.getByText('B1,B2')).toBeInTheDocument();
  });
});
