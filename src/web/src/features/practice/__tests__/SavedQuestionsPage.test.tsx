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
import type { SavedQuestionItemDto, SavedQuestionsViewDto } from '@/lib/api';
import { SavedQuestionsPage } from '../SavedQuestionsPage';
import { writeSavedNote } from '../practiceUtils';

function makeSavedItem(
  idx: number,
  overrides: Partial<SavedQuestionItemDto> = {},
): SavedQuestionItemDto {
  return {
    savedQuestionId: 1000 + idx,
    questionId: idx % 2 === 0 ? 500 + idx : null,
    itemId: idx % 2 === 1 ? 800 + idx : null,
    itemVersionId: idx % 2 === 1 ? 900 + idx : null,
    isRetiredDefective: false,
    source: idx % 2 === 0 ? 'manual' : 'incorrect',
    savedAt: '2026-11-13T10:00:00Z',
    sectionId: 101,
    sectionCode: 'PART5',
    sectionName: 'Incomplete Sentences',
    topicCode: 'GRAM_TENSE',
    topicName: 'Verb Tenses',
    categoryId: 20,
    categoryName: 'Grammar',
    stem: `Saved question stem #${idx}`,
    passage: null,
    imagePath: null,
    audioPath: null,
    hint: 'Check verb agreement.',
    explanation: `Explanation for saved question #${idx}`,
    transcript: null,
    options: [
      { optionId: 1, label: 'A', text: 'Right option', isCorrect: true },
      { optionId: 2, label: 'B', text: 'Wrong option', isCorrect: false },
    ],
    scopeCode: 'PART5',
    scopeName: 'Incomplete Sentences',
    ...overrides,
  };
}

function makeView(items: SavedQuestionItemDto[]): SavedQuestionsViewDto {
  return {
    certificate: { code: 'TOEIC', name: 'TOEIC Listening & Reading', sectionNoun: 'Part' },
    totalSavedCount: items.length,
    manualSavedCount: items.filter((i) => i.source === 'manual').length,
    incorrectSavedCount: items.filter((i) => i.source === 'incorrect').length,
    questions: items,
    sections: [{ sectionId: 101, code: 'PART5', name: 'Incomplete Sentences', count: items.length }],
    categories: [{ categoryId: 20, name: 'Grammar', count: items.length }],
  };
}

async function renderSavedBank(initialEntry = '/saved-questions') {
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const savedRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/saved-questions',
    component: SavedQuestionsPage,
  });
  const detailRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/saved-questions/$savedQuestionId',
    component: function DetailStub() {
      const { savedQuestionId } = detailRoute.useParams();
      return <div data-testid="saved-detail-route">Detail #{savedQuestionId}</div>;
    },
  });
  const practiceRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice',
    component: () => <div>Practice hub</div>,
  });

  const router = createRouter({
    routeTree: rootRoute.addChildren([savedRoute, detailRoute, practiceRoute]),
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
}

describe('SavedQuestionsPage', () => {
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

  it('paginates saved questions, renders inline explanations, personal notes, and defective warning', async () => {
    const items = Array.from({ length: 12 }, (_, idx) =>
      makeSavedItem(idx + 1, idx === 0 ? { isRetiredDefective: true } : {}),
    );
    writeSavedNote({ savedQuestionId: 1001 }, 'Watch out for defective key');

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes('/api/learner/saved-questions')) {
          return new Response(JSON.stringify(makeView(items)), { status: 200 });
        }
        return new Response('{}', { status: 404 });
      }),
    );

    await renderSavedBank();

    await waitFor(() => {
      expect(screen.getByText('Saved question stem #1')).toBeTruthy();
    });

    // Item 1 shows withdrawn as defective badge and alert
    expect(screen.getAllByText(/withdrawn as defective/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/Watch out for defective key/i)).toBeTruthy();

    // Item 11 is on page 2
    expect(screen.queryByText('Saved question stem #11')).toBeNull();

    // Expand explanation on item 1
    const showButtons = screen.getAllByRole('button', { name: /show explanation/i });
    fireEvent.click(showButtons[0]);
    expect(screen.getByText('Explanation for saved question #1')).toBeTruthy();

    // Go to page 2 via HeroUI Pagination
    fireEvent.click(screen.getByRole('button', { name: '2' }));
    await waitFor(() => {
      expect(screen.getByText('Saved question stem #11')).toBeTruthy();
    });
    expect(screen.queryByText('Saved question stem #1')).toBeNull();
  });

  it('filters by source and removes an item via DELETE /api/learner/saved-questions/{savedQuestionId}', async () => {
    let items = [
      makeSavedItem(1, { savedQuestionId: 2001, source: 'manual', stem: 'Manual item A' }),
      makeSavedItem(2, { savedQuestionId: 2002, source: 'incorrect', stem: 'Incorrect item B' }),
    ];

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith('/api/learner/saved-questions/2001') && init?.method === 'DELETE') {
        items = items.filter((i) => i.savedQuestionId !== 2001);
        return new Response(
          JSON.stringify({ isSaved: false, source: null, savedAt: null, savedQuestionId: 2001 }),
          { status: 200 },
        );
      }
      if (url.includes('/api/learner/saved-questions')) {
        const parsed = new URL(url, 'http://localhost');
        const src = parsed.searchParams.get('source');
        const filtered = src ? items.filter((i) => i.source === src) : items;
        return new Response(JSON.stringify(makeView(filtered)), { status: 200 });
      }
      return new Response('{}', { status: 404 });
    });
    vi.stubGlobal('fetch', fetchMock);

    await renderSavedBank();

    await waitFor(() => {
      expect(screen.getByText('Manual item A')).toBeTruthy();
    });

    // Click source filter chip/button for Incorrect only
    fireEvent.click(screen.getByRole('button', { name: /incorrect only/i }));
    await waitFor(() => {
      expect(screen.queryByText('Manual item A')).toBeNull();
      expect(screen.getByText('Incorrect item B')).toBeTruthy();
    });

    // Switch back to All and remove Manual item A
    fireEvent.click(screen.getByRole('button', { name: /all saved/i }));
    await waitFor(() => {
      expect(screen.getByText('Manual item A')).toBeTruthy();
    });

    const removeButtons = screen.getAllByRole('button', { name: /remove/i });
    fireEvent.click(removeButtons[0]);

    await waitFor(() => {
      expect(screen.queryByText('Manual item A')).toBeNull();
    });
    expect(screen.getByText('Incorrect item B')).toBeTruthy();
  });
});
