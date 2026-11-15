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
      purpose: 'mock',
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
      name: 'TOEIC Placement Test 1',
      status: 'published',
      purpose: 'placement',
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

const mockCertificates = [
  {
    certId: 1,
    code: 'TOEIC',
    name: 'TOEIC Listening and Reading',
    description: null,
    scoreAggregationRule: 'SUM',
    presetCode: null,
    isActive: true,
    skillsCount: 2,
    sectionsCount: 7,
    examsCount: 3,
  },
  {
    certId: 2,
    code: 'IELTS',
    name: 'IELTS Academic',
    description: null,
    scoreAggregationRule: 'AVERAGE',
    presetCode: null,
    isActive: true,
    skillsCount: 4,
    sectionsCount: 7,
    examsCount: 1,
  },
];

const mockExamDetail = {
  examId: 101,
  certId: 1,
  certCode: 'TOEIC',
  certName: 'TOEIC Listening and Reading',
  name: 'TOEIC Official Practice Test 1',
  status: 'draft',
  durationMinutes: 120,
  purpose: 'mock',
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

const mockExamStudio = {
  examId: 101,
  certId: 1,
  certCode: 'TOEIC',
  certName: 'TOEIC Listening and Reading',
  name: 'TOEIC Official Practice Test 1',
  status: 'draft',
  purpose: 'mock',
  durationMinutes: 120,
  description: null,
  sourceName: null,
  internalNote: null,
  questionCount: 200,
  actualQuestionCount: 195,
  finishedAttemptCount: 0,
  createdAt: '2026-11-15T01:00:00+07:00',
  updatedAt: null,
  sections: [
    {
      sectionId: 7,
      certId: 1,
      code: 'L1',
      name: 'Listening Comprehension',
      modality: 'listening',
      expectedQuestionCount: 25,
      displayOrder: 1,
      currentQuestionCount: 1,
      hasAudio: true,
      masterAudioPath: 'exams/101/master.mp3',
      masterAudioDurationSec: 1200,
      groups: [
        {
          groupId: 11,
          examId: 101,
          certId: 1,
          sectionId: 7,
          sectionCode: 'L1',
          sectionName: 'Listening Comprehension',
          modality: 'listening',
          passage: 'Questions 1 through 3 refer to the following announcement.',
          audioPath: 'exams/101/master.mp3',
          audioStartMs: 0,
          audioEndMs: 52000,
          imagePath: null,
          imageAlt: null,
          transcript: 'Attention, attendees of the Global FinTech Summit.',
          prepSeconds: null,
          responseSeconds: null,
          displayOrder: 1,
          unitNumber: 1,
          questions: [
            {
              questionId: 501,
              itemId: 9001,
              groupId: 11,
              examId: 101,
              sectionId: 7,
              sectionCode: 'L1',
              sectionName: 'Listening Comprehension',
              skillId: 1,
              skillCode: 'LIST',
              stem: 'Which auditorium hosts the keynote?',
              questionType: 'mcq',
              explanation: null,
              categoryId: null,
              categoryCode: null,
              categoryName: null,
              prepSeconds: null,
              responseSeconds: null,
              minWords: null,
              maxWords: null,
              hint: null,
              displayOrder: 1,
              updatedAt: null,
              options: [],
              criteria: [],
              hasAnswers: false,
              topicId: 12,
            },
          ],
        },
      ],
    },
  ],
  availableCategories: [],
  availableCriteria: [],
  audit: {
    canPublish: false,
    totalQuestions: 1,
    expectedQuestions: 25,
    blockingErrorCount: 1,
    warningCount: 0,
    issues: [],
    missingItems: [],
  },
};

/** Two questions in one Bundle, so an order has somewhere to move to. */
const mockExamStudioTwoQuestions = {
  ...mockExamStudio,
  sections: [
    {
      ...mockExamStudio.sections[0],
      currentQuestionCount: 2,
      groups: [
        {
          ...mockExamStudio.sections[0].groups[0],
          questions: [
            mockExamStudio.sections[0].groups[0].questions[0],
            {
              ...mockExamStudio.sections[0].groups[0].questions[0],
              questionId: 502,
              itemId: 9002,
              stem: 'How large is the overflow room?',
              displayOrder: 2,
            },
          ],
        },
      ],
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
    expect(screen.getByText('TOEIC Placement Test 1')).toBeInTheDocument();

    // Purpose labels come from the four real codes only (#357).
    expect(screen.getAllByText('Mock exam').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Placement test').length).toBeGreaterThan(0);
    expect(screen.queryByText('Full mock exam')).toBeNull();
    expect(screen.queryByText('Mini mock exam')).toBeNull();
    expect(screen.queryByText('Diagnostic')).toBeNull();

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

    // Open Quick Ingest modal from the P1 Section card
    const sectionList = await screen.findByRole('list', { name: /exam sections/i });
    const sectionCards = within(sectionList).getAllByRole('listitem');
    fireEvent.click(within(sectionCards[0]).getByRole('button', { name: /paste questions/i }));

    expect(await screen.findByRole('heading', { name: /quick ingest questions/i })).toBeInTheDocument();

    // Select required CEFR level and topic
    fireEvent.click(screen.getByRole('button', { name: /cefr level/i }));
    fireEvent.click(await screen.findByRole('option', { name: 'B1' }));
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

  it('lists every Section with its bound and expected question count (#358)', async () => {
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
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');

    fireEvent.click(
      await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i }),
    );
    fireEvent.click(await screen.findByRole('tab', { name: /studio & quick ingest/i }));

    const sectionList = await screen.findByRole('list', { name: /exam sections/i });
    const cards = within(sectionList).getAllByRole('listitem');

    expect(cards).toHaveLength(mockExamDetail.sectionProgress.length);
    expect(within(cards[0]).getByText('P1')).toBeInTheDocument();
    expect(within(cards[0]).getByText('Photographs')).toBeInTheDocument();
    expect(within(cards[0]).getByText('6 / 6')).toBeInTheDocument();
    expect(within(cards[1]).getByText('Question-Response')).toBeInTheDocument();
    expect(within(cards[1]).getByText('20 / 25')).toBeInTheDocument();
    expect(
      within(cards[1]).getByRole('button', { name: /paste questions/i }),
    ).toBeInTheDocument();

    // The dead placeholder is gone for good.
    expect(screen.queryByText(/select a section or group to edit questions/i)).toBeNull();
  });

  it('launches Quick Ingest from the chosen Section and commits against it (#358)', async () => {
    let parseBody: any = null;
    let committedRequest: any = null;
    let detailCalls = 0;

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
        detailCalls += 1;
        // The second read is the post-commit refresh: P2 gained one bound question.
        const detail =
          detailCalls === 1
            ? mockExamDetail
            : {
                ...mockExamDetail,
                sectionProgress: mockExamDetail.sectionProgress.map((sec) =>
                  sec.sectionId === 2 ? { ...sec, questionCount: 21 } : sec,
                ),
              };
        return jsonResponse(detail);
      }
      if (url === '/api/admin/sections/2/quick-ingest/sample') {
        return jsonResponse({
          template: '1. A\nTRANSCRIPT: When does the shuttle leave?',
          formatDescription: 'Section Blueprint Tailored Template',
        });
      }
      if (url === '/api/admin/exams/101/quick-ingest/parse' && init?.method === 'POST') {
        parseBody = JSON.parse(init.body as string);
        return jsonResponse({
          success: true,
          totalQuestions: 1,
          validQuestions: 1,
          errorCount: 0,
          questions: [
            {
              index: 1,
              startLineNumber: 1,
              endLineNumber: 2,
              stem: 'Where is the nearest bus stop?',
              explanation: null,
              categoryCode: null,
              options: [
                { label: 'A', text: 'Next to the library.', isCorrect: true },
                { label: 'B', text: 'Two hours ago.', isCorrect: false },
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

    fireEvent.click(
      await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i }),
    );
    fireEvent.click(await screen.findByRole('tab', { name: /studio & quick ingest/i }));

    const sectionList = await screen.findByRole('list', { name: /exam sections/i });
    fireEvent.click(
      within(within(sectionList).getAllByRole('listitem')[1]).getByRole('button', {
        name: /paste questions/i,
      }),
    );

    expect(await screen.findByRole('heading', { name: /quick ingest questions/i })).toBeInTheDocument();

    // The modal opens on the Section the author clicked, not on the first Section.
    expect(screen.getByRole('button', { name: /target section/i })).toHaveTextContent('P2');
    // The Section's own paste template is offered.
    expect(
      await screen.findByRole('button', { name: /section blueprint tailored template/i }),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText(/paste questions here/i), {
      target: { value: '1. A\nTRANSCRIPT: Where is the nearest bus stop?' },
    });
    fireEvent.click(screen.getByRole('button', { name: /parse text/i }));
    expect(await screen.findByText(/Where is the nearest bus stop/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /commit questions/i }));

    await waitFor(() => {
      expect(committedRequest).not.toBeNull();
    });
    expect(parseBody.sectionId).toBe(2);
    expect(committedRequest.sectionId).toBe(2);

    // The write is reported inside the modal, with what was actually written.
    const dialog = await screen.findByRole('dialog');
    expect(
      within(dialog).getByText(/1 pool items written, 1 bound to this section/i),
    ).toBeInTheDocument();

    // The refreshed detail raises P2's bound count before the author closes the modal.
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    expect(await screen.findByText('21 / 25')).toBeInTheDocument();
  });

  it('uploads a Listening Section audio file and records the answered path (#359)', async () => {
    let uploadCalls: { url: string; body: FormData | null }[] = [];
    let audioBody: any = null;

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
      if (url === '/api/admin/exams/101/media' && init?.method === 'POST') {
        uploadCalls.push({ url, body: init.body as FormData });
        return jsonResponse({
          url: '/media/exams/101/abc.mp3',
          path: 'exams/101/abc.mp3',
          fileName: 'abc.mp3',
        });
      }
      if (url === '/api/admin/exams/101/audio' && init?.method === 'POST') {
        audioBody = JSON.parse(String(init.body));
        return jsonResponse({}, 200);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');
    fireEvent.click(
      await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i }),
    );
    fireEvent.click(await screen.findByRole('tab', { name: /sections & audio/i }));

    // A Listening Section offers the audio control; a Reading one must not.
    const audioInputs = await screen.findAllByLabelText(/upload audio track/i);
    const audioInput = audioInputs[0] as HTMLInputElement;
    expect(audioInputs).toHaveLength(
      mockExamDetail.sectionProgress.filter((s) => s.modality === 'listening').length,
    );
    expect(audioInput.type).toBe('file');
    expect(audioInput.accept).toContain('.mp3');
    expect(audioInput.accept).not.toContain('.mp4');

    // A rejected format is refused before the bytes leave the browser.
    const badFile = new File(['x'], 'clip.mp4', { type: 'video/mp4' });
    fireEvent.change(audioInput, { target: { files: [badFile] } });
    expect(await screen.findByText(/'\.mp4' is not audio/i)).toBeInTheDocument();
    expect(uploadCalls).toHaveLength(0);

    // An image is media the route accepts, but it is not an audio track. A shared allow-list let a
    // .png be saved as a Section's audio; the audio control now refuses it by name.
    const imageFile = new File(['png'], 'diagram.png', { type: 'image/png' });
    fireEvent.change(audioInput, { target: { files: [imageFile] } });
    expect(await screen.findByText(/'\.png' is not audio/i)).toBeInTheDocument();
    expect(uploadCalls).toHaveLength(0);

    // An accepted file uploads, and the path the server answered is what gets recorded.
    const goodFile = new File(['audio'], 'part1.mp3', { type: 'audio/mpeg' });
    fireEvent.change(audioInput, { target: { files: [goodFile] } });

    await waitFor(() => expect(audioBody).not.toBeNull());
    expect(uploadCalls).toHaveLength(1);
    expect(uploadCalls[0].body?.get('file')).toBeInstanceOf(File);
    expect(audioBody.audioPath).toBe('exams/101/abc.mp3');
    expect(audioBody.sectionId).toBe(mockExamDetail.sectionProgress[0].sectionId);
    expect(audioBody.durationSec).toBeGreaterThan(0);
    expect(audioBody.sizeBytes).toBe(goodFile.size);
  });

  it('lists the Bundles of a Section with their stimulus and saves an edited one (#362)', async () => {
    let groupUpdate: { url: string; body: any } | null = null;

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
      if (url === '/api/admin/exams/101/studio') {
        return jsonResponse(mockExamStudio);
      }
      if (url.startsWith('/api/admin/exams/101/studio/groups/') && init?.method === 'PUT') {
        groupUpdate = { url, body: JSON.parse(String(init.body)) };
        return jsonResponse({ success: true });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');
    fireEvent.click(
      await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i }),
    );
    fireEvent.click(await screen.findByRole('tab', { name: /section content/i }));

    const sectionList = await screen.findByRole('list', { name: /section bundles/i });
    const sectionCards = within(sectionList).getAllByRole('listitem');
    expect(sectionCards[0]).toHaveTextContent('L1');
    // The bundle count is on the Section header, before anything is expanded.
    expect(within(sectionCards[0]).getByText(/1 bundle/i)).toBeInTheDocument();

    fireEvent.click(within(sectionCards[0]).getByRole('button', { name: /show bundles/i }));

    // The Bundle shows its stimulus and the child that refers to it.
    expect(await within(sectionCards[0]).findByText(/1 question/i)).toBeInTheDocument();
    expect(within(sectionCards[0]).getByText('attached')).toBeInTheDocument();
    expect(
      within(sectionCards[0]).getByText(/which auditorium hosts the keynote/i),
    ).toBeInTheDocument();

    fireEvent.click(within(sectionCards[0]).getByRole('button', { name: /edit stimulus/i }));
    fireEvent.click(within(sectionCards[0]).getByRole('button', { name: /save bundle/i }));

    await waitFor(() => expect(groupUpdate).not.toBeNull());
    expect(groupUpdate!.url).toBe('/api/admin/exams/101/studio/groups/11');
    // The edit names the Section it lands in: a stimulus can be bound by two sections.
    expect(groupUpdate!.body.sectionId).toBe(7);
    expect(groupUpdate!.body.audioPath).toBe('exams/101/master.mp3');
    // The audio slice survives a save that did not touch it. The group route assigns both fields
    // unconditionally, so omitting them would widen the Bundle's audio to the whole track.
    expect(groupUpdate!.body.audioStartMs).toBe(0);
    expect(groupUpdate!.body.audioEndMs).toBe(52000);
  });

  it('edits a child question through the pool item route, not the paper position (#362)', async () => {
    let itemUpdate: { url: string; body: any } | null = null;

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
      if (url.startsWith('/api/admin/exams?')) return jsonResponse(mockExamList);
      if (url === '/api/admin/exams/101') return jsonResponse(mockExamDetail);
      if (url === '/api/admin/exams/101/studio') return jsonResponse(mockExamStudio);
      if (url === '/api/admin/items/9001' && init?.method === 'PUT') {
        itemUpdate = { url, body: JSON.parse(String(init.body)) };
        return jsonResponse({ itemId: 9001 });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');
    fireEvent.click(
      await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i }),
    );
    fireEvent.click(await screen.findByRole('tab', { name: /section content/i }));

    const sectionList = await screen.findByRole('list', { name: /section bundles/i });
    fireEvent.click(within(sectionList).getByRole('button', { name: /show bundles/i }));
    fireEvent.click(await within(sectionList).findByRole('button', { name: /edit question/i }));

    const stem = within(sectionList).getByLabelText(/^stem$/i);
    fireEvent.change(stem, { target: { value: 'Which auditorium hosts the keynote session?' } });
    fireEvent.click(within(sectionList).getByRole('button', { name: /save question/i }));

    await waitFor(() => expect(itemUpdate).not.toBeNull());
    // The pool item id, NOT questionId (the paper position handle, 501): PUT /items/501 would address
    // a different item or nothing at all.
    expect(itemUpdate!.url).toBe('/api/admin/items/9001');
    expect(itemUpdate!.body.stem).toBe('Which auditorium hosts the keynote session?');
  });

  it('reorders the questions of a Section through the roster order (#362)', async () => {
    let reorderBody: any = null;

    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse({ id: 1, email: 'a@demo.local', fullName: 'A', role: 'staff', permissions: [STAFF_PERMISSIONS.ContentAuthor] });
      if (url.startsWith('/api/admin/exams?')) return jsonResponse(mockExamList);
      if (url === '/api/admin/exams/101') return jsonResponse(mockExamDetail);
      if (url === '/api/admin/exams/101/studio') return jsonResponse(mockExamStudioTwoQuestions);
      if (url === '/api/admin/exams/101/reorder' && init?.method === 'POST') {
        reorderBody = JSON.parse(String(init.body));
        return jsonResponse({ success: true });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');
    fireEvent.click(await screen.findByRole('button', { name: /open studio for toeic official practice test 1/i }));
    fireEvent.click(await screen.findByRole('tab', { name: /section content/i }));

    const list = await screen.findByRole('list', { name: /section bundles/i });
    fireEvent.click(within(list).getByRole('button', { name: /show bundles/i }));

    // Question 1 cannot move up; question 2 cannot move down. That is the boundary, not a bug.
    expect(await within(list).findByRole('button', { name: /move question 1 up/i })).toBeDisabled();

    fireEvent.click(within(list).getByRole('button', { name: /move question 2 up/i }));

    await waitFor(() => expect(reorderBody).not.toBeNull());
    expect(reorderBody.sectionId).toBe(7);
    // The roster's own order: questionId IS the position handle the reorder route expects.
    expect(reorderBody.items).toEqual([
      { groupId: null, questionId: 502, displayOrder: 1 },
      { groupId: null, questionId: 501, displayOrder: 2 },
    ]);
  });

  it('binds backend RFC 7807 field errors to HeroUI FieldError controls in the create exam modal', async () => {
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Exam Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.ContentAuthor, STAFF_PERMISSIONS.BlueprintDesign],
        });
      }
      if (url.startsWith('/api/admin/exams?')) {
        return jsonResponse(mockExamList);
      }
      if (url === '/api/admin/certificates') {
        return jsonResponse(mockCertificates);
      }
      if (url === '/api/admin/exams' && init?.method === 'POST') {
        return jsonResponse(
          {
            title: 'Validation failed',
            status: 400,
            code: 'VALIDATION_ERROR',
            errors: {
              name: ['Exam name is required.'],
              durationMinutes: ['Duration must be between 1 and 600 minutes.'],
            },
          },
          400,
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');

    fireEvent.click(await screen.findByRole('button', { name: /new exam/i }));
    expect(await screen.findByRole('heading', { name: /create new exam/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /certificate/i }));
    fireEvent.click(await screen.findByRole('option', { name: /toeic listening and reading/i }));

    fireEvent.click(screen.getByRole('button', { name: /create draft exam/i }));

    expect(await screen.findByText('Exam name is required.')).toBeInTheDocument();
    expect(screen.getByText('Duration must be between 1 and 600 minutes.')).toBeInTheDocument();
  });

  it('creates an exam with the chosen Certificate and the default mock purpose (#357)', async () => {
    let createBody: any = null;

    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Exam Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.ContentAuthor, STAFF_PERMISSIONS.BlueprintDesign],
        });
      }
      if (url === '/api/admin/certificates') {
        return jsonResponse(mockCertificates);
      }
      if (url.startsWith('/api/admin/exams?')) {
        return jsonResponse(mockExamList);
      }
      if (url === '/api/admin/exams' && init?.method === 'POST') {
        createBody = JSON.parse(init.body as string);
        return jsonResponse({ examId: 999 }, 201);
      }
      if (url === '/api/admin/exams/999') {
        return jsonResponse({ ...mockExamDetail, examId: 999 });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');

    fireEvent.click(await screen.findByRole('button', { name: /new exam/i }));
    expect(await screen.findByRole('heading', { name: /create new exam/i })).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText(/toeic official mock exam 2/i), {
      target: { value: 'IELTS Academic Mock 1' },
    });

    fireEvent.click(screen.getByRole('button', { name: /certificate/i }));
    fireEvent.click(await screen.findByRole('option', { name: /ielts academic/i }));

    fireEvent.click(screen.getByRole('button', { name: /create draft exam/i }));

    await waitFor(() => {
      expect(createBody).not.toBeNull();
    });
    expect(createBody.purpose).toBe('mock');
    expect(createBody.certId).toBe(2);
    expect(createBody.name).toBe('IELTS Academic Mock 1');
  });

  it('uses platform SearchField and Select filters on exam catalog (#338)', async () => {
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
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/exams');
    expect(await screen.findByText('TOEIC Official Practice Test 1')).toBeInTheDocument();

    const searchBox = screen.getByRole('searchbox', { name: /search exams/i });
    fireEvent.change(searchBox, { target: { value: 'Placement' } });
    expect(searchBox).toHaveValue('Placement');
    fireEvent.click(screen.getByRole('button', { name: /clear/i }));
    expect(searchBox).toHaveValue('');

    fireEvent.click(screen.getByRole('button', { name: /filter status/i }));
    fireEvent.click(await screen.findByRole('option', { name: /^published$/i }));

    fireEvent.click(screen.getByRole('button', { name: /filter purpose/i }));
    fireEvent.click(await screen.findByRole('option', { name: /placement test/i }));
  });
});
