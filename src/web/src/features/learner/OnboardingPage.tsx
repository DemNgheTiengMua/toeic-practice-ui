import React, { useEffect, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  FieldError,
  Form,
  Input,
  Label,
  ListBox,
  Radio,
  RadioGroup,
  Select,
  Spinner,
  TextField,
} from '@heroui/react';
import type { Key } from '@heroui/react';
import {
  AlertCircle,
  ArrowRight,
  Calendar,
  CheckCircle2,
  Clock,
  Compass,
  Sparkles,
  Target,
  Trophy,
} from 'lucide-react';
import {
  api,
  ApiClientError,
  PlacementScopeCode,
  SetGoalRequest,
  TargetBandViewDto,
} from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { formatFeasibilityStatus } from './learnerHelpers';

type OobeStep = 1 | 2 | 3 | 4;

export function OnboardingPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { learnerContext, onboardingState, refreshOnboardingState, switchCertificate } = useAuth();

  const [stepOverride, setStepOverride] = useState<OobeStep | null>(null);
  const [selectedScope, setSelectedScope] = useState<PlacementScopeCode>('listening_and_reading');
  const [targetBandCode, setTargetBandCode] = useState<string>('');
  const [targetDate, setTargetDate] = useState<string>('');
  const [weeklyHours, setWeeklyHours] = useState<string>('5');
  const [formError, setFormError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const derivedStep: OobeStep = !onboardingState
    ? 3
    : !onboardingState.hasCompletedPlacement
      ? 1
      : !onboardingState.hasTargetGoal
        ? 2
        : !onboardingState.hasActiveLearningPath
          ? 4
          : 3;

  const activeStep: OobeStep = stepOverride ?? derivedStep;

  const { data: catalog } = useQuery({
    queryKey: ['learner', 'placement-catalog'],
    queryFn: () => api.placement.catalog(),
    enabled: activeStep === 1 && Boolean(onboardingState),
  });

  const placementIdForResult = onboardingState?.latestPlacementId ?? null;
  const { data: placementResult, isLoading: resultLoading } = useQuery({
    queryKey: ['learner', 'placement-result', placementIdForResult],
    queryFn: () => api.placement.result(placementIdForResult!),
    enabled: activeStep === 2 && placementIdForResult != null,
    refetchInterval: (q) => (q.state.data && !q.state.data.isGraded ? 3000 : false),
  });

  const { data: targetBandView, isLoading, error } = useQuery({
    queryKey: ['learner', 'target-band', learnerContext?.active?.code],
    queryFn: () => api.placement.targetBand(),
    enabled: activeStep === 3 || activeStep === 4 || !onboardingState,
  });

  useEffect(() => {
    if (targetBandView) {
      if (targetBandView.targetBandCode) {
        setTargetBandCode(targetBandView.targetBandCode);
      } else if (targetBandView.goal?.targetBandCode) {
        setTargetBandCode(targetBandView.goal.targetBandCode);
      }
      if (targetBandView.goal?.targetDate) {
        setTargetDate(targetBandView.goal.targetDate);
      }
      if (targetBandView.goal?.weeklyStudyHours != null) {
        setWeeklyHours(String(targetBandView.goal.weeklyStudyHours));
      }
    }
  }, [targetBandView]);

  const startPlacementMutation = useMutation({
    mutationFn: async (scope: PlacementScopeCode) => {
      const exam = catalog?.exams?.[0];
      return await api.placement.start({
        examId: exam?.examId ?? 0,
        scope,
      });
    },
    onSuccess: (res) => {
      void navigate({
        to: '/placement/take/$placementId',
        params: { placementId: String(res.placementId) },
      });
    },
    onError: (err: unknown) => {
      setFormError(err instanceof ApiClientError ? err.message : 'Unable to start placement exam.');
    },
  });

  const saveGoalMutation = useMutation({
    mutationFn: async (req: SetGoalRequest) => {
      return await api.placement.setGoal(req);
    },
    onSuccess: async (updatedView) => {
      queryClient.setQueryData(['learner', 'target-band', learnerContext?.active?.code], updatedView);
      await queryClient.invalidateQueries({ queryKey: ['learner', 'dashboard'] });
      await refreshOnboardingState();
      setFormError(null);
      if (onboardingState) {
        setStepOverride(4);
      } else {
        setSuccessMessage('Study goal saved! Redirecting to your dashboard...');
        setTimeout(() => {
          void navigate({ to: '/dashboard' });
        }, 800);
      }
    },
    onError: (err: unknown) => {
      setFormError(err instanceof ApiClientError ? err.message : 'Failed to save your study goal.');
    },
  });

  const generatePathMutation = useMutation({
    mutationFn: async () => {
      const band = targetBandCode || targetBandView?.targetBandCode || targetBandView?.availableBands?.[0]?.code;
      return await api.learningPath.generate({ targetBandCode: band });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['learner'] });
      await refreshOnboardingState();
      void navigate({ to: '/learning-path' });
    },
    onError: (err: unknown) => {
      setFormError(err instanceof ApiClientError ? err.message : 'Unable to generate learning path.');
    },
  });

  const handleExploreForNow = () => {
    if (typeof window !== 'undefined') {
      window.sessionStorage.setItem('ee_oobe_skip_session', '1');
      window.dispatchEvent(new CustomEvent('ee:oobe-skip-changed'));
    }
    void navigate({ to: '/dashboard' });
  };

  const handleSubmitGoal = (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    if (!targetBandCode) {
      setFormError('Please select a target band.');
      return;
    }
    const parsedHours = weeklyHours ? Number(weeklyHours) : null;
    if (parsedHours !== null && (Number.isNaN(parsedHours) || parsedHours < 1 || parsedHours > 80)) {
      setFormError('Weekly study hours must be between 1 and 80.');
      return;
    }

    saveGoalMutation.mutate({
      targetBandCode,
      currentBandCode: targetBandView?.currentBandCode ?? undefined,
      targetDate: targetDate || null,
      weeklyStudyHours: parsedHours,
    });
  };

  const renderStepHeader = () => {
    if (!onboardingState) return null;
    const steps = [
      { num: 1, label: 'Universal Placement' },
      { num: 2, label: 'Diagnostic Result' },
      { num: 3, label: 'Certificate & Goal' },
      { num: 4, label: 'Learning Path' },
    ];
    return (
      <div className="mb-6 flex flex-wrap items-center gap-2">
        {steps.map((s) => (
          <Chip
            key={s.num}
            size="sm"
            variant={activeStep === s.num ? 'primary' : 'soft'}
            color={activeStep > s.num ? 'success' : activeStep === s.num ? 'accent' : 'default'}
          >
            {s.num}. {s.label}
          </Chip>
        ))}
      </div>
    );
  };

  if (activeStep === 1 && onboardingState) {
    return (
      <div className="max-w-2xl mx-auto py-8 px-4">
        {renderStepHeader()}
        <Card className="bg-surface border border-border shadow-sm">
          <Card.Header className="flex flex-col items-start gap-2">
            <Chip size="sm" variant="soft" color="accent">
              <Sparkles className="w-3.5 h-3.5" />
              Step 1 · Universal Placement
            </Chip>
            <Card.Title className="text-2xl font-display font-bold">
              Choose your placement exam scope
            </Card.Title>
            <Card.Description>
              First sitting is free. Choose Listening &amp; Reading for an instant diagnostic or full AI-graded Speaking &amp; Writing assessment.
            </Card.Description>
          </Card.Header>
          <Card.Content className="space-y-6">
            {formError && (
              <Alert status="danger">
                <Alert.Indicator />
                <Alert.Content>
                  <Alert.Description>{formError}</Alert.Description>
                </Alert.Content>
              </Alert>
            )}

            <RadioGroup
              value={selectedScope}
              onChange={(val) => setSelectedScope(val as PlacementScopeCode)}
              aria-label="Placement exam scope"
            >
              <Radio value="listening_and_reading">
                <div className="flex flex-col">
                  <span className="font-semibold text-foreground">2 skills — Listening &amp; Reading</span>
                  <span className="text-xs text-muted">
                    Instant automatic score and cross-certificate band estimate.
                  </span>
                </div>
              </Radio>
              <Radio value="all_skills">
                <div className="flex flex-col">
                  <span className="font-semibold text-foreground">4 skills — Listening, Reading, Speaking &amp; Writing</span>
                  <span className="text-xs text-muted">
                    Includes AI-graded Speaking &amp; Writing tasks for complete CEFR placement.
                  </span>
                </div>
              </Radio>
            </RadioGroup>

            <div className="flex flex-wrap items-center justify-between gap-3 pt-2">
              <Button variant="ghost" type="button" onPress={handleExploreForNow}>
                <Compass className="w-4 h-4" />
                Explore for now
              </Button>
              {onboardingState.activePlacementId ? (
                <Button
                  variant="primary"
                  type="button"
                  onPress={() =>
                    navigate({
                      to: '/placement/take/$placementId',
                      params: { placementId: String(onboardingState.activePlacementId) },
                    })
                  }
                >
                  Resume placement
                  <ArrowRight className="w-4 h-4" />
                </Button>
              ) : (
                <Button
                  variant="primary"
                  type="button"
                  isDisabled={startPlacementMutation.isPending}
                  onPress={() => startPlacementMutation.mutate(selectedScope)}
                >
                  {startPlacementMutation.isPending ? <Spinner size="sm" /> : <ArrowRight className="w-4 h-4" />}
                  Start Universal Placement
                </Button>
              )}
            </div>
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (activeStep === 2 && onboardingState) {
    return (
      <div className="max-w-3xl mx-auto py-8 px-4">
        {renderStepHeader()}
        <Card className="bg-surface border border-border shadow-sm">
          <Card.Header className="flex flex-col items-start gap-2">
            <Chip size="sm" variant="soft" color="accent">
              Step 2 · Diagnostic Result
            </Chip>
            <Card.Title className="text-2xl font-display font-bold">
              Your Universal Placement Diagnostic
            </Card.Title>
            <Card.Description>
              Cross-certificate band mapping estimated from your placement performance.
            </Card.Description>
          </Card.Header>
          <Card.Content className="space-y-6">
            {resultLoading || !placementResult ? (
              <div className="flex items-center justify-center py-12 gap-3 text-muted">
                <Spinner size="md" />
                <span>Loading diagnostic result...</span>
              </div>
            ) : (
              <>
                {!placementResult.isGraded && (
                  <Alert status="accent">
                    <Alert.Indicator />
                    <Alert.Content>
                      <Alert.Title>AI Grading in Progress</Alert.Title>
                      <Alert.Description>
                        Your Speaking &amp; Writing responses are being graded. This view updates automatically.
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>
                )}

                <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
                  <div className="p-3 rounded-lg bg-surface-secondary border border-border">
                    <span className="text-xs text-muted block">Overall</span>
                    <span className="text-xl font-bold font-mono">
                      {placementResult.overallPct != null ? `${Math.round(placementResult.overallPct)}%` : '—'}
                    </span>
                  </div>
                  {placementResult.listeningPct != null && (
                    <div className="p-3 rounded-lg bg-surface-secondary border border-border">
                      <span className="text-xs text-muted block">Listening</span>
                      <span className="text-lg font-semibold font-mono">{Math.round(placementResult.listeningPct)}%</span>
                    </div>
                  )}
                  {placementResult.readingPct != null && (
                    <div className="p-3 rounded-lg bg-surface-secondary border border-border">
                      <span className="text-xs text-muted block">Reading</span>
                      <span className="text-lg font-semibold font-mono">{Math.round(placementResult.readingPct)}%</span>
                    </div>
                  )}
                  {placementResult.speakingPct != null && (
                    <div className="p-3 rounded-lg bg-surface-secondary border border-border">
                      <span className="text-xs text-muted block">Speaking</span>
                      <span className="text-lg font-semibold font-mono">{Math.round(placementResult.speakingPct)}%</span>
                    </div>
                  )}
                  {placementResult.writingPct != null && (
                    <div className="p-3 rounded-lg bg-surface-secondary border border-border">
                      <span className="text-xs text-muted block">Writing</span>
                      <span className="text-lg font-semibold font-mono">{Math.round(placementResult.writingPct)}%</span>
                    </div>
                  )}
                </div>

                {placementResult.certificatePreviews && placementResult.certificatePreviews.length > 0 && (
                  <div className="space-y-3">
                    <h3 className="text-sm font-semibold text-foreground">Estimated Band by Certificate</h3>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                      {placementResult.certificatePreviews.map((preview) => (
                        <Card key={preview.certId} variant="secondary" className="border border-border">
                          <Card.Content className="p-4 flex items-center justify-between gap-3">
                            <div>
                              <div className="font-semibold text-foreground">{preview.certCode}</div>
                              <div className="text-xs text-muted">{preview.certName}</div>
                              {placementResult.scope === 'listening_and_reading' && preview.skillCount === 4 && (
                                <span className="text-[11px] text-muted">Initial estimate from 2-skill scope</span>
                              )}
                            </div>
                            <div className="text-right">
                              <Chip size="sm" color="accent" variant="soft">
                                {preview.mappedBandCode}
                              </Chip>
                              <div className="text-xs text-muted mt-1">{preview.mappedBandName}</div>
                            </div>
                          </Card.Content>
                        </Card>
                      ))}
                    </div>
                  </div>
                )}

                <div className="flex justify-end pt-2">
                  <Button variant="primary" type="button" onPress={() => setStepOverride(3)}>
                    Continue to goal setup
                    <ArrowRight className="w-4 h-4" />
                  </Button>
                </div>
              </>
            )}
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (activeStep === 4 && onboardingState) {
    return (
      <div className="max-w-2xl mx-auto py-8 px-4">
        {renderStepHeader()}
        <Card className="bg-surface border border-border shadow-sm">
          <Card.Header className="flex flex-col items-start gap-2">
            <Chip size="sm" variant="soft" color="accent">
              Step 4 · Learning Path
            </Chip>
            <Card.Title className="text-2xl font-display font-bold">
              Generate your personalized Learning Path
            </Card.Title>
            <Card.Description>
              Build a 4-week adaptive study plan tailored to your placement diagnostic and target band ({targetBandCode || targetBandView?.targetBandCode || 'Target'}).
            </Card.Description>
          </Card.Header>
          <Card.Content className="space-y-4">
            {formError && (
              <Alert status="danger">
                <Alert.Indicator />
                <Alert.Content>
                  <Alert.Description>{formError}</Alert.Description>
                </Alert.Content>
              </Alert>
            )}
            <div className="flex items-center justify-between pt-2">
              <Button variant="ghost" type="button" onPress={() => setStepOverride(3)}>
                Back to goal
              </Button>
              <Button
                variant="primary"
                type="button"
                isDisabled={generatePathMutation.isPending}
                onPress={() => generatePathMutation.mutate()}
              >
                {generatePathMutation.isPending ? <Spinner size="sm" /> : <Sparkles className="w-4 h-4" />}
                Generate Learning Path (5 credits)
              </Button>
            </div>
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-[50vh]">
        <Spinner size="lg" />
      </div>
    );
  }

  if (error || !targetBandView) {
    return (
      <div className="max-w-2xl mx-auto py-12 px-4">
        <Alert status="danger">
          <AlertCircle className="w-5 h-5" />
          <Alert.Title>Unable to load goal setup</Alert.Title>
          <Alert.Description>
            {error instanceof Error ? error.message : 'Please try refreshing the page.'}
          </Alert.Description>
        </Alert>
      </div>
    );
  }

  const view: TargetBandViewDto = targetBandView;
  const currentGoal = view.goal;

  return (
    <div className="max-w-2xl mx-auto py-8 px-4">
      {renderStepHeader()}
      <div className="mb-8">
        <h1 className="text-3xl font-display font-bold text-foreground mb-2">
          Set your study goal · {view.activeCert.code}
        </h1>
        <p className="text-muted">
          Choose your target proficiency band and pacing schedule so we can build a tailored 4-week study plan.
        </p>
      </div>

      {learnerContext && learnerContext.certificates.length > 1 && (
        <div className="mb-6 flex flex-wrap items-center gap-2">
          <span className="text-xs font-medium text-muted mr-1">Certificate Track:</span>
          {learnerContext.certificates.map((cert) => (
            <Button
              key={cert.code}
              size="sm"
              variant={cert.code === view.activeCert.code ? 'primary' : 'secondary'}
              type="button"
              onPress={() => {
                if (cert.code !== view.activeCert.code) {
                  void switchCertificate(cert.code);
                }
              }}
            >
              {cert.code}
            </Button>
          ))}
        </div>
      )}

      <Card className="bg-surface border border-border shadow-sm">
        <Form onSubmit={handleSubmitGoal}>
          <Card.Header className="pb-4 border-b border-separator flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-xl bg-accent/10 flex items-center justify-center text-accent">
                <Target className="w-5 h-5" />
              </div>
              <div>
                <Card.Title className="text-lg">Target &amp; Pacing</Card.Title>
                <Card.Description>
                  {view.currentBandCode ? (
                    <>
                      Current assessed level:{' '}
                      <span>
                        Band {view.currentBandCode} ({view.currentBandName ?? ''})
                      </span>
                    </>
                  ) : (
                    'Take a placement test anytime to establish your baseline'
                  )}
                </Card.Description>
              </div>
            </div>
            {view.currentBandCode && (
              <Chip variant="soft" color="accent" className="font-mono font-bold">
                <Trophy className="w-3.5 h-3.5 mr-1" />
                {view.currentBandCode}
              </Chip>
            )}
          </Card.Header>

          <Card.Content className="pt-6 space-y-6">
            {formError && (
              <Alert status="danger">
                <AlertCircle className="w-4 h-4" />
                <Alert.Description>{formError}</Alert.Description>
              </Alert>
            )}

            {successMessage && (
              <Alert status="success">
                <CheckCircle2 className="w-4 h-4" />
                <Alert.Description>{successMessage}</Alert.Description>
              </Alert>
            )}

            <div className="space-y-2">
              <Label className="text-sm font-medium text-foreground">Target Band</Label>
              <div className="flex flex-wrap gap-2">
                {view.availableBands.map((band) => (
                  <Button
                    key={band.code}
                    type="button"
                    size="sm"
                    variant={targetBandCode === band.code ? 'primary' : 'secondary'}
                    onPress={() => setTargetBandCode(band.code)}
                  >
                    {band.code} — {band.name}
                  </Button>
                ))}
              </div>
              <Select
                selectedKey={targetBandCode || null}
                onSelectionChange={(key: Key | null) => {
                  if (key != null) {
                    setTargetBandCode(String(key));
                  }
                }}
                aria-label="Target Band"
                placeholder="Select your target band"
                className="w-full"
                isRequired
              >
                <Select.Trigger>
                  <Select.Value />
                  <Select.Indicator />
                </Select.Trigger>
                <Select.Popover>
                  <ListBox>
                    {view.availableBands.map((band) => (
                      <ListBox.Item key={band.code} id={band.code} textValue={`${band.code} — ${band.name}`}>
                        <div className="flex items-center justify-between w-full">
                          <span className="font-semibold">{band.code} — {band.name}</span>
                          {band.minTotal != null && band.maxTotal != null && (
                            <span className="text-xs text-muted font-mono">
                              {band.minTotal}–{band.maxTotal}
                            </span>
                          )}
                        </div>
                      </ListBox.Item>
                    ))}
                  </ListBox>
                </Select.Popover>
              </Select>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <TextField
                name="targetDate"
                value={targetDate}
                onChange={setTargetDate}
              >
                <Label className="text-sm font-medium text-foreground flex items-center gap-1.5">
                  <Calendar className="w-4 h-4 text-muted" />
                  Target Exam Date (Optional)
                </Label>
                <Input type="date" />
                <FieldError />
              </TextField>

              <TextField
                name="weeklyHours"
                value={weeklyHours}
                onChange={setWeeklyHours}
              >
                <Label className="text-sm font-medium text-foreground flex items-center gap-1.5">
                  <Clock className="w-4 h-4 text-muted" />
                  Weekly Study Hours (Optional)
                </Label>
                <Input type="number" min={1} max={80} step={0.5} placeholder="e.g. 6" />
                <FieldError />
              </TextField>
            </div>

            {currentGoal && currentGoal.status && (
              <div className="p-4 rounded-xl bg-surface-secondary border border-border space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-mono font-bold uppercase text-muted">Pacing Feasibility</span>
                  <span
                    className={`text-xs px-2.5 py-0.5 rounded font-mono font-bold ${
                      currentGoal.status === 'feasible'
                        ? 'bg-success text-white'
                        : currentGoal.status === 'stretch'
                        ? 'bg-warning text-foreground'
                        : currentGoal.status === 'unrealistic'
                        ? 'bg-danger text-white'
                        : 'bg-surface text-muted'
                    }`}
                  >
                    {formatFeasibilityStatus(currentGoal.status)}
                  </span>
                </div>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-2 text-xs font-mono">
                  {currentGoal.requiredHours != null && (
                    <div>
                      <span className="text-muted block">Required</span>
                      <span className="font-bold text-foreground">{currentGoal.requiredHours}h</span>
                    </div>
                  )}
                  {currentGoal.availableHours != null && (
                    <div>
                      <span className="text-muted block">Available</span>
                      <span className="font-bold text-foreground">{currentGoal.availableHours}h</span>
                    </div>
                  )}
                  {currentGoal.earliestFeasibleDate && (
                    <div>
                      <span className="text-muted block">Earliest feasible</span>
                      <span className="font-bold text-foreground">{currentGoal.earliestFeasibleDate}</span>
                    </div>
                  )}
                </div>
              </div>
            )}

            <div className="flex flex-wrap items-center justify-between gap-2 pt-2">
              <Button
                variant="ghost"
                type="button"
                onPress={() => navigate({ to: '/dashboard' })}
              >
                Cancel
              </Button>
              <div className="flex items-center gap-2">
                {onboardingState && (
                  <Button
                    variant="secondary"
                    type="button"
                    onPress={() => setStepOverride(4)}
                  >
                    Continue to learning path
                    <ArrowRight className="w-4 h-4" />
                  </Button>
                )}
                <Button
                  variant="primary"
                  type="submit"
                  isDisabled={saveGoalMutation.isPending || !targetBandCode}
                  className="gap-2"
                >
                  {saveGoalMutation.isPending ? <Spinner size="sm" /> : <ArrowRight className="w-4 h-4" />}
                  Save goal &amp; continue
                </Button>
              </div>
            </div>
          </Card.Content>
        </Form>
      </Card>
    </div>
  );
}
