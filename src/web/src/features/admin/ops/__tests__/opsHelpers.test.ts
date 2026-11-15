import { describe, expect, it } from 'vitest';
import {
  formatOrderStatusLabel,
  formatGatewayLabel,
  formatPaymentEventSourceLabel,
  formatPaymentEventReportedStatusLabel,
  formatRefundMethodLabel,
  formatRefundStatusLabel,
  formatComplaintTypeLabel,
  formatComplaintStatusLabel,
  formatMentorSlotStatusLabel,
  formatWeekdayLabel,
  validateWeeklySlots,
  getDefaultUtc7MonthRange,
  getOrderStatusColor,
  getRefundStatusColor,
  getComplaintStatusColor,
  getSlotStatusColor,
  validateRefundRequest,
  validateComplaintResolve,
  validateCsvExportDates,
  DEFAULT_ORDERLESS_CEILING,
} from '../opsHelpers';

describe('opsHelpers - sentence-case label mappers', () => {
  it('formats order statuses without raw codes or underscores', () => {
    expect(formatOrderStatusLabel('pending')).toBe('Pending');
    expect(formatOrderStatusLabel('reconciling')).toBe('Confirming with the gateway');
    expect(formatOrderStatusLabel('paid')).toBe('Paid');
    expect(formatOrderStatusLabel('failed')).toBe('Failed');
    expect(formatOrderStatusLabel('expired')).toBe('Expired');
    expect(formatOrderStatusLabel(undefined)).toBe('Not reported');
  });

  it('formats payment gateway labels', () => {
    expect(formatGatewayLabel('payos')).toBe('payOS');
    expect(formatGatewayLabel('mock')).toBe('Mock gateway');
    expect(formatGatewayLabel(null)).toBe('Not reported');
  });

  it('formats payment event source and reported status', () => {
    expect(formatPaymentEventSourceLabel('webhook')).toBe('Gateway webhook');
    expect(formatPaymentEventSourceLabel('status_query')).toBe('Status check');
    expect(formatPaymentEventSourceLabel('return_redirect')).toBe('Learner returned from the gateway');
    expect(formatPaymentEventSourceLabel('cancel')).toBe('Cancelled at the gateway');

    expect(formatPaymentEventReportedStatusLabel('pending')).toBe('Pending');
    expect(formatPaymentEventReportedStatusLabel('paid')).toBe('Paid');
    expect(formatPaymentEventReportedStatusLabel('cancelled')).toBe('Cancelled');
    expect(formatPaymentEventReportedStatusLabel(null)).toBe('Not reported');
  });

  it('formats refund method and status labels', () => {
    expect(formatRefundMethodLabel('money')).toBe('Money');
    expect(formatRefundMethodLabel('credit')).toBe('Credits');

    expect(formatRefundStatusLabel('requested')).toBe('Requested');
    expect(formatRefundStatusLabel('approved')).toBe('Approved');
    expect(formatRefundStatusLabel('settled')).toBe('Settled');
    expect(formatRefundStatusLabel('rejected')).toBe('Rejected');
  });

  it('formats complaint type and status labels', () => {
    expect(formatComplaintTypeLabel('mentor_no_show')).toBe('Mentor did not attend');
    expect(formatComplaintTypeLabel('mentor_quality')).toBe('Mentor quality');
    expect(formatComplaintTypeLabel('grading_issue')).toBe('Grading issue');
    expect(formatComplaintTypeLabel('technical_error')).toBe('Technical error');
    expect(formatComplaintTypeLabel('payment_issue')).toBe('Payment issue');

    expect(formatComplaintStatusLabel('open')).toBe('Open');
    expect(formatComplaintStatusLabel('reviewing')).toBe('Processing');
    expect(formatComplaintStatusLabel('resolved')).toBe('Resolved');
  });

  it('formats mentor slot status and weekday labels', () => {
    expect(formatMentorSlotStatusLabel('open')).toBe('Open');
    expect(formatMentorSlotStatusLabel('booked')).toBe('Booked');
    expect(formatMentorSlotStatusLabel('closed')).toBe('Closed');

    expect(formatWeekdayLabel(1)).toBe('Thứ 2 (Monday)');
    expect(formatWeekdayLabel(7)).toBe('Chủ Nhật (Sunday)');
  });
});

describe('opsHelpers - semantic status colors', () => {
  it('returns appropriate semantic colors', () => {
    expect(getOrderStatusColor('paid')).toBe('success');
    expect(getOrderStatusColor('reconciling')).toBe('warning');
    expect(getOrderStatusColor('failed')).toBe('danger');

    expect(getRefundStatusColor('approved')).toBe('success');
    expect(getRefundStatusColor('requested')).toBe('warning');
    expect(getRefundStatusColor('rejected')).toBe('danger');

    expect(getComplaintStatusColor('resolved')).toBe('success');
    expect(getComplaintStatusColor('reviewing')).toBe('warning');
    expect(getComplaintStatusColor('open')).toBe('danger');

    expect(getSlotStatusColor('open')).toBe('success');
    expect(getSlotStatusColor('booked')).toBe('accent');
  });
});

