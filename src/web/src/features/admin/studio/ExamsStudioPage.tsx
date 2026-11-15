import React, { useState, useEffect } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Disclosure,
  Label,
  ListBox,
  Modal,
  ProgressBar,
  SearchField,
  Select,
  Table,
  FieldError,
  Form,
  Input,
  NumberField,
  Tabs,
  TextArea,
  TextField,
} from '@heroui/react';
import { extractErrorMessage, extractFieldErrors } from './validationErrors';
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
  ArrowUp,
  ArrowDown,
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
  AdminCertificateSummaryDto,
  AdminExamListItemDto,
  AdminExamDetailDto,
  AdminExamSectionProgressDto,
  ExamAuditResultDto,
  ExamAssemblyDto,
  QuickIngestParsedQuestionDto,
  QuickIngestSampleTemplateDto,
  CreateAdminExamRequest,
  ExamStudioDetailDto,
  ExamStudioQuestionDto,
  AdminImportResultDto,
} from './types';
import { AUDIO_MEDIA_EXTENSIONS, IMAGE_MEDIA_EXTENSIONS } from './types';

/**
 * #359: the file's own duration, measured in the browser before it is recorded. A Listening Section
 * refuses a zero duration and the server cannot measure an upload, so the answer has to come from
 * here.
 *
 * Measuring is best-effort on purpose. A browser that cannot decode the file, or an environment with
 * no `createObjectURL`, must not lose an upload the author already chose - the caller floors the
 * result to 1 second, which is the smallest value the route accepts. A measurement failure that
 * blocked the save would be worse than an approximate duration.
 */
function readAudioDurationSec(file: File): Promise<number> {
  return new Promise((resolve) => {
    let url: string | null = null;
    let settled = false;
    let timer: ReturnType<typeof setTimeout> | null = null;

    // Resolve exactly once: the timeout and the load event race, and a second call would resolve a
    // promise already settled and revoke a URL already revoked.
    const done = (seconds: number) => {
      if (settled) return;
      settled = true;
      if (timer) clearTimeout(timer);
      if (url) URL.revokeObjectURL(url);
      resolve(Number.isFinite(seconds) && seconds > 0 ? seconds : 0);
    };

    try {
      if (typeof URL.createObjectURL !== 'function') {
        done(0);
        return;
      }
      url = URL.createObjectURL(file);
      const audio = new Audio();
      audio.addEventListener('loadedmetadata', () => done(audio.duration));
      audio.addEventListener('error', () => done(0));
      // A source that never loads must not strand the upload control.
      timer = setTimeout(() => done(0), 5000);
      audio.src = url;
    } catch {
      done(0);
    }
  });
}

// The only purposes CK_Exams_Purpose accepts. Every purpose control draws from this list so the
// filter, the metadata editor and the create modal can never drift apart again (#357).
const EXAM_PURPOSES = [
  { id: 'mock', label: 'Mock exam' },
  { id: 'placement', label: 'Placement test' },
  { id: 'speaking_practice', label: 'Speaking practice' },
  { id: 'writing_practice', label: 'Writing practice' },
] as const;

