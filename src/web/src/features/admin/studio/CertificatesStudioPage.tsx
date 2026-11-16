import React, { useState, useEffect } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Label,
  ListBox,
  Modal,
  FieldError,
  Form,
  Input,
  NumberField,
  Select,
  TextArea,
  TextField,
} from '@heroui/react';
import { extractErrorMessage, extractFieldErrors } from './validationErrors';
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

/**
 * CK_Certificates_ScoreAggregationRule and CertificateService's own allow-list: these three codes
 * and nothing else. `AVERAGE` was never the spelling, and `BAND_CEFR` was never a rule.
 */
export const SCORE_AGGREGATION_RULES = [
  { id: 'sum', label: 'Sum of skill scores' },
  { id: 'avg_half_step', label: 'Average, rounded to nearest 0.5' },
  { id: 'avg_whole', label: 'Average, rounded to nearest whole number' },
] as const;

/** A preset could name a rule the API refuses; fall back to `sum` rather than fail the save. */
function normaliseAggregationRule(rule: string | null | undefined): string {
  const candidate = rule?.trim().toLowerCase();
  return SCORE_AGGREGATION_RULES.find((r) => r.id === candidate)?.id ?? 'sum';
}

/**
 * Mirrors CertificateConfigurationValidator.TotalRange: `sum` adds the skill scores, the average
 * rules keep the skill range. The two must agree or the validator refuses the derived bands.
 */
function totalRangeFor(
  rule: string,
  skillMin: number,
  skillMax: number,
  skillCount: number,
): [number, number] {
  const skills = Math.max(skillCount, 1);
  return normaliseAggregationRule(rule) === 'sum'
    ? [skillMin * skills, skillMax * skills]
    : [skillMin, skillMax];
}

const DEFAULT_BAND_LABELS = [
  { code: 'A2', name: 'Elementary (A2)' },
  { code: 'B1', name: 'Intermediate (B1)' },
  { code: 'B2', name: 'Upper-Intermediate (B2)' },
  { code: 'C1', name: 'Advanced (C1)' },
];

/**
 * Four contiguous bands covering the whole total range: the validator refuses a gap and an overlap
 * alike, so each band starts where the previous one ended, plus one.
 */
