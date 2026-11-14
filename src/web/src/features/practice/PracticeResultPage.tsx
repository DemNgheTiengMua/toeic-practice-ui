import React, { useMemo, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Button, Card, Chip, Spinner, Table } from '@heroui/react';
import {
  ArrowLeft,
  Bookmark,
  CheckCircle2,
  Lightbulb,
  RefreshCw,
  RotateCcw,
  Sparkles,
  XCircle,
} from 'lucide-react';
import { api, QuestionOutcomeDto } from '@/lib/api';
import { PracticeAudioPane } from './PracticeAudioPane';
import {
  formatInsightTrendLabel,
  formatLocalReopenTime,
  getPracticeErrorMessage,
  parseAcceptedAnswers,
} from './practiceUtils';

type OutcomeFilter = 'all' | 'incorrect' | 'skipped';

function formatLearnerAnswer(q: QuestionOutcomeDto): string {
  if (q.selectedOptionLabel || q.selectedOptionText) {
    return [q.selectedOptionLabel, q.selectedOptionText].filter(Boolean).join('. ');
  }
  if (q.responseText) return q.responseText;
  if (q.selectedOptionIdsJson) return q.selectedOptionIdsJson;
  return 'Skipped';
}

function formatExpectedAnswer(q: QuestionOutcomeDto): string {
  if (q.correctOptionLabel || q.correctOptionText) {
    return [q.correctOptionLabel, q.correctOptionText].filter(Boolean).join('. ');
  }
  const accepted = parseAcceptedAnswers(q.acceptedAnswersJson);
  if (accepted.length > 0) return accepted.join(', ');
  return '—';
}

