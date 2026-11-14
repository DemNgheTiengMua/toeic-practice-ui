import { useCallback, useEffect, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Label, ProgressBar, Spinner } from '@heroui/react';
import { ArrowLeft, Award, RefreshCw } from 'lucide-react';
import { api } from '@/lib/api';
import { ProductiveAiFeedback, SafeAnnotatedText } from '../ProductiveAiFeedback';
import {
  formatWritingAttemptStatusLabel,
  formatWritingGradingStatusLabel,
  type WritingGradingPending,
  type WritingReview,
} from './types';

interface WritingResultPageProps {
  attemptId: number;
  pollIntervalMs?: number;
}

function isGradingComplete(pending: WritingGradingPending): boolean {
  if (pending.attemptStatus === 'graded') return true;
  return (
    pending.failedResponses === 0 &&
    pending.pendingResponses === 0 &&
    pending.gradedResponses > 0 &&
    pending.attemptStatus !== 'queued' &&
    pending.attemptStatus !== 'processing' &&
    pending.attemptStatus !== 'submitted'
  );
}

export function WritingResultPage({ attemptId, pollIntervalMs = 2500 }: WritingResultPageProps) {
  const [pending, setPending] = useState<WritingGradingPending | null>(null);
  const [review, setReview] = useState<WritingReview | null>(null);
  const [loading, setLoading] = useState(true);
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchStatus = useCallback(async () => {
    try {
      const status = (await api.writing.pending(attemptId)) as WritingGradingPending;
      setPending(status);
      if (isGradingComplete(status)) {
        const result = (await api.writing.review(attemptId)) as WritingReview;
        setReview(result);
      }
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to check writing grading status.');
    } finally {
      setLoading(false);
    }
  }, [attemptId]);

  useEffect(() => {
    void fetchStatus();
  }, [fetchStatus]);

  useEffect(() => {
    if (!pending || review) return;
    const hasFailure =
      pending.failedResponses > 0 || pending.canRetry || pending.attemptStatus === 'failed';
    if (hasFailure || isGradingComplete(pending)) return;

    const timer = window.setTimeout(() => {
      void fetchStatus();
    }, pollIntervalMs);

    return () => window.clearTimeout(timer);
  }, [fetchStatus, pending, pollIntervalMs, review]);

  const handleRetry = async () => {
    setRetrying(true);
    setError(null);
    try {
      await api.writing.retry(attemptId);
      await fetchStatus();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to retry writing grading.');
    } finally {
      setRetrying(false);
    }
  };

  if (loading && !pending && !review) {
    return (
      <div className="flex min-h-[280px] items-center justify-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Loading writing result...</span>
      </div>
    );
  }

  if (error && !pending && !review) {
    return (
      <div className="space-y-4">
        <Alert status="danger" role="alert">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to load writing result</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
        <Link to="/writing" className="text-sm font-medium text-accent underline">
          Back to writing studio
        </Link>
      </div>
    );
  }

  if (!review && pending) {
    const hasFailure =
      pending.failedResponses > 0 || pending.canRetry || pending.attemptStatus === 'failed';
    const progressPercent =
      pending.totalResponses > 0
        ? Math.round((pending.gradedResponses / pending.totalResponses) * 100)
        : 0;

    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <div className="flex items-center justify-between gap-3">
          <div>
            <Chip color="accent" variant="soft" size="sm">
              {pending.certCode}
            </Chip>
            <h1 className="mt-1 font-display text-2xl font-semibold text-foreground">
              {pending.examName}
            </h1>
          </div>
          <Link to="/writing" className="inline-flex items-center gap-1.5 text-sm font-medium text-accent hover:underline">
            <ArrowLeft className="size-4" aria-hidden="true" />
            Writing studio
          </Link>
        </div>

        {hasFailure ? (
          <Alert status="danger" role="alert">
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Title>AI grading paused on an error</Alert.Title>
              <Alert.Description className="space-y-3">
                <p>
                  {pending.failureReason ??
                    'Your essays are saved safely, but automated grading encountered an issue. Retrying costs no extra credits.'}
                </p>
                <Button variant="primary" size="sm" isPending={retrying} onPress={() => void handleRetry()}>
                  <RefreshCw className="size-3.5" aria-hidden="true" />
                  Retry grading
                </Button>
              </Alert.Description>
            </Alert.Content>
          </Alert>
        ) : (
          <Card>
            <Card.Header className="flex flex-row items-center justify-between">
              <div className="flex items-center gap-2">
                <Spinner size="sm" color="accent" />
                <Card.Title className="text-base">AI grading in progress</Card.Title>
              </div>
              <Chip color="accent" variant="soft" size="sm">
                {formatWritingAttemptStatusLabel(pending.attemptStatus)}
              </Chip>
            </Card.Header>
            <Card.Content className="space-y-4">
              <ProgressBar value={progressPercent} maxValue={100} color="accent">
                <Label>Scoring written responses</Label>
                <ProgressBar.Output>
                  {pending.gradedResponses} / {pending.totalResponses} tasks graded
                </ProgressBar.Output>
                <ProgressBar.Track>
                  <ProgressBar.Fill />
                </ProgressBar.Track>
              </ProgressBar>

              <div className="divide-y divide-border rounded-lg border border-border">
                {pending.items.map((item) => (
                  <div key={item.freeResponseId} className="flex items-center justify-between px-3 py-2 text-sm">
                    <span className="font-medium text-foreground">Task #{item.displayOrder}</span>
                    <Chip
                      size="sm"
                      variant="soft"
                      color={item.status === 'done' ? 'success' : item.status === 'failed' ? 'danger' : 'accent'}
                    >
                      {formatWritingGradingStatusLabel(item.status)}
                    </Chip>
                  </div>
                ))}
              </div>
            </Card.Content>
          </Card>
        )}
      </div>
    );
  }

  if (!review) return null;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <Chip color="accent" variant="soft" size="sm">
              {review.certCode}
            </Chip>
            <Chip color="success" variant="soft" size="sm">
              Graded
            </Chip>
          </div>
          <h1 className="font-display text-2xl font-semibold text-foreground">{review.examName}</h1>
          <p className="text-sm text-muted">
            {review.answeredQuestions} of {review.totalQuestions} tasks submitted •{' '}
            <span className="font-mono-tabular">{review.gradedQuestions}</span> graded
          </p>
        </div>

        <Link to="/writing" className="inline-flex items-center gap-1.5 text-sm font-medium text-accent hover:underline">
          <ArrowLeft className="size-4" aria-hidden="true" />
          Back to writing studio
        </Link>
      </header>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <Card.Header>
            <Card.Title className="text-sm text-muted">Total writing score</Card.Title>
          </Card.Header>
          <Card.Content>
            <div className="flex items-baseline gap-1.5 font-mono-tabular">
              <span className="text-3xl font-bold text-accent">{review.totalScore}</span>
              <span className="text-sm text-muted">/ {review.maxScore}</span>
            </div>
          </Card.Content>
        </Card>

        <Card>
          <Card.Header className="flex flex-row items-center gap-2">
            <Award className="size-4 text-accent" aria-hidden="true" />
            <Card.Title className="text-sm text-muted">Estimated band</Card.Title>
          </Card.Header>
          <Card.Content className="space-y-1">
            {review.estimatedBandCode ? (
              <>
                <div className="text-xl font-semibold text-foreground">
                  {review.estimatedBandCode}{' '}
                  {review.estimatedBandName && (
                    <span className="text-sm font-normal text-muted">— {review.estimatedBandName}</span>
                  )}
                </div>
                <p className="text-xs text-muted">
                  {review.gradedQuestions === 1
                    ? 'Estimated from one task — complete all tasks for higher band confidence.'
                    : `Measured over ${review.gradedQuestions} graded tasks.`}
                </p>
              </>
            ) : (
              <p className="text-sm text-muted">No CEFR band scale is configured for this score.</p>
            )}
          </Card.Content>
        </Card>
      </div>

      <div className="space-y-6">
        {review.questions.map((question) => (
          <Card key={question.questionId}>
            <Card.Header className="flex flex-wrap items-start justify-between gap-3">
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <Chip size="sm" variant="soft" color="accent">
                    Task #{question.displayOrder}
                  </Chip>
                  <span className="text-xs font-medium text-muted">{question.sectionName}</span>
                  <span className="font-mono-tabular text-xs text-muted">{question.wordCount} words</span>
                </div>
                <Card.Title className="text-base">{question.stem}</Card.Title>
              </div>
              <Chip color="success" variant="soft" size="md">
                <span className="font-mono-tabular">
                  {question.totalScore} / {question.maxScore}
                </span>
              </Chip>
            </Card.Header>

            <Card.Content className="space-y-5">
              <ProductiveAiFeedback
                text={question.responseText}
                textLabel="Your submitted text"
                criteria={question.criteria}
                errorSpans={question.errorSpans}
                priorityFix={question.priorityFix}
                modelRewrite={question.modelRewrite}
              />

              {question.annotatedFeedback && (
                <div className="rounded-lg border border-border bg-surface-secondary p-3 space-y-1">
                  <div className="text-xs font-semibold uppercase tracking-wider text-muted">
                    Feedback summary
                  </div>
                  <p className="text-sm leading-relaxed text-foreground">
                    <SafeAnnotatedText text={question.annotatedFeedback} />
                  </p>
                </div>
              )}
            </Card.Content>
          </Card>
        ))}
      </div>
    </div>
  );
}
