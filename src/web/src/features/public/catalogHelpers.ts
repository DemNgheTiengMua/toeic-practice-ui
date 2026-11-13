import type { CertificateBandDto, CertificateDto, CertificateSkillDto } from '../../lib/api';

function startsWithCode(code: string, name: string): boolean {
  if (!name.toLowerCase().startsWith(code.toLowerCase())) {
    return false;
  }
  if (name.length === code.length) {
    return true;
  }
  return !/[a-z0-9]/i.test(name[code.length]);
}

export function formatCertificateHeading(cert: Pick<CertificateDto, 'code' | 'name'>): string {
  return startsWithCode(cert.code, cert.name) ? cert.name : `${cert.code} — ${cert.name}`;
}

export function getSortedBands(bands: readonly CertificateBandDto[]): CertificateBandDto[] {
  return [...bands].sort((a, b) => a.minTotal - b.minTotal);
}

export function computeBandColumnTemplate(bands: readonly CertificateBandDto[]): string {
  if (bands.length === 0) return 'none';
  return bands.map((b) => `${Math.max(1, b.maxTotal - b.minTotal)}fr`).join(' ');
}

export function getSkillQuestionCount(skill: CertificateSkillDto): number {
  return skill.sections.reduce((total, sec) => total + (sec.expectedQuestionCount ?? 0), 0);
}

export function getCertificateScoreRange(cert: CertificateDto): string {
  const sorted = getSortedBands(cert.bands);
  if (sorted.length === 0) return '';
  return `${sorted[0].minTotal}–${sorted[sorted.length - 1].maxTotal}`;
}
