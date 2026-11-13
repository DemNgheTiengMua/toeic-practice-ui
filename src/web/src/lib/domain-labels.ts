/**
 * Shared domain enum -> UI label maps & chip tones mirroring
 * src/Contracts/Display/OperationsLabels.cs and src/Contracts/Display/LearnerLabels.cs.
 * Never display raw wire codes in rendered copy.
 */

export type StatusTone = 'success' | 'warning' | 'danger' | 'accent' | 'default';

export interface StatusDisplay {
  label: string;
  tone: StatusTone;
}

export const ORDER_STATUS_MAP: Record<string, StatusDisplay> = {
  pending: { label: 'Pending', tone: 'warning' },
  reconciling: { label: 'Confirming with the gateway', tone: 'warning' },
  paid: { label: 'Paid', tone: 'success' },
  failed: { label: 'Failed', tone: 'danger' },
  expired: { label: 'Expired', tone: 'default' },
};

export function formatOrderStatus(status?: string | null): StatusDisplay {
  const key = (status ?? '').trim().toLowerCase();
  return ORDER_STATUS_MAP[key] ?? { label: 'Pending', tone: 'default' };
}

export const GATEWAY_LABEL_MAP: Record<string, string> = {
  payos: 'payOS',
  mock: 'Mock gateway',
};

export function formatGatewayLabel(gateway?: string | null): string {
  const key = (gateway ?? '').trim().toLowerCase();
  return GATEWAY_LABEL_MAP[key] ?? 'Payment gateway';
}

export const CREDIT_REASON_MAP: Record<string, string> = {
  purchase: 'Credit purchase',
  admin_grant: 'Granted by staff',
  mock_exam_start: 'Mock exam',
  ai_grading: 'AI grading',
  mentor_booking: 'Mentor booking',
  admin_revoke: 'Revoked by staff',
  booking_cancel: 'Booking cancelled',
  refund_credit: 'Refund credits',
  placement_retake: 'Placement retake',
};

export function formatCreditReason(reason?: string | null): string {
  const key = (reason ?? '').trim().toLowerCase();
  return CREDIT_REASON_MAP[key] ?? 'Credit activity';
}

export const BOOKING_STATUS_MAP: Record<string, StatusDisplay> = {
  pending: { label: 'Pending', tone: 'warning' },
  confirmed: { label: 'Confirmed', tone: 'accent' },
  done: { label: 'Completed', tone: 'success' },
  cancelled: { label: 'Cancelled', tone: 'default' },
};

export function formatBookingStatus(status?: string | null): StatusDisplay {
  const key = (status ?? '').trim().toLowerCase();
  return BOOKING_STATUS_MAP[key] ?? { label: 'Scheduled', tone: 'default' };
}

export const MENTOR_TYPE_MAP: Record<string, string> = {
  human: 'Human mentor',
  ai: 'AI coach',
};

export function formatMentorType(type?: string | null): string {
  const key = (type ?? '').trim().toLowerCase();
  return MENTOR_TYPE_MAP[key] ?? 'Mentor';
}

export const STANDING_STATUS_MAP: Record<string, StatusDisplay> = {
  measured: { label: 'Measured', tone: 'success' },
  not_measured: { label: 'Not measured yet', tone: 'warning' },
  not_examined: { label: 'Not examined by this certificate', tone: 'default' },
};

export function formatStandingStatus(status?: string | null): StatusDisplay {
  const key = (status ?? '').trim().toLowerCase();
  return STANDING_STATUS_MAP[key] ?? { label: 'Not measured yet', tone: 'default' };
}

export const COMPLAINT_STATUS_MAP: Record<string, StatusDisplay> = {
  open: { label: 'Open', tone: 'warning' },
  reviewing: { label: 'Under review', tone: 'accent' },
  resolved: { label: 'Resolved', tone: 'success' },
};

export function formatComplaintStatus(status?: string | null): StatusDisplay {
  const key = (status ?? '').trim().toLowerCase();
  return COMPLAINT_STATUS_MAP[key] ?? { label: 'Open', tone: 'default' };
}

export const COMPLAINT_TYPE_MAP: Record<string, string> = {
  mentor_no_show: 'Mentor did not attend',
  mentor_quality: 'Mentor quality',
  grading_issue: 'Grading issue',
  technical_error: 'Technical error',
  payment_issue: 'Payment issue',
};

export function formatComplaintType(type?: string | null): string {
  const key = (type ?? '').trim().toLowerCase();
  return COMPLAINT_TYPE_MAP[key] ?? 'Support report';
}

export const MODALITY_LABEL_MAP: Record<string, string> = {
  listening: 'Listening',
  reading: 'Reading',
  speaking: 'Speaking',
  writing: 'Writing',
};

export function formatModality(modality?: string | null): string {
  const key = (modality ?? '').trim().toLowerCase();
  return MODALITY_LABEL_MAP[key] ?? (modality ? modality : 'Skill');
}

/**
 * Curated IANA timezone IDs mirroring src/Contracts/TimeZones.cs.
 * Vietnam stays first as the platform's primary learner timezone.
 */
export const TIME_ZONE_IDS: readonly string[] = [
  'Asia/Ho_Chi_Minh',
  'Asia/Bangkok',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Asia/Seoul',
  'Asia/Shanghai',
  'Asia/Kolkata',
  'Asia/Dubai',
  'Europe/London',
  'Europe/Paris',
  'Europe/Berlin',
  'America/New_York',
  'America/Chicago',
  'America/Los_Angeles',
  'Australia/Sydney',
  'UTC',
];

export function formatDateTime(value?: string | null): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '—';
  return new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(parsed);
}

export function formatDateShort(value?: string | null): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '—';
  return new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium',
  }).format(parsed);
}
