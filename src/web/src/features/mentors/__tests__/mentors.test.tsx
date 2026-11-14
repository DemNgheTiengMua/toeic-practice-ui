import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type {
  MentorSummaryDto,
  MentorDetailDto,
  MentorBookingDto,
  CancelBookingResultDto,
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

const MOCK_LEARNER_CONTEXT = {
  active: { certId: 1, code: 'TOEIC', name: 'TOEIC Listening & Reading' },
  available: [{ certId: 1, code: 'TOEIC', name: 'TOEIC Listening & Reading' }],
  creditBalance: 30, // 30 credits
};

const MOCK_MENTORS: MentorSummaryDto[] = [
  {
    mentorId: 1,
    displayName: 'Sarah Jenkins',
    bio: 'Certified IELTS and TOEIC master instructor with 10 years experience.',
    certId: 1,
    certCode: 'TOEIC',
    certName: 'TOEIC Listening & Reading',
    skillId: 10,
    skillCode: 'LR',
    skillName: 'Listening & Reading',
    modality: 'listening',
    meetLink: 'https://meet.google.com/sarah-room',
    priceCredits: 5,
    headline: 'Senior TOEIC Specialist',
    expertise: 'Listening strategies, error traps',
    sessionMinutes: 45,
    isActive: true,
    avgRating: 4.9,
    reviewCount: 18,
    weeklySlots: [
      {
        weeklySlotId: 101,
        mentorId: 1,
        dayOfWeek: 1,
        startTime: '08:00',
        endTime: '09:00',
      },
      {
        weeklySlotId: 102,
        mentorId: 1,
        dayOfWeek: 1,
        startTime: '19:30',
        endTime: '20:30',
      },
      {
        weeklySlotId: 103,
        mentorId: 1,
        dayOfWeek: 5,
        startTime: '14:00',
        endTime: '15:00',
      },
    ],
  },
  {
    mentorId: 2,
    displayName: 'Dr. Trần Minh Khoa',
    bio: 'Speaking & Writing examiner specializing in structured feedback.',
    certId: 1,
    certCode: 'TOEIC',
    certName: 'TOEIC Listening & Reading',
    skillId: 11,
    skillCode: 'SPK',
    skillName: 'Speaking',
    modality: 'speaking',
    meetLink: 'https://meet.google.com/khoa-room',
    priceCredits: 3,
    headline: 'Speaking & Pronunciation Lead',
    sessionMinutes: 60,
    isActive: true,
    avgRating: 4.8,
    reviewCount: 42,
    weeklySlots: [
      {
        weeklySlotId: 201,
        mentorId: 2,
        dayOfWeek: 7,
        startTime: '09:00',
        endTime: '10:00',
      },
    ],
  },
];

const MOCK_DETAIL_1: MentorDetailDto = {
  mentor: MOCK_MENTORS[0]!,
  weeklySlots: MOCK_MENTORS[0]!.weeklySlots,
  availableSlots: [
    {
      slotId: 501,
      mentorId: 1,
      startAt: '2026-11-20T14:00:00Z',
      endAt: '2026-11-20T14:45:00Z',
      status: 'open',
    },
    {
      slotId: 502,
      mentorId: 1,
      startAt: '2026-11-21T10:00:00Z',
      endAt: '2026-11-21T10:45:00Z',
      status: 'booked',
    },
  ],
  reviews: [
    {
      reviewId: 1,
      rating: 5,
      text: 'Exceptional feedback on timing and section 4 strategies!',
      userDisplayName: 'Nguyen V.',
      createdAt: '2026-10-25T15:00:00Z',
    },
  ],
};

const MOCK_BOOKING_SAFE: MentorBookingDto = {
  bookingId: 901,
  userId: 42,
  mentorId: 1,
  mentorName: 'Sarah Jenkins',
  mentorHeadline: 'Senior TOEIC Specialist',
  slotId: 501,
  startAt: '2026-11-20T14:00:00Z',
  endAt: '2026-11-20T14:45:00Z',
  status: 'confirmed',
  meetLink: 'https://meet.google.com/abc-defg-hij',
  focusArea: 'Listening traps in Part 3',
  priceCredits: 5,
  createdAt: '2026-11-13T10:00:00Z',
  standing: {
    certId: 1,
    certCode: 'TOEIC',
    hasNoMeasurement: false,
    overallLevelCode: 'B2',
    limitingSkillCode: 'LST',
    skills: [
      {
        skillId: 1,
        modality: 'listening',
        skillCode: 'LST',
        skillName: 'Listening',
        status: 'measured',
        levelCode: 'B1',
        positionInBand: 45,
        isStale: false,
      },
      {
        skillId: 2,
        modality: 'reading',
        skillCode: 'RDG',
        skillName: 'Reading',
        status: 'not_measured',
        isStale: false,
      },
    ],
  },
};

const MOCK_BOOKING_XSS_INERT: MentorBookingDto = {
  ...MOCK_BOOKING_SAFE,
  bookingId: 902,
  meetLink: 'javascript:alert("XSS")',
};

const MOCK_BOOKING_NO_MEASUREMENT: MentorBookingDto = {
  ...MOCK_BOOKING_SAFE,
  bookingId: 903,
  standing: {
    certId: 1,
    certCode: 'TOEIC',
    hasNoMeasurement: true,
    skills: [],
  },
};

describe('Mentor bookings & standing feature (#301)', () => {
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

  it('renders mentor catalog with human mentors only, skill filtering, and credit pricing', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url.startsWith('/api/mentors')) return jsonResponse(MOCK_MENTORS);
      return jsonResponse({}, 404);
    });

    renderRoute('/mentors');

    expect(await screen.findByRole('heading', { name: /mentors & instructors/i })).toBeInTheDocument();
    expect(screen.getByText('Sarah Jenkins')).toBeInTheDocument();
    expect(screen.getByText('Dr. Trần Minh Khoa')).toBeInTheDocument();
    expect(screen.getByText('5 credits')).toBeInTheDocument();
    expect(screen.queryByText(/ai coach/i)).not.toBeInTheDocument();
  });

  it('renders mentor detail with weekly recurring schedule, selectable open slots, and disabled greyed-out booked slots', async () => {
    let bookedPayload: unknown = null;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/mentors/1') return jsonResponse(MOCK_DETAIL_1);
      if (url === '/api/mentors/1/book' && init?.method === 'POST') {
        bookedPayload = JSON.parse(String(init.body));
        return jsonResponse(MOCK_BOOKING_SAFE);
      }
      if (url === '/api/mentors/bookings/901') return jsonResponse(MOCK_BOOKING_SAFE);
      return jsonResponse({}, 404);
    });

    renderRoute('/mentors/1');

    expect(await screen.findByRole('heading', { name: /sarah jenkins/i })).toBeInTheDocument();
    expect(screen.getByText('Senior TOEIC Specialist')).toBeInTheDocument();
    expect(screen.getByText(/exceptional feedback/i)).toBeInTheDocument();
    expect(screen.getByText(/4\.9/)).toBeInTheDocument();

    // Weekly recurring schedule windows displayed
    expect(screen.getByText(/thứ 2 · monday/i)).toBeInTheDocument();
    expect(screen.getByText(/08:00 – 09:00/i)).toBeInTheDocument();
    expect(screen.getByText(/19:30 – 20:30/i)).toBeInTheDocument();
    expect(screen.getByText(/thứ 6 · friday/i)).toBeInTheDocument();
    expect(screen.getByText(/14:00 – 15:00/i)).toBeInTheDocument();

    // Both open (#501) and booked (#502) slots are rendered in the schedule list
    const openSlotBtn = screen.getByTestId('mentor-slot-501');
    const bookedSlotBtn = screen.getByTestId('mentor-slot-502');

    expect(openSlotBtn).not.toBeDisabled();
    expect(bookedSlotBtn).toBeDisabled();
    expect(within(bookedSlotBtn).getByText(/^booked$/i)).toBeInTheDocument();

    // Confirm button starts disabled until an open slot is selected
    const confirmBtn = await screen.findByRole('button', { name: /confirm & book/i });
    expect(confirmBtn).toBeDisabled();

    // Selecting the open slot enables booking
    fireEvent.click(openSlotBtn);
    expect(confirmBtn).not.toBeDisabled();

    fireEvent.change(screen.getByLabelText(/target focus or questions/i), {
      target: { value: 'Listening Part 3 & 4 pacing' },
    });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(bookedPayload).toEqual({
        slotId: 501,
        focusArea: 'Listening Part 3 & 4 pacing',
      });
    });
  });

  it('enforces block-before-charge paywall when credits are insufficient', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      // Low balance: 2 credits, but mentor costs 5
      if (url === '/api/learner/context')
        return jsonResponse({ ...MOCK_LEARNER_CONTEXT, creditBalance: 2 });
      if (url === '/api/mentors/1') return jsonResponse(MOCK_DETAIL_1);
      return jsonResponse({}, 404);
    });

    renderRoute('/mentors/1');

    expect(await screen.findByRole('heading', { name: /sarah jenkins/i })).toBeInTheDocument();

    // Booking card indicates insufficient credits
    expect(screen.getByText(/insufficient credits/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /buy credits|top up/i })).toBeInTheDocument();
  });

  it('safely renders https meet link as clickable external link and sanitizes javascript: meetLink into inert text', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/mentors/bookings/901') return jsonResponse(MOCK_BOOKING_SAFE);
      if (url === '/api/mentors/bookings/902') return jsonResponse(MOCK_BOOKING_XSS_INERT);
      return jsonResponse({}, 404);
    });

    // Case A: Safe meet link (https:)
    const { unmount } = renderRoute('/mentors/bookings/901');

    const safeLink = (await screen.findByRole('link', {
      name: /join meeting/i,
    })) as HTMLAnchorElement;
    expect(safeLink).toBeInTheDocument();
    expect(safeLink.href).toBe('https://meet.google.com/abc-defg-hij');
    expect(safeLink.target).toBe('_blank');

    unmount();

    // Case B: Injected javascript: meetLink
    renderRoute('/mentors/bookings/902');

    expect(await screen.findByText(/booking #902/i)).toBeInTheDocument();
    // Must NOT render any link pointing to javascript:
    const dangerousLink = screen.queryByRole('link', { name: /join meeting/i });
    expect(dangerousLink).toBeNull();
    // Instead renders an inert badge/warning text
    expect(screen.getByText(/invalid or unverified meeting link/i)).toBeInTheDocument();
  });

  it('renders MentorStanding component with hasNoMeasurement, measured bands without % sign, and not_measured', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/mentors/bookings/901') return jsonResponse(MOCK_BOOKING_SAFE);
      if (url === '/api/mentors/bookings/903') return jsonResponse(MOCK_BOOKING_NO_MEASUREMENT);
      return jsonResponse({}, 404);
    });

    // Render booking 901 with standing
    const { unmount } = renderRoute('/mentors/bookings/901');

    expect(await screen.findByText(/learner standing/i)).toBeInTheDocument();
    expect(screen.getByText(/Level B2|B2/)).toBeInTheDocument(); // Overall level
    expect(screen.getByText('Measured')).toBeInTheDocument(); // Listening status
    expect(screen.getByText('Not measured yet')).toBeInTheDocument(); // Reading status
    // Position in band is 45 without % sign
    expect(screen.getByText(/position in band: 45(?!\s*%)/i)).toBeInTheDocument();

    unmount();

    // Render booking 903 with hasNoMeasurement
    renderRoute('/mentors/bookings/903');
    expect(await screen.findByText(/nothing on toeic has been measured yet/i)).toBeInTheDocument();
  });

  it('allows cancelling a booking and shows refunded credits message', async () => {
    let cancelled = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(MOCK_USER);
      if (url === '/api/learner/context') return jsonResponse(MOCK_LEARNER_CONTEXT);
      if (url === '/api/mentors/bookings/901') {
        return jsonResponse(
          cancelled
            ? { ...MOCK_BOOKING_SAFE, status: 'cancelled', cancelledAt: '2026-11-13T12:00:00Z' }
            : MOCK_BOOKING_SAFE,
        );
      }
      if (url === '/api/mentors/bookings/901/cancel' && init?.method === 'POST') {
        cancelled = true;
        const res: CancelBookingResultDto = {
          bookingId: 901,
          status: 'cancelled',
          cancelledAt: '2026-11-13T12:00:00Z',
          refundedCredits: 5,
        };
        return jsonResponse(res);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/mentors/bookings/901');

    expect(await screen.findByRole('heading', { name: /booking #901/i })).toBeInTheDocument();

    const cancelBtn = screen.getByRole('button', { name: /cancel session/i });
    fireEvent.click(cancelBtn);

    expect(await screen.findByText(/booking cancelled/i)).toBeInTheDocument();
    expect(screen.getByText(/5 credits refunded/i)).toBeInTheDocument();
  });
});
