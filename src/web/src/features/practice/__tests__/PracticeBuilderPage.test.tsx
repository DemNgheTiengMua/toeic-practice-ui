import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from '@tanstack/react-router';
import { AuthProvider } from '@/context/AuthContext';
import type {
  FreeDrillStateDto,
  PathStepDto,
  PracticeCatalogDto,
  PracticeCellDto,
  PracticeSessionDto,
  StartStepResultDto,
} from '@/lib/api';
import { PracticeBuilderPage } from '../PracticeBuilderPage';

function makeCatalog(overrides: Partial<PracticeCatalogDto> = {}): PracticeCatalogDto {
  return {
    certificate: { code: 'TOEIC', name: 'TOEIC Listening & Reading', sectionNoun: 'Part' },
    listeningSections: [
      {
        sectionId: 101,
        skillCode: 'LIST',
        skillName: 'Listening',
        modality: 'listening',
        code: 'PART1',
        name: 'Photographs',
        optionCount: 4,
        expectedQuestionCount: 10,
        availableQuestionCount: 25,
        accuracyPercentage: 80,
        answeredCount: 20,
        displayOrder: 1,
        totalCategoryCount: 4,
        categoriesInCooldown: 0,
        nextReviewDueAt: null,
      },
    ],
    readingSections: [
      {
        sectionId: 102,
        skillCode: 'READ',
        skillName: 'Reading',
        modality: 'reading',
        code: 'PART5',
        name: 'Incomplete Sentences',
        optionCount: 4,
        expectedQuestionCount: 10,
        availableQuestionCount: 40,
        accuracyPercentage: 65,
        answeredCount: 15,
        displayOrder: 5,
        totalCategoryCount: 6,
        categoriesInCooldown: 1,
        nextReviewDueAt: '2026-11-15T10:00:00Z',
      },
    ],
    otherSections: [],
    weakestSection: null,
    unfinishedSession: null,
    speakingSections: [
      {
        sectionId: 201,
        skillCode: 'SPEAK',
        skillName: 'Speaking',
        modality: 'speaking',
        code: 'READ_ALOUD',
        name: 'Read a Text Aloud',
        optionCount: null,
        expectedQuestionCount: 2,
        availableQuestionCount: 6,
        accuracyPercentage: null,
        answeredCount: 0,
        displayOrder: 1,
        totalCategoryCount: 1,
        categoriesInCooldown: 0,
      },
    ],
    speakingPracticeExamId: 77,
    ...overrides,
  };
}

function makeMatrix(): PracticeCellDto[] {
  return [
    {
      topicId: 10,
      topicCode: 'GRAM_TENSE',
      topicName: 'Verb Tenses',
      modality: 'reading',
      levelCode: 'B1',
      mastery: 1, // numeric Learning
      isPrioritised: true,
      reopensAt: null,
      isResting: false,
    },
    {
      topicId: 11,
      topicCode: 'VOCAB_OFFICE',
      topicName: 'Office Vocabulary',
      modality: 'reading',
      levelCode: 'B2',
      mastery: 'Mastered',
      isPrioritised: false,
      reopensAt: '2026-11-16T12:00:00Z',
      isResting: true,
    },
  ];
}

function makeFreeDrill(overrides: Partial<FreeDrillStateDto> = {}): FreeDrillStateDto {
  return {
    isUnlocked: true,
    incompleteSteps: [],
    remainingMinutes: 0,
    priorities: [],
    cellUsedToday: false,
    dailyCeilingReached: false,
    answeredToday: 4,
    dailyItemCeiling: 30,
    lockNotice: null,
    ...overrides,
  };
}

async function renderPracticeBuilder(initialEntry = '/practice') {
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const practiceRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice',
    component: PracticeBuilderPage,
  });
  const sessionRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice/session/$sessionId',
    component: function SessionStub() {
      const { sessionId } = sessionRoute.useParams();
      return <div data-testid="session-route">Session #{sessionId}</div>;
    },
  });
  const speakingRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/speaking',
    component: () => <div data-testid="speaking-route">Speaking hub</div>,
  });

  const router = createRouter({
    routeTree: rootRoute.addChildren([practiceRoute, sessionRoute, speakingRoute]),
    history: createMemoryHistory({ initialEntries: [initialEntry] }),
  });

  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  qc.setQueryData(['auth', 'me'], {
    userId: 1,
    email: 'learner@demo.local',
    fullName: 'Demo Learner',
    role: 'learner',
    emailVerified: true,
  });
  qc.setQueryData(['learner', 'context', 1], {
    active: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
    certificates: [
      { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
      { code: 'IELTS', name: 'IELTS Academic' },
    ],
    creditBalance: 120,
  });

  render(
    <QueryClientProvider client={qc}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  );

  return { router };
}

