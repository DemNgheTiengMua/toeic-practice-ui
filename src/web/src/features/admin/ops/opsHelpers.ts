import type {
  OrderStatusDto,
  PaymentGatewayDto,
  PaymentEventSourceDto,
  GatewayStatusDto,
  RefundMethodDto,
  RefundStatusDto,
  ComplaintTypeDto,
  ComplaintStatusDto,
  MentorTypeDto,
  SlotStatusDto,
} from '../../../lib/api';

/**
 * Human-readable sentence-case label mappers.
 * Invariant: Never render raw wire enum/status codes (e.g. mentor_no_show, reconciling).
 * Exactly mirrors Contracts DisplayLabels / OperationsLabels.
 */

export function formatOrderStatusLabel(status?: OrderStatusDto | string | null): string {
  switch (status) {
    case 'pending':
      return 'Pending';
    case 'reconciling':
      return 'Confirming with the gateway';
    case 'paid':
      return 'Paid';
    case 'failed':
      return 'Failed';
    case 'expired':
      return 'Expired';
    default:
      return 'Not reported';
  }
}

export function formatGatewayLabel(gateway?: PaymentGatewayDto | string | null): string {
  switch (gateway) {
    case 'payos':
      return 'payOS';
    case 'mock':
      return 'Mock gateway';
    default:
      return 'Not reported';
  }
}

export function formatPaymentEventSourceLabel(source?: PaymentEventSourceDto | string | null): string {
  switch (source) {
    case 'webhook':
      return 'Gateway webhook';
    case 'status_query':
      return 'Status check';
    case 'return_redirect':
      return 'Learner returned from the gateway';
    case 'cancel':
      return 'Cancelled at the gateway';
    default:
      return 'Not reported';
  }
}

export function formatPaymentEventReportedStatusLabel(status?: GatewayStatusDto | string | null): string {
  switch (status) {
    case 'pending':
      return 'Pending';
    case 'paid':
      return 'Paid';
    case 'failed':
      return 'Failed';
    case 'expired':
      return 'Expired';
    case 'cancelled':
      return 'Cancelled';
    default:
      return 'Not reported';
  }
}

export function formatRefundMethodLabel(method?: RefundMethodDto | string | null): string {
  switch (method) {
    case 'money':
      return 'Money';
    case 'credit':
      return 'Credits';
    default:
      return 'Not reported';
  }
}

export function formatRefundStatusLabel(status?: RefundStatusDto | string | null): string {
  switch (status) {
    case 'requested':
      return 'Requested';
    case 'approved':
      return 'Approved';
    case 'settled':
      return 'Settled';
    case 'rejected':
      return 'Rejected';
    default:
      return 'Not reported';
  }
}

export function formatComplaintTypeLabel(type?: ComplaintTypeDto | string | null): string {
  switch (type) {
    case 'mentor_no_show':
      return 'Mentor did not attend';
    case 'mentor_quality':
      return 'Mentor quality';
    case 'grading_issue':
      return 'Grading issue';
    case 'technical_error':
      return 'Technical error';
    case 'payment_issue':
      return 'Payment issue';
    default:
      return 'Not reported';
  }
}

export function formatComplaintStatusLabel(status?: ComplaintStatusDto | string | null): string {
  switch (status) {
    case 'open':
      return 'Open';
    case 'reviewing':
      return 'Under review';
    case 'resolved':
      return 'Resolved';
    default:
      return 'Not reported';
  }
}

export function formatMentorTypeLabel(type?: MentorTypeDto | string | null): string {
  switch (type) {
    case 'human':
      return 'Human mentor';
    case 'ai':
      return 'AI coach';
    default:
      return 'Not reported';
  }
}

export function formatMentorSlotStatusLabel(status?: SlotStatusDto | string | null): string {
  switch (status) {
    case 'open':
      return 'Open';
    case 'booked':
      return 'Booked';
    case 'closed':
      return 'Closed';
    default:
      return 'Not reported';
  }
}

// ============================================================================
// Semantic Chip Status Colors (DESIGN.md hierarchy)
// ============================================================================

export type ChipColor = 'default' | 'accent' | 'success' | 'warning' | 'danger';

export function getOrderStatusColor(status?: OrderStatusDto | string | null): ChipColor {
  switch (status) {
    case 'paid':
      return 'success';
    case 'reconciling':
    case 'pending':
      return 'warning';
    case 'failed':
    case 'expired':
      return 'danger';
    default:
      return 'default';
  }
}

export function getRefundStatusColor(status?: RefundStatusDto | string | null): ChipColor {
  switch (status) {
    case 'settled':
    case 'approved':
      return 'success';
    case 'requested':
      return 'warning';
    case 'rejected':
      return 'danger';
    default:
      return 'default';
  }
}

export function getComplaintStatusColor(status?: ComplaintStatusDto | string | null): ChipColor {
  switch (status) {
    case 'resolved':
      return 'success';
    case 'reviewing':
      return 'warning';
    case 'open':
      return 'danger';
    default:
      return 'default';
  }
}

