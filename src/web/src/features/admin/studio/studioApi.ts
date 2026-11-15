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
  AdminExamListSummaryDto,
  AdminExamDetailDto,
  CreateAdminExamRequest,
  UpdateAdminExamRequest,
  SaveExamSectionAudioRequest,
  AdminExamMediaUploadResultDto,
  ExamAuditResultDto,
  AdminPublishExamResultDto,
  ExamStudioDetailDto,
  ExamAssemblyDto,
  AdminExamQuestionsDto,
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
  // #360: getCertificateBlueprint had no caller. The wizard owns a certificate's configuration, and it
  // saves through saveCertificateWizard below rather than loading a blueprint to edit.
  saveCertificateWizard: (body: SaveCertificateWizardRequest) =>
    apiFetch<SaveCertificateWizardResultDto>('/api/admin/certificates/wizard', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // #360: score conversion and the rubric ladder had no route on this server. The wizard owns
  // scoring configuration now, so the client's half is gone rather than pointing at a 404.

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
  // #360: deleteExam had no caller. Withdraw is the reversible act an admin screen offers; a hard
  // delete of a paper a learner may have sat is not something this client should be able to reach.
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
  /**
   * #359: one file to the exam's own media directory. The server names the file and answers the path
   * to store, so nothing here composes a path. Images and audio share this door; there is no video
   * door, because the schema has no column for one.
   */
  uploadExamMedia: async (examId: number, file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return apiFetch<AdminExamMediaUploadResultDto>(`/api/admin/exams/${examId}/media`, {
      method: 'POST',
      body: formData,
    });
  },
  getExamAudit: (examId: number) => apiFetch<ExamAuditResultDto>(`/api/admin/exams/${examId}/audit`),
  getExamStudio: (examId: number) =>
    apiFetch<ExamStudioDetailDto>(`/api/admin/exams/${examId}/studio`),
  getExamAssembly: (examId: number) =>
    apiFetch<ExamAssemblyDto>(`/api/admin/exams/${examId}/assembly`),
  // #360: getExamQuestions had no caller. getExamStudio above returns the same paper shaped as the
  // editor's Bundle tree, which is what every screen reads.
  // #360: createQuestion / saveQuestion / deleteQuestion / createQuestionGroup are GONE. The server
  // maps only GET /exams/{id}/questions; the write routes were retired with the legacy question model
  // (#265) and a caller got a 404. Authoring writes the pool (POST /items) and the Bundle
  // (PUT /studio/groups/{id}); those are below.
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

  // #360: Stimulus Studio, Productive Studio, the rubric ladder and the ingestion workbench were
  // retired with the legacy pool. Their routes do not exist, so neither does this client's half.
  // Pool authoring is POST /items plus POST /exams/{id}/bindings, which are above.

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
