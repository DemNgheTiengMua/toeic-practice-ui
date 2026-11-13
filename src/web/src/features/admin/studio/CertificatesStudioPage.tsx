import React, { useState, useEffect } from 'react';
import {
  Button,
  Card,
  Chip,
  Tabs,
  Modal,
  Table,
  TextField,
  NumberField,
  Alert,
} from '@heroui/react';
import {
  Award,
  ShieldCheck,
  Scale,
  Plus,
  Play,
  Lock,
  CheckCircle2,
  AlertTriangle,
  RefreshCw,
  FileText,
  Sliders,
} from 'lucide-react';
import { useAuth } from '../../../context/AuthContext';
import { canManageCatalog, hasCapability, STAFF_PERMISSIONS } from '../../../lib/security';
import { studioApi } from './studioApi';
import {
  formatRubricLadderOutcomeLabel,
  formatRubricBandLabel,
  formatModalityLabel,
} from './studioLabels';
import type {
  AdminCertificateSummaryDto,
  CertificateBlueprintPresetDto,
  SaveCertificateWizardRequest,
  SaveSkillWizardDto,
  SaveSectionWizardDto,
  SaveScoreBandWizardDto,
  SaveGradingCriterionWizardDto,
  AdminScoreConversionDto,
  ScorePreviewResultDto,
  RubricStudioDto,
  RubricLadderDto,
} from './types';

