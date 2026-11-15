import '../../sitting/__tests__/test-setup';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from '@tanstack/react-router';
import { api, type AttemptQuestionDto, type PlacementTakeViewDto } from '@/lib/api';
import { PlacementTakePage } from '../PlacementTakePage';

vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>();
  return {
    ...actual,
    api: {
      ...actual.api,
      placement: {
        ...actual.api.placement,
        take: vi.fn(),
        saveAnswer: vi.fn(),
        saveDraft: vi.fn(),
        submit: vi.fn(),
      },
    },
  };
});

function mcq(questionId: number, displayOrder: number): AttemptQuestionDto {
  return {
    questionId,
    displayOrder,
    totalQuestions: 2,
    sectionId: 1,
    sectionCode: 'REA-1',
    sectionName: 'Reading Comprehension',
    skillCode: 'reading',
    skillName: 'Reading',
    modality: 'reading',
    stem: displayOrder === 1 ? 'Which city hosted the symposium?' : 'Which year was the treaty signed?',
    options: [
      { optionId: questionId * 10 + 1, label: 'A', text: 'Option A' },
      { optionId: questionId * 10 + 2, label: 'B', text: 'Option B' },
    ],
    selectedOptionId: null,
    isFlagged: false,
    questionType: 'mcq',
  };
}

/** Both questions live in one section, so the move is a pure state change inside the shell. */
function takeAt(order: number): PlacementTakeViewDto {
  const q1 = mcq(101, 1);
  const q2 = mcq(102, 2);
  return {
    placementId: 42,
    attemptId: 420,
    examId: 10,
    examName: 'IELTS Placement',
    certCode: 'IELTS',
    status: 'in_progress',
    startedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 1800_000).toISOString(),
    remainingSeconds: 1800,
    totalQuestions: 2,
    answeredCount: 0,
    flaggedCount: 0,
    currentQuestion: order === 2 ? q2 : q1,
    questions: [q1, q2],
    navigator: [q1, q2].map((q) => ({
      questionId: q.questionId,
      displayOrder: q.displayOrder,
      sectionId: 1,
      sectionCode: 'REA-1',
      modality: 'reading',
      isAnswered: false,
      isFlagged: false,
      isCurrent: q.displayOrder === order,
      isLocked: false,
    })),
    isAutoSubmitted: false,
  };
}

/** Real browser history, as in the app: the shell's URL sync reaches the router. */
function renderTake() {
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const takeRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/take/$attemptId',
    validateSearch: (search: Record<string, unknown>) => ({
      q: typeof search.q === 'number' || typeof search.q === 'string' ? Number(search.q) : undefined,
    }),
    component: PlacementTakePage,
  });
  const resultRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/result/$attemptId',
    component: () => <div>result</div>,
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([takeRoute, resultRoute]),
  });
  render(<RouterProvider router={router} />);
  return router;
}

describe('PlacementTakePage navigation (#368)', () => {
  beforeEach(() => {
    // A real sitting answers over the network: the delay is what turns a refetch into a visible
    // reload of the paper.
    vi.mocked(api.placement.take).mockImplementation(
      (_id, q) => new Promise((resolve) => setTimeout(() => resolve(takeAt(q ?? 1)), 40)),
    );
    vi.mocked(api.placement.saveAnswer).mockResolvedValue({
      questionId: 101,
      selectedOptionId: 1011,
      isFlagged: false,
      remainingSeconds: 1790,
      isAutoSubmitted: false,
    });
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
    window.history.replaceState({}, '', '/');
  });

  it('moves within a section without reloading the sitting and keeps ?q= on the shown question', async () => {
    window.history.replaceState({}, '', '/placement/take/42');
    const router = renderTake();

    await screen.findByText('Which city hosted the symposium?');
    const header = document.querySelector('header');
    expect(header).not.toBeNull();
    expect(header).toHaveTextContent('Question 1 of 2');
    const takeCallsAtMount = vi.mocked(api.placement.take).mock.calls.length;

    fireEvent.click(screen.getByRole('button', { name: /next/i }));

    await screen.findByText('Which year was the treaty signed?');
    await waitFor(() => expect(header).toHaveTextContent('Question 2 of 2'));

    // The paper was never reloaded: the same shell stayed on screen, and no new sitting was fetched.
    expect(document.querySelector('header')).toBe(header);
    expect(screen.queryByText(/Loading placement sitting/i)).not.toBeInTheDocument();
    expect(vi.mocked(api.placement.take).mock.calls.length).toBe(takeCallsAtMount);

    // The URL reflects what is shown, so a manual reload returns here.
    expect(window.location.pathname + window.location.search).toBe('/placement/take/42?q=2');

    router.history.destroy();

    vi.mocked(api.placement.take).mockClear();
    const reloaded = renderTake();
    await screen.findByText('Which year was the treaty signed?');
    expect(api.placement.take).toHaveBeenCalledWith(42, 2);

    reloaded.history.destroy();
  });

  it('keeps a typed writing draft when moving away and back within the section', async () => {
    const writing = (questionId: number, displayOrder: number): AttemptQuestionDto => ({
      ...mcq(questionId, displayOrder),
      sectionCode: 'WRT-1',
      sectionName: 'Writing Task',
      skillCode: 'writing',
      skillName: 'Writing',
      modality: 'writing',
      stem: `Writing prompt ${displayOrder}`,
      options: [],
      questionType: 'free_response',
      responseText: '',
    });
    const makeTake = (order: number): PlacementTakeViewDto => {
      const base = takeAt(order);
      const q1 = writing(201, 1);
      const q2 = writing(202, 2);
      return {
        ...base,
        currentQuestion: order === 2 ? q2 : q1,
        questions: [q1, q2],
        navigator: [q1, q2].map((q) => ({
          questionId: q.questionId,
          displayOrder: q.displayOrder,
          sectionId: 1,
          sectionCode: 'WRT-1',
          modality: 'writing',
          isAnswered: false,
          isFlagged: false,
          isCurrent: q.displayOrder === order,
          isLocked: false,
        })),
      };
    };
    vi.mocked(api.placement.take).mockImplementation(async (_id, q) => makeTake(q ?? 1));
    vi.mocked(api.placement.saveDraft).mockResolvedValue({
      freeResponseId: 1,
      questionId: 201,
      savedAt: new Date().toISOString(),
      wordCount: 9,
    });
    window.history.replaceState({}, '', '/placement/take/42');

    const router = renderTake();
    await screen.findByText('Writing prompt 1');

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: 'The chart shows steady growth across the decade.' },
    });

    fireEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('Writing prompt 2');

    fireEvent.click(screen.getByRole('button', { name: /previous/i }));
    await screen.findByText('Writing prompt 1');

    expect(screen.getByRole('textbox')).toHaveValue(
      'The chart shows steady growth across the decade.',
    );

    router.history.destroy();
  });
});
