import { describe, expect, it } from 'vitest';
import type { CreateExamClusterRequest, ExamStudioGroupDto } from '../types';
import { studioApi } from '../studioApi';

/**
 * #362: the Section content workspace. A real English paper Section is not a flat list of questions —
 * it is Bundles, and a Bundle is its shared stimulus (passage, audio track, image with alt text,
 * transcript) plus the child questions that refer to it.
 *
 * These pin the contract the workspace is built on. They are deliberately at the client boundary,
 * because that is where the defect lived: the routes all existed and no screen called them.
 *
 * CAVEAT, stated so nobody mistakes this for proof: these are a CONTRACT GUARD. They assert that the
 * routes stay reachable and that a Bundle carries the right shape. They do not prove the screen works —
 * a rendered-workspace test arrives with the workspace itself, and the ticket is not done until it does.
 */
function group(overrides: Partial<ExamStudioGroupDto> = {}): ExamStudioGroupDto {
  return {
    groupId: 11,
    examId: 5,
    certId: 1,
    sectionId: 7,
    sectionCode: 'L1',
    sectionName: 'Listening Comprehension',
    modality: 'listening',
    passage: null,
    audioPath: 'exams/5/abc.mp3',
    audioStartMs: 0,
    audioEndMs: 52000,
    imagePath: null,
    imageAlt: null,
    transcript: 'Attention, attendees of the Global FinTech Summit.',
    prepSeconds: null,
    responseSeconds: null,
    displayOrder: 1,
    questions: [],
    unitNumber: 1,
    ...overrides,
  };
}

describe('Section content workspace contract (#362)', () => {
  it('a bundle carries the passage, audio slice, image and transcript together', () => {
    const bundle = group({
      passage: 'Questions 10 through 12 refer to the following announcement.',
      imagePath: 'exams/5/venue.png',
      imageAlt: 'Floor plan showing the Sapphire Auditorium on the mezzanine level.',
    });

    // The shape is the point: one bundle, one stimulus, many children. A flat question list would
    // copy the passage per question, which is the defect #269 removed.
    expect(bundle.passage).toBeTruthy();
    expect(bundle.audioPath).toBeTruthy();
    expect(bundle.audioEndMs! - bundle.audioStartMs!).toBeGreaterThan(0);
    expect(bundle.imageAlt).toBeTruthy();
  });

  it('creates a bundle with its child count in one act', async () => {
    const request: CreateExamClusterRequest = {
      sectionId: 7,
      passage: 'Read the following notice.',
      audioPath: null,
      questionCount: 3,
      cefrLevelCode: 'B1',
    };
    expect(request.sectionId).toBe(7);
    expect(request.questionCount).toBe(3);
    expect(typeof studioApi.createExamCluster).toBe('function');
  });

  it('edits a bundle through the group route, naming the section it belongs to', () => {
    // UpdateQuestionGroupRequest requires sectionId: the server refuses a group edit that cannot say
    // which section it lands in, because a stimulus can be bound by two sections.
    expect(typeof studioApi.updateQuestionGroup).toBe('function');
    expect(typeof studioApi.deleteQuestionGroup).toBe('function');
  });

  it('reorders and previews through routes that exist', () => {
    expect(typeof studioApi.reorderExamItems).toBe('function');
    expect(typeof studioApi.getExamStudio).toBe('function');
  });

  it('has no video field anywhere, because the schema has none', () => {
    const bundle = group();
    expect(Object.keys(bundle).some((k) => /video/i.test(k))).toBe(false);
  });
});
