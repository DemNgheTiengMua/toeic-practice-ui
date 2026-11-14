/**
 * Admin Studio API Client
 * Wraps typed endpoints for Certificates, Exams Studio, and Question Bank Studio.
 */

import { apiFetch } from '../../../lib/api';
import type {
  AdminCertificateSummaryDto,
  CertificateBlueprintDetailDto,
  CertificateBlueprintPresetDto,
  SaveCertificateWizardRequest,
  SaveCertificateWizardResultDto,
  AdminScoreConversionDto,
  UpdateScoreConversionRequest,
  ScorePreviewRequestDto,
  ScorePreviewResultDto,
  RubricStudioDto,
  SaveRubricRequest,
  RunRubricLadderRequest,
  RubricLadderDto,
  AdminExamListSummaryDto,
  AdminExamDetailDto,
  CreateAdminExamRequest,
  UpdateAdminExamRequest,
  SaveExamSectionAudioRequest,
  ExamAuditResultDto,
  AdminPublishExamResultDto,
  ExamStudioDetailDto,
  ExamAssemblyDto,
  AdminExamQuestionsDto,
  CreateQuestionRequest,
  UpdateQuestionRequest,
  CreateQuestionGroupRequest,
  UpdateQuestionGroupRequest,
  ReorderSectionItemsRequest,
  CreateExamClusterRequest,
  QuickIngestSampleTemplateDto,
  QuickIngestParseRequest,
  QuickIngestParseResultDto,
  QuickIngestCommitRequest,
  QuickIngestCommitResultDto,
  BindPoolItemRequest,
  ExamItemBindingDto,
  AdminCoverageMatrixDto,
  CreatePoolItemRequest,
  UpdatePoolItemRequest,
  PoolItemDto,
  StimulusStudioOptionsDto,
  StimulusDraftRequest,
  StimulusDraftAnalysisDto,
  StimulusBundleDto,
  ProductiveStudioOptionsDto,
  ProductiveTaskDraftRequest,
  ProductiveTaskAnalysisDto,
  ProductiveTaskBundleDto,
  IngestionBatchListDto,
  IngestionBatchDetailDto,
  IngestionRecordDto,
  IngestionUploadResultDto,
  AdminImportResultDto,
  AdminLessonSummaryDto,
  AdminLessonFormOptionsDto,
  AdminLessonDetailDto,
  AdminLessonUpsertDto,
  LessonManifestDto,
  AdminLessonImportResultDto,
} from './types';

