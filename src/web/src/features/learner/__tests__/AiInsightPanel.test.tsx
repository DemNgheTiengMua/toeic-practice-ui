import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import { AiInsightPanel } from '../AiInsightPanel';
import type { AiInsightDto } from '../../../lib/api';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...props }: { to: string; children: React.ReactNode; [k: string]: unknown }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));

describe('AiInsightPanel', () => {
  afterEach(() => {
    cleanup();
  });

  it('renders trend chip, Vietnamese narrative, strengths/weaknesses, and prioritized recommendations linking to learning-path or practice', () => {
    const insight: AiInsightDto = {
      insightId: 10,
      userId: 1,
      certId: 2,
      scope: 'attempt',
      attemptId: 99,
      narrative: 'Bạn đang tiến bộ ổn định ở kỹ năng Reading (85%), cần tập trung cải thiện Listening Part 3 (40%).',
      trend: 'improving',
      status: 'done',
      sourceFingerprint: 'abc',
      createdAt: '2026-11-14T00:00:00Z',
      strengths: ['Reading comprehension (85%)'],
      weaknesses: ['Listening Part 3 inference (40%)'],
      recommendations: [
        {
          recId: 1,
          insightId: 10,
          text: 'Ôn lại bài học Listening Part 3 trong lộ trình',
          severity: 'major',
          targetBandCode: '7.0',
          isResolved: false,
          createdAt: '2026-11-14T00:00:00Z',
        },
        {
          recId: 2,
          insightId: 10,
          text: 'Luyện tập thêm câu hỏi từ vựng học thuật',
          severity: 'minor',
          targetBandCode: '6.5',
          isResolved: false,
          createdAt: '2026-11-14T00:00:00Z',
        },
        {
          recId: 3,
          insightId: 10,
          text: 'Duy trì tốc độ đọc hiểu hiện tại',
          severity: 'info',
          isResolved: false,
          createdAt: '2026-11-14T00:00:00Z',
        },
      ],
    };

    render(<AiInsightPanel insight={insight} scope="attempt" />);

    expect(screen.getByText('Improving')).toBeTruthy();
    expect(screen.getByText(/Bạn đang tiến bộ ổn định/)).toBeTruthy();
    expect(screen.getByText('Reading comprehension (85%)')).toBeTruthy();
    expect(screen.getByText('Listening Part 3 inference (40%)')).toBeTruthy();
    expect(screen.getByText('High')).toBeTruthy();
    expect(screen.getByText('Medium')).toBeTruthy();
    expect(screen.getByText('Low')).toBeTruthy();

    const links = screen.getAllByRole('link');
    expect(links.some((a) => a.getAttribute('href') === '/learning-path')).toBe(true);
    expect(links.some((a) => a.getAttribute('href') === '/practice')).toBe(true);
  });
});
