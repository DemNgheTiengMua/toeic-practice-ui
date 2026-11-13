import React, { useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { Alert, Breadcrumbs, Button, Card, Chip, Spinner, Table, Tabs } from '@heroui/react';
import { ArrowRight, CheckCircle2, ShieldAlert } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { BandStrip } from './BandStrip';
import {
  formatCertificateHeading,
  getCertificateScoreRange,
  getSkillQuestionCount,
  getSortedBands,
} from './catalogHelpers';

export function CertificateDetailPage() {
  const { slug } = useParams({ strict: false }) as { slug?: string };
  const { isAuthenticated, isStaff, learnerContext, switchCertificate } = useAuth();
  const navigate = useNavigate();
  const [isSwitching, setIsSwitching] = useState(false);
  const [switchError, setSwitchError] = useState<string | null>(null);

  const certificatesQuery = useQuery({
    queryKey: ['catalog', 'certificates'],
    queryFn: () => api.catalog.certificates(),
    staleTime: 60_000,
    retry: false,
  });

  const cert = (certificatesQuery.data ?? []).find(
    (c) => c.code.toLowerCase() === (slug ?? '').toLowerCase(),
  );

  if (certificatesQuery.isLoading) {
    return (
      <div className="max-w-5xl mx-auto py-8">
        <Card>
          <Card.Content className="flex items-center gap-3 py-8">
            <Spinner size="sm" />
            <span className="text-sm text-muted">Loading certificate details...</span>
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (certificatesQuery.isError || !cert) {
    return (
      <div className="max-w-3xl mx-auto py-6 flex flex-col gap-4">
        <Alert status="danger" role="alert">
          <Alert.Indicator>
            <ShieldAlert className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Certificate not found</Alert.Title>
            <Alert.Description>
              We could not find a certificate matching “{slug ?? ''}” in the active catalog.
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <div>
          <Link to="/certificates" className="no-underline">
            <Button variant="secondary">Back to certificates</Button>
          </Link>
        </div>
      </div>
    );
  }

  const isActive =
    Boolean(learnerContext?.active?.code) &&
    learnerContext!.active.code.toLowerCase() === cert.code.toLowerCase();
  const sortedBands = getSortedBands(cert.bands);
  const scoreRange = getCertificateScoreRange(cert);
  const totalQuestions = cert.skills.reduce((sum, s) => sum + getSkillQuestionCount(s), 0);
  const totalMinutes = cert.skills.reduce((sum, s) => sum + (s.durationMinutes ?? 0), 0);

  const handleSwitch = async () => {
    setSwitchError(null);
    setIsSwitching(true);
    try {
      await switchCertificate(cert.code);
      await navigate({ to: '/dashboard' });
    } catch {
      setSwitchError(`Could not switch active certificate to ${cert.code}. Please try again.`);
    } finally {
      setIsSwitching(false);
    }
  };

  return (
    <div className="max-w-5xl mx-auto flex flex-col gap-6 py-4">
      <Breadcrumbs>
        <Breadcrumbs.Item>
          <Link to="/" className="no-underline text-inherit">
            Home
          </Link>
        </Breadcrumbs.Item>
        <Breadcrumbs.Item>
          <Link to="/certificates" className="no-underline text-inherit">
            Certificates
          </Link>
        </Breadcrumbs.Item>
        <Breadcrumbs.Item>{cert.code}</Breadcrumbs.Item>
      </Breadcrumbs>

      {switchError && (
        <Alert status="danger" role="alert">
          <Alert.Indicator>
            <ShieldAlert className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Switch failed</Alert.Title>
            <Alert.Description>{switchError}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="max-w-2xl">
          <div className="flex flex-wrap items-center gap-2 mb-2">
            <Chip size="sm" color="accent" variant="soft" className="font-mono-tabular">
              {cert.code}
            </Chip>
            {scoreRange && (
              <Chip size="sm" variant="secondary" className="font-mono-tabular">
                Scale {scoreRange}
              </Chip>
            )}
            {totalQuestions > 0 && (
              <Chip size="sm" variant="secondary" className="font-mono-tabular">
                {totalQuestions} questions
              </Chip>
            )}
            {totalMinutes > 0 && (
              <Chip size="sm" variant="secondary" className="font-mono-tabular">
                {totalMinutes} min
              </Chip>
            )}
          </div>
          <h1 className="font-display text-3xl font-bold tracking-tight text-foreground m-0">
            {formatCertificateHeading(cert)}
          </h1>
          {cert.description && (
            <p className="text-sm sm:text-base text-muted mt-2 mb-0">{cert.description}</p>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {isAuthenticated && !isStaff ? (
            isActive ? (
              <>
                <Chip size="md" color="success" variant="soft">
                  <CheckCircle2 className="w-3.5 h-3.5 mr-1" />
                  Current certificate
                </Chip>
                <Link to="/placement" className="no-underline">
                  <Button variant="primary">
                    Placement & mock exams
                    <ArrowRight className="w-4 h-4" />
                  </Button>
                </Link>
              </>
            ) : (
              <Button
                variant="primary"
                isPending={isSwitching}
                isDisabled={isSwitching}
                onPress={() => void handleSwitch()}
              >
                {isSwitching ? `Switching to ${cert.code}...` : `Switch to ${cert.code}`}
              </Button>
            )
          ) : !isAuthenticated ? (
            <Link to="/register" className="no-underline">
              <Button variant="primary">
                Start free with {cert.code}
                <ArrowRight className="w-4 h-4" />
              </Button>
            </Link>
          ) : null}
        </div>
      </div>

      <Tabs className="w-full">
        <Tabs.ListContainer>
          <Tabs.List aria-label={`${cert.code} blueprint details`}>
            <Tabs.Tab id="skills">
              Skills & sections
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="bands">
              Score bands
              <Tabs.Indicator />
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>

        <Tabs.Panel id="skills" className="pt-4 flex flex-col gap-4">
          {cert.skills.map((skill) => (
            <Card key={skill.code}>
              <Card.Header className="flex flex-row flex-wrap items-center justify-between gap-2">
                <div>
                  <Card.Title>{skill.name}</Card.Title>
                  <Card.Description className="capitalize">
                    Modality: {skill.modality}
                  </Card.Description>
                </div>
                <div className="flex items-center gap-2">
                  {skill.durationMinutes != null && (
                    <Chip size="sm" variant="secondary" className="font-mono-tabular">
                      {skill.durationMinutes} min
                    </Chip>
                  )}
                  <Chip size="sm" variant="soft" className="font-mono-tabular">
                    {getSkillQuestionCount(skill)} questions
                  </Chip>
                </div>
              </Card.Header>
              <Card.Content>
                {skill.sections.length === 0 ? (
                  <p className="text-sm text-muted m-0">No sections configured yet.</p>
                ) : (
                  <Table>
                    <Table.ScrollContainer>
                      <Table.Content aria-label={`${skill.name} sections`}>
                        <Table.Header>
                          <Table.Column isRowHeader>Code</Table.Column>
                          <Table.Column>Section</Table.Column>
                          <Table.Column>Questions</Table.Column>
                        </Table.Header>
                        <Table.Body>
                          {skill.sections.map((sec) => (
                            <Table.Row key={sec.code}>
                              <Table.Cell className="font-mono-tabular font-semibold">
                                {sec.code}
                              </Table.Cell>
                              <Table.Cell>{sec.name}</Table.Cell>
                              <Table.Cell className="font-mono-tabular">
                                {sec.expectedQuestionCount}
                              </Table.Cell>
                            </Table.Row>
                          ))}
                        </Table.Body>
                      </Table.Content>
                    </Table.ScrollContainer>
                  </Table>
                )}
              </Card.Content>
            </Card>
          ))}
        </Tabs.Panel>

        <Tabs.Panel id="bands" className="pt-4 flex flex-col gap-4">
          <Card>
            <Card.Header>
              <Card.Title>Score bands & scale</Card.Title>
              <Card.Description>
                Aggregation rule: <span className="font-mono-tabular">{cert.scoreAggregationRule ?? 'sum'}</span>
              </Card.Description>
            </Card.Header>
            <Card.Content className="flex flex-col gap-5">
              <BandStrip bands={sortedBands} ownerName={cert.code} />

              {sortedBands.length > 0 && (
                <Table>
                  <Table.ScrollContainer>
                    <Table.Content aria-label={`${cert.code} score bands`}>
                      <Table.Header>
                        <Table.Column isRowHeader>Band</Table.Column>
                        <Table.Column>Level name</Table.Column>
                        <Table.Column>Score range</Table.Column>
                      </Table.Header>
                      <Table.Body>
                        {sortedBands.map((band) => (
                          <Table.Row key={band.code}>
                            <Table.Cell className="font-mono-tabular font-semibold">
                              {band.code}
                            </Table.Cell>
                            <Table.Cell>{band.name}</Table.Cell>
                            <Table.Cell className="font-mono-tabular">
                              {band.minTotal}–{band.maxTotal}
                            </Table.Cell>
                          </Table.Row>
                        ))}
                      </Table.Body>
                    </Table.Content>
                  </Table.ScrollContainer>
                </Table>
              )}
            </Card.Content>
          </Card>
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}