export const studioApi = {
  // Certificates & Blueprints
  getCertificates: () => apiFetch<AdminCertificateSummaryDto[]>('/api/admin/certificates'),
  getCertificatePresets: () =>
    apiFetch<CertificateBlueprintPresetDto[]>('/api/admin/certificates/presets'),
  getCertificateBlueprint: (certId: number) =>
    apiFetch<CertificateBlueprintDetailDto>(`/api/admin/certificates/${certId}/blueprint`),
  saveCertificateWizard: (body: SaveCertificateWizardRequest) =>
    apiFetch<SaveCertificateWizardResultDto>('/api/admin/certificates/wizard', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Score Conversion & Preview
  getScoreConversion: (params?: { certCode?: string; examId?: number }) => {
    const sp = new URLSearchParams();
    if (params?.certCode) sp.set('certCode', params.certCode);
    if (params?.examId != null) sp.set('examId', String(params.examId));
    const q = sp.toString();
    return apiFetch<AdminScoreConversionDto>(`/api/admin/score-conversion${q ? `?${q}` : ''}`);
  },
  updateScoreConversion: (certId: number, body: UpdateScoreConversionRequest) =>
    apiFetch<AdminScoreConversionDto>(`/api/admin/score-conversion/${certId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  previewScore: (body: ScorePreviewRequestDto) =>
    apiFetch<ScorePreviewResultDto>('/api/admin/score-conversion/preview', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Rubric Studio
  getRubricStudio: (certId: number, skillId: number) =>
    apiFetch<RubricStudioDto>(`/api/admin/studio/rubric?certId=${certId}&skillId=${skillId}`),
  saveRubric: (body: SaveRubricRequest) =>
    apiFetch<RubricStudioDto>('/api/admin/studio/rubric', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  runRubricLadder: (body: RunRubricLadderRequest) =>
    apiFetch<RubricLadderDto>('/api/admin/studio/rubric/ladder', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  lockRubric: (body: RunRubricLadderRequest) =>
    apiFetch<RubricLadderDto>('/api/admin/studio/rubric/lock', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Exams Authoring
  getExams: (params?: {
    q?: string;
    status?: string;
    certId?: number;
    purpose?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const sp = new URLSearchParams();
    sp.set('page', String(params?.page ?? 1));
    sp.set('pageSize', String(params?.pageSize ?? 25));
    if (params?.q) sp.set('q', params.q);
    if (params?.status) sp.set('status', params.status);
    if (params?.certId != null) sp.set('certId', String(params.certId));
    if (params?.purpose) sp.set('purpose', params.purpose);
    return apiFetch<AdminExamListSummaryDto>(`/api/admin/exams?${sp.toString()}`);
  },
  getExamDetail: (examId: number) => apiFetch<AdminExamDetailDto>(`/api/admin/exams/${examId}`),
  createExam: (body: CreateAdminExamRequest) =>
    apiFetch<{ examId: number }>('/api/admin/exams', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateExam: (examId: number, body: UpdateAdminExamRequest) =>
    apiFetch<void>(`/api/admin/exams/${examId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteExam: (examId: number) =>
    apiFetch<void>(`/api/admin/exams/${examId}`, {
      method: 'DELETE',
    }),
  publishExam: (examId: number) =>
    apiFetch<AdminPublishExamResultDto>(`/api/admin/exams/${examId}/publish`, {
      method: 'POST',
      body: JSON.stringify({}),
    }),
  withdrawExam: (examId: number) =>
    apiFetch<void>(`/api/admin/exams/${examId}/withdraw`, {
      method: 'POST',
      body: JSON.stringify({}),
    }),
  saveExamAudio: (examId: number, body: SaveExamSectionAudioRequest) =>
    apiFetch<void>(`/api/admin/exams/${examId}/audio`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  getExamAudit: (examId: number) => apiFetch<ExamAuditResultDto>(`/api/admin/exams/${examId}/audit`),
  getExamStudio: (examId: number) =>
    apiFetch<ExamStudioDetailDto>(`/api/admin/exams/${examId}/studio`),
  getExamAssembly: (examId: number) =>
    apiFetch<ExamAssemblyDto>(`/api/admin/exams/${examId}/assembly`),
  getExamQuestions: (examId: number) =>
    apiFetch<AdminExamQuestionsDto>(`/api/admin/exams/${examId}/questions`),
  createQuestion: (examId: number, body: CreateQuestionRequest) =>
    apiFetch<{ questionId: number }>(`/api/admin/exams/${examId}/questions`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  saveQuestion: (examId: number, questionId: number, body: UpdateQuestionRequest) =>
    apiFetch<void>(`/api/admin/exams/${examId}/questions/${questionId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteQuestion: (examId: number, questionId: number) =>
    apiFetch<void>(`/api/admin/exams/${examId}/questions/${questionId}`, {
      method: 'DELETE',
    }),
  createQuestionGroup: (examId: number, body: CreateQuestionGroupRequest) =>
    apiFetch<{ groupId: number }>(`/api/admin/exams/${examId}/studio/groups`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateQuestionGroup: (examId: number, groupId: number, body: UpdateQuestionGroupRequest) =>
    apiFetch<void>(`/api/admin/exams/${examId}/studio/groups/${groupId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteQuestionGroup: (examId: number, groupId: number) =>
    apiFetch<void>(`/api/admin/exams/${examId}/studio/groups/${groupId}`, {
      method: 'DELETE',
    }),
  reorderExamItems: (examId: number, body: ReorderSectionItemsRequest) =>
    apiFetch<void>(`/api/admin/exams/${examId}/reorder`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  createExamCluster: (examId: number, body: CreateExamClusterRequest) =>
    apiFetch<{ groupId: number; questionIds: number[] }>(`/api/admin/exams/${examId}/clusters`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  getQuickIngestSample: (sectionId: number) =>
    apiFetch<QuickIngestSampleTemplateDto>(`/api/admin/sections/${sectionId}/quick-ingest/sample`),
  parseQuickIngest: (examId: number, body: QuickIngestParseRequest) =>
    apiFetch<QuickIngestParseResultDto>(`/api/admin/exams/${examId}/quick-ingest/parse`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  commitQuickIngest: (examId: number, body: QuickIngestCommitRequest) =>
    apiFetch<QuickIngestCommitResultDto>(`/api/admin/exams/${examId}/quick-ingest/commit`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  bindPoolItem: (examId: number, body: BindPoolItemRequest) =>
    apiFetch<ExamItemBindingDto>(`/api/admin/exams/${examId}/bindings`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  uploadExamManifest: async (certId: number, file: File) => {
    const formData = new FormData();
    formData.append('certId', String(certId));
    formData.append('file', file);
    return apiFetch<AdminImportResultDto>('/api/admin/imports', {
      method: 'POST',
      body: formData,
    });
  },

  // Question Bank & Coverage
  getCoverageMatrix: () => apiFetch<AdminCoverageMatrixDto>('/api/admin/coverage'),
  createPoolItem: (body: CreatePoolItemRequest) =>
    apiFetch<PoolItemDto>('/api/admin/items', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updatePoolItem: (itemId: number, body: UpdatePoolItemRequest) =>
    apiFetch<PoolItemDto>(`/api/admin/items/${itemId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  // Stimulus Studio
  getStimulusOptions: (skill: string) =>
    apiFetch<StimulusStudioOptionsDto>(
      `/api/admin/studio/stimulus/options?skill=${encodeURIComponent(skill)}`,
    ),
  analyseStimulus: (body: StimulusDraftRequest) =>
    apiFetch<StimulusDraftAnalysisDto>('/api/admin/studio/stimulus/analyse', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  saveStimulus: (body: StimulusDraftRequest) =>
    apiFetch<StimulusBundleDto>('/api/admin/studio/stimulus', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Productive Studio
  getProductiveOptions: (skill: string) =>
    apiFetch<ProductiveStudioOptionsDto>(
      `/api/admin/studio/productive/options?skill=${encodeURIComponent(skill)}`,
    ),
  analyseProductive: (body: ProductiveTaskDraftRequest) =>
    apiFetch<ProductiveTaskAnalysisDto>('/api/admin/studio/productive/analyse', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  saveProductive: (body: ProductiveTaskDraftRequest) =>
    apiFetch<ProductiveTaskBundleDto>('/api/admin/studio/productive', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Ingestion Workbench
  getIngestionBatches: () => apiFetch<IngestionBatchListDto>('/api/admin/ingestion'),
  getIngestionBatch: (batchId: number, rule?: string) =>
    apiFetch<IngestionBatchDetailDto>(
      rule
        ? `/api/admin/ingestion/${batchId}?rule=${encodeURIComponent(rule)}`
        : `/api/admin/ingestion/${batchId}`,
    ),
  clearWorkbenchItem: (batchId: number, itemId: number) =>
    apiFetch<void>(`/api/admin/ingestion/${batchId}/items/${itemId}/clear`, {
      method: 'POST',
    }),
  discardWorkbenchRecord: (batchId: number, line: number, reason?: string) =>
    apiFetch<void>(`/api/admin/ingestion/${batchId}/records/${line}/discard`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  resubmitWorkbenchRecord: (batchId: number, line: number, recordJson: string) =>
    apiFetch<IngestionRecordDto>(`/api/admin/ingestion/${batchId}/records/${line}/resubmit`, {
      method: 'POST',
      body: JSON.stringify({ recordJson }),
    }),
  uploadPoolManifest: async (skill: string, file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return apiFetch<IngestionUploadResultDto>(
      `/api/admin/ingestion/upload?skill=${encodeURIComponent(skill)}`,
      {
        method: 'POST',
        body: formData,
      },
    );
  },

  // Micro-Lessons
  getLessons: (params?: { certId?: number; skillId?: number }) => {
    const sp = new URLSearchParams();
    if (params?.certId != null) sp.set('certId', String(params.certId));
    if (params?.skillId != null) sp.set('skillId', String(params.skillId));
    const q = sp.toString();
    return apiFetch<AdminLessonSummaryDto[]>(`/api/admin/lessons${q ? `?${q}` : ''}`);
  },
  getLessonFormOptions: (certId?: number) => {
    const q = certId != null ? `?certId=${certId}` : '';
    return apiFetch<AdminLessonFormOptionsDto>(`/api/admin/lessons/options${q}`);
  },
  getLessonDetail: (lessonId: number) =>
    apiFetch<AdminLessonDetailDto>(`/api/admin/lessons/${lessonId}`),
  createLesson: (body: AdminLessonUpsertDto) =>
    apiFetch<AdminLessonDetailDto>('/api/admin/lessons', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateLesson: (lessonId: number, body: AdminLessonUpsertDto) =>
    apiFetch<AdminLessonDetailDto>(`/api/admin/lessons/${lessonId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteLesson: (lessonId: number) =>
    apiFetch<void>(`/api/admin/lessons/${lessonId}`, {
      method: 'DELETE',
    }),
  getLessonTemplate: () => apiFetch<LessonManifestDto>('/api/admin/lessons/template'),
  importLessons: async (certId: number, file: File, overwrite = false) => {
    const formData = new FormData();
    formData.append('file', file);
    return apiFetch<AdminLessonImportResultDto>(
      `/api/admin/lessons/import?certId=${certId}&overwrite=${overwrite}`,
      {
        method: 'POST',
        body: formData,
      },
    );
  },
};