describe('opsHelpers - validation helpers', () => {
  it('validates refund request with orderless ceiling', () => {
    // Missing user ID or reason
    const r1 = validateRefundRequest({
      userId: 0,
      method: 'credit',
      creditCount: 5,
      reason: '',
    });
    expect(r1.valid).toBe(false);
    expect(r1.errors.userId).toBeDefined();
    expect(r1.errors.reason).toBeDefined();

    // Money refund with credit count supplied
    const r2 = validateRefundRequest({
      userId: 10,
      method: 'money',
      creditCount: 2,
      amount: 100000,
      reason: 'Issue with service',
    });
    expect(r2.valid).toBe(false);
    expect(r2.errors.creditCount).toBeDefined();

    // Orderless credit refund exceeding default ceiling
    const r3 = validateRefundRequest({
      userId: 10,
      method: 'credit',
      creditCount: DEFAULT_ORDERLESS_CEILING + 1,
      reason: 'Generous make-good',
      orderId: null,
    });
    expect(r3.valid).toBe(false);
    expect(r3.errors.creditCount).toContain(`at most ${DEFAULT_ORDERLESS_CEILING} credits`);

    // Valid order-linked money refund
    const r4 = validateRefundRequest({
      userId: 10,
      method: 'money',
      creditCount: 0,
      amount: 500000,
      reason: 'Agreed refund',
      orderId: 42,
    });
    expect(r4.valid).toBe(true);
    expect(r4.errors).toEqual({});
  });

  it('bounds the refund reason at the 400 characters the API accepts (#349)', () => {
    const base = { userId: 10, method: 'credit' as const, creditCount: 2 };

    expect(validateRefundRequest({ ...base, reason: 'a'.repeat(400) }).valid).toBe(true);

    const tooLong = validateRefundRequest({ ...base, reason: 'a'.repeat(401) });
    expect(tooLong.valid).toBe(false);
    expect(tooLong.errors.reason).toContain('400');
  });

  it('validates complaint resolve input, one ending at a time (#350)', () => {
    // Missing resolution or notes
    const c1 = validateComplaintResolve({
      resolution: '',
      internalNotes: '',
      compensation: 'none',
    });
    expect(c1.valid).toBe(false);
    expect(c1.errors.resolution).toBeDefined();
    expect(c1.errors.internalNotes).toBeDefined();

    const base = {
      resolution: 'Apologies for the delay.',
      internalNotes: 'Confirmed bug in system.',
      creditsSpent: 3,
      impliedRefundMethod: 'credit' as const,
    };

    // The ceiling is what was spent, not a constant: 4 is refused, 3 is accepted.
    const tooMuch = validateComplaintResolve({ ...base, compensation: 'make_good', grantCredits: 4 });
    expect(tooMuch.valid).toBe(false);
    expect(tooMuch.errors.grantCredits).toContain('at most 3 credit');

    const exact = validateComplaintResolve({ ...base, compensation: 'make_good', grantCredits: 3 });
    expect(exact.valid).toBe(true);
    expect(exact.errors).toEqual({});

    // A subject that spent nothing offers no make-good at all.
    const nothingSpent = validateComplaintResolve({
      ...base,
      compensation: 'make_good',
      grantCredits: 1,
      creditsSpent: 0,
    });
    expect(nothingSpent.valid).toBe(false);
    expect(nothingSpent.errors.compensation).toContain('never charged');

    // An order is money: a make-good in credits is the wrong currency.
    const moneySubject = validateComplaintResolve({
      ...base,
      compensation: 'make_good',
      grantCredits: 1,
      impliedRefundMethod: 'money',
      moneyPaid: 199000,
    });
    expect(moneySubject.valid).toBe(false);
    expect(moneySubject.errors.compensation).toContain('money');

    // A credit count under "no compensation" is refused: one ending owns the number.
    const strayCredits = validateComplaintResolve({
      ...base,
      compensation: 'none',
      grantCredits: 2,
    });
    expect(strayCredits.valid).toBe(false);
    expect(strayCredits.errors.grantCredits).toBeDefined();

    // The refund ending is valid on a charged subject and refused on an uncharged one.
    expect(validateComplaintResolve({ ...base, compensation: 'refund' }).valid).toBe(true);
    const uncharged = validateComplaintResolve({
      resolution: 'Handing to the refund desk.',
      internalNotes: 'Nothing was charged.',
      compensation: 'refund',
      creditsSpent: 0,
      moneyPaid: null,
    });
    expect(uncharged.valid).toBe(false);
    expect(uncharged.errors.compensation).toContain('Nothing to refund');
  });

  it('validates CSV export date range', () => {
    expect(validateCsvExportDates('2026-10-01', '2026-10-15').valid).toBe(true);
    expect(validateCsvExportDates('2026-10-15', '2026-10-01').valid).toBe(false);
    expect(validateCsvExportDates('invalid-date', '2026-10-01').valid).toBe(false);
    expect(validateCsvExportDates(undefined, undefined).valid).toBe(true);
  });

  it('validates weekly availability windows and rejects invalid or overlapping ranges on the same weekday', () => {
    // Multiple non-overlapping windows on Monday (dayOfWeek = 1) + Friday (dayOfWeek = 5)
    expect(
      validateWeeklySlots([
        { dayOfWeek: 1, startTime: '08:00', endTime: '09:00' },
        { dayOfWeek: 1, startTime: '14:00', endTime: '15:00' },
        { dayOfWeek: 5, startTime: '08:30', endTime: '09:30' },
      ]).valid,
    ).toBe(true);

    // End time not after start time
    const badRange = validateWeeklySlots([
      { dayOfWeek: 2, startTime: '15:00', endTime: '14:00' },
    ]);
    expect(badRange.valid).toBe(false);
    expect(badRange.error).toMatch(/after start time/i);

    // Overlapping windows on the same day of week
    const overlap = validateWeeklySlots([
      { dayOfWeek: 1, startTime: '08:00', endTime: '09:30' },
      { dayOfWeek: 1, startTime: '09:00', endTime: '10:00' },
    ]);
    expect(overlap.valid).toBe(false);
    expect(overlap.error).toMatch(/overlap/i);
  });

  it('computes default current month range in UTC+7', () => {
    const range = getDefaultUtc7MonthRange(new Date('2026-11-14T03:00:00Z'));
    expect(range).toEqual({
      from: '2026-11-01',
      to: '2026-11-14',
    });
  });
});
