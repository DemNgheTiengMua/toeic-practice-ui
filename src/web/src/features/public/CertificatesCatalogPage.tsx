import React, { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { Alert, Breadcrumbs, Button, Card, Chip, Spinner } from '@heroui/react';
import { ArrowRight, CheckCircle2, RefreshCw, ShieldAlert } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { BandStrip } from './BandStrip';
import { formatCertificateHeading, getSkillQuestionCount } from './catalogHelpers';

export function CertificatesCatalogPage() {
  const { isAuthenticated, isStaff, learnerContext, switchCertificate } = useAuth();
  const navigate = useNavigate();
  const [switchingCode, setSwitchingCode] = useState<string | null>(null);
  const [switchError, setSwitchError] = useState<string | null>(null);

  const certificatesQuery = useQuery({
    queryKey: ['catalog', 'certificates'],
    queryFn: () => api.catalog.certificates(),
    staleTime: 60_000,
    retry: false,
  });

  const certificates = certificatesQuery.data ?? [];
  const activeCertCode = learnerContext?.active?.code;

  const heading =
    certificates.length === 1
      ? 'One certificate, one account'
      : `${certificates.length} certificates, one account`;

  const handleSwitch = async (code: string) => {
    setSwitchError(null);
    setSwitchingCode(code);
    try {
      await switchCertificate(code);
      await navigate({ to: '/dashboard' });
    } catch {
      setSwitchError(`Could not switch active certificate to ${code}. Please try again.`);
    } finally {
      setSwitchingCode(null);
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
        <Breadcrumbs.Item>Certificates</Breadcrumbs.Item>
      </Breadcrumbs>

      <div className="max-w-2xl">
        <h1 className="font-display text-3xl font-bold tracking-tight text-foreground m-0">
          {certificatesQuery.isLoading ? 'Certificates' : heading}
        </h1>
        <p className="text-sm sm:text-base text-muted mt-2 mb-0">
          Pick the certificate you are working toward. Your placement exam, learning path, practice
          bank, and mock exams all follow that certificate's real structure and scoring — and you
          can switch at any time.
        </p>
      </div>

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

      {certificatesQuery.isLoading ? (
        <Card>
          <Card.Content className="flex items-center gap-3 py-8">
            <Spinner size="sm" />
            <span className="text-sm text-muted">Loading certificate catalog...</span>
          </Card.Content>
        </Card>
      ) : certificatesQuery.isError ? (
        <Alert status="danger" role="alert">
          <Alert.Indicator>
            <ShieldAlert className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Certificate catalog temporarily unavailable</Alert.Title>
            <Alert.Description>
              Could not load the live certificate list right now.
            </Alert.Description>
          </Alert.Content>
          <Button
            size="sm"
            variant="secondary"
            onPress={() => void certificatesQuery.refetch()}
          >
            <RefreshCw className="w-3.5 h-3.5" />
            Retry
          </Button>
        </Alert>
      ) : (
        <div className="flex flex-col gap-6">
          {certificates.map((cert) => {
            const isActive =
              Boolean(activeCertCode) &&
              activeCertCode!.toLowerCase() === cert.code.toLowerCase();
            const certHeading = formatCertificateHeading(cert);

            return (
              <section
                key={cert.code}
                id={`cert-${cert.code.toLowerCase()}`}
                aria-labelledby={`heading-${cert.code.toLowerCase()}`}
                className="flex flex-col gap-3"
              >
                <h2
                  id={`heading-${cert.code.toLowerCase()}`}
                  className="font-display text-xl font-bold text-foreground m-0"
                >
                  {certHeading}
                </h2>
                <Card>
                  <Card.Content className="flex flex-col gap-5">
                    {cert.description && (
                      <p className="text-sm text-foreground m-0">{cert.description}</p>
                    )}

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      {cert.skills.map((skill) => {
                        const questionCount = getSkillQuestionCount(skill);
                        return (
                          <div
                            key={skill.code}
                            className="rounded-lg bg-surface-secondary p-3.5 flex flex-col gap-2"
                          >
                            <div className="font-semibold text-sm text-foreground">
                              {skill.name}
                              {skill.durationMinutes != null && (
                                <span className="text-muted font-normal">
                                  {' '}
                                  — {skill.durationMinutes} minutes
                                </span>
                              )}
                              {questionCount > 0 && (
                                <span className="text-muted font-mono-tabular font-normal">
                                  {' '}
                                  · {questionCount} questions
                                </span>
                              )}
                            </div>

                            {skill.sections.length > 0 && (
                              <ul className="text-xs text-muted m-0 pl-4 flex flex-col gap-1">
                                {skill.sections.map((sec) => (
                                  <li key={sec.code}>
                                    <span className="font-mono-tabular text-foreground">
                                      {sec.code}
                                    </span>{' '}
                                    — {sec.name}{' '}
                                    <span className="font-mono-tabular">
                                      ({sec.expectedQuestionCount})
                                    </span>
                                  </li>
                                ))}
                              </ul>
                            )}
                          </div>
                        );
                      })}
                    </div>

                    {cert.bands.length > 0 && (
                      <div className="flex flex-col gap-2">
                        <span className="text-xs font-semibold text-foreground">Score bands</span>
                        <BandStrip bands={cert.bands} ownerName={cert.code} />
                      </div>
                    )}
                  </Card.Content>

                  <Card.Footer className="flex flex-wrap items-center justify-between gap-3">
                    <div className="flex flex-wrap items-center gap-2">
                      {isAuthenticated && !isStaff ? (
                        isActive ? (
                          <Chip size="md" color="success" variant="soft">
                            <CheckCircle2 className="w-3.5 h-3.5 mr-1" />
                            Current certificate
                          </Chip>
                        ) : (
                          <Button
                            variant="primary"
                            isPending={switchingCode === cert.code}
                            isDisabled={switchingCode !== null}
                            onPress={() => void handleSwitch(cert.code)}
                          >
                            {switchingCode === cert.code
                              ? `Switching to ${cert.code}...`
                              : `Switch to ${cert.code}`}
                          </Button>
                        )
                      ) : !isAuthenticated ? (
                        <Link to="/register" className="no-underline">
                          <Button variant="primary">Start with {cert.code}</Button>
                        </Link>
                      ) : null}
                    </div>

                    <Link
                      to="/certificates/$slug"
                      params={{ slug: cert.code.toLowerCase() }}
                      className="no-underline"
                    >
                      <Button variant="secondary" size="sm">
                        Inspect structure & bands
                        <ArrowRight className="w-3.5 h-3.5" />
                      </Button>
                    </Link>
                  </Card.Footer>
                </Card>
              </section>
            );
          })}
        </div>
      )}

      <Card>
        <Card.Content className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <p className="font-semibold text-foreground m-0">
              Not sure which one fits your goal?
            </p>
            <p className="text-sm text-muted m-0 mt-0.5">
              Create a free account and take the placement exam — it works for every certificate.
            </p>
          </div>
          {isAuthenticated && !isStaff ? (
            <Link to="/placement" className="no-underline">
              <Button variant="primary">Take placement test</Button>
            </Link>
          ) : !isAuthenticated ? (
            <Link to="/register" className="no-underline">
              <Button variant="primary">Start for free</Button>
            </Link>
          ) : null}
        </Card.Content>
      </Card>
    </div>
  );
}
