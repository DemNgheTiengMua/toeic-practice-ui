import React, { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Description,
  Label,
  ListBox,
  Pagination,
  Popover,
  Select,
  Spinner,
  Table,
  TextArea,
  TextField,
} from '@heroui/react';
import {
  AlertTriangle,
  Bookmark,
  Check,
  ChevronDown,
  ChevronUp,
  ExternalLink,
  Play,
  StickyNote,
  Trash2,
} from 'lucide-react';
import { useAuth } from '@/context/AuthContext';
import { api, SavedQuestionItemDto } from '@/lib/api';
import { toBrowserMediaUrl } from '@/lib/security';
import { PracticeAudioPane } from './PracticeAudioPane';
import {
  formatLocalReopenTime,
  formatSavedSourceLabel,
  getPracticeErrorMessage,
  readSavedNote,
  removeSavedNote,
  writeSavedNote,
} from './practiceUtils';

const ITEMS_PER_PAGE = 10;

function SavedNotePopover({
  item,
  onChanged,
}: {
  item: SavedQuestionItemDto;
  onChanged: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(() =>
    readSavedNote({
      savedQuestionId: item.savedQuestionId,
      questionId: item.questionId,
      itemVersionId: item.itemVersionId,
    }),
  );

  const handleOpenChange = (nextOpen: boolean) => {
    setOpen(nextOpen);
    if (nextOpen) {
      setDraft(
        readSavedNote({
          savedQuestionId: item.savedQuestionId,
          questionId: item.questionId,
          itemVersionId: item.itemVersionId,
        }),
      );
    }
  };

  const handleSave = () => {
    writeSavedNote(
      {
        savedQuestionId: item.savedQuestionId,
        questionId: item.questionId,
        itemVersionId: item.itemVersionId,
      },
      draft,
    );
    onChanged();
    setOpen(false);
  };

  return (
    <Popover isOpen={open} onOpenChange={handleOpenChange}>
      <Button size="sm" variant="ghost">
        <StickyNote className="h-3.5 w-3.5" aria-hidden="true" />
        <span>Note</span>
      </Button>
      <Popover.Content className="w-80">
        <Popover.Dialog className="space-y-3">
          <Popover.Heading>Personal study note</Popover.Heading>
          <TextField aria-label="Study note" value={draft} onChange={setDraft}>
            <Label className="sr-only">Study note</Label>
            <TextArea rows={3} placeholder="Add a reminder for this saved question..." />
            <Description>Stored locally in this browser.</Description>
          </TextField>
          <div className="flex justify-end">
            <Button size="sm" variant="primary" onPress={handleSave}>
              <Check className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Save note</span>
            </Button>
          </div>
        </Popover.Dialog>
      </Popover.Content>
    </Popover>
  );
}

export function SavedQuestionsPage() {
  const { learnerContext } = useAuth();
  const queryClient = useQueryClient();

  const [sourceFilter, setSourceFilter] = useState<string>('all');
  const [sectionFilter, setSectionFilter] = useState<string>('all');
  const [categoryFilter, setCategoryFilter] = useState<string>('all');
  const [page, setPage] = useState<number>(1);
  const [expandedIds, setExpandedIds] = useState<Record<number, boolean>>({});
  const [noteTick, setNoteTick] = useState<number>(0);
  const [actionError, setActionError] = useState<string | null>(null);

  const activeCertCode = learnerContext?.active?.code ?? 'default';

  const queryParams = useMemo(
    () => ({
      source: sourceFilter !== 'all' ? sourceFilter : undefined,
      sectionId: sectionFilter !== 'all' ? Number(sectionFilter) : undefined,
      categoryId: categoryFilter !== 'all' ? Number(categoryFilter) : undefined,
    }),
    [sourceFilter, sectionFilter, categoryFilter],
  );

  const savedQuery = useQuery({
    queryKey: ['saved-questions', activeCertCode, queryParams],
    queryFn: () => api.practice.savedQuestions(queryParams),
  });

  const removeMutation = useMutation({
    mutationFn: async (item: SavedQuestionItemDto) => {
      const result = await api.practice.unsaveQuestion(item.savedQuestionId);
      removeSavedNote({
        savedQuestionId: item.savedQuestionId,
        questionId: item.questionId,
        itemVersionId: item.itemVersionId,
      });
      return result;
    },
    onSuccess: () => {
      setActionError(null);
      void queryClient.invalidateQueries({ queryKey: ['saved-questions'] });
    },
    onError: (err) => {
      setActionError(getPracticeErrorMessage(err, 'Unable to remove saved question.'));
    },
  });

  const view = savedQuery.data;
  const allItems = view?.questions ?? [];
  const totalPages = Math.max(1, Math.ceil(allItems.length / ITEMS_PER_PAGE));
  const safePage = Math.min(page, totalPages);

  const pageItems = useMemo(() => {
    const start = (safePage - 1) * ITEMS_PER_PAGE;
    return allItems.slice(start, start + ITEMS_PER_PAGE);
  }, [allItems, safePage]);

  const pageNumbers = useMemo(() => {
    const pages: number[] = [];
    for (let i = 1; i <= totalPages; i++) {
      pages.push(i);
    }
    return pages;
  }, [totalPages]);

  const toggleExpand = (savedQuestionId: number) => {
    setExpandedIds((prev) => ({ ...prev, [savedQuestionId]: !prev[savedQuestionId] }));
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-separator pb-4">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="font-display text-2xl font-bold tracking-tight text-foreground">
              Saved questions bank
            </h1>
            {view?.certificate && (
              <Chip size="sm" variant="soft">
                <Chip.Label>{view.certificate.name}</Chip.Label>
              </Chip>
            )}
          </div>
          <p className="text-sm text-muted">
            Review bookmarked items and missed questions with explanations, audio, and personal study notes.
          </p>
        </div>

        <Link to="/practice" className="no-underline">
          <Button size="sm" variant="primary">
            <Play className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Free practice</span>
          </Button>
        </Link>
      </header>

      {actionError && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Action failed</Alert.Title>
            <Alert.Description>{actionError}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {savedQuery.isLoading ? (
        <Card className="p-8">
          <div className="flex items-center justify-center gap-3 text-sm text-muted">
            <Spinner size="sm" />
            <span>Loading saved questions…</span>
          </div>
        </Card>
      ) : savedQuery.isError ? (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Failed to load saved questions</Alert.Title>
            <Alert.Description>{getPracticeErrorMessage(savedQuery.error)}</Alert.Description>
          </Alert.Content>
        </Alert>
      ) : (
        <>
          <section className="flex flex-wrap items-end justify-between gap-4">
            <div className="flex flex-wrap items-center gap-2">
              <Button
                size="sm"
                variant={sourceFilter === 'all' ? 'primary' : 'secondary'}
                onPress={() => {
                  setSourceFilter('all');
                  setPage(1);
                }}
              >
                <span>All saved</span>
                <span className="font-mono-tabular ml-1">({view?.totalSavedCount ?? 0})</span>
              </Button>
              <Button
                size="sm"
                variant={sourceFilter === 'manual' ? 'primary' : 'secondary'}
                onPress={() => {
                  setSourceFilter('manual');
                  setPage(1);
                }}
              >
                <span>Manual bookmarks</span>
                <span className="font-mono-tabular ml-1">({view?.manualSavedCount ?? 0})</span>
              </Button>
              <Button
                size="sm"
                variant={sourceFilter === 'incorrect' ? 'primary' : 'secondary'}
                onPress={() => {
                  setSourceFilter('incorrect');
                  setPage(1);
                }}
              >
                <span>Incorrect only</span>
                <span className="font-mono-tabular ml-1">({view?.incorrectSavedCount ?? 0})</span>
              </Button>
            </div>

            <div className="flex flex-wrap items-end gap-3">
              <Select
                aria-label="Filter by section"
                className="w-52"
                value={sectionFilter}
                onChange={(key) => {
                  setSectionFilter(key != null ? String(key) : 'all');
                  setPage(1);
                }}
              >
                <Label>Section</Label>
                <Select.Trigger>
                  <Select.Value />
                  <Select.Indicator />
                </Select.Trigger>
                <Select.Popover>
                  <ListBox>
                    <ListBox.Item id="all" textValue="All sections">
                      All sections
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                    {(view?.sections ?? []).map((sec) => (
                      <ListBox.Item
                        key={sec.sectionId}
                        id={String(sec.sectionId)}
                        textValue={`${sec.name} (${sec.count})`}
                      >
                        {sec.name} ({sec.count})
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                    ))}
                  </ListBox>
                </Select.Popover>
              </Select>

              <Select
                aria-label="Filter by category"
                className="w-52"
                value={categoryFilter}
                onChange={(key) => {
                  setCategoryFilter(key != null ? String(key) : 'all');
                  setPage(1);
                }}
              >
                <Label>Category</Label>
                <Select.Trigger>
                  <Select.Value />
                  <Select.Indicator />
                </Select.Trigger>
                <Select.Popover>
                  <ListBox>
                    <ListBox.Item id="all" textValue="All categories">
                      All categories
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                    {(view?.categories ?? []).map((cat) => (
                      <ListBox.Item
                        key={cat.categoryId}
                        id={String(cat.categoryId)}
                        textValue={`${cat.name} (${cat.count})`}
                      >
                        {cat.name} ({cat.count})
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                    ))}
                  </ListBox>
                </Select.Popover>
              </Select>
            </div>
          </section>

          {allItems.length === 0 ? (
            <Card className="border border-border bg-surface p-8 text-center">
              <div className="mx-auto max-w-md space-y-3">
                <Bookmark className="mx-auto h-8 w-8 text-muted" aria-hidden="true" />
                <h2 className="font-display text-lg font-semibold text-foreground">
                  No saved questions match
                </h2>
                <p className="text-sm text-muted">
                  Bookmark tricky questions during a free practice drill or adjust the filters above.
                </p>
                <Link to="/practice" className="inline-block no-underline">
                  <Button size="sm" variant="primary">
                    Start free practice
                  </Button>
                </Link>
              </div>
            </Card>
          ) : (
            <div className="space-y-4">
              <Card className="border border-border bg-surface">
                <Table>
                  <Table.ScrollContainer>
                    <Table.Content aria-label="Saved questions list">
                      <Table.Header>
                        <Table.Column isRowHeader>Question & explanation</Table.Column>
                        <Table.Column>Scope</Table.Column>
                        <Table.Column>Source</Table.Column>
                        <Table.Column>Actions</Table.Column>
                      </Table.Header>
                      <Table.Body>
                        {pageItems.map((item) => {
                          const isExpanded = Boolean(expandedIds[item.savedQuestionId]);
                          const imageUrl = toBrowserMediaUrl(item.imagePath);
                          const savedAtText = formatLocalReopenTime(item.savedAt);
                          const localNote = readSavedNote({
                            savedQuestionId: item.savedQuestionId,
                            questionId: item.questionId,
                            itemVersionId: item.itemVersionId,
                          });
                          void noteTick;

                          return (
                            <Table.Row key={item.savedQuestionId}>
                              <Table.Cell className="max-w-2xl space-y-2 align-top">
                                <div className="flex flex-wrap items-center gap-2">
                                  {item.categoryName && (
                                    <Chip size="sm" variant="soft" color="accent">
                                      <Chip.Label>{item.categoryName}</Chip.Label>
                                    </Chip>
                                  )}
                                  {item.isRetiredDefective && (
                                    <Chip size="sm" variant="soft" color="warning">
                                      <Chip.Label className="inline-flex items-center gap-1">
                                        <AlertTriangle className="h-3 w-3" aria-hidden="true" />
                                        Withdrawn as defective
                                      </Chip.Label>
                                    </Chip>
                                  )}
                                  {savedAtText && (
                                    <span className="font-mono-tabular text-xs text-muted">
                                      Saved {savedAtText}
                                    </span>
                                  )}
                                </div>

                                <p className="font-medium text-foreground">{item.stem}</p>

                                {localNote && (
                                  <div className="rounded-md border border-accent/30 bg-surface-secondary px-3 py-2 text-xs text-foreground">
                                    <strong>Personal note:</strong> {localNote}
                                  </div>
                                )}

                                {item.isRetiredDefective && (
                                  <Alert status="warning">
                                    <Alert.Indicator />
                                    <Alert.Content>
                                      <Alert.Title>Withdrawn as defective</Alert.Title>
                                      <Alert.Description>
                                        This retired item may contain an inaccurate answer key and is kept only for historical context.
                                      </Alert.Description>
                                    </Alert.Content>
                                  </Alert>
                                )}

                                {isExpanded && (
                                  <div className="space-y-3 pt-1">
                                    {item.passage && (
                                      <div className="whitespace-pre-line rounded-md border border-border bg-surface-secondary p-3 text-xs text-foreground">
                                        {item.passage}
                                      </div>
                                    )}
                                    {imageUrl && (
                                      <img
                                        src={imageUrl}
                                        alt={item.stem}
                                        className="max-h-56 rounded-md border border-border object-contain"
                                      />
                                    )}
                                    {(item.audioPath || item.transcript) && (
                                      <PracticeAudioPane
                                        compact
                                        audioPath={item.audioPath}
                                        transcript={item.transcript}
                                      />
                                    )}
                                    {item.options.length > 0 && (
                                      <ul className="space-y-1 text-xs">
                                        {item.options.map((opt) => (
                                          <li
                                            key={opt.optionId}
                                            className={`rounded border px-2.5 py-1.5 ${
                                              opt.isCorrect && !item.isRetiredDefective
                                                ? 'border-success bg-success/10 font-medium text-foreground'
                                                : 'border-border bg-surface-secondary text-muted'
                                            }`}
                                          >
                                            <strong className="font-mono-tabular mr-1.5">
                                              {opt.label}.
                                            </strong>
                                            {opt.text}
                                          </li>
                                        ))}
                                      </ul>
                                    )}
                                    {item.explanation && (
                                      <div className="rounded-md bg-surface-secondary p-3 text-xs leading-relaxed text-foreground">
                                        <strong>Explanation:</strong> {item.explanation}
                                      </div>
                                    )}
                                  </div>
                                )}
                              </Table.Cell>

                              <Table.Cell className="align-top text-sm">
                                {item.scopeName || item.sectionName || item.topicName || '—'}
                              </Table.Cell>

                              <Table.Cell className="align-top">
                                <Chip
                                  size="sm"
                                  variant="soft"
                                  color={item.source === 'incorrect' ? 'danger' : 'accent'}
                                >
                                  <Chip.Label>{formatSavedSourceLabel(item.source)}</Chip.Label>
                                </Chip>
                              </Table.Cell>

                              <Table.Cell className="align-top">
                                <div className="flex flex-wrap items-center gap-1.5">
                                  <Button
                                    size="sm"
                                    variant="secondary"
                                    onPress={() => toggleExpand(item.savedQuestionId)}
                                  >
                                    {isExpanded ? (
                                      <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" />
                                    ) : (
                                      <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
                                    )}
                                    <span>
                                      {isExpanded ? 'Hide explanation' : 'Show explanation'}
                                    </span>
                                  </Button>

                                  <SavedNotePopover
                                    item={item}
                                    onChanged={() => setNoteTick((t) => t + 1)}
                                  />

                                  <Link
                                    to="/saved-questions/$savedQuestionId"
                                    params={{ savedQuestionId: String(item.savedQuestionId) }}
                                    className="no-underline"
                                  >
                                    <Button size="sm" variant="ghost" aria-label="Full review">
                                      <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                                    </Button>
                                  </Link>

                                  <Button
                                    size="sm"
                                    variant="danger-soft"
                                    isDisabled={removeMutation.isPending}
                                    onPress={() => removeMutation.mutate(item)}
                                  >
                                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                                    <span>Remove</span>
                                  </Button>
                                </div>
                              </Table.Cell>
                            </Table.Row>
                          );
                        })}
                      </Table.Body>
                    </Table.Content>
                  </Table.ScrollContainer>
                </Table>
              </Card>

              <Pagination>
                <Pagination.Summary className="font-mono-tabular">
                  Showing {(safePage - 1) * ITEMS_PER_PAGE + 1}-
                  {Math.min(safePage * ITEMS_PER_PAGE, allItems.length)} of {allItems.length} saved
                </Pagination.Summary>
                <Pagination.Content>
                  <Pagination.Item>
                    <Pagination.Previous
                      isDisabled={safePage <= 1}
                      onPress={() => setPage((p) => Math.max(1, p - 1))}
                    >
                      <Pagination.PreviousIcon />
                      <span>Previous</span>
                    </Pagination.Previous>
                  </Pagination.Item>
                  {pageNumbers.map((num) => (
                    <Pagination.Item key={num}>
                      <Pagination.Link
                        isActive={num === safePage}
                        onPress={() => setPage(num)}
                      >
                        {num}
                      </Pagination.Link>
                    </Pagination.Item>
                  ))}
                  <Pagination.Item>
                    <Pagination.Next
                      isDisabled={safePage >= totalPages}
                      onPress={() => setPage((p) => Math.min(totalPages, p + 1))}
                    >
                      <span>Next</span>
                      <Pagination.NextIcon />
                    </Pagination.Next>
                  </Pagination.Item>
                </Pagination.Content>
              </Pagination>
            </div>
          )}
        </>
      )}
    </div>
  );
}
