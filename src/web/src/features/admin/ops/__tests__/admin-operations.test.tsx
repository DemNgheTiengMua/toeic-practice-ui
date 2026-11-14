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
  AdminMentorPayoutsReportDto,
  MentorCertificateOptionDto,
  MentorWeeklySlotDto,
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

const SAMPLE_WEEKLY_SLOTS: MentorWeeklySlotDto[] = [
  {
    weeklySlotId: 701,
    mentorId: 10,
    dayOfWeek: 1,
    startTime: '08:00',
    endTime: '09:00',
  },
  {
    weeklySlotId: 702,
    mentorId: 10,
    dayOfWeek: 5,
    startTime: '19:30',
    endTime: '20:30',
  },
];

const SAMPLE_MENTORS: AdminMentorListItemDto[] = [
  {
    mentorId: 10,
    displayName: 'Thầy John Smith',
    certId: 1,
    certCode: 'TOEIC',
    skillId: 3,
    skillName: 'Speaking',
    meetLink: 'https://meet.google.com/john-smith-room',
    priceCredits: 5,
    priceVnd: 100000,
    sessionMinutes: 45,
    avgRating: 4.8,
    reviewCount: 32,
    isActive: true,
    weeklySlots: SAMPLE_WEEKLY_SLOTS,
    completedSessionsCount: 4,
    totalEarnedCredits: 20,
    totalEarnedVnd: 400000,
  },
  {
    mentorId: 11,
    displayName: 'Dr. Trần Minh Khoa',
    certId: 1,
    certCode: 'TOEIC',
    skillId: null,
    skillName: null,
    meetLink: 'https://meet.google.com/khoa-room',
    priceCredits: 3,
    priceVnd: 60000,
    sessionMinutes: 60,
    avgRating: 4.9,
    reviewCount: 54,
    isActive: true,
    weeklySlots: [
      {
        weeklySlotId: 703,
        mentorId: 11,
        dayOfWeek: 7,
        startTime: '09:00',
        endTime: '10:00',
      },
    ],
    completedSessionsCount: 2,
    totalEarnedCredits: 6,
    totalEarnedVnd: 120000,
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

const SAMPLE_PAYOUTS_REPORT: AdminMentorPayoutsReportDto = {
  from: '2026-11-01',
  to: '2026-11-14',
  totalSessions: 6,
  totalCredits: 26,
  totalAmountVnd: 520000,
  vndPerCredit: 20000,
  mentors: [
    {
      mentorId: 10,
      mentorName: 'Thầy John Smith',
      certCode: 'TOEIC',
      completedSessionsCount: 4,
      totalCredits: 20,
      totalAmountVnd: 400000,
    },
    {
      mentorId: 11,
      mentorName: 'Dr. Trần Minh Khoa',
      certCode: 'TOEIC',
      completedSessionsCount: 2,
      totalCredits: 6,
      totalAmountVnd: 120000,
    },
  ],
  sessions: [
    {
      bookingId: 501,
      sessionDate: '2026-11-10',
      startAt: '2026-11-10T01:00:00Z',
      endAt: '2026-11-10T01:45:00Z',
      mentorId: 10,
      mentorName: 'Thầy John Smith',
      learnerName: 'Nguyễn Văn A',
      learnerEmail: 'learnera@test.local',
      certCode: 'TOEIC',
      skillName: 'Speaking',
      priceCredits: 5,
      amountVnd: 100000,
    },
  ],
};

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
      if (url.startsWith('/api/admin/mentors/payouts/export')) {
        return new Response('\uFEFF"Booking ID","Mentor name"\r\n"501","Thầy John Smith"\r\n', {
          status: 200,
          headers: {
            'Content-Type': 'text/csv; charset=utf-8',
            'Content-Disposition': 'attachment; filename="mentor-payouts-2026-11-01-to-2026-11-14.csv"',
          },
        });
      }
      if (url.startsWith('/api/admin/mentors/payouts')) {
        return jsonResponse(SAMPLE_PAYOUTS_REPORT);
      }
      if (url.startsWith('/api/admin/mentors/10/weekly-slots') && init?.method === 'PUT') {
        const body = JSON.parse(init?.body as string);
        const list = Array.isArray(body) ? body : body.weeklySlots ?? [];
        return jsonResponse(
          list.map((w: any, idx: number) => ({
            weeklySlotId: 710 + idx,
            mentorId: 10,
            dayOfWeek: w.dayOfWeek,
            startTime: w.startTime,
            endTime: w.endTime,
          })),
        );
      }
      if (url.startsWith('/api/admin/mentors/10/weekly-slots')) {
        return jsonResponse(SAMPLE_WEEKLY_SLOTS);
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

  it('manages human mentors with fixed MeetLink, credit pricing, weekly availability schedule, and payout CSV export', async () => {
    window.URL.createObjectURL = vi.fn(() => 'blob:mock-payout-csv');
    window.URL.revokeObjectURL = vi.fn();
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

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

    expect(await screen.findAllByText('Thầy John Smith')).not.toHaveLength(0);
    expect(screen.getAllByText('Dr. Trần Minh Khoa')).not.toHaveLength(0);
    expect(screen.queryByText(/ai coach/i)).not.toBeInTheDocument();

    // 1. Create mentor modal (human-only, mandatory MeetLink, credits with live VND equivalent)
    const createMentorBtn = screen.getByRole('button', { name: /create mentor profile/i });
    fireEvent.click(createMentorBtn);

    expect(await screen.findByRole('heading', { name: /create mentor profile/i })).toBeInTheDocument();
    expect(screen.queryByLabelText(/associated user id/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/price per session/i)).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/display name/i), { target: { value: 'Cô Lan Anh' } });
    fireEvent.change(screen.getByLabelText(/price in credits/i), { target: { value: '4' } });
    fireEvent.change(screen.getByLabelText(/session duration/i), { target: { value: '60' } });

    // Live VND equivalent (4 * 20,000 = 80.000 ₫)
    expect(screen.getByText(/80\.000\s*₫/)).toBeInTheDocument();

    // Clear MeetLink to verify mandatory validation
    fireEvent.change(screen.getByLabelText(/meeting link/i), { target: { value: '' } });

    const saveMentorBtn = screen.getByRole('button', { name: /save mentor/i });
    fireEvent.click(saveMentorBtn);

    expect(await screen.findByText(/valid meeting link.*is required/i)).toBeInTheDocument();

    // Provide valid MeetLink
    fireEvent.change(screen.getByLabelText(/meeting link/i), {
      target: { value: 'https://meet.google.com/lan-anh-room' },
    });
    fireEvent.click(saveMentorBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/mentors'),
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // 2. Open weekly schedule modal for mentor 10 and add a weekly availability window
    const scheduleBtns = screen.getAllByRole('button', { name: /weekly schedule/i });
    fireEvent.click(scheduleBtns[0]);

    expect(await screen.findByText(/weekly schedule — thầy john smith/i)).toBeInTheDocument();
    expect(await screen.findByText(/08:00 – 09:00/)).toBeInTheDocument();

    // Try adding an overlapping window on Monday (08:30 - 09:30 overlaps 08:00 - 09:00)
    fireEvent.change(screen.getByLabelText(/weekday/i), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText(/window start/i), { target: { value: '08:30' } });
    fireEvent.change(screen.getByLabelText(/window end/i), { target: { value: '09:30' } });
    fireEvent.click(screen.getByRole('button', { name: /add window/i }));

    expect(await screen.findByText(/overlap/i)).toBeInTheDocument();

    // Add a non-overlapping window on Monday (14:00 - 15:00)
    fireEvent.change(screen.getByLabelText(/window start/i), { target: { value: '14:00' } });
    fireEvent.change(screen.getByLabelText(/window end/i), { target: { value: '15:00' } });
    fireEvent.click(screen.getByRole('button', { name: /add window/i }));

    expect(await screen.findByText(/14:00 – 15:00/)).toBeInTheDocument();

    // Save weekly schedule
    fireEvent.click(screen.getByRole('button', { name: /save weekly schedule/i }));

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/mentors/10/weekly-slots'),
        expect.objectContaining({ method: 'PUT' }),
      );
    });

    // 3. Mentor payouts & CSV export section
    expect(screen.getByText(/mentor payouts & completed sessions/i)).toBeInTheDocument();
    expect(await screen.findByText('Nguyễn Văn A')).toBeInTheDocument();
    expect(screen.getAllByText(/520\.000\s*₫/).length).toBeGreaterThan(0);

    const exportCsvBtn = screen.getByRole('button', { name: /export payout csv/i });
    fireEvent.click(exportCsvBtn);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/admin/mentors/payouts/export'),
        expect.anything(),
      );
      expect(clickSpy).toHaveBeenCalled();
    });
  }, 15_000);
});
