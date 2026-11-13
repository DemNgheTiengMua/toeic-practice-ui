import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../../context/AuthContext';
import { createAppRouter } from '../../../../router';
import { STAFF_PERMISSIONS } from '../../../../lib/security';
import type {
  AdminComplaintsSummaryDto,
  AdminComplaintDetailDto,
  AdminMentorListItemDto,
  AdminMentorSlotDto,
  MentorCertificateOptionDto,
  MentorSummaryDto,
  AuthUserDto,
} from '../../../../lib/api';

let activeUser: AuthUserDto | null = null;

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

const SAMPLE_COMPLAINTS_SUMMARY: AdminComplaintsSummaryDto = {
  complaints: [
    {
      complaintId: 301,
      userId: 15,
      learnerName: 'Đặng Thị E',
      learnerEmail: 'learner15@test.local',
      type: 'mentor_no_show',
      subjectType: 'mentor_booking',
      subjectId: 55,
      subjectSummary: 'Speaking 1-on-1 with Mentor John',
      status: 'open',
      createdAt: '2026-11-08T09:00:00Z',
    },
    {
      complaintId: 302,
      userId: 16,
      learnerName: 'Hoàng Văn F',
      learnerEmail: 'learner16@test.local',
      type: 'grading_issue',
      subjectType: 'writing_attempt',
      subjectId: 88,
      subjectSummary: 'TOEIC Writing Task 8 Essay',
      status: 'resolved',
      createdAt: '2026-11-07T10:00:00Z',
      resolvedAt: '2026-11-07T15:00:00Z',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 25,
  pendingCount: 1,
  resolvedCount: 1,
  oldestPendingCreatedAt: '2026-11-08T09:00:00Z',
  oldestPendingId: 301,
  oldestPendingType: 'mentor_no_show',
};

const SAMPLE_COMPLAINT_DETAIL_OPEN: AdminComplaintDetailDto = {
  complaintId: 301,
  userId: 15,
  learnerName: 'Đặng Thị E',
  learnerEmail: 'learner15@test.local',
  type: 'mentor_no_show',
  reason: 'I waited for 20 minutes in the call room but the mentor never showed up.',
  status: 'open',
  subjectType: 'mentor_booking',
  subjectId: 55,
  subjectSummary: 'Speaking 1-on-1 with Mentor John',
  subjectDetails: 'Booking ID #55 scheduled on 2026-11-08 08:30 UTC',
  createdAt: '2026-11-08T09:00:00Z',
};

const SAMPLE_COMPLAINT_DETAIL_RESOLVED: AdminComplaintDetailDto = {
  complaintId: 302,
  userId: 16,
  learnerName: 'Hoàng Văn F',
  learnerEmail: 'learner16@test.local',
  type: 'grading_issue',
  reason: 'The AI flagged grammatical errors that follow standard British English.',
  status: 'resolved',
  subjectType: 'writing_attempt',
  subjectId: 88,
  subjectSummary: 'TOEIC Writing Task 8 Essay',
  createdAt: '2026-11-07T10:00:00Z',
  resolution: 'Regraded manually by staff and verified accurate British spelling.',
  internalNotes: 'Prompt fine-tuning ticket opened for dialect tolerance.',
  resolvedByUserId: 1,
  resolvedByUserName: 'Lead Triage Staff',
  resolvedAt: '2026-11-07T15:00:00Z',
  resolvedByGrantTxnId: 4410,
  grantedCredits: 2,
};

const SAMPLE_MENTORS: AdminMentorListItemDto[] = [
  {
    mentorId: 10,
    userId: 25,
    displayName: 'Thầy John Smith',
    type: 'human',
    certId: 1,
    certCode: 'TOEIC',
    skillId: 3,
    skillName: 'Speaking',
    pricePerSlot: 250000,
    priceCredits: 2,
    sessionMinutes: 45,
    avgRating: 4.8,
    reviewCount: 32,
    totalSlotsCount: 12,
    openSlotsCount: 4,
    isActive: true,
  },
  {
    mentorId: 11,
    userId: null,
    displayName: 'AI Fluency Coach',
    type: 'ai',
    certId: 1,
    certCode: 'TOEIC',
    skillId: null,
    skillName: null,
    pricePerSlot: 0,
    priceCredits: 1,
    sessionMinutes: 30,
    avgRating: 4.6,
    reviewCount: 128,
    totalSlotsCount: 0,
    openSlotsCount: 0,
    isActive: true,
  },
];

const SAMPLE_MENTOR_OPTIONS: MentorCertificateOptionDto[] = [
  {
    certId: 1,
    code: 'TOEIC',
    name: 'TOEIC Comprehensive',
    skills: [
      { skillId: 1, code: 'LR_LIST', name: 'Listening', modality: 'listening' },
      { skillId: 2, code: 'LR_READ', name: 'Reading', modality: 'reading' },
      { skillId: 3, code: 'SW_SPK', name: 'Speaking', modality: 'speaking' },
      { skillId: 4, code: 'SW_WRT', name: 'Writing', modality: 'writing' },
    ],
  },
];

const SAMPLE_MENTOR_SLOTS: AdminMentorSlotDto[] = [
  {
    slotId: 801,
    mentorId: 10,
    startAt: '2026-11-15T10:00:00Z',
    endAt: '2026-11-15T10:45:00Z',
    status: 'open',
  },
  {
    slotId: 802,
    mentorId: 10,
    startAt: '2026-11-15T11:00:00Z',
    endAt: '2026-11-15T11:45:00Z',
    status: 'booked',
  },
];

function renderRoute(initialUrl = '/admin/operations', user: AuthUserDto | null = null) {
  activeUser = user;
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

describe('AdminOperationsPage (#304)', () => {
  let fetchMock: ReturnType<typeof installFetchMock>;

  beforeEach(() => {
    if (typeof window !== 'undefined') {
      window.scrollTo = vi.fn();
    }
    if (typeof Element !== 'undefined' && !Element.prototype.getAnimations) {
      Element.prototype.getAnimations = () => [];
    }
    (global as any).ResizeObserver = class ResizeObserver {
      observe() {}
      unobserve() {}
      disconnect() {}
    };

    fetchMock = installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        if (!activeUser) return jsonResponse({ code: 'AUTH_REQUIRED' }, 401);
        return jsonResponse(activeUser);
      }
      if (url === '/api/learner/context') {
        return jsonResponse({}, 404);
      }
      if (url.startsWith('/api/admin/complaints/301/status')) {
        const body = JSON.parse(init?.body as string);
        return jsonResponse({
          ...SAMPLE_COMPLAINT_DETAIL_OPEN,
          status: body.status,
        });
      }
      if (url.startsWith('/api/admin/complaints/301/resolve')) {
        const body = JSON.parse(init?.body as string);
        return jsonResponse({
          ...SAMPLE_COMPLAINT_DETAIL_OPEN,
          status: 'resolved',
          resolution: body.resolution,
          internalNotes: body.internalNotes,
          grantedCredits: body.grantCredits,
          resolvedAt: new Date().toISOString(),
        });
      }
      if (url.startsWith('/api/admin/complaints/301')) {
        return jsonResponse(SAMPLE_COMPLAINT_DETAIL_OPEN);
      }
      if (url.startsWith('/api/admin/complaints/302')) {
        return jsonResponse(SAMPLE_COMPLAINT_DETAIL_RESOLVED);
      }
      if (url.startsWith('/api/admin/complaints')) {
        return jsonResponse(SAMPLE_COMPLAINTS_SUMMARY);
      }
      if (url.startsWith('/api/admin/mentors/options')) {
        return jsonResponse(SAMPLE_MENTOR_OPTIONS);
      }
      if (url.startsWith('/api/admin/mentors/10/slots/801/close')) {
        return new Response(null, { status: 200 });
      }
      if (url.startsWith('/api/admin/mentors/10/slots') && init?.method === 'POST') {
        return jsonResponse({
          slotId: 803,
          mentorId: 10,
          startAt: '2026-11-16T14:00:00Z',
          endAt: '2026-11-16T14:45:00Z',
          status: 'open',
        });
      }
      if (url.startsWith('/api/admin/mentors/10/slots')) {
        return jsonResponse(SAMPLE_MENTOR_SLOTS);
      }
      if (url.startsWith('/api/admin/mentors') && init?.method === 'POST') {
        const body = JSON.parse(init?.body as string);
        return jsonResponse({
          mentorId: 12,
          ...body,
          certCode: 'TOEIC',
          certName: 'TOEIC Comprehensive',
          avgRating: null,
          reviewCount: 0,
        });
      }
      if (url.startsWith('/api/admin/mentors')) {
        return jsonResponse(SAMPLE_MENTORS);
      }
      return jsonResponse({ error: 'Not found' }, 404);
    });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('renders access denied card when user lacks support triage or admin capability', async () => {
    renderRoute('/admin/operations', {
      userId: 99,
      email: 'content@test.local',
      fullName: 'Content Author',
      role: 'staff',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.ContentAuthor],
    });

    expect(await screen.findByText(/access restricted/i)).toBeInTheDocument();
  });

  it('renders complaints triage workspace with status transitions and make-good credit grant validation', async () => {
    renderRoute('/admin/operations', {
      userId: 2,
      email: 'triage@test.local',
      fullName: 'Triage Specialist',
      role: 'staff',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SupportTriage],
    });

    // Page title and breadcrumbs
    expect(await screen.findByRole('heading', { name: /operations desk/i })).toBeInTheDocument();

    // Summary tiles
    expect(await screen.findByText('Đặng Thị E')).toBeInTheDocument();
    expect(screen.getByText('Hoàng Văn F')).toBeInTheDocument();

    // Sentence-case labels (never raw wire codes)
    expect(screen.getAllByText('Mentor did not attend').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Grading issue').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Open').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Resolved').length).toBeGreaterThan(0);

    // Open detail modal for complaint 301 (open)
    const viewButtons = screen.getAllByRole('button', { name: /view/i });
    fireEvent.click(viewButtons[0]);

    expect(await screen.findByText(/complaint details — #301/i)).toBeInTheDocument();
    expect(await screen.findByText(/waited for 20 minutes/i)).toBeInTheDocument();

    // 1. Status transition: Mark as under review
    const reviewBtn = await screen.findByRole('button', { name: /mark as under review/i });
    fireEvent.click(reviewBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/complaints/301/status'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ status: 'reviewing' }),
        }),
      );
    });

    // 2. Open resolution modal and test make-good credit grant bounds (0..20)
    const resolveBtn = screen.getByRole('button', { name: /resolve complaint/i });
    fireEvent.click(resolveBtn);

    expect(await screen.findByText(/resolve complaint #301/i)).toBeInTheDocument();

    // Try resolution with 25 credits (> 20 limit)
    fireEvent.change(screen.getByLabelText(/message for learner/i), {
      target: { value: 'Apologies for the mentor no-show.' },
    });
    fireEvent.change(screen.getByLabelText(/internal investigation notes/i), {
      target: { value: 'Mentor verified absent. Added strike to record.' },
    });
    fireEvent.change(screen.getByLabelText(/grant make-good credits/i), {
      target: { value: '25' },
    });

    const confirmResolveBtn = screen.getByRole('button', { name: /confirm resolution/i });
    fireEvent.click(confirmResolveBtn);

    expect(await screen.findByText(/grant credits must be between 0 and 20/i)).toBeInTheDocument();

    // Change to valid 2 credits and confirm
    fireEvent.change(screen.getByLabelText(/grant make-good credits/i), {
      target: { value: '2' },
    });
    fireEvent.click(confirmResolveBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/complaints/301/resolve'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({
            resolution: 'Apologies for the mentor no-show.',
            internalNotes: 'Mentor verified absent. Added strike to record.',
            grantCredits: 2,
          }),
        }),
      );
    });
  });

  it('manages mentors and scheduling slots: filters skills by cert, validates human user ID, and schedules slot', async () => {
    renderRoute('/admin/operations', {
      userId: 1,
      email: 'admin@test.local',
      fullName: 'Super Admin',
      role: 'administrator',
      emailVerified: true,
      permissions: [STAFF_PERMISSIONS.SystemAdmin],
    });

    // Switch to Mentors & schedules tab
    const mentorsTab = await screen.findByRole('tab', { name: /mentors & schedules/i });
    fireEvent.click(mentorsTab);

    expect(await screen.findByText('Thầy John Smith')).toBeInTheDocument();
    expect(screen.getByText('AI Fluency Coach')).toBeInTheDocument();

    // Check sentence-case type labels
    expect(screen.getByText('Human mentor')).toBeInTheDocument();
    expect(screen.getByText('AI coach')).toBeInTheDocument();

    // 1. Create mentor modal
    const createMentorBtn = screen.getByRole('button', { name: /create mentor profile/i });
    fireEvent.click(createMentorBtn);

    expect(await screen.findByRole('heading', { name: /create mentor profile/i })).toBeInTheDocument();

    // Human mentor requires associated user ID
    fireEvent.change(screen.getByLabelText(/display name/i), { target: { value: 'Cô Lan Anh' } });
    fireEvent.change(screen.getByLabelText(/price per session/i), { target: { value: '300000' } });
    fireEvent.change(screen.getByLabelText(/session duration/i), { target: { value: '60' } });

    const saveMentorBtn = screen.getByRole('button', { name: /save mentor/i });
    fireEvent.click(saveMentorBtn);

    // Shows user ID required error for human mentor
    expect(await screen.findByText(/user account id is required for human mentors/i)).toBeInTheDocument();

    // Provide user ID
    fireEvent.change(screen.getByLabelText(/associated user id/i), { target: { value: '35' } });
    fireEvent.click(saveMentorBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/mentors'),
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // 2. Open slots management modal for Human mentor (mentor 10)
    const manageSlotsBtns = screen.getAllByRole('button', { name: /manage slots/i });
    fireEvent.click(manageSlotsBtns[0]);

    expect(await screen.findByText(/schedule slots — thầy john smith/i)).toBeInTheDocument();

    // Existing slots displayed with sentence-case status (wait for async query)
    expect(await screen.findByText('Booked')).toBeInTheDocument();
    expect(screen.getAllByText('Open').length).toBeGreaterThan(0);

    // Add new slot
    fireEvent.change(screen.getByLabelText(/start time/i), {
      target: { value: '2026-11-16T14:00' },
    });
    const addSlotBtn = screen.getByRole('button', { name: /add slot/i });
    fireEvent.click(addSlotBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/mentors/10/slots'),
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // Close an open slot
    const closeSlotBtn = screen.getByRole('button', { name: /close slot/i });
    fireEvent.click(closeSlotBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/mentors/10/slots/801/close'),
        expect.objectContaining({ method: 'POST' }),
      );
    });
  });
});