export function CertificatesStudioPage() {
  const { user } = useAuth();
  const [activeTab, setActiveTab] = useState<string>('catalog');

  // Capabilities
  const isCatalogManager = canManageCatalog(user);
  const isBlueprintDesigner = hasCapability(user, STAFF_PERMISSIONS.BlueprintDesign);

  // Catalog State
  const [certificates, setCertificates] = useState<AdminCertificateSummaryDto[]>([]);
  const [presets, setPresets] = useState<CertificateBlueprintPresetDto[]>([]);
  const [isLoadingCerts, setIsLoadingCerts] = useState(false);
  const [certError, setCertError] = useState<string | null>(null);

  // Wizard Modal State
  const [isWizardOpen, setIsWizardOpen] = useState(false);
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

  // Scoring Tab State
  const [selectedCertIdForScore, setSelectedCertIdForScore] = useState<number>(1);
  const [scoreConversion, setScoreConversion] = useState<AdminScoreConversionDto | null>(null);
  const [isLoadingScore, setIsLoadingScore] = useState(false);
  const [scorePreviewInput, setScorePreviewInput] = useState<Record<string, number>>({});
  const [scorePreviewResult, setScorePreviewResult] = useState<ScorePreviewResultDto | null>(null);
  const [isPreviewing, setIsPreviewing] = useState(false);

  // Rubric Studio State
  const [selectedCertIdForRubric, setSelectedCertIdForRubric] = useState<number>(1);
  const [selectedSkillIdForRubric, setSelectedSkillIdForRubric] = useState<number>(12);
  const [rubricStudio, setRubricStudio] = useState<RubricStudioDto | null>(null);
  const [isLoadingRubric, setIsLoadingRubric] = useState(false);
  const [ladderVerdict, setLadderVerdict] = useState<RubricLadderDto | null>(null);
  const [isRunningLadder, setIsRunningLadder] = useState(false);
  const [isLockingRubric, setIsLockingRubric] = useState(false);
  const [rubricNotice, setRubricNotice] = useState<string | null>(null);

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
      if (data.length > 0 && !selectedCertIdForScore) {
        setSelectedCertIdForScore(data[0].certId);
        setSelectedCertIdForRubric(data[0].certId);
      }
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

  // Load Score Conversion
  useEffect(() => {
    if (activeTab === 'scoring' && isCatalogManager) {
      loadScoreConversion();
    }
  }, [activeTab, selectedCertIdForScore, isCatalogManager]);

  const loadScoreConversion = async () => {
    setIsLoadingScore(true);
    try {
      const data = await studioApi.getScoreConversion();
      setScoreConversion(data);
    } catch {
      // Fallback
    } finally {
      setIsLoadingScore(false);
    }
  };

  // Load Rubric Studio
  useEffect(() => {
    if (activeTab === 'rubrics' && isCatalogManager) {
      loadRubricStudio();
    }
  }, [activeTab, selectedCertIdForRubric, selectedSkillIdForRubric, isCatalogManager]);

  const loadRubricStudio = async () => {
    setIsLoadingRubric(true);
    try {
      const data = await studioApi.getRubricStudio(selectedCertIdForRubric, selectedSkillIdForRubric);
      setRubricStudio(data);
      setLadderVerdict(data.ladder);
    } catch {
      // Fallback
    } finally {
      setIsLoadingRubric(false);
    }
  };

  // Preset Selection in Wizard
  const handlePresetChange = (presetCode: string) => {
    setSelectedPresetCode(presetCode);
    const found = presets.find((p) => p.presetCode === presetCode);
    if (!found) return;

    setWizardRule(found.scoreAggregationRule);
    setWizardSkills(
      found.skills.map((s) => ({
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
      })),
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

  // Score Preview Simulator
  const handleRunScorePreview = async () => {
    setIsPreviewing(true);
    try {
      const skillsInput = (scoreConversion?.skillScales ?? []).map((sk) => ({
        skillId: sk.skillId,
        skillCode: sk.skillCode,
        rawScore: scorePreviewInput[sk.skillCode] ?? 50,
      }));

      const res = await studioApi.previewScore({
        certId: selectedCertIdForScore,
        scoreAggregationRule: scoreConversion?.certificates[0] ? 'SUM' : 'SUM',
        skills: skillsInput,
      });
      setScorePreviewResult(res);
    } catch {
      // preview error
    } finally {
      setIsPreviewing(false);
    }
  };

  // Run Rubric Ladder
  const handleRunLadder = async () => {
    setIsRunningLadder(true);
    setRubricNotice(null);
    try {
      const responses = [
        { band: 0, responseText: 'Pre-A1 sample benchmark text', isVerboseTrap: false },
        { band: 1, responseText: 'A1 sample benchmark text', isVerboseTrap: false },
        { band: 2, responseText: 'A2 sample benchmark text', isVerboseTrap: false },
        { band: 3, responseText: 'B1 sample benchmark text', isVerboseTrap: false },
        { band: 4, responseText: 'B2 sample benchmark text', isVerboseTrap: false },
        { band: 5, responseText: 'C1 sample benchmark text', isVerboseTrap: false },
        { band: 6, responseText: 'C2 sample benchmark text', isVerboseTrap: false },
        { band: 3, responseText: 'Verbose trap sample text with excessive repetitive sentences', isVerboseTrap: true },
      ];

      const ladder = await studioApi.runRubricLadder({
        certId: selectedCertIdForRubric,
        skillId: selectedSkillIdForRubric,
        responses,
      });
      setLadderVerdict(ladder);
    } catch (err: any) {
      setRubricNotice(err?.detail || err?.title || 'Ladder evaluation failed.');
    } finally {
      setIsRunningLadder(false);
    }
  };

  // Lock Rubric
  const handleLockRubric = async () => {
    setIsLockingRubric(true);
    setRubricNotice(null);
    try {
      const responses = [
        { band: 0, responseText: 'Pre-A1 benchmark', isVerboseTrap: false },
        { band: 1, responseText: 'A1 benchmark', isVerboseTrap: false },
        { band: 2, responseText: 'A2 benchmark', isVerboseTrap: false },
        { band: 3, responseText: 'B1 benchmark', isVerboseTrap: false },
        { band: 4, responseText: 'B2 benchmark', isVerboseTrap: false },
        { band: 5, responseText: 'C1 benchmark', isVerboseTrap: false },
        { band: 6, responseText: 'C2 benchmark', isVerboseTrap: false },
      ];
      const locked = await studioApi.lockRubric({
        certId: selectedCertIdForRubric,
        skillId: selectedSkillIdForRubric,
        responses,
      });
      setLadderVerdict(locked);
      if (rubricStudio) {
        setRubricStudio({ ...rubricStudio, isLocked: true, lockedAt: new Date().toISOString() });
      }
      setRubricNotice('Rubric successfully locked and calibrated!');
    } catch (err: any) {
      setRubricNotice(err?.detail || err?.title || 'Lock refused: rubric has holes or failed ladder separation.');
    } finally {
      setIsLockingRubric(false);
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

      {/* Tabs */}
      <Tabs selectedKey={activeTab} onSelectionChange={(k) => setActiveTab(String(k))}>
        <Tabs.ListContainer>
          <Tabs.List aria-label="Certificates Studio navigation">
            <Tabs.Tab id="catalog">
              <Tabs.Indicator />
              <div className="flex items-center gap-2">
                <Award className="w-4 h-4" />
                <span>Catalog & Blueprints</span>
              </div>
            </Tabs.Tab>
            <Tabs.Tab id="scoring">
              <Tabs.Indicator />
              <div className="flex items-center gap-2">
                <Scale className="w-4 h-4" />
                <span>Score Conversion</span>
              </div>
            </Tabs.Tab>
            <Tabs.Tab id="rubrics">
              <Tabs.Indicator />
              <div className="flex items-center gap-2">
                <Sliders className="w-4 h-4" />
                <span>Rubric Studio & Calibration</span>
              </div>
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>

        {/* Tab 1: Catalog & Blueprints */}
        <Tabs.Panel id="catalog" className="pt-4 space-y-4">
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
                <Card.Footer className="pt-2 flex justify-end gap-2 border-t border-border">
                  <Button
                    size="sm"
                    variant="ghost"
                    onPress={() => {
                      setSelectedCertIdForScore(cert.certId);
                      setActiveTab('scoring');
                    }}
                  >
                    View scoring
                  </Button>
                </Card.Footer>
              </Card>
            ))}
          </div>
        </Tabs.Panel>

        {/* Tab 2: Score Conversion */}
        <Tabs.Panel id="scoring" className="pt-4 space-y-6">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-bold font-display text-foreground flex items-center gap-2">
              <Scale className="w-5 h-5 text-accent" />
              Score Conversion & Scaling Simulator
            </h2>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            {/* Scale Tables */}
            <div className="lg:col-span-2 space-y-4">
              <Card>
                <Card.Header>
                  <Card.Title className="text-base">Skill Scaled Score Conversions</Card.Title>
                  <Card.Description>
                    Maps raw question scores to official scaled scores per certificate skill.
                  </Card.Description>
                </Card.Header>
                <Card.Content>
                  {scoreConversion?.skillScales?.map((group) => (
                    <div key={group.skillId} className="mb-4">
                      <div className="flex justify-between items-center mb-2">
                        <span className="font-semibold text-sm">
                          {group.skillName} ({group.skillCode})
                        </span>
                        <span className="text-xs text-muted font-mono-tabular">
                          Max questions: {group.maxQuestions}
                        </span>
                      </div>
                      <Table>
                        <Table.ScrollContainer>
                          <Table.Content aria-label={`Scale table for ${group.skillName}`}>
                            <Table.Header>
                              <Table.Column isRowHeader>Raw score</Table.Column>
                              <Table.Column>Scaled score</Table.Column>
                            </Table.Header>
                            <Table.Body>
                              {group.scales.map((s, idx) => (
                                <Table.Row key={s.scaleId ?? idx}>
                                  <Table.Cell className="font-mono-tabular">{s.rawScore}</Table.Cell>
                                  <Table.Cell className="font-mono-tabular font-bold text-accent">
                                    {s.scaledScore}
                                  </Table.Cell>
                                </Table.Row>
                              ))}
                            </Table.Body>
                          </Table.Content>
                        </Table.ScrollContainer>
                      </Table>
                    </div>
                  ))}
                </Card.Content>
              </Card>

              {/* Bands Config */}
              <Card>
                <Card.Header>
                  <Card.Title className="text-base">Score Bands & Proficiency Tiers</Card.Title>
                </Card.Header>
                <Card.Content>
                  <Table>
                    <Table.ScrollContainer>
                      <Table.Content aria-label="Score bands configuration">
                        <Table.Header>
                          <Table.Column isRowHeader>Band code</Table.Column>
                          <Table.Column>Tier name</Table.Column>
                          <Table.Column>Total score range</Table.Column>
                        </Table.Header>
                        <Table.Body>
                          {scoreConversion?.bands?.map((b) => (
                            <Table.Row key={b.bandId}>
                              <Table.Cell className="font-mono-tabular font-bold">{b.code}</Table.Cell>
                              <Table.Cell>{b.name}</Table.Cell>
                              <Table.Cell className="font-mono-tabular">
                                {b.minTotal} — {b.maxTotal}
                              </Table.Cell>
                            </Table.Row>
                          ))}
                        </Table.Body>
                      </Table.Content>
                    </Table.ScrollContainer>
                  </Table>
                </Card.Content>
              </Card>
            </div>

            {/* Preview Simulator */}
            <div>
              <Card className="sticky top-20 border-accent/20">
                <Card.Header>
                  <Card.Title className="text-base">Score Simulator</Card.Title>
                  <Card.Description>
                    Test real-time aggregation and band mapping with live raw scores.
                  </Card.Description>
                </Card.Header>
                <Card.Content className="space-y-4">
                  {scoreConversion?.skillScales?.map((sk) => (
                    <div key={sk.skillId} className="space-y-1">
                      <label className="text-xs font-medium text-foreground">
                        {sk.skillName} raw score (0–{sk.maxQuestions})
                      </label>
                      <input
                        type="number"
                        min={0}
                        max={sk.maxQuestions}
                        value={scorePreviewInput[sk.skillCode] ?? 50}
                        onChange={(e) =>
                          setScorePreviewInput({
                            ...scorePreviewInput,
                            [sk.skillCode]: Number(e.target.value),
                          })
                        }
                        className="w-full px-3 py-1.5 rounded-md border border-border bg-surface text-foreground font-mono-tabular text-sm"
                      />
                    </div>
                  ))}

                  <Button
                    size="sm"
                    variant="primary"
                    className="w-full"
                    onPress={handleRunScorePreview}
                    isDisabled={isPreviewing}
                  >
                    <Play className="w-3.5 h-3.5 mr-1.5" />
                    Simulate score
                  </Button>

                  {scorePreviewResult && (
                    <div className="pt-3 border-t border-border space-y-2">
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-muted">Total scaled score:</span>
                        <span className="font-mono-tabular text-lg font-bold text-accent">
                          {scorePreviewResult.totalScore}
                        </span>
                      </div>
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-muted">Assigned band:</span>
                        <Chip size="sm" variant="soft" color="accent">
                          {scorePreviewResult.bandCode} · {scorePreviewResult.bandName}
                        </Chip>
                      </div>
                    </div>
                  )}
                </Card.Content>
              </Card>
            </div>
          </div>
        </Tabs.Panel>

        {/* Tab 3: Rubric Studio & Calibration Ladder */}
        <Tabs.Panel id="rubrics" className="pt-4 space-y-6">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
            <div>
              <h2 className="text-lg font-bold font-display text-foreground flex items-center gap-2">
                <Sliders className="w-5 h-5 text-accent" />
                Rubric Studio & Calibration Ladder
              </h2>
              <p className="text-xs text-muted mt-0.5">
                Each productive skill defines a 28-cell grid (7 CEFR bands × criteria). Distinct Descriptor and Binary Gates are mandatory.
              </p>
            </div>

            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="secondary"
                onPress={handleRunLadder}
                isDisabled={isRunningLadder}
              >
                <Play className="w-3.5 h-3.5 mr-1" />
                Run calibration ladder
              </Button>
              <Button
                size="sm"
                variant="primary"
                onPress={handleLockRubric}
                isDisabled={isLockingRubric || rubricStudio?.isLocked}
              >
                <Lock className="w-3.5 h-3.5 mr-1" />
                {rubricStudio?.isLocked ? 'Rubric locked' : 'Lock rubric'}
              </Button>
            </div>
          </div>

          {rubricNotice && (
            <Alert status="accent">
              <Alert.Indicator />
              <Alert.Content>
                <Alert.Title>Notice</Alert.Title>
                <Alert.Description>{rubricNotice}</Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          {/* Ladder Verdict Banner */}
          {ladderVerdict && (
            <Alert status={ladderVerdict.accepted ? 'success' : 'danger'}>
              <Alert.Indicator>
                {ladderVerdict.accepted ? (
                  <CheckCircle2 className="w-4 h-4 text-success" />
                ) : (
                  <AlertTriangle className="w-4 h-4 text-danger" />
                )}
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title className="font-bold">
                  {formatRubricLadderOutcomeLabel(ladderVerdict.outcome)}
                </Alert.Title>
                {ladderVerdict.reason && (
                  <Alert.Description>{ladderVerdict.reason}</Alert.Description>
                )}
              </Alert.Content>
            </Alert>
          )}

          {/* 28-Cell Rubric Grid */}
          <div className="space-y-6">
            {rubricStudio?.criteria.map((criterion) => (
              <Card key={criterion.criterionId || criterion.code}>
                <Card.Header className="pb-3 border-b border-border">
                  <div className="flex items-center justify-between">
                    <div>
                      <Card.Title className="text-base">{criterion.name}</Card.Title>
                      <Card.Description className="font-mono text-xs">
                        Code: {criterion.code} · Max score: {criterion.maxScore} · Weight: {criterion.weight}
                      </Card.Description>
                    </div>
                  </div>
                </Card.Header>
                <Card.Content className="pt-3">
                  <Table>
                    <Table.ScrollContainer>
                      <Table.Content aria-label={`Rubric grid for ${criterion.name}`}>
                        <Table.Header>
                          <Table.Column isRowHeader className="w-24">Band</Table.Column>
                          <Table.Column className="w-1/2">Descriptor (What it looks like)</Table.Column>
                          <Table.Column className="w-1/2">Binary gates (Must be true to reach band)</Table.Column>
                        </Table.Header>
                        <Table.Body>
                          {criterion.cells.map((cell) => (
                            <Table.Row key={cell.band}>
                              <Table.Cell className="font-mono-tabular font-bold text-accent">
                                {formatRubricBandLabel(cell.band)}
                              </Table.Cell>
                              <Table.Cell className="text-sm">{cell.descriptor}</Table.Cell>
                              <Table.Cell className="text-sm bg-surface-secondary/40 font-mono text-xs">
                                {cell.binaryGates}
                              </Table.Cell>
                            </Table.Row>
                          ))}
                        </Table.Body>
                      </Table.Content>
                    </Table.ScrollContainer>
                  </Table>

                  {/* Anchors Display */}
                  {criterion.anchors && criterion.anchors.length > 0 && (
                    <div className="mt-4 pt-3 border-t border-border">
                      <span className="text-xs font-semibold uppercase tracking-wider text-muted block mb-2">
                        Calibrated Anchor Responses (Bands 2, 3, 4)
                      </span>
                      <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
                        {criterion.anchors.map((anc) => (
                          <div
                            key={anc.band}
                            className="p-2.5 rounded-md bg-surface-secondary border border-border text-xs"
                          >
                            <span className="font-bold text-accent block mb-1">
                              {formatRubricBandLabel(anc.band)} Anchor
                            </span>
                            <p className="text-muted italic line-clamp-3">"{anc.responseText}"</p>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </Card.Content>
              </Card>
            ))}
          </div>
        </Tabs.Panel>
      </Tabs>

      {/* Blueprint Wizard Modal */}
      <Modal.Backdrop isOpen={isWizardOpen} onOpenChange={setIsWizardOpen}>
        <Modal.Container>
          <Modal.Dialog className="max-w-2xl max-h-[90vh] overflow-y-auto">
            <Modal.CloseTrigger />
            <Modal.Header>
              <Modal.Heading>Certificate Blueprint Wizard</Modal.Heading>
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

              {/* Preset Selector */}
              <div className="space-y-1">
                <label htmlFor="wizard-preset-select" className="text-xs font-semibold text-foreground">
                  Autofill from preset
                </label>
                <select
                  id="wizard-preset-select"
                  aria-label="Preset"
                  value={selectedPresetCode}
                  onChange={(e) => handlePresetChange(e.target.value)}
                  className="w-full px-3 py-2 rounded-md border border-border bg-surface text-foreground text-sm"
                >
                  <option value="">-- Choose an official blueprint preset --</option>
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

              {wizardSkills.length > 0 && (
                <div className="p-3 bg-surface-secondary rounded-md space-y-2 text-xs">
                  <span className="font-semibold text-foreground block">
                    Configured skills ({wizardSkills.length}):
                  </span>
                  {wizardSkills.map((sk) => (
                    <div key={sk.code} className="flex justify-between items-center text-muted">
                      <span>{sk.name} ({sk.code})</span>
                      <span>{sk.sections.length} sections · {formatModalityLabel(sk.modality)}</span>
                    </div>
                  ))}
                </div>
              )}
            </Modal.Body>
            <Modal.Footer>
              <Button size="sm" variant="secondary" onPress={() => setIsWizardOpen(false)}>
                Cancel
              </Button>
              <Button
                size="sm"
                variant="primary"
                onPress={handleSaveWizard}
                isDisabled={isSavingWizard}
              >
                Save blueprint
              </Button>
            </Modal.Footer>
          </Modal.Dialog>
        </Modal.Container>
      </Modal.Backdrop>
    </div>
  );
}
