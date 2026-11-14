import { Alert, Card, Chip, Label, ProgressBar, Table } from '@heroui/react';
import { BookOpenCheck, Sparkles } from 'lucide-react';
import { parseSafeInlineSegments } from '@/lib/security';
import { resolveWritingErrorCategoryLabel, type WritingErrorSpan } from './writing/types';

export interface ProductiveCriterionItem {
  criterionId: number;
  code: string;
  name: string;
  score: number;
  maxScore: number;
  comment?: string | null;
}

interface ProductiveAiFeedbackProps {
  text?: string | null;
  textLabel: string;
  criteria: ProductiveCriterionItem[];
  errorSpans?: WritingErrorSpan[] | null;
  priorityFix?: string | null;
  modelRewrite?: string | null;
  criteriaHeading?: string;
}

export function SafeAnnotatedText({ text }: { text: string }) {
  const segments = parseSafeInlineSegments(text);
  return (
    <>
      {segments.map((seg, idx) =>
        seg.type === 'bold' ? (
          <strong key={idx} className="font-semibold text-foreground">
            {seg.value}
          </strong>
        ) : (
          <span key={idx}>{seg.value}</span>
        ),
      )}
    </>
  );
}

function HighlightedResponseText({
  text,
  errorSpans,
}: {
  text: string;
  errorSpans?: WritingErrorSpan[] | null;
}) {
  if (!errorSpans || errorSpans.length === 0) {
    return <p className="text-sm leading-relaxed text-foreground whitespace-pre-line">{text}</p>;
  }

  const matches: Array<{ start: number; end: number; span: WritingErrorSpan }> = [];
  for (const span of errorSpans) {
    if (!span.originalSnippet) continue;
    const idx = text.indexOf(span.originalSnippet);
    if (idx >= 0) {
      const end = idx + span.originalSnippet.length;
      const overlaps = matches.some((m) => idx < m.end && end > m.start);
      if (!overlaps) {
        matches.push({ start: idx, end, span });
      }
    }
  }

  if (matches.length === 0) {
    return <p className="text-sm leading-relaxed text-foreground whitespace-pre-line">{text}</p>;
  }

  matches.sort((a, b) => a.start - b.start);
  const parts: Array<{ key: string; value: string; span?: WritingErrorSpan }> = [];
  let cursor = 0;

  matches.forEach((m, i) => {
    if (m.start > cursor) {
      parts.push({ key: `txt-${i}`, value: text.slice(cursor, m.start) });
    }
    parts.push({ key: `mark-${i}`, value: text.slice(m.start, m.end), span: m.span });
    cursor = m.end;
  });

  if (cursor < text.length) {
    parts.push({ key: 'txt-end', value: text.slice(cursor) });
  }

  return (
    <p className="text-sm leading-relaxed text-foreground whitespace-pre-line">
      {parts.map((part) =>
        part.span ? (
          <mark
            key={part.key}
            title={`${part.span.correctedSnippet}: ${part.span.explanationVi ?? part.span.explanation ?? ''}`}
            className="rounded bg-warning/30 px-1 py-0.5 text-foreground underline decoration-warning decoration-2"
          >
            {part.value}
          </mark>
        ) : (
          <span key={part.key}>{part.value}</span>
        ),
      )}
    </p>
  );
}

export function ProductiveAiFeedback({
  text,
  textLabel,
  criteria,
  errorSpans,
  priorityFix,
  modelRewrite,
  criteriaHeading = 'Rubric criteria',
}: ProductiveAiFeedbackProps) {
  return (
    <div className="space-y-5">
      {priorityFix && (
        <Alert status="accent">
          <Sparkles className="size-4" aria-hidden="true" />
          <Alert.Content>
            <Alert.Title>Priority fix</Alert.Title>
            <Alert.Description>
              <SafeAnnotatedText text={priorityFix} />
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {text && (
        <div className="rounded-lg border border-border bg-surface-secondary p-3 space-y-1.5">
          <div className="text-xs font-semibold uppercase tracking-wider text-muted">{textLabel}</div>
          <HighlightedResponseText text={text} errorSpans={errorSpans} />
        </div>
      )}

      {criteria.length > 0 && (
        <div className="space-y-3">
          <div className="text-xs font-semibold uppercase tracking-wider text-muted">{criteriaHeading}</div>
          <div className="grid gap-3 md:grid-cols-2">
            {criteria.map((criterion) => {
              const hasComment = criterion.comment && criterion.comment.trim().length > 0;
              return (
                <Card key={criterion.criterionId} className="border-border">
                  <Card.Content className="space-y-2 p-3">
                    <ProgressBar
                      value={criterion.score}
                      minValue={0}
                      maxValue={criterion.maxScore > 0 ? criterion.maxScore : 1}
                      color={criterion.score >= criterion.maxScore * 0.7 ? 'success' : 'accent'}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <Label className="text-sm font-medium text-foreground">{criterion.name}</Label>
                        <ProgressBar.Output className="font-mono-tabular text-xs font-semibold text-foreground">
                          {criterion.score} / {criterion.maxScore}
                        </ProgressBar.Output>
                      </div>
                      <ProgressBar.Track>
                        <ProgressBar.Fill />
                      </ProgressBar.Track>
                    </ProgressBar>
                    <p className="text-xs text-muted">
                      {hasComment ? (
                        <SafeAnnotatedText text={criterion.comment as string} />
                      ) : (
                        'No note was recorded for this criterion on this grading.'
                      )}
                    </p>
                  </Card.Content>
                </Card>
              );
            })}
          </div>
        </div>
      )}

      {errorSpans && errorSpans.length > 0 && (
        <div className="space-y-2">
          <div className="text-xs font-semibold uppercase tracking-wider text-muted">Annotated error spans</div>
          <Table>
            <Table.ScrollContainer>
              <Table.Content aria-label="Corrections">
                <Table.Header>
                  <Table.Column isRowHeader>Original</Table.Column>
                  <Table.Column>Corrected</Table.Column>
                  <Table.Column>Vietnamese Explanation</Table.Column>
                </Table.Header>
                <Table.Body>
                  {errorSpans.map((span, idx) => (
                    <Table.Row key={idx} id={`span-${idx}`}>
                      <Table.Cell>
                        <div className="space-y-1">
                          <Chip size="sm" variant="soft" color="warning">
                            {resolveWritingErrorCategoryLabel(span.category)}
                          </Chip>
                          <div className="text-sm line-through text-danger">{span.originalSnippet}</div>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-sm font-medium text-success">{span.correctedSnippet}</span>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs text-muted">
                          <SafeAnnotatedText text={(span.explanationVi ?? span.explanation ?? '') as string} />
                        </span>
                      </Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Content>
            </Table.ScrollContainer>
          </Table>
        </div>
      )}

      {modelRewrite && (
        <Card className="border-border bg-surface-secondary">
          <Card.Header className="pb-1">
            <Card.Title className="inline-flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-muted">
              <BookOpenCheck className="size-3.5" aria-hidden="true" />
              Model rewrite
            </Card.Title>
          </Card.Header>
          <Card.Content>
            <p className="text-sm leading-relaxed text-foreground whitespace-pre-line">
              <SafeAnnotatedText text={modelRewrite} />
            </p>
          </Card.Content>
        </Card>
      )}
    </div>
  );
}
