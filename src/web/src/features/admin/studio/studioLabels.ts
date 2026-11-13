/**
 * Sentence-case human-readable label mappers for wire enums across admin studios
 * (AGENTS.md §4, board #23, #34, #37).
 */

export function formatExamStatusLabel(status: string | null | undefined): string {
  switch (status?.toLowerCase()) {
    case 'draft':
      return 'Draft';
    case 'published':
      return 'Published';
    case 'withdrawn':
      return 'Withdrawn';
    case 'hidden':
      return 'Hidden';
    default:
      return status ? status.replace(/_/g, ' ') : 'Unknown';
  }
}

export function formatExamPurposeLabel(purpose: string | null | undefined): string {
  switch (purpose?.toLowerCase()) {
    case 'full_mock':
      return 'Full mock exam';
    case 'mini_mock':
      return 'Mini mock exam';
    case 'placement':
      return 'Placement test';
    case 'diagnostic':
      return 'Diagnostic';
    case 'practice':
      return 'Practice drill';
    case 'speaking_practice':
      return 'Speaking practice';
    case 'writing_practice':
      return 'Writing practice';
    default:
      return purpose ? purpose.replace(/_/g, ' ') : 'General';
  }
}

export function formatModalityLabel(modality: string | null | undefined): string {
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
      return modality ? modality.replace(/_/g, ' ') : 'Unknown';
  }
}

export function formatQuestionTypeLabel(type: string | null | undefined): string {
  switch (type?.toLowerCase()) {
    case 'mcq':
      return 'Multiple choice (4 options)';
    case 'mcq_3':
      return 'Multiple choice (3 options)';
    case 'multi_select':
      return 'Multiple select';
    case 'inline_completion':
      return 'Inline completion';
    case 'dropdown':
      return 'Dropdown select';
    case 'drag_drop':
      return 'Drag and drop';
    case 'free_response':
      return 'Free response';
    default:
      return type ? type.replace(/_/g, ' ') : 'Standard question';
  }
}

export function formatNavigationModeLabel(mode: string | null | undefined): string {
  switch (mode?.toLowerCase()) {
    case 'full_sandbox':
      return 'Full sandbox';
    case 'linear_locked':
      return 'Linear locked';
    case 'audio_driven':
      return 'Audio driven';
    case 'system_paced_auto':
      return 'System paced auto';
    default:
      return mode ? mode.replace(/_/g, ' ') : 'Standard';
  }
}

export function formatLayoutTypeLabel(layout: string | null | undefined): string {
  switch (layout?.toLowerCase()) {
    case 'standard_vertical':
      return 'Standard vertical';
    case 'split_horizontal':
      return 'Split horizontal';
    case 'side_by_side':
      return 'Side by side';
    case 'prompt_first':
      return 'Prompt first';
    default:
      return layout ? layout.replace(/_/g, ' ') : 'Standard';
  }
}

export function formatContentTierLabel(tier: string | null | undefined): string {
  switch (tier?.toLowerCase()) {
    case 'demo':
      return 'Demo stock';
    case 'live':
      return 'Production stock';
    default:
      return tier ? tier.replace(/_/g, ' ') : 'Demo';
  }
}

export function formatLifecycleStatusLabel(status: string | null | undefined): string {
  switch (status?.toLowerCase()) {
    case 'draft':
      return 'Draft';
    case 'active':
      return 'Active';
    case 'retired':
      return 'Retired';
    case 'quarantined':
      return 'Quarantined';
    default:
      return status ? status.replace(/_/g, ' ') : 'Draft';
  }
}

export function formatRubricLadderOutcomeLabel(outcome: string | null | undefined): string {
  switch (outcome?.toLowerCase()) {
    case 'accepted':
      return 'Calibrated: every adjacent band separated and the trap stayed under its ceiling.';
    case 'not_monotonic':
      return 'Not monotonic: a higher band scored no better than the one below it, so that segment is flat.';
    case 'separation':
      return 'Bands too close: adjacent bands are nearer than the ladder\'s minimum separation.';
    case 'trap_ceiling':
      return 'Verbose trap above its ceiling: the rubric rewards length it should not reward.';
    case 'not_plotted':
    default:
      return 'Not plotted: the ladder has not run against this rubric, so nothing is known about its separation.';
  }
}

export function formatRubricBandLabel(band: number): string {
  switch (band) {
    case 0:
      return 'Pre-A1';
    case 1:
      return 'A1';
    case 2:
      return 'A2';
    case 3:
      return 'B1';
    case 4:
      return 'B2';
    case 5:
      return 'C1';
    case 6:
      return 'C2';
    default:
      return `Band ${band}`;
  }
}

export function formatImportBatchStatusLabel(status: string | null | undefined): string {
  switch (status?.toLowerCase()) {
    case 'staged':
      return 'Staged';
    case 'processing':
      return 'Processing';
    case 'completed':
      return 'Completed';
    case 'failed':
      return 'Failed';
    case 'partially_accepted':
      return 'Partially accepted';
    default:
      return status ? status.replace(/_/g, ' ') : 'Staged';
  }
}
