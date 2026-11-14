import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  AlertDialog,
  Button,
  Card,
  Chip,
  Description,
  Label,
  Modal,
  ProgressBar,
  Radio,
  RadioGroup,
  Spinner,
  TextArea,
  TextField,
  ToggleButton,
} from '@heroui/react';
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  Bookmark,
  CheckCircle2,
  Clock,
  Lock,
  RefreshCw,
  Save,
  Send,
  WifiOff,
} from 'lucide-react';
import type {
  AttemptPhaseDto,
  AttemptQuestionDto,
  AttemptTakeDto,
  NavigatorItemDto,
  PlacementTakeViewDto,
  SaveAnswerResultDto,
} from '@/lib/api';
import {
  countWords,
  formatTimerSeconds,
  parseSafeInlineSegments,
  toBrowserMediaUrl,
} from '@/lib/security';
import {
  dequeueOfflineMutation,
  enqueueOfflineMutation,
  formatSavePlacard,
  readOfflineQueue,
  type SittingKind,
} from '@/lib/sitting-queue';
import {
  buildSectionSubmitSummary,
  canNavigateBackInSitting,
  classifyQuestionKind,
  needsLockBoundaryServerFetch,
  reconcileRequestedQuestionOrder,
} from './sitting-helpers';
import { ExamAudioPlayer } from './ExamAudioPlayer';
import { SpeakingResponsePane } from './SpeakingResponsePane';
import type { ReactNode } from 'react';

export type UnifiedTakeView = PlacementTakeViewDto | AttemptTakeDto;

export interface ExamSittingShellProps {
  sittingKind: SittingKind;
  sittingId: number;
  attemptId: number;
  examName: string;
  certCode: string;
  initialTake: UnifiedTakeView;
  initialRequestedOrder?: number | null;
  basePath: string;
  onFetchQuestion: (displayOrder: number) => Promise<UnifiedTakeView>;
  onSaveAnswer: (
    questionId: number,
    payload: {
      selectedOptionId?: number | null;
      isFlagged: boolean;
      responseText?: string | null;
      selectedOptionIdsJson?: string | null;
    },
  ) => Promise<SaveAnswerResultDto>;
  onSaveDraft?: (
    questionId: number,
    responseText: string,
  ) => Promise<{ savedAt?: string; wordCount?: number }>;
  onUploadAudio?: (
    questionId: number,
    blob: Blob,
    fileName: string,
    durationMs?: number,
  ) => Promise<{
    audioPath?: string | null;
    responseAudioPath?: string | null;
    durationMs?: number | null;
    savedAt?: string;
  }>;
  onRecordInterruption?: (payload: {
    questionId: number;
    sessionToken: string;
    capturedMs: number;
  }) => Promise<{
    interruptions: number;
    outcome: string;
    replacementQuestionId?: number | null;
  }>;
  onSubmit: (isAutoSubmit: boolean) => Promise<void>;
  onSaveExit?: () => void;
  /** Render prop / hook override for speaking response pane (wired in Slice 3) */
  renderSpeakingPane?: (ctx: {
    question: AttemptQuestionDto;
    attemptId: number;
    sittingKind: SittingKind;
    sittingId: number;
    isOnline: boolean;
    onAudioSaved: (audioPath: string, durationMs?: number) => void;
    onNextQuestion: () => void;
    onUploadAudio?: ExamSittingShellProps['onUploadAudio'];
    onRecordInterruption?: ExamSittingShellProps['onRecordInterruption'];
  }) => ReactNode;
}

function isNetworkFailure(err: unknown): boolean {
  if (!navigator.onLine) return true;
  if (err instanceof TypeError) return true;
  if (err && typeof err === 'object' && 'status' in err) {
    const status = (err as { status?: number }).status;
    return status === 0 || status === 502 || status === 503 || status === 504;
  }
  return false;
}

function renderSafeParagraphs(text: string) {
  return text.split(/\n{2,}/).map((para, pIdx) => (
    <p key={pIdx} className="leading-relaxed text-foreground">
      {parseSafeInlineSegments(para).map((seg, sIdx) =>
        seg.type === 'bold' ? (
          <strong key={sIdx} className="font-semibold">
            {seg.value}
          </strong>
        ) : (
          <span key={sIdx}>{seg.value}</span>
        ),
      )}
    </p>
  ));
}

