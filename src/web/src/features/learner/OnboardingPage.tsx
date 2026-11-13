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
  Select,
  Spinner,
  TextField,
} from '@heroui/react';
import type { Key } from '@heroui/react';
import { AlertCircle, ArrowRight, Calendar, CheckCircle2, Clock, Target, Trophy } from 'lucide-react';
import { api, ApiClientError, TargetBandViewDto, SetGoalRequest } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { formatFeasibilityStatus } from './learnerHelpers';

export function OnboardingPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { learnerContext, switchCertificate } = useAuth();

  const [targetBandCode, setTargetBandCode] = useState<string>('');
  const [targetDate, setTargetDate] = useState<string>('');
  const [weeklyHours, setWeeklyHours] = useState<string>('5');
  const [formError, setFormError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const { data: targetBandView, isLoading, error } = useQuery({
    queryKey: ['learner', 'target-band', learnerContext?.active?.code],
    queryFn: () => api.placement.targetBand(),
  });

  // Sync initial state from targetBandView when loaded
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

  const saveGoalMutation = useMutation({
    mutationFn: async (req: SetGoalRequest) => {
      return await api.placement.setGoal(req);
    },
    onSuccess: (res) => {
      setSuccessMessage('Your target and study plan have been saved successfully.');
      queryClient.invalidateQueries({ queryKey: ['learner'] });
      setTimeout(() => {
        navigate({ to: '/dashboard' });
      }, 800);
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setFormError(err.detail || err.title || 'Could not save study goal.');
      } else {
        setFormError('An unexpected error occurred while saving your goal.');
      }
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    if (!targetBandCode) {
      setFormError('Please select a target band.');
      return;
    }

    const hoursNum = parseFloat(weeklyHours);
    if (isNaN(hoursNum) || hoursNum < 0.5 || hoursNum > 25) {
      setFormError('Weekly study hours must be between 0.5 and 25 hours.');
      return;
    }

    saveGoalMutation.mutate({
      currentBandCode: targetBandView?.currentBandCode ?? null,
      targetBandCode,
      targetDate: targetDate || null,
      weeklyStudyHours: hoursNum,
    });
  };

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[350px] space-y-3">
        <Spinner size="lg" />
        <p className="text-sm text-muted font-mono">Loading goal setup…</p>
      </div>
    );
  }

  if (error || !targetBandView) {
    return (
      <div className="max-w-2xl mx-auto py-8">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Could not load target band view</Alert.Title>
            <Alert.Description>
              {error instanceof Error ? error.message : 'Please check your connection and try again.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  const activeCert = targetBandView.activeCert;
  const availableBands = targetBandView.availableBands ?? [];
  const currentGoal = targetBandView.goal;

  return (
    <div className="max-w-3xl mx-auto py-6 space-y-6">
      <div>
        <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
          Set your study goal · {activeCert.code}
        </h1>
        <p className="text-sm text-muted mt-1.5 mb-0">
          Choose your target score band, test date, and weekly study pace for {activeCert.name}.
        </p>
      </div>

      {successMessage && (
        <Alert status="success">
          <Alert.Indicator>
            <CheckCircle2 className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Goal saved</Alert.Title>
            <Alert.Description>{successMessage}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {formError && (
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Could not update goal</Alert.Title>
            <Alert.Description>{formError}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      <Card>
        <Form onSubmit={handleSubmit} validationBehavior="aria">
          <Card.Content className="space-y-6 p-6">
            {/* Certificate Switcher / Info */}
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 rounded-lg bg-surface-secondary border border-border">
              <div>
                <span className="text-xs uppercase font-mono font-bold text-muted">Active certificate</span>
                <div className="text-sm font-semibold text-foreground">
                  {activeCert.code} — {activeCert.name}
                </div>
              </div>
              {learnerContext && (learnerContext.certificates ?? learnerContext.available ?? []).length > 1 && (
                <div className="w-full sm:w-[200px]">
                  <Select
                    aria-label="Switch active certificate"
                    value={activeCert.code as Key}
                    onChange={(val) => {
                      if (typeof val === 'string' && val !== activeCert.code) {
                        void switchCertificate(val);
                      }
                    }}
                  >
                    <Select.Trigger className="h-8 text-xs">
                      <Select.Value />
                      <Select.Indicator />
                    </Select.Trigger>
                    <Select.Popover>
                      <ListBox>
                        {(learnerContext.certificates ?? learnerContext.available ?? []).map((c) => (
                          <ListBox.Item key={c.code} id={c.code} textValue={`${c.code} - ${c.name}`}>
                            {c.code} — {c.name}
                          </ListBox.Item>
                        ))}
                      </ListBox>
                    </Select.Popover>
                  </Select>
                </div>
              )}
            </div>

            {/* Current Level / Standing */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="p-3.5 rounded-lg border border-border bg-surface">
                <span className="text-xs text-muted block mb-1">Current assessed level</span>
                <div className="font-mono text-base font-bold text-foreground">
                  {targetBandView.currentBandCode ? (
                    <span>
                      Band {targetBandView.currentBandCode}{' '}
                      {targetBandView.currentBandName && `(${targetBandView.currentBandName})`}
                    </span>
                  ) : (
                    <span className="text-muted">Not placed yet</span>
                  )}
                </div>
              </div>

              <div className="p-3.5 rounded-lg border border-border bg-surface">
                <span className="text-xs text-muted block mb-1">Scale total score</span>
                <div className="font-mono text-base font-bold text-foreground">
                  {targetBandView.currentTotalScore != null ? (
                    `${targetBandView.currentTotalScore} points`
                  ) : (
                    <span className="text-muted">--</span>
                  )}
                </div>
              </div>
            </div>

            {/* Target Band Selection */}
            <div className="space-y-2">
              <label className="text-sm font-semibold text-foreground flex items-center gap-1.5">
                <Trophy className="w-4 h-4 text-accent" />
                Select your target band
              </label>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                {availableBands.map((band) => {
                  const isSelected = targetBandCode === band.code;
                  return (
                    <button
                      key={band.code}
                      type="button"
                      onClick={() => setTargetBandCode(band.code)}
                      className={`p-3 text-left rounded-lg border transition-all ${
                        isSelected
                          ? 'border-accent bg-accent/5 ring-2 ring-accent'
                          : 'border-border bg-surface hover:bg-surface-secondary'
                      }`}
                    >
                      <div className="flex items-center justify-between">
                        <span className="font-mono text-sm font-bold text-foreground">{band.code}</span>
                        <span className="font-mono text-xs text-muted">
                          {band.minTotal}–{band.maxTotal} pts
                        </span>
                      </div>
                      <div className="text-xs font-medium text-foreground mt-0.5">{band.name}</div>
                      {band.description && (
                        <p className="text-[11px] text-muted m-0 mt-1 line-clamp-2">{band.description}</p>
                      )}
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Target Exam Date & Weekly Study Hours */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 pt-2">
              <TextField
                name="targetDate"
                value={targetDate}
                onChange={(val) => setTargetDate(val)}
              >
                <Label className="text-xs font-semibold text-foreground flex items-center gap-1">
                  <Calendar className="w-3.5 h-3.5 text-muted" /> Target exam date (optional)
                </Label>
                <Input type="date" />
              </TextField>

              <TextField
                isRequired
                name="weeklyStudyHours"
                value={weeklyHours}
                onChange={(val) => setWeeklyHours(val)}
              >
                <Label className="text-xs font-semibold text-foreground flex items-center gap-1">
                  <Clock className="w-3.5 h-3.5 text-muted" /> Weekly study hours (0.5 – 25)
                </Label>
                <Input type="number" step="0.5" min="0.5" max="25" />
              </TextField>
            </div>

            {/* Existing Feasibility Verdict from Goal if available */}
            {currentGoal && (
              <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-2">
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

            <div className="flex items-center justify-between pt-2">
              <Button
                variant="ghost"
                type="button"
                onPress={() => navigate({ to: '/dashboard' })}
              >
                Cancel
              </Button>
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
          </Card.Content>
        </Form>
      </Card>
    </div>
  );
}
