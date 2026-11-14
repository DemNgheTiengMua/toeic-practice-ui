import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner } from '@heroui/react';
import { ArrowRight, Clock, Coins, History, Mic, Play } from 'lucide-react';
import { api } from '@/lib/api';
import { SpeakingMicrophoneCard } from './SpeakingMicrophoneCard';
import { useMicrophoneCheck } from './useMicrophoneCheck';
import {
  formatSpeakingAttemptStatusLabel,
  type SpeakingCatalog,
  type SpeakingInstructions,
  type SpeakingPriorAttempt,
} from './types';

function isReviewAttempt(attempt: SpeakingPriorAttempt): boolean {
  return attempt.canReview || attempt.status === 'graded' || attempt.status === 'submitted';
}

export function SpeakingStudioPage() {
  const navigate = useNavigate();
  const mic = useMicrophoneCheck(true);

  const [catalog, setCatalog] = useState<SpeakingCatalog | null>(null);
  const [selectedExamId, setSelectedExamId] = useState<number | null>(null);
  const [instructions, setInstructions] = useState<SpeakingInstructions | null>(null);
  const [loadingCatalog, setLoadingCatalog] = useState(true);
  const [loadingInstructions, setLoadingInstructions] = useState(false);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadCatalog = useCallback(async () => {
    setLoadingCatalog(true);
    setError(null);
    try {
      const data = (await api.speaking.catalog()) as SpeakingCatalog;
      setCatalog(data);
      setSelectedExamId((prev) => {
        if (prev && data.exams.some((e) => e.examId === prev)) {
          return prev;
        }
        return data.exams[0]?.examId ?? null;
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load speaking studio.');
    } finally {
      setLoadingCatalog(false);
    }
  }, []);

  useEffect(() => {
    void loadCatalog();
  }, [loadCatalog]);

  useEffect(() => {
    if (!selectedExamId) {
      setInstructions(null);
      return;
    }
    let cancelled = false;
    setLoadingInstructions(true);
    api.speaking
      .instructions(selectedExamId)
      .then((data) => {
        if (!cancelled) {
          setInstructions(data as SpeakingInstructions);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load speaking instructions.');
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingInstructions(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [selectedExamId]);

  const handleStart = async () => {
    if (!instructions) return;
    if (instructions.activeAttemptId) {
      void navigate({
        to: '/speaking/practice/$questionId',
        params: { questionId: String(instructions.activeAttemptId) },
      });
      return;
    }
    setStarting(true);
    setError(null);
    try {
      const started = await api.speaking.start(instructions.examId);
      void navigate({
        to: '/speaking/practice/$questionId',
        params: { questionId: String(started.attemptId) },
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not start speaking session.');
    } finally {
      setStarting(false);
    }
  };

  if (loadingCatalog) {
    return (
      <div className="flex min-h-[280px] items-center justify-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Loading speaking practice studio...</span>
      </div>
    );
  }

  const insufficientCredits =
    instructions !== null &&
    !instructions.activeAttemptId &&
    instructions.creditBalance < instructions.creditCost;

  const startBlocked =
    !instructions ||
    insufficientCredits ||
    (!instructions.activeAttemptId && mic.status !== 'ready');

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <Chip color="accent" variant="soft" size="sm">
              {catalog?.certificate.code ?? 'Speaking'}
            </Chip>
            <span className="text-xs uppercase tracking-wider text-muted">Productive studio</span>
          </div>
          <h1 className="font-display text-2xl font-semibold tracking-tight text-foreground">
            Speaking practice studio
          </h1>
          <p className="text-sm text-muted">
            Record timed responses for {catalog?.certificate.name ?? 'your active certificate'} and review AI pronunciation and fluency scores.
          </p>
        </div>

        <Card variant="secondary" className="min-w-[180px]">
          <Card.Content className="flex items-center gap-3 py-3">
            <Coins className="size-5 text-accent" aria-hidden="true" />
            <div>
              <div className="text-xs text-muted">Credit balance</div>
              <div className="font-mono-tabular text-lg font-semibold text-foreground">
                {catalog?.creditBalance ?? 0} credits
              </div>
            </div>
          </Card.Content>
        </Card>
      </header>

      {error && (
        <Alert status="danger" role="alert">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Speaking studio error</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {!catalog || catalog.exams.length === 0 ? (
        <Card>
          <Card.Content className="py-10 text-center text-sm text-muted">
            No speaking practice sets are currently published for this certificate.
          </Card.Content>
        </Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-12">
          <div className="space-y-3 lg:col-span-5">
            <h2 className="text-sm font-semibold text-foreground">Available speaking sets</h2>
            {catalog.exams.map((exam) => {
              const isSelected = exam.examId === selectedExamId;
              return (
                <Card
                  key={exam.examId}
                  variant={isSelected ? 'default' : 'secondary'}
                  className={isSelected ? 'border-accent ring-1 ring-accent' : ''}
                >
                  <Card.Header className="flex flex-row items-start justify-between gap-2">
                    <div className="space-y-1">
                      <Card.Title className="text-base">{exam.name}</Card.Title>
                      {exam.description && (
                        <Card.Description>{exam.description}</Card.Description>
                      )}
                    </div>
                    <Chip color={exam.creditCost > 0 ? 'accent' : 'success'} variant="soft" size="sm">
                      <span className="font-mono-tabular">
                        {exam.creditCost > 0 ? `${exam.creditCost} cr` : 'Free'}
                      </span>
                    </Chip>
                  </Card.Header>
                  <Card.Content className="flex flex-wrap items-center justify-between gap-3 pt-0">
                    <span className="inline-flex items-center gap-1.5 text-xs text-muted font-mono-tabular">
                      <Clock className="size-3.5" aria-hidden="true" />
                      {exam.durationMinutes} min
                    </span>
                    <div className="flex items-center gap-2">
                      {exam.latestGradedAttemptId && (
                        <Link
                          to="/speaking/result/$attemptId"
                          params={{ attemptId: String(exam.latestGradedAttemptId) }}
                          className="text-xs font-medium text-accent hover:underline"
                        >
                          Latest result
                        </Link>
                      )}
                      <Button
                        size="sm"
                        variant={isSelected ? 'primary' : 'secondary'}
                        onPress={() => setSelectedExamId(exam.examId)}
                      >
                        {isSelected ? 'Selected' : 'Inspect'}
                      </Button>
                    </div>
                  </Card.Content>
                </Card>
              );
            })}
          </div>

          <div className="space-y-4 lg:col-span-7">
            <SpeakingMicrophoneCard mic={mic} />

            {loadingInstructions ? (
              <Card>
                <Card.Content className="flex items-center justify-center gap-2 py-8 text-sm text-muted">
                  <Spinner size="sm" />
                  Loading exam instructions...
                </Card.Content>
              </Card>
            ) : instructions ? (
              <Card>
                <Card.Header className="flex flex-row flex-wrap items-start justify-between gap-3">
                  <div>
                    <Card.Title className="text-lg">{instructions.examName}</Card.Title>
                    <Card.Description>
                      {instructions.questionCount} questions •{' '}
                      <span className="font-mono-tabular">{instructions.durationMinutes} min</span> •{' '}
                      <span className="font-mono-tabular">{instructions.creditCost} credits</span>
                    </Card.Description>
                  </div>
                  <Button
                    variant="primary"
                    size="md"
                    isDisabled={startBlocked}
                    isPending={starting}
                    onPress={() => void handleStart()}
                  >
                    {instructions.activeAttemptId ? (
                      <>
                        <Play className="size-4" aria-hidden="true" />
                        Resume speaking session
                      </>
                    ) : (
                      <>
                        <Mic className="size-4" aria-hidden="true" />
                        Start speaking session
                      </>
                    )}
                  </Button>
                </Card.Header>

                <Card.Content className="space-y-4">
                  {insufficientCredits && (
                    <Alert status="warning" role="alert">
                      <Alert.Indicator />
                      <Alert.Content>
                        <Alert.Title>Not enough credits to start</Alert.Title>
                        <Alert.Description className="space-y-2">
                          <p>
                            This speaking session requires{' '}
                            <span className="font-mono-tabular font-semibold">{instructions.creditCost}</span>{' '}
                            credits; your balance is{' '}
                            <span className="font-mono-tabular font-semibold">{instructions.creditBalance}</span>.
                          </p>
                          <div className="flex flex-wrap gap-3 pt-1">
                            <Link
                              to="/checkout"
                              search={{ credits: 1, returnUrl: '/practice/speaking' } as any}
                              className="text-sm font-medium text-accent underline"
                            >
                              Top up wallet
                            </Link>
                            <Link to="/pricing" className="text-sm font-medium text-accent underline">
                              View credit pricing
                            </Link>
                          </div>
                        </Alert.Description>
                      </Alert.Content>
                    </Alert>
                  )}

                  <div className="space-y-2">
                    <h3 className="text-xs font-semibold uppercase tracking-wider text-muted">
                      Speaking sections
                    </h3>
                    <div className="divide-y divide-border rounded-lg border border-border bg-surface-secondary">
                      {instructions.sections.map((section) => (
                        <div
                          key={section.sectionId}
                          className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm"
                        >
                          <span className="font-medium text-foreground">{section.name}</span>
                          <span className="font-mono-tabular text-xs text-muted">
                            {section.expectedQuestionCount} tasks
                            {section.prepSeconds ? ` • ${section.prepSeconds}s prep` : ''}
                            {section.responseSeconds ? ` • ${section.responseSeconds}s speak` : ''}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>

                  {instructions.attempts && instructions.attempts.length > 0 && (
                    <div className="space-y-2">
                      <h3 className="inline-flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-muted">
                        <History className="size-3.5" aria-hidden="true" />
                        Prior attempts
                      </h3>
                      <div className="divide-y divide-border rounded-lg border border-border">
                        {instructions.attempts.map((attempt) => (
                          <div
                            key={attempt.attemptId}
                            className="flex items-center justify-between gap-3 px-3 py-2 text-sm"
                          >
                            <div className="flex items-center gap-2">
                              <Chip size="sm" variant="soft" color={attempt.status === 'graded' ? 'success' : 'default'}>
                                {formatSpeakingAttemptStatusLabel(attempt.status)}
                              </Chip>
                              <span className="font-mono-tabular text-xs text-muted">
                                Attempt #{attempt.attemptId}
                              </span>
                            </div>
                            {isReviewAttempt(attempt) ? (
                              <Link
                                to="/speaking/result/$attemptId"
                                params={{ attemptId: String(attempt.attemptId) }}
                                className="inline-flex items-center gap-1 text-xs font-medium text-accent hover:underline"
                              >
                                View result
                                <ArrowRight className="size-3" aria-hidden="true" />
                              </Link>
                            ) : (
                              <Link
                                to="/speaking/practice/$questionId"
                                params={{ questionId: String(attempt.attemptId) }}
                                className="inline-flex items-center gap-1 text-xs font-medium text-accent hover:underline"
                              >
                                Resume session
                                <ArrowRight className="size-3" aria-hidden="true" />
                              </Link>
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </Card.Content>
              </Card>
            ) : null}
          </div>
        </div>
      )}
    </div>
  );
}