export function deriveDefaultBands(
  rule: string,
  skillCount: number,
  min: number,
  max: number,
): SaveScoreBandWizardDto[] {
  const [low, high] = totalRangeFor(rule, min, max, skillCount);
  const span = Math.max(high - low + 1, 1);
  const count = Math.min(DEFAULT_BAND_LABELS.length, span);
  const size = Math.ceil(span / count);

  return DEFAULT_BAND_LABELS.slice(0, count).map((label, index) => ({
    // The band DTO is a positional record too: a missing bandId fails JSON binding for the whole
    // request, which surfaced as "the request is not valid" with no field named.
    bandId: null,
    code: label.code,
    name: label.name,
    minTotal: low + index * size,
    maxTotal: index === count - 1 ? high : Math.min(low + (index + 1) * size - 1, high),
    displayOrder: index + 1,
  }));
}

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
  // Removal is confirmed before it runs: it either destroys an untouched certificate or withdraws one
  // holding learner history, and neither is undoable from this screen.
  const [removalTarget, setRemovalTarget] = useState<AdminCertificateSummaryDto | null>(null);
  const [isRemoving, setIsRemoving] = useState(false);
  const [removalNotice, setRemovalNotice] = useState<string | null>(null);

  // Wizard Modal State (3-step template flow)
  const [isWizardOpen, setIsWizardOpen] = useState(false);
  const [wizardStep, setWizardStep] = useState<1 | 2 | 3>(1);
  const [selectedPresetCode, setSelectedPresetCode] = useState<string>('');
  const [wizardCode, setWizardCode] = useState('');
  const [wizardName, setWizardName] = useState('');
  const [wizardDescription, setWizardDescription] = useState('');
  const [wizardRule, setWizardRule] = useState('sum');
  const [wizardMinScore, setWizardMinScore] = useState(0);
  const [wizardMaxScore, setWizardMaxScore] = useState(100);
  const [wizardScoreStep, setWizardScoreStep] = useState(1);
  const [wizardIsActive, setWizardIsActive] = useState(true);
  const [wizardSkills, setWizardSkills] = useState<SaveSkillWizardDto[]>([]);
  const [wizardBands, setWizardBands] = useState<SaveScoreBandWizardDto[]>([]);
  const [wizardCriteria, setWizardCriteria] = useState<SaveGradingCriterionWizardDto[]>([]);
  const [isSavingWizard, setIsSavingWizard] = useState(false);
  const [wizardError, setWizardError] = useState<string | null>(null);
  const [wizardFieldErrors, setWizardFieldErrors] = useState<Record<string, string>>({});

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

  /**
   * Removes the certificate the admin confirmed. The server decides whether that means destroying an
   * untouched certificate or withdrawing one that holds learner history, and says which — so the page
   * reports what happened instead of assuming the row disappeared.
   */
  const handleRemoveCertificate = async () => {
    if (!removalTarget) return;
    setIsRemoving(true);
    setCertError(null);
    setRemovalNotice(null);
    try {
      const result = await studioApi.removeCertificate(removalTarget.certId);
      setRemovalNotice(result.message);
      setRemovalTarget(null);
      await loadCertificates();
    } catch (err: any) {
      setCertError(
        err?.detail || err?.title || `Could not remove '${removalTarget.code}'.`,
      );
      setRemovalTarget(null);
    } finally {
      setIsRemoving(false);
    }
  };

  // Preset Selection in Wizard
  const handlePresetChange = (presetCode: string) => {
    setSelectedPresetCode(presetCode);
    const found = presets.find((p) => p.presetCode === presetCode);
    if (!found) return;

    const rule = normaliseAggregationRule(found.scoreAggregationRule);
    setWizardRule(rule);
    const mappedSkills: SaveSkillWizardDto[] = found.skills.map((s) => ({
      skillId: null,
      code: s.code,
      name: s.name,
      modality: s.modality,
      displayOrder: s.displayOrder,
      durationMinutes: s.durationMinutes,
      sections: s.sections.map((sec) => ({
        sectionId: null,
        code: sec.code,
        name: sec.name,
        displayOrder: sec.displayOrder,
        scoreWeight: sec.scoreWeight,
        navigationMode: sec.navigationMode,
        timerPolicyJson: sec.timerPolicyJson ?? null,
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

    // A preset brings its own skills and rule, so the bands must follow them; keeping the previous
    // tile would leave the validator a gap at the top when the skill count changed.
    setWizardBands(deriveDefaultBands(rule, mappedSkills.length, wizardMinScore, wizardMaxScore));

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
              sectionId: null,
              code: `${sk.code.slice(0, 1)}P${nextOrder}`,
              name: `${sk.name} Part ${nextOrder}`,
              displayOrder: nextOrder,
              scoreWeight: 1,
              navigationMode: sk.modality === 'listening' ? 'AUDIO_DRIVEN' : 'FULL_SANDBOX',
              timerPolicyJson: null,
              layoutType: 'STANDARD_VERTICAL',
              optionCount: 4,
              clusterSize: 1,
              unitCount: 1,
              hideOptionText: false,
              requirePassage: sk.modality === 'reading',
              requireAudio: sk.modality === 'listening',
              requireImage: false,
              requiredKeywordCount: 0,
              expectedQuestionCount: 10,
              allowedComponentTypesJson: null,
              toolsConfigJson: null,
            },
          ],
        };
      }),
    );
  };

  // Save Blueprint Wizard
  const handleSaveWizard = async () => {
    setWizardError(null);
    setWizardFieldErrors({});
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
            // The request DTO is a positional record, so every member must be present or JSON binding
            // rejects the whole body with a 400 that names nothing. skillId/timeCost were omitted here
            // and that is what made "New certificate" impossible to save.
            skillId: null,
            durationMinutes: 45,
            sections: [
              {
                sectionId: null,
                code: 'PART1',
                name: 'Section 1',
                displayOrder: 1,
                scoreWeight: 1,
                navigationMode: 'FULL_SANDBOX',
                timerPolicyJson: null,
                layoutType: 'STANDARD_VERTICAL',
                optionCount: 4,
                clusterSize: 1,
                unitCount: 1,
                hideOptionText: false,
                requirePassage: false,
                requireAudio: false,
                requireImage: false,
                requiredKeywordCount: 0,
                expectedQuestionCount: 10,
                allowedComponentTypesJson: null,
                toolsConfigJson: null,
              },
            ],
          },
        ],
        // A save that never touched the template still needs bands that tile the range, or the
        // validator refuses it on the first press.
        bands:
          wizardBands.length > 0
            ? wizardBands
            : deriveDefaultBands(
                wizardRule,
                Math.max(wizardSkills.length, 1),
                wizardMinScore,
                wizardMaxScore,
              ),
        criteria: wizardCriteria,
        minScaledScore: wizardMinScore,
        maxScaledScore: wizardMaxScore,
        scoreStep: wizardScoreStep,
      };

      await studioApi.saveCertificateWizard(payload);
      setIsWizardOpen(false);
      loadCertificates();
    } catch (err: unknown) {
      const fieldErrs = extractFieldErrors(err);
      setWizardFieldErrors(fieldErrs);
      if (
        fieldErrs.code ||
        fieldErrs.name ||
        fieldErrs.description ||
        fieldErrs.minScaledScore ||
        fieldErrs.maxScaledScore ||
        fieldErrs.scoreStep
      ) {
        setWizardStep(1);
      }
      setWizardError(extractErrorMessage(err, 'Failed to save certificate blueprint.'));
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
              setWizardRule('sum');
              setWizardMinScore(0);
              setWizardMaxScore(100);
              setWizardScoreStep(1);
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

          {removalNotice && (
            <Alert status="success">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Title>Certificate removed</Alert.Title>
                <Alert.Description>{removalNotice}</Alert.Description>
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
                  <div className="pt-2 border-t border-border">
                    <Button
                      size="sm"
                      variant="ghost"
                      className="text-danger"
                      onPress={() => {
                        setRemovalNotice(null);
                        setRemovalTarget(cert);
                      }}
                    >
                      Remove certificate
                    </Button>
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
                  <Select
                    aria-label="Template (2-Skill or 4-Skill)"
                    className="w-full"
                    value={selectedPresetCode || 'none'}
                    onChange={(key) => handlePresetChange(key === 'none' || key == null ? '' : String(key))}
                  >
                    <Label className="text-xs font-semibold text-foreground">
                      Template (2-Skill or 4-Skill)
                    </Label>
                    <Select.Trigger>
                      <Select.Value />
                      <Select.Indicator />
                    </Select.Trigger>
                    <Select.Popover>
                      <ListBox>
                        <ListBox.Item id="none" textValue="Choose a skill template">
                          -- Choose a skill template --
                          <ListBox.ItemIndicator />
                        </ListBox.Item>
                        {presets.map((p) => (
                          <ListBox.Item
                            key={p.presetCode}
                            id={p.presetCode}
                            textValue={`${p.name} (${p.presetCode})`}
                          >
                            {p.name} ({p.presetCode})
                            <ListBox.ItemIndicator />
                          </ListBox.Item>
                        ))}
                      </ListBox>
                    </Select.Popover>
                  </Select>

                  <Form validationBehavior="aria" className="space-y-4">
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      <TextField
                        isRequired
                        name="code"
                        value={wizardCode}
                        onChange={(value) => {
                          setWizardCode(value);
                          setWizardFieldErrors((prev) => ({ ...prev, code: '' }));
                        }}
                        isInvalid={Boolean(wizardFieldErrors.code)}
                      >
                        <Label htmlFor="wizard-cert-code">Certificate code</Label>
                        <Input
                          id="wizard-cert-code"
                          placeholder="e.g. TOEIC, TOEFL"
                          className="font-mono text-sm uppercase"
                        />
                        {wizardFieldErrors.code && (
                          <FieldError>{wizardFieldErrors.code}</FieldError>
                        )}
                      </TextField>

                      <TextField
                        isRequired
                        name="name"
                        value={wizardName}
                        onChange={(value) => {
                          setWizardName(value);
                          setWizardFieldErrors((prev) => ({ ...prev, name: '' }));
                        }}
                        isInvalid={Boolean(wizardFieldErrors.name)}
                      >
                        <Label htmlFor="wizard-cert-name">Certificate name</Label>
                        <Input
                          id="wizard-cert-name"
                          placeholder="Full certificate title"
                          className="text-sm"
                        />
                        {wizardFieldErrors.name && (
                          <FieldError>{wizardFieldErrors.name}</FieldError>
                        )}
                      </TextField>
                    </div>

                    <TextField
                      name="description"
                      value={wizardDescription}
                      onChange={(value) => {
                        setWizardDescription(value);
                        setWizardFieldErrors((prev) => ({ ...prev, description: '' }));
                      }}
                      isInvalid={Boolean(wizardFieldErrors.description)}
                    >
                      <Label htmlFor="wizard-cert-desc">Description</Label>
                      <TextArea
                        id="wizard-cert-desc"
                        rows={2}
                        placeholder="Official overview..."
                        className="text-sm"
                      />
                      {wizardFieldErrors.description && (
                        <FieldError>{wizardFieldErrors.description}</FieldError>
                      )}
                    </TextField>
                  </Form>

                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <Select
                      aria-label="Score aggregation rule"
                      className="w-full"
                      value={normaliseAggregationRule(wizardRule)}
                      onChange={(key) => {
                        if (key == null) return;
                        const rule = normaliseAggregationRule(String(key));
                        setWizardRule(rule);
                        // The bands tile a range the rule just changed, so stale ones would gap.
                        setWizardBands((prev) =>
                          prev.length === 0
                            ? prev
                            : deriveDefaultBands(rule, wizardSkills.length, wizardMinScore, wizardMaxScore),
                        );
                      }}
                    >
                      <Label className="text-xs font-semibold text-foreground">
                        Score aggregation rule
                      </Label>
                      <Select.Trigger>
                        <Select.Value />
                        <Select.Indicator />
                      </Select.Trigger>
                      <Select.Popover>
                        <ListBox>
                          {SCORE_AGGREGATION_RULES.map((rule) => (
                            <ListBox.Item
                              key={rule.id}
                              id={rule.id}
                              textValue={`${rule.label} (${rule.id})`}
                            >
                              {rule.label}
                              <ListBox.ItemIndicator />
                            </ListBox.Item>
                          ))}
                        </ListBox>
                      </Select.Popover>
                    </Select>

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

                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                    <NumberField
                      isRequired
                      name="minScaledScore"
                      minValue={0}
                      value={wizardMinScore}
                      onChange={(value) => {
                        // HeroUI v3 hands back `undefined` while the field is empty, and the payload
                        // must always carry a scale: the server refuses a certificate without
                        // Min/Max/Step. The previous value therefore survives an empty box rather
                        // than being replaced by undefined, which JSON.stringify would then drop
                        // from the request entirely.
                        const next = value ?? wizardMinScore;
                        setWizardMinScore(next);
                        // The bands tile the total range, so a new scale invalidates them.
                        setWizardBands((prev) =>
                          prev.length === 0
                            ? prev
                            : deriveDefaultBands(wizardRule, wizardSkills.length, next, wizardMaxScore),
                        );
                        setWizardFieldErrors((prev) => ({ ...prev, minScaledScore: '' }));
                      }}
                      isInvalid={Boolean(wizardFieldErrors.minScaledScore)}
                    >
                      <Label>Minimum scaled score</Label>
                      <NumberField.Group>
                        <NumberField.DecrementButton />
                        <NumberField.Input className="font-mono-tabular text-sm" />
                        <NumberField.IncrementButton />
                      </NumberField.Group>
                      {wizardFieldErrors.minScaledScore && (
                        <FieldError>{wizardFieldErrors.minScaledScore}</FieldError>
                      )}
                    </NumberField>

                    <NumberField
                      isRequired
                      name="maxScaledScore"
                      minValue={1}
                      value={wizardMaxScore}
                      onChange={(value) => {
                        // Same guard as the minimum: an empty box must not erase the scale the payload
                        // has to carry.
                        const next = value ?? wizardMaxScore;
                        setWizardMaxScore(next);
                        setWizardBands((prev) =>
                          prev.length === 0
                            ? prev
                            : deriveDefaultBands(wizardRule, wizardSkills.length, wizardMinScore, next),
                        );
                        setWizardFieldErrors((prev) => ({ ...prev, maxScaledScore: '' }));
                      }}
                      isInvalid={Boolean(wizardFieldErrors.maxScaledScore)}
                    >
                      <Label>Maximum scaled score</Label>
                      <NumberField.Group>
                        <NumberField.DecrementButton />
                        <NumberField.Input className="font-mono-tabular text-sm" />
                        <NumberField.IncrementButton />
                      </NumberField.Group>
                      {wizardFieldErrors.maxScaledScore && (
                        <FieldError>{wizardFieldErrors.maxScaledScore}</FieldError>
                      )}
                    </NumberField>

                    <NumberField
                      isRequired
                      name="scoreStep"
                      minValue={1}
                      value={wizardScoreStep}
                      onChange={(value) => {
                        setWizardScoreStep(value ?? wizardScoreStep);
                        setWizardFieldErrors((prev) => ({ ...prev, scoreStep: '' }));
                      }}
                      isInvalid={Boolean(wizardFieldErrors.scoreStep)}
                    >
                      <Label>Score step</Label>
                      <NumberField.Group>
                        <NumberField.DecrementButton />
                        <NumberField.Input className="font-mono-tabular text-sm" />
                        <NumberField.IncrementButton />
                      </NumberField.Group>
                      {wizardFieldErrors.scoreStep && (
                        <FieldError>{wizardFieldErrors.scoreStep}</FieldError>
                      )}
                    </NumberField>
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
                              bandId: null,
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

      {/* Confirmed before it runs. The wording says what will happen to a certificate that holds
          learner history, because "remove" would otherwise promise a deletion it cannot perform. */}
      <Modal.Backdrop
        isOpen={removalTarget !== null}
        onOpenChange={(open) => {
          if (!open) setRemovalTarget(null);
        }}
      >
        <Modal.Container>
          <Modal.Dialog className="sm:max-w-md">
            <Modal.Header>
              <Modal.Heading>Remove {removalTarget?.code}?</Modal.Heading>
              <p className="text-sm text-muted m-0 mt-1">
                A certificate with no learner history is deleted. One that holds attempts, placements or
                a learning path is withdrawn from the catalogue instead, and any learner on it is moved
                to another active certificate.
              </p>
            </Modal.Header>
            <Modal.Footer>
              <Button variant="secondary" onPress={() => setRemovalTarget(null)}>
                Keep it
              </Button>
              <Button
                variant="danger"
                isPending={isRemoving}
                onPress={() => void handleRemoveCertificate()}
              >
                Remove
              </Button>
            </Modal.Footer>
          </Modal.Dialog>
        </Modal.Container>
      </Modal.Backdrop>
    </div>
  );
}
