import React from 'react';
import { Link } from '@tanstack/react-router';
import {
  Accordion,
  Alert,
  Breadcrumbs,
  Button,
  Card,
  Chip,
  Spinner,
} from '@heroui/react';
import {
  Check,
  ChevronDown,
  Coins,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { api, type PackageDto } from '../../lib/api';
import { formatVnd, formatVndUnitPrice } from '../../lib/security';
import { useAuth } from '../../context/AuthContext';

const FAQ_ITEMS = [
  {
    id: 'expiry',
    question: 'Do credits expire?',
    answer:
      'No. Purchased credits remain in your wallet ledger until you use them on a mock exam, AI free-response grading, or placement retake.',
  },
  {
    id: 'retake',
    question: 'How does placement retake pricing work?',
    answer:
      'Your first placement exam for each certificate is free. Re-taking placement on the same certificate uses 5 credits when a specific target band is configured, or 3 credits for a general retake.',
  },
  {
    id: 'free-practice',
    question: 'What stays free without credits?',
    answer:
      'Unlimited practice drilling by skill or section, deterministic weakness analysis across topics and sections, the learning path, and your first placement test per certificate cost zero credits.',
  },
  {
    id: 'refunds',
    question: 'How are payment reversals or refunds handled?',
    answer:
      'If a payment order fails or is reversed before settlement, no credits are posted. Approved billing refunds are recorded as explicit ledger adjustments so your transaction history stays auditable.',
  },
];

export function PricingPage() {
  const { isAuthenticated, isStaff, learnerContext } = useAuth();

  const packagesQuery = useQuery({
    queryKey: ['catalog', 'packages'],
    queryFn: () => api.catalog.packages(),
  });

  const packages: PackageDto[] = React.useMemo(() => {
    const raw = packagesQuery.data ?? [];
    return raw
      .filter((pkg) => pkg.isActive)
      .slice()
      .sort((a, b) => a.displayOrder - b.displayOrder || a.price - b.price);
  }, [packagesQuery.data]);

  const showBalance = isAuthenticated && !isStaff && learnerContext != null;

  return (
    <div className="space-y-8">
      <Breadcrumbs>
        <Breadcrumbs.Item href="/">Home</Breadcrumbs.Item>
        <Breadcrumbs.Item>Pricing</Breadcrumbs.Item>
      </Breadcrumbs>

      <header className="space-y-3 border-b border-border pb-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="space-y-2 max-w-2xl">
            <p className="text-xs font-medium uppercase tracking-wider text-muted">
              Credit packages
            </p>
            <h1 className="font-display text-3xl font-semibold tracking-tight text-foreground sm:text-4xl">
              Pay for what you test, never a monthly lock-in
            </h1>
            <p className="text-base text-muted">
              Diagnostics, practice drilling, and your first placement per certificate are free. Credits cover full timed mock exams, AI writing and speaking grading, and placement retakes.
            </p>
          </div>

          {showBalance && (
            <Card variant="secondary" className="min-w-[220px] border border-border">
              <Card.Content className="flex items-center gap-3 p-4">
                <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-accent/10 text-accent">
                  <Coins className="h-5 w-5" aria-hidden="true" />
                </span>
                <div>
                  <div className="text-xs text-muted">Current credit balance</div>
                  <div className="font-mono-tabular text-lg font-semibold text-foreground">
                    {learnerContext.creditBalance} credits
                  </div>
                </div>
              </Card.Content>
            </Card>
          )}
        </div>
      </header>

      {packagesQuery.isLoading ? (
        <div className="flex items-center gap-3 py-12 text-sm text-muted">
          <Spinner size="sm" />
          <span>Loading credit packages…</span>
        </div>
      ) : packagesQuery.isError ? (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Credit packages are temporarily unavailable</Alert.Title>
            <Alert.Description>
              We could not reach the catalog service right now. Please try again in a moment.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      ) : packages.length === 0 ? (
        <Alert status="warning">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>No active credit packages</Alert.Title>
            <Alert.Description>
              No packages are currently available for purchase. Free diagnostics and practice remain open.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      ) : (
        <section
          aria-label="Available credit packages"
          className="grid gap-5 md:grid-cols-2 lg:grid-cols-3"
        >
          {packages.map((pkg) => {
            const unitPrice = formatVndUnitPrice(pkg.price, pkg.credits);
            const features =
              pkg.featureList && pkg.featureList.length > 0
                ? pkg.featureList
                : (pkg.features ?? '')
                    .split(/\r?\n/)
                    .map((line) => line.trim())
                    .filter(Boolean);

            return (
              <Card
                key={pkg.packageId}
                className={
                  pkg.isFeatured
                    ? 'flex h-full flex-col border-2 border-accent'
                    : 'flex h-full flex-col border border-border'
                }
              >
                <Card.Header className="flex flex-col gap-2 pb-2">
                  <div className="flex items-center justify-between gap-2">
                    <Card.Title className="font-display text-xl font-semibold text-foreground">
                      {pkg.name}
                    </Card.Title>
                    {pkg.isFeatured && (
                      <Chip size="sm" color="accent" variant="soft">
                        Popular
                      </Chip>
                    )}
                  </div>
                  <div className="flex items-baseline gap-2 pt-1">
                    <span className="font-mono-tabular text-3xl font-semibold tracking-tight text-foreground">
                      {formatVnd(pkg.price)}
                    </span>
                  </div>
                  <div className="flex flex-wrap items-center gap-2 text-sm text-muted">
                    <span className="font-mono-tabular font-medium text-foreground">
                      {pkg.credits} {pkg.credits === 1 ? 'credit' : 'credits'}
                    </span>
                    {unitPrice && (
                      <>
                        <span aria-hidden="true">·</span>
                        <span className="font-mono-tabular">{unitPrice}</span>
                      </>
                    )}
                  </div>
                </Card.Header>

                <Card.Content className="flex-1 pt-2">
                  {features.length > 0 && (
                    <ul className="space-y-2 text-sm text-foreground">
                      {features.map((feat, idx) => (
                        <li key={idx} className="flex items-start gap-2">
                          <Check
                            className="mt-0.5 h-4 w-4 shrink-0 text-success"
                            aria-hidden="true"
                          />
                          <span>{feat}</span>
                        </li>
                      ))}
                    </ul>
                  )}
                </Card.Content>

                <Card.Footer className="pt-4">
                  {isStaff ? (
                    <Link to="/admin/finance" className="w-full">
                      <Button variant="secondary" fullWidth>
                        Manage packages
                      </Button>
                    </Link>
                  ) : isAuthenticated ? (
                    <Link
                      to="/wallet"
                      search={{ packageId: pkg.packageId } as never}
                      className="w-full"
                    >
                      <Button
                        variant={pkg.isFeatured ? 'primary' : 'secondary'}
                        fullWidth
                      >
                        Choose this package
                      </Button>
                    </Link>
                  ) : (
                    <Link
                      to="/sign-in"
                      search={{ returnUrl: '/wallet' } as never}
                      className="w-full"
                    >
                      <Button
                        variant={pkg.isFeatured ? 'primary' : 'secondary'}
                        fullWidth
                      >
                        Choose this package
                      </Button>
                    </Link>
                  )}
                </Card.Footer>
              </Card>
            );
          })}
        </section>
      )}

      <section aria-label="Free versus credit actions" className="grid gap-4 md:grid-cols-2">
        <Card variant="secondary" className="border border-border">
          <Card.Header className="pb-2">
            <Card.Title className="flex items-center gap-2 font-display text-lg">
              <ShieldCheck className="h-5 w-5 text-success" aria-hidden="true" />
              Always free
            </Card.Title>
          </Card.Header>
          <Card.Content className="space-y-2 text-sm text-muted">
            <p>First placement test for every certificate on your account.</p>
            <p>Skill &amp; section practice sessions, learning paths, and accuracy diagnostics.</p>
          </Card.Content>
        </Card>

        <Card variant="secondary" className="border border-border">
          <Card.Header className="pb-2">
            <Card.Title className="flex items-center gap-2 font-display text-lg">
              <Sparkles className="h-5 w-5 text-accent" aria-hidden="true" />
              Uses credits
            </Card.Title>
          </Card.Header>
          <Card.Content className="space-y-2 text-sm text-muted">
            <p>Full timed mock exam sittings with official section rules and band scaling.</p>
            <p>AI writing &amp; speaking rubric gradings, and subsequent placement retakes (5 credits scoped, 3 credits general).</p>
          </Card.Content>
        </Card>
      </section>

      <section aria-label="Frequently asked questions" className="space-y-3">
        <h2 className="font-display text-2xl font-semibold text-foreground">
          Questions about credits &amp; billing
        </h2>
        <Card className="border border-border">
          <Card.Content className="p-2 sm:p-4">
            <Accordion>
              {FAQ_ITEMS.map((item) => (
                <Accordion.Item key={item.id} id={item.id}>
                  <Accordion.Heading>
                    <Accordion.Trigger className="flex w-full items-center justify-between py-3 text-left text-sm font-medium text-foreground">
                      <span>{item.question}</span>
                      <Accordion.Indicator>
                        <ChevronDown className="h-4 w-4 text-muted" aria-hidden="true" />
                      </Accordion.Indicator>
                    </Accordion.Trigger>
                  </Accordion.Heading>
                  <Accordion.Panel>
                    <Accordion.Body className="pb-3 text-sm leading-relaxed text-muted">
                      {item.answer}
                    </Accordion.Body>
                  </Accordion.Panel>
                </Accordion.Item>
              ))}
            </Accordion>
          </Card.Content>
        </Card>
      </section>
    </div>
  );
}
