import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, ProgressBar, Spinner } from '@heroui/react';
import {
  ArrowRight,
  Award,
  CheckCircle2,
  Compass,
  Info,
  RefreshCw,
  RotateCcw,
  Sparkles,
  Target,
} from 'lucide-react';
import {
  api,
  type PlacementGradingStatusDto,
  type PlacementResultViewDto,
  type PlacementSkillResultInfoDto,
  type PlacementSkillScoreInfoDto,
} from '@/lib/api';
import { formatNotMeasuredReason } from '@/lib/sitting-queue';
import { formatPlacementScore, normalizePlacementSkillStatus } from '../sitting/sitting-helpers';

export function PlacementResultPage() {
  const { attemptId } = useParams({ strict: false }) as { attemptId?: string };
  const placementId = Number(attemptId);

  const [result, setResult] = useState<PlacementResultViewDto | null>(null);
  const [gradingStatus, setGradingStatus] = useState<PlacementGradingStatusDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedTargetBand, setSelectedTargetBand] = useState<string | null>(null);
  const [savingTarget, setSavingTarget] = useState(false);
  const [targetSaved, setTargetSaved] = useState(false);

  const fetchResult = useCallback(async () => {
    if (!Number.isFinite(placementId) || placementId <= 0) {
      setError('Invalid placement result identifier.');
      setLoading(false);
      return;
    }
    try {
      const data = await api.placement.result(placementId);
      setResult(data);
      setError(null);
      const currentTarget = data.cefrBands?.find((b) => b.isTarget)?.code ?? null;
      if (currentTarget) {
        setSelectedTargetBand(currentTarget);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load placement result.');
    } finally {
      setLoading(false);
    }
  }, [placementId]);

  useEffect(() => {
    void fetchResult();
  }, [fetchResult]);

  // SIT-03: 3-second polling loop while AI grading is pending
  useEffect(() => {
    if (!result || result.isGraded) return;
    let cancelled = false;

    const poll = async () => {
      try {
        const status = await api.placement.gradingStatus(placementId);
        if (cancelled) return;
        setGradingStatus(status);
        if (status.isGraded) {
          await fetchResult();
        }
      } catch {
        // Keep polling gracefully on transient error
      }
    };

    const timer = window.setInterval(() => {
      void poll();
    }, 3000);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [result, placementId, fetchResult]);

  const handleRetryGrading = async () => {
    setRetrying(true);
    setError(null);
    try {
      await api.placement.retryGrading(placementId);
      await fetchResult();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to requeue AI grading.');
    } finally {
      setRetrying(false);
    }
  };

  const handleSaveTargetBand = async (code: string) => {
    setSelectedTargetBand(code);
    setSavingTarget(true);
    setTargetSaved(false);
    try {
      await api.placement.setTargetBand(code);
      setTargetSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to save target band.');
    } finally {
      setSavingTarget(false);
    }
  };

  const mergedSkills = useMemo(() => {
    if (!result) return [];
    const scoreBySkillId = new Map<number, PlacementSkillScoreInfoDto>();
    for (const sc of result.skillScores ?? []) {
      scoreBySkillId.set(sc.skillId, sc);
    }

    if (result.skillResults && result.skillResults.length > 0) {
      return result.skillResults.map((sr: PlacementSkillResultInfoDto) => ({
        result: sr,
        score: scoreBySkillId.get(sr.skillId) ?? null,
      }));
    }

    return (result.skillScores ?? []).map((sc) => ({
      result: {
        skillId: sc.skillId,
        skillCode: sc.skillCode,
        skillName: sc.skillName,
        status: 'measured',
        levelCode: sc.bandCode ?? null,
        positionInBand: null,
        measuredAt: result.takenAt,
        reusedFromPrior: false,
        failureReason: null,
      } satisfies PlacementSkillResultInfoDto,
      score: sc,
    }));
  }, [result]);

  const hasUnmeasuredSkills = useMemo(
    () =>
      mergedSkills.some((s) => normalizePlacementSkillStatus(s.result.status) === 'not_measured'),
    [mergedSkills],
  );

  const hasFailedGrading = useMemo(
    () =>
      (result?.failedFreeResponseCount ?? 0) > 0 ||
      mergedSkills.some((s) => s.result.failureReason === 'ai_grading_failed'),
    [result, mergedSkills],
  );

  if (loading) {
    return (
      <div className="mx-auto max-w-5xl p-6 flex items-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Loading placement result...</span>
      </div>
    );
  }

  if (error && !result) {
    return (
      <div className="mx-auto max-w-4xl p-6 space-y-4">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Result unavailable</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
        <Link to="/placement" className="button button--sm button--outline">
          Return to placement catalog
        </Link>
      </div>
    );
  }

  if (!result) return null;

  const scoreFactor = result.scoreDisplayFactor ?? 1;
  const formattedTotal = formatPlacementScore(result.totalScore, scoreFactor);
  const isTwoSkillScope = result.scope === 'listening_and_reading';

  return (
    <div className="mx-auto max-w-5xl p-4 md:p-6 space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-5">
        <div className="space-y-1.5">
          <div className="flex items-center gap-2">
            <Chip size="sm" variant="soft" color="accent" className="font-display">
              {result.certCode}
            </Chip>
            <span className="text-xs text-muted font-mono-tabular">
              Taken {new Date(result.takenAt).toLocaleDateString()}
            </span>
            {result.isAutoSubmitted && (
              <Chip size="sm" variant="soft" color="warning">
                Auto-submitted at time limit
              </Chip>
            )}
          </div>
          <h1 className="font-display text-2xl font-semibold text-foreground">{result.examName}</h1>
        </div>

        <div className="rounded-lg border border-border bg-surface px-5 py-3 text-right">
          <p className="text-xs text-muted">Assessed band</p>
          <p className="font-display text-2xl font-semibold text-accent">
            {result.resultBandCode}
            {result.resultBandName ? ` · ${result.resultBandName}` : ''}
          </p>
          {formattedTotal != null && (
            <p className="text-xs text-muted font-mono-tabular">Score {formattedTotal}</p>
          )}
        </div>
      </header>

      {error && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* SIT-03 AI Grading Pending Banner */}
      {!result.isGraded && (
        <Alert status="accent">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title className="flex items-center gap-2">
              <Spinner size="sm" color="accent" />
              AI grading in progress
            </Alert.Title>
            <Alert.Description>
              {gradingStatus?.stageDescriptor ||
                'Scoring your spoken and written responses. This view refreshes automatically every 3 seconds.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Two-skill scope informational banner */}
      {isTwoSkillScope && (
        <Alert status="default">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title className="flex items-center gap-1.5">
              <Info className="h-4 w-4 text-accent" aria-hidden="true" />
              Two-skill scope (Listening + Reading)
            </Alert.Title>
            <Alert.Description>
              You chose the objective Listening + Reading scope. Speaking and Writing were left
              unmeasured in this sitting.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Degraded partial result warning */}
      {(hasUnmeasuredSkills || hasFailedGrading) && (
        <Alert status="warning">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Partial placement result</Alert.Title>
            <Alert.Description>
              One or more skills remain unmeasured and are excluded from scoring instead of being
              counted as zero.
            </Alert.Description>
          </Alert.Content>
          {hasFailedGrading && (
            <Button
              size="sm"
              variant="outline"
              isPending={retrying}
              onPress={() => void handleRetryGrading()}
            >
              <RefreshCw className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
              Retry grading
            </Button>
          )}
        </Alert>
      )}

      {/* CEFR / Band Signboard Track */}
      {result.cefrBands && result.cefrBands.length > 0 && (
        <Card>
          <Card.Header>
            <Card.Title className="font-display text-base flex items-center gap-2">
              <Award className="h-4 w-4 text-accent" aria-hidden="true" />
              Band progression signboard
            </Card.Title>
            {result.bandNote && <Card.Description>{result.bandNote}</Card.Description>}
          </Card.Header>
          <Card.Content>
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-6 gap-2.5">
              {result.cefrBands.map((band) => {
                const isSelectedTarget =
                  selectedTargetBand != null ? selectedTargetBand === band.code : band.isTarget;
                const cardClass = band.isCurrent
                  ? 'border-2 border-accent bg-accent/10'
                  : band.isReached
                    ? 'border border-success/40 bg-success/5'
                    : 'border border-border bg-surface-secondary/50';
                return (
                  <div key={band.code} className={`rounded-lg p-3 space-y-1.5 ${cardClass}`}>
                    <div className="flex items-center justify-between">
                      <span className="font-display font-semibold text-base text-foreground">
                        {band.code}
                      </span>
                      {band.isCurrent && (
                        <Chip size="sm" variant="primary" color="accent">
                          Here
                        </Chip>
                      )}
                      {isSelectedTarget && !band.isCurrent && (
                        <Chip size="sm" variant="soft" color="success">
                          Target
                        </Chip>
                      )}
                    </div>
                    <p className="text-xs text-muted truncate">{band.name}</p>
                    <p className="text-xs font-mono-tabular text-muted">
                      {formatPlacementScore(band.minTotal, scoreFactor)}–
                      {formatPlacementScore(band.maxTotal, scoreFactor)}
                    </p>
                  </div>
                );
              })}
            </div>
          </Card.Content>
        </Card>
      )}

      {/* Skill Breakdown */}
      <Card>
        <Card.Header>
          <Card.Title className="font-display text-lg">Skill breakdown</Card.Title>
          {result.weakerSkillName && (
            <Card.Description>
              Priority growth area: <strong>{result.weakerSkillName}</strong>
            </Card.Description>
          )}
        </Card.Header>
        <Card.Content className="divide-y divide-separator">
          {mergedSkills.map(({ result: row, score }) => {
            const status = normalizePlacementSkillStatus(row.status);
            const scaledDisplay =
              status === 'not_measured'
                ? null
                : formatPlacementScore(score?.scaledScore, scoreFactor);

            return (
              <div
                key={row.skillId}
                data-testid={`skill-result-${row.skillCode.toLowerCase()}`}
                className="py-4 first:pt-0 last:pb-0 flex flex-wrap items-center justify-between gap-4"
              >
                <div className="space-y-1 min-w-[200px]">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium text-foreground">{row.skillName}</span>

                    {status === 'not_measured' && (
                      <Chip size="sm" variant="soft" color="warning">
                        Unmeasured
                      </Chip>
                    )}
                    {status === 'grading' && (
                      <Chip size="sm" variant="soft" color="accent">
                        Grading
                      </Chip>
                    )}
                    {status === 'estimated_from_one_task' && (
                      <Chip size="sm" variant="soft" color="default">
                        AI-estimated
                      </Chip>
                    )}
                    {status === 'exempt_accommodated' && (
                      <Chip size="sm" variant="soft" color="default">
                        Exempt
                      </Chip>
                    )}
                    {status === 'measured' && (
                      <Chip size="sm" variant="soft" color="success">
                        Measured
                      </Chip>
                    )}

                    {row.reusedFromPrior && (
                      <Chip size="sm" variant="soft" color="accent">
                        Carried over
                      </Chip>
                    )}
                  </div>

                  {status === 'not_measured' ? (
                    <p className="text-xs text-muted">
                      {formatNotMeasuredReason(row.failureReason)}
                    </p>
                  ) : (
                    <div className="flex flex-wrap items-center gap-3 text-xs text-muted font-mono-tabular">
                      {row.measuredAt && (
                        <span>Measured {new Date(row.measuredAt).toLocaleDateString()}</span>
                      )}
                      {score && score.totalQuestions > 0 && (
                        <span>
                          Raw {score.rawScore} / {score.totalQuestions}
                        </span>
                      )}
                    </div>
                  )}
                </div>

                <div className="flex items-center gap-4 min-w-[160px] justify-end">
                  {status === 'not_measured' ? (
                    row.failureReason === 'ai_grading_failed' ? (
                      <Button
                        size="sm"
                        variant="outline"
                        isPending={retrying}
                        onPress={() => void handleRetryGrading()}
                      >
                        <RefreshCw className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
                        Retry grading
                      </Button>
                    ) : (
                      <span className="text-sm text-muted">Not measured</span>
                    )
                  ) : (
                    <div className="text-right space-y-1">
                      <div className="flex items-center justify-end gap-2 font-mono-tabular">
                        {row.levelCode && (
                          <Chip size="sm" variant="soft" color="accent">
                            {row.levelCode}
                          </Chip>
                        )}
                        {scaledDisplay != null && (
                          <span className="text-lg font-semibold text-foreground">
                            {scaledDisplay}
                          </span>
                        )}
                      </div>
                      {row.positionInBand != null && (
                        <ProgressBar
                          aria-label={`${row.skillName} position in band`}
                          value={row.positionInBand}
                          minValue={0}
                          maxValue={100}
                          size="sm"
                          color="accent"
                          className="w-32"
                        >
                          <ProgressBar.Track>
                            <ProgressBar.Fill />
                          </ProgressBar.Track>
                        </ProgressBar>
                      )}
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </Card.Content>
      </Card>

      {/* Target Band & Next Actions */}
      <Card variant="secondary">
        <Card.Header>
          <Card.Title className="font-display text-base flex items-center gap-2">
            <Target className="h-4 w-4 text-accent" aria-hidden="true" />
            Set target band &amp; study plan
          </Card.Title>
          <Card.Description>
            Choose your target CEFR band so your guided learning path prioritizes the highest-impact
            skills.
          </Card.Description>
        </Card.Header>
        <Card.Content className="space-y-4">
          {result.cefrBands && result.cefrBands.length > 0 && (
            <div className="flex flex-wrap items-center gap-2">
              {result.cefrBands.map((band) => (
                <Button
                  key={band.code}
                  size="sm"
                  variant={selectedTargetBand === band.code ? 'primary' : 'outline'}
                  isDisabled={savingTarget}
                  onPress={() => void handleSaveTargetBand(band.code)}
                >
                  {band.code} · {band.name}
                </Button>
              ))}
              {targetSaved && (
                <Chip size="sm" color="success" variant="soft">
                  <CheckCircle2 className="h-3 w-3 mr-1 inline" aria-hidden="true" />
                  Target saved
                </Chip>
              )}
            </div>
          )}

          <div className="flex flex-wrap items-center justify-between gap-3 pt-2 border-t border-separator">
            <Link to="/placement" className="button button--sm button--outline">
              <RotateCcw className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
              Back to placement
            </Link>
            <div className="flex flex-wrap items-center gap-2">
              <Link to="/practice" className="button button--sm button--secondary">
                <Sparkles className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
                Practice weaker skills
              </Link>
              <Link to="/learning-path" className="button button--sm button--primary">
                <Compass className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
                Open learning path
                <ArrowRight className="h-3.5 w-3.5 ml-1.5" aria-hidden="true" />
              </Link>
            </div>
          </div>
        </Card.Content>
      </Card>
    </div>
  );
}
