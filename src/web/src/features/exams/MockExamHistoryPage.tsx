import { useEffect, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner, Table } from '@heroui/react';
import {
  Award,
  History,
  Play,
} from 'lucide-react';
import {
  api,
  type AttemptSkillScoreDto,
  type ExamHistoryDto,
  type ExamHistoryItemDto,
} from '@/lib/api';

function formatDate(isoString: string): string {
  try {
    const d = new Date(isoString);
    return d.toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return isoString;
  }
}

export function MockExamHistoryPage() {
  const navigate = useNavigate();
  const [history, setHistory] = useState<ExamHistoryDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    api.exams
      .history()
      .then((data) => {
        if (!cancelled) {
          setHistory(data);
          setError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load exam history.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  if (loading) {
    return (
      <div className="mx-auto max-w-5xl p-6 flex items-center justify-center gap-3 text-muted min-h-[50vh]">
        <Spinner size="md" color="accent" />
        <span>Loading exam history...</span>
      </div>
    );
  }

  if (error && !history) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to load history</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
        <div className="mt-4">
          <Button variant="secondary" size="sm" onClick={() => void navigate({ to: '/exams' })}>
            Back to mock exams
          </Button>
        </div>
      </div>
    );
  }

  if (!history) return null;

  return (
    <div className="mx-auto max-w-5xl p-4 sm:p-6 space-y-6">
      {/* Breadcrumbs */}
      <nav className="flex items-center gap-2 text-xs text-muted" aria-label="Breadcrumb">
        <Link to="/dashboard" className="hover:text-foreground transition-colors">
          Home
        </Link>
        <span aria-hidden="true">/</span>
        <Link to="/exams" className="hover:text-foreground transition-colors">
          Mock exams
        </Link>
        <span aria-hidden="true">/</span>
        <span className="text-foreground font-medium" aria-current="page">
          Exam history
        </span>
      </nav>

      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">Exam history</h1>
          <p className="text-sm text-muted mt-1">
            Track your mock exam attempts and score progression for {history.certificate.name}.
          </p>
        </div>

        <Button
          variant="primary"
          size="sm"
          onClick={() => void navigate({ to: '/exams' })}
        >
          <Play className="size-4 mr-1.5" />
          Start new exam
        </Button>
      </div>

      {/* Empty State */}
      {history.attempts.length === 0 ? (
        <Card className="p-8 text-center bg-surface border-border">
          <Card.Content className="flex flex-col items-center gap-3">
            <div className="p-3 rounded-full bg-surface-secondary text-muted">
              <History className="size-8" />
            </div>
            <h2 className="text-lg font-semibold text-foreground">No mock exams taken yet</h2>
            <p className="text-sm text-muted max-w-md">
              Take your first mock exam to begin tracking your scaled score progression and your
              standing on {history.certificate.name}&apos;s scale.
            </p>
            <Button
              variant="primary"
              size="sm"
              className="mt-2"
              onClick={() => void navigate({ to: '/exams' })}
            >
              <Play className="size-4 mr-1.5" />
              Browse mock exams
            </Button>
          </Card.Content>
        </Card>
      ) : (
        <>
          {/* Aggregate Stats Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <Card className="bg-surface border-border shadow-surface">
              <Card.Content className="p-4 space-y-1">
                <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                  Exams taken
                </span>
                <div className="text-3xl font-extrabold font-mono-tabular text-foreground">
                  {history.totalAttempts}
                </div>
              </Card.Content>
            </Card>

            <Card className="bg-surface border-border shadow-surface">
              <Card.Content className="p-4 space-y-1">
                <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                  Best score
                </span>
                <div className="text-3xl font-extrabold font-mono-tabular text-accent">
                  {history.bestScore != null ? history.bestScore : '--'}
                </div>
                {history.bestScoreBandName && (
                  <div className="text-xs text-muted font-medium">
                    Band: {history.bestScoreBandName}
                  </div>
                )}
              </Card.Content>
            </Card>

            <Card className="bg-surface border-border shadow-surface">
              <Card.Content className="p-4 space-y-1">
                <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                  Average score
                </span>
                <div className="text-3xl font-extrabold font-mono-tabular text-foreground">
                  {history.averageScore != null ? history.averageScore : '--'}
                </div>
              </Card.Content>
            </Card>
          </div>

          {/* Best Band Placard on Certificate Scale */}
          {history.bestScoreBandCode && (
            <Card className="bg-surface border-border">
              <Card.Header className="pb-2">
                <Card.Title className="text-sm font-semibold flex items-center gap-2">
                  <Award className="size-4 text-accent" />
                  Best standing on {history.certificate.name}&apos;s scale
                </Card.Title>
              </Card.Header>
              <Card.Content className="space-y-1">
                <div className="text-lg font-bold text-foreground">
                  {history.bestScoreBandName || history.bestScoreBandCode}
                </div>
                <p className="text-xs text-muted">
                  Referenced from your top score ({history.bestScore} points) against{' '}
                  {history.certificate.name}&apos;s band thresholds.
                </p>
              </Card.Content>
            </Card>
          )}

          {/* Past Attempts Table */}
          <div className="space-y-3">
            <h2 className="text-base font-semibold text-foreground">
              Past attempts ({history.attempts.length})
            </h2>

            <Card className="bg-surface border-border overflow-hidden">
              <Table.ScrollContainer>
                <Table.Content aria-label="Mock exam attempts history">
                  <Table.Header>
                    <Table.Column isRowHeader>Date</Table.Column>
                    <Table.Column>Exam</Table.Column>
                    <Table.Column>Score</Table.Column>
                    <Table.Column>Band</Table.Column>
                    <Table.Column>Skills</Table.Column>
                    <Table.Column>Actions</Table.Column>
                  </Table.Header>
                  <Table.Body>
                    {history.attempts.map((attempt: ExamHistoryItemDto) => (
                      <Table.Row key={attempt.attemptId}>
                        <Table.Cell className="text-xs text-muted font-mono-tabular whitespace-nowrap">
                          {formatDate(attempt.submittedAt)}
                        </Table.Cell>
                        <Table.Cell className="font-medium text-foreground text-sm">
                          {attempt.examName}
                        </Table.Cell>
                        <Table.Cell className="font-mono-tabular font-bold text-accent text-sm">
                          {attempt.totalScore}
                        </Table.Cell>
                        <Table.Cell className="text-xs text-muted">
                          {attempt.bandName || attempt.bandCode || '--'}
                        </Table.Cell>
                        <Table.Cell className="text-xs">
                          <div className="flex flex-wrap items-center gap-1.5">
                            {attempt.skillScores.map((s: AttemptSkillScoreDto) => (
                              <Chip
                                key={s.skillId}
                                size="sm"
                                variant="secondary"
                                className="text-[11px] font-mono-tabular py-0"
                              >
                                {s.skillCode}: {s.scaledScore}
                              </Chip>
                            ))}
                          </div>
                        </Table.Cell>
                        <Table.Cell>
                          <div className="flex items-center gap-2">
                            <Button
                              size="sm"
                              variant="secondary"
                              onClick={() =>
                                void navigate({
                                  to: '/exams/result/$attemptId',
                                  params: { attemptId: String(attempt.attemptId) },
                                })
                              }
                              className="text-xs h-7 px-2.5"
                            >
                              Result
                            </Button>
                            <Button
                              size="sm"
                              variant="secondary"
                              onClick={() =>
                                void navigate({
                                  to: '/exams/review/$attemptId',
                                  params: { attemptId: String(attempt.attemptId) },
                                })
                              }
                              className="text-xs h-7 px-2.5 text-muted hover:text-foreground"
                            >
                              Review
                            </Button>
                          </div>
                        </Table.Cell>
                      </Table.Row>
                    ))}
                  </Table.Body>
                </Table.Content>
              </Table.ScrollContainer>
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