export function ExamsStudioPage() {
  const { user } = useAuth();
  const isCatalogManager = canManageCatalog(user);

  // Certificate options for the create modal
  const [certificates, setCertificates] = useState<AdminCertificateSummaryDto[]>([]);

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
  const [newCertId, setNewCertId] = useState<string>('');
  const [newName, setNewName] = useState('');
  const [newDuration, setNewDuration] = useState<number>(120);
  const [newPurpose, setNewPurpose] = useState('mock');
  const [newDescription, setNewDescription] = useState('');
  const [isCreatingExam, setIsCreatingExam] = useState(false);
  const [newExamError, setNewExamError] = useState<string | null>(null);
  const [newExamFieldErrors, setNewExamFieldErrors] = useState<Record<string, string>>({});

  // Edit Metadata state
  const [editName, setEditName] = useState('');
  const [editDuration, setEditDuration] = useState<number>(120);
  const [editPurpose, setEditPurpose] = useState('mock');
  const [editDescription, setEditDescription] = useState('');
  const [editSourceName, setEditSourceName] = useState('');
  const [editInternalNote, setEditInternalNote] = useState('');
  const [isSavingMetadata, setIsSavingMetadata] = useState(false);
  const [metadataNotice, setMetadataNotice] = useState<string | null>(null);
  const [metadataFieldErrors, setMetadataFieldErrors] = useState<Record<string, string>>({});

  // #359: which Section's upload control is busy, and the outcome shown beside it.
  const [uploadingSectionId, setUploadingSectionId] = useState<number | null>(null);
  const [sectionMediaNotice, setSectionMediaNotice] = useState<
    { sectionId: number; isError: boolean; text: string } | null
  >(null);

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
  const [ingestLevelBySection, setIngestLevelBySection] = useState<Record<number, string>>({});
  const [ingestTopic, setIngestTopic] = useState<string>('L-RESP');
  const [ingestText, setIngestText] = useState('');
  const [isParsingIngest, setIsParsingIngest] = useState(false);
  const [parsedQuestions, setParsedQuestions] = useState<QuickIngestParsedQuestionDto[]>([]);
  const [isCommitingIngest, setIsCommitingIngest] = useState(false);
  const [ingestNotice, setIngestNotice] = useState<{ text: string; isError: boolean } | null>(null);
  const [ingestSample, setIngestSample] = useState<QuickIngestSampleTemplateDto | null>(null);

  // The paste template belongs to the Section being filled, so it reloads whenever the target changes.
  useEffect(() => {
    if (!isQuickIngestOpen) return;
    let cancelled = false;
    setIngestSample(null);
    studioApi
      .getQuickIngestSample(ingestSectionId)
      .then((sample) => {
        if (!cancelled) setIngestSample(sample);
      })
      .catch(() => {
        // A missing template is not an error: the generic placeholder still lets the author paste.
      });
    return () => {
      cancelled = true;
    };
  }, [isQuickIngestOpen, ingestSectionId]);

  // Load Exam List
  useEffect(() => {
    if (!isCatalogManager) return;
    loadExamList();
  }, [isCatalogManager, examSearch, examStatusFilter, examPurposeFilter]);

  // Load Certificate options for the create modal
  useEffect(() => {
    if (!isCatalogManager) return;
    loadCertificates();
  }, [isCatalogManager]);

  const loadCertificates = async () => {
    try {
      setCertificates(await studioApi.getCertificates());
    } catch {
      // The modal's Certificate selector stays empty; the create button refuses to submit.
    }
  };

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
    } else if (activeWorkspaceTab === 'content') {
      loadStudio(selectedExamId);
    }
  }, [activeWorkspaceTab, selectedExamId]);

  const loadStudio = async (examId: number) => {
    setIsLoadingStudio(true);
    try {
      setStudioDetail(await studioApi.getExamStudio(examId));
    } catch (err: unknown) {
      setBundleNotice({
        sectionId: 0,
        isError: true,
        text: extractErrorMessage(err, 'Failed to load Section content.'),
      });
    } finally {
      setIsLoadingStudio(false);
    }
  };

  // #362: a Bundle is the shared stimulus its children hang off, so an edit writes the group - not
  // each child, which would copy the passage per question.
  const startEditingBundle = (sectionId: number, groupId: number) => {
    const group = studioDetail?.sections
      .find((s) => s.sectionId === sectionId)
      ?.groups.find((g) => g.groupId === groupId);
    if (!group) return;
    setBundleNotice(null);
    setBundleDraft({
      sectionId,
      groupId,
      passage: group.passage ?? '',
      transcript: group.transcript ?? '',
      imagePath: group.imagePath ?? '',
      imageAlt: group.imageAlt ?? '',
      audioPath: group.audioPath ?? '',
      // Carried, not dropped: the group route assigns the slice unconditionally, so omitting it would
      // silently widen the Bundle's audio to the whole track - a data change nobody asked for.
      audioStartMs: group.audioStartMs == null ? '' : String(group.audioStartMs),
      audioEndMs: group.audioEndMs == null ? '' : String(group.audioEndMs),
    });
  };

  const handleSaveBundle = async () => {
    if (!selectedExamId || !bundleDraft) return;
    setIsSavingBundle(true);
    setBundleNotice(null);
    try {
      // The slice must survive a save that did not touch it: the group route assigns these two
      // unconditionally, so a null here would widen the Bundle's audio to the whole track.
      const start = bundleDraft.audioStartMs.trim();
      const end = bundleDraft.audioEndMs.trim();
      const hasSlice = start !== '' && end !== '';
      if ((start === '') !== (end === '')) {
        throw new Error('An audio slice needs both a start and an end, or neither.');
      }

      await studioApi.updateQuestionGroup(selectedExamId, bundleDraft.groupId, {
        sectionId: bundleDraft.sectionId,
        passage: bundleDraft.passage.trim() || null,
        transcript: bundleDraft.transcript.trim() || null,
        imagePath: bundleDraft.imagePath.trim() || null,
        imageAlt: bundleDraft.imageAlt.trim() || null,
        audioPath: bundleDraft.audioPath.trim() || null,
        audioStartMs: hasSlice ? Number(start) : null,
        audioEndMs: hasSlice ? Number(end) : null,
      });
      setBundleNotice({ sectionId: bundleDraft.sectionId, isError: false, text: 'Bundle saved.' });
      setBundleDraft(null);
      loadStudio(selectedExamId);
    } catch (err: unknown) {
      setBundleNotice({
        sectionId: bundleDraft.sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Failed to save the Bundle.'),
      });
    } finally {
      setIsSavingBundle(false);
    }
  };

  // #362: a child question's content lives on the pool item version, so editing it names the ITEM, not
  // the paper position. QuestionId is a position handle and would address nothing on that route.
  const startEditingQuestion = (sectionId: number, question: ExamStudioQuestionDto) => {
    const option = (label: string) =>
      question.options.find((o) => o.label === label)?.text ?? '';
    const key = question.options
      .filter((o) => o.isCorrect)
      .map((o) => o.label)
      .sort()
      .join(',');
    setBundleNotice(null);
    setQuestionDraft({
      sectionId,
      itemId: question.itemId,
      stem: question.stem,
      optionA: option('A'),
      optionB: option('B'),
      optionC: option('C'),
      optionD: option('D'),
      keyLabel: key,
      explanation: question.explanation ?? '',
    });
  };

  const handleSaveQuestion = async () => {
    if (!questionDraft) return;
    setIsSavingQuestion(true);
    setBundleNotice(null);
    try {
      await studioApi.updatePoolItem(questionDraft.itemId, {
        stem: questionDraft.stem.trim(),
        optionA: questionDraft.optionA.trim() || null,
        optionB: questionDraft.optionB.trim() || null,
        optionC: questionDraft.optionC.trim() || null,
        optionD: questionDraft.optionD.trim() || null,
        keyLabel: questionDraft.keyLabel.trim() || null,
        explanation: questionDraft.explanation.trim() || null,
      });
      setBundleNotice({ sectionId: questionDraft.sectionId, isError: false, text: 'Question saved as a new version.' });
      setQuestionDraft(null);
      if (selectedExamId) loadStudio(selectedExamId);
    } catch (err: unknown) {
      setBundleNotice({
        sectionId: questionDraft.sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Failed to save the question.'),
      });
    } finally {
      setIsSavingQuestion(false);
    }
  };

  /**
   * #362: adds a question to an EXISTING Bundle. Two acts, because the pool separates them: author the
   * item carrying the Bundle's stimulus id, then bind that version to the next position in the Section.
   * Creating a second Bundle instead would duplicate the passage the first one already owns.
   */
  const handleCreateChild = async () => {
    if (!selectedExamId || !newChild) return;
    setIsSavingChild(true);
    setBundleNotice(null);
    try {
      const section = studioDetail?.sections.find((s) => s.sectionId === newChild.sectionId);
      const nextOrder = (section?.currentQuestionCount ?? 0) + 1;

      const created = await studioApi.createPoolItem({
        modality: section?.modality ?? 'reading',
        topicId: newChild.topicId,
        cefrLevelCode: newChild.cefrLevelCode,
        itemType: 'mcq',
        stem: newChild.stem.trim(),
        optionA: null,
        optionB: null,
        optionC: null,
        optionD: null,
        keyLabel: null,
        explanation: newChild.explanation.trim() || null,
        stimulusId: newChild.stimulusId,
        targetBands: 'ALL',
      });

      // The pool answers an item; the paper needs one of its versions. The newest is the one just made.
      await studioApi.bindPoolItem(selectedExamId, {
        sectionId: newChild.sectionId,
        itemVersionId: created.itemVersionId,
        displayOrder: nextOrder,
      });

      setBundleNotice({
        sectionId: newChild.sectionId,
        isError: false,
        text: 'Question added to the Bundle. Edit its stem and options below.',
      });
      setNewChild(null);
      loadStudio(selectedExamId);
    } catch (err: unknown) {
      setBundleNotice({
        sectionId: newChild.sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Failed to add the question.'),
      });
    } finally {
      setIsSavingChild(false);
    }
  };

  /** #362: reorder is the roster's own order, so it sends the position ids the read returned. */
  const handleReorder = async (sectionId: number, orderedPositions: number[]) => {
    if (!selectedExamId) return;
    setBundleNotice(null);
    try {
      await studioApi.reorderExamItems(selectedExamId, {
        sectionId,
        items: orderedPositions.map((questionId, index) => ({
          groupId: null,
          questionId,
          displayOrder: index + 1,
        })),
      });
      setBundleNotice({ sectionId, isError: false, text: 'Order saved.' });
      loadStudio(selectedExamId);
    } catch (err: unknown) {
      setBundleNotice({
        sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Failed to save the order.'),
      });
    }
  };

  const handleCreateBundle = async (sectionId: number, cefrLevelCode: string, questionCount: number) => {    if (!selectedExamId) return;
    setBundleNotice(null);
    try {
      await studioApi.createExamCluster(selectedExamId, {
        sectionId,
        questionCount: Math.max(1, questionCount),
        cefrLevelCode,
      });
      setBundleNotice({
        sectionId,
        isError: false,
        text: `Bundle created with ${Math.max(1, questionCount)} question${questionCount === 1 ? '' : 's'}. Edit its stimulus and each question below.`,
      });
      setNewBundleSectionId(null);
      loadStudio(selectedExamId);
    } catch (err: unknown) {
      setBundleNotice({
        sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Failed to create the Bundle.'),
      });
    }
  };

  const handleDeleteBundle = async (sectionId: number, groupId: number) => {
    if (!selectedExamId) return;
    setBundleNotice(null);
    try {
      await studioApi.deleteQuestionGroup(selectedExamId, groupId);
      setBundleNotice({ sectionId, isError: false, text: 'Bundle removed.' });
      loadStudio(selectedExamId);
    } catch (err: unknown) {
      setBundleNotice({
        sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Failed to remove the Bundle.'),
      });
    }
  };

  // #360: the manifest import door. The route writes Pool stock for the Exam's Certificate; the screen
  // reports how many lines landed and how many were refused, because a silent import is a lie.
  const handleImportManifest = async (file: File) => {
    if (!examDetail) return;
    setIsImportingManifest(true);
    setImportResult(null);
    try {
      const result = await studioApi.uploadExamManifest(examDetail.certId, file);
      setImportResult(result);
      if (selectedExamId) {
        loadExamDetail(selectedExamId);
        if (activeWorkspaceTab === 'content') loadStudio(selectedExamId);
      }
    } catch (err: unknown) {
      setBundleNotice({
        sectionId: 0,
        isError: true,
        text: extractErrorMessage(err, 'Import failed.'),
      });
    } finally {
      setIsImportingManifest(false);
    }
  };

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

  // #359: one audio file per Listening Section. The server names the file and answers the path, so
  // this stores what came back rather than composing a path. Images share the same door.
  const handleUploadSectionAudio = async (sectionId: number, file: File) => {
    if (!selectedExamId) return;
    const ext = `.${file.name.split('.').pop()?.toLowerCase() ?? ''}`;
    // The audio control accepts only audio: the route's allow-list also holds images, so checking the
    // union here let a .png be saved as a section's audio track.
    if (!(AUDIO_MEDIA_EXTENSIONS as readonly string[]).includes(ext)) {
      setSectionMediaNotice({
        sectionId,
        isError: true,
        text: `'${ext}' is not audio. Choose ${AUDIO_MEDIA_EXTENSIONS.join(', ')}.`,
      });
      return;
    }

    setUploadingSectionId(sectionId);
    setSectionMediaNotice(null);
    try {
      const uploaded = await studioApi.uploadExamMedia(selectedExamId, file);
      // Duration is the audio element's own answer; a file we cannot measure gets 1s, which is the
      // smallest value SaveSectionAudioRequest accepts.
      const durationSec = Math.max(1, Math.round(await readAudioDurationSec(file)));
      await studioApi.saveExamAudio(selectedExamId, {
        sectionId,
        audioPath: uploaded.path,
        durationSec,
        sizeBytes: file.size,
      });
      setSectionMediaNotice({ sectionId, isError: false, text: 'Audio saved to this Section.' });
      loadExamDetail(selectedExamId);
    } catch (err: unknown) {
      setSectionMediaNotice({
        sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Upload failed.'),
      });
    } finally {
      setUploadingSectionId(null);
    }
  };

  const handleUploadSectionImage = async (sectionId: number, file: File) => {
    if (!selectedExamId) return;
    const ext = `.${file.name.split('.').pop()?.toLowerCase() ?? ''}`;
    // An image is a different kind from audio: the route's allow-list holds both, so the refusal has
    // to name what THIS control accepts.
    if (!(IMAGE_MEDIA_EXTENSIONS as readonly string[]).includes(ext)) {
      setSectionMediaNotice({
        sectionId,
        isError: true,
        text: `'${ext}' is not an image. Choose ${IMAGE_MEDIA_EXTENSIONS.join(', ')}.`,
      });
      return;
    }
    setUploadingSectionId(sectionId);
    setSectionMediaNotice(null);
    try {
      const uploaded = await studioApi.uploadExamMedia(selectedExamId, file);
      setSectionMediaNotice({
        sectionId,
        isError: false,
        text: `Image stored at ${uploaded.path}. Attach it to a Bundle on the Section content tab.`,
      });
    } catch (err: unknown) {
      setSectionMediaNotice({
        sectionId,
        isError: true,
        text: extractErrorMessage(err, 'Upload failed.'),
      });
    } finally {
      setUploadingSectionId(null);
    }
  };

  // #362: the Section content workspace. Loaded lazily when its tab opens, because it is the heaviest
  // read on the screen (every Section, every Bundle, every child question).
  const [studioDetail, setStudioDetail] = useState<ExamStudioDetailDto | null>(null);
  const [isLoadingStudio, setIsLoadingStudio] = useState(false);
  const [expandedSectionId, setExpandedSectionId] = useState<number | null>(null);
  const [bundleDraft, setBundleDraft] = useState<{
    sectionId: number;
    groupId: number;
    passage: string;
    transcript: string;
    imagePath: string;
    imageAlt: string;
    audioPath: string;
    audioStartMs: string;
    audioEndMs: string;
  } | null>(null);
  const [bundleNotice, setBundleNotice] = useState<{
    sectionId: number;
    isError: boolean;
    text: string;
  } | null>(null);
  const [isSavingBundle, setIsSavingBundle] = useState(false);
  const [newBundleSectionId, setNewBundleSectionId] = useState<number | null>(null);
  const [newBundleCount, setNewBundleCount] = useState(3);
  // #362: one child question being edited. Keyed by the pool item id, which is what the update route
  // names - the position handle cannot be used there.
  const [questionDraft, setQuestionDraft] = useState<{
    sectionId: number;
    itemId: number;
    stem: string;
    optionA: string;
    optionB: string;
    optionC: string;
    optionD: string;
    keyLabel: string;
    explanation: string;
  } | null>(null);
  const [isSavingQuestion, setIsSavingQuestion] = useState(false);
  // #362: a new child question for an existing Bundle, so a Bundle can grow past the count it was
  // created with without forking a second Bundle (which would duplicate its passage).
  const [newChild, setNewChild] = useState<{
    sectionId: number;
    groupId: number;
    stimulusId: number;
    cefrLevelCode: string;
    topicId: number;
    stem: string;
    explanation: string;
  } | null>(null);
  const [isSavingChild, setIsSavingChild] = useState(false);
  // #360: the Import & Export tab offered only export, so the import route had no caller.
  const [isImportingManifest, setIsImportingManifest] = useState(false);
  const [importResult, setImportResult] = useState<AdminImportResultDto | null>(null);

  // Create Exam
  const handleCreateExam = async () => {
    setNewExamError(null);
    setNewExamFieldErrors({});
    if (!newCertId) {
      setNewExamFieldErrors({ certId: 'Choose the Certificate this exam belongs to.' });
      return;
    }
    setIsCreatingExam(true);
    try {
      const res = await studioApi.createExam({
        certId: Number(newCertId),
        name: newName.trim(),
        durationMinutes: newDuration,
        purpose: newPurpose,
        description: newDescription.trim() || null,
      });
      setIsNewExamOpen(false);
      setNewCertId('');
      setNewName('');
      setNewPurpose('mock');
      setNewDescription('');
      loadExamList();
      setSelectedExamId(res.examId);
    } catch (err: unknown) {
      setNewExamFieldErrors(extractFieldErrors(err));
      setNewExamError(extractErrorMessage(err, 'Failed to create exam.'));
    } finally {
      setIsCreatingExam(false);
    }
  };

  // Save Metadata
  const handleSaveMetadata = async () => {
    if (!selectedExamId || !examDetail) return;
    setIsSavingMetadata(true);
    setMetadataNotice(null);
    setMetadataFieldErrors({});
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
    } catch (err: unknown) {
      setMetadataFieldErrors(extractFieldErrors(err));
      setMetadataNotice(extractErrorMessage(err, 'Failed to update metadata.'));
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
      setIngestNotice({ text: err?.detail || err?.title || 'Parsing failed.', isError: true });
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
      setIngestNotice({
        text: `${res.message} ${res.poolItemsWritten ?? 0} pool items written, ${
          res.poolItemsBound ?? 0
        } bound to this section.`,
        isError: false,
      });
      setParsedQuestions([]);
      setIngestText('');
      loadExamDetail(selectedExamId);
    } catch (err: any) {
      setIngestNotice({ text: err?.detail || err?.title || 'Commit failed.', isError: true });
    } finally {
      setIsCommitingIngest(false);
    }
  };

  // A Section card names its own Section and hands the modal everything it needs to fill it.
  const openQuickIngestFor = (section: AdminExamSectionProgressDto) => {
    // SectionProgress carries no CEFR field, so the hint is the level last used for this Section.
    setIngestSectionId(section.sectionId);
    setIngestLevel(ingestLevelBySection[section.sectionId] ?? ingestLevel);
    setIngestNotice(null);
    setParsedQuestions([]);
    setIsQuickIngestOpen(true);
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
      // #382: this used to append two hard-coded lines ("P2: 5 questions missing", "P2: Section audio
      // file missing") to every refusal, so an admin fixing a stem saw a fabricated diagnosis beside the
      // real one. The server names its own deficits; inventing more is a lie the screen cannot retract.
      setPublishRefusal({
        message: err?.detail || err?.title || 'Publish refused: the paper has unmet requirements.',
        missingItems: err?.missingItems || [],
      });
      // The refusal is authoritative, so reload the audit it was based on rather than leaving the rail
      // showing the pre-refusal state.
      loadAudit(selectedExamId);
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
            <SearchField
              aria-label="Search exams by name"
              className="w-full"
              value={examSearch}
              onChange={setExamSearch}
              onClear={() => setExamSearch('')}
            >
              <SearchField.Group>
                <SearchField.SearchIcon />
                <SearchField.Input placeholder="Search exams by name..." />
                <SearchField.ClearButton />
              </SearchField.Group>
            </SearchField>

            <Select
              aria-label="Filter status"
              className="w-full"
              value={examStatusFilter || 'all'}
              onChange={(key) => setExamStatusFilter(key === 'all' || key == null ? '' : String(key))}
            >
              <Select.Trigger>
                <Select.Value />
                <Select.Indicator />
              </Select.Trigger>
              <Select.Popover>
                <ListBox>
                  <ListBox.Item id="all" textValue="All statuses">
                    All statuses
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                  <ListBox.Item id="draft" textValue="Draft">
                    Draft
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                  <ListBox.Item id="published" textValue="Published">
                    Published
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                </ListBox>
              </Select.Popover>
            </Select>

            <Select
              aria-label="Filter purpose"
              className="w-full"
              value={examPurposeFilter || 'all'}
              onChange={(key) => setExamPurposeFilter(key === 'all' || key == null ? '' : String(key))}
            >
              <Select.Trigger>
                <Select.Value />
                <Select.Indicator />
              </Select.Trigger>
              <Select.Popover>
                <ListBox>
                  <ListBox.Item id="all" textValue="All purposes">
                    All purposes
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                  {EXAM_PURPOSES.map((p) => (
                    <ListBox.Item key={p.id} id={p.id} textValue={p.label}>
                      {p.label}
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                  ))}
                </ListBox>
              </Select.Popover>
            </Select>
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
                <Tabs.Tab id="content">
                  <Tabs.Indicator />
                  <span>Section content</span>
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

                  <Form validationBehavior="aria" className="space-y-4">
                    <TextField
                      isRequired
                      name="name"
                      value={editName}
                      onChange={(value) => {
                        setEditName(value);
                        setMetadataFieldErrors((prev) => ({ ...prev, name: '' }));
                      }}
                      isInvalid={Boolean(metadataFieldErrors.name)}
                    >
                      <Label>Exam name</Label>
                      <Input className="text-sm" />
                      {metadataFieldErrors.name && (
                        <FieldError>{metadataFieldErrors.name}</FieldError>
                      )}
                    </TextField>

                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      <NumberField
                        minValue={1}
                        maxValue={600}
                        step={1}
                        name="durationMinutes"
                        value={editDuration}
                        onChange={(value) => {
                          setEditDuration(value);
                          setMetadataFieldErrors((prev) => ({ ...prev, durationMinutes: '' }));
                        }}
                        isInvalid={Boolean(metadataFieldErrors.durationMinutes)}
                      >
                        <Label>Duration (minutes)</Label>
                        <NumberField.Group>
                          <NumberField.DecrementButton />
                          <NumberField.Input className="font-mono-tabular text-sm" />
                          <NumberField.IncrementButton />
                        </NumberField.Group>
                        {metadataFieldErrors.durationMinutes && (
                          <FieldError>{metadataFieldErrors.durationMinutes}</FieldError>
                        )}
                      </NumberField>
                      <Select
                        aria-label="Purpose"
                        className="w-full"
                        value={editPurpose}
                        onChange={(key) => {
                          if (key != null) setEditPurpose(String(key));
                        }}
                      >
                        <Label className="text-xs font-semibold text-foreground">Purpose</Label>
                        <Select.Trigger>
                          <Select.Value />
                          <Select.Indicator />
                        </Select.Trigger>
                        <Select.Popover>
                          <ListBox>
                            {EXAM_PURPOSES.map((p) => (
                              <ListBox.Item key={p.id} id={p.id} textValue={p.label}>
                                {p.label}
                                <ListBox.ItemIndicator />
                              </ListBox.Item>
                            ))}
                          </ListBox>
                        </Select.Popover>
                      </Select>
                    </div>

                    <TextField
                      name="description"
                      value={editDescription}
                      onChange={(value) => {
                        setEditDescription(value);
                        setMetadataFieldErrors((prev) => ({ ...prev, description: '' }));
                      }}
                      isInvalid={Boolean(metadataFieldErrors.description)}
                    >
                      <Label>Description</Label>
                      <TextArea rows={2} className="text-sm" />
                      {metadataFieldErrors.description && (
                        <FieldError>{metadataFieldErrors.description}</FieldError>
                      )}
                    </TextField>

                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      <TextField
                        name="sourceName"
                        value={editSourceName}
                        onChange={(value) => {
                          setEditSourceName(value);
                          setMetadataFieldErrors((prev) => ({ ...prev, sourceName: '' }));
                        }}
                        isInvalid={Boolean(metadataFieldErrors.sourceName)}
                      >
                        <Label>Source name</Label>
                        <Input placeholder="e.g. Official Practice Set 1" className="text-sm" />
                        {metadataFieldErrors.sourceName && (
                          <FieldError>{metadataFieldErrors.sourceName}</FieldError>
                        )}
                      </TextField>
                      <TextField
                        name="internalNote"
                        value={editInternalNote}
                        onChange={(value) => {
                          setEditInternalNote(value);
                          setMetadataFieldErrors((prev) => ({ ...prev, internalNote: '' }));
                        }}
                        isInvalid={Boolean(metadataFieldErrors.internalNote)}
                      >
                        <Label>Internal note</Label>
                        <Input placeholder="Author review note" className="text-sm" />
                        {metadataFieldErrors.internalNote && (
                          <FieldError>{metadataFieldErrors.internalNote}</FieldError>
                        )}
                      </TextField>
                    </div>
                  </Form>

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

                      {/* #359: a file, never a typed path. The server names the file and answers it. */}
                      <div className="pt-2 border-t border-border space-y-2">
                        {sec.modality === 'listening' && (
                          <div className="space-y-1">
                            <label
                              htmlFor={`section-audio-${sec.sectionId}`}
                              className="block text-xs font-semibold text-foreground"
                            >
                              Upload audio track
                            </label>
                            <input
                              id={`section-audio-${sec.sectionId}`}
                              type="file"
                              accept={AUDIO_MEDIA_EXTENSIONS.join(",")}
                              disabled={uploadingSectionId === sec.sectionId}
                              onChange={(e) => {
                                const file = e.target.files?.[0];
                                if (file) void handleUploadSectionAudio(sec.sectionId, file);
                                e.target.value = '';
                              }}
                              className="block w-full text-xs text-foreground file:mr-2 file:px-2 file:py-1 file:rounded file:border file:border-border file:bg-surface file:text-xs file:text-foreground"
                            />
                          </div>
                        )}

                        <div className="space-y-1">
                          <label
                            htmlFor={`section-image-${sec.sectionId}`}
                            className="block text-xs font-semibold text-foreground"
                          >
                            Upload image
                          </label>
                          <input
                            id={`section-image-${sec.sectionId}`}
                            type="file"
                            accept={IMAGE_MEDIA_EXTENSIONS.join(",")}
                            disabled={uploadingSectionId === sec.sectionId}
                            onChange={(e) => {
                              const file = e.target.files?.[0];
                              if (file) void handleUploadSectionImage(sec.sectionId, file);
                              e.target.value = '';
                            }}
                            className="block w-full text-xs text-foreground file:mr-2 file:px-2 file:py-1 file:rounded file:border file:border-border file:bg-surface file:text-xs file:text-foreground"
                          />
                        </div>

                        {uploadingSectionId === sec.sectionId && (
                          <p className="text-muted">Uploading…</p>
                        )}

                        {sectionMediaNotice?.sectionId === sec.sectionId && (
                          <Alert status={sectionMediaNotice.isError ? 'danger' : 'success'}>
                            <Alert.Indicator />
                            <Alert.Content>
                              <Alert.Description>{sectionMediaNotice.text}</Alert.Description>
                            </Alert.Content>
                          </Alert>
                        )}
                      </div>
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
              <div>
                <h3 className="text-base font-bold text-foreground">Sections</h3>
                <p className="text-xs text-muted">
                  Pick a Section, paste its questions, and the stock is written to the pool and bound
                  to this Exam.
                </p>
              </div>

              {ingestNotice && !ingestNotice.isError && (
                <Alert status="success">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Notice</Alert.Title>
                    <Alert.Description>{ingestNotice.text}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <ul aria-label="Exam sections" className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {examDetail?.sectionProgress.map((sec) => (
                  <li key={sec.sectionId}>
                    <Card className="border border-border h-full">
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
                      <Card.Content className="space-y-3 border-t border-border pt-3">
                        <div className="flex justify-between items-baseline text-xs">
                          <span className="text-muted">Questions bound</span>
                          <span className="font-mono-tabular font-medium text-foreground">
                            {sec.questionCount} / {sec.expectedQuestionCount}
                          </span>
                        </div>
                        <ProgressBar
                          aria-label={`${sec.sectionCode} bound questions`}
                          color={sec.isComplete ? 'success' : 'accent'}
                          value={sec.questionCount}
                          maxValue={Math.max(sec.expectedQuestionCount, 1)}
                          size="sm"
                        >
                          <ProgressBar.Track>
                            <ProgressBar.Fill />
                          </ProgressBar.Track>
                        </ProgressBar>
                        <Button
                          size="sm"
                          variant="secondary"
                          onPress={() => openQuickIngestFor(sec)}
                        >
                          <Sparkles className="w-3.5 h-3.5 mr-1" />
                          Paste questions
                        </Button>
                      </Card.Content>
                    </Card>
                  </li>
                ))}
              </ul>
            </Tabs.Panel>

            {/* Tab 5: Section content — Bundles of passage + audio + image with their children (#362) */}
            <Tabs.Panel id="content" className="pt-4 space-y-4">
              <div>
                <h3 className="text-base font-bold text-foreground">Section content</h3>
                <p className="text-xs text-muted">
                  A Section is Bundles. Each Bundle is its shared passage, audio track, image and
                  transcript, plus the questions that refer to it — the shape a real Exam has.
                </p>
              </div>

              {isLoadingStudio && <p className="text-sm text-muted">Loading Section content…</p>}

              {bundleNotice && (
                <Alert status={bundleNotice.isError ? 'danger' : 'success'}>
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>{bundleNotice.isError ? 'Refused' : 'Notice'}</Alert.Title>
                    <Alert.Description>{bundleNotice.text}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <ul aria-label="Section bundles" className="space-y-4">
                {(studioDetail?.sections ?? []).map((sec) => (
                  <li key={sec.sectionId}>
                    <Card className="border border-border">
                      <Card.Header className="pb-2">
                        <div className="flex flex-wrap justify-between items-center gap-2">
                          <div className="flex items-center gap-2">
                            <span className="font-mono text-xs font-bold text-accent">{sec.code}</span>
                            <Card.Title className="text-base">{sec.name}</Card.Title>
                          </div>
                          <div className="flex items-center gap-2">
                            <Chip size="sm" variant="soft" color="default">
                              {sec.groups.length} {sec.groups.length === 1 ? 'bundle' : 'bundles'}
                            </Chip>
                            <Chip
                              size="sm"
                              variant="soft"
                              color={sec.currentQuestionCount >= sec.expectedQuestionCount ? 'success' : 'warning'}
                            >
                              {sec.currentQuestionCount} / {sec.expectedQuestionCount}
                            </Chip>
                            <Button
                              size="sm"
                              variant="ghost"
                              onPress={() =>
                                setExpandedSectionId(
                                  expandedSectionId === sec.sectionId ? null : sec.sectionId,
                                )
                              }
                            >
                              {expandedSectionId === sec.sectionId ? 'Hide' : 'Show'} bundles
                            </Button>
                          </div>
                        </div>
                      </Card.Header>

                      {expandedSectionId === sec.sectionId && (
                        <Card.Content className="space-y-3 border-t border-border pt-3">
                          {sec.groups.length === 0 && (
                            <p className="text-xs text-muted">
                              No bundles yet. Create one to give this Section its passage, audio or image.
                            </p>
                          )}

                          {sec.groups.map((bundle) => (
                            <div
                              key={bundle.groupId}
                              className="p-3 rounded border border-border bg-surface space-y-2"
                            >
                              <div className="flex flex-wrap justify-between items-center gap-2">
                                <span className="text-xs font-semibold text-foreground">
                                  Bundle #{bundle.groupId} · {bundle.questions.length}{' '}
                                  {bundle.questions.length === 1 ? 'question' : 'questions'}
                                </span>
                                <div className="flex items-center gap-2">
                                  <Button
                                    size="sm"
                                    variant="secondary"
                                    onPress={() => startEditingBundle(sec.sectionId, bundle.groupId)}
                                  >
                                    Edit stimulus
                                  </Button>
                                  <Button
                                    size="sm"
                                    variant="ghost"
                                    onPress={() => void handleDeleteBundle(sec.sectionId, bundle.groupId)}
                                  >
                                    <Trash2 className="w-3.5 h-3.5" />
                                  </Button>
                                </div>
                              </div>

                              <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1 text-xs">
                                <div className="flex gap-1">
                                  <dt className="text-muted">Passage:</dt>
                                  <dd className="text-foreground">
                                    {bundle.passage ? `${bundle.passage.slice(0, 60)}…` : '—'}
                                  </dd>
                                </div>
                                <div className="flex gap-1">
                                  <dt className="text-muted">Audio:</dt>
                                  <dd className="text-foreground">
                                    {bundle.audioPath ? 'attached' : '—'}
                                  </dd>
                                </div>
                                <div className="flex gap-1">
                                  <dt className="text-muted">Image:</dt>
                                  <dd className="text-foreground">
                                    {bundle.imagePath ? bundle.imageAlt || 'alt missing' : '—'}
                                  </dd>
                                </div>
                                <div className="flex gap-1">
                                  <dt className="text-muted">Transcript:</dt>
                                  <dd className="text-foreground">
                                    {bundle.transcript ? 'present' : '—'}
                                  </dd>
                                </div>
                              </dl>

                              <ul className="space-y-1 pl-3 border-l border-separator">
                                {bundle.questions.map((q) => (
                                  <li key={q.questionId} className="text-xs text-foreground space-y-1">
                                    <div className="flex flex-wrap justify-between items-start gap-2">
                                      <span>{q.stem}</span>
                                      <div className="flex items-center gap-1">
                                        <Button
                                          size="sm"
                                          variant="ghost"
                                          aria-label={`Move question ${q.displayOrder} up`}
                                          isDisabled={q.displayOrder <= 1}
                                          onPress={() => {
                                            const ordered = [...bundle.questions]
                                              .sort((a, b) => a.displayOrder - b.displayOrder)
                                              .map((x) => x.questionId);
                                            const i = ordered.indexOf(q.questionId);
                                            if (i <= 0) return;
                                            [ordered[i - 1], ordered[i]] = [ordered[i], ordered[i - 1]];
                                            void handleReorder(sec.sectionId, ordered);
                                          }}
                                        >
                                          <ArrowUp className="w-3.5 h-3.5" />
                                        </Button>
                                        <Button
                                          size="sm"
                                          variant="ghost"
                                          aria-label={`Move question ${q.displayOrder} down`}
                                          isDisabled={q.displayOrder >= bundle.questions.length}
                                          onPress={() => {
                                            const ordered = [...bundle.questions]
                                              .sort((a, b) => a.displayOrder - b.displayOrder)
                                              .map((x) => x.questionId);
                                            const i = ordered.indexOf(q.questionId);
                                            if (i < 0 || i >= ordered.length - 1) return;
                                            [ordered[i + 1], ordered[i]] = [ordered[i], ordered[i + 1]];
                                            void handleReorder(sec.sectionId, ordered);
                                          }}
                                        >
                                          <ArrowDown className="w-3.5 h-3.5" />
                                        </Button>
                                        <Button
                                          size="sm"
                                          variant="ghost"
                                          onPress={() => startEditingQuestion(sec.sectionId, q)}
                                        >
                                          Edit question
                                        </Button>
                                      </div>
                                    </div>

                                    {questionDraft?.itemId === q.itemId && (
                                      <Form validationBehavior="aria" className="space-y-2 pt-2">
                                        <TextField
                                          isRequired
                                          name="stem"
                                          value={questionDraft.stem}
                                          onChange={(v) => setQuestionDraft({ ...questionDraft, stem: v })}
                                        >
                                          <Label className="text-xs">Stem</Label>
                                          <TextArea rows={2} className="text-xs" />
                                        </TextField>
                                        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                                          {(['A', 'B', 'C', 'D'] as const).map((label) => {
                                            const field = `option${label}` as
                                              | 'optionA'
                                              | 'optionB'
                                              | 'optionC'
                                              | 'optionD';
                                            return (
                                              <TextField
                                                key={label}
                                                name={field}
                                                value={questionDraft[field]}
                                                onChange={(v) =>
                                                  setQuestionDraft({ ...questionDraft, [field]: v })
                                                }
                                              >
                                                <Label className="text-xs">Option ({label})</Label>
                                                <Input className="text-xs" />
                                              </TextField>
                                            );
                                          })}
                                        </div>
                                        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                                          <TextField
                                            name="keyLabel"
                                            value={questionDraft.keyLabel}
                                            onChange={(v) => setQuestionDraft({ ...questionDraft, keyLabel: v })}
                                          >
                                            <Label className="text-xs">Key (A, B, C, D; or A,C for multi-select)</Label>
                                            <Input className="text-xs font-mono uppercase" />
                                          </TextField>
                                          <TextField
                                            isRequired
                                            name="explanation"
                                            value={questionDraft.explanation}
                                            onChange={(v) =>
                                              setQuestionDraft({ ...questionDraft, explanation: v })
                                            }
                                          >
                                            <Label className="text-xs">Explanation</Label>
                                            <Input className="text-xs" />
                                          </TextField>
                                        </div>
                                        <div className="flex gap-2">
                                          <Button
                                            size="sm"
                                            variant="primary"
                                            onPress={() => void handleSaveQuestion()}
                                            isDisabled={isSavingQuestion}
                                          >
                                            Save question
                                          </Button>
                                          <Button
                                            size="sm"
                                            variant="secondary"
                                            onPress={() => setQuestionDraft(null)}
                                          >
                                            Cancel
                                          </Button>
                                        </div>
                                      </Form>
                                    )}
                                  </li>
                                ))}

                                {/* #362: grow an existing Bundle instead of forking a second one,
                                    which would duplicate the passage this Bundle already owns. */}
                                {newChild?.groupId === bundle.groupId ? (
                                  <li className="pt-2">
                                    <Form validationBehavior="aria" className="space-y-2">
                                      <TextField
                                        isRequired
                                        name="newChildStem"
                                        value={newChild.stem}
                                        onChange={(v) => setNewChild({ ...newChild, stem: v })}
                                      >
                                        <Label className="text-xs">New question stem</Label>
                                        <TextArea rows={2} className="text-xs" />
                                      </TextField>
                                      <TextField
                                        name="newChildExplanation"
                                        value={newChild.explanation}
                                        onChange={(v) => setNewChild({ ...newChild, explanation: v })}
                                      >
                                        <Label className="text-xs">Explanation</Label>
                                        <Input className="text-xs" />
                                      </TextField>
                                      <div className="flex gap-2">
                                        <Button
                                          size="sm"
                                          variant="primary"
                                          onPress={() => void handleCreateChild()}
                                          isDisabled={isSavingChild || newChild.stem.trim() === ''}
                                        >
                                          Add to Bundle
                                        </Button>
                                        <Button
                                          size="sm"
                                          variant="secondary"
                                          onPress={() => setNewChild(null)}
                                        >
                                          Cancel
                                        </Button>
                                      </div>
                                    </Form>
                                  </li>
                                ) : (
                                  <li className="pt-1">
                                    <Button
                                      size="sm"
                                      variant="ghost"
                                      onPress={() =>
                                        setNewChild({
                                          sectionId: sec.sectionId,
                                          groupId: bundle.groupId,
                                          stimulusId: bundle.groupId,
                                          cefrLevelCode: 'B1',
                                          topicId: bundle.questions[0]?.topicId ?? 0,
                                          stem: '',
                                          explanation: '',
                                        })
                                      }
                                    >
                                      <Plus className="w-3.5 h-3.5 mr-1" />
                                      Add question
                                    </Button>
                                  </li>
                                )}
                              </ul>

                              {bundleDraft?.groupId === bundle.groupId && (
                                <Form validationBehavior="aria" className="space-y-2 pt-2 border-t border-border">
                                  <TextField
                                    name="passage"
                                    value={bundleDraft.passage}
                                    onChange={(v) => setBundleDraft({ ...bundleDraft, passage: v })}
                                  >
                                    <Label className="text-xs">Passage</Label>
                                    <TextArea rows={3} className="text-xs" />
                                  </TextField>
                                  <TextField
                                    name="transcript"
                                    value={bundleDraft.transcript}
                                    onChange={(v) => setBundleDraft({ ...bundleDraft, transcript: v })}
                                  >
                                    <Label className="text-xs">Transcript</Label>
                                    <TextArea rows={2} className="text-xs" />
                                  </TextField>
                                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                                    <div className="space-y-1">
                                      <span className="block text-xs font-semibold text-foreground">
                                        Bundle image
                                      </span>
                                      <input
                                        aria-label="Upload bundle image"
                                        type="file"
                                        accept={IMAGE_MEDIA_EXTENSIONS.join(",")}
                                        onChange={(e) => {
                                          const file = e.target.files?.[0];
                                          e.target.value = '';
                                          if (!file || !selectedExamId) return;
                                          void studioApi
                                            .uploadExamMedia(selectedExamId, file)
                                            .then((uploaded) =>
                                              setBundleDraft({ ...bundleDraft, imagePath: uploaded.path }),
                                            )
                                            .catch((err: unknown) =>
                                              setBundleNotice({
                                                sectionId: bundleDraft.sectionId,
                                                isError: true,
                                                text: extractErrorMessage(err, 'Image upload failed.'),
                                              }),
                                            );
                                        }}
                                        className="block w-full text-xs text-foreground file:mr-2 file:px-2 file:py-1 file:rounded file:border file:border-border file:bg-surface file:text-xs file:text-foreground"
                                      />
                                      <p className="text-xs text-muted font-mono">
                                        {bundleDraft.imagePath || 'no image'}
                                      </p>
                                    </div>
                                    <TextField
                                      name="imageAlt"
                                      value={bundleDraft.imageAlt}
                                      onChange={(v) => setBundleDraft({ ...bundleDraft, imageAlt: v })}
                                    >
                                      <Label className="text-xs">Image alt text</Label>
                                      <Input className="text-xs" />
                                    </TextField>
                                  </div>
                                  <div className="space-y-1">
                                    <span className="block text-xs font-semibold text-foreground">
                                      Bundle audio
                                    </span>
                                    <input
                                      aria-label="Upload bundle audio"
                                      type="file"
                                      accept={AUDIO_MEDIA_EXTENSIONS.join(",")}
                                      onChange={(e) => {
                                        const file = e.target.files?.[0];
                                        e.target.value = '';
                                        if (!file || !selectedExamId) return;
                                        void studioApi
                                          .uploadExamMedia(selectedExamId, file)
                                          .then((uploaded) =>
                                            setBundleDraft({ ...bundleDraft, audioPath: uploaded.path }),
                                          )
                                          .catch((err: unknown) =>
                                            setBundleNotice({
                                              sectionId: bundleDraft.sectionId,
                                              isError: true,
                                              text: extractErrorMessage(err, 'Audio upload failed.'),
                                            }),
                                          );
                                      }}
                                      className="block w-full text-xs text-foreground file:mr-2 file:px-2 file:py-1 file:rounded file:border file:border-border file:bg-surface file:text-xs file:text-foreground"
                                    />
                                    <p className="text-xs text-muted font-mono">
                                      {bundleDraft.audioPath || 'no audio'}
                                    </p>
                                    {/* #362: the slice of the section's master track this Bundle plays.
                                        Carried on every save, because the route assigns it unconditionally. */}
                                    <div className="grid grid-cols-2 gap-2">
                                      <TextField
                                        name="audioStartMs"
                                        value={bundleDraft.audioStartMs}
                                        onChange={(v) => setBundleDraft({ ...bundleDraft, audioStartMs: v })}
                                      >
                                        <Label className="text-xs">Audio start (ms)</Label>
                                        <Input className="text-xs font-mono-tabular" inputMode="numeric" />
                                      </TextField>
                                      <TextField
                                        name="audioEndMs"
                                        value={bundleDraft.audioEndMs}
                                        onChange={(v) => setBundleDraft({ ...bundleDraft, audioEndMs: v })}
                                      >
                                        <Label className="text-xs">Audio end (ms)</Label>
                                        <Input className="text-xs font-mono-tabular" inputMode="numeric" />
                                      </TextField>
                                    </div>
                                  </div>
                                  <div className="flex gap-2 pt-1">
                                    <Button
                                      size="sm"
                                      variant="primary"
                                      onPress={() => void handleSaveBundle()}
                                      isDisabled={isSavingBundle}
                                    >
                                      Save Bundle
                                    </Button>
                                    <Button
                                      size="sm"
                                      variant="secondary"
                                      onPress={() => setBundleDraft(null)}
                                    >
                                      Cancel
                                    </Button>
                                  </div>
                                </Form>
                              )}
                            </div>
                          ))}

                          <div className="pt-1">
                            {newBundleSectionId === sec.sectionId ? (
                              <div className="flex flex-wrap items-end gap-2">
                                <div className="space-y-1">
                                  <label
                                    htmlFor={`new-bundle-count-${sec.sectionId}`}
                                    className="block text-xs font-semibold text-foreground"
                                  >
                                    Questions in this Bundle
                                  </label>
                                  <input
                                    id={`new-bundle-count-${sec.sectionId}`}
                                    type="number"
                                    min={1}
                                    max={50}
                                    value={newBundleCount}
                                    onChange={(e) => setNewBundleCount(Math.max(1, Number(e.target.value) || 1))}
                                    className="w-24 px-2 py-1 rounded border border-border bg-surface text-foreground font-mono-tabular text-xs"
                                  />
                                </div>
                                <span className="text-xs text-muted pb-1">Level:</span>
                                {['A2', 'B1', 'B2', 'C1'].map((lvl) => (
                                  <Button
                                    key={lvl}
                                    size="sm"
                                    variant="secondary"
                                    onPress={() => void handleCreateBundle(sec.sectionId, lvl, newBundleCount)}
                                  >
                                    {lvl}
                                  </Button>
                                ))}
                                <Button
                                  size="sm"
                                  variant="ghost"
                                  onPress={() => setNewBundleSectionId(null)}
                                >
                                  Cancel
                                </Button>
                              </div>
                            ) : (
                              <Button
                                size="sm"
                                variant="secondary"
                                onPress={() => setNewBundleSectionId(sec.sectionId)}
                              >
                                <Plus className="w-3.5 h-3.5 mr-1" />
                                Add bundle
                              </Button>
                            )}
                          </div>
                        </Card.Content>
                      )}
                    </Card>
                  </li>
                ))}
              </ul>
            </Tabs.Panel>

            {/* Tab 6: Publish Readiness */}
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
                  {/* #360: the tab was called Import & Export and offered only export, so the client
                      method that names the import route had no caller. This is that half. */}
                  <div className="space-y-2">
                    <h4 className="text-xs font-semibold text-foreground">Import a package</h4>
                    <p className="text-xs text-muted">
                      A JSON manifest or a ZIP package writes Pool stock for this Exam&apos;s Certificate.
                      Per-line errors come back in the result.
                    </p>
                    <input
                      aria-label="Import manifest or package"
                      type="file"
                      accept=".json,.zip"
                      disabled={isImportingManifest}
                      onChange={(e) => {
                        const file = e.target.files?.[0];
                        e.target.value = '';
                        if (file) void handleImportManifest(file);
                      }}
                      className="block w-full text-xs text-foreground file:mr-2 file:px-2 file:py-1 file:rounded file:border file:border-border file:bg-surface file:text-xs file:text-foreground"
                    />
                    {isImportingManifest && <p className="text-muted">Importing…</p>}
                    {importResult && (
                      <Alert status={importResult.errorCount > 0 ? 'warning' : 'success'}>
                        <Alert.Indicator />
                        <Alert.Content>
                          <Alert.Description>
                            Batch {importResult.batchId}: {importResult.questionCount} question(s),{' '}
                            {importResult.errorCount} error(s), {importResult.warningCount} warning(s).
                          </Alert.Description>
                        </Alert.Content>
                      </Alert>
                    )}
                  </div>

                  <div className="pt-2 border-t border-border space-y-2">
                    <h4 className="text-xs font-semibold text-foreground">Export this Exam</h4>
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
              {ingestNotice && (
                <Alert status={ingestNotice.isError ? 'danger' : 'success'}>
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>
                      {ingestNotice.isError ? 'Quick ingest failed' : 'Committed'}
                    </Alert.Title>
                    <Alert.Description>{ingestNotice.text}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <Select
                  aria-label="Target section"
                  className="w-full"
                  value={ingestSectionId ? String(ingestSectionId) : undefined}
                  onChange={(key) => {
                    if (key != null) {
                      setIngestSectionId(Number(key));
                      setParsedQuestions([]);
                    }
                  }}
                >
                  <Label className="text-xs font-semibold text-foreground">Target section</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      {(examDetail?.sectionProgress ?? []).map((s) => (
                        <ListBox.Item
                          key={s.sectionId}
                          id={String(s.sectionId)}
                          textValue={`${s.sectionCode} — ${s.sectionName}`}
                        >
                          {s.sectionCode} — {s.sectionName}
                          <ListBox.ItemIndicator />
                        </ListBox.Item>
                      ))}
                    </ListBox>
                  </Select.Popover>
                </Select>

                <Select
                  aria-label="CEFR level"
                  className="w-full"
                  value={ingestLevel}
                  onChange={(key) => {
                    if (key != null) {
                      const level = String(key);
                      setIngestLevel(level);
                      setIngestLevelBySection((prev) => ({ ...prev, [ingestSectionId]: level }));
                    }
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

              {ingestSample && (
                <Disclosure>
                  <Disclosure.Heading>
                    <Disclosure.Trigger className="text-xs font-semibold text-accent">
                      {ingestSample.formatDescription}
                      <Disclosure.Indicator className="w-4 h-4" />
                    </Disclosure.Trigger>
                  </Disclosure.Heading>
                  <Disclosure.Content>
                    <Disclosure.Body>
                      <pre className="p-3 rounded-md border border-border bg-surface-secondary text-foreground font-mono text-xs whitespace-pre-wrap overflow-x-auto">
                        {ingestSample.template}
                      </pre>
                    </Disclosure.Body>
                  </Disclosure.Content>
                </Disclosure>
              )}

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

              <Form validationBehavior="aria" className="space-y-4">
                <Select
                  isRequired
                  aria-label="Certificate"
                  className="w-full"
                  placeholder="Choose a certificate"
                  value={newCertId}
                  onChange={(key) => {
                    setNewCertId(key == null ? '' : String(key));
                    setNewExamFieldErrors((prev) => ({ ...prev, certId: '' }));
                  }}
                  isInvalid={Boolean(newExamFieldErrors.certId)}
                >
                  <Label className="text-xs font-semibold text-foreground">Certificate</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      {certificates.map((cert) => (
                        <ListBox.Item
                          key={cert.certId}
                          id={String(cert.certId)}
                          textValue={`${cert.code} — ${cert.name}`}
                        >
                          {cert.code} — {cert.name}
                          <ListBox.ItemIndicator />
                        </ListBox.Item>
                      ))}
                    </ListBox>
                  </Select.Popover>
                  {newExamFieldErrors.certId && (
                    <FieldError>{newExamFieldErrors.certId}</FieldError>
                  )}
                </Select>

                <TextField
                  isRequired
                  name="name"
                  value={newName}
                  onChange={(value) => {
                    setNewName(value);
                    setNewExamFieldErrors((prev) => ({ ...prev, name: '' }));
                  }}
                  isInvalid={Boolean(newExamFieldErrors.name)}
                >
                  <Label>Exam name</Label>
                  <Input placeholder="e.g. TOEIC Official Mock Exam 2" className="text-sm" />
                  {newExamFieldErrors.name && (
                    <FieldError>{newExamFieldErrors.name}</FieldError>
                  )}
                </TextField>

                <div className="grid grid-cols-2 gap-3">
                  <NumberField
                    minValue={1}
                    maxValue={600}
                    step={1}
                    name="durationMinutes"
                    value={newDuration}
                    onChange={(value) => {
                      setNewDuration(value);
                      setNewExamFieldErrors((prev) => ({ ...prev, durationMinutes: '' }));
                    }}
                    isInvalid={Boolean(newExamFieldErrors.durationMinutes)}
                  >
                    <Label>Duration (mins)</Label>
                    <NumberField.Group>
                      <NumberField.DecrementButton />
                      <NumberField.Input className="font-mono-tabular text-sm" />
                      <NumberField.IncrementButton />
                    </NumberField.Group>
                    {newExamFieldErrors.durationMinutes && (
                      <FieldError>{newExamFieldErrors.durationMinutes}</FieldError>
                    )}
                  </NumberField>
                  <Select
                    aria-label="Purpose"
                    className="w-full"
                    value={newPurpose}
                    onChange={(key) => {
                      if (key != null) setNewPurpose(String(key));
                    }}
                  >
                    <Label className="text-xs font-semibold text-foreground">Purpose</Label>
                    <Select.Trigger>
                      <Select.Value />
                      <Select.Indicator />
                    </Select.Trigger>
                    <Select.Popover>
                      <ListBox>
                        {EXAM_PURPOSES.map((p) => (
                          <ListBox.Item key={p.id} id={p.id} textValue={p.label}>
                            {p.label}
                            <ListBox.ItemIndicator />
                          </ListBox.Item>
                        ))}
                      </ListBox>
                    </Select.Popover>
                  </Select>
                </div>

                <TextField
                  name="description"
                  value={newDescription}
                  onChange={(value) => {
                    setNewDescription(value);
                    setNewExamFieldErrors((prev) => ({ ...prev, description: '' }));
                  }}
                  isInvalid={Boolean(newExamFieldErrors.description)}
                >
                  <Label>Description</Label>
                  <TextArea rows={2} className="text-sm" />
                  {newExamFieldErrors.description && (
                    <FieldError>{newExamFieldErrors.description}</FieldError>
                  )}
                </TextField>
              </Form>
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
