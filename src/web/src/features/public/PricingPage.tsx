import React, { useState } from 'react';
import { Link } from '@tanstack/react-router';
import {
  Accordion,
  Breadcrumbs,
  Button,
  Card,
  Chip,
  Label,
  NumberField,
  Slider,
} from '@heroui/react';
import {
  ArrowRight,
  ChevronDown,
  Coins,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import { formatVnd } from '../../lib/security';
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

const PRICE_PER_CREDIT_VND = 20_000;
const PRESET_CREDITS = [5, 10, 20];

export function PricingPage() {
  const { isAuthenticated, isStaff, learnerContext } = useAuth();
  const [credits, setCredits] = useState<number>(5);

  const handleCreditsChange = (val: number) => {
    if (Number.isNaN(val)) return;
    const clamped = Math.max(1, Math.min(100, Math.floor(val)));
    setCredits(clamped);
  };

  const handleSliderChange = (v: number | number[]) => {
    const val = Array.isArray(v) ? v[0] : v;
    handleCreditsChange(val ?? 1);
  };

  const totalPrice = credits * PRICE_PER_CREDIT_VND;
  const showBalance = isAuthenticated && !isStaff && learnerContext != null;

  return (
    <div className="space-y-8">
      <Breadcrumbs>
        <Breadcrumbs.Item>
          <Link to="/" className="no-underline text-inherit">
            Home
          </Link>
        </Breadcrumbs.Item>
        <Breadcrumbs.Item>Pricing</Breadcrumbs.Item>
      </Breadcrumbs>

      <header className="space-y-3 border-b border-border pb-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="space-y-2 max-w-2xl">
            <p className="text-xs font-medium uppercase tracking-wider text-muted">
              Flat-rate pricing
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

      {/* Credit Calculator Card */}
      <section aria-label="Interactive Credit Calculator" className="max-w-2xl mx-auto w-full">
        <Card className="border border-border shadow-surface">
          <Card.Header className="pb-4">
            <div className="flex items-center justify-between gap-2">
              <Card.Title className="font-display text-2xl font-bold text-foreground">
                Credit Calculator
              </Card.Title>
              <Chip size="sm" color="accent" variant="soft">
                Pay-as-you-go
              </Chip>
            </div>
            <Card.Description className="text-sm font-semibold text-accent mt-1">
              Flat-rate pricing: 1 Credit = 20,000 VND
            </Card.Description>
          </Card.Header>

          <Card.Content className="space-y-6">
            <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-4">
              <NumberField
                minValue={1}
                maxValue={100}
                step={1}
                value={credits}
                onChange={handleCreditsChange}
                className="flex items-center justify-between"
              >
                <Label className="text-sm font-semibold text-foreground">
                  Credits to purchase
                </Label>
                <div className="flex items-center gap-2">
                  <NumberField.Group className="w-28 h-8">
                    <NumberField.DecrementButton />
                    <NumberField.Input
                      id="pricing-credits-input"
                      aria-label="Credits count"
                      className="w-12 text-center font-mono font-bold text-sm"
                      onChange={(e) => {
                        const val = parseInt(e.target.value, 10);
                        if (!Number.isNaN(val)) {
                          handleCreditsChange(val);
                        }
                      }}
                    />
                    <NumberField.IncrementButton />
                  </NumberField.Group>
                  <span className="text-xs text-muted">credits</span>
                </div>
              </NumberField>

              {/* HeroUI v3 Compound Slider */}
              <div className="pt-2">
                <Slider
                  aria-label="Credit slider"
                  minValue={1}
                  maxValue={100}
                  step={1}
                  value={credits}
                  onChange={handleSliderChange}
                >
                  <div className="flex items-center justify-between text-xs text-muted mb-1">
                    <span>1 credit</span>
                    <Slider.Output className="font-mono-tabular font-semibold text-foreground" />
                    <span>100 credits</span>
                  </div>
                  <Slider.Track>
                    <Slider.Fill />
                    <Slider.Thumb />
                  </Slider.Track>
                </Slider>
              </div>

              {/* Quick preset chips */}
              <div className="flex items-center gap-2 pt-1">
                <span className="text-xs text-muted mr-1">Presets:</span>
                {PRESET_CREDITS.map((preset) => (
                  <Button
                    key={preset}
                    size="sm"
                    variant={credits === preset ? 'primary' : 'secondary'}
                    onPress={() => setCredits(preset)}
                    className="font-mono text-xs px-2.5 h-7 min-w-0"
                  >
                    {preset} credits
                  </Button>
                ))}
              </div>
            </div>

            {/* Live total price calculation */}
            <div className="p-4 rounded-lg bg-surface-secondary border border-border flex items-baseline justify-between">
              <div>
                <div className="text-xs text-muted">Total price</div>
                <div className="font-mono-tabular text-3xl font-extrabold text-foreground">
                  {formatVnd(totalPrice)}
                </div>
              </div>
              <div className="text-right">
                <div className="text-xs text-muted font-mono-tabular">
                  {credits} {credits === 1 ? 'credit' : 'credits'} × 20,000 VND
                </div>
                <div className="text-xs text-success font-medium mt-0.5">
                  Credits never expire
                </div>
              </div>
            </div>
          </Card.Content>

          <Card.Footer className="pt-2">
            <Link
              to="/checkout"
              search={{ credits } as never}
              className="no-underline w-full block"
            >
              <Button variant="primary" size="lg" className="w-full justify-center">
                <Sparkles className="w-4 h-4 mr-2" />
                Proceed to Top-up
                <ArrowRight className="w-4 h-4 ml-2" />
              </Button>
            </Link>
          </Card.Footer>
        </Card>
      </section>

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
