import React, { useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Radio,
  RadioGroup,
  Spinner,
} from '@heroui/react';
import {
  AlertCircle,
  ArrowLeft,
  ArrowRight,
  BookOpen,
  CheckCircle2,
  HelpCircle,
  RotateCw,
  XCircle,
} from 'lucide-react';
import {
  api,
  ApiClientError,
  LessonDetailDto,
  LessonProgressDto,
  MicroCheckResultDto,
} from '../../lib/api';
import { resolveSafeReturnUrl } from '../../lib/security';
import { SafeMarkdown } from './SafeMarkdown';

export function LessonReaderPage() {
  const { lessonId } = useParams({ strict: false }) as { lessonId: string };
  const numericLessonId = parseInt(lessonId, 10);
  const search = useSearch({ strict: false }) as { module?: string; returnUrl?: string };
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const currentModule = (search?.module?.toLowerCase() === 'remedial' ? 'remedial' : 'primary') as
    | 'primary'
    | 'remedial';
  const otherModule = currentModule === 'primary' ? 'remedial' : 'primary';

  const [activeCardIndex, setActiveCardIndex] = useState(0);
  const [selectedOption, setSelectedOption] = useState<string>('');
  const [microCheckResult, setMicroCheckResult] = useState<MicroCheckResultDto | null>(null);
  const [microCheckError, setMicroCheckError] = useState<string | null>(null);
  const [isCompletedState, setIsCompletedState] = useState(false);

  // 1. Fetch lesson details
  const {
    data: lesson,
    isLoading: isLessonLoading,
    error: lessonError,
    refetch: refetchLesson,
  } = useQuery<LessonDetailDto>({
    queryKey: ['learner', 'lesson', numericLessonId, currentModule],
    queryFn: () => api.learningPath.lesson(numericLessonId, currentModule),
    enabled: !isNaN(numericLessonId),
  });

  // 2. Fetch lesson progress
  const { data: progress } = useQuery<LessonProgressDto>({
    queryKey: ['learner', 'lesson-progress', numericLessonId, currentModule],
    queryFn: () => api.learningPath.lessonProgress(numericLessonId, currentModule),
    enabled: !isNaN(numericLessonId),
  });

  // Resume card index initialization
  useEffect(() => {
    if (progress && lesson?.cards) {
      if (progress.isCompleted) {
        setIsCompletedState(true);
      }
      if (progress.resumeCardIndex && progress.resumeCardIndex > 0) {
        const targetIdx = Math.min(progress.resumeCardIndex - 1, Math.max(0, lesson.cards.length - 1));
        setActiveCardIndex(targetIdx);
      }
    }
  }, [progress, lesson]);

  // Micro-check mutation (server-side grading)
  const microCheckMutation = useMutation({
    mutationFn: async (params: { cardId: number; option: string }) => {
      return await api.learningPath.submitMicroCheck(numericLessonId, params.cardId, params.option);
    },
    onSuccess: (result) => {
      setMicroCheckResult(result);
      setMicroCheckError(null);
      if (result.isCorrect) {
        setIsCompletedState(true);
      }
      queryClient.invalidateQueries({
        queryKey: ['learner', 'lesson-progress', numericLessonId, currentModule],
      });
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setMicroCheckError(err.detail || err.title || 'Could not verify your answer.');
      } else {
        setMicroCheckError('A network error occurred. Please try checking again.');
      }
    },
  });

  // Record card view mutation
  const recordViewMutation = useMutation({
    mutationFn: async (cardId: number) => {
      await api.learningPath.recordCardView(numericLessonId, cardId);
    },
  });

  if (isNaN(numericLessonId)) {
    return (
      <div className="max-w-3xl mx-auto py-8">
        <Alert status="danger">
          <Alert.Title>Invalid lesson ID</Alert.Title>
        </Alert>
      </div>
    );
  }

  if (isLessonLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[400px] space-y-3">
        <Spinner size="lg" />
        <p className="text-sm text-muted font-mono">Loading micro-lesson…</p>
      </div>
    );
  }

  if (lessonError || !lesson) {
    return (
      <div className="max-w-3xl mx-auto py-8 space-y-4">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Could not load lesson</Alert.Title>
            <Alert.Description>
              {lessonError instanceof Error ? lessonError.message : 'Please check your connection.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <Button variant="ghost" size="sm" onPress={() => refetchLesson()} className="gap-1.5">
          <RotateCw className="w-3.5 h-3.5" /> Retry
        </Button>
      </div>
    );
  }

  const cards = lesson.cards ?? [];
  const totalCards = cards.length;
  const currentCard = cards[activeCardIndex];
  const isFirstCard = activeCardIndex === 0;
  const isLastCard = activeCardIndex === totalCards - 1;
  const safeReturnUrl = resolveSafeReturnUrl(search?.returnUrl, '/learning-path');

  const handleNext = () => {
    if (activeCardIndex < totalCards - 1) {
      const nextIdx = activeCardIndex + 1;
      setActiveCardIndex(nextIdx);
      const nextCard = cards[nextIdx];
      if (nextCard) {
        recordViewMutation.mutate(nextCard.cardId);
      }
    }
  };

  const handlePrev = () => {
    if (activeCardIndex > 0) {
      setActiveCardIndex(activeCardIndex - 1);
    }
  };

  const handleCheckAnswer = () => {
    if (!currentCard || !selectedOption) return;
    setMicroCheckError(null);
    microCheckMutation.mutate({ cardId: currentCard.cardId, option: selectedOption });
  };

  return (
    <div className="max-w-3xl mx-auto py-6 space-y-6">
      {/* Breadcrumb navigation */}
      <nav className="flex items-center gap-1.5 text-xs text-muted font-mono" aria-label="Breadcrumb">
        <Link to="/dashboard" className="hover:text-foreground no-underline">
          Home
        </Link>
        <span>›</span>
        <Link to="/learning-path" className="hover:text-foreground no-underline">
          My learning path
        </Link>
        <span>›</span>
        <span className="text-foreground font-semibold">{lesson.title}</span>
      </nav>

      {/* Lesson Header Banner */}
      <div className="p-4 rounded-xl border border-border bg-surface-secondary flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <span className="text-xs uppercase font-mono font-bold text-muted tracking-wider block">
            {lesson.topicCode} · LEVEL {lesson.cefrLevel}
          </span>
          <h1 className="text-xl sm:text-2xl font-bold text-foreground m-0 mt-0.5">{lesson.title}</h1>
          <div className="flex items-center gap-2 mt-2">
            <span
              className={`text-xs px-2 py-0.5 rounded font-mono font-semibold ${
                currentModule === 'remedial' ? 'bg-warning text-foreground' : 'bg-accent text-white'
              }`}
            >
              {currentModule === 'remedial' ? 'Remedial module' : 'Primary module'}
            </span>
            {lesson.hasRemedialModule && (
              <button
                type="button"
                onClick={() =>
                  navigate({
                    to: `/lessons/${lesson.lessonId}`,
                    search: { module: otherModule, returnUrl: search?.returnUrl },
                  })
                }
                className="text-xs text-accent hover:underline bg-transparent border-0 p-0 cursor-pointer"
              >
                Switch to {otherModule} module
              </button>
            )}
          </div>
        </div>

        <div className="flex sm:flex-col items-center sm:items-end justify-between sm:justify-center gap-3 shrink-0 text-xs font-mono">
          <div>
            <span className="text-muted block text-right">Duration</span>
            <span className="font-bold text-foreground">~{lesson.estimatedMinutes} min</span>
          </div>
          <div>
            <span className="text-muted block text-right">Progress</span>
            <span className="font-bold text-foreground" id="deckProgressLabel">
              {totalCards > 0 ? `Card ${activeCardIndex + 1} of ${totalCards}` : '0 cards'}
            </span>
          </div>
        </div>
      </div>

      {/* Empty State when no cards exist */}
      {totalCards === 0 ? (
        <Card className="border-border">
          <Card.Content className="p-8 text-center space-y-3">
            <div className="p-2.5 rounded-full bg-surface-secondary text-muted inline-block">
              <BookOpen className="w-6 h-6" />
            </div>
            <h2 className="text-base font-bold text-foreground m-0">This module has no cards yet.</h2>
            <p className="text-xs text-muted max-w-sm mx-auto m-0">
              The {currentModule} module for this cell has not been authored yet.
            </p>
            <div className="pt-2">
              <Link
                to="/learning-path"
                className="inline-flex items-center gap-1.5 px-4 py-2 rounded-md bg-accent text-white text-xs font-semibold no-underline"
              >
                Back to my path
              </Link>
            </div>
          </Card.Content>
        </Card>
      ) : (
        /* Lesson Card Deck */
        <div className="space-y-4">
          <div className="flex items-center justify-between text-xs font-mono text-muted">
            <span className="px-2 py-0.5 rounded bg-surface-secondary border border-border font-bold text-foreground uppercase">
              CARD {activeCardIndex + 1} OF {totalCards}
            </span>
            <span>
              {activeCardIndex + 1} / {totalCards} cards
            </span>
          </div>

          {currentCard && (
            <Card className="border-border">
              <Card.Header className="border-b border-border pb-3">
                <div className="flex flex-col gap-1.5">
                  <div className="flex items-center gap-2">
                    {currentCard.cardType === 'rule' && (
                      <span className="text-xs font-mono px-2 py-0.5 rounded bg-accent text-white font-bold">
                        RULE
                      </span>
                    )}
                    {currentCard.cardType === 'pitfall' && (
                      <span className="text-xs font-mono px-2 py-0.5 rounded bg-warning text-foreground font-bold">
                        COMMON PITFALLS &amp; EXAM TRAPS
                      </span>
                    )}
                    {currentCard.cardType === 'example' && (
                      <span className="text-xs font-mono px-2 py-0.5 rounded bg-accent/80 text-white font-bold">
                        WORKED EXAMPLE
                      </span>
                    )}
                    {currentCard.cardType === 'micro_check' && (
                      <span className="text-xs font-mono px-2 py-0.5 rounded bg-success text-white font-bold">
                        MICRO-CHECK (1 QUESTION)
                      </span>
                    )}
                  </div>
                  <h2 className="text-lg font-bold text-foreground m-0">{currentCard.title}</h2>
                </div>
              </Card.Header>

              <Card.Content className="p-6 space-y-4">
                {/* Content body for rule, pitfall, example */}
                {currentCard.contentMarkdown && (
                  <div className="text-sm text-foreground leading-relaxed">
                    <SafeMarkdown content={currentCard.contentMarkdown} />
                  </div>
                )}

                {/* Micro-check question rendering */}
                {currentCard.cardType === 'micro_check' && (
                  <div className="space-y-4 pt-2">
                    {currentCard.questionPrompt && (
                      <p className="font-semibold text-sm text-foreground m-0">{currentCard.questionPrompt}</p>
                    )}

                    <div className="space-y-2" id="microCheckOptions">
                      {(['A', 'B', 'C', 'D'] as const).map((optKey) => {
                        const optText = (currentCard as any)[`option${optKey}`];
                        if (!optText) return null;
                        const isSelected = selectedOption === optKey;

                        return (
                          <label
                            key={optKey}
                            className={`flex items-start gap-3 p-3 rounded-lg border cursor-pointer transition-all ${
                              isSelected
                                ? 'border-accent bg-accent/5 ring-1 ring-accent'
                                : 'border-border bg-surface hover:bg-surface-secondary'
                            }`}
                          >
                            <input
                              type="radio"
                              name="microCheck"
                              value={optKey}
                              checked={isSelected}
                              onChange={() => setSelectedOption(optKey)}
                              className="mt-0.5"
                            />
                            <div className="text-sm text-foreground">
                              <span className="font-mono font-bold mr-1.5">({optKey})</span>
                              {optText}
                            </div>
                          </label>
                        );
                      })}
                    </div>

                    {microCheckError && (
                      <Alert status="danger">
                        <Alert.Indicator>
                          <AlertCircle className="w-4 h-4" />
                        </Alert.Indicator>
                        <Alert.Content>
                          <Alert.Description>{microCheckError}</Alert.Description>
                        </Alert.Content>
                      </Alert>
                    )}

                    <div className="pt-1">
                      <Button
                        id="btnCheckAnswer"
                        variant="primary"
                        size="sm"
                        isDisabled={microCheckMutation.isPending || !selectedOption}
                        onPress={handleCheckAnswer}
                        onClick={handleCheckAnswer}
                      >
                        {microCheckMutation.isPending ? <Spinner size="sm" /> : 'Check answer'}
                      </Button>
                    </div>

                    {/* Graded Verdict - revealed ONLY upon server grading response (#155 / #240) */}
                    {microCheckResult && (
                      <div
                        id="microCheckExplanation"
                        className={`p-4 rounded-lg border space-y-1.5 ${
                          microCheckResult.isCorrect
                            ? 'border-success/40 bg-success/10 text-success'
                            : 'border-danger/40 bg-danger/10 text-danger'
                        }`}
                      >
                        <div className="flex items-center gap-2">
                          {microCheckResult.isCorrect ? (
                            <CheckCircle2 className="w-5 h-5 text-success" />
                          ) : (
                            <XCircle className="w-5 h-5 text-danger" />
                          )}
                          <strong id="microCheckVerdict" className="text-sm font-bold">
                            {microCheckResult.isCorrect ? 'Correct!' : 'Incorrect'}
                          </strong>
                        </div>
                        <p className="text-xs m-0 text-foreground" id="microCheckCorrectAnswer">
                          Correct option: <strong>({microCheckResult.correctOption})</strong>
                        </p>
                        {microCheckResult.explanation && (
                          <p className="text-xs m-0 text-muted" id="microCheckReason">
                            <strong>Explanation: </strong>
                            <span id="microCheckReasonText">{microCheckResult.explanation}</span>
                          </p>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </Card.Content>
            </Card>
          )}

          {/* Deck Navigation Controls */}
          <div className="deck-navigation flex items-center justify-between pt-2">
            <Button
              id="btnPrev"
              variant="secondary"
              size="sm"
              isDisabled={isFirstCard}
              onPress={handlePrev}
              onClick={handlePrev}
            >
              Previous card
            </Button>

            <div className="flex items-center gap-2">
              {!isLastCard && (
                <Button
                  id="btnNext"
                  variant="primary"
                  size="sm"
                  onPress={handleNext}
                  onClick={handleNext}
                >
                  Next card
                </Button>
              )}

              {(isLastCard || isCompletedState) && (
                <a
                  id="btnFinish"
                  href={safeReturnUrl}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-success text-white font-medium text-xs no-underline hover:opacity-90 transition-opacity"
                >
                  Finish lesson &amp; return to path
                </a>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
