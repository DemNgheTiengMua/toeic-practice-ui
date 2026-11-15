import React, { useState, useEffect } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Label,
  ListBox,
  Modal,
  Select,
  Table,
  FieldError,
  Form,
  Input,
  Tabs,
  TextArea,
  TextField,
} from '@heroui/react';
import { extractErrorMessage, extractFieldErrors } from './validationErrors';
import {
  Layers,
  Plus,
  Play,
  CheckCircle2,
  AlertTriangle,
  FileText,
  UploadCloud,
  FileCheck,
  ShieldCheck,
  Filter,
  RefreshCw,
  Sliders,
  ExternalLink,
  BookOpen,
  Trash2,
  Lock,
} from 'lucide-react';
import { useAuth } from '../../../context/AuthContext';
import { canManageCatalog, hasCapability, STAFF_PERMISSIONS } from '../../../lib/security';
import { studioApi } from './studioApi';
import {
  formatModalityLabel,
  formatContentTierLabel,
  formatLifecycleStatusLabel,
  formatImportBatchStatusLabel,
} from './studioLabels';
import type {
  AdminCoverageMatrixDto,
  AdminCoverageCellDto,
  AdminLessonSummaryDto,
  CreatePoolItemRequest,
} from './types';

export function QuestionBankStudioPage() {
  const { user } = useAuth();
  const isCatalogManager = canManageCatalog(user);
  const canReviewContent = hasCapability(user, STAFF_PERMISSIONS.ContentReview);

  // Active Tab (2 flat tabs: questions | lessons)
  const [activeTab, setActiveTab] = useState<string>('questions');

  // Coverage State
  const [coverage, setCoverage] = useState<AdminCoverageMatrixDto | null>(null);
  const [isLoadingCoverage, setIsLoadingCoverage] = useState(false);
  const [coverageModalityFilter, setCoverageModalityFilter] = useState<string>('all');

  // Create Pool Item Modal State
  const [isNewItemOpen, setIsNewItemOpen] = useState(false);
  const [newItemModality, setNewItemModality] = useState('reading');
  const [newItemTopicId, setNewItemTopicId] = useState(1);
  const [newItemTargetBands, setNewItemTargetBands] = useState<string[]>([]);
  const [newItemLevel, setNewItemLevel] = useState('B1');
  const [newItemType, setNewItemType] = useState('mcq');
  const [newItemStem, setNewItemStem] = useState('');
  const [newItemOptionA, setNewItemOptionA] = useState('');
  const [newItemOptionB, setNewItemOptionB] = useState('');
  const [newItemOptionC, setNewItemOptionC] = useState('');
  const [newItemOptionD, setNewItemOptionD] = useState('');
  const [newItemKey, setNewItemKey] = useState('A');
  const [newItemExplanation, setNewItemExplanation] = useState('');
  const [newItemRationaleA, setNewItemRationaleA] = useState('');
  const [newItemRationaleB, setNewItemRationaleB] = useState('');
  const [newItemRationaleC, setNewItemRationaleC] = useState('');
  const [newItemRationaleD, setNewItemRationaleD] = useState('');
  const [newItemPlacementEligible, setNewItemPlacementEligible] = useState(false);
  const [isSavingItem, setIsSavingItem] = useState(false);
  const [itemSaveError, setItemSaveError] = useState<string | null>(null);
  const [itemFieldErrors, setItemFieldErrors] = useState<Record<string, string>>({});

  // Micro-Lessons State
  const [lessons, setLessons] = useState<AdminLessonSummaryDto[]>([]);
  const [isLoadingLessons, setIsLoadingLessons] = useState(false);

  // Load Coverage on mount
  useEffect(() => {
    if (!isCatalogManager) return;
    loadCoverage();
    loadLessons();
  }, [isCatalogManager]);

  const loadCoverage = async () => {
    setIsLoadingCoverage(true);
    try {
      const data = await studioApi.getCoverageMatrix();
      setCoverage(data);
    } catch {
      // Fallback
    } finally {
      setIsLoadingCoverage(false);
    }
  };

  const loadLessons = async () => {
    setIsLoadingLessons(true);
    try {
      const data = await studioApi.getLessons();
      setLessons(data);
    } catch {
      // Fallback
    } finally {
      setIsLoadingLessons(false);
    }
  };

  // Create Item Handler
  const handleCreatePoolItem = async () => {
    setItemSaveError(null);
    setItemFieldErrors({});
    setIsSavingItem(true);
    try {
      const payload: CreatePoolItemRequest = {
        modality: newItemModality,
        topicId: newItemTopicId,
        cefrLevelCode: newItemLevel,
        itemType: newItemType,
        stem: newItemStem.trim(),
        optionA: newItemOptionA || null,
        optionB: newItemOptionB || null,
        optionC: newItemOptionC || null,
        optionD: newItemOptionD || null,
        keyLabel: newItemKey,
        explanation: newItemExplanation || null,
        rationaleA: newItemRationaleA || null,
        rationaleB: newItemRationaleB || null,
        rationaleC: newItemRationaleC || null,
        rationaleD: newItemRationaleD || null,
        placementEligible: newItemPlacementEligible,
        targetBands: newItemTargetBands.length === 0 ? 'ALL' : newItemTargetBands.join(','),
      };
      await studioApi.createPoolItem(payload);
      setIsNewItemOpen(false);
      loadCoverage();
    } catch (err: unknown) {
      setItemFieldErrors(extractFieldErrors(err));
      setItemSaveError(extractErrorMessage(err, 'Failed to create pool item.'));
    } finally {
      setIsSavingItem(false);
    }
  };

  // #360: Stimulus Studio and Productive Studio handlers are gone with their routes. Pool authoring
  // is POST /items (below) plus the exam's binding route; nothing else writes stock.

  if (!isCatalogManager) {
    return (
      <div className="p-6 max-w-4xl mx-auto">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Access Denied</Alert.Title>
            <Alert.Description>
              You do not have permission to access the Question Bank Studio. Staff capability{' '}
              <code className="font-mono text-xs">pool.content.author</code> is required.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  const filteredCells = (coverage?.cells ?? []).filter((c) =>
    coverageModalityFilter === 'all' ? true : c.modality === coverageModalityFilter,
  );

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 border-b border-border pb-4">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold font-display tracking-tight text-foreground">
              Question Bank Studio
            </h1>
            <Chip size="sm" variant="soft" color="accent">
              Pool & Stock Warehouse
            </Chip>
          </div>
          <p className="text-sm text-muted mt-1">
            Author and inspect warehouse coverage stock and micro-lessons. Bundle stimuli are authored
            per Section in the Exams studio.
          </p>
        </div>

        <Button size="md" variant="primary" onPress={() => setIsNewItemOpen(true)}>
          <Plus className="w-4 h-4 mr-1.5" />
          New pool item
        </Button>
      </div>

      {/* Tabs */}
      <Tabs selectedKey={activeTab} onSelectionChange={(k) => setActiveTab(String(k))}>
        <Tabs.ListContainer>
          <Tabs.List aria-label="Question Bank Studio tabs">
            <Tabs.Tab id="questions">
              <Tabs.Indicator />
              <span>Question Bank</span>
            </Tabs.Tab>
            <Tabs.Tab id="lessons">
              <Tabs.Indicator />
              <span>Lessons</span>
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>

        {/* Tab 1: Question Bank */}
        <Tabs.Panel id="questions" className="pt-4 space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
            <div>
              <h2 className="text-base font-bold text-foreground">120-Cell Coverage Matrix</h2>
              <p className="text-xs text-muted">
                Authoritative view produced directly by the database warehouse views. No client-side recalculation.
              </p>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold text-muted">Filter:</span>
              {['all', 'reading', 'listening', 'speaking', 'writing'].map((mod) => (
                <Button
                  key={mod}
                  size="sm"
                  variant={coverageModalityFilter === mod ? 'primary' : 'ghost'}
                  onPress={() => setCoverageModalityFilter(mod)}
                >
                  {mod === 'all' ? 'All skills' : formatModalityLabel(mod)}
                </Button>
              ))}
            </div>
          </div>

          <Card>
            <Table>
              <Table.ScrollContainer>
                <Table.Content aria-label="Warehouse coverage matrix table">
                  <Table.Header>
                    <Table.Column isRowHeader>Topic code</Table.Column>
                    <Table.Column>Topic name</Table.Column>
                    <Table.Column>Modality</Table.Column>
                    <Table.Column>CEFR level</Table.Column>
                    <Table.Column>Target count</Table.Column>
                    <Table.Column>Active count</Table.Column>
                    <Table.Column>Placement eligible</Table.Column>
                    <Table.Column>Deficit</Table.Column>
                  </Table.Header>
                  <Table.Body>
                    {filteredCells.map((cell) => (
                      <Table.Row key={`${cell.topicId}-${cell.cefrLevel}`}>
                        <Table.Cell className="font-mono font-bold text-accent">
                          {cell.topicCode}
                        </Table.Cell>
                        <Table.Cell className="font-medium text-foreground">
                          {cell.topicName}
                        </Table.Cell>
                        <Table.Cell>
                          <Chip size="sm" variant="soft" color="default">
                            {formatModalityLabel(cell.modality)}
                          </Chip>
                        </Table.Cell>
                        <Table.Cell className="font-mono-tabular font-bold">
                          {cell.cefrLevel}
                        </Table.Cell>
                        <Table.Cell className="font-mono-tabular">{cell.targetItemCount}</Table.Cell>
                        <Table.Cell className="font-mono-tabular font-medium">
                          {cell.activeItemCount}
                        </Table.Cell>
                        <Table.Cell className="font-mono-tabular">
                          {cell.placementEligibleItemCount}
                        </Table.Cell>
                        <Table.Cell>
                          {cell.deficit > 0 ? (
                            <span className="font-mono-tabular text-warning font-bold">
                              Deficit: {cell.deficit}
                            </span>
                          ) : (
                            <span className="font-mono-tabular text-success font-medium">
                              Full
                            </span>
                          )}
                        </Table.Cell>
                      </Table.Row>
                    ))}
                  </Table.Body>
                </Table.Content>
              </Table.ScrollContainer>
            </Table>
          </Card>
        </Tabs.Panel>

        {/* Tab 5: Micro-Lessons */}
        <Tabs.Panel id="lessons" className="pt-4 space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
            <div>
              <h2 className="text-lg font-bold font-display text-foreground">
                Micro-Lessons Catalog
              </h2>
              <p className="text-xs text-muted">
                Author and organize multi-card guided micro-lessons (Primary & Supplemental modules) linked to topic cells.
              </p>
            </div>

            <div className="flex gap-2">
              <a
                href="/api/admin/lessons/template"
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md border border-border bg-surface text-foreground text-xs font-semibold no-underline hover:bg-surface-secondary"
              >
                Download template
              </a>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {lessons.map((lesson) => (
              <Card key={lesson.lessonId} className="border border-border">
                <Card.Header className="pb-2">
                  <div className="flex justify-between items-center">
                    <span className="font-mono text-xs font-bold text-accent">
                      {lesson.certCode} · {lesson.skillCode}
                    </span>
                    <div className="flex items-center gap-1.5">
                      <Chip size="sm" variant="soft" color="accent">
                        {lesson.targetBands || 'ALL'}
                      </Chip>
                      <Chip size="sm" variant="soft" color="default">
                        {lesson.cefrLevel}
                      </Chip>
                    </div>
                  </div>
                  <Card.Title className="text-base mt-1">{lesson.title}</Card.Title>
                  {lesson.summary && (
                    <Card.Description className="line-clamp-2 text-xs">
                      {lesson.summary}
                    </Card.Description>
                  )}
                </Card.Header>
                <Card.Content className="text-xs space-y-1.5 pt-2 border-t border-border">
                  <div className="flex justify-between text-muted">
                    <span>Topic:</span>
                    <span className="font-mono text-foreground">{lesson.topicCode}</span>
                  </div>
                  <div className="flex justify-between text-muted">
                    <span>Duration:</span>
                    <span className="font-mono-tabular text-foreground">{lesson.estimatedMinutes} mins</span>
                  </div>
                  <div className="flex justify-between text-muted">
                    <span>Cards count:</span>
                    <span className="font-mono-tabular font-medium text-foreground">{lesson.cardCount} cards</span>
                  </div>
                </Card.Content>
              </Card>
            ))}
          </div>
        </Tabs.Panel>
      </Tabs>

      {/* New Pool Item Modal */}
      <Modal.Backdrop isOpen={isNewItemOpen} onOpenChange={setIsNewItemOpen}>
        <Modal.Container>
          <Modal.Dialog className="max-w-2xl max-h-[90vh] overflow-y-auto">
            <Modal.CloseTrigger />
            <Modal.Header>
              <Modal.Heading>Create Pool Item</Modal.Heading>
            </Modal.Header>
            <Modal.Body className="space-y-4">
              {itemSaveError && (
                <Alert status="danger">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Validation Error</Alert.Title>
                    <Alert.Description>{itemSaveError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <Select
                  aria-label="Modality"
                  className="w-full"
                  value={newItemModality}
                  onChange={(key) => {
                    if (key != null) setNewItemModality(String(key));
                  }}
                >
                  <Label className="text-xs font-semibold text-foreground">Modality</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      <ListBox.Item id="reading" textValue="Reading">
                        Reading
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="listening" textValue="Listening">
                        Listening
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="speaking" textValue="Speaking">
                        Speaking
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="writing" textValue="Writing">
                        Writing
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                    </ListBox>
                  </Select.Popover>
                </Select>

                <Select
                  aria-label="CEFR level"
                  className="w-full"
                  value={newItemLevel}
                  onChange={(key) => {
                    if (key != null) setNewItemLevel(String(key));
                  }}
                >
                  <Label className="text-xs font-semibold text-foreground">CEFR level</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      {['A1', 'A2', 'B1', 'B2', 'C1', 'C2'].map((lvl) => (
                        <ListBox.Item key={lvl} id={lvl} textValue={lvl}>
                          {lvl}
                          <ListBox.ItemIndicator />
                        </ListBox.Item>
                      ))}
                    </ListBox>
                  </Select.Popover>
                </Select>

                <Select
                  aria-label="Item type"
                  className="w-full"
                  value={newItemType}
                  onChange={(key) => {
                    if (key != null) setNewItemType(String(key));
                  }}
                >
                  <Label className="text-xs font-semibold text-foreground">Item type</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      <ListBox.Item id="mcq" textValue="Multiple choice (4 options)">
                        Multiple choice (4 options)
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="mcq_3" textValue="3-option choice">
                        3-option choice
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="multi_select" textValue="Multiple select">
                        Multiple select
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="inline_completion" textValue="Inline completion">
                        Inline completion
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="dropdown" textValue="Dropdown select">
                        Dropdown select
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                      <ListBox.Item id="drag_drop" textValue="Drag and drop">
                        Drag and drop
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                    </ListBox>
                  </Select.Popover>
                </Select>
              </div>

              <Form validationBehavior="aria" className="space-y-4">
                <TextField
                  isRequired
                  name="stem"
                  value={newItemStem}
                  onChange={(value) => {
                    setNewItemStem(value);
                    setItemFieldErrors((prev) => ({ ...prev, stem: '' }));
                  }}
                  isInvalid={Boolean(itemFieldErrors.stem)}
                >
                  <Label>Question stem</Label>
                  <TextArea
                    rows={2}
                    placeholder="Enter clear, concise question prompt..."
                    className="text-sm"
                  />
                  {itemFieldErrors.stem && (
                    <FieldError>{itemFieldErrors.stem}</FieldError>
                  )}
                </TextField>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <div className="space-y-1.5">
                    <TextField
                      name="optionA"
                      value={newItemOptionA}
                      onChange={(value) => {
                        setNewItemOptionA(value);
                        setItemFieldErrors((prev) => ({ ...prev, optionA: '' }));
                      }}
                      isInvalid={Boolean(itemFieldErrors.optionA)}
                    >
                      <Label>Option (A)</Label>
                      <Input className="text-sm" />
                      {itemFieldErrors.optionA && (
                        <FieldError>{itemFieldErrors.optionA}</FieldError>
                      )}
                    </TextField>
                    <input
                      type="text"
                      placeholder="Rationale for option A..."
                      value={newItemRationaleA}
                      onChange={(e) => setNewItemRationaleA(e.target.value)}
                      className="w-full px-3 py-1 rounded-md border border-border bg-surface-secondary text-muted text-xs"
                    />
                  </div>
                  <div className="space-y-1.5">
                    <TextField
                      name="optionB"
                      value={newItemOptionB}
                      onChange={(value) => {
                        setNewItemOptionB(value);
                        setItemFieldErrors((prev) => ({ ...prev, optionB: '' }));
                      }}
                      isInvalid={Boolean(itemFieldErrors.optionB)}
                    >
                      <Label>Option (B)</Label>
                      <Input className="text-sm" />
                      {itemFieldErrors.optionB && (
                        <FieldError>{itemFieldErrors.optionB}</FieldError>
                      )}
                    </TextField>
                    <input
                      type="text"
                      placeholder="Rationale for option B..."
                      value={newItemRationaleB}
                      onChange={(e) => setNewItemRationaleB(e.target.value)}
                      className="w-full px-3 py-1 rounded-md border border-border bg-surface-secondary text-muted text-xs"
                    />
                  </div>
                  <div className="space-y-1.5">
                    <TextField
                      name="optionC"
                      value={newItemOptionC}
                      onChange={(value) => {
                        setNewItemOptionC(value);
                        setItemFieldErrors((prev) => ({ ...prev, optionC: '' }));
                      }}
                      isInvalid={Boolean(itemFieldErrors.optionC)}
                    >
                      <Label>Option (C)</Label>
                      <Input className="text-sm" />
                      {itemFieldErrors.optionC && (
                        <FieldError>{itemFieldErrors.optionC}</FieldError>
                      )}
                    </TextField>
                    <input
                      type="text"
                      placeholder="Rationale for option C..."
                      value={newItemRationaleC}
                      onChange={(e) => setNewItemRationaleC(e.target.value)}
                      className="w-full px-3 py-1 rounded-md border border-border bg-surface-secondary text-muted text-xs"
                    />
                  </div>
                  <div className="space-y-1.5">
                    <TextField
                      name="optionD"
                      value={newItemOptionD}
                      onChange={(value) => {
                        setNewItemOptionD(value);
                        setItemFieldErrors((prev) => ({ ...prev, optionD: '' }));
                      }}
                      isInvalid={Boolean(itemFieldErrors.optionD)}
                    >
                      <Label>Option (D)</Label>
                      <Input className="text-sm" />
                      {itemFieldErrors.optionD && (
                        <FieldError>{itemFieldErrors.optionD}</FieldError>
                      )}
                    </TextField>
                    <input
                      type="text"
                      placeholder="Rationale for option D..."
                      value={newItemRationaleD}
                      onChange={(e) => setNewItemRationaleD(e.target.value)}
                      className="w-full px-3 py-1 rounded-md border border-border bg-surface-secondary text-muted text-xs"
                    />
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-1">
                    <Select
                      aria-label="Correct key"
                      className="w-full"
                      value={newItemKey}
                      onChange={(key) => {
                        if (key != null) {
                          setNewItemKey(String(key));
                          setItemFieldErrors((prev) => ({ ...prev, keyLabel: '' }));
                        }
                      }}
                      isInvalid={Boolean(itemFieldErrors.keyLabel)}
                    >
                      <Label className="text-xs font-semibold text-foreground">Correct key</Label>
                      <Select.Trigger>
                        <Select.Value />
                        <Select.Indicator />
                      </Select.Trigger>
                      <Select.Popover>
                        <ListBox>
                          {['A', 'B', 'C', 'D'].map((k) => (
                            <ListBox.Item key={k} id={k} textValue={k}>
                              {k}
                              <ListBox.ItemIndicator />
                            </ListBox.Item>
                          ))}
                        </ListBox>
                      </Select.Popover>
                    </Select>
                    {itemFieldErrors.keyLabel && (
                      <p className="text-xs text-danger">{itemFieldErrors.keyLabel}</p>
                    )}
                  </div>

                  <div className="flex items-center gap-2 pt-6">
                    <input
                      type="checkbox"
                      id="new-placement-eligible"
                      checked={newItemPlacementEligible}
                      onChange={(e) => setNewItemPlacementEligible(e.target.checked)}
                      className="w-4 h-4 rounded border-border"
                    />
                    <label htmlFor="new-placement-eligible" className="text-sm font-medium text-foreground">
                      Placement eligible
                    </label>
                  </div>
                </div>

                <div className="space-y-1.5">
                  <span className="text-xs font-semibold text-foreground block">Target bands</span>
                  <div className="flex flex-wrap items-center gap-3">
                    <label className="inline-flex items-center gap-1.5 text-xs text-foreground">
                      <input
                        type="checkbox"
                        checked={newItemTargetBands.length === 0}
                        onChange={() => setNewItemTargetBands([])}
                        className="w-3.5 h-3.5 rounded border-border"
                      />
                      ALL
                    </label>
                    {['A1', 'A2', 'B1', 'B2', 'C1', 'C2'].map((band) => {
                      const checked = newItemTargetBands.includes(band);
                      return (
                        <label key={band} className="inline-flex items-center gap-1.5 text-xs text-foreground">
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={() =>
                              setNewItemTargetBands((prev) =>
                                checked ? prev.filter((b) => b !== band) : [...prev, band],
                              )
                            }
                            className="w-3.5 h-3.5 rounded border-border"
                          />
                          {band}
                        </label>
                      );
                    })}
                  </div>
                </div>

                <TextField
                  isRequired
                  name="explanation"
                  value={newItemExplanation}
                  onChange={(value) => {
                    setNewItemExplanation(value);
                    setItemFieldErrors((prev) => ({ ...prev, explanation: '' }));
                  }}
                  isInvalid={Boolean(itemFieldErrors.explanation)}
                >
                  <Label>Explanation</Label>
                  <TextArea
                    rows={2}
                    placeholder="Why the key is correct..."
                    className="text-xs"
                  />
                  {itemFieldErrors.explanation && (
                    <FieldError>{itemFieldErrors.explanation}</FieldError>
                  )}
                </TextField>
              </Form>
            </Modal.Body>
            <Modal.Footer>
              <Button size="sm" variant="secondary" onPress={() => setIsNewItemOpen(false)}>
                Cancel
              </Button>
              <Button
                size="sm"
                variant="primary"
                onPress={handleCreatePoolItem}
                isDisabled={isSavingItem}
              >
                Create item
              </Button>
            </Modal.Footer>
          </Modal.Dialog>
        </Modal.Container>
      </Modal.Backdrop>
    </div>
  );
}
