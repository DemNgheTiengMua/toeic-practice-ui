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
import type { GradedAnswerDto, PracticeSessionTakeDto, SaveQuestionResultDto } from '@/lib/api';
import { PracticeSessionPage } from '../PracticeSessionPage';
import { readSavedNote } from '../practiceUtils';

function makeTakePayload(overrides: Partial<PracticeSessionTakeDto> = {}): PracticeSessionTakeDto {
  const q1 = {
    sessionId: 701,
    sectionName: 'Grammar Pool',
    questionId: null,
    itemVersionId: 9001,
    displayOrder: 1,
    totalQuestions: 2,
    stem: 'The manager asked the team to ____ the report before noon.',
    categoryName: 'Business Verbs',
    hint: 'Think about a verb meaning to complete or hand in.',
    hintUsed: false,
    options: [
      { optionId: 11, label: 'A', text: 'submit' },
      { optionId: 12, label: 'B', text: 'submitting' },
    ],
    selectedOptionId: null,
    isCorrect: null,
    correctOptionId: null,
    explanation: null,
    transcript: null,
    passage: 'Internal memo regarding quarterly deliverables.',
    audioPath: '/media/audio/q1.mp3',
    imagePath: '/media/images/desk.png',
    imageAlt: 'Office desk',
    isSaved: false,
    audioStartMs: 1000,
    audioEndMs: 12000,
    questionType: 'mcq',
    maxSelections: null,
    allowNumber: true,
    acceptedAnswersJson: null,
    requiredKeywords: null,
    minWords: null,
    maxWords: null,
    responseText: null,
    selectedOptionIds: null,
    selectedOptionIdsJson: null,
    correctOptionIds: null,
    savedQuestionId: null,
    cefrLevelCode: null,
    address: 9001,
  };

  const q2 = {
    sessionId: 701,
    sectionName: 'Grammar Pool',
    questionId: null,
    itemVersionId: 9002,
    displayOrder: 2,
    totalQuestions: 2,
    stem: 'Select two valid greetings.',
    categoryName: 'Pragmatics',
    hint: null,
    hintUsed: false,
    options: [
      { optionId: 21, label: 'A', text: 'Good morning' },
      { optionId: 22, label: 'B', text: 'Regards' },
      { optionId: 23, label: 'C', text: 'Yesterday' },
    ],
    selectedOptionId: null,
    isCorrect: null,
    correctOptionId: null,
    explanation: null,
    transcript: null,
    passage: null,
    audioPath: null,
    imagePath: null,
    imageAlt: null,
    isSaved: false,
    audioStartMs: null,
    audioEndMs: null,
    questionType: 'multi_select',
    maxSelections: 2,
    allowNumber: true,
    acceptedAnswersJson: null,
    requiredKeywords: null,
    minWords: null,
    maxWords: null,
    responseText: null,
    selectedOptionIds: null,
    selectedOptionIdsJson: null,
    correctOptionIds: null,
    savedQuestionId: null,
    cefrLevelCode: null,
    address: 9002,
  };

  return {
    session: {
      sessionId: 701,
      sectionId: null,
      sectionCode: 'GRAM_TENSE',
      sectionName: 'Grammar Pool',
      modality: 'reading',
      questionCount: 2,
      status: 'in_progress',
      correctCount: 0,
      questionSource: 'all',
      startedAt: '2026-11-13T10:00:00Z',
      topicId: 10,
      cefrLevelCode: 'B1',
      isShort: true,
    },
    question: q1,
    questions: [q1, q2],
    navigator: [
      {
        questionId: null,
        itemVersionId: 9001,
        displayOrder: 1,
        isAnswered: false,
        isCorrect: null,
        isSaved: false,
        isCurrent: true,
        savedQuestionId: null,
        address: 9001,
      },
      {
        questionId: null,
        itemVersionId: 9002,
        displayOrder: 2,
        isAnswered: false,
        isCorrect: null,
        isSaved: false,
        isCurrent: false,
        savedQuestionId: null,
        address: 9002,
      },
    ],
    ...overrides,
  };
}