export function PracticeResultPage() {
  const { sessionId } = useParams({ strict: false }) as { sessionId?: string };
  const numericSessionId = Number(sessionId);
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<OutcomeFilter>('all');

  const summaryQuery = useQuery({
    queryKey: ['practice', 'summary', numericSessionId],
    queryFn: () => api.practice.summary(numericSessionId),
    enabled: Number.isInteger(numericSessionId) && numericSessionId > 0,
  });

  const insightQuery = useQuery({
    queryKey: ['practice', 'insight', numericSessionId],
    queryFn: () => api.practice.insight(numericSessionId),
    enabled: Number.isInteger(numericSessionId) && numericSessionId > 0,
    retry: false,
  });

  const retryInsightMutation = useMutation({
    mutationFn: () => api.practice.retryInsight(numericSessionId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['practice', 'insight', numericSessionId] });
    },
  });

  const filteredQuestions = useMemo(() => {
    const rows = summaryQuery.data?.questions ?? [];
    if (filter === 'incorrect') return rows.filter((q) => q.isCorrect === false);
    if (filter === 'skipped') return rows.filter((q) => q.isCorrect == null);
    return rows;
  }, [summaryQuery.data, filter]);

  if (summaryQuery.isLoading) {
    return (
      <Card className="p-8">
        <div className="flex items-center justify-center gap-3 text-sm text-muted">
          <Spinner size="sm" />
          <span>Loading session results…</span>
        </div>
      </Card>
    );
  }

  if (summaryQuery.isError || !summaryQuery.data) {
    return (
      <Alert status="danger">
        <Alert.Indicator />
        <Alert.Content>
          <Alert.Title>Unable to load practice summary</Alert.Title>
          <Alert.Description>
            {getPracticeErrorMessage(summaryQuery.error, 'Summary not available.')}
          </Alert.Description>
          <div className="mt-3">
            <Link to="/practice" className="no-underline">
              <Button size="sm" variant="secondary">
                Back to practice drills
              </Button>
            </Link>
          </div>
        </Alert.Content>
      </Alert>
    );
  }

  const summary = summaryQuery.data;
  const insight = insightQuery.data;
  const startedLabel = formatLocalReopenTime(summary.startedAt);
  const finishedLabel = formatLocalReopenTime(summary.finishedAt);

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-separator pb-4">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <Link to="/practice" className="no-underline">
              <Button size="sm" variant="ghost">
                <ArrowLeft className="h-4 w-4" aria-hidden="true" />
                <span>Practice drills</span>
              </Button>
            </Link>
            <h1 className="font-display text-2xl font-bold text-foreground">
              {summary.sectionName}
            </h1>
            <Chip size="sm" color="success" variant="soft">
              <Chip.Label className="font-mono-tabular">0 credits</Chip.Label>
            </Chip>
          </div>
          <p className="text-sm text-muted">
            Session #{summary.sessionId}
            {startedLabel ? ` · Started ${startedLabel}` : ''}
            {finishedLabel ? ` · Finished ${finishedLabel}` : ''}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Link to="/saved-questions" className="no-underline">
            <Button size="sm" variant="secondary">
              <Bookmark className="h-4 w-4" aria-hidden="true" />
              <span>Saved questions</span>
            </Button>
          </Link>
          <Link to="/practice" className="no-underline">
            <Button size="sm" variant="primary">
              <RotateCcw className="h-4 w-4" aria-hidden="true" />
              <span>Practice again</span>
            </Button>
          </Link>
        </div>
      </header>

      <section className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <Card className="border border-border bg-surface">
          <Card.Header className="pb-1">
            <Card.Description>Accuracy</Card.Description>
            <Card.Title className="font-mono-tabular text-2xl text-accent">
              {summary.accuracyPercentage}%
            </Card.Title>
          </Card.Header>
          <Card.Content className="pt-0 text-xs text-muted font-mono-tabular">
            {summary.answeredCount}/{summary.totalQuestions} answered
          </Card.Content>
        </Card>

        <Card className="border border-border bg-surface">
          <Card.Header className="pb-1">
            <Card.Description>Correct</Card.Description>
            <Card.Title className="font-mono-tabular text-2xl text-success">
              {summary.correctCount}
            </Card.Title>
          </Card.Header>
        </Card>

        <Card className="border border-border bg-surface">
          <Card.Header className="pb-1">
            <Card.Description>Incorrect</Card.Description>
            <Card.Title className="font-mono-tabular text-2xl text-danger">
              {summary.incorrectCount}
            </Card.Title>
          </Card.Header>
        </Card>

        <Card className="border border-border bg-surface">
          <Card.Header className="pb-1">
            <Card.Description>Skipped</Card.Description>
            <Card.Title className="font-mono-tabular text-2xl text-warning-foreground">
              {summary.skippedCount}
            </Card.Title>
          </Card.Header>
        </Card>

        <Card className="border border-border bg-surface">
          <Card.Header className="pb-1">
            <Card.Description>Hints used</Card.Description>
            <Card.Title className="font-mono-tabular text-2xl text-foreground">
              {summary.hintUsedCount}
            </Card.Title>
          </Card.Header>
          <Card.Content className="pt-0 text-xs text-muted font-mono-tabular">
            {summary.correctWithHintCount} correct · {summary.incorrectWithHintCount} incorrect
          </Card.Content>
        </Card>
      </section>

      <Card className="border border-border bg-surface">
        <Card.Header className="flex flex-row flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-accent" aria-hidden="true" />
            <Card.Title className="text-base">AI practice insight</Card.Title>
          </div>
          {insight?.trend && (
            <Chip
              size="sm"
              variant="soft"
              color={
                insight.trend === 'improving'
                  ? 'success'
                  : insight.trend === 'declining'
                    ? 'danger'
                    : 'default'
              }
            >
              <Chip.Label>{formatInsightTrendLabel(insight.trend)}</Chip.Label>
            </Chip>
          )}
        </Card.Header>
        <Card.Content className="space-y-3">
          {insightQuery.isLoading ? (
            <div className="flex items-center gap-2 text-sm text-muted">
              <Spinner size="sm" />
              <span>Analyzing session performance…</span>
            </div>
          ) : insightQuery.isError ? (
            <Alert status="warning">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Title>Insight unavailable</Alert.Title>
                <Alert.Description>
                  {getPracticeErrorMessage(insightQuery.error, 'AI insight could not be retrieved right now.')}
                </Alert.Description>
              </Alert.Content>
            </Alert>
          ) : insight?.status === 'failed' ? (
            <Alert status="warning">
              <Alert.Indicator />
              <Alert.Content className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <Alert.Title>Insight generation failed</Alert.Title>
                  <Alert.Description>
                    {insight.failureReason || 'The AI grading service encountered a temporary error.'}
                  </Alert.Description>
                </div>
                <Button
                  size="sm"
                  variant="secondary"
                  isDisabled={retryInsightMutation.isPending}
                  onPress={() => retryInsightMutation.mutate()}
                >
                  <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>{retryInsightMutation.isPending ? 'Retrying…' : 'Retry insight'}</span>
                </Button>
              </Alert.Content>
            </Alert>
          ) : insight?.status === 'pending' ? (
            <div className="flex flex-wrap items-center justify-between gap-3 text-sm text-muted">
              <span>AI insight is still generating for this session.</span>
              <Button size="sm" variant="secondary" onPress={() => void insightQuery.refetch()}>
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Refresh</span>
              </Button>
            </div>
          ) : (
            <div className="space-y-3">
              <p className="text-sm leading-relaxed text-foreground">
                {insight?.formattedNarrative || insight?.narrative || 'No narrative recorded yet.'}
              </p>
              {insight?.recommendations && insight.recommendations.length > 0 && (
                <ul className="space-y-1.5">
                  {insight.recommendations.map((rec) => (
                    <li
                      key={rec.recId}
                      className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-surface-secondary px-3 py-2 text-sm"
                    >
                      <span>{rec.text}</span>
                      {rec.skillName && (
                        <Chip size="sm" variant="soft">
                          <Chip.Label>{rec.skillName}</Chip.Label>
                        </Chip>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </Card.Content>
      </Card>

      <section className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="font-display text-lg font-semibold text-foreground">
            Question breakdown
          </h2>
          <div className="flex items-center gap-1.5">
            {(['all', 'incorrect', 'skipped'] as const).map((mode) => (
              <Button
                key={mode}
                size="sm"
                variant={filter === mode ? 'primary' : 'secondary'}
                onPress={() => setFilter(mode)}
              >
                <span className="capitalize">{mode}</span>
              </Button>
            ))}
          </div>
        </div>

        <Card className="border border-border bg-surface">
          <Table>
            <Table.ScrollContainer>
              <Table.Content aria-label="Practice session question breakdown">
                <Table.Header>
                  <Table.Column isRowHeader>#</Table.Column>
                  <Table.Column>Question & explanation</Table.Column>
                  <Table.Column>Your answer</Table.Column>
                  <Table.Column>Correct answer</Table.Column>
                  <Table.Column>Outcome</Table.Column>
                </Table.Header>
                <Table.Body>
                  {filteredQuestions.map((q) => (
                    <Table.Row key={q.displayOrder}>
                      <Table.Cell className="align-top font-mono-tabular">
                        Q{q.displayOrder}
                      </Table.Cell>
                      <Table.Cell className="max-w-xl space-y-2">
                        <div className="flex flex-wrap items-center gap-2">
                          {q.categoryName && (
                            <Chip size="sm" variant="soft" color="accent">
                              <Chip.Label>{q.categoryName}</Chip.Label>
                            </Chip>
                          )}
                          {q.hintUsed && (
                            <Chip size="sm" variant="soft" color="warning">
                              <Chip.Label className="inline-flex items-center gap-1">
                                <Lightbulb className="h-3 w-3" aria-hidden="true" />
                                Hint used
                              </Chip.Label>
                            </Chip>
                          )}
                        </div>
                        <p className="font-medium text-foreground">{q.stem}</p>
                        {(q.audioPath || q.transcript) && (
                          <PracticeAudioPane
                            compact
                            audioPath={q.audioPath}
                            audioStartMs={q.audioStartMs}
                            audioEndMs={q.audioEndMs}
                            transcript={q.transcript}
                          />
                        )}
                        {q.explanation && (
                          <p className="rounded-md bg-surface-secondary p-2.5 text-xs leading-relaxed text-muted">
                            <strong className="text-foreground">Explanation:</strong> {q.explanation}
                          </p>
                        )}
                      </Table.Cell>
                      <Table.Cell className="align-top text-sm">
                        {formatLearnerAnswer(q)}
                      </Table.Cell>
                      <Table.Cell className="align-top text-sm font-medium text-foreground">
                        {formatExpectedAnswer(q)}
                      </Table.Cell>
                      <Table.Cell className="align-top">
                        {q.isCorrect === true ? (
                          <Chip size="sm" color="success" variant="soft">
                            <Chip.Label className="inline-flex items-center gap-1">
                              <CheckCircle2 className="h-3 w-3" aria-hidden="true" />
                              Correct
                            </Chip.Label>
                          </Chip>
                        ) : q.isCorrect === false ? (
                          <Chip size="sm" color="danger" variant="soft">
                            <Chip.Label className="inline-flex items-center gap-1">
                              <XCircle className="h-3 w-3" aria-hidden="true" />
                              Incorrect
                            </Chip.Label>
                          </Chip>
                        ) : (
                          <Chip size="sm" color="warning" variant="soft">
                            <Chip.Label>Skipped</Chip.Label>
                          </Chip>
                        )}
                      </Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Content>
            </Table.ScrollContainer>
          </Table>
        </Card>
      </section>
    </div>
  );
}
