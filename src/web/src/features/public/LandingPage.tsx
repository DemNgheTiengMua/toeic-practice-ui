import React from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, Chip } from '@heroui/react';
import { ArrowRight, BarChart3, BookOpen, CheckCircle2, Compass, FileCheck2, MessageSquare } from 'lucide-react';
import { api, type CertificateDto } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { getCertificateScoreRange } from './catalogHelpers';

function buildPreview(cert: CertificateDto) {
  const firstSkill = cert.skills[0];
  const firstSection = firstSkill?.sections[0];
  const range = getCertificateScoreRange(cert) || '—';
  const nextStepTitle = firstSection
    ? `${firstSkill.name} — ${firstSection.name}`
    : `${firstSkill?.name ?? 'Practice'} practice`;
  const nextStepMeta = firstSection
    ? `${firstSection.expectedQuestionCount} questions`
    : 'from this certificate';

  return {
    certificateName: cert.name,
    targetScore: range,
    nextStepTitle,
    nextStepMeta,
  };
}

export function LandingPage() {
  const { isAuthenticated, isStaff } = useAuth();

  const certificatesQuery = useQuery({
    queryKey: ['catalog', 'certificates'],
    queryFn: async () => {
      try {
        return await api.catalog.certificates();
      } catch {
        // Marketing page remains readable even when the catalog API is temporarily unavailable.
        return [] as CertificateDto[];
      }
    },
    staleTime: 60_000,
    retry: false,
  });

  const certificates = certificatesQuery.data ?? [];
  const preview = certificates.length > 0 ? buildPreview(certificates[0]) : null;
  const primaryActionTo = isAuthenticated ? (isStaff ? '/admin' : '/dashboard') : '/register';

  return (
    <div className="flex flex-col gap-12 py-4">
      <section className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center">
        <div className="lg:col-span-7 flex flex-col gap-5">
          <div className="flex flex-wrap items-center gap-2">
            <Chip size="sm" color="accent" variant="soft">
              Diagnostic-first practice
            </Chip>
            <Chip size="sm" color="success" variant="soft">
              First placement free per certificate
            </Chip>
          </div>

          <h1 className="font-display text-4xl sm:text-5xl font-bold tracking-tight text-foreground m-0 leading-[1.08]">
            Know where you stand.
            <br />
            <span className="text-accent">Know what's next.</span>
          </h1>

          <p className="text-base sm:text-lg text-muted max-w-2xl m-0">
            Practice for an English certificate with a level-matched path, skill-based exercises,
            and progress that's your own.
          </p>

          <div className="flex flex-wrap items-center gap-3">
            <Link to={primaryActionTo} className="no-underline">
              <Button variant="primary" size="lg">
                {isAuthenticated ? 'Open workspace' : 'Start for free'}
                <ArrowRight className="w-4 h-4" />
              </Button>
            </Link>
            <Link to="/certificates" className="no-underline">
              <Button variant="secondary" size="lg">
                Explore certificates
              </Button>
            </Link>
          </div>

          {certificates.length > 0 && (
            <div className="flex flex-wrap items-center gap-2 pt-1">
              <span className="text-xs font-semibold tracking-wide text-muted uppercase">
                Start with
              </span>
              {certificates.map((cert) => (
                <Link
                  key={cert.code}
                  to="/certificates/$slug"
                  params={{ slug: cert.code.toLowerCase() }}
                  className="no-underline"
                >
                  <Chip size="sm" variant="secondary" className="font-mono-tabular">
                    {cert.code}
                  </Chip>
                </Link>
              ))}
            </div>
          )}
        </div>

        {preview && (
          <div className="lg:col-span-5">
            <Card aria-label="Sample illustration: a learner's dashboard">
              <Card.Header className="flex flex-row items-center justify-between gap-2 border-b border-separator pb-3">
                <span className="text-xs font-semibold tracking-wide text-muted uppercase">
                  Learning path
                </span>
                <Chip size="sm" color="success" variant="soft">
                  {preview.certificateName} · Sample
                </Chip>
              </Card.Header>
              <Card.Content className="flex flex-col gap-4 pt-4">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <p className="text-xs font-medium text-muted m-0">Sample path</p>
                    <h2 className="font-display text-xl font-bold text-foreground m-0 mt-0.5">
                      Step by step to your target.
                    </h2>
                  </div>
                  <div className="text-right shrink-0">
                    <span className="text-[11px] font-medium text-muted block">Range</span>
                    <strong className="font-mono-tabular text-lg text-accent">
                      {preview.targetScore}
                    </strong>
                    <span className="text-xs text-muted block">{preview.certificateName}</span>
                  </div>
                </div>

                <div className="rounded-lg bg-surface-secondary p-3 flex flex-col gap-2">
                  <div className="flex items-center justify-between text-xs font-medium">
                    <span>Progress this week</span>
                    <span className="font-mono-tabular font-semibold">68%</span>
                  </div>
                  <div
                    className="h-2 rounded-full bg-surface-tertiary overflow-hidden"
                    role="img"
                    aria-label="Progress this week: 68 percent"
                  >
                    <div className="h-full bg-accent rounded-full" style={{ width: '68%' }} />
                  </div>
                  <div className="flex items-center justify-between text-[11px] text-muted">
                    <span>4 / 6 sessions</span>
                    <span>Personalized path</span>
                  </div>
                </div>

                <div className="rounded-lg border border-border p-3 flex items-center justify-between gap-3">
                  <div>
                    <span className="text-[11px] font-mono-tabular font-semibold text-accent block">
                      NEXT STEP · 03
                    </span>
                    <strong className="text-sm text-foreground block">
                      {preview.nextStepTitle}
                    </strong>
                    <span className="text-xs text-muted">{preview.nextStepMeta}</span>
                  </div>
                  <ArrowRight className="w-4 h-4 text-muted shrink-0" />
                </div>
              </Card.Content>
            </Card>
          </div>
        )}
      </section>

      {certificates.length > 0 && (
        <section aria-labelledby="certificates-title" className="flex flex-col gap-4">
          <div className="flex flex-wrap items-end justify-between gap-4">
            <div>
              <h2
                id="certificates-title"
                className="font-display text-2xl font-bold tracking-tight text-foreground m-0"
              >
                Pick a certificate. Start your journey.
              </h2>
              <p className="text-sm text-muted m-0 mt-1">
                Every certificate uses its own real sections, scoring scale, and band boundaries.
              </p>
            </div>
            <Link to="/certificates" className="text-sm font-medium text-accent hover:underline">
              Compare all certificates
            </Link>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {certificates.map((cert, index) => {
              const skillsSummary =
                cert.skills.length === 0
                  ? 'No skills configured yet'
                  : cert.skills.map((s) => s.name).join(' · ');
              const range = getCertificateScoreRange(cert);

              return (
                <Card key={cert.code} className="flex flex-col justify-between">
                  <Card.Header>
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-mono-tabular text-xs font-semibold text-accent">
                        {String(index + 1).padStart(2, '0')} / {cert.code}
                      </span>
                      {range && (
                        <Chip size="sm" variant="soft" className="font-mono-tabular">
                          {range}
                        </Chip>
                      )}
                    </div>
                    <Card.Title className="mt-1">{cert.name}</Card.Title>
                    <Card.Description>{skillsSummary}</Card.Description>
                  </Card.Header>
                  <Card.Footer className="pt-2">
                    <Link
                      to="/certificates/$slug"
                      params={{ slug: cert.code.toLowerCase() }}
                      className="no-underline w-full"
                    >
                      <Button variant="secondary" size="sm" className="w-full">
                        See test details
                        <ArrowRight className="w-3.5 h-3.5" />
                      </Button>
                    </Link>
                  </Card.Footer>
                </Card>
              );
            })}
          </div>
        </section>
      )}

      <section id="method" aria-labelledby="method-title" className="flex flex-col gap-4">
        <div>
          <h2
            id="method-title"
            className="font-display text-2xl font-bold tracking-tight text-foreground m-0"
          >
            Study with direction.
          </h2>
          <p className="text-sm text-muted m-0 mt-1">
            Start from your current level, focus on the skills that need work, and track every step
            forward.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <Card>
            <Card.Header>
              <div className="flex items-center justify-between">
                <span className="font-mono-tabular text-xs font-bold text-accent">01</span>
                <Chip size="sm" color="success" variant="soft">
                  Free
                </Chip>
              </div>
              <Card.Title className="flex items-center gap-2 mt-1">
                <Compass className="w-4 h-4 text-accent" />
                Find your level
              </Card.Title>
            </Card.Header>
            <Card.Content>
              <p className="text-sm text-muted m-0">
                Take a free placement exam to see your strengths and what to practice next.
              </p>
            </Card.Content>
          </Card>

          <Card>
            <Card.Header>
              <div className="flex items-center justify-between">
                <span className="font-mono-tabular text-xs font-bold text-accent">02</span>
                <Chip size="sm" color="accent" variant="soft">
                  Personalized
                </Chip>
              </div>
              <Card.Title className="flex items-center gap-2 mt-1">
                <CheckCircle2 className="w-4 h-4 text-accent" />
                Get a matching path
              </Card.Title>
            </Card.Header>
            <Card.Content>
              <p className="text-sm text-muted m-0">
                Pick a target score. Your study plan is suggested based on your certificate and
                level.
              </p>
            </Card.Content>
          </Card>

          <Card>
            <Card.Header>
              <div className="flex items-center justify-between">
                <span className="font-mono-tabular text-xs font-bold text-accent">03</span>
                <Chip size="sm" variant="soft">
                  Tracked
                </Chip>
              </div>
              <Card.Title className="flex items-center gap-2 mt-1">
                <BarChart3 className="w-4 h-4 text-accent" />
                Practice and improve
              </Card.Title>
            </Card.Header>
            <Card.Content>
              <p className="text-sm text-muted m-0">
                Practice each skill, see your weakness analysis, and adjust your next step.
              </p>
            </Card.Content>
          </Card>
        </div>
      </section>

      <section id="features" aria-labelledby="features-title" className="flex flex-col gap-4">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h2
              id="features-title"
              className="font-display text-2xl font-bold tracking-tight text-foreground m-0"
            >
              From practice to exam day.
            </h2>
            <p className="text-sm text-muted m-0 mt-1">
              One place to practice regularly, understand your results, and know what to do next.
            </p>
          </div>
          <Link to={primaryActionTo} className="no-underline">
            <Button variant="primary" size="sm">
              Build my path
            </Button>
          </Link>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <Card>
            <Card.Header>
              <Card.Title className="flex items-center gap-2">
                <BookOpen className="w-4 h-4 text-accent" />
                Practice by skill
              </Card.Title>
            </Card.Header>
            <Card.Content>
              <p className="text-sm text-muted m-0">
                Content broken down by exam section, with answers and explanations to reinforce what
                you learn.
              </p>
            </Card.Content>
          </Card>

          <Card>
            <Card.Header>
              <Card.Title className="flex items-center gap-2">
                <BarChart3 className="w-4 h-4 text-accent" />
                See where you need work
              </Card.Title>
            </Card.Header>
            <Card.Content>
              <p className="text-sm text-muted m-0">
                Track practice and mock exam results to know which skills and topics to prioritize.
              </p>
            </Card.Content>
          </Card>

          <Card>
            <Card.Header>
              <Card.Title className="flex items-center gap-2">
                <MessageSquare className="w-4 h-4 text-accent" />
                Get specific feedback
              </Card.Title>
            </Card.Header>
            <Card.Content>
              <p className="text-sm text-muted m-0">
                For speaking and writing, get feedback against a grading rubric so you know how to
                improve.
              </p>
            </Card.Content>
          </Card>

          <Card>
            <Card.Header>
              <Card.Title className="flex items-center gap-2">
                <FileCheck2 className="w-4 h-4 text-accent" />
                Mock exams close to the real thing
              </Card.Title>
            </Card.Header>
            <Card.Content>
              <p className="text-sm text-muted m-0">
                Simulate the exam to get used to the time pressure and check how ready you are.
              </p>
            </Card.Content>
          </Card>
        </div>
      </section>

      <footer className="border-t border-separator pt-6 pb-2 flex flex-wrap items-center justify-between gap-4 text-xs text-muted">
        <p className="m-0">Focus on what matters. Reach your target with confidence.</p>
        <div className="flex flex-wrap items-center gap-4">
          <Link to="/certificates" className="text-muted hover:text-foreground no-underline">
            Certificates
          </Link>
          <Link to="/pricing" className="text-muted hover:text-foreground no-underline">
            Pricing
          </Link>
          <Link to="/terms" className="text-muted hover:text-foreground no-underline">
            Terms
          </Link>
          <Link to="/privacy" className="text-muted hover:text-foreground no-underline">
            Privacy
          </Link>
        </div>
      </footer>
    </div>
  );
}