async function renderSession(initialEntry = '/practice/session/701') {
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const sessionRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice/session/$sessionId',
    component: PracticeSessionPage,
  });
  const resultRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice/result/$sessionId',
    component: function ResultStub() {
      const { sessionId } = resultRoute.useParams();
      return <div data-testid="result-route">Result #{sessionId}</div>;
    },
  });
  const practiceRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice',
    component: () => <div>Practice hub</div>,
  });

  const router = createRouter({
    routeTree: rootRoute.addChildren([sessionRoute, resultRoute, practiceRoute]),
    history: createMemoryHistory({ initialEntries: [initialEntry] }),
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

  return { router };
}

describe('PracticeSessionPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.localStorage.clear();
    window.scrollTo = vi.fn() as unknown as typeof window.scrollTo;
    if (typeof globalThis.CSS === 'undefined') {
      (globalThis as unknown as { CSS: { escape: (v: string) => string } }).CSS = {
        escape: (v: string) => v,
      };
    } else if (!globalThis.CSS.escape) {
      globalThis.CSS.escape = (v: string) => v;
    }
  });

  afterEach(() => {
    cleanup();
  });

  it('renders media via toBrowserMediaUrl, short drill notice, instant MCQ grading, and reveals CEFR level only after lock', async () => {
    const graded: GradedAnswerDto = {
      isCorrect: true,
      selectedOptionId: 11,
      correctOptionId: 11,
      explanation: 'After "to", use the base infinitive verb "submit".',
      transcript: 'Audio transcript line 1.',
      isSessionFinished: false,
      nextQuestionDisplayOrder: 2,
      isSaved: false,
      cefrLevelCode: 'B1',
    };

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.includes('/api/learner/practice/sessions/701/take')) {
          return new Response(JSON.stringify(makeTakePayload()), { status: 200 });
        }
        if (
          url.endsWith('/api/learner/practice/sessions/701/questions/9001/answer') &&
          init?.method === 'POST'
        ) {
          expect(JSON.parse(String(init.body)).selectedOptionId).toBe(11);
          return new Response(JSON.stringify(graded), { status: 200 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderSession();

    await waitFor(() => {
      expect(screen.getByText(/The manager asked the team/i)).toBeTruthy();
    });

    // Short pool drill notice shown honestly
    expect(screen.getByText(/shorter drill \(2 items\)/i)).toBeTruthy();

    // Media URLs normalized via toBrowserMediaUrl
    const img = screen.getByAltText('Office desk') as HTMLImageElement;
    expect(img.getAttribute('src')).toBe('/media/images/desk.png');

    // CEFR item level MUST NOT appear before lock
    expect(screen.queryByText(/Item level/i)).toBeNull();

    fireEvent.click(screen.getByText('submit'));
    fireEvent.click(screen.getByRole('button', { name: /check answer/i }));

    await waitFor(() => {
      expect(screen.getByText(/Correct/i)).toBeTruthy();
    });

    expect(screen.getByText(/After "to", use the base infinitive verb "submit"./i)).toBeTruthy();
    expect(screen.getByText(/Item level · B1/i)).toBeTruthy();
  });

  it('supports AI hint popover, item-version bookmarking, personal note saving, and hides hint when unavailable', async () => {
    const saveResult: SaveQuestionResultDto = {
      isSaved: true,
      source: 'manual',
      savedAt: '2026-11-13T10:05:00Z',
      savedQuestionId: 404,
    };

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.includes('/api/learner/practice/sessions/701/take')) {
          return new Response(JSON.stringify(makeTakePayload()), { status: 200 });
        }
        if (
          url.endsWith('/api/learner/practice/sessions/701/questions/9001/hint') &&
          init?.method === 'POST'
        ) {
          return new Response(
            JSON.stringify({
              hint: 'Think about a verb meaning to complete or hand in.',
              hintUsed: true,
            }),
            { status: 200 },
          );
        }
        if (url.endsWith('/api/learner/saved-questions/item-versions/9001') && init?.method === 'POST') {
          return new Response(JSON.stringify(saveResult), { status: 200 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderSession();

    await waitFor(() => {
      expect(screen.getByText(/The manager asked the team/i)).toBeTruthy();
    });

    // Hint button visible on Q1
    fireEvent.click(screen.getByRole('button', { name: /hint/i }));
    await waitFor(() => {
      expect(screen.getByText(/Think about a verb meaning to complete/i)).toBeTruthy();
    });

    // Bookmark toggle calls /item-versions/9001
    fireEvent.click(screen.getByRole('button', { name: /save question/i }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /saved/i })).toBeTruthy();
    });

    // Personal note saved locally
    fireEvent.click(screen.getByRole('button', { name: /personal note/i }));
    const noteBox = await screen.findByPlaceholderText(/add a personal reminder/i);
    fireEvent.change(noteBox, { target: { value: 'Infinitive after ask + object + to' } });
    fireEvent.click(screen.getByRole('button', { name: /save note/i }));

    expect(readSavedNote({ savedQuestionId: 404, itemVersionId: 9001 })).toBe(
      'Infinitive after ask + object + to',
    );

    // Move to Q2 (hint == null -> hint trigger hidden)
    fireEvent.click(screen.getByRole('button', { name: /question 2/i }));
    await waitFor(() => {
      expect(screen.getByText('Select two valid greetings.')).toBeTruthy();
    });
    expect(screen.queryByRole('button', { name: /hint/i })).toBeNull();
  });

  it('submits multi_select answers and navigates to /practice/result/701 when finished', async () => {
    const finishReply: GradedAnswerDto = {
      isCorrect: true,
      selectedOptionId: null,
      correctOptionId: null,
      correctOptionIds: [21, 22],
      selectedOptionIdsJson: '[21,22]',
      explanation: 'Both Good morning and Regards are valid greetings/closings.',
      transcript: null,
      isSessionFinished: true,
      nextQuestionDisplayOrder: null,
      isSaved: false,
    };

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.includes('/api/learner/practice/sessions/701/take')) {
          return new Response(JSON.stringify(makeTakePayload()), { status: 200 });
        }
        if (
          url.endsWith('/api/learner/practice/sessions/701/questions/9002/answer') &&
          init?.method === 'POST'
        ) {
          const payload = JSON.parse(String(init.body));
          expect(payload.selectedOptionIds).toEqual([21, 22]);
          return new Response(JSON.stringify(finishReply), { status: 200 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderSession('/practice/session/701?q=2');

    await waitFor(() => {
      expect(screen.getByText('Select two valid greetings.')).toBeTruthy();
    });

    const checkboxes = screen.getAllByRole('checkbox');
    fireEvent.click(checkboxes[0]);
    fireEvent.click(checkboxes[1]);
    fireEvent.click(screen.getByRole('button', { name: /check answer/i }));

    await waitFor(() => {
      expect(screen.getByText(/session complete/i)).toBeTruthy();
    });

    fireEvent.click(screen.getAllByRole('link', { name: /view results/i })[0]);
    await waitFor(() => {
      expect(screen.getByTestId('result-route').textContent).toContain('Result #701');
    });
  });
});
