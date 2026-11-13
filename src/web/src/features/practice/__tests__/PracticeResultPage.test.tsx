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
import type { AiInsightDto, PracticeSummaryDto } from '@/lib/api';
import { PracticeResultPage } from '../PracticeResultPage';

function makeSummary(): PracticeSummaryDto {
  return {
    sessionId: 701,
    sectionCode: 'PART5',
    sectionName: 'Incomplete Sentences',
    modality: 'reading',
    startedAt: '2026-11-13T10:00:00Z',
    finishedAt: '2026-11-13T10:06:00Z',
    totalQuestions: 3,
    answeredCount: 2,
    correctCount: 1,
    incorrectCount: 1,
    skippedCount: 1,
    accuracyPercentage: 50,
    hintUsedCount: 1,
    correctWithHintCount: 1,
    incorrectWithHintCount: 0,
    questions: [
      {
        displayOrder: 1,
        questionId: 10,
        categoryName: 'Verb Tenses',
        stem: 'She ____ the report yesterday.',
        selectedOptionLabel: 'A',
        selectedOptionText: 'submitted',
        correctOptionLabel: 'A',
        correctOptionText: 'submitted',
        isCorrect: true,
        hintUsed: true,
        explanation: 'Yesterday requires simple past tense.',
        audioPath: null,
        transcript: null,
        questionType: 'mcq',
      },
      {
        displayOrder: 2,
        questionId: null,
        itemVersionId: 9002,
        categoryName: 'Prepositions',
        stem: 'Meet us ____ 3 PM.',
        selectedOptionLabel: 'B',
        selectedOptionText: 'on',
        correctOptionLabel: 'A',
        correctOptionText: 'at',
        isCorrect: false,
        hintUsed: false,
        explanation: 'Clock times take "at".',
        audioPath: '/media/audio/q2.mp3',
        audioStartMs: 0,
        audioEndMs: 5000,
        transcript: 'Meet us at three PM sharp.',
        questionType: 'mcq',
      },
      {
        displayOrder: 3,
        questionId: 12,
        categoryName: 'Adverbs',
        stem: 'The system ran ____.',
        selectedOptionLabel: null,
        selectedOptionText: null,
        correctOptionLabel: 'C',
        correctOptionText: 'smoothly',
        isCorrect: null,
        hintUsed: false,
        explanation: 'Adverb modifies the verb ran.',
        questionType: 'mcq',
      },
    ],
  };
}

function makeInsight(overrides: Partial<AiInsightDto> = {}): AiInsightDto {
  return {
    insightId: 1,
    userId: 1,
    certId: 1,
    scope: 'practice_session',
    practiceSessionId: 701,
    attemptId: null,
    narrative: 'Strong control of past tense verbs; review time prepositions.',
    trend: 'improving',
    status: 'done',
    failureReason: null,
    model: 'claude-sonnet',
    promptTokens: 100,
    completionTokens: 40,
    sourceFingerprint: 'fp1',
    createdAt: '2026-11-13T10:06:00Z',
    completedAt: '2026-11-13T10:06:05Z',
    recommendations: [
      {
        recId: 5,
        insightId: 1,
        practiceSessionId: 701,
        attemptId: null,
        text: 'Drill prepositions of time at B1.',
        severity: 'medium',
        targetBandCode: 'B1',
        skillName: 'Reading',
        isResolved: false,
        createdAt: '2026-11-13T10:06:05Z',
      },
    ],
    ...overrides,
  };
}

async function renderResultPage() {
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const resultRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice/result/$sessionId',
    component: PracticeResultPage,
  });
  const practiceRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice',
    component: () => <div>Practice hub</div>,
  });
  const savedRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/saved-questions',
    component: () => <div>Saved questions</div>,
  });

  const router = createRouter({
    routeTree: rootRoute.addChildren([resultRoute, practiceRoute, savedRoute]),
    history: createMemoryHistory({ initialEntries: ['/practice/result/701'] }),
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  qc.setQueryData(['auth', 'me'], {
    userId: 1,
    email: 'learner@demo.local',
    fullName: 'Demo Learner',
    role: 'learner',
    emailVerified: true,
  });

  render(
    <QueryClientProvider client={qc}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe('PracticeResultPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn() as unknown as typeof window.scrollTo;
  });

  afterEach(() => {
    cleanup();
  });

  it('renders summary stats, AI insight narrative, and outcome review rows with explanations', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.endsWith('/api/learner/practice/sessions/701/summary')) {
          return new Response(JSON.stringify(makeSummary()), { status: 200 });
        }
        if (url.endsWith('/api/learner/insights/practice/701')) {
          return new Response(JSON.stringify(makeInsight()), { status: 200 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderResultPage();

    await waitFor(() => {
      expect(screen.getByText('Incomplete Sentences')).toBeTruthy();
    });

    expect(screen.getByText('50%')).toBeTruthy();
    expect(screen.getByText(/Strong control of past tense verbs/i)).toBeTruthy();
    expect(screen.getByText(/Drill prepositions of time at B1./i)).toBeTruthy();
    expect(screen.getByText(/Yesterday requires simple past tense./i)).toBeTruthy();
    expect(screen.getByText(/Clock times take "at"./i)).toBeTruthy();
  });

  it('supports retrying a failed AI insight', async () => {
    let retried = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.endsWith('/api/learner/practice/sessions/701/summary')) {
          return new Response(JSON.stringify(makeSummary()), { status: 200 });
        }
        if (url.endsWith('/api/learner/insights/practice/701/retry') && init?.method === 'POST') {
          retried = true;
          return new Response(null, { status: 204 });
        }
        if (url.endsWith('/api/learner/insights/practice/701')) {
          return new Response(
            JSON.stringify(
              retried
                ? makeInsight({ status: 'done', narrative: 'Updated narrative after retry.' })
                : makeInsight({
                    status: 'failed',
                    narrative: null,
                    failureReason: 'Provider timeout',
                    recommendations: [],
                  }),
            ),
            { status: 200 },
          );
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderResultPage();

    await waitFor(() => {
      expect(screen.getByText(/Provider timeout/i)).toBeTruthy();
    });

    fireEvent.click(screen.getByRole('button', { name: /retry insight/i }));

    await waitFor(() => {
      expect(screen.getByText(/Updated narrative after retry./i)).toBeTruthy();
    });
  });
});
