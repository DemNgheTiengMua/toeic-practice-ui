import React, { useMemo, useState } from 'react';
import { Link, useLocation, useNavigate } from '@tanstack/react-router';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Label,
  ListBox,
  Select,
  Spinner,
  Table,
} from '@heroui/react';
import {
  ArrowRight,
  Bookmark,
  Clock,
  Compass,
  Headphones,
  Lock,
  Mic,
  Play,
  Sparkles,
  Target,
} from 'lucide-react';
import { useAuth } from '@/context/AuthContext';
import {
  api,
  PracticeCatalogDto,
  PracticeCellDto,
  PracticeSectionDto,
} from '@/lib/api';
import {
  buildStartSessionPayload,
  CanonicalMasteryState,
  formatLocalReopenTime,
  formatModalityLabel,
  getPracticeErrorMessage,
  normalizeMasteryState,
} from './practiceUtils';

function masteryChipColor(state: CanonicalMasteryState): 'default' | 'accent' | 'warning' | 'success' {
  switch (state) {
    case 'Mastered':
      return 'success';
    case 'Reviewing':
      return 'warning';
    case 'Learning':
      return 'accent';
    default:
      return 'default';
  }
}

function SectionCard({
  section,
  isStarting,
  onStart,
}: {
  section: PracticeSectionDto;
  isStarting: boolean;
  onStart: (sectionId: number) => void;
}) {
  const reopenText = formatLocalReopenTime(section.nextReviewDueAt);
  const canStart = section.availableQuestionCount > 0;

  return (
    <Card className="border border-border bg-surface">
      <Card.Header className="flex flex-row items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <Chip size="sm" variant="soft" color="accent">
              <Chip.Label className="font-mono-tabular">{section.code}</Chip.Label>
            </Chip>
            <Chip size="sm" variant="soft">
              <Chip.Label>{section.skillName}</Chip.Label>
            </Chip>
          </div>
          <Card.Title className="text-base">{section.name}</Card.Title>
          <Card.Description>
            {section.availableQuestionCount} available items · typical draw {section.expectedQuestionCount}
          </Card.Description>
        </div>
        {section.accuracyPercentage != null && (
          <div className="text-right">
            <div className="text-xs text-muted">Accuracy</div>
            <div className="font-mono-tabular text-lg font-semibold text-foreground">
              {section.accuracyPercentage}%
            </div>
          </div>
        )}
      </Card.Header>
      <Card.Content className="space-y-3 pt-0">
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted">
          <span className="font-mono-tabular">Answered {section.answeredCount} items</span>
          {section.categoriesInCooldown > 0 && (
            <span className="text-warning-foreground">
              {section.categoriesInCooldown} of {section.totalCategoryCount} categories resting
              {reopenText ? ` · reopens ${reopenText}` : ''}
            </span>
          )}
        </div>
      </Card.Content>
      <Card.Footer className="flex items-center justify-between gap-2 border-t border-separator pt-3">
        <span className="font-mono-tabular text-xs font-medium text-success">0 credits</span>
        <Button
          size="sm"
          variant="primary"
          isDisabled={!canStart || isStarting}
          onPress={() => onStart(section.sectionId)}
        >
          <Play className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Start section drill</span>
        </Button>
      </Card.Footer>
    </Card>
  );
}

