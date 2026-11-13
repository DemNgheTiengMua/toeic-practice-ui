import React from 'react';
import { parseSafeInlineSegments } from '../../lib/security';

export function formatPathStepStatus(status?: string | null): string {
  switch (status?.toLowerCase()) {
    case 'locked':
      return 'Locked';
    case 'available':
      return 'Available';
    case 'in_progress':
      return 'In progress';
    case 'done':
      return 'Done';
    default:
      return status ?? 'Step status not reported';
  }
}

export function formatPathStatus(status?: string | null): string {
  switch (status?.toLowerCase()) {
    case 'active':
      return 'Active';
    case 'completed':
      return 'Completed';
    case 'reset':
      return 'Reset';
    default:
      return status ?? 'Path status not reported';
  }
}

export function formatFeasibilityStatus(status?: string | null): string {
  switch (status?.toLowerCase()) {
    case 'feasible':
      return 'Reachable at this pace';
    case 'stretch':
      return 'A stretch at this pace';
    case 'unrealistic':
      return 'Out of reach at this pace';
    default:
      return 'Cannot judge';
  }
}

export function formatRecommendationSeverity(severity?: string | null): {
  label: string;
  badgeClass: string;
} {
  switch (severity?.toLowerCase()) {
    case 'major':
      return { label: 'Priority', badgeClass: 'bg-danger text-white' };
    case 'minor':
      return { label: 'Focus', badgeClass: 'bg-warning text-foreground' };
    case 'info':
    default:
      return { label: 'Tip', badgeClass: 'bg-accent text-white' };
  }
}

export function formatAiInsightStatus(status?: string | null): string {
  switch (status?.toLowerCase()) {
    case 'pending':
      return 'Being written';
    case 'done':
      return 'Ready';
    case 'failed':
      return 'Could not be written';
    default:
      return 'Insight status not reported';
  }
}

export function formatAiInsightTrend(trend?: string | null): {
  label: string;
  badgeClass: string;
} {
  switch (trend?.toLowerCase()) {
    case 'improving':
      return { label: 'Improving', badgeClass: 'bg-success text-white' };
    case 'flat':
      return { label: 'Holding steady', badgeClass: 'bg-surface-secondary text-foreground' };
    case 'declining':
      return { label: 'Declining', badgeClass: 'bg-danger text-white' };
    default:
      return { label: 'Trend not reported', badgeClass: 'bg-surface-secondary text-muted' };
  }
}

export function formatModality(modality?: string | null): string {
  switch (modality?.toLowerCase()) {
    case 'listening':
      return 'Listening';
    case 'reading':
      return 'Reading';
    case 'speaking':
      return 'Speaking';
    case 'writing':
      return 'Writing';
    default:
      return modality ?? 'Skill';
  }
}

export function formatStepType(stepType?: string | null): string {
  switch (stepType?.toLowerCase()) {
    case 'lesson':
      return 'Lesson';
    case 'practice':
      return 'Practice drill';
    case 'mock_exam':
      return 'Mock exam';
    default:
      return stepType ?? 'Step';
  }
}
