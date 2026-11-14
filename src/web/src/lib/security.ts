/**
 * Security, Link-Injection Guards & Staff Capability Predicates
 * (ADR-0015 / Tier2BoundaryCornerCaseTests / StaffGatesUseCapabilitiesTests)
 */

export const STAFF_PERMISSIONS = {
  ContentAuthor: 'pool.content.author',
  ContentReview: 'pool.content.review',
  BlueprintDesign: 'assessment.blueprint.design',
  SupportTriage: 'operations.support.triage',
  SystemAdmin: 'system.governance.admin',
} as const;

export interface CapabilitySubject {
  role?: string | null;
  permissions?: readonly string[] | null;
  held?: readonly string[] | null;
}

export function getHeldCapabilities(subject: CapabilitySubject | null | undefined): string[] {
  if (!subject) return [];
  return [...(subject.permissions ?? subject.held ?? [])];
}

export function hasCapability(
  subject: CapabilitySubject | null | undefined,
  capability: string,
): boolean {
  return getHeldCapabilities(subject).includes(capability);
}

export function isStaffSubject(subject: CapabilitySubject | null | undefined): boolean {
  return getHeldCapabilities(subject).length > 0;
}

export function canViewDashboard(subject: CapabilitySubject | null | undefined): boolean {
  return getHeldCapabilities(subject).length > 0;
}

export function canManageUsers(subject: CapabilitySubject | null | undefined): boolean {
  return hasCapability(subject, STAFF_PERMISSIONS.SystemAdmin);
}

export function canManageCatalog(subject: CapabilitySubject | null | undefined): boolean {
  return (
    hasCapability(subject, STAFF_PERMISSIONS.ContentAuthor) ||
    hasCapability(subject, STAFF_PERMISSIONS.ContentReview) ||
    hasCapability(subject, STAFF_PERMISSIONS.BlueprintDesign)
  );
}

export function canManageBilling(subject: CapabilitySubject | null | undefined): boolean {
  return (
    hasCapability(subject, STAFF_PERMISSIONS.SupportTriage) ||
    hasCapability(subject, STAFF_PERMISSIONS.SystemAdmin)
  );
}

export function canTriageSupport(subject: CapabilitySubject | null | undefined): boolean {
  return hasCapability(subject, STAFF_PERMISSIONS.SupportTriage);
}

export const VND_PER_CREDIT = 20_000;

export function creditsToVnd(credits: number): number {
  return Math.max(0, credits) * VND_PER_CREDIT;
}

export function isLocalPath(url: string | null | undefined): url is string {
  if (!url || typeof url !== 'string') return false;
  const trimmed = url.trim();
  if (!trimmed.startsWith('/')) return false;
  if (trimmed.startsWith('//') || trimmed.startsWith('/\\')) return false;
  if (/[\u0000-\u001F\u007F]/.test(trimmed)) return false;
  return true;
}

export function resolveSafeReturnUrl(
  returnUrl: string | null | undefined,
  fallback = '/dashboard',
): string {
  return isLocalPath(returnUrl) ? returnUrl.trim() : fallback;
}

export function safeExternalHref(url: string | null | undefined): string | null {
  if (!url || typeof url !== 'string') return null;
  try {
    const parsed = new URL(url.trim());
    if (parsed.protocol === 'https:' || parsed.protocol === 'http:') {
      return parsed.toString();
    }
    return null;
  } catch {
    return null;
  }
}

export function toBrowserMediaUrl(rawPath: string | null | undefined): string | null {
  if (!rawPath || typeof rawPath !== 'string') return null;
  const trimmed = rawPath.trim();
  if (!trimmed) return null;
  if (trimmed.startsWith('/api/media/')) {
    return `/media/${trimmed.slice('/api/media/'.length)}`;
  }
  if (trimmed.startsWith('api/media/')) {
    return `/media/${trimmed.slice('api/media/'.length)}`;
  }
  if (trimmed.startsWith('/media/')) {
    return trimmed;
  }
  if (trimmed.startsWith('media/')) {
    return `/${trimmed}`;
  }
  if (trimmed.startsWith('imports/') || trimmed.startsWith('exams/')) {
    return `/media/${trimmed}`;
  }
  if (trimmed.startsWith('/imports/') || trimmed.startsWith('/exams/')) {
    return `/media${trimmed}`;
  }
  return trimmed.startsWith('/') ? trimmed : `/${trimmed}`;
}

export interface NarrativeSegment {
  type: 'text' | 'bold';
  value: string;
}

/**
 * Parses **bold** markers into safe React-renderable segments without ever
 * interpreting raw HTML tags (<script>, <iframe>, etc. remain literal text).
 */
export function parseSafeInlineSegments(text: string): NarrativeSegment[] {
  if (!text) return [];
  const segments: NarrativeSegment[] = [];
  const regex = /\*\*([^*]+)\*\*/g;
  let lastIndex = 0;
  let match: RegExpExecArray | null;

  while ((match = regex.exec(text)) !== null) {
    if (match.index > lastIndex) {
      segments.push({ type: 'text', value: text.slice(lastIndex, match.index) });
    }
    segments.push({ type: 'bold', value: match[1] });
    lastIndex = regex.lastIndex;
  }

  if (lastIndex < text.length) {
    segments.push({ type: 'text', value: text.slice(lastIndex) });
  }

  return segments;
}

export function formatVnd(amount: number): string {
  return `${new Intl.NumberFormat('vi-VN').format(amount)} ₫`;
}

export function formatVndUnitPrice(price: number, credits: number): string {
  if (credits <= 0) {
    return `${formatVnd(price)}/credit`;
  }
  const unitPrice = Math.round(price / credits);
  return `${formatVnd(unitPrice)}/credit`;
}

/** Formats net revenue cleanly using a true minus sign for negative figures (ADR-0014). */
export function formatSignedVnd(amount: number): string {
  const formatted = new Intl.NumberFormat('vi-VN').format(Math.abs(amount));
  if (amount < 0) {
    return `\u2212${formatted} ₫`;
  }
  return `${formatted} ₫`;
}

export function formatTimerSeconds(totalSeconds: number): string {
  const clamped = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(clamped / 3600);
  const minutes = Math.floor((clamped % 3600) / 60);
  const seconds = clamped % 60;
  const mm = String(minutes).padStart(2, '0');
  const ss = String(seconds).padStart(2, '0');
  if (hours > 0) {
    return `${String(hours).padStart(2, '0')}:${mm}:${ss}`;
  }
  return `${mm}:${ss}`;
}

export function countWords(text: string): number {
  const trimmed = text.trim();
  if (!trimmed) return 0;
  return trimmed.split(/\s+/).length;
}
