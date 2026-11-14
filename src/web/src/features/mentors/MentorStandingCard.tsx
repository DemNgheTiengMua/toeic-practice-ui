import React from 'react';
import { Card, Chip, Alert, Table } from '@heroui/react';
import { Award, CheckCircle2, AlertTriangle, ShieldCheck, HelpCircle } from 'lucide-react';
import type { MentorLearnerStandingDto } from '../../lib/api';
import { formatStandingStatus, formatModality } from '../../lib/domain-labels';

interface MentorStandingCardProps {
  standing?: MentorLearnerStandingDto | null;
  certCode?: string;
}

export function MentorStandingCard({ standing, certCode = 'Assessment' }: MentorStandingCardProps) {
  if (!standing || standing.hasNoMeasurement) {
    return (
      <Card className="shadow-surface">
        <Card.Header>
          <div className="flex items-center gap-2">
            <Award className="w-5 h-5 text-accent" />
            <Card.Title className="text-base font-bold">Learner standing</Card.Title>
          </div>
          <Card.Description>Verified diagnostic assessment baseline</Card.Description>
        </Card.Header>
        <Card.Content>
          <Alert status="accent">
            <Alert.Indicator>
              <HelpCircle className="w-5 h-5 text-accent" />
            </Alert.Indicator>
            <Alert.Content>
              <Alert.Title>No diagnostic measurements yet</Alert.Title>
              <Alert.Description>
                Nothing on {standing?.certCode || certCode} has been measured yet. Complete a placement or mock exam to establish baseline proficiency.
              </Alert.Description>
            </Alert.Content>
          </Alert>
        </Card.Content>
      </Card>
    );
  }

  const skills = standing.skills || [];

  return (
    <Card className="shadow-surface">
      <Card.Header>
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <div className="flex items-center gap-2">
              <Award className="w-5 h-5 text-accent" />
              <Card.Title className="text-base font-bold">Learner standing</Card.Title>
            </div>
            <Card.Description>
              Certificate: <span className="font-mono-tabular font-semibold text-foreground">{standing.certCode}</span>
            </Card.Description>
          </div>

          <div className="flex items-center gap-2">
            {standing.overallLevelCode && (
              <Chip size="md" color="accent" variant="primary" className="font-mono-tabular font-bold">
                Level {standing.overallLevelCode}
              </Chip>
            )}
            {standing.limitingSkillCode && (
              <Chip size="sm" variant="soft" color="warning" className="font-mono-tabular">
                Limiting: {standing.limitingSkillCode}
              </Chip>
            )}
          </div>
        </div>
      </Card.Header>

      <Card.Content className="space-y-4">
        {skills.length === 0 ? (
          <p className="text-sm text-muted m-0">No skill breakdowns available.</p>
        ) : (
          <Table>
            <Table.ScrollContainer>
              <Table.Content aria-label="Skill standing table">
                <Table.Header>
                  <Table.Column isRowHeader>Skill / Modality</Table.Column>
                  <Table.Column>Measurement status</Table.Column>
                  <Table.Column>Score level</Table.Column>
                  <Table.Column>Position in band</Table.Column>
                  <Table.Column className="text-right">Validity</Table.Column>
                </Table.Header>
                <Table.Body>
                  {skills.map((skill, idx) => {
                    const statusDisplay = formatStandingStatus(skill.status);
                    const isMeasured = (skill.status || '').toLowerCase() === 'measured';
                    return (
                      <Table.Row key={skill.skillId ?? idx}>
                        <Table.Cell className="font-medium text-foreground">
                          {skill.skillName || formatModality(skill.modality)}
                          {skill.skillCode && (
                            <span className="ml-1.5 text-xs text-muted font-mono-tabular">
                              ({skill.skillCode})
                            </span>
                          )}
                        </Table.Cell>

                        <Table.Cell>
                          <Chip
                            size="sm"
                            color={statusDisplay.tone}
                            variant="soft"
                          >
                            {statusDisplay.label}
                          </Chip>
                        </Table.Cell>

                        <Table.Cell className="font-mono-tabular font-semibold">
                          {isMeasured && skill.levelCode ? skill.levelCode : '—'}
                        </Table.Cell>

                        <Table.Cell className="font-mono-tabular text-xs">
                          {isMeasured && skill.positionInBand != null ? (
                            <span>Position in band: {skill.positionInBand}</span>
                          ) : (
                            '—'
                          )}
                        </Table.Cell>

                        <Table.Cell className="text-right">
                          {skill.isStale ? (
                            <Chip size="sm" color="warning" variant="soft">
                              Assessment may be out of date
                            </Chip>
                          ) : (
                            <span className="text-xs text-muted">Current</span>
                          )}
                        </Table.Cell>
                      </Table.Row>
                    );
                  })}
                </Table.Body>
              </Table.Content>
            </Table.ScrollContainer>
          </Table>
        )}
      </Card.Content>
    </Card>
  );
}
