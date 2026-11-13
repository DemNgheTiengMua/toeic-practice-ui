import type { DashboardRevision } from '../../lib/api';
import { STAFF_PERMISSIONS } from '../../lib/security';

export interface StaffCapabilityMeta {
  code: string;
  name: string;
  description: string;
  area: string;
}

export const STAFF_CAPABILITY_CATALOG: StaffCapabilityMeta[] = [
  {
    code: STAFF_PERMISSIONS.SystemAdmin,
    name: 'System Governance Admin',
    description: 'System governance, user account management, lock/unlock, role assignments, and manual credit grants.',
    area: 'Governance',
  },
  {
    code: STAFF_PERMISSIONS.ContentAuthor,
    name: 'Content Author',
    description: 'Authoring and drafting items, passages, audio stimuli, and batch uploads.',
    area: 'Catalog',
  },
  {
    code: STAFF_PERMISSIONS.ContentReview,
    name: 'Content Review',
    description: 'Stratified item audit, blind solving, item verification/rejection, and catalog batch promotion.',
    area: 'Catalog & Audit',
  },
  {
    code: STAFF_PERMISSIONS.BlueprintDesign,
    name: 'Blueprint Design',
    description: 'Certificate structures, exam blueprints, CEFR score conversion tables, and rubrics.',
    area: 'Assessment',
  },
  {
    code: STAFF_PERMISSIONS.SupportTriage,
    name: 'Support & Operations Triage',
    description: 'Order inquiries, manual payment reconciliation, dispute/complaint resolution, and refund proposals.',
    area: 'Operations & Finance',
  },
];

export function areRevisionsEqual(
  a: DashboardRevision | null | undefined,
  b: DashboardRevision | null | undefined,
): boolean {
  if (!a || !b) return false;
  return (
    a.paidOrdersCount === b.paidOrdersCount &&
    a.paidOrdersAmount === b.paidOrdersAmount &&
    a.complaintsOpen === b.complaintsOpen &&
    a.complaintsReviewing === b.complaintsReviewing &&
    a.complaintsResolved === b.complaintsResolved &&
    a.refundsRequested === b.refundsRequested &&
    a.refundsApproved === b.refundsApproved &&
    a.refundsSettled === b.refundsSettled &&
    a.refundsRejected === b.refundsRejected &&
    a.settledMoneyRefundsCount === b.settledMoneyRefundsCount &&
    a.settledMoneyRefundsAmount === b.settledMoneyRefundsAmount
  );
}

export function formatPlacementStatusLabel(status: string | null | undefined): string {
  if (!status) return 'Unknown';
  switch (status.toLowerCase()) {
    case 'in_progress':
      return 'In progress';
    case 'done':
      return 'Done';
    case 'abandoned':
      return 'Abandoned';
    default:
      return status;
  }
}

export function formatPlacementScopeLabel(scope: string | null | undefined): string {
  if (!scope) return 'Standard';
  switch (scope.toLowerCase()) {
    case 'all_skills':
      return 'All 4 skills';
    case 'listening_and_reading':
      return 'Listening & Reading';
    default:
      return scope;
  }
}

export function formatAuditStratumStatusLabel(status: string | null | undefined): string {
  if (!status) return 'Pending';
  switch (status.toLowerCase()) {
    case 'pending':
      return 'Pending review';
    case 'approved':
      return 'Approved';
    case 'quarantined':
      return 'Quarantined';
    default:
      return status;
  }
}

export function buildAuditPromotionSentence(
  sampledPromoted: number,
  unsampledPromoted: number,
  quarantined: number,
): string {
  return `Promoted ${sampledPromoted} reviewed and ${unsampledPromoted} unsampled items to active catalog (${quarantined} quarantined).`;
}

export function validateCreditAdjustment(
  delta: number,
  reason: string,
): { valid: boolean; error?: string } {
  if (isNaN(delta) || delta === 0) {
    return { valid: false, error: 'Credit delta cannot be zero.' };
  }
  const trimmed = reason.trim();
  if (!trimmed) {
    return { valid: false, error: 'Reason is required for audit ledger integrity.' };
  }
  if (trimmed.length > 500) {
    return { valid: false, error: 'Reason cannot exceed 500 characters.' };
  }
  return { valid: true };
}
