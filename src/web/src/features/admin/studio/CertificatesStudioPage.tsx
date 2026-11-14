import React, { useState, useEffect } from 'react';
import {
  Button,
  Card,
  Chip,
  Modal,
  Alert,
} from '@heroui/react';
import {
  Award,
  ShieldCheck,
  Plus,
} from 'lucide-react';
import { useAuth } from '../../../context/AuthContext';
import { canManageCatalog, hasCapability, STAFF_PERMISSIONS } from '../../../lib/security';
import { studioApi } from './studioApi';
import { formatModalityLabel } from './studioLabels';
import type {
  AdminCertificateSummaryDto,
  CertificateBlueprintPresetDto,
  SaveCertificateWizardRequest,
  SaveSkillWizardDto,
  SaveScoreBandWizardDto,
  SaveGradingCriterionWizardDto,
} from './types';

export function CertificatesStudioPage() {
  const { user } = useAuth();

  // Capabilities
  const isCatalogManager = canManageCatalog(user);
  const isBlueprintDesigner = hasCapability(user, STAFF_PERMISSIONS.BlueprintDesign);

  // Catalog State
  const [certificates, setCertificates] = useState<AdminCertificateSummaryDto[]>([]);
  const [presets, setPresets] = useState<CertificateBlueprintPresetDto[]>([]);
  const [isLoadingCerts, setIsLoadingCerts] = useState(false);
  const [certError, setCertError] = useState<string | null>(null);

  // Wizard Modal State (3-step template flow)
  const [isWizardOpen, setIsWizardOpen] = useState(false);
  const [wizardStep, setWizardStep] = useState<1 | 2 | 3>(1);
  const [selectedPresetCode, setSelectedPresetCode] = useState<string>('');
  const [wizardCode, setWizardCode] = useState('');
  const [wizardName, setWizardName] = useState('');
  const [wizardDescription, setWizardDescription] = useState('');
  const [wizardRule, setWizardRule] = useState('SUM');
  const [wizardIsActive, setWizardIsActive] = useState(true);
  const [wizardSkills, setWizardSkills] = useState<SaveSkillWizardDto[]>([]);
  const [wizardBands, setWizardBands] = useState<SaveScoreBandWizardDto[]>([]);
  const [wizardCriteria, setWizardCriteria] = useState<SaveGradingCriterionWizardDto[]>([]);
  const [isSavingWizard, setIsSavingWizard] = useState(false);
  const [wizardError, setWizardError] = useState<string | null>(null);

  // Initial load
  useEffect(() => {
    if (!isCatalogManager) return;
    loadCertificates();
    loadPresets();
  }, [isCatalogManager]);

  const loadCertificates = async () => {
    setIsLoadingCerts(true);
    setCertError(null);
    try {
      const data = await studioApi.getCertificates();
      setCertificates(data);
    } catch (err: any) {
      setCertError(err?.detail || err?.title || 'Failed to load certificates catalog.');
    } finally {
      setIsLoadingCerts(false);
    }
  };

  const loadPresets = async () => {
    try {
      const data = await studioApi.getCertificatePresets();
      setPresets(data);
    } catch {
      // Non-fatal
    }
  };

  // Preset Selection in Wizard
  const handlePresetChange = (presetCode: string) => {
    setSelectedPresetCode(presetCode);
    const found = presets.find((p) => p.presetCode === presetCode);
    if (!found) return;

    setWizardRule(found.scoreAggregationRule);
    const mappedSkills: SaveSkillWizardDto[] = found.skills.map((s) => ({
      code: s.code,
      name: s.name,
      modality: s.modality,
      displayOrder: s.displayOrder,
      durationMinutes: s.durationMinutes,
      sections: s.sections.map((sec) => ({
        code: sec.code,
        name: sec.name,
        displayOrder: sec.displayOrder,
        scoreWeight: sec.scoreWeight,
        navigationMode: sec.navigationMode,
        layoutType: sec.layoutType,
        optionCount: sec.optionCount,
        clusterSize: sec.clusterSize,
        unitCount: sec.unitCount,
        hideOptionText: sec.hideOptionText,
        requirePassage: sec.requirePassage,
        requireAudio: sec.requireAudio,
        requireImage: sec.requireImage,
        requiredKeywordCount: sec.requiredKeywordCount,
        expectedQuestionCount: sec.expectedQuestionCount,
        allowedComponentTypesJson: sec.allowedComponentTypesJson,
        toolsConfigJson: sec.toolsConfigJson,
      })),
    }));
    setWizardSkills(mappedSkills);

    if (wizardBands.length === 0) {
      setWizardBands([
        { code: 'A2', name: 'Elementary (A2)', minTotal: 0, maxTotal: 40, displayOrder: 1 },
        { code: 'B1', name: 'Intermediate (B1)', minTotal: 41, maxTotal: 65, displayOrder: 2 },
        { code: 'B2', name: 'Upper-Intermediate (B2)', minTotal: 66, maxTotal: 85, displayOrder: 3 },
        { code: 'C1', name: 'Advanced (C1)', minTotal: 86, maxTotal: 100, displayOrder: 4 },
      ]);
    }

    const productiveSkills = mappedSkills.filter(
      (s) => s.modality === 'speaking' || s.modality === 'writing',
    );
    setWizardCriteria(
      productiveSkills.map((s, idx) => ({
        skillCode: s.code,
        code: `${s.code}_OVERALL`,
        name: `${s.name} Overall Quality`,
        maxScore: 10,
        weight: 1,
        displayOrder: idx + 1,
        rubricDescription: `Flat grading criterion for ${s.name}.`,
      })),
    );
  };

  const updateSectionField = (
    skillIdx: number,
    secIdx: number,
    field: 'code' | 'name' | 'expectedQuestionCount',
    value: string | number,
  ) => {
    setWizardSkills((prev) =>
      prev.map((sk, sIndex) =>
        sIndex !== skillIdx
          ? sk
          : {
              ...sk,
              sections: sk.sections.map((sec, xIndex) =>
                xIndex !== secIdx ? sec : { ...sec, [field]: value },
              ),
            },
      ),
    );
  };

  const addSectionToSkill = (skillIdx: number) => {
    setWizardSkills((prev) =>
      prev.map((sk, sIndex) => {
        if (sIndex !== skillIdx) return sk;
        const nextOrder = sk.sections.length + 1;
        return {
          ...sk,
          sections: [
            ...sk.sections,
            {
              code: `${sk.code.slice(0, 1)}P${nextOrder}`,
              name: `${sk.name} Part ${nextOrder}`,
              displayOrder: nextOrder,
              scoreWeight: 1,
              navigationMode: sk.modality === 'listening' ? 'audio_driven' : 'full_sandbox',
              layoutType: 'standard_vertical',
              optionCount: 4,
              clusterSize: 1,
              unitCount: 1,
              hideOptionText: false,
              requirePassage: sk.modality === 'reading',
              requireAudio: sk.modality === 'listening',
              requireImage: false,
              requiredKeywordCount: 0,
              expectedQuestionCount: 10,
            },
          ],
        };
      }),
    );
  };

  // Save Blueprint Wizard
  const handleSaveWizard = async () => {
    setWizardError(null);
    if (!wizardCode.trim() || !wizardName.trim()) {
      setWizardError('Certificate Code and Name are required.');
      return;
    }

    setIsSavingWizard(true);
    try {
      const payload: SaveCertificateWizardRequest = {
        code: wizardCode.trim().toUpperCase(),
        name: wizardName.trim(),
        description: wizardDescription.trim() || null,
        scoreAggregationRule: wizardRule,
        presetCode: selectedPresetCode || null,
        isActive: wizardIsActive,
        skills: wizardSkills.length > 0 ? wizardSkills : [
          {
            code: 'LISTENING',
            name: 'Listening',
            modality: 'listening',
            displayOrder: 1,
            sections: [
              {
                code: 'PART1',
                name: 'Section 1',
                displayOrder: 1,
                scoreWeight: 1,
                navigationMode: 'full_sandbox',
                layoutType: 'standard_vertical',
                optionCount: 4,
                clusterSize: 1,
                unitCount: 1,
                hideOptionText: false,
                requirePassage: false,
                requireAudio: false,
                requireImage: false,
                requiredKeywordCount: 0,
                expectedQuestionCount: 10,
              },
            ],
          },
        ],
        bands: wizardBands,
        criteria: wizardCriteria,
      };

      await studioApi.saveCertificateWizard(payload);
      setIsWizardOpen(false);
      loadCertificates();
    } catch (err: any) {
      setWizardError(err?.detail || err?.title || 'Failed to save certificate blueprint.');
    } finally {
      setIsSavingWizard(false);
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
              You do not have permission to access the Certificates Studio. Staff capability{' '}
              <code className="font-mono text-xs">assessment.blueprint.design</code> or{' '}
              <code className="font-mono text-xs">pool.content.author</code> is required.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-[1440px] mx-auto space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 border-b border-border pb-4">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold font-display tracking-tight text-foreground">
              Certificates Studio
            </h1>
            <Chip size="sm" variant="soft" color="accent">
              Blueprint & Calibration
            </Chip>
          </div>
          <p className="text-sm text-muted mt-1">
            Author certificates, define skills & section blueprints, configure score conversions, and calibrate rubrics.
          </p>
        </div>

        {isBlueprintDesigner && (
          <Button
            size="md"
            variant="primary"
            onPress={() => {
              setWizardCode('');
              setWizardName('');
              setWizardDescription('');
              setSelectedPresetCode('');
              setWizardSkills([]);
              setWizardBands([]);
              setWizardCriteria([]);
              setWizardStep(1);
              setWizardError(null);
              setIsWizardOpen(true);
            }}
          >
            <Plus className="w-4 h-4 mr-1.5" />
            New certificate
          </Button>
        )}
      </div>

      {/* Certificate Isolation Architectural Invariant Callout */}
      <Alert status="warning">
        <Alert.Indicator>
          <ShieldCheck className="w-4 h-4 text-warning" />
        </Alert.Indicator>
        <Alert.Content>
          <Alert.Title>Certificate Isolation Guarantee</Alert.Title>
          <Alert.Description>
            Composite foreign keys carry <code className="font-mono text-xs font-bold">CertId</code> down through exams, sections, attempts, and learning paths. One certificate's content, scoring, or rubric can never leak into another.
          </Alert.Description>
        </Alert.Content>
      </Alert>

      {/* Catalog & Blueprints */}
      <div className="space-y-4">
          {certError && (
            <Alert status="danger">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Title>Error loading certificates</Alert.Title>
                <Alert.Description>{certError}</Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {certificates.map((cert) => (
              <Card key={cert.certId} className="border border-border">
                <Card.Header className="pb-2">
                  <div className="flex items-center justify-between">
                    <span className="font-mono-tabular font-bold text-accent text-lg">
                      {cert.code}
                    </span>
                    <Chip size="sm" variant="soft" color={cert.isActive ? 'success' : 'default'}>
                      {cert.isActive ? 'Active' : 'Inactive'}
                    </Chip>
                  </div>
                  <Card.Title className="text-base mt-1">{cert.name}</Card.Title>
                  {cert.description && (
                    <Card.Description className="line-clamp-2 text-xs">
                      {cert.description}
                    </Card.Description>
                  )}
                </Card.Header>
                <Card.Content className="text-xs space-y-2 pt-2 border-t border-border">
                  <div className="flex justify-between text-muted">
                    <span>Aggregation rule:</span>
                    <span className="font-mono font-medium text-foreground">{cert.scoreAggregationRule}</span>
                  </div>
                  <div className="flex justify-between text-muted">
                    <span>Skills count:</span>
                    <span className="font-mono-tabular font-medium text-foreground">{cert.skillsCount}</span>
                  </div>
                  <div className="flex justify-between text-muted">
                    <span>Sections count:</span>
                    <span className="font-mono-tabular font-medium text-foreground">{cert.sectionsCount}</span>
                  </div>
                  <div className="flex justify-between text-muted">
                    <span>Authored exams:</span>
                    <span className="font-mono-tabular font-medium text-foreground">{cert.examsCount}</span>
                  </div>
                </Card.Content>
              </Card>
            ))}
          </div>
      </div>


      {/* Blueprint Wizard Modal (3-step template flow) */}
      <Modal.Backdrop isOpen={isWizardOpen} onOpenChange={setIsWizardOpen}>
        <Modal.Container>
          <Modal.Dialog className="max-w-2xl max-h-[90vh] overflow-y-auto">
            <Modal.CloseTrigger />
            <Modal.Header>
              <Modal.Heading>Certificate Blueprint Wizard</Modal.Heading>
              <p className="text-xs text-muted mt-1">
                Step {wizardStep} of 3:{' '}
                {wizardStep === 1
                  ? 'Template & Basics'
                  : wizardStep === 2
                    ? 'Sections (Code, Name, Expected Questions)'
                    : 'Score Bands & Flat Grading Criteria'}
              </p>
            </Modal.Header>
            <Modal.Body className="space-y-4">
              {wizardError && (
                <Alert status="danger">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Validation error</Alert.Title>
                    <Alert.Description>{wizardError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              {wizardStep === 1 && (
                <div className="space-y-4">
                  <div className="space-y-1">
                    <label htmlFor="wizard-preset-select" className="text-xs font-semibold text-foreground">
                      Template (2-Skill or 4-Skill)
                    </label>
                    <select
                      id="wizard-preset-select"
                      aria-label="Template"
                      value={selectedPresetCode}
                      onChange={(e) => handlePresetChange(e.target.value)}
                      className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                    >
                      <option value="">-- Choose a skill template --</option>
                      {presets.map((p) => (
                        <option key={p.presetCode} value={p.presetCode}>
                          {p.name} ({p.presetCode})
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div className="space-y-1">
                      <label htmlFor="wizard-cert-code" className="text-xs font-semibold text-foreground">
                        Certificate code
                      </label>
                      <input
                        id="wizard-cert-code"
                        type="text"
                        value={wizardCode}
                        onChange={(e) => setWizardCode(e.target.value)}
                        placeholder="e.g. TOEIC, TOEFL"
                        className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground font-mono text-sm uppercase"
                      />
                    </div>
                    <div className="space-y-1">
                      <label htmlFor="wizard-cert-name" className="text-xs font-semibold text-foreground">
                        Certificate name
                      </label>
                      <input
                        id="wizard-cert-name"
                        type="text"
                        value={wizardName}
                        onChange={(e) => setWizardName(e.target.value)}
                        placeholder="Full certificate title"
                        className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                      />
                    </div>
                  </div>

                  <div className="space-y-1">
                    <label htmlFor="wizard-cert-desc" className="text-xs font-semibold text-foreground">
                      Description
                    </label>
                    <textarea
                      id="wizard-cert-desc"
                      rows={2}
                      value={wizardDescription}
                      onChange={(e) => setWizardDescription(e.target.value)}
                      placeholder="Official overview..."
                      className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                    />
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div className="space-y-1">
                      <label htmlFor="wizard-score-agg" className="text-xs font-semibold text-foreground">
                        Score aggregation rule
                      </label>
                      <select
                        id="wizard-score-agg"
                        value={wizardRule}
                        onChange={(e) => setWizardRule(e.target.value)}
                        className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                      >
                        <option value="SUM">SUM (Sum across sections)</option>
                        <option value="AVERAGE">AVERAGE (Average band/scores)</option>
                        <option value="BAND_CEFR">BAND_CEFR (Direct CEFR mapping)</option>
                      </select>
                    </div>

                    <div className="flex items-center gap-2 pt-6">
                      <input
                        type="checkbox"
                        id="wizard-is-active"
                        checked={wizardIsActive}
                        onChange={(e) => setWizardIsActive(e.target.checked)}
                        className="w-4 h-4 rounded border-border"
                      />
                      <label htmlFor="wizard-is-active" className="text-sm font-medium text-foreground">
                        Active certificate
                      </label>
                    </div>
                  </div>
                </div>
              )}

              {wizardStep === 2 && (
                <div className="space-y-4">
                  <p className="text-xs text-muted">
                    Configure each skill&apos;s sections using 3 fields (Code, Name, Expected questions). Modality defaults are applied automatically.
                  </p>
                  {wizardSkills.map((sk, skillIdx) => (
                    <div key={sk.code} className="p-3 border border-border rounded-md space-y-3">
                      <div className="flex items-center justify-between">
                        <span className="text-sm font-semibold text-foreground">
                          {sk.name} ({formatModalityLabel(sk.modality)})
                        </span>
                        <Button size="sm" variant="ghost" onPress={() => addSectionToSkill(skillIdx)}>
                          + Add section
                        </Button>
                      </div>
                      <div className="space-y-2">
                        {sk.sections.map((sec, secIdx) => (
                          <div key={`${sk.code}-${secIdx}`} className="grid grid-cols-1 sm:grid-cols-3 gap-2">
                            <input
                              type="text"
                              aria-label="Section code"
                              value={sec.code}
                              onChange={(e) => updateSectionField(skillIdx, secIdx, 'code', e.target.value)}
                              placeholder="Code"
                              className="px-2.5 py-1.5 rounded border border-border bg-surface text-foreground font-mono text-xs uppercase"
                            />
                            <input
                              type="text"
                              aria-label="Section name"
                              value={sec.name}
                              onChange={(e) => updateSectionField(skillIdx, secIdx, 'name', e.target.value)}
                              placeholder="Name"
                              className="px-2.5 py-1.5 rounded border border-border bg-surface text-foreground text-xs"
                            />
                            <input
                              type="number"
                              min={1}
                              aria-label="Expected questions"
                              value={sec.expectedQuestionCount}
                              onChange={(e) =>
                                updateSectionField(
                                  skillIdx,
                                  secIdx,
                                  'expectedQuestionCount',
                                  Math.max(1, Number(e.target.value) || 1),
                                )
                              }
                              placeholder="Expected questions"
                              className="px-2.5 py-1.5 rounded border border-border bg-surface text-foreground font-mono text-xs"
                            />
                          </div>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {wizardStep === 3 && (
                <div className="space-y-4">
                  <div className="space-y-2">
                    <div className="flex items-center justify-between">
                      <h3 className="text-sm font-semibold text-foreground">Score bands</h3>
                      <Button
                        size="sm"
                        variant="ghost"
                        onPress={() =>
                          setWizardBands((prev) => [
                            ...prev,
                            {
                              code: `BAND_${prev.length + 1}`,
                              name: `Band ${prev.length + 1}`,
                              minTotal: 0,
                              maxTotal: 100,
                              displayOrder: prev.length + 1,
                            },
                          ])
                        }
                      >
                        + Add band
                      </Button>
                    </div>
                    {wizardBands.map((band, idx) => (
                      <div key={idx} className="grid grid-cols-4 gap-2">
                        <input
                          type="text"
                          aria-label="Band code"
                          value={band.code}
                          onChange={(e) =>
                            setWizardBands((prev) =>
                              prev.map((b, i) => (i === idx ? { ...b, code: e.target.value } : b)),
                            )
                          }
                          className="px-2 py-1.5 rounded border border-border bg-surface text-xs font-mono"
                        />
                        <input
                          type="text"
                          aria-label="Band name"
                          value={band.name}
                          onChange={(e) =>
                            setWizardBands((prev) =>
                              prev.map((b, i) => (i === idx ? { ...b, name: e.target.value } : b)),
                            )
                          }
                          className="px-2 py-1.5 rounded border border-border bg-surface text-xs"
                        />
                        <input
                          type="number"
                          aria-label="Band min"
                          value={band.minTotal}
                          onChange={(e) =>
                            setWizardBands((prev) =>
                              prev.map((b, i) => (i === idx ? { ...b, minTotal: Number(e.target.value) || 0 } : b)),
                            )
                          }
                          className="px-2 py-1.5 rounded border border-border bg-surface text-xs font-mono"
                        />
                        <input
                          type="number"
                          aria-label="Band max"
                          value={band.maxTotal}
                          onChange={(e) =>
                            setWizardBands((prev) =>
                              prev.map((b, i) => (i === idx ? { ...b, maxTotal: Number(e.target.value) || 0 } : b)),
                            )
                          }
                          className="px-2 py-1.5 rounded border border-border bg-surface text-xs font-mono"
                        />
                      </div>
                    ))}
                  </div>

                  <div className="space-y-2 border-t border-border pt-3">
                    <h3 className="text-sm font-semibold text-foreground">Flat grading criteria</h3>
                    {wizardCriteria.length === 0 ? (
                      <p className="text-xs text-muted">
                        No productive skills (Speaking/Writing) in this template — objective sections use linear auto-scales.
                      </p>
                    ) : (
                      wizardCriteria.map((crit, idx) => (
                        <div key={idx} className="grid grid-cols-3 gap-2">
                          <input
                            type="text"
                            aria-label="Criterion name"
                            value={crit.name}
                            onChange={(e) =>
                              setWizardCriteria((prev) =>
                                prev.map((c, i) => (i === idx ? { ...c, name: e.target.value } : c)),
                              )
                            }
                            className="px-2 py-1.5 rounded border border-border bg-surface text-xs"
                          />
                          <input
                            type="number"
                            aria-label="Criterion max score"
                            value={crit.maxScore}
                            onChange={(e) =>
                              setWizardCriteria((prev) =>
                                prev.map((c, i) =>
                                  i === idx ? { ...c, maxScore: Number(e.target.value) || 1 } : c,
                                ),
                              )
                            }
                            className="px-2 py-1.5 rounded border border-border bg-surface text-xs font-mono"
                          />
                          <input
                            type="text"
                            aria-label="Criterion description"
                            value={crit.rubricDescription ?? ''}
                            onChange={(e) =>
                              setWizardCriteria((prev) =>
                                prev.map((c, i) =>
                                  i === idx ? { ...c, rubricDescription: e.target.value } : c,
                                ),
                              )
                            }
                            className="px-2 py-1.5 rounded border border-border bg-surface text-xs"
                          />
                        </div>
                      ))
                    )}
                  </div>
                </div>
              )}
            </Modal.Body>
            <Modal.Footer>
              {wizardStep > 1 ? (
                <Button
                  size="sm"
                  variant="secondary"
                  onPress={() => setWizardStep((s) => (s === 3 ? 2 : 1))}
                >
                  Back
                </Button>
              ) : (
                <Button size="sm" variant="secondary" onPress={() => setIsWizardOpen(false)}>
                  Cancel
                </Button>
              )}
              {wizardStep < 3 ? (
                <Button
                  size="sm"
                  variant="primary"
                  onPress={() => {
                    if (wizardStep === 1 && (!wizardCode.trim() || !wizardName.trim())) {
                      setWizardError('Certificate Code and Name are required.');
                      return;
                    }
                    setWizardError(null);
                    setWizardStep((s) => (s === 1 ? 2 : 3));
                  }}
                >
                  Next
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="primary"
                  onPress={handleSaveWizard}
                  isDisabled={isSavingWizard}
                >
                  Save blueprint
                </Button>
              )}
            </Modal.Footer>
          </Modal.Dialog>
        </Modal.Container>
      </Modal.Backdrop>
    </div>
  );
}
