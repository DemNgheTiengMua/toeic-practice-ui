import { useEffect, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner } from '@heroui/react';
import { ArrowRight, Clock, FileText, History, Play, Sparkles, Wallet } from 'lucide-react';
import { api, type MockExamItemDto, type MockExamListViewDto } from '@/lib/api';

export function MockExamCatalogPage() {
  const navigate = useNavigate();
  const [catalog, setCatalog] = useState<MockExamListViewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    api.exams
      .list()
      .then((data) => {
        if (!cancelled) {
          setCatalog(data);
          setError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load mock exam catalog.');
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
        <span>Loading mock exams...</span>
      </div>
    );
  }

  if (error && !catalog) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to load mock exams</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  if (!catalog) return null;

  const hasCredits = catalog.creditBalance > 0;

  return (
    <div className="mx-auto max-w-5xl p-4 sm:p-6 space-y-6">
      {/* Breadcrumbs */}
      <nav className="flex items-center gap-2 text-xs text-muted" aria-label="Breadcrumb">
        <Link to="/dashboard" className="hover:text-foreground transition-colors">
          Home
        </Link>
        <span aria-hidden="true">/</span>
        <span className="text-foreground font-medium" aria-current="page">
          Mock exams
        </span>
      </nav>

      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">Mock exams</h1>
          <p className="text-sm text-muted mt-1">
            Full-length timed exams replicate authentic test conditions with strict countdown
            timers, scaled score conversion, and CEFR band mapping.
          </p>
        </div>

        <div className="flex items-center gap-3 shrink-0">
          <Chip
            color={hasCredits ? 'accent' : 'warning'}
            variant="soft"
            size="lg"
            className="font-medium"
          >
            <Wallet className="size-4 mr-1.5" />
            <span className="font-mono-tabular font-bold">{catalog.creditBalance}</span>{' '}
            {catalog.creditBalance === 1 ? 'credit' : 'credits'}
          </Chip>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => void navigate({ to: '/exams/history' })}
            className="text-foreground"
          >
            <History className="size-4 mr-1.5" />
            Exam history
          </Button>
        </div>
      </div>

      {/* Out of Credits Alert */}
      {!hasCredits && (
        <Alert status="warning" className="border-warning/30 bg-warning/10 text-foreground">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>You are out of credits</Alert.Title>
            <Alert.Description>
              Each full mock exam sitting requires 1 credit. Top up your wallet to continue, or
              practice individual sections for free!
            </Alert.Description>
            <div className="flex flex-wrap items-center gap-3 mt-3">
              <Button
                variant="primary"
                size="sm"
                onClick={() => void navigate({ to: '/checkout', search: { credits: 1, returnUrl: '/exams' } as any })}
              >
                <Wallet className="size-4 mr-1.5" />
                Top up wallet
              </Button>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void navigate({ to: '/practice' })}
              >
                Practice free
              </Button>
            </div>
          </Alert.Content>
        </Alert>
      )}

      {/* Empty State */}
      {catalog.exams.length === 0 ? (
        <Card className="p-8 text-center bg-surface border-border">
          <Card.Content className="flex flex-col items-center gap-3">
            <div className="p-3 rounded-full bg-surface-secondary text-muted">
              <FileText className="size-8" />
            </div>
            <h2 className="text-lg font-semibold text-foreground">No mock exams available</h2>
            <p className="text-sm text-muted max-w-md">
              New full-length mock exams for {catalog.certificate.name} will appear here once
              published. In the meantime, section-by-section drilling is always free.
            </p>
            <Button
              variant="primary"
              size="sm"
              className="mt-2"
              onClick={() => void navigate({ to: '/practice' })}
            >
              <Sparkles className="size-4 mr-1.5" />
              Practice by section
            </Button>
          </Card.Content>
        </Card>
      ) : (
        /* Exams Grid */
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {catalog.exams.map((exam: MockExamItemDto) => {
            const hasActiveAttempt = Boolean(exam.activeAttemptId);
            const isCompleted = exam.completedAttemptScore != null;

            return (
              <Card
                key={exam.examId}
                className="flex flex-col justify-between bg-surface border-border shadow-surface hover:border-accent/40 transition-colors"
              >
                <Card.Header className="flex flex-row items-start justify-between gap-3 pb-2">
                  <div className="space-y-1">
                    <h3 className="font-semibold text-base text-foreground leading-snug">
                      {exam.name}
                    </h3>
                    <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
                      <span className="font-medium text-foreground">{exam.certCode}</span>
                      {exam.sourceName && (
                        <>
                          <span>•</span>
                          <span>{exam.sourceName}</span>
                        </>
                      )}
                    </div>
                  </div>

                  <div className="shrink-0 flex items-center gap-1.5">
                    {hasActiveAttempt ? (
                      <Chip size="sm" color="warning" variant="soft">
                        In progress
                      </Chip>
                    ) : isCompleted ? (
                      <Chip size="sm" color="success" variant="soft">
                        Done · {exam.completedAttemptScore} pts
                        {exam.completedAttemptBand ? ` (${exam.completedAttemptBand})` : ''}
                      </Chip>
                    ) : (
                      <Chip size="sm" variant="secondary">
                        {exam.creditCost} {exam.creditCost === 1 ? 'credit' : 'credits'}
                      </Chip>
                    )}
                  </div>
                </Card.Header>

                <Card.Content className="flex flex-col gap-2 py-2 text-sm text-muted">
                  {exam.description && <p className="line-clamp-2">{exam.description}</p>}

                  <div className="flex flex-wrap items-center gap-4 text-xs font-mono-tabular mt-2">
                    <span className="flex items-center gap-1">
                      <Clock className="size-3.5" />
                      {exam.durationMinutes} mins
                    </span>
                    {exam.questionCount != null && (
                      <span className="flex items-center gap-1">
                        <FileText className="size-3.5" />
                        {exam.questionCount} questions
                      </span>
                    )}
                  </div>
                </Card.Content>

                <Card.Footer className="pt-3 border-t border-separator/40 flex items-center justify-between">
                  <div>
                    {isCompleted && (
                      <Link
                        to="/exams/history"
                        className="text-xs text-muted hover:text-foreground transition-colors"
                      >
                        Prior attempts
                      </Link>
                    )}
                  </div>

                  <div>
                    {hasActiveAttempt ? (
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={() =>
                          void navigate({
                            to: '/exams/take/$attemptId',
                            params: { attemptId: String(exam.activeAttemptId) },
                          })
                        }
                      >
                        <Play className="size-4 mr-1.5" />
                        Resume sitting
                      </Button>
                    ) : (
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={() =>
                          void navigate({
                            to: '/exams/$examId',
                            params: { examId: String(exam.examId) },
                          })
                        }
                      >
                        Take exam
                        <ArrowRight className="size-4 ml-1.5" />
                      </Button>
                    )}
                  </div>
                </Card.Footer>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
