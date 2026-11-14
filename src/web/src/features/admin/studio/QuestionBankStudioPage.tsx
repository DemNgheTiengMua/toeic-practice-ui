import React, { useState, useEffect } from 'react';
import {
  Button,
  Card,
  Chip,
  Tabs,
  Modal,
  Table,
  Alert,
} from '@heroui/react';
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
  StimulusDraftAnalysisDto,
  ProductiveTaskAnalysisDto,
  IngestionBatchSummaryDto,
  IngestionBatchDetailDto,
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

  // Stimulus Studio State
  const [stimulusSkill, setStimulusSkill] = useState('reading');
  const [stimulusLevel, setStimulusLevel] = useState('B1');
  const [stimulusItemType, setStimulusItemType] = useState('mcq');
  const [stimulusPassage, setStimulusPassage] = useState('Sample reading passage discussing organizational efficiency...');
  const [stimulusAnalysis, setStimulusAnalysis] = useState<StimulusDraftAnalysisDto | null>(null);
  const [isAnalysingStimulus, setIsAnalysingStimulus] = useState(false);
  const [isSavingStimulus, setIsSavingStimulus] = useState(false);
  const [stimulusNotice, setStimulusNotice] = useState<string | null>(null);

  // Productive Studio State
  const [productiveSkill, setProductiveSkill] = useState('writing');
  const [productiveLevel, setProductiveLevel] = useState('B1');
  const [productiveTopic, setProductiveTopic] = useState('W-ARG');
  const [productivePrompt, setProductivePrompt] = useState('Write an essay examining the advantages of hybrid working models.');
  const [productiveAnalysis, setProductiveAnalysis] = useState<ProductiveTaskAnalysisDto | null>(null);
  const [isAnalysingProductive, setIsAnalysingProductive] = useState(false);
  const [isSavingProductive, setIsSavingProductive] = useState(false);
  const [productiveNotice, setProductiveNotice] = useState<string | null>(null);

  // Ingestion Workbench State
  const [batches, setBatches] = useState<IngestionBatchSummaryDto[]>([]);
  const [isLoadingBatches, setIsLoadingBatches] = useState(false);
  const [selectedBatchId, setSelectedBatchId] = useState<number | null>(null);
  const [batchDetail, setBatchDetail] = useState<IngestionBatchDetailDto | null>(null);
  const [isLoadingBatchDetail, setIsLoadingBatchDetail] = useState(false);
  const [workbenchRuleFilter, setWorkbenchRuleFilter] = useState('');
  const [workbenchNotice, setWorkbenchNotice] = useState<string | null>(null);

  // Micro-Lessons State
  const [lessons, setLessons] = useState<AdminLessonSummaryDto[]>([]);
  const [isLoadingLessons, setIsLoadingLessons] = useState(false);

  // Load Coverage on mount
  useEffect(() => {
    if (!isCatalogManager) return;
    loadCoverage();
    loadBatches();
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

  const loadBatches = async () => {
    setIsLoadingBatches(true);
    try {
      const data = await studioApi.getIngestionBatches();
      setBatches(data.batches);
    } catch {
      // Fallback
    } finally {
      setIsLoadingBatches(false);
    }
  };

  const loadBatchDetail = async (batchId: number, rule?: string) => {
    setIsLoadingBatchDetail(true);
    setWorkbenchNotice(null);
    try {
      const data = await studioApi.getIngestionBatch(batchId, rule);
      setBatchDetail(data);
    } catch {
      // Fallback
    } finally {
      setIsLoadingBatchDetail(false);
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
    if (!newItemStem.trim()) {
      setItemSaveError('Question stem is required.');
      return;
    }
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
    } catch (err: any) {
      setItemSaveError(err?.detail || err?.title || 'Failed to create pool item.');
    } finally {
      setIsSavingItem(false);
    }
  };

  // Stimulus Studio Handlers
  const handleAnalyseStimulus = async () => {
    setIsAnalysingStimulus(true);
    setStimulusNotice(null);
    try {
      const res = await studioApi.analyseStimulus({
        modality: stimulusSkill,
        stimulusLevel,
        itemType: stimulusItemType,
        passage: stimulusPassage,
        children: [
          {
            position: 1,
            cefrLevel: stimulusLevel,
            topicCode: 'R-GIST',
            stem: 'What is the main topic of the passage?',
            optionA: 'Organizational efficiency models',
            optionB: 'Employee benefit structures',
            optionC: 'Annual corporate taxation',
            optionD: 'Office hardware procurement',
            keyLabel: 'A',
            explanation: 'The opening paragraph clearly establishes organizational models.',
            rationaleA: 'Directly supported by paragraph 1 thesis.',
            rationaleB: 'Discussed only in minor detail.',
            rationaleC: 'Not mentioned in text.',
            rationaleD: 'Unrelated topic.',
          },
        ],
      });
      setStimulusAnalysis(res);
    } catch (err: any) {
      setStimulusNotice(err?.detail || err?.title || 'Analysis failed.');
    } finally {
      setIsAnalysingStimulus(false);
    }
  };

  const handleSaveStimulus = async () => {
    setIsSavingStimulus(true);
    setStimulusNotice(null);
    try {
      await studioApi.saveStimulus({
        modality: stimulusSkill,
        stimulusLevel,
        itemType: stimulusItemType,
        passage: stimulusPassage,
        children: [
          {
            position: 1,
            cefrLevel: stimulusLevel,
            topicCode: 'R-GIST',
            stem: 'What is the main topic of the passage?',
            optionA: 'Organizational efficiency models',
            optionB: 'Employee benefit structures',
            keyLabel: 'A',
            explanation: 'Verified explanation.',
          },
        ],
      });
      setStimulusNotice('Stimulus bundle saved successfully to the pool stock!');
      loadCoverage();
    } catch (err: any) {
      setStimulusNotice(err?.detail || err?.title || 'Save failed.');
    } finally {
      setIsSavingStimulus(false);
    }
  };

  // Productive Studio Handlers
  const handleAnalyseProductive = async () => {
    setIsAnalysingProductive(true);
    setProductiveNotice(null);
    try {
      const res = await studioApi.analyseProductive({
        modality: productiveSkill,
        level: productiveLevel,
        topicCode: productiveTopic,
        promptCopy: productivePrompt,
        subTasks: [
          {
            itemType: productiveTopic,
            prompt: productivePrompt,
            minWords: 250,
            maxWords: 400,
            prepSeconds: 30,
            responseSeconds: 1800,
            explanation: 'Benchmark essay should provide two distinct arguments.',
          },
        ],
      });
      setProductiveAnalysis(res);
    } catch (err: any) {
      setProductiveNotice(err?.detail || err?.title || 'Analysis failed.');
    } finally {
      setIsAnalysingProductive(false);
    }
  };

  const handleSaveProductive = async () => {
    setIsSavingProductive(true);
    setProductiveNotice(null);
    try {
      await studioApi.saveProductive({
        modality: productiveSkill,
        level: productiveLevel,
        topicCode: productiveTopic,
        promptCopy: productivePrompt,
        subTasks: [
          {
            itemType: productiveTopic,
            prompt: productivePrompt,
            minWords: 250,
            maxWords: 400,
            prepSeconds: 30,
            responseSeconds: 1800,
            explanation: 'Benchmark essay should provide two distinct arguments.',
          },
        ],
      });
      setProductiveNotice('Productive task bundle saved successfully to the pool stock!');
      loadCoverage();
    } catch (err: any) {
      setProductiveNotice(err?.detail || err?.title || 'Save failed.');
    } finally {
      setIsSavingProductive(false);
    }
  };

  // Workbench Actions
  const handleClearWorkbenchItem = async (batchId: number, itemId: number) => {
    if (!canReviewContent) {
      setWorkbenchNotice('Staff capability pool.content.review is required to clear items.');
      return;
    }
    try {
      await studioApi.clearWorkbenchItem(batchId, itemId);
      setWorkbenchNotice(`Item ${itemId} cleared successfully.`);
      loadBatchDetail(batchId, workbenchRuleFilter || undefined);
    } catch (err: any) {
      setWorkbenchNotice(err?.detail || err?.title || 'Clear failed.');
    }
  };

  const handleDiscardRecord = async (batchId: number, line: number) => {
    try {
      await studioApi.discardWorkbenchRecord(batchId, line, 'Author withdrawal from workbench');
      setWorkbenchNotice(`Record on line ${line} discarded.`);
      loadBatchDetail(batchId, workbenchRuleFilter || undefined);
    } catch (err: any) {
      setWorkbenchNotice(err?.detail || err?.title || 'Discard failed.');
    }
  };

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
            Author and inspect warehouse coverage stock, stimulus bundles, productive tasks, ingestion batches, and micro-lessons.
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
                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Modality</label>
                  <select
                    value={newItemModality}
                    onChange={(e) => setNewItemModality(e.target.value)}
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                  >
                    <option value="reading">Reading</option>
                    <option value="listening">Listening</option>
                    <option value="speaking">Speaking</option>
                    <option value="writing">Writing</option>
                  </select>
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">CEFR level</label>
                  <select
                    value={newItemLevel}
                    onChange={(e) => setNewItemLevel(e.target.value)}
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground font-mono text-sm"
                  >
                    <option value="A1">A1</option>
                    <option value="A2">A2</option>
                    <option value="B1">B1</option>
                    <option value="B2">B2</option>
                    <option value="C1">C1</option>
                    <option value="C2">C2</option>
                  </select>
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Item type</label>
                  <select
                    value={newItemType}
                    onChange={(e) => setNewItemType(e.target.value)}
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                  >
                    <option value="mcq">Multiple choice (4 options)</option>
                    <option value="mcq_3">3-option choice</option>
                    <option value="multi_select">Multiple select</option>
                    <option value="inline_completion">Inline completion</option>
                    <option value="dropdown">Dropdown select</option>
                    <option value="drag_drop">Drag and drop</option>
                  </select>
                </div>
              </div>

              <div className="space-y-1">
                <label className="text-xs font-semibold text-foreground">Question stem</label>
                <textarea
                  rows={2}
                  value={newItemStem}
                  onChange={(e) => setNewItemStem(e.target.value)}
                  placeholder="Enter clear, concise question prompt..."
                  className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Option (A)</label>
                  <input
                    type="text"
                    value={newItemOptionA}
                    onChange={(e) => setNewItemOptionA(e.target.value)}
                    className="w-full px-3 py-1.5 rounded-md border border-border bg-surface text-foreground text-sm"
                  />
                  <input
                    type="text"
                    placeholder="Rationale for option A..."
                    value={newItemRationaleA}
                    onChange={(e) => setNewItemRationaleA(e.target.value)}
                    className="w-full px-3 py-1 rounded-md border border-border bg-surface-secondary text-muted text-xs"
                  />
                </div>
                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Option (B)</label>
                  <input
                    type="text"
                    value={newItemOptionB}
                    onChange={(e) => setNewItemOptionB(e.target.value)}
                    className="w-full px-3 py-1.5 rounded-md border border-border bg-surface text-foreground text-sm"
                  />
                  <input
                    type="text"
                    placeholder="Rationale for option B..."
                    value={newItemRationaleB}
                    onChange={(e) => setNewItemRationaleB(e.target.value)}
                    className="w-full px-3 py-1 rounded-md border border-border bg-surface-secondary text-muted text-xs"
                  />
                </div>
                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Option (C)</label>
                  <input
                    type="text"
                    value={newItemOptionC}
                    onChange={(e) => setNewItemOptionC(e.target.value)}
                    className="w-full px-3 py-1.5 rounded-md border border-border bg-surface text-foreground text-sm"
                  />
                  <input
                    type="text"
                    placeholder="Rationale for option C..."
                    value={newItemRationaleC}
                    onChange={(e) => setNewItemRationaleC(e.target.value)}
                    className="w-full px-3 py-1 rounded-md border border-border bg-surface-secondary text-muted text-xs"
                  />
                </div>
                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Option (D)</label>
                  <input
                    type="text"
                    value={newItemOptionD}
                    onChange={(e) => setNewItemOptionD(e.target.value)}
                    className="w-full px-3 py-1.5 rounded-md border border-border bg-surface text-foreground text-sm"
                  />
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
                  <label className="text-xs font-semibold text-foreground">Correct key</label>
                  <select
                    value={newItemKey}
                    onChange={(e) => setNewItemKey(e.target.value)}
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground font-mono font-bold text-sm"
                  >
                    <option value="A">A</option>
                    <option value="B">B</option>
                    <option value="C">C</option>
                    <option value="D">D</option>
                  </select>
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

              <div className="space-y-1">
                <label className="text-xs font-semibold text-foreground">Explanation</label>
                <textarea
                  rows={2}
                  value={newItemExplanation}
                  onChange={(e) => setNewItemExplanation(e.target.value)}
                  placeholder="Why the key is correct..."
                  className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-xs"
                />
              </div>
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
