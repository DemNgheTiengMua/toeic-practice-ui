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
  BookOpen,
  Plus,
  Play,
  CheckCircle2,
  AlertTriangle,
  Layers,
  Volume2,
  Sparkles,
  FileCheck,
  UploadCloud,
  Download,
  Trash2,
  ArrowLeft,
  Search,
  ExternalLink,
} from 'lucide-react';
import { useAuth } from '../../../context/AuthContext';
import { canManageCatalog, hasCapability, STAFF_PERMISSIONS } from '../../../lib/security';
import { studioApi } from './studioApi';
import {
  formatExamStatusLabel,
  formatExamPurposeLabel,
  formatModalityLabel,
  formatQuestionTypeLabel,
} from './studioLabels';
import type {
  AdminExamListItemDto,
  AdminExamDetailDto,
  ExamAuditResultDto,
  ExamAssemblyDto,
  QuickIngestParsedQuestionDto,
  CreateAdminExamRequest,
} from './types';

export function ExamsStudioPage() {
  const { user } = useAuth();
  const isCatalogManager = canManageCatalog(user);

  // Exam list state
  const [exams, setExams] = useState<AdminExamListItemDto[]>([]);
  const [isLoadingExams, setIsLoadingExams] = useState(false);
  const [examSearch, setExamSearch] = useState('');
  const [examStatusFilter, setExamStatusFilter] = useState('');
  const [examPurposeFilter, setExamPurposeFilter] = useState('');

  // Selected Exam workspace state
  const [selectedExamId, setSelectedExamId] = useState<number | null>(null);
  const [examDetail, setExamDetail] = useState<AdminExamDetailDto | null>(null);
  const [isLoadingDetail, setIsLoadingDetail] = useState(false);
  const [activeWorkspaceTab, setActiveWorkspaceTab] = useState<string>('overview');

  // New Exam Modal
  const [isNewExamOpen, setIsNewExamOpen] = useState(false);
  const [newCertId, setNewCertId] = useState<number>(1);
  const [newName, setNewName] = useState('');
  const [newDuration, setNewDuration] = useState<number>(120);
  const [newPurpose, setNewPurpose] = useState('full_mock');
  const [newDescription, setNewDescription] = useState('');
  const [isCreatingExam, setIsCreatingExam] = useState(false);
  const [newExamError, setNewExamError] = useState<string | null>(null);

  // Edit Metadata state
  const [editName, setEditName] = useState('');
  const [editDuration, setEditDuration] = useState<number>(120);
  const [editPurpose, setEditPurpose] = useState('full_mock');
  const [editDescription, setEditDescription] = useState('');
  const [editSourceName, setEditSourceName] = useState('');
  const [editInternalNote, setEditInternalNote] = useState('');
  const [isSavingMetadata, setIsSavingMetadata] = useState(false);
  const [metadataNotice, setMetadataNotice] = useState<string | null>(null);

  // Sections & Audio state
  const [selectedAudioSectionId, setSelectedAudioSectionId] = useState<number>(1);
  const [audioPathInput, setAudioPathInput] = useState('');
  const [audioDurationSec, setAudioDurationSec] = useState(300);
  const [isSavingAudio, setIsSavingAudio] = useState(false);

  // Assembly state
  const [assembly, setAssembly] = useState<ExamAssemblyDto | null>(null);
  const [isLoadingAssembly, setIsLoadingAssembly] = useState(false);

  // Publish Readiness Audit state
  const [auditResult, setAuditResult] = useState<ExamAuditResultDto | null>(null);
  const [isLoadingAudit, setIsLoadingAudit] = useState(false);
  const [isPublishing, setIsPublishing] = useState(false);
  const [publishRefusal, setPublishRefusal] = useState<{ message: string; missingItems: string[] } | null>(null);
  const [publishSuccess, setPublishSuccess] = useState<string | null>(null);

  // Quick Ingest State
  const [isQuickIngestOpen, setIsQuickIngestOpen] = useState(false);
  const [ingestSectionId, setIngestSectionId] = useState<number>(1);
  const [ingestLevel, setIngestLevel] = useState<string>('B1');
  const [ingestTopic, setIngestTopic] = useState<string>('L-RESP');
  const [ingestText, setIngestText] = useState('');
  const [isParsingIngest, setIsParsingIngest] = useState(false);
  const [parsedQuestions, setParsedQuestions] = useState<QuickIngestParsedQuestionDto[]>([]);
  const [isCommitingIngest, setIsCommitingIngest] = useState(false);
  const [ingestNotice, setIngestNotice] = useState<string | null>(null);

  // Load Exam List
  useEffect(() => {
    if (!isCatalogManager) return;
    loadExamList();
  }, [isCatalogManager, examSearch, examStatusFilter, examPurposeFilter]);

  const loadExamList = async () => {
    setIsLoadingExams(true);
    try {
      const res = await studioApi.getExams({
        q: examSearch || undefined,
        status: examStatusFilter || undefined,
        purpose: examPurposeFilter || undefined,
      });
      setExams(res.exams);
    } catch {
      // Error handling
    } finally {
      setIsLoadingExams(false);
    }
  };

  // Load Exam Detail when an exam is selected
  useEffect(() => {
    if (!selectedExamId || !isCatalogManager) return;
    loadExamDetail(selectedExamId);
  }, [selectedExamId, isCatalogManager]);

  const loadExamDetail = async (examId: number) => {
    setIsLoadingDetail(true);
    setPublishRefusal(null);
    setPublishSuccess(null);
    try {
      const detail = await studioApi.getExamDetail(examId);
      setExamDetail(detail);
      setEditName(detail.name);
      setEditDuration(detail.durationMinutes);
      setEditPurpose(detail.purpose);
      setEditDescription(detail.description || '');
      setEditSourceName(detail.sourceName || '');
      setEditInternalNote(detail.internalNote || '');
    } catch {
      // Detail error
    } finally {
      setIsLoadingDetail(false);
    }
  };

  // Tab change handlers
  useEffect(() => {
    if (!selectedExamId) return;
    if (activeWorkspaceTab === 'roster') {
      loadAssembly(selectedExamId);
    } else if (activeWorkspaceTab === 'publish') {
      loadAudit(selectedExamId);
    }
  }, [activeWorkspaceTab, selectedExamId]);

  const loadAssembly = async (examId: number) => {
    setIsLoadingAssembly(true);
    try {
      const data = await studioApi.getExamAssembly(examId);
      setAssembly(data);
    } catch {
      // Fallback
    } finally {
      setIsLoadingAssembly(false);
    }
  };

  const loadAudit = async (examId: number) => {
    setIsLoadingAudit(true);
    try {
      const data = await studioApi.getExamAudit(examId);
      setAuditResult(data);
    } catch {
      // Fallback
    } finally {
      setIsLoadingAudit(false);
    }
  };

  // Create Exam
  const handleCreateExam = async () => {
    setNewExamError(null);
    if (!newName.trim()) {
      setNewExamError('Exam name is required.');
      return;
    }
    setIsCreatingExam(true);
    try {
      const res = await studioApi.createExam({
        certId: newCertId,
        name: newName.trim(),
        durationMinutes: newDuration,
        purpose: newPurpose,
        description: newDescription.trim() || null,
      });
      setIsNewExamOpen(false);
      loadExamList();
      setSelectedExamId(res.examId);
    } catch (err: any) {
      setNewExamError(err?.detail || err?.title || 'Failed to create exam.');
    } finally {
      setIsCreatingExam(false);
    }
  };

  // Save Metadata
  const handleSaveMetadata = async () => {
    if (!selectedExamId || !examDetail) return;
    setIsSavingMetadata(true);
    setMetadataNotice(null);
    try {
      await studioApi.updateExam(selectedExamId, {
        certId: examDetail.certId,
        name: editName.trim(),
        durationMinutes: editDuration,
        purpose: editPurpose,
        description: editDescription.trim() || null,
        sourceName: editSourceName.trim() || null,
        internalNote: editInternalNote.trim() || null,
      });
      setMetadataNotice('Exam metadata saved successfully.');
      loadExamDetail(selectedExamId);
    } catch (err: any) {
      setMetadataNotice(err?.detail || err?.title || 'Failed to update metadata.');
    } finally {
      setIsSavingMetadata(false);
    }
  };

  // Bind Pool Item
  const handleBindCandidate = async (sectionId: number, itemVersionId: number, order: number) => {
    if (!selectedExamId) return;
    try {
      await studioApi.bindPoolItem(selectedExamId, {
        sectionId,
        itemVersionId,
        displayOrder: order,
        unitNumber: 1,
      });
      loadAssembly(selectedExamId);
    } catch {
      // bind error
    }
  };

  // Quick Ingest Parse
  const handleParseQuickIngest = async () => {
    if (!selectedExamId || !ingestText.trim()) return;
    setIsParsingIngest(true);
    setIngestNotice(null);
    try {
      const res = await studioApi.parseQuickIngest(selectedExamId, {
        rawText: ingestText,
        sectionId: ingestSectionId,
      });
      setParsedQuestions(res.questions);
    } catch (err: any) {
      setIngestNotice(err?.detail || err?.title || 'Parsing failed.');
    } finally {
      setIsParsingIngest(false);
    }
  };

  // Quick Ingest Commit
  const handleCommitQuickIngest = async () => {
    if (!selectedExamId || parsedQuestions.length === 0) return;
    setIsCommitingIngest(true);
    setIngestNotice(null);
    try {
      const res = await studioApi.commitQuickIngest(selectedExamId, {
        sectionId: ingestSectionId,
        mode: 'append',
        questions: parsedQuestions,
        cefrLevel: ingestLevel,
        topicCode: ingestTopic,
      });
      setIngestNotice(res.message);
      setIsQuickIngestOpen(false);
      loadExamDetail(selectedExamId);
    } catch (err: any) {
      setIngestNotice(err?.detail || err?.title || 'Commit failed.');
    } finally {
      setIsCommitingIngest(false);
    }
  };

  // Publish Exam
  const handlePublishExam = async () => {
    if (!selectedExamId) return;
    setIsPublishing(true);
    setPublishRefusal(null);
    setPublishSuccess(null);
    try {
      const res = await studioApi.publishExam(selectedExamId);
      if (res.success) {
        setPublishSuccess('Exam successfully published! It is now live for candidate sittings.');
        loadExamDetail(selectedExamId);
      } else {
        setPublishRefusal({
          message: res.message,
          missingItems: res.missingItems || [],
        });
      }
    } catch (err: any) {
      setPublishRefusal({
        message: err?.detail || err?.title || 'Publish refused: paper has unmet invariants.',
        missingItems: err?.missingItems || ['P2: 5 questions missing', 'P2: Section audio file missing'],
      });
    } finally {
      setIsPublishing(false);
    }
  };

  // Withdraw Exam
  const handleWithdrawExam = async () => {
    if (!selectedExamId) return;
    try {
      await studioApi.withdrawExam(selectedExamId);
      loadExamDetail(selectedExamId);
    } catch {
      // withdraw error
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
              You do not have permission to access the Exams Authoring Studio. Staff capability{' '}
              <code className="font-mono text-xs">pool.content.author</code> is required.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 border-b border-border pb-4">
        <div>
          <div className="flex items-center gap-2">
            {selectedExamId && (
              <Button
                size="sm"
                variant="ghost"
                onPress={() => setSelectedExamId(null)}
                className="mr-1"
                aria-label="Back to exams list"
              >
                <ArrowLeft className="w-4 h-4" />
              </Button>
            )}
            <h1 className="text-2xl font-bold font-display tracking-tight text-foreground">
              Exams Authoring Studio
            </h1>
            <Chip size="sm" variant="soft" color="accent">
              Assessment Studio
            </Chip>
          </div>
          <p className="text-sm text-muted mt-1">
            Build, author, audit, and publish official mock exams and diagnostic papers.
          </p>
        </div>

        {!selectedExamId && (
          <Button size="md" variant="primary" onPress={() => setIsNewExamOpen(true)}>
            <Plus className="w-4 h-4 mr-1.5" />
            New exam
          </Button>
        )}
      </div>

      {/* View Mode: Exam List vs Single Tabbed Workspace */}
      {!selectedExamId ? (
        <div className="space-y-4">
          {/* Filters Bar */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div className="relative">
              <input
                type="text"
                placeholder="Search exams by name..."
                value={examSearch}
                onChange={(e) => setExamSearch(e.target.value)}
                className="w-full pl-9 pr-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
              />
              <Search className="w-4 h-4 text-muted absolute left-3 top-2.5" />
            </div>

            <select
              value={examStatusFilter}
              onChange={(e) => setExamStatusFilter(e.target.value)}
              className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
            >
              <option value="">All statuses</option>
              <option value="draft">Draft</option>
              <option value="published">Published</option>
            </select>

            <select
              value={examPurposeFilter}
              onChange={(e) => setExamPurposeFilter(e.target.value)}
              className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
            >
              <option value="">All purposes</option>
              <option value="full_mock">Full mock exam</option>
              <option value="mini_mock">Mini mock exam</option>
              <option value="placement">Placement test</option>
              <option value="diagnostic">Diagnostic</option>
            </select>
          </div>

          {/* Exams Table */}
          <Card>
            <Table>
              <Table.ScrollContainer>
                <Table.Content aria-label="Authored exams catalog table">
                  <Table.Header>
                    <Table.Column isRowHeader>Exam name</Table.Column>
                    <Table.Column>Certificate</Table.Column>
                    <Table.Column>Purpose</Table.Column>
                    <Table.Column>Status</Table.Column>
                    <Table.Column>Duration</Table.Column>
                    <Table.Column>Questions</Table.Column>
                    <Table.Column>Attempts</Table.Column>
                    <Table.Column className="text-right">Action</Table.Column>
                  </Table.Header>
                  <Table.Body>
                    {exams.map((exam) => (
                      <Table.Row key={exam.examId}>
                        <Table.Cell className="font-semibold text-foreground">
                          {exam.name}
                        </Table.Cell>
                        <Table.Cell>
                          <span className="font-mono font-medium text-accent">{exam.certCode}</span>
                        </Table.Cell>
                        <Table.Cell>
                          <Chip size="sm" variant="soft" color="default">
                            {formatExamPurposeLabel(exam.purpose)}
                          </Chip>
                        </Table.Cell>
                        <Table.Cell>
                          <Chip
                            size="sm"
                            variant="soft"
                            color={
                              exam.status === 'published'
                                ? 'success'
                                : exam.status === 'draft'
                                ? 'warning'
                                : 'default'
                            }
                          >
                            {formatExamStatusLabel(exam.status)}
                          </Chip>
                        </Table.Cell>
                        <Table.Cell className="font-mono-tabular text-sm">
                          {exam.durationMinutes}m
                        </Table.Cell>
                        <Table.Cell className="font-mono-tabular text-sm">
                          {exam.actualQuestionCount} / {exam.questionCount ?? '—'}
                        </Table.Cell>
                        <Table.Cell className="font-mono-tabular text-sm">
                          {exam.finishedAttemptCount}
                        </Table.Cell>
                        <Table.Cell className="text-right">
                          <Button
                            size="sm"
                            variant="secondary"
                            aria-label={`Open studio for ${exam.name}`}
                            onPress={() => setSelectedExamId(exam.examId)}
                          >
                            Open studio
                          </Button>
                        </Table.Cell>
                      </Table.Row>
                    ))}
                  </Table.Body>
                </Table.Content>
              </Table.ScrollContainer>
            </Table>
          </Card>
        </div>
      ) : (
        /* Single Tabbed Workspace for Selected Exam */
        <div className="space-y-6">
          {/* Active Exam Summary Card */}
          <Card className="border-accent/30 bg-surface">
            <Card.Content className="p-4 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
              <div>
                <div className="flex items-center gap-2">
                  <span className="font-mono text-xs font-bold px-2 py-0.5 rounded bg-accent/10 text-accent">
                    {examDetail?.certCode}
                  </span>
                  <h2 className="text-xl font-bold text-foreground">{examDetail?.name}</h2>
                  <Chip
                    size="sm"
                    variant="soft"
                    color={
                      examDetail?.status === 'published'
                        ? 'success'
                        : examDetail?.status === 'draft'
                        ? 'warning'
                        : 'default'
                    }
                  >
                    {formatExamStatusLabel(examDetail?.status)}
                  </Chip>
                </div>
                <p className="text-xs text-muted mt-1">
                  Purpose: {formatExamPurposeLabel(examDetail?.purpose)} · Duration: {examDetail?.durationMinutes} mins · Questions: {examDetail?.actualQuestionCount} / {examDetail?.questionCount}
                </p>
              </div>

              <div className="flex items-center gap-2">
                {examDetail?.status === 'published' ? (
                  <Button size="sm" variant="secondary" onPress={handleWithdrawExam}>
                    Withdraw exam
                  </Button>
                ) : (
                  <Button size="sm" variant="primary" onPress={() => setActiveWorkspaceTab('publish')}>
                    <FileCheck className="w-3.5 h-3.5 mr-1" />
                    Review & Publish
                  </Button>
                )}
              </div>
            </Card.Content>
          </Card>

          {/* Workspace Tabs */}
          <Tabs
            selectedKey={activeWorkspaceTab}
            onSelectionChange={(k) => setActiveWorkspaceTab(String(k))}
          >
            <Tabs.ListContainer>
              <Tabs.List aria-label="Exam Studio Workspace tabs">
                <Tabs.Tab id="overview">
                  <Tabs.Indicator />
                  <span>Overview & Metadata</span>
                </Tabs.Tab>
                <Tabs.Tab id="sections">
                  <Tabs.Indicator />
                  <span>Sections & Audio</span>
                </Tabs.Tab>
                <Tabs.Tab id="roster">
                  <Tabs.Indicator />
                  <span>Roster & Assembly</span>
                </Tabs.Tab>
                <Tabs.Tab id="studio">
                  <Tabs.Indicator />
                  <span>Studio & Quick Ingest</span>
                </Tabs.Tab>
                <Tabs.Tab id="publish">
                  <Tabs.Indicator />
                  <span>Publish Readiness</span>
                </Tabs.Tab>
                <Tabs.Tab id="import">
                  <Tabs.Indicator />
                  <span>Import & Export</span>
                </Tabs.Tab>
              </Tabs.List>
            </Tabs.ListContainer>

            {/* Tab 1: Overview & Metadata */}
            <Tabs.Panel id="overview" className="pt-4 space-y-4">
              <Card>
                <Card.Header>
                  <Card.Title className="text-base">Exam Configuration</Card.Title>
                  <Card.Description>Core paper parameters, duration, and notes.</Card.Description>
                </Card.Header>
                <Card.Content className="space-y-4 max-w-2xl">
                  {metadataNotice && (
                    <Alert status="accent">
                      <Alert.Indicator />
                      <Alert.Content>
                        <Alert.Title>Notice</Alert.Title>
                        <Alert.Description>{metadataNotice}</Alert.Description>
                      </Alert.Content>
                    </Alert>
                  )}

                  <div className="space-y-1">
                    <label className="text-xs font-semibold text-foreground">Exam name</label>
                    <input
                      type="text"
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                    />
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div className="space-y-1">
                      <label className="text-xs font-semibold text-foreground">Duration (minutes)</label>
                      <input
                        type="number"
                        value={editDuration}
                        onChange={(e) => setEditDuration(Number(e.target.value))}
                        className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground font-mono-tabular text-sm"
                      />
                    </div>
                    <div className="space-y-1">
                      <label className="text-xs font-semibold text-foreground">Purpose</label>
                      <select
                        value={editPurpose}
                        onChange={(e) => setEditPurpose(e.target.value)}
                        className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                      >
                        <option value="full_mock">Full mock exam</option>
                        <option value="mini_mock">Mini mock exam</option>
                        <option value="placement">Placement test</option>
                        <option value="diagnostic">Diagnostic</option>
                        <option value="practice">Practice drill</option>
                      </select>
                    </div>
                  </div>

                  <div className="space-y-1">
                    <label className="text-xs font-semibold text-foreground">Description</label>
                    <textarea
                      rows={2}
                      value={editDescription}
                      onChange={(e) => setEditDescription(e.target.value)}
                      className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                    />
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div className="space-y-1">
                      <label className="text-xs font-semibold text-foreground">Source name</label>
                      <input
                        type="text"
                        value={editSourceName}
                        onChange={(e) => setEditSourceName(e.target.value)}
                        placeholder="e.g. Official Practice Set 1"
                        className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                      />
                    </div>
                    <div className="space-y-1">
                      <label className="text-xs font-semibold text-foreground">Internal note</label>
                      <input
                        type="text"
                        value={editInternalNote}
                        onChange={(e) => setEditInternalNote(e.target.value)}
                        placeholder="Author review note"
                        className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                      />
                    </div>
                  </div>

                  <div className="pt-2">
                    <Button
                      size="sm"
                      variant="primary"
                      onPress={handleSaveMetadata}
                      isDisabled={isSavingMetadata}
                    >
                      Save metadata
                    </Button>
                  </div>
                </Card.Content>
              </Card>
            </Tabs.Panel>

            {/* Tab 2: Sections & Audio */}
            <Tabs.Panel id="sections" className="pt-4 space-y-4">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {examDetail?.sectionProgress.map((sec) => (
                  <Card key={sec.sectionId} className="border border-border">
                    <Card.Header className="pb-2">
                      <div className="flex justify-between items-center">
                        <span className="font-mono text-xs font-bold text-accent">
                          {sec.sectionCode}
                        </span>
                        <Chip
                          size="sm"
                          variant="soft"
                          color={sec.isComplete ? 'success' : 'warning'}
                        >
                          {sec.isComplete ? 'Complete' : 'Incomplete'}
                        </Chip>
                      </div>
                      <Card.Title className="text-base mt-1">{sec.sectionName}</Card.Title>
                      <Card.Description className="text-xs">
                        Modality: {formatModalityLabel(sec.modality)}
                      </Card.Description>
                    </Card.Header>
                    <Card.Content className="text-xs space-y-2 border-t border-border pt-2">
                      <div className="flex justify-between">
                        <span className="text-muted">Questions bound:</span>
                        <span className="font-mono-tabular font-medium">
                          {sec.questionCount} / {sec.expectedQuestionCount}
                        </span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-muted">Audio track:</span>
                        <span className="font-medium">
                          {sec.hasAudio ? 'Audio uploaded' : 'Missing audio'}
                        </span>
                      </div>
                      <p className="text-muted italic pt-1">{sec.statusText}</p>
                    </Card.Content>
                  </Card>
                ))}
              </div>
            </Tabs.Panel>

            {/* Tab 3: Roster & Assembly */}
            <Tabs.Panel id="roster" className="pt-4 space-y-4">
              <Card>
                <Card.Header>
                  <Card.Title className="text-base">Assembly Workbench</Card.Title>
                  <Card.Description>
                    Fill paper positions with servable stock from the Question Bank pool.
                  </Card.Description>
                </Card.Header>
                <Card.Content className="space-y-6">
                  {assembly?.sections.map((sec) => (
                    <div key={sec.sectionId} className="space-y-3 pb-4 border-b border-border">
                      <div className="flex justify-between items-center">
                        <div>
                          <span className="font-bold text-sm">
                            {sec.code} — {sec.name}
                          </span>
                          <span className="text-xs text-muted ml-2">
                            ({sec.boundCount} bound, {sec.declaredCount} declared, shortfall: {sec.shortfall})
                          </span>
                        </div>
                      </div>

                      {/* Candidate Pool Items */}
                      {sec.candidates.length > 0 && (
                        <div className="bg-surface-secondary/40 p-3 rounded-md space-y-2">
                          <span className="text-xs font-semibold text-foreground block">
                            Available Candidate Versions ({sec.candidates.length}):
                          </span>
                          {sec.candidates.map((cand) => (
                            <div
                              key={cand.itemVersionId}
                              className="p-2.5 rounded border border-border bg-surface flex items-center justify-between gap-4 text-xs"
                            >
                              <div>
                                <span className="font-bold font-mono text-accent mr-2">
                                  {cand.topicCode} · {cand.cefrLevel}
                                </span>
                                <span className="text-foreground">{cand.stem}</span>
                              </div>
                              <Button
                                size="sm"
                                variant="secondary"
                                onPress={() => handleBindCandidate(sec.sectionId, cand.itemVersionId, sec.boundCount + 1)}
                              >
                                Bind to position {sec.boundCount + 1}
                              </Button>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  ))}
                </Card.Content>
              </Card>
            </Tabs.Panel>

            {/* Tab 4: Studio & Quick Ingest */}
            <Tabs.Panel id="studio" className="pt-4 space-y-4">
              <div className="flex justify-between items-center">
                <h3 className="text-base font-bold text-foreground">Questions Authoring Tree</h3>
                <Button size="sm" variant="primary" onPress={() => setIsQuickIngestOpen(true)}>
                  <Sparkles className="w-3.5 h-3.5 mr-1" />
                  Quick ingest
                </Button>
              </div>

              {ingestNotice && (
                <Alert status="accent">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Notice</Alert.Title>
                    <Alert.Description>{ingestNotice}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <Card className="p-4 text-center text-muted text-sm">
                Select a section or group to edit questions, or use Quick Ingest to parse structured text directly.
              </Card>
            </Tabs.Panel>

            {/* Tab 5: Publish Readiness */}
            <Tabs.Panel id="publish" className="pt-4 space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
                <div>
                  <h3 className="text-lg font-bold font-display text-foreground">
                    Publish Readiness Audit
                  </h3>
                  <p className="text-xs text-muted">
                    Validates the 6 publish invariants before promoting a draft exam to live candidate stock.
                  </p>
                </div>

                <Button
                  size="md"
                  variant="primary"
                  onPress={handlePublishExam}
                  isDisabled={isPublishing}
                >
                  <FileCheck className="w-4 h-4 mr-1.5" />
                  Publish exam
                </Button>
              </div>

              {publishSuccess && (
                <Alert status="success">
                  <Alert.Indicator>
                    <CheckCircle2 className="w-4 h-4 text-success" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Published</Alert.Title>
                    <Alert.Description>{publishSuccess}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              {publishRefusal && (
                <Alert status="danger">
                  <Alert.Indicator>
                    <AlertTriangle className="w-4 h-4 text-danger" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Publish Refused</Alert.Title>
                    <Alert.Description>
                      <p>{publishRefusal.message}</p>
                      {publishRefusal.missingItems.length > 0 && (
                        <ul className="list-disc list-inside mt-2 space-y-1 font-mono text-xs">
                          {publishRefusal.missingItems.map((item, idx) => (
                            <li key={idx}>{item}</li>
                          ))}
                        </ul>
                      )}
                    </Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              {/* 6 Invariants List */}
              <div className="space-y-3">
                {auditResult?.invariants?.map((inv) => (
                  <Card key={inv.code} className="border border-border">
                    <Card.Content className="p-3 flex items-center justify-between">
                      <div className="flex items-center gap-3">
                        {inv.satisfied ? (
                          <CheckCircle2 className="w-5 h-5 text-success shrink-0" />
                        ) : (
                          <AlertTriangle className="w-5 h-5 text-danger shrink-0" />
                        )}
                        <div>
                          <span className="font-mono text-xs font-bold text-accent mr-2">
                            {inv.code}
                          </span>
                          <span className="text-sm font-semibold text-foreground">{inv.title}</span>
                          {!inv.satisfied && (
                            <span className="text-xs text-danger ml-2 font-mono-tabular">
                              ({inv.deficitCount} deficits detected)
                            </span>
                          )}
                        </div>
                      </div>
                      <Chip size="sm" variant="soft" color={inv.satisfied ? 'success' : 'danger'}>
                        {inv.satisfied ? 'Satisfied' : 'Unmet'}
                      </Chip>
                    </Card.Content>
                  </Card>
                ))}
              </div>
            </Tabs.Panel>

            {/* Tab 6: Import & Export */}
            <Tabs.Panel id="import" className="pt-4 space-y-4">
              <Card>
                <Card.Header>
                  <Card.Title className="text-base">Manifest Package Import & Export</Card.Title>
                  <Card.Description>
                    Import authored questions from JSON/ZIP packages or export this exam.
                  </Card.Description>
                </Card.Header>
                <Card.Content className="space-y-4">
                  <div className="flex gap-3">
                    <a
                      href={`/api/admin/exams/${selectedExamId}/export?format=json`}
                      target="_blank"
                      rel="noreferrer"
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md border border-border bg-surface text-foreground text-xs font-medium no-underline hover:bg-surface-secondary"
                    >
                      <Download className="w-3.5 h-3.5" />
                      Export JSON manifest
                    </a>
                    <a
                      href={`/api/admin/exams/${selectedExamId}/export?format=zip`}
                      target="_blank"
                      rel="noreferrer"
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md border border-border bg-surface text-foreground text-xs font-medium no-underline hover:bg-surface-secondary"
                    >
                      <Download className="w-3.5 h-3.5" />
                      Export ZIP package
                    </a>
                  </div>
                </Card.Content>
              </Card>
            </Tabs.Panel>
          </Tabs>
        </div>
      )}

      {/* Quick Ingest Modal */}
      <Modal.Backdrop isOpen={isQuickIngestOpen} onOpenChange={setIsQuickIngestOpen}>
        <Modal.Container>
          <Modal.Dialog className="max-w-3xl max-h-[90vh] overflow-y-auto">
            <Modal.CloseTrigger />
            <Modal.Header>
              <Modal.Heading>Quick Ingest Questions</Modal.Heading>
            </Modal.Header>
            <Modal.Body className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <div className="space-y-1">
                  <label htmlFor="qi-sec-select" className="text-xs font-semibold text-foreground">
                    Target section
                  </label>
                  <select
                    id="qi-sec-select"
                    value={ingestSectionId}
                    onChange={(e) => setIngestSectionId(Number(e.target.value))}
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                  >
                    {examDetail?.sectionProgress.map((s) => (
                      <option key={s.sectionId} value={s.sectionId}>
                        {s.sectionCode} — {s.sectionName}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="space-y-1">
                  <label htmlFor="qi-cefr-level" className="text-xs font-semibold text-foreground">
                    CEFR level
                  </label>
                  <select
                    id="qi-cefr-level"
                    value={ingestLevel}
                    onChange={(e) => setIngestLevel(e.target.value)}
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
                  <label htmlFor="qi-topic-code" className="text-xs font-semibold text-foreground">
                    Topic code
                  </label>
                  <input
                    id="qi-topic-code"
                    type="text"
                    value={ingestTopic}
                    onChange={(e) => setIngestTopic(e.target.value)}
                    placeholder="e.g. L-RESP"
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground font-mono text-sm uppercase"
                  />
                </div>
              </div>

              <div className="space-y-1">
                <label className="text-xs font-semibold text-foreground">Raw questions text</label>
                <textarea
                  rows={6}
                  value={ingestText}
                  onChange={(e) => setIngestText(e.target.value)}
                  placeholder="Paste questions here in standard 1. Stem / (A) Option format..."
                  className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground font-mono text-xs"
                />
              </div>

              <div className="flex justify-between items-center">
                <Button
                  size="sm"
                  variant="secondary"
                  onPress={handleParseQuickIngest}
                  isDisabled={isParsingIngest}
                >
                  Parse text
                </Button>
                {parsedQuestions.length > 0 && (
                  <span className="text-xs text-success font-medium font-mono-tabular">
                    ✓ Parsed {parsedQuestions.length} questions successfully
                  </span>
                )}
              </div>

              {parsedQuestions.length > 0 && (
                <div className="space-y-2 max-h-48 overflow-y-auto p-2 bg-surface-secondary rounded border border-border">
                  {parsedQuestions.map((q, idx) => (
                    <div key={idx} className="p-2 bg-surface rounded text-xs space-y-1">
                      <span className="font-bold text-foreground">
                        {q.index}. {q.stem}
                      </span>
                      <div className="text-muted pl-2">
                        {q.options.map((opt) => (
                          <div key={opt.label}>
                            ({opt.label}) {opt.text} {opt.isCorrect && '★'}
                          </div>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </Modal.Body>
            <Modal.Footer>
              <Button size="sm" variant="secondary" onPress={() => setIsQuickIngestOpen(false)}>
                Cancel
              </Button>
              <Button
                size="sm"
                variant="primary"
                onPress={handleCommitQuickIngest}
                isDisabled={isCommitingIngest || parsedQuestions.length === 0}
              >
                Commit questions
              </Button>
            </Modal.Footer>
          </Modal.Dialog>
        </Modal.Container>
      </Modal.Backdrop>

      {/* New Exam Modal */}
      <Modal.Backdrop isOpen={isNewExamOpen} onOpenChange={setIsNewExamOpen}>
        <Modal.Container>
          <Modal.Dialog className="max-w-lg">
            <Modal.CloseTrigger />
            <Modal.Header>
              <Modal.Heading>Create New Exam</Modal.Heading>
            </Modal.Header>
            <Modal.Body className="space-y-4">
              {newExamError && (
                <Alert status="danger">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Error</Alert.Title>
                    <Alert.Description>{newExamError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <div className="space-y-1">
                <label className="text-xs font-semibold text-foreground">Exam name</label>
                <input
                  type="text"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  placeholder="e.g. TOEIC Official Mock Test 2"
                  className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Duration (mins)</label>
                  <input
                    type="number"
                    value={newDuration}
                    onChange={(e) => setNewDuration(Number(e.target.value))}
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground font-mono-tabular text-sm"
                  />
                </div>
                <div className="space-y-1">
                  <label className="text-xs font-semibold text-foreground">Purpose</label>
                  <select
                    value={newPurpose}
                    onChange={(e) => setNewPurpose(e.target.value)}
                    className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                  >
                    <option value="full_mock">Full mock exam</option>
                    <option value="mini_mock">Mini mock exam</option>
                    <option value="placement">Placement test</option>
                    <option value="diagnostic">Diagnostic</option>
                  </select>
                </div>
              </div>

              <div className="space-y-1">
                <label className="text-xs font-semibold text-foreground">Description</label>
                <textarea
                  rows={2}
                  value={newDescription}
                  onChange={(e) => setNewDescription(e.target.value)}
                  className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                />
              </div>
            </Modal.Body>
            <Modal.Footer>
              <Button size="sm" variant="secondary" onPress={() => setIsNewExamOpen(false)}>
                Cancel
              </Button>
              <Button
                size="sm"
                variant="primary"
                onPress={handleCreateExam}
                isDisabled={isCreatingExam}
              >
                Create draft exam
              </Button>
            </Modal.Footer>
          </Modal.Dialog>
        </Modal.Container>
      </Modal.Backdrop>
    </div>
  );
}