export function PracticeBuilderPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { learnerContext, switchCertificate } = useAuth();

  const stepId = useMemo(() => {
    const searchObj = location.search as Record<string, unknown> | undefined;
    const rawFromObj = searchObj?.stepId;
    const rawFromStr = new URLSearchParams(location.searchStr || '').get('stepId');
    const raw = rawFromObj ?? rawFromStr;
    const parsed = raw != null ? Number(raw) : NaN;
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }, [location.search, location.searchStr]);

  const [skillFilter, setSkillFilter] = useState<string>('all');
  const [matrixModalityFilter, setMatrixModalityFilter] = useState<string>('all');
  const [selectedCellKey, setSelectedCellKey] = useState<string>('');
  const [actionError, setActionError] = useState<string | null>(null);

  const activeCertCode = learnerContext?.active?.code ?? 'default';

  const stepQuery = useQuery({
    queryKey: ['learning-path', 'step', stepId],
    queryFn: () => api.learningPath.step(stepId!),
    enabled: stepId != null,
  });

  const catalogQuery = useQuery({
    queryKey: ['practice', 'catalog', activeCertCode],
    queryFn: () => api.practice.catalog(),
  });

  const matrixQuery = useQuery({
    queryKey: ['practice', 'matrix', activeCertCode],
    queryFn: () => api.practice.matrix(),
    retry: false,
  });

  const freeDrillQuery = useQuery({
    queryKey: ['practice', 'free-drill', activeCertCode],
    queryFn: () => api.practice.freeDrill(),
    retry: false,
  });

  const startSessionMutation = useMutation({
    mutationFn: (payload: { sectionId?: number; topicId?: number; cefrLevelCode?: string }) =>
      api.practice.startSession(payload),
    onSuccess: (session) => {
      setActionError(null);
      navigate({
        to: '/practice/session/$sessionId',
        params: { sessionId: String(session.sessionId) },
      });
    },
    onError: (err) => {
      setActionError(getPracticeErrorMessage(err, 'Unable to start practice session.'));
    },
  });

  const startStepMutation = useMutation({
    mutationFn: (id: number) => api.learningPath.startStep(id),
    onSuccess: (result) => {
      setActionError(null);
      if (result.sessionId != null) {
        navigate({
          to: '/practice/session/$sessionId',
          params: { sessionId: String(result.sessionId) },
        });
        return;
      }
      if (result.redirectUrl) {
        navigate({ to: result.redirectUrl as any });
      }
    },
    onError: (err) => {
      setActionError(getPracticeErrorMessage(err, 'Unable to start learning path step.'));
    },
  });

  const catalog: PracticeCatalogDto | undefined = catalogQuery.data;
  const cells: PracticeCellDto[] = matrixQuery.data ?? [];
  const freeDrill = freeDrillQuery.data;

  const allSections = useMemo(() => {
    if (!catalog) return [];
    return [
      ...catalog.listeningSections,
      ...catalog.readingSections,
      ...catalog.otherSections,
    ];
  }, [catalog]);

  const skillOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const s of allSections) {
      map.set(s.skillCode, s.skillName);
    }
    return [{ id: 'all', name: 'All skills' }, ...Array.from(map.entries()).map(([id, name]) => ({ id, name }))];
  }, [allSections]);

  const filteredSections = useMemo(() => {
    if (skillFilter === 'all') return allSections;
    return allSections.filter((s) => s.skillCode === skillFilter);
  }, [allSections, skillFilter]);

  const filteredCells = useMemo(() => {
    if (matrixModalityFilter === 'all') return cells;
    return cells.filter((c) => String(c.modality).toLowerCase() === matrixModalityFilter);
  }, [cells, matrixModalityFilter]);

  const openCells = useMemo(
    () => filteredCells.filter((c) => !(c.isResting && c.reopensAt != null)),
    [filteredCells],
  );

  const selectedCell = useMemo(() => {
    if (!selectedCellKey) return null;
    return openCells.find((c) => `${c.topicId}:${c.levelCode}` === selectedCellKey) ?? null;
  }, [openCells, selectedCellKey]);

  const handleStartSection = (sectionId: number) => {
    setActionError(null);
    startSessionMutation.mutate(buildStartSessionPayload({ sectionId }));
  };

  const handleStartCell = (cell: PracticeCellDto) => {
    setActionError(null);
    startSessionMutation.mutate(
      buildStartSessionPayload({ topicId: cell.topicId, cefrLevelCode: cell.levelCode }),
    );
  };

  const availableCertificates = learnerContext?.certificates ?? learnerContext?.available ?? [];

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-separator pb-4">
        <div className="space-y-1.5">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="font-display text-2xl font-bold tracking-tight text-foreground">
              Free practice drills
            </h1>
            <Chip color="success" variant="soft" size="sm">
              <Chip.Label className="font-mono-tabular">0 credits</Chip.Label>
            </Chip>
            {catalog?.certificate && (
              <Chip variant="soft" size="sm">
                <Chip.Label>{catalog.certificate.name}</Chip.Label>
              </Chip>
            )}
          </div>
          <p className="text-sm text-muted">
            Drill exam sections or targeted Topic × CEFR cells at no credit cost. Answers are saved as
            free diagnostic practice.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          {learnerContext && availableCertificates.length > 1 && (
            <Select
              aria-label="Active certificate"
              className="w-56"
              value={learnerContext.active.code}
              onChange={(key) => {
                if (key != null && String(key) !== learnerContext.active.code) {
                  setActionError(null);
                  void switchCertificate(String(key)).catch((err) => {
                    setActionError(getPracticeErrorMessage(err, 'Unable to switch certificate.'));
                  });
                }
              }}
            >
              <Label className="sr-only">Active certificate</Label>
              <Select.Trigger>
                <Select.Value />
                <Select.Indicator />
              </Select.Trigger>
              <Select.Popover>
                <ListBox>
                  {availableCertificates.map((cert) => (
                    <ListBox.Item key={cert.code} id={cert.code} textValue={cert.name}>
                      {cert.name}
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                  ))}
                </ListBox>
              </Select.Popover>
            </Select>
          )}
          <Link to="/saved-questions" className="no-underline">
            <Button variant="secondary" size="sm">
              <Bookmark className="h-4 w-4" aria-hidden="true" />
              <span>Saved questions</span>
            </Button>
          </Link>
        </div>
      </header>

      {actionError && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to start drill</Alert.Title>
            <Alert.Description>{actionError}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {stepId != null && (
        <Card className="border border-accent/40 bg-surface">
          <Card.Header className="flex flex-row items-start justify-between gap-4">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <Chip color="accent" size="sm">
                  <Chip.Label>Learning path launch</Chip.Label>
                </Chip>
                <Chip color="success" variant="soft" size="sm">
                  <Chip.Label className="font-mono-tabular">0 credits</Chip.Label>
                </Chip>
              </div>
              <Card.Title className="text-lg">
                {stepQuery.data ? stepQuery.data.title : `Loading learning path step #${stepId}…`}
              </Card.Title>
              {stepQuery.data && (
                <Card.Description>
                  Week {stepQuery.data.weekNumber} · {stepQuery.data.skillName} · {stepQuery.data.topicName}{' '}
                  ({stepQuery.data.targetCefr}) ·{' '}
                  <span className="font-mono-tabular">
                    {stepQuery.data.plannedItemCount ?? 10} items · ~{stepQuery.data.estimatedMinutes ?? 10} min
                  </span>
                </Card.Description>
              )}
            </div>
            {stepQuery.data && (
              <Button
                variant="primary"
                isDisabled={!stepQuery.data.canStart || startStepMutation.isPending}
                onPress={() => startStepMutation.mutate(stepQuery.data!.stepId)}
              >
                <Sparkles className="h-4 w-4" aria-hidden="true" />
                <span>{startStepMutation.isPending ? 'Launching…' : 'Launch step drill'}</span>
              </Button>
            )}
          </Card.Header>
          {stepQuery.isError && (
            <Card.Content>
              <Alert status="danger">
                <Alert.Indicator />
                <Alert.Content>
                  <Alert.Title>Step unavailable</Alert.Title>
                  <Alert.Description>
                    {getPracticeErrorMessage(stepQuery.error, 'Could not load learning path step.')}
                  </Alert.Description>
                </Alert.Content>
              </Alert>
            </Card.Content>
          )}
        </Card>
      )}

      {catalog?.unfinishedSession && (
        <Alert status="warning">
          <Alert.Indicator />
          <Alert.Content className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <Alert.Title>Unfinished practice session</Alert.Title>
              <Alert.Description>
                {catalog.unfinishedSession.sectionName} ({catalog.unfinishedSession.questionCount} questions) is still
                in progress.
              </Alert.Description>
            </div>
            <Link
              to="/practice/session/$sessionId"
              params={{ sessionId: String(catalog.unfinishedSession.sessionId) }}
              className="no-underline"
            >
              <Button size="sm" variant="primary">
                <span>Resume session</span>
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </Link>
          </Alert.Content>
        </Alert>
      )}

      {freeDrill && !freeDrill.isUnlocked && (
        <Card className="border border-warning/50 bg-surface">
          <Card.Header>
            <div className="flex items-center gap-2">
              <Lock className="h-4 w-4 text-warning" aria-hidden="true" />
              <Card.Title className="text-base">Topic × CEFR free drill locked</Card.Title>
            </div>
            <Card.Description>
              {freeDrill.lockNotice ||
                `Complete ${freeDrill.remainingMinutes} min of planned learning path steps to unlock free topic drilling.`}
            </Card.Description>
          </Card.Header>
          {freeDrill.incompleteSteps.length > 0 && (
            <Card.Content className="space-y-2 pt-0">
              <ul className="space-y-1.5 text-sm">
                {freeDrill.incompleteSteps.map((s) => (
                  <li
                    key={s.stepId}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-surface-secondary px-3 py-2"
                  >
                    <span>
                      <strong className="font-medium">Week {s.weekNumber}:</strong> {s.title}{' '}
                      <span className="text-xs text-muted">({s.topicCode})</span>
                    </span>
                    <span className="font-mono-tabular text-xs text-muted">
                      {s.estimatedMinutes} min
                    </span>
                  </li>
                ))}
              </ul>
            </Card.Content>
          )}
        </Card>
      )}

      {freeDrill?.dailyCeilingReached && (
        <Alert status="warning">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Daily free-drill ceiling reached</Alert.Title>
            <Alert.Description>
              You have completed{' '}
              <span className="font-mono-tabular">
                {freeDrill.answeredToday}/{freeDrill.dailyItemCeiling}
              </span>{' '}
              free-drill items today. Section drills and guided path steps remain available.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {catalogQuery.isLoading ? (
        <Card className="p-8">
          <div className="flex items-center justify-center gap-3 text-sm text-muted">
            <Spinner size="sm" />
            <span>Loading practice catalog…</span>
          </div>
        </Card>
      ) : catalogQuery.isError ? (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Failed to load practice catalog</Alert.Title>
            <Alert.Description>{getPracticeErrorMessage(catalogQuery.error)}</Alert.Description>
          </Alert.Content>
        </Alert>
      ) : (
        <>
          <section className="space-y-4">
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <h2 className="font-display text-lg font-semibold text-foreground">
                  Section drills
                </h2>
                <p className="text-sm text-muted">
                  Practice one exam section at a time (up to 10 items per session, mastery-balanced).
                </p>
              </div>
              <Select
                aria-label="Filter sections by skill"
                className="w-48"
                value={skillFilter}
                onChange={(key) => setSkillFilter(key != null ? String(key) : 'all')}
              >
                <Label>Skill</Label>
                <Select.Trigger>
                  <Select.Value />
                  <Select.Indicator />
                </Select.Trigger>
                <Select.Popover>
                  <ListBox>
                    {skillOptions.map((opt) => (
                      <ListBox.Item key={opt.id} id={opt.id} textValue={opt.name}>
                        {opt.name}
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                    ))}
                  </ListBox>
                </Select.Popover>
              </Select>
            </div>

            {catalog?.weakestSection && (
              <Card className="border border-accent/40 bg-surface-secondary">
                <Card.Content className="flex flex-wrap items-center justify-between gap-3 py-3">
                  <div className="flex items-center gap-3">
                    <Target className="h-5 w-5 text-accent" aria-hidden="true" />
                    <div>
                      <div className="text-xs font-medium text-accent">Recommended focus</div>
                      <div className="text-sm font-semibold text-foreground">
                        {catalog.weakestSection.name}{' '}
                        {catalog.weakestSection.accuracyPercentage != null && (
                          <span className="font-mono-tabular text-muted">
                            ({catalog.weakestSection.accuracyPercentage}% accuracy)
                          </span>
                        )}
                      </div>
                    </div>
                  </div>
                  <Button
                    size="sm"
                    variant="primary"
                    isDisabled={
                      catalog.weakestSection.availableQuestionCount === 0 ||
                      startSessionMutation.isPending
                    }
                    onPress={() => handleStartSection(catalog.weakestSection!.sectionId)}
                  >
                    <Play className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Drill weakest section</span>
                  </Button>
                </Card.Content>
              </Card>
            )}

            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {filteredSections.map((section) => (
                <SectionCard
                  key={section.sectionId}
                  section={section}
                  isStarting={startSessionMutation.isPending}
                  onStart={handleStartSection}
                />
              ))}
            </div>
          </section>

          <section className="space-y-4">
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <h2 className="font-display text-lg font-semibold text-foreground">
                  Topic × CEFR mastery matrix
                </h2>
                <p className="text-sm text-muted">
                  Choose any open topic and CEFR level cell for a focused pool drill.
                </p>
              </div>
              <div className="flex flex-wrap items-end gap-3">
                <Select
                  aria-label="Filter matrix by modality"
                  className="w-44"
                  value={matrixModalityFilter}
                  onChange={(key) => setMatrixModalityFilter(key != null ? String(key) : 'all')}
                >
                  <Label>Modality</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      <ListBox.Item id="all" textValue="All modalities">
                        All modalities
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="listening" textValue="Listening">
                        Listening
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="reading" textValue="Reading">
                        Reading
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                    </ListBox>
                  </Select.Popover>
                </Select>

                {openCells.length > 0 && (
                  <div className="flex items-end gap-2">
                    <Select
                      aria-label="Quick topic cell picker"
                      placeholder="Choose topic & level"
                      className="w-60"
                      value={selectedCellKey || undefined}
                      onChange={(key) => setSelectedCellKey(key != null ? String(key) : '')}
                    >
                      <Label>Quick cell launch</Label>
                      <Select.Trigger>
                        <Select.Value />
                        <Select.Indicator />
                      </Select.Trigger>
                      <Select.Popover>
                        <ListBox>
                          {openCells.map((cell) => {
                            const id = `${cell.topicId}:${cell.levelCode}`;
                            const label = `${cell.topicName} (${cell.levelCode})`;
                            return (
                              <ListBox.Item key={id} id={id} textValue={label}>
                                {label}
                                <ListBox.ItemIndicator />
                              </ListBox.Item>
                            );
                          })}
                        </ListBox>
                      </Select.Popover>
                    </Select>
                    <Button
                      size="sm"
                      variant="primary"
                      isDisabled={!selectedCell || startSessionMutation.isPending}
                      onPress={() => selectedCell && handleStartCell(selectedCell)}
                    >
                      <span>Start cell</span>
                    </Button>
                  </div>
                )}
              </div>
            </div>

            {matrixQuery.isError || freeDrillQuery.isError ? (
              <Alert status="warning">
                <Alert.Indicator />
                <Alert.Content>
                  <Alert.Title>Topic matrix is currently unavailable</Alert.Title>
                  <Alert.Description>
                    {getPracticeErrorMessage(
                      matrixQuery.error ?? freeDrillQuery.error,
                      'Topic matrix could not be loaded. You can still start section drills above.',
                    )}
                  </Alert.Description>
                </Alert.Content>
              </Alert>
            ) : matrixQuery.isLoading ? (
              <Card className="p-6">
                <div className="flex items-center gap-2 text-sm text-muted">
                  <Spinner size="sm" />
                  <span>Loading topic mastery matrix…</span>
                </div>
              </Card>
            ) : filteredCells.length === 0 ? (
              <Card className="p-6 text-sm text-muted">
                No topic × CEFR cells match the selected modality.
              </Card>
            ) : (
              <Card className="border border-border bg-surface">
                <Table>
                  <Table.ScrollContainer>
                    <Table.Content aria-label="Topic mastery practice matrix">
                      <Table.Header>
                        <Table.Column isRowHeader>Topic</Table.Column>
                        <Table.Column>Level</Table.Column>
                        <Table.Column>Modality</Table.Column>
                        <Table.Column>Mastery</Table.Column>
                        <Table.Column>Availability</Table.Column>
                        <Table.Column>Action</Table.Column>
                      </Table.Header>
                      <Table.Body>
                        {filteredCells.map((cell) => {
                          const mastery = normalizeMasteryState(cell.mastery);
                          const resting = Boolean(cell.isResting && cell.reopensAt);
                          const reopenLocal = formatLocalReopenTime(cell.reopensAt);
                          const isCellLocked = Boolean(freeDrill && !freeDrill.isUnlocked) || Boolean(freeDrill?.dailyCeilingReached);

                          return (
                            <Table.Row key={`${cell.topicId}-${cell.levelCode}`}>
                              <Table.Cell>
                                <div className="flex flex-wrap items-center gap-2">
                                  <span className="font-medium text-foreground">{cell.topicName}</span>
                                  {cell.isPrioritised && (
                                    <Chip size="sm" color="accent" variant="soft">
                                      <Chip.Label>Prioritised</Chip.Label>
                                    </Chip>
                                  )}
                                </div>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="font-mono-tabular font-medium">{cell.levelCode}</span>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="inline-flex items-center gap-1 text-muted">
                                  {String(cell.modality).toLowerCase() === 'listening' ? (
                                    <Headphones className="h-3.5 w-3.5" aria-hidden="true" />
                                  ) : (
                                    <Compass className="h-3.5 w-3.5" aria-hidden="true" />
                                  )}
                                  {formatModalityLabel(cell.modality)}
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <Chip size="sm" color={masteryChipColor(mastery)} variant="soft">
                                  <Chip.Label>{mastery}</Chip.Label>
                                </Chip>
                              </Table.Cell>
                              <Table.Cell>
                                {resting && reopenLocal ? (
                                  <span className="inline-flex items-center gap-1 text-xs text-muted">
                                    <Clock className="h-3.5 w-3.5" aria-hidden="true" />
                                    Resting until {reopenLocal}
                                  </span>
                                ) : (
                                  <span className="text-xs text-success">Ready</span>
                                )}
                              </Table.Cell>
                              <Table.Cell>
                                <Button
                                  size="sm"
                                  variant="secondary"
                                  isDisabled={resting || isCellLocked || startSessionMutation.isPending}
                                  onPress={() => handleStartCell(cell)}
                                >
                                  Start cell drill
                                </Button>
                              </Table.Cell>
                            </Table.Row>
                          );
                        })}
                      </Table.Body>
                    </Table.Content>
                  </Table.ScrollContainer>
                </Table>
              </Card>
            )}
          </section>

          {catalog?.speakingSections && catalog.speakingSections.length > 0 && (
            <section className="space-y-3">
              <div>
                <h2 className="font-display text-lg font-semibold text-foreground">
                  Speaking practice
                </h2>
                <p className="text-sm text-muted">
                  Speaking tasks are recorded and graded by AI separately from objective drills.
                </p>
              </div>
              <Card className="border border-border bg-surface">
                <Card.Content className="flex flex-wrap items-center justify-between gap-4 py-4">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2 font-medium text-foreground">
                      <Mic className="h-4 w-4 text-accent" aria-hidden="true" />
                      <span>Available speaking tasks</span>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {catalog.speakingSections.map((s) => (
                        <Chip key={s.sectionId} size="sm" variant="soft">
                          <Chip.Label>{s.name}</Chip.Label>
                        </Chip>
                      ))}
                    </div>
                  </div>
                  <Link to="/speaking" className="no-underline">
                    <Button variant="secondary" size="sm">
                      <span>Open speaking practice</span>
                      <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                    </Button>
                  </Link>
                </Card.Content>
              </Card>
            </section>
          )}
        </>
      )}
    </div>
  );
}