export function ExamSittingShell({
  sittingKind,
  sittingId,
  attemptId,
  examName,
  certCode,
  initialTake,
  initialRequestedOrder,
  basePath,
  onFetchQuestion,
  onSaveAnswer,
  onSaveDraft,
  onUploadAudio,
  onRecordInterruption,
  onSubmit,
  onSaveExit,
  renderSpeakingPane,
}: ExamSittingShellProps) {
  const initialQuestionsMap = useMemo(() => {
    const map = new Map<number, AttemptQuestionDto>();
    for (const q of initialTake.questions ?? []) {
      map.set(q.displayOrder, q);
    }
    if (initialTake.currentQuestion) {
      map.set(initialTake.currentQuestion.displayOrder, initialTake.currentQuestion);
    }
    return map;
  }, [initialTake]);

  const [questionsMap, setQuestionsMap] = useState<Map<number, AttemptQuestionDto>>(
    () => initialQuestionsMap,
  );
  const [navigatorItems, setNavigatorItems] = useState<NavigatorItemDto[]>(
    () => initialTake.navigator ?? [],
  );
  const [phases, setPhases] = useState<AttemptPhaseDto[]>(() => initialTake.phases ?? []);

  const initialOrderResolved = useMemo(() => {
    const fallbackOrder = initialTake.currentQuestion?.displayOrder ?? 1;
    const oneWay = Boolean(
      initialTake.phases?.some(
        (p) =>
          p.lockNavigationOneWay &&
          p.skillCode.toLowerCase() === initialTake.currentQuestion?.skillCode.toLowerCase(),
      ),
    );
    return reconcileRequestedQuestionOrder(initialRequestedOrder ?? fallbackOrder, {
      navigator: initialTake.navigator ?? [],
      currentDisplayOrder: fallbackOrder,
      totalQuestions: initialTake.totalQuestions,
      oneWayForwardOnly: oneWay,
    });
  }, [initialRequestedOrder, initialTake]);

  const [currentOrder, setCurrentOrder] = useState<number>(initialOrderResolved.resolvedOrder);
  const [reconciliationNotice, setReconciliationNotice] = useState<string | null>(() =>
    initialOrderResolved.wasReconciled
      ? `Navigated to question ${initialOrderResolved.resolvedOrder} (locked or out-of-range index clamped).`
      : null,
  );

  const [remainingSeconds, setRemainingSeconds] = useState<number>(
    Math.max(0, initialTake.remainingSeconds),
  );
  const [isOnline, setIsOnline] = useState<boolean>(() =>
    typeof navigator !== 'undefined' ? navigator.onLine : true,
  );
  const [queuedCount, setQueuedCount] = useState<number>(
    () => readOfflineQueue(sittingKind, sittingId).length,
  );
  const [isSaving, setIsSaving] = useState(false);
  const [waitingForBoundary, setWaitingForBoundary] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [isLoadingQuestion, setIsLoadingQuestion] = useState(false);
  const [isSubmitModalOpen, setIsSubmitModalOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [pendingBoundaryOrder, setPendingBoundaryOrder] = useState<number | null>(null);

  // Sync URL query ?q= on initial reconciliation without triggering navigation
  useEffect(() => {
    try {
      window.history.replaceState(null, '', `${basePath}?q=${currentOrder}`);
    } catch {
      // Ignore history errors in restricted environments
    }
  }, [basePath, currentOrder]);

  // Countdown clock + auto-submit once at 0
  const autoSubmittedRef = useRef(false);
  useEffect(() => {
    if (remainingSeconds <= 0) {
      if (!autoSubmittedRef.current) {
        autoSubmittedRef.current = true;
        void onSubmit(true);
      }
      return;
    }
    const timer = window.setInterval(() => {
      setRemainingSeconds((prev) => {
        if (prev <= 1) {
          window.clearInterval(timer);
          if (!autoSubmittedRef.current) {
            autoSubmittedRef.current = true;
            void onSubmit(true);
          }
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
    return () => window.clearInterval(timer);
  }, [remainingSeconds, onSubmit]);

  const currentQuestion = questionsMap.get(currentOrder) ?? initialTake.currentQuestion;
  const allQuestions = useMemo(
    () => Array.from(questionsMap.values()).sort((a, b) => a.displayOrder - b.displayOrder),
    [questionsMap],
  );

  const totalQuestions = initialTake.totalQuestions || navigatorItems.length || allQuestions.length;
  const answeredCount = useMemo(
    () => navigatorItems.filter((n) => n.isAnswered).length,
    [navigatorItems],
  );
  const flaggedCount = useMemo(
    () => navigatorItems.filter((n) => n.isFlagged).length,
    [navigatorItems],
  );

  const [draftText, setDraftText] = useState<string>(currentQuestion?.responseText ?? '');
  useEffect(() => {
    setDraftText(currentQuestion?.responseText ?? '');
  }, [currentQuestion?.questionId]);

  const flushOfflineQueue = useCallback(async (): Promise<boolean> => {
    const queue = readOfflineQueue(sittingKind, sittingId);
    if (queue.length === 0) {
      setWaitingForBoundary(false);
      return true;
    }

    setIsSaving(true);
    try {
      for (const item of queue) {
        await onSaveAnswer(item.questionId, {
          selectedOptionId: item.selectedOptionId ?? null,
          isFlagged: Boolean(item.isFlagged),
          responseText: item.responseText ?? null,
        });
        const remaining = dequeueOfflineMutation(sittingKind, sittingId, item.questionId);
        setQueuedCount(remaining.length);
      }
      setWaitingForBoundary(false);
      setActionError(null);
      return true;
    } catch {
      setQueuedCount(readOfflineQueue(sittingKind, sittingId).length);
      return false;
    } finally {
      setIsSaving(false);
    }
  }, [sittingKind, sittingId, onSaveAnswer]);

  useEffect(() => {
    const handleOnline = () => {
      setIsOnline(true);
      void flushOfflineQueue();
    };
    const handleOffline = () => {
      setIsOnline(false);
    };
    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
    };
  }, [flushOfflineQueue]);

  const applyLocalQuestionUpdate = useCallback(
    (
      questionId: number,
      patch: Partial<AttemptQuestionDto>,
      navPatch: Partial<NavigatorItemDto>,
    ) => {
      setQuestionsMap((prev) => {
        const next = new Map(prev);
        for (const [order, q] of next.entries()) {
          if (q.questionId === questionId) {
            next.set(order, { ...q, ...patch });
          }
        }
        return next;
      });
      setNavigatorItems((prev) =>
        prev.map((item) => (item.questionId === questionId ? { ...item, ...navPatch } : item)),
      );
    },
    [],
  );

  const persistAnswerMutation = useCallback(
    async (
      question: AttemptQuestionDto,
      payload: {
        selectedOptionId?: number | null;
        isFlagged: boolean;
        responseText?: string | null;
        selectedOptionIdsJson?: string | null;
      },
      answered: boolean,
    ) => {
      setIsSaving(true);
      setActionError(null);
      try {
        if (typeof navigator !== 'undefined' && !navigator.onLine) {
          throw new TypeError('Offline');
        }
        const res = await onSaveAnswer(question.questionId, payload);
        if (res.remainingSeconds != null) {
          setRemainingSeconds(res.remainingSeconds);
        }
        if (res.isAutoSubmitted) {
          await onSubmit(true);
          return;
        }
        const remaining = dequeueOfflineMutation(sittingKind, sittingId, question.questionId);
        setQueuedCount(remaining.length);
        setWaitingForBoundary(false);
      } catch (err) {
        if (isNetworkFailure(err)) {
          const nextQueue = enqueueOfflineMutation(sittingKind, sittingId, {
            questionId: question.questionId,
            selectedOptionId: payload.selectedOptionId ?? null,
            isFlagged: payload.isFlagged,
            responseText: payload.responseText ?? null,
          });
          setQueuedCount(nextQueue.length);
        } else {
          setActionError(
            err instanceof Error ? err.message : 'Unable to save answer. Please try again.',
          );
        }
      } finally {
        setIsSaving(false);
        applyLocalQuestionUpdate(
          question.questionId,
          {
            selectedOptionId: payload.selectedOptionId ?? question.selectedOptionId,
            isFlagged: payload.isFlagged,
            responseText: payload.responseText ?? question.responseText,
          },
          {
            isAnswered: answered,
            isFlagged: payload.isFlagged,
          },
        );
      }
    },
    [sittingKind, sittingId, onSaveAnswer, onSubmit, applyLocalQuestionUpdate],
  );

  const handleMcqSelect = useCallback(
    (optionValue: string) => {
      if (!currentQuestion) return;
      const optionId = Number(optionValue);
      if (!Number.isFinite(optionId)) return;
      applyLocalQuestionUpdate(
        currentQuestion.questionId,
        { selectedOptionId: optionId },
        { isAnswered: true },
      );
      void persistAnswerMutation(
        currentQuestion,
        {
          selectedOptionId: optionId,
          isFlagged: currentQuestion.isFlagged,
        },
        true,
      );
    },
    [currentQuestion, applyLocalQuestionUpdate, persistAnswerMutation],
  );

  const handleMultiSelectToggle = useCallback(
    (optionId: number) => {
      if (!currentQuestion) return;
      const currentIds = currentQuestion.selectedOptionIds ?? [];
      const exists = currentIds.includes(optionId);
      let nextIds: number[];
      if (exists) {
        nextIds = currentIds.filter((id) => id !== optionId);
      } else {
        const limit = currentQuestion.maxSelections ?? Number.MAX_SAFE_INTEGER;
        if (currentIds.length >= limit) {
          nextIds = [...currentIds.slice(1), optionId];
        } else {
          nextIds = [...currentIds, optionId];
        }
      }
      applyLocalQuestionUpdate(
        currentQuestion.questionId,
        { selectedOptionIds: nextIds },
        { isAnswered: nextIds.length > 0 },
      );
      void persistAnswerMutation(
        currentQuestion,
        {
          selectedOptionId: nextIds[0] ?? null,
          isFlagged: currentQuestion.isFlagged,
          selectedOptionIdsJson: JSON.stringify(nextIds),
        },
        nextIds.length > 0,
      );
    },
    [currentQuestion, applyLocalQuestionUpdate, persistAnswerMutation],
  );

  const handleToggleFlag = useCallback(
    (isSelected: boolean) => {
      if (!currentQuestion) return;
      const qKind = classifyQuestionKind(currentQuestion);
      const isAnswered =
        qKind === 'speaking'
          ? Boolean(currentQuestion.responseAudioPath)
          : qKind === 'writing'
            ? countWords(draftText) > 0
            : qKind === 'mcq_multi'
              ? (currentQuestion.selectedOptionIds?.length ?? 0) > 0
              : currentQuestion.selectedOptionId != null;

      applyLocalQuestionUpdate(
        currentQuestion.questionId,
        { isFlagged: isSelected },
        { isFlagged: isSelected, isAnswered },
      );
      void persistAnswerMutation(
        currentQuestion,
        {
          selectedOptionId: currentQuestion.selectedOptionId ?? null,
          isFlagged: isSelected,
          responseText: qKind === 'writing' ? draftText : currentQuestion.responseText ?? null,
        },
        isAnswered,
      );
    },
    [currentQuestion, draftText, applyLocalQuestionUpdate, persistAnswerMutation],
  );

  const saveWritingDraft = useCallback(
    async (question: AttemptQuestionDto, text: string) => {
      const hasWords = countWords(text) > 0;
      applyLocalQuestionUpdate(
        question.questionId,
        { responseText: text },
        { isAnswered: hasWords },
      );
      setIsSaving(true);
      try {
        if (typeof navigator !== 'undefined' && !navigator.onLine) {
          throw new TypeError('Offline');
        }
        if (onSaveDraft) {
          const res = await onSaveDraft(question.questionId, text);
          if (res.savedAt) {
            applyLocalQuestionUpdate(question.questionId, { draftSavedAt: res.savedAt }, {});
          }
        } else {
          await onSaveAnswer(question.questionId, {
            isFlagged: question.isFlagged,
            responseText: text,
          });
        }
        const remaining = dequeueOfflineMutation(sittingKind, sittingId, question.questionId);
        setQueuedCount(remaining.length);
      } catch (err) {
        if (isNetworkFailure(err)) {
          const nextQueue = enqueueOfflineMutation(sittingKind, sittingId, {
            questionId: question.questionId,
            isFlagged: question.isFlagged,
            responseText: text,
          });
          setQueuedCount(nextQueue.length);
        } else {
          setActionError(err instanceof Error ? err.message : 'Unable to save draft.');
        }
      } finally {
        setIsSaving(false);
      }
    },
    [sittingKind, sittingId, onSaveDraft, onSaveAnswer, applyLocalQuestionUpdate],
  );

  // Debounced draft save for writing questions
  const draftTimerRef = useRef<number | null>(null);
  const handleDraftChange = useCallback(
    (nextValue: string) => {
      setDraftText(nextValue);
      if (!currentQuestion) return;
      const targetQuestion = currentQuestion;
      if (draftTimerRef.current != null) {
        window.clearTimeout(draftTimerRef.current);
      }
      draftTimerRef.current = window.setTimeout(() => {
        void saveWritingDraft(targetQuestion, nextValue);
      }, 1200);
    },
    [currentQuestion, saveWritingDraft],
  );

  useEffect(() => {
    return () => {
      if (draftTimerRef.current != null) {
        window.clearTimeout(draftTimerRef.current);
      }
    };
  }, []);

  const mergeTakeResponse = useCallback((take: UnifiedTakeView, targetOrder: number) => {
    setQuestionsMap((prev) => {
      const next = new Map(prev);
      for (const q of take.questions ?? []) {
        next.set(q.displayOrder, q);
      }
      if (take.currentQuestion) {
        next.set(take.currentQuestion.displayOrder, take.currentQuestion);
      }
      return next;
    });
    if (take.navigator) {
      setNavigatorItems(take.navigator);
    }
    if (take.phases) {
      setPhases(take.phases);
    }
    setRemainingSeconds(Math.max(0, take.remainingSeconds));
    const confirmedOrder = take.currentQuestion?.displayOrder ?? targetOrder;
    setCurrentOrder(confirmedOrder);
    if (confirmedOrder !== targetOrder) {
      setReconciliationNotice(
        `Navigated to question ${confirmedOrder} because question ${targetOrder} is locked.`,
      );
    } else {
      setReconciliationNotice(null);
    }
  }, []);

  const executeNavigation = useCallback(
    async (targetOrder: number, requireServerFetch: boolean) => {
      if (!currentQuestion) return;

      // Flush pending draft timer immediately when leaving a writing question
      if (draftTimerRef.current != null) {
        window.clearTimeout(draftTimerRef.current);
        draftTimerRef.current = null;
        if (classifyQuestionKind(currentQuestion) === 'writing') {
          await saveWritingDraft(currentQuestion, draftText);
        }
      }

      if (requireServerFetch) {
        const flushed = await flushOfflineQueue();
        if (!flushed) {
          setWaitingForBoundary(true);
          return;
        }

        setIsLoadingQuestion(true);
        setActionError(null);
        try {
          const updatedTake = await onFetchQuestion(targetOrder);
          mergeTakeResponse(updatedTake, targetOrder);
        } catch (err) {
          setActionError(
            err instanceof Error ? err.message : 'Unable to unlock next section right now.',
          );
        } finally {
          setIsLoadingQuestion(false);
        }
        return;
      }

      const reconciled = reconcileRequestedQuestionOrder(targetOrder, {
        navigator: navigatorItems,
        currentDisplayOrder: currentOrder,
        totalQuestions,
        oneWayForwardOnly: !canNavigateBackInSitting(currentQuestion, navigatorItems, phases),
      });

      if (reconciled.wasReconciled && reconciled.reason === 'locked') {
        setReconciliationNotice(
          `Question ${targetOrder} is in a locked section and cannot be opened directly.`,
        );
        return;
      }

      const cached = questionsMap.get(reconciled.resolvedOrder);
      if (cached) {
        setReconciliationNotice(null);
        setCurrentOrder(reconciled.resolvedOrder);
        setNavigatorItems((prev) =>
          prev.map((item) => ({
            ...item,
            isCurrent: item.displayOrder === reconciled.resolvedOrder,
          })),
        );
        return;
      }

      setIsLoadingQuestion(true);
      try {
        const updatedTake = await onFetchQuestion(reconciled.resolvedOrder);
        mergeTakeResponse(updatedTake, reconciled.resolvedOrder);
      } catch (err) {
        setActionError(err instanceof Error ? err.message : 'Unable to load question.');
      } finally {
        setIsLoadingQuestion(false);
      }
    },
    [
      currentQuestion,
      currentOrder,
      draftText,
      navigatorItems,
      phases,
      questionsMap,
      totalQuestions,
      saveWritingDraft,
      flushOfflineQueue,
      onFetchQuestion,
      mergeTakeResponse,
    ],
  );

  const requestNavigateTo = useCallback(
    (targetOrder: number) => {
      if (!currentQuestion) return;
      if (targetOrder === currentOrder) return;
      const targetQuestion = questionsMap.get(targetOrder);
      const crossesBoundary = needsLockBoundaryServerFetch(
        currentQuestion,
        targetQuestion,
        navigatorItems,
      );

      if (
        crossesBoundary &&
        targetOrder > currentOrder &&
        (currentQuestion.skillCode !== targetQuestion?.skillCode ||
          currentQuestion.navigationMode === 'HYBRID_GROUP_LOCKED')
      ) {
        setPendingBoundaryOrder(targetOrder);
        return;
      }

      void executeNavigation(targetOrder, crossesBoundary);
    },
    [currentQuestion, currentOrder, questionsMap, navigatorItems, executeNavigation],
  );

  const handleConfirmSubmit = useCallback(async () => {
    if (!isOnline || queuedCount > 0) {
      const ok = await flushOfflineQueue();
      if (!ok) {
        setWaitingForBoundary(true);
        return;
      }
    }
    setIsSubmitting(true);
    try {
      await onSubmit(false);
      setIsSubmitModalOpen(false);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Submission failed. Please retry.');
    } finally {
      setIsSubmitting(false);
    }
  }, [isOnline, queuedCount, flushOfflineQueue, onSubmit]);

  const canGoBack = canNavigateBackInSitting(currentQuestion, navigatorItems, phases);
  const isLastQuestion = currentOrder >= totalQuestions;
  const placard = formatSavePlacard(isOnline, queuedCount, isSaving, waitingForBoundary);
  const sectionSummaries = useMemo(
    () => buildSectionSubmitSummary(navigatorItems, allQuestions),
    [navigatorItems, allQuestions],
  );
  const totalUnanswered = Math.max(0, totalQuestions - answeredCount);

  if (!currentQuestion) {
    return (
      <div className="mx-auto max-w-3xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Sitting unavailable</Alert.Title>
            <Alert.Description>No active question could be loaded for this sitting.</Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  const questionKind = classifyQuestionKind(currentQuestion);
  const wordCount = countWords(draftText);
  const imageUrl = toBrowserMediaUrl(currentQuestion.imagePath);

  return (
    <div className="min-h-screen bg-background text-foreground flex flex-col">
      {/* Sticky sitting header */}
      <header className="sticky top-0 z-30 border-b border-border bg-surface shadow-surface">
        <div className="mx-auto max-w-7xl px-4 py-3 flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-3 min-w-0">
            <Chip size="sm" variant="soft" color="accent" className="font-display">
              {certCode}
            </Chip>
            <div className="min-w-0">
              <h1 className="font-display text-base font-semibold truncate">{examName}</h1>
              <p className="text-xs text-muted truncate">
                {currentQuestion.skillName} · {currentQuestion.sectionName}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3 flex-wrap">
            <Chip
              size="sm"
              variant="soft"
              color={
                placard.tone === 'saved'
                  ? 'success'
                  : placard.tone === 'saving'
                    ? 'accent'
                    : 'warning'
              }
            >
              {placard.tone === 'offline' && <WifiOff className="h-3 w-3 mr-1 inline" />}
              {placard.text}
            </Chip>

            <div
              className={`inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-sm font-mono-tabular font-semibold ${
                remainingSeconds <= 60
                  ? 'border-danger/40 bg-danger/10 text-danger'
                  : 'border-border bg-surface-secondary text-foreground'
              }`}
              aria-label="Time remaining"
            >
              <Clock className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{formatTimerSeconds(remainingSeconds)}</span>
            </div>

            {onSaveExit && (
              <Button size="sm" variant="outline" onPress={onSaveExit}>
                Pause & exit
              </Button>
            )}

            <Button size="sm" variant="primary" onPress={() => setIsSubmitModalOpen(true)}>
              <Send className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
              Review & submit
            </Button>
          </div>
        </div>

        {/* Stable, never-remounted ProgressBar + optional phase strip */}
        <div className="mx-auto max-w-7xl px-4 pb-2.5 space-y-2">
          <div className="flex items-center justify-between text-xs text-muted font-mono-tabular">
            <span>
              Progress: {answeredCount} / {totalQuestions} answered
            </span>
            <span>Question {currentOrder} of {totalQuestions}</span>
          </div>
          <ProgressBar
            aria-label="Sitting completion progress"
            value={answeredCount}
            minValue={0}
            maxValue={Math.max(1, totalQuestions)}
            size="sm"
            color="accent"
          >
            <ProgressBar.Track>
              <ProgressBar.Fill />
            </ProgressBar.Track>
          </ProgressBar>

          {phases.length > 1 && (
            <div className="flex flex-wrap items-center gap-1.5 pt-1">
              {phases
                .slice()
                .sort((a, b) => a.skillDisplayOrder - b.skillDisplayOrder)
                .map((phase) => {
                  const isCurrentPhase =
                    phase.skillCode.toLowerCase() === currentQuestion.skillCode.toLowerCase();
                  return (
                    <Chip
                      key={phase.skillId}
                      size="sm"
                      variant={isCurrentPhase ? 'primary' : 'soft'}
                      color={isCurrentPhase ? 'accent' : 'default'}
                    >
                      {phase.skillName}
                    </Chip>
                  );
                })}
            </div>
          )}
        </div>
      </header>

      {/* Main sitting workspace */}
      <main className="mx-auto w-full max-w-7xl flex-1 p-4 md:p-6 space-y-4">
        {(!isOnline || queuedCount > 0 || waitingForBoundary) && (
          <Alert status={waitingForBoundary ? 'danger' : 'warning'}>
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Title>
                {waitingForBoundary
                  ? 'Waiting for a connection before continuing'
                  : 'Connection interrupted'}
              </Alert.Title>
              <Alert.Description>
                {queuedCount > 0
                  ? `${queuedCount} response(s) are safely held on this device and will sync automatically when online.`
                  : 'You appear to be offline. Answers are stored locally until your connection returns.'}
              </Alert.Description>
            </Alert.Content>
            <Button size="sm" variant="outline" onPress={() => void flushOfflineQueue()}>
              <RefreshCw className="h-3.5 w-3.5 mr-1" aria-hidden="true" />
              Retry sync
            </Button>
          </Alert>
        )}

        {reconciliationNotice && (
          <Alert status="default">
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Description>{reconciliationNotice}</Alert.Description>
            </Alert.Content>
          </Alert>
        )}

        {actionError && (
          <Alert status="danger">
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Title>Save issue</Alert.Title>
              <Alert.Description>{actionError}</Alert.Description>
            </Alert.Content>
          </Alert>
        )}

        <div className="grid grid-cols-1 lg:grid-cols-[1fr_296px] gap-6 items-start">
          {/* Active Question Card */}
          <Card className="min-w-0">
            <Card.Header className="flex flex-wrap items-center justify-between gap-2 border-b border-border pb-4">
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <span className="font-mono-tabular text-xs font-semibold uppercase tracking-wide text-accent">
                    Question {currentQuestion.displayOrder} of {totalQuestions}
                  </span>
                  <Chip size="sm" variant="soft" color="default">
                    {currentQuestion.sectionCode}
                  </Chip>
                </div>
                <Card.Title className="text-lg font-display">
                  {currentQuestion.sectionName}
                </Card.Title>
              </div>

              <ToggleButton
                size="sm"
                variant="ghost"
                isSelected={currentQuestion.isFlagged}
                onChange={handleToggleFlag}
                aria-label="Flag question for review"
              >
                <Bookmark className="h-4 w-4 mr-1.5" aria-hidden="true" />
                {currentQuestion.isFlagged ? 'Flagged' : 'Flag'}
              </ToggleButton>
            </Card.Header>

            <Card.Content className="space-y-5 pt-5">
              {isLoadingQuestion ? (
                <div className="flex items-center justify-center py-12 gap-3 text-muted">
                  <Spinner size="md" color="accent" />
                  <span>Loading next question...</span>
                </div>
              ) : (
                <>
                  {currentQuestion.audioPath && (
                    <ExamAudioPlayer
                      sittingKind={sittingKind}
                      sittingId={sittingId}
                      audioPath={currentQuestion.audioPath}
                      audioStartMs={currentQuestion.audioStartMs}
                      audioEndMs={currentQuestion.audioEndMs}
                      questionId={currentQuestion.questionId}
                      allowReplay={Boolean(currentQuestion.allowReplay)}
                    />
                  )}

                  {currentQuestion.passage && (
                    <div className="rounded-lg border border-border bg-surface-secondary p-4 space-y-2 max-h-80 overflow-y-auto text-sm">
                      <p className="text-xs font-semibold uppercase tracking-wide text-muted">
                        Reading passage
                      </p>
                      {renderSafeParagraphs(currentQuestion.passage)}
                    </div>
                  )}

                  {imageUrl && (
                    <div className="rounded-lg border border-border bg-surface-secondary p-3">
                      <img
                        src={imageUrl}
                        alt={currentQuestion.imageAlt || `Visual prompt for question ${currentQuestion.displayOrder}`}
                        className="max-h-80 w-auto mx-auto rounded object-contain"
                      />
                    </div>
                  )}

                  <div className="space-y-2 text-base">{renderSafeParagraphs(currentQuestion.stem)}</div>

                  {/* Response Control by Question Type */}
                  {questionKind === 'mcq' && (
                    <RadioGroup
                      aria-label={`Choices for question ${currentQuestion.displayOrder}`}
                      value={
                        currentQuestion.selectedOptionId != null
                          ? String(currentQuestion.selectedOptionId)
                          : ''
                      }
                      onChange={handleMcqSelect}
                      className="space-y-2"
                    >
                      {currentQuestion.options.map((opt) => (
                        <Radio
                          key={opt.optionId}
                          value={String(opt.optionId)}
                          className="rounded-lg border border-border bg-surface-secondary/50 px-4 py-3 hover:border-accent/60 transition-colors"
                        >
                          <Radio.Control>
                            <Radio.Indicator />
                          </Radio.Control>
                          <Radio.Content className="flex-1">
                            <Label className="cursor-pointer font-normal text-foreground">
                              <span className="font-mono-tabular font-semibold mr-2">
                                {opt.label}.
                              </span>
                              {!currentQuestion.hideOptionText && opt.text}
                            </Label>
                          </Radio.Content>
                        </Radio>
                      ))}
                    </RadioGroup>
                  )}

                  {questionKind === 'mcq_multi' && (
                    <div className="space-y-2" role="group" aria-label="Multiple choice selections">
                      {currentQuestion.maxSelections && (
                        <p className="text-xs text-muted">
                          Select up to {currentQuestion.maxSelections} option(s).
                        </p>
                      )}
                      {currentQuestion.options.map((opt) => {
                        const selected = Boolean(
                          currentQuestion.selectedOptionIds?.includes(opt.optionId),
                        );
                        return (
                          <ToggleButton
                            key={opt.optionId}
                            isSelected={selected}
                            onChange={() => handleMultiSelectToggle(opt.optionId)}
                            variant="default"
                            className="w-full justify-start text-left h-auto py-3 px-4"
                          >
                            <span className="font-mono-tabular font-semibold mr-2">
                              {opt.label}.
                            </span>
                            {!currentQuestion.hideOptionText && opt.text}
                          </ToggleButton>
                        );
                      })}
                    </div>
                  )}

                  {questionKind === 'writing' && (
                    <div className="space-y-3">
                      <TextField aria-label="Written response">
                        <Label>Your written response</Label>
                        <TextArea
                          rows={10}
                          fullWidth
                          value={draftText}
                          onChange={(e) => handleDraftChange(e.target.value)}
                          placeholder="Write your response here..."
                        />
                        <Description>
                          Drafts autosave shortly after you stop typing.
                        </Description>
                      </TextField>

                      <div className="flex flex-wrap items-center justify-between gap-3 text-xs text-muted">
                        <div className="flex items-center gap-3 font-mono-tabular">
                          <span>{wordCount} words</span>
                          <span>{draftText.length} characters</span>
                          {currentQuestion.minWords != null && (
                            <span
                              className={
                                wordCount < currentQuestion.minWords ? 'text-warning' : 'text-success'
                              }
                            >
                              Target minimum: {currentQuestion.minWords} words
                            </span>
                          )}
                          {currentQuestion.maxWords != null && (
                            <span>Max: {currentQuestion.maxWords} words</span>
                          )}
                        </div>

                        <Button
                          size="sm"
                          variant="secondary"
                          onPress={() => void saveWritingDraft(currentQuestion, draftText)}
                        >
                          <Save className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
                          Save draft
                        </Button>
                      </div>
                    </div>
                  )}

                  {questionKind === 'speaking' &&
                    (renderSpeakingPane ? (
                      renderSpeakingPane({
                        question: currentQuestion,
                        attemptId,
                        sittingKind,
                        sittingId,
                        isOnline,
                        onAudioSaved: (audioPath, durationMs) => {
                          applyLocalQuestionUpdate(
                            currentQuestion.questionId,
                            {
                              responseAudioPath: audioPath,
                              responseDurationMs: durationMs ?? null,
                            },
                            { isAnswered: true },
                          );
                        },
                        onNextQuestion: () => {
                          if (isLastQuestion) {
                            setIsSubmitModalOpen(true);
                          } else {
                            requestNavigateTo(currentOrder + 1);
                          }
                        },
                        onUploadAudio,
                        onRecordInterruption,
                      })
                    ) : (
                      <SpeakingResponsePane
                        question={currentQuestion}
                        attemptId={attemptId}
                        onAudioSaved={(audioPath, durationMs) => {
                          applyLocalQuestionUpdate(
                            currentQuestion.questionId,
                            {
                              responseAudioPath: audioPath,
                              responseDurationMs: durationMs ?? null,
                            },
                            { isAnswered: true },
                          );
                        }}
                        onNextQuestion={() => {
                          if (isLastQuestion) {
                            setIsSubmitModalOpen(true);
                          } else {
                            requestNavigateTo(currentOrder + 1);
                          }
                        }}
                        onUploadAudio={onUploadAudio}
                        onRecordInterruption={onRecordInterruption}
                      />
                    ))}
                </>
              )}
            </Card.Content>

            <Card.Footer className="flex items-center justify-between gap-3 border-t border-border pt-4">
              <div>
                {canGoBack && (
                  <Button
                    variant="outline"
                    onPress={() => requestNavigateTo(currentOrder - 1)}
                    isDisabled={isLoadingQuestion}
                  >
                    <ArrowLeft className="h-4 w-4 mr-1.5" aria-hidden="true" />
                    Previous
                  </Button>
                )}
              </div>

              <div className="flex items-center gap-2">
                {isLastQuestion ? (
                  <Button variant="primary" onPress={() => setIsSubmitModalOpen(true)}>
                    Review & submit
                  </Button>
                ) : (
                  <Button
                    variant="primary"
                    onPress={() => requestNavigateTo(currentOrder + 1)}
                    isDisabled={isLoadingQuestion}
                  >
                    Next
                    <ArrowRight className="h-4 w-4 ml-1.5" aria-hidden="true" />
                  </Button>
                )}
              </div>
            </Card.Footer>
          </Card>

          {/* Right Question Navigator Panel */}
          <Card variant="secondary">
            <Card.Header className="pb-2">
              <Card.Title className="text-sm font-display">Question navigator</Card.Title>
              <Card.Description className="font-mono-tabular text-xs">
                {answeredCount} answered · {flaggedCount} flagged
              </Card.Description>
            </Card.Header>
            <Card.Content className="space-y-4">
              <div className="grid grid-cols-5 gap-1.5">
                {navigatorItems.map((item) => {
                  const isCurrent = item.displayOrder === currentOrder;
                  const stateClass = item.isLocked
                    ? 'border-border bg-surface text-muted opacity-50 cursor-not-allowed'
                    : isCurrent
                      ? 'border-2 border-accent bg-accent/15 text-accent font-semibold'
                      : item.isAnswered
                        ? 'border-success/40 bg-success/10 text-success'
                        : 'border-border bg-surface text-foreground hover:border-accent/50';

                  return (
                    <button
                      key={item.questionId}
                      type="button"
                      disabled={item.isLocked}
                      onClick={() => requestNavigateTo(item.displayOrder)}
                      aria-label={`Question ${item.displayOrder}${item.isAnswered ? ', answered' : ''}${item.isFlagged ? ', flagged' : ''}${item.isLocked ? ', locked' : ''}`}
                      className={`relative h-9 rounded-md border text-xs font-mono-tabular transition-colors flex items-center justify-center ${stateClass}`}
                    >
                      {item.isLocked ? (
                        <Lock className="h-3 w-3" aria-hidden="true" />
                      ) : (
                        item.displayOrder
                      )}
                      {item.isFlagged && !item.isLocked && (
                        <span
                          aria-hidden="true"
                          className="absolute top-0.5 right-0.5 h-1.5 w-1.5 rounded-full bg-warning"
                        />
                      )}
                    </button>
                  );
                })}
              </div>

              <div className="flex flex-wrap gap-3 text-xs text-muted pt-1 border-t border-separator">
                <span className="inline-flex items-center gap-1">
                  <span className="h-2.5 w-2.5 rounded-sm bg-success/20 border border-success/50" />
                  Answered
                </span>
                <span className="inline-flex items-center gap-1">
                  <span className="h-2.5 w-2.5 rounded-full bg-warning" />
                  Flagged
                </span>
                <span className="inline-flex items-center gap-1">
                  <Lock className="h-3 w-3" aria-hidden="true" />
                  Locked
                </span>
              </div>
            </Card.Content>
          </Card>
        </div>
      </main>

      {/* PLC-18 Submit Confirmation Modal */}
      {isSubmitModalOpen && (
        <Modal.Backdrop
          isOpen={isSubmitModalOpen}
          onOpenChange={setIsSubmitModalOpen}
          variant="blur"
        >
          <Modal.Container size="lg">
            <Modal.Dialog>
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading className="font-display">Review & submit sitting</Modal.Heading>
              </Modal.Header>

              <Modal.Body className="space-y-4">
                {totalUnanswered > 0 ? (
                  <Alert status="warning">
                    <Alert.Indicator />
                    <Alert.Content>
                      <Alert.Title>
                        {totalUnanswered} unanswered question(s) remaining
                      </Alert.Title>
                      <Alert.Description>
                        Review the breakdown below before submitting. Once submitted, answers cannot
                        be edited.
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>
                ) : (
                  <Alert status="success">
                    <Alert.Indicator />
                    <Alert.Content>
                      <Alert.Title>All questions answered</Alert.Title>
                      <Alert.Description>
                        Ready to submit your sitting for scoring and grading.
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>
                )}

                <div className="divide-y divide-separator rounded-lg border border-border bg-surface-secondary/40">
                  {sectionSummaries.map((sec) => (
                    <div
                      key={sec.sectionId}
                      className="flex items-center justify-between gap-3 px-4 py-2.5 text-sm"
                    >
                      <div className="min-w-0">
                        <p className="font-medium text-foreground truncate">{sec.sectionName}</p>
                        <p className="text-xs text-muted font-mono-tabular">{sec.sectionCode}</p>
                      </div>
                      <div className="flex items-center gap-2 shrink-0 font-mono-tabular">
                        <span className="text-foreground">
                          {sec.answered} / {sec.total} answered
                        </span>
                        {sec.unanswered > 0 ? (
                          <Chip size="sm" color="warning" variant="soft">
                            {sec.unanswered} unanswered
                          </Chip>
                        ) : (
                          <Chip size="sm" color="success" variant="soft">
                            <CheckCircle2 className="h-3 w-3 mr-1 inline" aria-hidden="true" />
                            Complete
                          </Chip>
                        )}
                      </div>
                    </div>
                  ))}
                </div>

                {(queuedCount > 0 || !isOnline) && (
                  <Alert status="danger">
                    <Alert.Indicator />
                    <Alert.Content>
                      <Alert.Title>Pending offline responses ({queuedCount})</Alert.Title>
                      <Alert.Description>
                        Your device still has unsynced responses. Sync before finalizing
                        submission.
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>
                )}
              </Modal.Body>

              <Modal.Footer>
                <Button variant="outline" onPress={() => setIsSubmitModalOpen(false)}>
                  Keep working
                </Button>
                <Button
                  variant="primary"
                  isPending={isSubmitting}
                  onPress={() => void handleConfirmSubmit()}
                >
                  Submit answers now
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}

      {/* Locked Part / Section Advance Confirmation AlertDialog */}
      {pendingBoundaryOrder != null && (
        <AlertDialog.Backdrop
          isOpen={pendingBoundaryOrder != null}
          onOpenChange={(open) => {
            if (!open) setPendingBoundaryOrder(null);
          }}
        >
          <AlertDialog.Container>
            <AlertDialog.Dialog>
              <AlertDialog.Header>
                <AlertDialog.Icon status="warning">
                  <AlertTriangle className="h-5 w-5" aria-hidden="true" />
                </AlertDialog.Icon>
                <AlertDialog.Heading>Advance to next locked section?</AlertDialog.Heading>
              </AlertDialog.Header>
              <AlertDialog.Body>
                <p className="text-sm text-muted">
                  Moving into the next section locks the current section. You will not be able to
                  return to edit earlier questions.
                </p>
              </AlertDialog.Body>
              <AlertDialog.Footer>
                <Button variant="outline" onPress={() => setPendingBoundaryOrder(null)}>
                  Stay in current section
                </Button>
                <Button
                  variant="primary"
                  onPress={() => {
                    const target = pendingBoundaryOrder;
                    setPendingBoundaryOrder(null);
                    if (target != null) {
                      void executeNavigation(target, true);
                    }
                  }}
                >
                  Continue to next section
                </Button>
              </AlertDialog.Footer>
            </AlertDialog.Dialog>
          </AlertDialog.Container>
        </AlertDialog.Backdrop>
      )}
    </div>
  );
}
