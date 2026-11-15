import { describe, expect, it } from 'vitest';
import { studioApi } from '../studioApi';

/**
 * #360: the admin client must not name a route the server does not have. Stimulus Studio,
 * Productive Studio, the rubric ladder and the ingestion workbench were retired with the legacy
 * pool; their routes are gone, so their client methods had to go too. A demo click on one of them
 * was a 404.
 *
 * This asserts absence on purpose: re-adding a call to a retired route fails here rather than in
 * front of an audience.
 */
const RETIRED_CLIENT_METHODS = [
  'getStimulusOptions',
  'analyseStimulus',
  'saveStimulus',
  'getProductiveOptions',
  'analyseProductive',
  'saveProductive',
  'getRubricStudio',
  'saveRubric',
  'runRubricLadder',
  'lockRubric',
  'getScoreConversion',
  'updateScoreConversion',
  'previewScore',
  'getIngestionBatch',
  'clearWorkbenchItem',
  'discardWorkbenchRecord',
  'resubmitWorkbenchRecord',
  'uploadPoolManifest',
  // These four named POST/PUT/DELETE /exams/{id}/questions, which the server does not map: the write
  // routes were retired with the legacy question model. Only GET survives there.
  'createQuestion',
  'saveQuestion',
  'deleteQuestion',
  'createQuestionGroup',
];

describe('admin studio client surface (#360)', () => {
  it.each(RETIRED_CLIENT_METHODS)('no longer exposes %s', (method) => {
    expect((studioApi as Record<string, unknown>)[method]).toBeUndefined();
  });

  it('still exposes every method a live admin screen calls', () => {
    // The complement of the list above: deleting the dead half must not have taken a live one with it.
    for (const method of [
      'getCertificates',
      'getCertificatePresets',
      'saveCertificateWizard',
      'getExams',
      'getExamDetail',
      'createExam',
      'updateExam',
      'publishExam',
      'withdrawExam',
      'getExamAudit',
      'getExamAssembly',
      'bindPoolItem',
      'parseQuickIngest',
      'commitQuickIngest',
      'getCoverageMatrix',
      'createPoolItem',
      'getLessons',
      // Kept for the Section content workspace (#362): each names a route that exists today.
      'getExamStudio',
      'getExamQuestions',
      'createExamCluster',
      'updateQuestionGroup',
      'deleteQuestionGroup',
      'updatePoolItem',
      'saveExamAudio',
      'getQuickIngestSample',
      'uploadExamMedia',
    ]) {
      expect(typeof (studioApi as Record<string, unknown>)[method]).toBe('function');
    }
  });
});
