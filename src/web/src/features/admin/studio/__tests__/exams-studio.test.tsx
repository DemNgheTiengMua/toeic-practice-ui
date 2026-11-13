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

const mockExamList = {
  exams: [
    {
      examId: 101,
      certId: 1,
      certCode: 'TOEIC',
      certName: 'TOEIC Listening and Reading',
      name: 'TOEIC Official Practice Test 1',
      status: 'draft',
      purpose: 'full_mock',
      durationMinutes: 120,
      questionCount: 200,
      actualQuestionCount: 195,
      audioStatus: 'complete',
      finishedAttemptCount: 12,
      updatedAt: '2026-11-01T10:00:00Z',
      createdAt: '2026-10-15T08:00:00Z',
    },
    {
      examId: 102,
      certId: 1,
      certCode: 'TOEIC',
      certName: 'TOEIC Listening and Reading',
      name: 'TOEIC Mini Mock 1',
      status: 'published',
      purpose: 'mini_mock',
      durationMinutes: 60,
      questionCount: 100,
      actualQuestionCount: 100,
      audioStatus: 'complete',
      finishedAttemptCount: 45,
      updatedAt: '2026-11-05T12:00:00Z',
      createdAt: '2026-10-20T09:00:00Z',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 25,
  certificates: [{ certId: 1, code: 'TOEIC', name: 'TOEIC Listening and Reading' }],
};

const mockExamDetail = {
  examId: 101,
  certId: 1,
  certCode: 'TOEIC',
  certName: 'TOEIC Listening and Reading',
  name: 'TOEIC Official Practice Test 1',
  status: 'draft',
  durationMinutes: 120,
  purpose: 'full_mock',
  description: 'Full official mock paper with 200 questions.',
  sourceName: 'ETS Official 2026',
  internalNote: 'Needs review on Part 7 passage length.',
  questionCount: 200,
  actualQuestionCount: 195,
  finishedAttemptCount: 12,
  createdAt: '2026-10-15T08:00:00Z',
  updatedAt: '2026-11-01T10:00:00Z',
  audios: [
    {
      examId: 101,
      sectionId: 1,
      sectionCode: 'P1',
      sectionName: 'Photographs',
      audioPath: '/media/exams/101/p1.mp3',
      durationSec: 360,
      sizeBytes: 4200000,
      uploadedAt: '2026-10-16T09:00:00Z',
    },
  ],
  sectionProgress: [
    {
      sectionId: 1,
      sectionCode: 'P1',
      sectionName: 'Photographs',
      modality: 'listening',
      questionCount: 6,
      expectedQuestionCount: 6,
      hasAudio: true,
      isComplete: true,
      statusText: 'Complete',
    },
    {
      sectionId: 2,
      sectionCode: 'P2',
      sectionName: 'Question-Response',
      modality: 'listening',
      questionCount: 20,
      expectedQuestionCount: 25,
      hasAudio: false,
      isComplete: false,
      statusText: '5 questions short, audio missing',
    },
  ],
};

const mockAssembly = {
  examId: 101,
  examName: 'TOEIC Official Practice Test 1',
  certCode: 'TOEIC',
  contentTier: 'live',
  durationMinutes: 120,
  finishedAttemptCount: 12,
  sections: [
    {
      sectionId: 2,
      code: 'P2',
      name: 'Question-Response',
      modality: 'listening',
      displayOrder: 2,
      declaredCount: 25,
      boundCount: 20,
      supplyCount: 40,
      shortfall: 5,
      positions: [
        { displayOrder: 21, isDeclared: true, itemVersionId: null, topicCode: 'L-RESP', cefrLevel: 'B1', unitNumber: 1 },
      ],
      candidates: [
        {
          itemId: 501,
          itemVersionId: 701,
          versionNumber: 1,
          topicCode: 'L-RESP',
          cefrLevel: 'B1',
          itemType: 'mcq_3',
          contentTier: 'live',
          stem: 'Where did you put the sales report?',
        },
      ],
      candidateTotal: 1,
    },
  ],
};

const mockAuditBlocked = {
  canPublish: false,
  totalQuestions: 195,
  expectedQuestions: 200,
  blockingErrorCount: 2,
  warningCount: 1,
  issues: [
    {
      code: 'SECTION_SHORTFALL',
      severity: 'error',
      message: 'Section P2 has 20 questions bound, expected 25 questions.',
      sectionId: 2,
      sectionCode: 'P2',
    },
    {
      code: 'AUDIO_MISSING',
      severity: 'error',
      message: 'Listening section P2 requires audio file before publishing.',
      sectionId: 2,
      sectionCode: 'P2',
    },
  ],
  missingItems: ['P2: 5 questions missing', 'P2: Section audio file missing'],
  invariants: [
    { code: 'INV-1', title: 'Declared question count matches blueprint', satisfied: false, deficitCount: 5, firstElementTarget: 'sec-p2' },
    { code: 'INV-2', title: 'Required audio present for listening sections', satisfied: false, deficitCount: 1, firstElementTarget: 'sec-p2' },
    { code: 'INV-3', title: 'Scoring scales and bands configured', satisfied: true, deficitCount: 0, firstElementTarget: null },
    { code: 'INV-4', title: 'Key and options valid for all items', satisfied: true, deficitCount: 0, firstElementTarget: null },
    { code: 'INV-5', title: 'Rationales present for active items', satisfied: true, deficitCount: 0, firstElementTarget: null },
    { code: 'INV-6', title: 'Production exams draw only production stock', satisfied: true, deficitCount: 0, firstElementTarget: null },
  ],
};

describe('Exams Authoring Studio (/admin/exams) (#303)', () => {
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

    renderRoute('/admin/exams');

    expect(await screen.findByText(/access denied/i)).toBeInTheDocument();
  });

  it('renders exam list and single tabbed workspace when an exam is selected', async () => {
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
      if (url.startsWith('/api/admin/exams?')) {
        return jsonResponse(mockExamList);
      }
      if (url === '/api/admin/exams/101') {
        return jsonResponse(mockExamDetail);
      }
      if (url === '/api/admin/exams/101/assembly') {
        return jsonResponse(mockAssembly);
      }
      if (url === '/api/admin/exams/101/audit') {
        return jsonResponse(mockAuditBlocked);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');

    expect(await screen.findByRole('heading', { name: /exams authoring studio/i })).toBeInTheDocument();
    expect(screen.getByText('TOEIC Official Practice Test 1')).toBeInTheDocument();
    expect(screen.getByText('TOEIC Mini Mock 1')).toBeInTheDocument();

    // Select Exam 101 to open tabbed workspace
    const editBtn = screen.getByRole('button', { name: /open studio for toeic official practice test 1/i });
    fireEvent.click(editBtn);

    // Verify workspace tabs appear
    expect(await screen.findByRole('tab', { name: /overview & metadata/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /sections & audio/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /roster & assembly/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /studio & quick ingest/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /publish readiness/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /import & export/i })).toBeInTheDocument();
  });

  it('enforces publish gate with clear inline errors and invariant reporting on refusal', async () => {
    let publishAttempted = false;

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
      if (url.startsWith('/api/admin/exams?')) {
        return jsonResponse(mockExamList);
      }
      if (url === '/api/admin/exams/101') {
        return jsonResponse(mockExamDetail);
      }
      if (url === '/api/admin/exams/101/assembly') {
        return jsonResponse(mockAssembly);
      }
      if (url === '/api/admin/exams/101/audit') {
        return jsonResponse(mockAuditBlocked);
      }
      if (url === '/api/admin/exams/101/publish' && init?.method === 'POST') {
        publishAttempted = true;
        return jsonResponse(
          {
            success: false,
            message: 'Publish refused: paper has unmet invariants.',
            missingItems: ['P2: 5 questions missing', 'P2: Section audio file missing'],
          },
          400,
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');

    const editBtn = await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i });
    fireEvent.click(editBtn);

    // Navigate to Publish Readiness tab
    const publishTab = await screen.findByRole('tab', { name: /publish readiness/i });
    fireEvent.click(publishTab);

    // Verify invariant audit list is rendered
    expect(await screen.findByText('Declared question count matches blueprint')).toBeInTheDocument();
    expect(screen.getByText('Required audio present for listening sections')).toBeInTheDocument();

    // Click Publish button
    const publishBtn = screen.getByRole('button', { name: /publish exam/i });
    fireEvent.click(publishBtn);

    await waitFor(() => {
      expect(publishAttempted).toBe(true);
      // Inline refusal alert with missing items must be displayed
      expect(screen.getAllByText(/publish refused/i).length).toBeGreaterThan(0);
      expect(screen.getByText(/p2: 5 questions missing/i)).toBeInTheDocument();
    });
  });

  it('supports quick ingest parsing and committing with required level and topic', async () => {
    let committedRequest: any = null;

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
      if (url.startsWith('/api/admin/exams?')) {
        return jsonResponse(mockExamList);
      }
      if (url === '/api/admin/exams/101') {
        return jsonResponse(mockExamDetail);
      }
      if (url === '/api/admin/exams/101/assembly') {
        return jsonResponse(mockAssembly);
      }
      if (url === '/api/admin/exams/101/audit') {
        return jsonResponse(mockAuditBlocked);
      }
      if (url === '/api/admin/exams/101/quick-ingest/parse' && init?.method === 'POST') {
        return jsonResponse({
          success: true,
          totalQuestions: 1,
          validQuestions: 1,
          errorCount: 0,
          questions: [
            {
              index: 1,
              startLineNumber: 1,
              endLineNumber: 6,
              stem: 'When will the project be finalized?',
              explanation: 'Option B accurately answers the timeline question.',
              categoryCode: null,
              options: [
                { label: 'A', text: 'Yes, in the conference room.', isCorrect: false },
                { label: 'B', text: 'By next Friday afternoon.', isCorrect: true },
                { label: 'C', text: 'Mr. Tanaka will attend.', isCorrect: false },
              ],
              issues: [],
              isValid: true,
              questionType: 'mcq_3',
            },
          ],
          globalIssues: [],
        });
      }
      if (url === '/api/admin/exams/101/quick-ingest/commit' && init?.method === 'POST') {
        committedRequest = JSON.parse(init.body as string);
        return jsonResponse({
          success: true,
          questionsCommitted: 1,
          groupsCreated: 1,
          message: 'Committed 1 question and created 1 pool item.',
          poolItemsWritten: 1,
          poolStimuliWritten: 0,
          poolItemsBound: 1,
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');

    const editBtn = await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i });
    fireEvent.click(editBtn);

    // Switch to Studio & Quick Ingest tab
    const studioTab = await screen.findByRole('tab', { name: /studio & quick ingest/i });
    fireEvent.click(studioTab);

    // Open Quick Ingest modal
    const openIngestBtn = await screen.findByRole('button', { name: /quick ingest/i });
    fireEvent.click(openIngestBtn);

    expect(await screen.findByRole('heading', { name: /quick ingest questions/i })).toBeInTheDocument();

    // Select required CEFR level and topic
    const levelSelect = screen.getByLabelText(/cefr level/i);
    fireEvent.change(levelSelect, { target: { value: 'B1' } });
    const topicInput = screen.getByLabelText(/topic code/i);
    fireEvent.change(topicInput, { target: { value: 'L-RESP' } });

    // Paste text into editor
    const textarea = screen.getByPlaceholderText(/paste questions here/i);
    fireEvent.change(textarea, {
      target: {
        value: `1. When will the project be finalized?
(A) Yes, in the conference room.
(B) *By next Friday afternoon.
(C) Mr. Tanaka will attend.`,
      },
    });

    // Click Parse
    const parseBtn = screen.getByRole('button', { name: /parse text/i });
    fireEvent.click(parseBtn);

    expect(await screen.findByText(/When will the project be finalized/i)).toBeInTheDocument();

    // Commit parsed questions
    const commitBtn = screen.getByRole('button', { name: /commit questions/i });
    fireEvent.click(commitBtn);

    await waitFor(() => {
      expect(committedRequest).not.toBeNull();
      expect(committedRequest.cefrLevel).toBe('B1');
      expect(committedRequest.topicCode).toBe('L-RESP');
      expect(committedRequest.questions.length).toBe(1);
    });
  });
});