describe('PracticeBuilderPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn() as unknown as typeof window.scrollTo;
  });

  afterEach(() => {
    cleanup();
  });

  it('renders 0-credit standalone catalog, speaking link, and starts a section drill', async () => {
    const startedSession: PracticeSessionDto = {
      sessionId: 505,
      sectionId: 101,
      sectionCode: 'PART1',
      sectionName: 'Photographs',
      modality: 'listening',
      questionCount: 10,
      status: 'in_progress',
      correctCount: 0,
      questionSource: 'all',
      startedAt: '2026-11-13T10:00:00Z',
    };

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith('/api/learner/practice/catalog')) {
        return new Response(JSON.stringify(makeCatalog()), { status: 200 });
      }
      if (url.endsWith('/api/learner/practice/matrix')) {
        return new Response(JSON.stringify(makeMatrix()), { status: 200 });
      }
      if (url.includes('/api/learner/practice/free-drill')) {
        return new Response(JSON.stringify(makeFreeDrill()), { status: 200 });
      }
      if (url.endsWith('/api/learner/practice/sessions') && init?.method === 'POST') {
        expect(JSON.parse(String(init.body))).toEqual({ sectionId: 101 });
        return new Response(JSON.stringify(startedSession), { status: 200 });
      }
      return new Response('{}', { status: 404 });
    });
    vi.stubGlobal('fetch', fetchMock);

    await renderPracticeBuilder('/practice');

    await waitFor(() => {
      expect(screen.getByText('Photographs')).toBeTruthy();
    });

    expect(screen.getAllByText(/0 credits/i).length).toBeGreaterThan(0);
    expect(screen.getByText('Incomplete Sentences')).toBeTruthy();
    expect(screen.getByText('Verb Tenses')).toBeTruthy();
    expect(screen.getByText('Prioritised')).toBeTruthy();
    expect(screen.getByText('Read a Text Aloud')).toBeTruthy();

    // Speaking sections must navigate to /speaking, never call startSession
    const speakingLink = screen.getByRole('link', { name: /speaking practice/i });
    expect(speakingLink.getAttribute('href')).toContain('/speaking');

    const startButtons = screen.getAllByRole('button', { name: /start section drill/i });
    fireEvent.click(startButtons[0]);

    await waitFor(() => {
      expect(screen.getByTestId('session-route').textContent).toContain('Session #505');
    });
  });

  it('keeps section drills available even when matrix and free-drill endpoints fail', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.endsWith('/api/learner/practice/catalog')) {
          return new Response(JSON.stringify(makeCatalog()), { status: 200 });
        }
        if (url.endsWith('/api/learner/practice/matrix')) {
          return new Response(JSON.stringify({ title: 'Matrix unavailable' }), { status: 500 });
        }
        if (url.includes('/api/learner/practice/free-drill')) {
          return new Response(JSON.stringify({ title: 'Free drill state unavailable' }), { status: 500 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderPracticeBuilder('/practice');

    await waitFor(() => {
      expect(screen.getByText('Photographs')).toBeTruthy();
    });
    expect(screen.getByText(/topic matrix is currently unavailable/i)).toBeTruthy();
  });

  it('supports ?stepId= learning path launch without charging credits', async () => {
    const step: PathStepDto = {
      stepId: 42,
      weekNumber: 2,
      weekSequence: 1,
      stepType: 'practice',
      route: 'practice',
      title: 'Business Reading Comprehension',
      status: 'ready',
      skillCode: 'READ',
      skillName: 'Reading',
      modality: 'reading',
      topicCode: 'READ_EMAIL',
      topicName: 'Business Emails',
      targetCefr: 'B1',
      sizingTier: 'standard',
      plannedItemCount: 10,
      estimatedMinutes: 12,
      score: null,
      actualAccuracyPct: null,
      targetAccuracyPct: 75,
      canStart: true,
      startedAt: null,
      completedAt: null,
      refLessonId: null,
      refExamId: null,
    };

    const startResult: StartStepResultDto = {
      stepId: 42,
      stepType: 'practice',
      route: 'practice',
      redirectUrl: '/Practice/Take/888',
      sessionId: 888,
      attemptId: null,
      substitutedFromTopicId: null,
    };

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.endsWith('/api/learner/learning-path/steps/42')) {
          return new Response(JSON.stringify(step), { status: 200 });
        }
        if (url.endsWith('/api/learner/learning-path/steps/42/start') && init?.method === 'POST') {
          return new Response(JSON.stringify(startResult), { status: 200 });
        }
        if (url.endsWith('/api/learner/practice/catalog')) {
          return new Response(JSON.stringify(makeCatalog()), { status: 200 });
        }
        if (url.endsWith('/api/learner/practice/matrix')) {
          return new Response(JSON.stringify([]), { status: 200 });
        }
        if (url.includes('/api/learner/practice/free-drill')) {
          return new Response(JSON.stringify(makeFreeDrill()), { status: 200 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderPracticeBuilder('/practice?stepId=42');

    await waitFor(() => {
      expect(screen.getByText('Business Reading Comprehension')).toBeTruthy();
    });
    expect(screen.getByText(/Week 2/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /launch step drill/i }));

    await waitFor(() => {
      expect(screen.getByTestId('session-route').textContent).toContain('Session #888');
    });
  });

  it('shows unfinished session resume banner and free-drill lock incomplete steps', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.endsWith('/api/learner/practice/catalog')) {
          return new Response(
            JSON.stringify(
              makeCatalog({
                unfinishedSession: {
                  sessionId: 333,
                  sectionId: 102,
                  sectionCode: 'PART5',
                  sectionName: 'Incomplete Sentences',
                  modality: 'reading',
                  questionCount: 10,
                  status: 'in_progress',
                  correctCount: 4,
                  questionSource: 'all',
                  startedAt: '2026-11-13T09:00:00Z',
                },
              }),
            ),
            { status: 200 },
          );
        }
        if (url.endsWith('/api/learner/practice/matrix')) {
          return new Response(JSON.stringify(makeMatrix()), { status: 200 });
        }
        if (url.includes('/api/learner/practice/free-drill')) {
          return new Response(
            JSON.stringify(
              makeFreeDrill({
                isUnlocked: false,
                remainingMinutes: 25,
                lockNotice: 'Finish this week’s planned steps first.',
                incompleteSteps: [
                  {
                    stepId: 9,
                    weekNumber: 1,
                    title: 'Core Grammar Review',
                    topicCode: 'GRAM_TENSE',
                    estimatedMinutes: 25,
                  },
                ],
              }),
            ),
            { status: 200 },
          );
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderPracticeBuilder('/practice');

    await waitFor(() => {
      expect(screen.getByText(/Core Grammar Review/i)).toBeTruthy();
    });
    expect(screen.queryByText(/unfinished practice session/i)).toBeNull();
    expect(screen.getByText(/25 min/i)).toBeTruthy();
  });

  it('displays unlocked sections with accuracy vs locked sections with link to Learning Path', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.endsWith('/api/learner/practice/catalog')) {
          return new Response(
            JSON.stringify(
              makeCatalog({
                listeningSections: [
                  {
                    sectionId: 101,
                    skillCode: 'LIST',
                    skillName: 'Listening',
                    modality: 'listening',
                    code: 'PART1',
                    name: 'Photographs',
                    optionCount: 4,
                    expectedQuestionCount: 10,
                    availableQuestionCount: 25,
                    accuracyPercentage: 80,
                    answeredCount: 20,
                    displayOrder: 1,
                    totalCategoryCount: 4,
                    categoriesInCooldown: 0,
                    nextReviewDueAt: null,
                    isUnlocked: true,
                  },
                ],
                readingSections: [
                  {
                    sectionId: 102,
                    skillCode: 'READ',
                    skillName: 'Reading',
                    modality: 'reading',
                    code: 'PART5',
                    name: 'Incomplete Sentences',
                    optionCount: 4,
                    expectedQuestionCount: 10,
                    availableQuestionCount: 40,
                    accuracyPercentage: null,
                    answeredCount: 0,
                    displayOrder: 5,
                    totalCategoryCount: 6,
                    categoriesInCooldown: 0,
                    nextReviewDueAt: null,
                    isUnlocked: false,
                  },
                ],
              }),
            ),
            { status: 200 },
          );
        }
        if (url.endsWith('/api/learner/practice/matrix')) {
          return new Response(JSON.stringify([]), { status: 200 });
        }
        if (url.includes('/api/learner/practice/free-drill')) {
          return new Response(JSON.stringify(makeFreeDrill()), { status: 200 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderPracticeBuilder('/practice');

    await waitFor(() => {
      expect(screen.getByText('Photographs')).toBeTruthy();
    });
    expect(screen.getByText('80%')).toBeTruthy();
    expect(screen.getByRole('button', { name: /start section drill/i })).toBeTruthy();
    expect(screen.getByText(/locked · complete in learning path/i)).toBeTruthy();
    expect(screen.getByRole('link', { name: /unlock in learning path/i }).getAttribute('href')).toContain(
      '/learning-path',
    );
  });
});
