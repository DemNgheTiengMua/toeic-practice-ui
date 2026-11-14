import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, ProgressBar, Spinner } from '@heroui/react';
import {
  Award,
  CheckCircle2,
  Clock,
  Eye,
  History,
} from 'lucide-react';
import {
  api,
  type AttemptResultDto,
  type AttemptSkillScoreDto,
} from '@/lib/api';

function formatDurationSeconds(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  if (mins === 0) return `${secs}s`;
  return `${mins}m ${secs}s`;
}

function formatDate(isoString: string): string {
  try {
    const d = new Date(isoString);
    return d.toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return isoString;
  }
}

export function MockExamResultPage() {
  const { attemptId } = useParams({ strict: false }) as { attemptId?: string };
  const navigate = useNavigate();
  const id = Number(attemptId);

  const [result, setResult] = useState<AttemptResultDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const isAwaitingGrading =
    result != null &&
    (result.freeResponseCount ?? 0) > (result.gradedFreeResponseCount ?? 0);

  const fetchResult = useCallback(async () => {
    if (!Number.isFinite(id) || id <= 0) {
      setError('Invalid attempt identifier.');
      setLoading(false);
      return;
    }

    try {
      const data = await api.exams.result(id);
      setResult(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load attempt results.');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void fetchResult();
  }, [fetchResult]);

  // 3-second polling loop when free responses are awaiting AI grading
  useEffect(() => {
    if (!isAwaitingGrading) return;
    let cancelled = false;

    const interval = window.setInterval(async () => {
      try {
        const updated = await api.exams.result(id);
        if (cancelled) return;
        setResult(updated);
        if ((updated.freeResponseCount ?? 0) <= (updated.gradedFreeResponseCount ?? 0)) {
          window.clearInterval(interval);
        }
      } catch {
        // Suppress polling failures gracefully
      }
    }, 3000);

    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [isAwaitingGrading, id]);

  if (loading) {
    return (
      <div className="mx-auto max-w-4xl p-6 flex items-center justify-center gap-3 text-muted min-h-[50vh]">
        <Spinner size="md" color="accent" />
        <span>Loading exam results...</span>
      </div>
    );
  }

  if (error && !result) {
    return (
      <div className="mx-auto max-w-4xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to load results</Alert.Title>
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

  if (!result) return null;

  const displayFactor = result.scoreDisplayFactor && result.scoreDisplayFactor > 0 ? result.scoreDisplayFactor : 1;
  const displayTotalScore = Math.round(result.totalScore / displayFactor);

  return (
    <div className="mx-auto max-w-4xl p-4 sm:p-6 space-y-6">
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
          Results
        </span>
      </nav>

      {/* Header & Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            Results — {result.examName}
          </h1>
          <p className="text-xs text-muted mt-1 flex flex-wrap items-center gap-2">
            <span>Completed on {formatDate(result.submittedAt)}</span>
            <span>•</span>
            <span className="flex items-center gap-1 font-mono-tabular">
              <Clock className="size-3.5" />
              {formatDurationSeconds(result.durationSeconds)}
            </span>
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {!isAwaitingGrading && (
            <Button
              variant="primary"
              size="sm"
              onClick={() =>
                void navigate({
                  to: '/exams/review/$attemptId',
                  params: { attemptId: String(result.attemptId) },
                })
              }
            >
              <Eye className="size-4 mr-1.5" />
              Review answers
            </Button>
          )}
          <Button
            variant="secondary"
            size="sm"
            onClick={() => void navigate({ to: '/exams/history' })}
            className="text-foreground"
          >
            <History className="size-4 mr-1.5" />
            Exam history
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => void navigate({ to: '/exams' })}
          >
            All exams
          </Button>
        </div>
      </div>

      {/* Auto-Submitted Banner */}
      {result.isAutoSubmitted && (
        <Alert status="warning" className="border-warning/30 bg-warning/10 text-foreground">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Time expired</Alert.Title>
            <Alert.Description>
              The exam was submitted automatically when your sitting timer reached zero. All
              answered questions were preserved and scored.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* AI Grading in Progress Notice */}
      {isAwaitingGrading && (
        <Alert status="accent" className="border-accent/30 bg-accent/10 text-foreground">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>AI grading in progress</Alert.Title>
            <Alert.Description>
              Objective multiple-choice sections are scored below.{' '}
              {(result.freeResponseCount ?? 0) - (result.gradedFreeResponseCount ?? 0)} free response(s)
              are currently being evaluated by the AI grading engine. This page will update
              automatically.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Score Hero Card */}
      <Card className="bg-surface border-border shadow-surface overflow-hidden">
        <Card.Content className="p-6">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6 items-center">
            {/* Total Scaled Score */}
            <div className="flex flex-col items-center md:items-start text-center md:text-left space-y-1">
              <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                Total scaled score
              </span>
              <div className="text-5xl font-extrabold font-mono-tabular text-accent">
                {displayTotalScore}
              </div>
              <div className="text-xs text-muted">
                {result.certName} scaled score conversion
              </div>

              {/* Previous Attempt Delta */}
              {result.scoreDelta != null && (
                <div className="pt-2">
                  {result.scoreDelta > 0 ? (
                    <Chip color="success" variant="soft" size="sm" className="font-mono-tabular">
                      +{result.scoreDelta} vs previous attempt
                    </Chip>
                  ) : result.scoreDelta < 0 ? (
                    <Chip color="danger" variant="soft" size="sm" className="font-mono-tabular">
                      {'\u2212'}{Math.abs(result.scoreDelta)} vs previous attempt
                    </Chip>
                  ) : (
                    <Chip variant="soft" size="sm" className="font-mono-tabular">
                      ±0 vs previous attempt
                    </Chip>
                  )}
                </div>
              )}
            </div>

            {/* CEFR Band on Certificate Scale */}
            <div className="flex flex-col items-center justify-center p-4 rounded-xl bg-surface-secondary border border-border/60 text-center space-y-1">
              <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                Certificate band
              </span>
              <div className="text-2xl font-bold text-foreground">
                {result.bandName || result.bandCode || '--'}
              </div>
              <span className="text-xs text-muted">
                Assigned on {result.certCode} scale
              </span>
            </div>

            {/* Completion Status */}
            <div className="flex flex-col items-center md:items-end text-center md:text-right space-y-2">
              <Chip
                color={result.status === 'graded' || result.status === 'done' ? 'success' : 'accent'}
                variant="soft"
                size="md"
              >
                <CheckCircle2 className="size-4 mr-1.5" />
                {result.status === 'graded' || result.status === 'done'
                  ? 'Completed & graded'
                  : 'Submitted'}
              </Chip>
              <div className="text-xs text-muted font-mono-tabular">
                Sitting time: {formatDurationSeconds(result.durationSeconds)}
              </div>
            </div>
          </div>
        </Card.Content>
      </Card>

      {/* Skill Breakdown Section */}
      <div className="space-y-3">
        <h2 className="text-base font-semibold text-foreground">Section & skill breakdown</h2>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          {result.skillScores.map((skill: AttemptSkillScoreDto) => {
            const accuracyPct =
              skill.totalQuestions > 0
                ? Math.round((skill.rawScore / skill.totalQuestions) * 100)
                : 0;

            return (
              <Card key={skill.skillId} className="bg-surface border-border">
                <Card.Header className="flex flex-row items-center justify-between pb-2">
                  <h3 className="font-semibold text-base text-foreground">{skill.skillName}</h3>
                  <Chip size="sm" variant="secondary" className="font-mono-tabular font-bold">
                    {skill.scaledScore} pts
                  </Chip>
                </Card.Header>
                <Card.Content className="space-y-3 text-sm">
                  <div className="flex items-center justify-between text-xs text-muted">
                    <span>
                      Correct items: <strong className="text-foreground">{skill.rawScore}</strong> /{' '}
                      {skill.totalQuestions}
                    </span>
                    <span className="font-mono-tabular font-semibold text-foreground">
                      {accuracyPct}%
                    </span>
                  </div>

                  <ProgressBar
                    value={accuracyPct}
                    color="accent"
                    aria-label={`${skill.skillName} accuracy percentage`}
                  />
                </Card.Content>
              </Card>
            );
          })}
        </div>
      </div>

      {/* Bottom Actions */}
      <div className="pt-4 flex flex-col sm:flex-row items-center justify-between gap-3">
        <Button
          variant="secondary"
          size="md"
          onClick={() => void navigate({ to: '/exams' })}
        >
          Back to all exams
        </Button>

        <div className="flex items-center gap-3 w-full sm:w-auto">
          <Button
            variant="primary"
            size="lg"
            onClick={() =>
              void navigate({
                to: '/exams/review/$attemptId',
                params: { attemptId: String(result.attemptId) },
              })
            }
            className="w-full sm:w-auto"
          >
            <Eye className="size-4 mr-1.5" />
            Review detailed answers
          </Button>
        </div>
      </div>
    </div>
  );
}
