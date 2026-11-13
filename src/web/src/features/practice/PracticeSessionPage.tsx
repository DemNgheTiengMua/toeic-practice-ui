import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useParams } from '@tanstack/react-router';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Checkbox,
  CheckboxGroup,
  Chip,
  Description,
  Input,
  Label,
  ListBox,
  Popover,
  Radio,
  RadioGroup,
  Select,
  Spinner,
  TextArea,
  TextField,
  ToggleButton,
} from '@heroui/react';
import {
  ArrowLeft,
  ArrowRight,
  Bookmark,
  Check,
  ChevronLeft,
  ChevronRight,
  HelpCircle,
  Lightbulb,
  SkipForward,
  StickyNote,
} from 'lucide-react';
import {
  api,
  GradedAnswerDto,
  PracticeNavigatorItemDto,
  PracticeQuestionDto,
  PracticeSessionTakeDto,
} from '@/lib/api';
import { countWords, toBrowserMediaUrl } from '@/lib/security';
import { PracticeAudioPane } from './PracticeAudioPane';
import {
  buildSaveTogglePlan,
  getPracticeErrorMessage,
  getPracticeQuestionAddress,
  parseAcceptedAnswers,
  parseSelectedOptionIds,
  readSavedNote,
  writeSavedNote,
} from './practiceUtils';

type NavFilter = 'all' | 'incorrect' | 'saved';

function isQuestionLocked(q: PracticeQuestionDto, nav?: PracticeNavigatorItemDto): boolean {
  if (nav?.isAnswered) return true;
  return (
    q.isCorrect !== null &&
    q.isCorrect !== undefined
  ) || q.selectedOptionId != null || Boolean(q.responseText) || parseSelectedOptionIds(q).length > 0;
}

