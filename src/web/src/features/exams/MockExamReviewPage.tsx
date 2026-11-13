import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner } from '@heroui/react';
import {
  ArrowLeft,
  Bookmark,
  CheckCircle2,
  HelpCircle,
  History,
  XCircle,
} from 'lucide-react';
import {
  api,
  type AttemptReviewDto,
  type AttemptReviewOptionDto,
  type AttemptReviewQuestionDto,
} from '@/lib/api';
import { parseSafeInlineSegments, toBrowserMediaUrl } from '@/lib/security';
import { ExamAudioPlayer } from '../sitting/ExamAudioPlayer';

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

export function MockExamReviewPage() {
  const { attemptId } = useParams({ strict: false }) as { attemptId?: string };
  const navigate = useNavigate();
  const id = Number(attemptId);

  const [review, setReview] = useState<AttemptReviewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [selectedSection, setSelectedSection] = useState<string>('all');
  const [selectedStatusFilter, setSelectedStatusFilter] = useState<
    'all' | 'incorrect' | 'blank' | 'flagged' | 'correct'
  >('all');

  useEffect(() => {
    if (!Number.isFinite(id) || id <= 0) {
      setError('Invalid attempt identifier.');
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    api.exams
      .review(id)
      .then((data) => {
        if (!cancelled) {
          setReview(data);
          setError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load attempt review.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [id]);

  // Distinct sections for filter
  const distinctSections = useMemo(() => {
    if (!review) return [];
    const map = new Map<string, { code: string; name: string; count: number }>();
    for (const q of review.questions) {
      const existing = map.get(q.sectionCode);
      if (existing) {
        existing.count++;
      } else {
        map.set(q.sectionCode, {
          code: q.sectionCode,
          name: q.sectionName,
          count: 1,
        });
      }
    }
    return Array.from(map.values());
  }, [review]);

  // Filtered questions
  const filteredQuestions = useMemo(() => {
    if (!review) return [];
    return review.questions.filter((q) => {
      // Section filter
      if (selectedSection !== 'all' && q.sectionCode !== selectedSection) {
        return false;
      }
      // Status filter
      if (selectedStatusFilter === 'incorrect' && q.isCorrect !== false) {
        return false;
      }
      if (selectedStatusFilter === 'correct' && q.isCorrect !== true) {
        return false;
      }
      if (selectedStatusFilter === 'blank' && q.selectedOptionId != null) {
        return false;
      }
      if (selectedStatusFilter === 'flagged' && !q.isFlagged) {
        return false;
      }
      return true;
    });
  }, [review, selectedSection, selectedStatusFilter]);

  const scrollToQuestion = (questionId: number) => {
    const el = document.getElementById(`review-question-${questionId}`);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

  if (loading) {
    return (
      <div className="mx-auto max-w-6xl p-6 flex items-center justify-center gap-3 text-muted min-h-[50vh]">
        <Spinner size="md" color="accent" />
        <span>Loading answer review...</span>
      </div>
    );
  }

  if (error && !review) {
    return (
      <div className="mx-auto max-w-6xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to load review</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
        <div className="mt-4 flex gap-3">
          <Button variant="secondary" size="sm" onClick={() => void navigate({ to: '/exams' })}>
            Back to mock exams
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() =>
              void navigate({
                to: '/exams/result/$attemptId',
                params: { attemptId: String(id) },
              })
            }
          >
            Back to results
          </Button>
        </div>
      </div>
    );
  }

  if (!review) return null;

  return (
    <div className="mx-auto max-w-6xl p-4 sm:p-6 space-y-6">
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
        <Link
          to="/exams/result/$attemptId"
          params={{ attemptId: String(review.attemptId) }}
          className="hover:text-foreground transition-colors"
        >
          Results
        </Link>
        <span aria-hidden="true">/</span>
        <span className="text-foreground font-medium" aria-current="page">
          Answer review
        </span>
      </nav>

      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            Review — {review.examName}
          </h1>
          <p className="text-xs text-muted mt-1 flex flex-wrap items-center gap-2">
            <span className="font-semibold text-foreground font-mono-tabular">
              {review.totalScore} points
            </span>
            <span>•</span>
            <span>{review.certName}</span>
            {review.bandName && (
              <>
                <span>•</span>
                <span>Band: {review.bandName}</span>
              </>
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="secondary"
            size="sm"
            onClick={() =>
              void navigate({
                to: '/exams/result/$attemptId',
                params: { attemptId: String(review.attemptId) },
              })
            }
            className="text-foreground"
          >
            <ArrowLeft className="size-4 mr-1.5" />
            Back to results
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => void navigate({ to: '/exams/history' })}
            className="text-muted hover:text-foreground"
          >
            <History className="size-4 mr-1.5" />
            Exam history
          </Button>
        </div>
      </div>

      {/* Filter and Stats Toolbar */}
      <Card className="bg-surface border-border">
        <Card.Content className="p-4 flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          {/* Filters */}
          <div className="flex flex-wrap items-center gap-3">
            {/* Section filter */}
            <div className="flex items-center gap-2">
              <label htmlFor="review-section-filter" className="text-xs font-medium text-muted">
                Section:
              </label>
              <select
                id="review-section-filter"
                value={selectedSection}
                onChange={(e) => setSelectedSection(e.target.value)}
                className="text-xs rounded-md border border-border bg-surface-secondary px-2.5 py-1.5 text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
              >
                <option value="all">All sections ({review.totalQuestions})</option>
                {distinctSections.map((sec) => (
                  <option key={sec.code} value={sec.code}>
                    {sec.code} — {sec.name} ({sec.count})
                  </option>
                ))}
              </select>
            </div>

            {/* Status filter */}
            <div className="flex items-center gap-2">
              <label htmlFor="review-status-filter" className="text-xs font-medium text-muted">
                Status:
              </label>
              <select
                id="review-status-filter"
                value={selectedStatusFilter}
                onChange={(e) => setSelectedStatusFilter(e.target.value as any)}
                className="text-xs rounded-md border border-border bg-surface-secondary px-2.5 py-1.5 text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
              >
                <option value="all">All answers</option>
                <option value="incorrect">Incorrect only</option>
                <option value="blank">Left blank only</option>
                <option value="flagged">Flagged only</option>
                <option value="correct">Correct only</option>
              </select>
            </div>
          </div>

          {/* Aggregate Badges */}
          <div className="flex flex-wrap items-center gap-2">
            <Chip color="success" variant="soft" size="sm" className="font-mono-tabular">
              <CheckCircle2 className="size-3.5 mr-1" />
              {review.correctCount} correct
            </Chip>
            <Chip color="danger" variant="soft" size="sm" className="font-mono-tabular">
              <XCircle className="size-3.5 mr-1" />
              {review.incorrectCount} incorrect
            </Chip>
            <Chip variant="soft" size="sm" className="font-mono-tabular">
              {review.blankCount} blank
            </Chip>
            {review.flaggedCount > 0 && (
              <Chip color="warning" variant="soft" size="sm" className="font-mono-tabular">
                <Bookmark className="size-3.5 mr-1" />
                {review.flaggedCount} flagged
              </Chip>
            )}
          </div>
        </Card.Content>
      </Card>

      {/* Main Review Layout: Questions + Navigator Sidebar */}
      <div className="grid grid-cols-1 lg:grid-cols-4 gap-6 items-start">
        {/* Questions Column (3 cols on lg) */}
        <div className="lg:col-span-3 space-y-4">
          {filteredQuestions.length === 0 ? (
            <Card className="p-8 text-center bg-surface border-border">
              <p className="text-sm text-muted">No questions match the current filter selection.</p>
              <Button
                variant="secondary"
                size="sm"
                className="mt-3"
                onClick={() => {
                  setSelectedSection('all');
                  setSelectedStatusFilter('all');
                }}
              >
                Reset filters
              </Button>
            </Card>
          ) : (
            filteredQuestions.map((q: AttemptReviewQuestionDto) => {
              const isCorrect = q.isCorrect === true;
              const isIncorrect = q.isCorrect === false;

              return (
                <Card
                  key={q.questionId}
                  id={`review-question-${q.questionId}`}
                  className="bg-surface border-border shadow-surface overflow-hidden scroll-mt-20"
                >
                  <Card.Header className="flex flex-row items-center justify-between pb-3 border-b border-separator/40">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-bold text-sm text-foreground">
                        Question {q.displayOrder}
                      </span>
                      <Chip size="sm" variant="secondary" className="text-xs">
                        {q.sectionCode} — {q.sectionName}
                      </Chip>

                      {isCorrect ? (
                        <Chip color="success" variant="soft" size="sm">
                          <CheckCircle2 className="size-3.5 mr-1" />
                          Correct
                        </Chip>
                      ) : isIncorrect ? (
                        <Chip color="danger" variant="soft" size="sm">
                          <XCircle className="size-3.5 mr-1" />
                          Incorrect
                        </Chip>
                      ) : (
                        <Chip variant="soft" size="sm">
                          Left blank
                        </Chip>
                      )}
                    </div>

                    {q.isFlagged && (
                      <Chip color="warning" variant="soft" size="sm">
                        <Bookmark className="size-3.5 mr-1" />
                        Flagged
                      </Chip>
                    )}
                  </Card.Header>

                  <Card.Content className="pt-4 space-y-4 text-sm">
                    {/* Audio Player if present */}
                    {q.audioPath && (
                      <div className="mb-2">
                        <ExamAudioPlayer
                          sittingKind="mock"
                          sittingId={review.attemptId}
                          questionId={q.questionId}
                          audioPath={q.audioPath}
                          audioStartMs={q.audioStartMs ?? undefined}
                          audioEndMs={q.audioEndMs ?? undefined}
                          allowReplay={true}
                        />
                      </div>
                    )}

                    {/* Passage / Context if present */}
                    {q.passage && (
                      <div className="p-4 rounded-lg bg-surface-secondary border border-border/80 text-sm leading-relaxed space-y-2">
                        {renderSafeParagraphs(q.passage)}
                      </div>
                    )}

                    {/* Image if present */}
                    {q.imagePath && (
                      <div className="rounded-lg overflow-hidden border border-border max-w-lg">
                        <img
                          src={toBrowserMediaUrl(q.imagePath) ?? undefined}
                          alt={q.imageAlt ?? `Illustration for question ${q.displayOrder}`}
                          className="w-full object-contain max-h-80"
                        />
                      </div>
                    )}

                    {/* Question Stem */}
                    <div className="text-base font-medium text-foreground">
                      {renderSafeParagraphs(q.stem)}
                    </div>

                    {/* Options List */}
                    {q.options && q.options.length > 0 && (
                      <div
                        className="space-y-2 pt-1"
                        role="group"
                        aria-label={`Options for Question ${q.displayOrder}`}
                      >
                        {q.options.map((opt: AttemptReviewOptionDto) => {
                          const isLearnerChoice = opt.optionId === q.selectedOptionId;
                          const isOptionCorrect = opt.isCorrect;

                          let optionStyles = 'border-border bg-surface text-foreground';
                          let badgeText = '';
                          let badgeColor: 'success' | 'danger' | 'accent' | 'warning' = 'accent';

                          if (isOptionCorrect && isLearnerChoice) {
                            optionStyles =
                              'border-success bg-success/10 text-success-foreground font-medium';
                            badgeText = 'Your answer · Correct';
                            badgeColor = 'success';
                          } else if (isOptionCorrect) {
                            optionStyles =
                              'border-success bg-success/5 text-success-foreground font-medium';
                            badgeText = 'Correct answer';
                            badgeColor = 'success';
                          } else if (isLearnerChoice) {
                            optionStyles =
                              'border-danger bg-danger/10 text-danger-foreground font-medium';
                            badgeText = 'Your answer';
                            badgeColor = 'danger';
                          }

                          return (
                            <div
                              key={opt.optionId}
                              className={`p-3 rounded-lg border transition-colors ${optionStyles}`}
                            >
                              <div className="flex items-start justify-between gap-3">
                                <div className="flex items-start gap-2.5">
                                  <span className="font-mono-tabular font-bold shrink-0">
                                    {opt.label}.
                                  </span>
                                  <span>{opt.text}</span>
                                </div>

                                {badgeText && (
                                  <Chip
                                    size="sm"
                                    color={badgeColor}
                                    variant="soft"
                                    className="shrink-0 text-xs"
                                  >
                                    {badgeText}
                                  </Chip>
                                )}
                              </div>

                              {/* Option Rationale */}
                              {opt.rationale && (
                                <p className="text-xs text-muted mt-2 pl-6 pt-1 border-t border-separator/30">
                                  <strong>Rationale:</strong> {opt.rationale}
                                </p>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    )}

                    {/* Free Response Answer / AI Feedback (for productive tasks) */}
                    {(q.responseText || q.responseAudioPath) && (
                      <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-3">
                        <div className="font-semibold text-foreground text-xs uppercase tracking-wide">
                          Your response
                        </div>
                        {q.responseText && (
                          <div className="text-sm font-sans whitespace-pre-wrap text-foreground">
                            {q.responseText}
                          </div>
                        )}
                        {q.responseAudioPath && (
                          <audio
                            controls
                            src={toBrowserMediaUrl(q.responseAudioPath) ?? undefined}
                            className="w-full max-w-sm h-10"
                          />
                        )}

                        {q.aiScore != null && (
                          <div className="pt-2 border-t border-separator/40 flex items-center justify-between text-xs">
                            <span className="text-muted">AI graded score:</span>
                            <span className="font-mono-tabular font-bold text-accent">
                              {q.aiScore} pts
                            </span>
                          </div>
                        )}

                        {q.aiFeedback && (
                          <div className="text-xs text-muted pt-1">
                            <strong>AI feedback:</strong> {q.aiFeedback}
                          </div>
                        )}

                        {/* Rubric scores breakdown if present */}
                        {q.rubricScores && q.rubricScores.length > 0 && (
                          <div className="pt-2 space-y-1.5 border-t border-separator/40">
                            <span className="text-xs font-semibold text-foreground">
                              Rubric breakdown:
                            </span>
                            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
                              {q.rubricScores.map((rubric) => (
                                <div
                                  key={rubric.criterionCode}
                                  className="p-2 rounded bg-surface border border-border flex items-center justify-between"
                                >
                                  <span>{rubric.criterionName}</span>
                                  <span className="font-mono-tabular font-bold">
                                    {rubric.score}/{rubric.maxScore}
                                  </span>
                                </div>
                              ))}
                            </div>
                          </div>
                        )}
                      </div>
                    )}

                    {/* Explanation */}
                    {q.explanation && (
                      <div className="p-4 rounded-lg bg-accent/5 border border-accent/20 text-xs text-foreground space-y-1">
                        <div className="font-semibold text-accent text-xs flex items-center gap-1.5">
                          <HelpCircle className="size-3.5" />
                          Explanation
                        </div>
                        <p className="leading-relaxed">{q.explanation}</p>
                      </div>
                    )}

                    {/* Audio Transcript */}
                    {q.transcript && (
                      <details className="text-xs text-muted bg-surface-secondary p-3 rounded-lg border border-border">
                        <summary className="cursor-pointer font-medium text-foreground hover:underline">
                          View audio transcript
                        </summary>
                        <p className="mt-2 leading-relaxed text-foreground whitespace-pre-wrap">
                          {q.transcript}
                        </p>
                      </details>
                    )}
                  </Card.Content>
                </Card>
              );
            })
          )}
        </div>

        {/* Navigator Sidebar (1 col on lg, sticky) */}
        <aside className="lg:col-span-1 sticky top-6 space-y-4">
          <Card className="bg-surface border-border shadow-surface">
            <Card.Header className="flex flex-row items-center justify-between pb-2 border-b border-separator/40">
              <span className="text-xs font-semibold text-foreground">Question navigator</span>
              <span className="text-xs font-mono-tabular text-muted">
                {review.totalQuestions} questions
              </span>
            </Card.Header>

            <Card.Content className="pt-3 space-y-3">
              {/* Question palette grid */}
              <div
                className="grid grid-cols-5 gap-1.5 max-h-[60vh] overflow-y-auto p-1"
                role="group"
                aria-label="Review Question Navigator"
              >
                {review.questions.map((q: AttemptReviewQuestionDto) => {
                  const isCorrect = q.isCorrect === true;
                  const isIncorrect = q.isCorrect === false;

                  let cellColor = 'bg-surface-secondary text-muted border-border';
                  if (isCorrect) {
                    cellColor = 'bg-success/20 text-success-foreground border-success/40';
                  } else if (isIncorrect) {
                    cellColor = 'bg-danger/20 text-danger-foreground border-danger/40';
                  }

                  return (
                    <button
                      key={q.questionId}
                      type="button"
                      onClick={() => scrollToQuestion(q.questionId)}
                      title={`Question ${q.displayOrder}: ${
                        isCorrect ? 'Correct' : isIncorrect ? 'Incorrect' : 'Blank'
                      }${q.isFlagged ? ' (Flagged)' : ''}`}
                      className={`h-8 rounded text-xs font-mono-tabular font-medium border flex items-center justify-center relative hover:scale-105 transition-all ${cellColor}`}
                    >
                      {q.displayOrder}
                      {q.isFlagged && (
                        <span className="absolute -top-0.5 -right-0.5 size-2 rounded-full bg-warning ring-1 ring-background" />
                      )}
                    </button>
                  );
                })}
              </div>

              {/* Legend */}
              <div className="pt-2 border-t border-separator/40 text-[11px] text-muted space-y-1.5">
                <div className="flex items-center gap-2">
                  <span className="size-3 rounded bg-success/20 border border-success/40" />
                  <span>Correct</span>
                </div>
                <div className="flex items-center gap-2">
                  <span className="size-3 rounded bg-danger/20 border border-danger/40" />
                  <span>Incorrect</span>
                </div>
                <div className="flex items-center gap-2">
                  <span className="size-3 rounded bg-surface-secondary border border-border" />
                  <span>Left blank</span>
                </div>
                <div className="flex items-center gap-2">
                  <span className="size-2 rounded-full bg-warning" />
                  <span>Flagged for review</span>
                </div>
              </div>
            </Card.Content>
          </Card>
        </aside>
      </div>
    </div>
  );
}
