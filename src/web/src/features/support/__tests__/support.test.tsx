import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type {
  LearnerComplaintDto,
  EligibleSubjectsDto,
} from '../../../lib/api';

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

  return { ...view, router, history, queryClient };
}

const MOCK_USER = {
  userId: 42,
  email: 'learner@demo.local',
  fullName: 'Learner Demo',
  role: 'Learner',
  isActive: true,
  emailVerified: true,
  createdAt: '2026-01-01T00:00:00Z',
  permissions: [],
};

const MOCK_ELIGIBLE_SUBJECTS: EligibleSubjectsDto = {
  bookings: [
    {
      bookingId: 901,
      mentorName: 'Sarah Jenkins',
      scheduledAt: '2026-11-20T14:00:00Z',
      status: 'confirmed',
    },
  ],
  attempts: [
    {
      attemptId: 301,
      examTitle: 'TOEIC Full Practice Test 1',
      startedAt: '2026-11-10T09:00:00Z',
      status: 'graded',
      totalScore: 780,
    },
  ],
  orders: [
    {
      orderId: 101,
      orderCode: 'ORD-101',
      packageName: 'Starter Pack',
      amount: 250000,
      createdAt: '2026-10-10T10:00:00Z',
      status: 'paid',
    },
  ],
};

const MOCK_COMPLAINTS: (LearnerComplaintDto & { internalNotes?: string })[] = [
  {
    complaintId: 1,
    type: 'mentor_no_show',
    reason: 'Mentor did not join the scheduled Google Meet room.',
    status: 'resolved',
    resolution: 'Verified meeting logs. Granted 20 credits refund.',
    resolvedAt: '2026-11-12T15:00:00Z',
    grantedCredits: 20,
    createdAt: '2026-11-12T11:00:00Z',
    bookingId: 901,
    subjectSummary: 'Booking #901 (Sarah Jenkins)',
    // Invariant check: internal administrative notes must NEVER be shown to learner
    internalNotes: 'SECRET_STAFF_NOTE: Instructor was delayed due to power outage.',
  },
  {
    complaintId: 2,
    type: 'grading_issue',
    reason: 'Speaking task 2 audio grading failed to return score.',
    status: 'open',
    createdAt: '2026-11-13T08:00:00Z',
    attemptId: 301,
    subjectSummary: 'Attempt #301 (TOEIC Full Practice Test 1)',
  },
];

describe('Support & complaints feature (#301)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as any;
    if (!Element.prototype.getAnimations) {
      Element.prototype.getAnimations = () => [];
    }
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('renders support workspace with complaints history and human-readable status labels', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/complaints') return jsonResponse(MOCK_COMPLAINTS);
      if (url === '/api/learner/complaints/eligible-subjects')
        return jsonResponse(MOCK_ELIGIBLE_SUBJECTS);
      return jsonResponse({}, 404);
    });

    renderRoute('/support');

    expect(await screen.findByRole('heading', { name: /support & complaints/i })).toBeInTheDocument();

    // Check complaint 1 (resolved)
    expect(await screen.findByText('Mentor did not attend')).toBeInTheDocument();
    expect(screen.getByText('Resolved')).toBeInTheDocument();
    expect(screen.getByText(/granted 20 credits refund/i)).toBeInTheDocument();
    expect(screen.getByText('+20 credits refunded')).toBeInTheDocument();

    // Check complaint 2 (open)
    expect(screen.getByText('Grading issue')).toBeInTheDocument();
    expect(screen.getByText('Open')).toBeInTheDocument();

    // CRITICAL: NEVER leak internalNotes!
    expect(screen.queryByText(/SECRET_STAFF_NOTE/i)).toBeNull();
  });

  it('filters eligible subjects based on category selection', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/complaints') return jsonResponse(MOCK_COMPLAINTS);
      if (url === '/api/learner/complaints/eligible-subjects')
        return jsonResponse(MOCK_ELIGIBLE_SUBJECTS);
      return jsonResponse({}, 404);
    });

    renderRoute('/support');

    // Switch to new report tab
    const newReportTab = await screen.findByRole('tab', { name: /file a report|new ticket/i });
    fireEvent.click(newReportTab);

    // Default category: Mentor
    expect(await screen.findByText(/booking #901/i)).toBeInTheDocument();

    // Switch category to Exam
    const examCatBtn = screen.getByRole('button', { name: /exam & grading/i });
    fireEvent.click(examCatBtn);

    expect(await screen.findByText(/attempt #301/i)).toBeInTheDocument();

    // Switch category to Payment
    const paymentCatBtn = screen.getByRole('button', { name: /billing & payment/i });
    fireEvent.click(paymentCatBtn);

    expect(await screen.findByText(/ORD-101/i)).toBeInTheDocument();
  });

  it('validates reason input and submits complaint report (POST /api/learner/complaints)', async () => {
    let postBody: any = null;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/complaints') {
        if (init?.method === 'POST') {
          postBody = JSON.parse(String(init.body));
          const newComplaint: LearnerComplaintDto = {
            complaintId: 3,
            type: postBody.type,
            reason: postBody.reason,
            status: 'open',
            createdAt: '2026-11-13T12:00:00Z',
            bookingId: postBody.bookingId,
          };
          return jsonResponse(newComplaint);
        }
        return jsonResponse(MOCK_COMPLAINTS);
      }
      if (url === '/api/learner/complaints/eligible-subjects')
        return jsonResponse(MOCK_ELIGIBLE_SUBJECTS);
      return jsonResponse({}, 404);
    });

    renderRoute('/support');

    const newReportTab = await screen.findByRole('tab', { name: /file a report|new ticket/i });
    fireEvent.click(newReportTab);

    const reasonInput = await screen.findByLabelText(/description & reason/i);
    fireEvent.change(reasonInput, {
      target: { value: 'Mentor was 25 minutes late and audio was distorted.' },
    });

    const submitBtn = screen.getByRole('button', { name: /submit report/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(postBody).toEqual({
        type: 'mentor_no_show',
        reason: 'Mentor was 25 minutes late and audio was distorted.',
        bookingId: 901,
        attemptId: null,
        orderId: null,
      });
      expect(screen.getByText(/report submitted successfully/i)).toBeInTheDocument();
    });
  });
});