export function PracticeSessionPage() {
  const { sessionId } = useParams({ strict: false }) as { sessionId?: string };
  const location = useLocation();
  const numericSessionId = Number(sessionId);

  const initialQ = useMemo(() => {
    const searchObj = location.search as Record<string, unknown> | undefined;
    const raw = searchObj?.q ?? new URLSearchParams(location.searchStr || '').get('q');
    const parsed = raw != null ? Number(raw) : NaN;
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }, [location.search, location.searchStr]);

  const [takeState, setTakeState] = useState<PracticeSessionTakeDto | null>(null);
  const [activeOrder, setActiveOrder] = useState<number>(initialQ ?? 1);
  const [navFilter, setNavFilter] = useState<NavFilter>('all');

  // Draft answer state per question address
  const [draftOptionId, setDraftOptionId] = useState<number | null>(null);
  const [draftOptionIds, setDraftOptionIds] = useState<number[]>([]);
  const [draftText, setDraftText] = useState<string>('');
  const [actionError, setActionError] = useState<string | null>(null);
  const [sessionFinished, setSessionFinished] = useState<boolean>(false);

  // Popover & personal note state
  const [revealedHintText, setRevealedHintText] = useState<string | null>(null);
  const [noteOpen, setNoteOpen] = useState<boolean>(false);
  const [noteDraft, setNoteDraft] = useState<string>('');
  const [savedNoteTick, setSavedNoteTick] = useState<number>(0);

  const takeQuery = useQuery({
    queryKey: ['practice', 'take', numericSessionId],
    queryFn: () => api.practice.sessionTake(numericSessionId, initialQ),
    enabled: Number.isInteger(numericSessionId) && numericSessionId > 0,
  });

  useEffect(() => {
    if (!takeQuery.data) return;
    setTakeState(takeQuery.data);
    const resolvedOrder =
      initialQ != null && takeQuery.data.questions.some((q) => q.displayOrder === initialQ)
        ? initialQ
        : takeQuery.data.question.displayOrder;
    setActiveOrder(resolvedOrder);
    setSessionFinished(takeQuery.data.session.status === 'finished');
  }, [takeQuery.data, initialQ]);

  const activeQuestion = useMemo(() => {
    if (!takeState) return null;
    return (
      takeState.questions.find((q) => q.displayOrder === activeOrder) ??
      takeState.question
    );
  }, [takeState, activeOrder]);

  const activeNav = useMemo(() => {
    if (!takeState || !activeQuestion) return undefined;
    return takeState.navigator.find((n) => n.displayOrder === activeQuestion.displayOrder);
  }, [takeState, activeQuestion]);

  const locked = activeQuestion ? isQuestionLocked(activeQuestion, activeNav) : false;

  // Reset per-question draft inputs when switching question
  useEffect(() => {
    if (!activeQuestion) return;
    setActionError(null);
    setDraftOptionId(activeQuestion.selectedOptionId ?? null);
    setDraftOptionIds(parseSelectedOptionIds(activeQuestion));
    setDraftText(activeQuestion.responseText ?? '');
    setRevealedHintText(activeQuestion.hintUsed ? activeQuestion.hint ?? null : null);
    setNoteDraft(
      readSavedNote({
        savedQuestionId: activeQuestion.savedQuestionId,
        questionId: activeQuestion.questionId,
        itemVersionId: activeQuestion.itemVersionId,
      }),
    );
  }, [activeQuestion?.displayOrder, activeQuestion?.savedQuestionId, savedNoteTick]);

  const selectQuestion = useCallback(
    (order: number) => {
      setActiveOrder(order);
      if (typeof window !== 'undefined' && window.history?.replaceState) {
        const nextUrl = `${window.location.pathname}?q=${order}`;
        window.history.replaceState(null, '', nextUrl);
      }
    },
    [],
  );

  const applyGradedResult = useCallback(
    (order: number, reply: GradedAnswerDto, submittedBody: { selectedOptionId?: number | null; responseText?: string | null; selectedOptionIds?: number[] | null }) => {
      setTakeState((prev) => {
        if (!prev) return prev;
        const updatedQuestions = prev.questions.map((q) => {
          if (q.displayOrder !== order) return q;
          return {
            ...q,
            isCorrect: reply.isCorrect ?? null,
            selectedOptionId: reply.selectedOptionId ?? submittedBody.selectedOptionId ?? null,
            correctOptionId: reply.correctOptionId ?? q.correctOptionId,
            correctOptionIds: reply.correctOptionIds ?? q.correctOptionIds,
            acceptedAnswersJson: reply.acceptedAnswersJson ?? q.acceptedAnswersJson,
            explanation: reply.explanation ?? q.explanation,
            transcript: reply.transcript ?? q.transcript,
            responseText: reply.responseText ?? submittedBody.responseText ?? q.responseText,
            selectedOptionIdsJson: reply.selectedOptionIdsJson ?? q.selectedOptionIdsJson,
            selectedOptionIds: submittedBody.selectedOptionIds ?? q.selectedOptionIds,
            isSaved: reply.isSaved || q.isSaved,
            savedQuestionId: reply.savedQuestionId ?? q.savedQuestionId,
            cefrLevelCode: reply.cefrLevelCode ?? q.cefrLevelCode,
          };
        });
        const updatedNavigator = prev.navigator.map((n) => {
          if (n.displayOrder !== order) return n;
          return {
            ...n,
            isAnswered: true,
            isCorrect: reply.isCorrect ?? null,
            isSaved: reply.isSaved || n.isSaved,
            savedQuestionId: reply.savedQuestionId ?? n.savedQuestionId,
          };
        });
        return {
          ...prev,
          session: {
            ...prev.session,
            status: reply.isSessionFinished ? 'finished' : prev.session.status,
            correctCount:
              reply.isCorrect === true ? prev.session.correctCount + 1 : prev.session.correctCount,
          },
          questions: updatedQuestions,
          navigator: updatedNavigator,
        };
      });
      if (reply.isSessionFinished) {
        setSessionFinished(true);
      }
    },
    [],
  );

  const submitAnswerMutation = useMutation({
    mutationFn: async (isSkip: boolean) => {
      if (!activeQuestion) throw new Error('No active question.');
      const address = getPracticeQuestionAddress(activeQuestion);
      const qType = (activeQuestion.questionType || 'mcq').toLowerCase();

      const body = isSkip
        ? {
            selectedOptionId: null,
            responseText: null,
            selectedOptionIdsJson: null,
            selectedOptionIds: null,
          }
        : qType === 'multi_select'
          ? {
              selectedOptionId: null,
              selectedOptionIds: draftOptionIds,
              selectedOptionIdsJson: JSON.stringify(draftOptionIds),
            }
          : qType === 'inline_completion' || qType === 'free_response'
            ? {
                selectedOptionId: null,
                responseText: draftText.trim(),
              }
            : {
                selectedOptionId: draftOptionId,
                responseText: null,
              };

      const reply = await api.practice.submitAnswer(numericSessionId, address, body);
      return { reply, body, order: activeQuestion.displayOrder };
    },
    onSuccess: ({ reply, body, order }) => {
      setActionError(null);
      applyGradedResult(order, reply, body);
    },
    onError: (err) => {
      setActionError(getPracticeErrorMessage(err, 'Unable to record answer.'));
    },
  });

  const hintMutation = useMutation({
    mutationFn: async () => {
      if (!activeQuestion) throw new Error('No active question.');
      const address = getPracticeQuestionAddress(activeQuestion);
      return api.practice.revealHint(numericSessionId, address);
    },
    onSuccess: (res) => {
      setActionError(null);
      const hintText = res.hint ?? activeQuestion?.hint ?? null;
      setRevealedHintText(hintText);
      setTakeState((prev) => {
        if (!prev || !activeQuestion) return prev;
        return {
          ...prev,
          questions: prev.questions.map((q) =>
            q.displayOrder === activeQuestion.displayOrder
              ? { ...q, hintUsed: true, hint: hintText ?? q.hint }
              : q,
          ),
        };
      });
    },
    onError: (err) => {
      setActionError(getPracticeErrorMessage(err, 'Unable to load hint.'));
    },
  });

  const bookmarkMutation = useMutation({
    mutationFn: async () => {
      if (!activeQuestion) throw new Error('No active question.');
      const plan = buildSaveTogglePlan(activeQuestion);
      if (!plan) throw new Error('Question cannot be bookmarked.');
      if (plan.action === 'unsave') {
        return api.practice.unsaveQuestion(plan.savedQuestionId);
      }
      if (plan.action === 'save_item_version') {
        return api.practice.saveItemVersion(plan.itemVersionId, 'manual');
      }
      return api.practice.saveQuestion(plan.questionId, 'manual');
    },
    onSuccess: (res) => {
      setActionError(null);
      setTakeState((prev) => {
        if (!prev || !activeQuestion) return prev;
        return {
          ...prev,
          questions: prev.questions.map((q) =>
            q.displayOrder === activeQuestion.displayOrder
              ? {
                  ...q,
                  isSaved: res.isSaved,
                  savedQuestionId: res.savedQuestionId ?? (res.isSaved ? q.savedQuestionId : null),
                }
              : q,
          ),
          navigator: prev.navigator.map((n) =>
            n.displayOrder === activeQuestion.displayOrder
              ? {
                  ...n,
                  isSaved: res.isSaved,
                  savedQuestionId: res.savedQuestionId ?? (res.isSaved ? n.savedQuestionId : null),
                }
              : n,
          ),
        };
      });
    },
    onError: (err) => {
      setActionError(getPracticeErrorMessage(err, 'Unable to update bookmark.'));
    },
  });

  const canCheckAnswer = useMemo(() => {
    if (!activeQuestion || locked || submitAnswerMutation.isPending) return false;
    const qType = (activeQuestion.questionType || 'mcq').toLowerCase();
    if (qType === 'multi_select') return draftOptionIds.length > 0;
    if (qType === 'inline_completion' || qType === 'free_response') {
      return draftText.trim().length > 0;
    }
    return draftOptionId != null;
  }, [activeQuestion, locked, submitAnswerMutation.isPending, draftOptionIds, draftText, draftOptionId]);

  // Keyboard shortcuts (`1-4`, `S`, `H`, `B`, `N`, `P`) outside form inputs
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const tag = target?.tagName?.toLowerCase();
      if (tag === 'input' || tag === 'textarea' || tag === 'select' || target?.isContentEditable) {
        return;
      }
      if (!activeQuestion || !takeState) return;

      const key = e.key.toLowerCase();
      if (['1', '2', '3', '4'].includes(key) && !locked) {
        const idx = Number(key) - 1;
        const opt = activeQuestion.options[idx];
        const qType = (activeQuestion.questionType || 'mcq').toLowerCase();
        if (opt && (qType === 'mcq' || qType === 'mcq_3' || qType === 'drag_drop')) {
          setDraftOptionId(opt.optionId);
        }
      } else if (key === 's' && canCheckAnswer) {
        e.preventDefault();
        submitAnswerMutation.mutate(false);
      } else if (key === 'h' && !locked && !activeQuestion.hintUsed && activeQuestion.hint) {
        e.preventDefault();
        hintMutation.mutate();
      } else if (key === 'b') {
        e.preventDefault();
        bookmarkMutation.mutate();
      } else if (key === 'n' && activeOrder < takeState.questions.length) {
        e.preventDefault();
        selectQuestion(activeOrder + 1);
      } else if (key === 'p' && activeOrder > 1) {
        e.preventDefault();
        selectQuestion(activeOrder - 1);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [
    activeQuestion,
    takeState,
    locked,
    canCheckAnswer,
    activeOrder,
    submitAnswerMutation,
    hintMutation,
    bookmarkMutation,
    selectQuestion,
  ]);

  const filteredNavigator = useMemo(() => {
    if (!takeState) return [];
    if (navFilter === 'incorrect') return takeState.navigator.filter((n) => n.isCorrect === false);
    if (navFilter === 'saved') return takeState.navigator.filter((n) => n.isSaved);
    return takeState.navigator;
  }, [takeState, navFilter]);

  if (takeQuery.isLoading || (!takeState && !takeQuery.isError)) {
    return (
      <Card className="p-8">
        <div className="flex items-center justify-center gap-3 text-sm text-muted">
          <Spinner size="sm" />
          <span>Loading practice session…</span>
        </div>
      </Card>
    );
  }

  if (takeQuery.isError || !takeState || !activeQuestion) {
    return (
      <Alert status="danger">
        <Alert.Indicator />
        <Alert.Content>
          <Alert.Title>Unable to load practice session</Alert.Title>
          <Alert.Description>
            {getPracticeErrorMessage(takeQuery.error, 'Practice session not found.')}
          </Alert.Description>
          <div className="mt-3">
            <Link to="/practice" className="no-underline">
              <Button size="sm" variant="secondary">
                Back to practice drills
              </Button>
            </Link>
          </div>
        </Alert.Content>
      </Alert>
    );
  }

  const { session } = takeState;
  const isShortDrill =
    Boolean(session.isShort) ||
    (session.sectionId == null && session.topicId != null && session.questionCount < 10);
  const imageUrl = toBrowserMediaUrl(activeQuestion.imagePath);
  const hasMediaPane = Boolean(
    activeQuestion.passage ||
      imageUrl ||
      activeQuestion.audioPath ||
      activeQuestion.transcript,
  );
  const qType = (activeQuestion.questionType || 'mcq').toLowerCase();
  const acceptedAnswers = parseAcceptedAnswers(activeQuestion.acceptedAnswersJson);
  const answeredCount = takeState.navigator.filter((n) => n.isAnswered).length;
  const wordCount = countWords(draftText);

  const handleSaveNote = () => {
    writeSavedNote(
      {
        savedQuestionId: activeQuestion.savedQuestionId,
        questionId: activeQuestion.questionId,
        itemVersionId: activeQuestion.itemVersionId,
      },
      noteDraft,
    );
    setSavedNoteTick((t) => t + 1);
    setNoteOpen(false);
  };

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-separator pb-4">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <Link to="/practice" className="no-underline">
              <Button size="sm" variant="ghost">
                <ArrowLeft className="h-4 w-4" aria-hidden="true" />
                <span>Practice drills</span>
              </Button>
            </Link>
            <h1 className="font-display text-xl font-bold text-foreground">
              {session.sectionName}
            </h1>
            <Chip size="sm" color="success" variant="soft">
              <Chip.Label className="font-mono-tabular">0 credits</Chip.Label>
            </Chip>
          </div>
          <p className="text-xs text-muted">
            <span className="font-mono-tabular">
              Question {activeQuestion.displayOrder} of {activeQuestion.totalQuestions}
            </span>{' '}
            ·{' '}
            <span className="font-mono-tabular">
              {answeredCount}/{session.questionCount} answered
            </span>
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Link
            to="/practice/result/$sessionId"
            params={{ sessionId: String(session.sessionId) }}
            className="no-underline"
          >
            <Button size="sm" variant={sessionFinished ? 'primary' : 'secondary'}>
              <span>{sessionFinished ? 'View results' : 'Summary'}</span>
              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          </Link>
        </div>
      </header>

      {isShortDrill && (
        <Alert status="accent">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Shorter drill ({session.questionCount} items)</Alert.Title>
            <Alert.Description>
              This pool cell currently holds {session.questionCount} open items rather than a full 10-item set.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {sessionFinished && (
        <Alert status="success">
          <Alert.Indicator />
          <Alert.Content className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <Alert.Title>Session complete</Alert.Title>
              <Alert.Description>
                All questions in this drill are finished. Review your answers below or open your full summary.
              </Alert.Description>
            </div>
            <Link
              to="/practice/result/$sessionId"
              params={{ sessionId: String(session.sessionId) }}
              className="no-underline"
            >
              <Button size="sm" variant="primary">
                <span>View results</span>
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </Link>
          </Alert.Content>
        </Alert>
      )}

      {actionError && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Action failed</Alert.Title>
            <Alert.Description>{actionError}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-[1fr_280px]">
        <div className={`grid gap-5 ${hasMediaPane ? 'md:grid-cols-2' : ''}`}>
          {hasMediaPane && (
            <Card className="border border-border bg-surface">
              <Card.Header>
                <Card.Title className="text-base">Context & media</Card.Title>
              </Card.Header>
              <Card.Content className="space-y-4">
                {imageUrl && (
                  <img
                    src={imageUrl}
                    alt={activeQuestion.imageAlt || activeQuestion.stem}
                    className="max-h-72 w-full rounded-md border border-border object-contain bg-surface-secondary"
                  />
                )}
                <PracticeAudioPane
                  audioPath={activeQuestion.audioPath}
                  audioStartMs={activeQuestion.audioStartMs}
                  audioEndMs={activeQuestion.audioEndMs}
                  transcript={activeQuestion.transcript}
                />
                {activeQuestion.passage && (
                  <div className="whitespace-pre-line rounded-md border border-border bg-surface-secondary p-4 text-sm leading-relaxed text-foreground">
                    {activeQuestion.passage}
                  </div>
                )}
              </Card.Content>
            </Card>
          )}

          <Card className="border border-border bg-surface">
            <Card.Header className="flex flex-row items-start justify-between gap-3">
              <div className="space-y-1.5">
                <div className="flex flex-wrap items-center gap-2">
                  <Chip size="sm" variant="soft">
                    <Chip.Label className="font-mono-tabular">
                      Q{activeQuestion.displayOrder}
                    </Chip.Label>
                  </Chip>
                  {activeQuestion.categoryName && (
                    <Chip size="sm" variant="soft" color="accent">
                      <Chip.Label>{activeQuestion.categoryName}</Chip.Label>
                    </Chip>
                  )}
                  {locked && activeQuestion.cefrLevelCode && (
                    <Chip size="sm" variant="soft" color="warning">
                      <Chip.Label className="font-mono-tabular">
                        Item level · {activeQuestion.cefrLevelCode}
                      </Chip.Label>
                    </Chip>
                  )}
                </div>
                <Card.Title className="text-lg leading-snug">{activeQuestion.stem}</Card.Title>
              </div>

              <div className="flex shrink-0 items-center gap-1.5">
                <ToggleButton
                  size="sm"
                  aria-label={activeQuestion.isSaved ? 'Saved question' : 'Save question'}
                  isSelected={activeQuestion.isSaved}
                  isDisabled={bookmarkMutation.isPending}
                  onChange={() => bookmarkMutation.mutate()}
                >
                  {({ isSelected }) => (
                    <>
                      <Bookmark
                        className={`h-3.5 w-3.5 ${isSelected ? 'fill-current' : ''}`}
                        aria-hidden="true"
                      />
                      <span>{isSelected ? 'Saved' : 'Save question'}</span>
                    </>
                  )}
                </ToggleButton>

                <Popover isOpen={noteOpen} onOpenChange={setNoteOpen}>
                  <Button size="sm" variant="ghost" aria-label="Personal note">
                    <StickyNote className="h-3.5 w-3.5" aria-hidden="true" />
                    <span className="sr-only sm:not-sr-only">Personal note</span>
                  </Button>
                  <Popover.Content className="w-80">
                    <Popover.Dialog className="space-y-3">
                      <Popover.Heading>Personal study note</Popover.Heading>
                      <TextField
                        aria-label="Personal reminder note"
                        value={noteDraft}
                        onChange={setNoteDraft}
                      >
                        <Label className="sr-only">Note</Label>
                        <TextArea
                          rows={3}
                          placeholder="Add a personal reminder for review..."
                        />
                        <Description>Saved locally in this browser for your review bank.</Description>
                      </TextField>
                      <div className="flex justify-end gap-2">
                        <Button size="sm" variant="primary" onPress={handleSaveNote}>
                          <Check className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Save note</span>
                        </Button>
                      </div>
                    </Popover.Dialog>
                  </Popover.Content>
                </Popover>
              </div>
            </Card.Header>

            <Card.Content className="space-y-4">
              {qType === 'multi_select' ? (
                <CheckboxGroup
                  aria-label="Select multiple options"
                  isDisabled={locked}
                  value={draftOptionIds.map(String)}
                  onChange={(vals) => {
                    const max = activeQuestion.maxSelections;
                    const parsed = vals.map(Number);
                    if (max != null && parsed.length > max) return;
                    setDraftOptionIds(parsed);
                  }}
                >
                  <Label className="text-xs text-muted">
                    Select {activeQuestion.maxSelections ? `up to ${activeQuestion.maxSelections}` : 'all matching'} options
                  </Label>
                  {activeQuestion.options.map((opt) => (
                    <Checkbox key={opt.optionId} value={String(opt.optionId)}>
                      <Checkbox.Content>
                        <Checkbox.Control>
                          <Checkbox.Indicator />
                        </Checkbox.Control>
                        <span>
                          <strong className="font-mono-tabular mr-1.5">{opt.label}.</strong>
                          {opt.text}
                        </span>
                      </Checkbox.Content>
                    </Checkbox>
                  ))}
                </CheckboxGroup>
              ) : qType === 'dropdown' ? (
                <Select
                  aria-label="Choose an answer"
                  placeholder="Select best match"
                  isDisabled={locked}
                  value={draftOptionId != null ? String(draftOptionId) : undefined}
                  onChange={(key) => setDraftOptionId(key != null ? Number(key) : null)}
                >
                  <Label>Select best match</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      {activeQuestion.options.map((opt) => (
                        <ListBox.Item
                          key={opt.optionId}
                          id={String(opt.optionId)}
                          textValue={`${opt.label}. ${opt.text}`}
                        >
                          {opt.label}. {opt.text}
                          <ListBox.ItemIndicator />
                        </ListBox.Item>
                      ))}
                    </ListBox>
                  </Select.Popover>
                </Select>
              ) : qType === 'inline_completion' || qType === 'free_response' ? (
                <TextField
                  aria-label="Your answer"
                  isDisabled={locked}
                  value={draftText}
                  onChange={setDraftText}
                >
                  <Label>Your answer</Label>
                  {qType === 'free_response' ? (
                    <TextArea rows={4} placeholder="Type your response..." />
                  ) : (
                    <Input placeholder="Type missing word or phrase..." />
                  )}
                  <Description className="font-mono-tabular">
                    {wordCount} {wordCount === 1 ? 'word' : 'words'}
                    {activeQuestion.maxWords != null ? ` · max ${activeQuestion.maxWords}` : ''}
                    {activeQuestion.minWords != null ? ` · min ${activeQuestion.minWords}` : ''}
                    {!activeQuestion.allowNumber ? ' · letters only' : ''}
                  </Description>
                </TextField>
              ) : qType === 'drag_drop' ? (
                <div className="space-y-3">
                  <div className="text-xs font-medium text-muted">
                    Choose a tile to place into the blank:
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {activeQuestion.options.map((opt) => {
                      const active = draftOptionId === opt.optionId;
                      return (
                        <Button
                          key={opt.optionId}
                          size="sm"
                          variant={active ? 'primary' : 'secondary'}
                          isDisabled={locked}
                          onPress={() => setDraftOptionId(opt.optionId)}
                        >
                          <span className="font-mono-tabular mr-1">{opt.label}.</span>
                          <span>{opt.text}</span>
                        </Button>
                      );
                    })}
                  </div>
                </div>
              ) : (
                <RadioGroup
                  aria-label="Answer choices"
                  isDisabled={locked}
                  value={draftOptionId != null ? String(draftOptionId) : ''}
                  onChange={(val) => setDraftOptionId(val ? Number(val) : null)}
                >
                  <Label className="sr-only">Answer choices</Label>
                  {activeQuestion.options.map((opt) => (
                    <Radio key={opt.optionId} value={String(opt.optionId)}>
                      <Radio.Content>
                        <Radio.Control>
                          <Radio.Indicator />
                        </Radio.Control>
                        <span>
                          <strong className="font-mono-tabular mr-1.5">{opt.label}.</strong>
                          {opt.text}
                        </span>
                      </Radio.Content>
                    </Radio>
                  ))}
                </RadioGroup>
              )}

              {!locked && (
                <div className="flex flex-wrap items-center gap-2 pt-2">
                  <Button
                    variant="primary"
                    isDisabled={!canCheckAnswer}
                    onPress={() => submitAnswerMutation.mutate(false)}
                  >
                    <Check className="h-4 w-4" aria-hidden="true" />
                    <span>{submitAnswerMutation.isPending ? 'Checking…' : 'Check answer'}</span>
                  </Button>

                  <Button
                    variant="secondary"
                    isDisabled={submitAnswerMutation.isPending}
                    onPress={() => submitAnswerMutation.mutate(true)}
                  >
                    <SkipForward className="h-4 w-4" aria-hidden="true" />
                    <span>Skip</span>
                  </Button>

                  {!activeQuestion.hintUsed && Boolean(activeQuestion.hint) && (
                    <Popover>
                      <Button
                        variant="ghost"
                        onPress={() => {
                          if (!activeQuestion.hintUsed) {
                            hintMutation.mutate();
                          }
                        }}
                      >
                        <Lightbulb className="h-4 w-4 text-warning" aria-hidden="true" />
                        <span>Hint</span>
                      </Button>
                      <Popover.Content className="max-w-sm">
                        <Popover.Dialog className="space-y-2">
                          <Popover.Heading>AI hint</Popover.Heading>
                          <p className="text-sm text-foreground">
                            {hintMutation.isPending
                              ? 'Loading hint…'
                              : revealedHintText || activeQuestion.hint}
                          </p>
                        </Popover.Dialog>
                      </Popover.Content>
                    </Popover>
                  )}
                </div>
              )}

              {activeQuestion.hintUsed && (revealedHintText || activeQuestion.hint) && (
                <Alert status="warning">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Hint used</Alert.Title>
                    <Alert.Description>
                      {revealedHintText || activeQuestion.hint}
                    </Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              {locked && (
                <div className="space-y-3 pt-1">
                  <Alert
                    status={
                      activeQuestion.isCorrect === true
                        ? 'success'
                        : activeQuestion.isCorrect === false
                          ? 'danger'
                          : 'warning'
                    }
                  >
                    <Alert.Indicator />
                    <Alert.Content>
                      <Alert.Title>
                        {activeQuestion.isCorrect === true
                          ? 'Correct'
                          : activeQuestion.isCorrect === false
                            ? 'Incorrect'
                            : 'Skipped'}
                      </Alert.Title>
                      <Alert.Description className="space-y-1">
                        {activeQuestion.correctOptionId != null && (
                          <div>
                            Correct answer:{' '}
                            <strong>
                              {(() => {
                                const correctOpt = activeQuestion.options.find(
                                  (o) => o.optionId === activeQuestion.correctOptionId,
                                );
                                return correctOpt
                                  ? `${correctOpt.label}. ${correctOpt.text}`
                                  : `#${activeQuestion.correctOptionId}`;
                              })()}
                            </strong>
                          </div>
                        )}
                        {activeQuestion.correctOptionIds && activeQuestion.correctOptionIds.length > 0 && (
                          <div>
                            Correct choices:{' '}
                            <strong>
                              {activeQuestion.options
                                .filter((o) => activeQuestion.correctOptionIds!.includes(o.optionId))
                                .map((o) => `${o.label}. ${o.text}`)
                                .join(', ')}
                            </strong>
                          </div>
                        )}
                        {acceptedAnswers.length > 0 && (
                          <div>
                            Accepted answers: <strong>{acceptedAnswers.join(', ')}</strong>
                          </div>
                        )}
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>

                  {activeQuestion.explanation && (
                    <Card className="border border-border bg-surface-secondary">
                      <Card.Header className="pb-2">
                        <Card.Title className="flex items-center gap-2 text-sm">
                          <HelpCircle className="h-4 w-4 text-accent" aria-hidden="true" />
                          <span>Explanation</span>
                        </Card.Title>
                      </Card.Header>
                      <Card.Content className="pt-0 text-sm leading-relaxed text-foreground">
                        {activeQuestion.explanation}
                      </Card.Content>
                    </Card>
                  )}
                </div>
              )}

              <div className="flex items-center justify-between border-t border-separator pt-3">
                <Button
                  size="sm"
                  variant="secondary"
                  isDisabled={activeOrder <= 1}
                  onPress={() => selectQuestion(activeOrder - 1)}
                >
                  <ChevronLeft className="h-4 w-4" aria-hidden="true" />
                  <span>Previous</span>
                </Button>

                {activeOrder < takeState.questions.length ? (
                  <Button
                    size="sm"
                    variant="secondary"
                    onPress={() => selectQuestion(activeOrder + 1)}
                  >
                    <span>Next</span>
                    <ChevronRight className="h-4 w-4" aria-hidden="true" />
                  </Button>
                ) : (
                  <Link
                    to="/practice/result/$sessionId"
                    params={{ sessionId: String(session.sessionId) }}
                    className="no-underline"
                  >
                    <Button size="sm" variant="primary">
                      <span>Finish & review</span>
                      <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                    </Button>
                  </Link>
                )}
              </div>
            </Card.Content>
          </Card>
        </div>

        <aside className="space-y-4">
          <Card className="border border-border bg-surface">
            <Card.Header className="pb-2">
              <Card.Title className="text-sm">Question navigator</Card.Title>
              <div className="flex flex-wrap gap-1 pt-2">
                {(['all', 'incorrect', 'saved'] as const).map((filter) => (
                  <Button
                    key={filter}
                    size="sm"
                    variant={navFilter === filter ? 'primary' : 'ghost'}
                    onPress={() => setNavFilter(filter)}
                  >
                    <span className="capitalize">{filter}</span>
                  </Button>
                ))}
              </div>
            </Card.Header>
            <Card.Content>
              {filteredNavigator.length === 0 ? (
                <p className="text-xs text-muted">No questions match this filter.</p>
              ) : (
                <div className="grid grid-cols-5 gap-2">
                  {filteredNavigator.map((item) => {
                    const isCurrent = item.displayOrder === activeOrder;
                    const tone =
                      item.isCorrect === true
                        ? 'border-success text-success'
                        : item.isCorrect === false
                          ? 'border-danger text-danger'
                          : item.isAnswered
                            ? 'border-warning text-warning-foreground'
                            : 'border-border text-foreground';
                    return (
                      <Button
                        key={item.displayOrder}
                        size="sm"
                        aria-label={`Question ${item.displayOrder}`}
                        variant={isCurrent ? 'primary' : 'secondary'}
                        className={`font-mono-tabular ${!isCurrent ? tone : ''}`}
                        onPress={() => selectQuestion(item.displayOrder)}
                      >
                        <span>{item.displayOrder}</span>
                      </Button>
                    );
                  })}
                </div>
              )}
              <p className="mt-4 text-xs text-muted">
                Shortcuts: 1-4 option · S check · H hint · B save · N/P move
              </p>
            </Card.Content>
          </Card>
        </aside>
      </div>
    </div>
  );
}