export function getSlotStatusColor(status?: SlotStatusDto | string | null): ChipColor {
  switch (status) {
    case 'open':
      return 'success';
    case 'booked':
      return 'accent';
    case 'closed':
      return 'default';
    default:
      return 'default';
  }
}

// ============================================================================
// Date and Time Formatting
// ============================================================================

export function formatDateTime(isoString?: string | null): string {
  if (!isoString) return '—';
  try {
    const d = new Date(isoString);
    if (isNaN(d.getTime())) return isoString;
    return d.toLocaleString('vi-VN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return isoString;
  }
}

export function formatDateOnly(isoString?: string | null): string {
  if (!isoString) return '—';
  try {
    const d = new Date(isoString);
    if (isNaN(d.getTime())) return isoString;
    return d.toLocaleDateString('vi-VN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });
  } catch {
    return isoString;
  }
}

// ============================================================================
// Domain Validations (Ceiling, Make-Good Grants, CSV Dates)
// ============================================================================

export const DEFAULT_ORDERLESS_CEILING = 200;
export const MAX_COMPLAINT_GRANT_CREDITS = 20;

export interface RefundValidationInput {
  userId: number;
  method: RefundMethodDto;
  creditCount: number;
  amount?: number | null;
  reason: string;
  orderId?: number | null;
}

export interface ValidationResult {
  valid: boolean;
  errors: Record<string, string>;
}

export function validateRefundRequest(input: RefundValidationInput): ValidationResult {
  const errors: Record<string, string> = {};

  if (!input.userId || input.userId <= 0 || isNaN(input.userId)) {
    errors.userId = 'Valid learner user ID is required.';
  }

  const trimmedReason = (input.reason || '').trim();
  if (!trimmedReason) {
    errors.reason = 'Reason is mandatory.';
  } else if (trimmedReason.length > 4000) {
    errors.reason = 'Reason cannot exceed 4000 characters.';
  }

  if (input.method === 'money') {
    const amt = input.amount ?? 0;
    if (amt <= 0 || isNaN(amt)) {
      errors.amount = 'A money refund requires a positive amount.';
    }
    if (input.creditCount > 0) {
      errors.creditCount = 'A money refund carries an amount, not a credit count.';
    }
    if (!input.orderId && amt > DEFAULT_ORDERLESS_CEILING) {
      errors.amount = `A money refund with no order behind it may return at most ${DEFAULT_ORDERLESS_CEILING} dong.`;
    }
  } else if (input.method === 'credit') {
    const cred = input.creditCount ?? 0;
    if (cred <= 0 || isNaN(cred)) {
      errors.creditCount = 'A credit refund requires at least 1 credit.';
    }
    if (input.amount && input.amount > 0) {
      errors.amount = 'A credit refund carries a credit count, not an amount of money.';
    }
    if (!input.orderId && cred > DEFAULT_ORDERLESS_CEILING) {
      errors.creditCount = `A credit refund with no order behind it may return at most ${DEFAULT_ORDERLESS_CEILING} credits.`;
    }
  } else {
    errors.method = 'Invalid refund method.';
  }

  return {
    valid: Object.keys(errors).length === 0,
    errors,
  };
}

export interface ComplaintResolveInput {
  resolution: string;
  internalNotes: string;
  grantCredits?: number;
}

export function validateComplaintResolve(input: ComplaintResolveInput): ValidationResult {
  const errors: Record<string, string> = {};

  const trimmedRes = (input.resolution || '').trim();
  if (!trimmedRes) {
    errors.resolution = 'Resolution message for learner is required.';
  } else if (trimmedRes.length > 4000) {
    errors.resolution = 'Resolution cannot exceed 4000 characters.';
  }

  const trimmedNotes = (input.internalNotes || '').trim();
  if (!trimmedNotes) {
    errors.internalNotes = 'Internal investigation notes are required.';
  } else if (trimmedNotes.length > 4000) {
    errors.internalNotes = 'Internal notes cannot exceed 4000 characters.';
  }

  const credits = input.grantCredits ?? 0;
  if (isNaN(credits) || credits < 0 || credits > MAX_COMPLAINT_GRANT_CREDITS) {
    errors.grantCredits = `Grant credits must be between 0 and ${MAX_COMPLAINT_GRANT_CREDITS}.`;
  }

  return {
    valid: Object.keys(errors).length === 0,
    errors,
  };
}

export function validateCsvExportDates(from?: string, to?: string): { valid: boolean; error?: string } {
  const dateRegex = /^\d{4}-\d{2}-\d{2}$/;
  if (from && !dateRegex.test(from)) {
    return { valid: false, error: 'From date must be in YYYY-MM-DD format.' };
  }
  if (to && !dateRegex.test(to)) {
    return { valid: false, error: 'To date must be in YYYY-MM-DD format.' };
  }
  if (from && to && from > to) {
    return { valid: false, error: 'From date cannot be after to date.' };
  }
  return { valid: true };
}

/**
 * Initiates an in-browser download of a binary Blob.
 */
export function triggerBlobDownload(blob: Blob, filename: string): void {
  const url = window.URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  window.URL.revokeObjectURL(url);
}
