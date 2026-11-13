import React from 'react';
import { Breadcrumbs, Card } from '@heroui/react';

export function PrivacyPage() {
  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <Breadcrumbs>
        <Breadcrumbs.Item href="/">Home</Breadcrumbs.Item>
        <Breadcrumbs.Item>Privacy Policy</Breadcrumbs.Item>
      </Breadcrumbs>

      <header className="space-y-2 border-b border-border pb-4">
        <p className="text-xs font-medium uppercase tracking-wider text-muted">
          Legal
        </p>
        <h1 className="font-display text-3xl font-semibold tracking-tight text-foreground">
          Privacy Policy
        </h1>
        <p className="text-sm text-muted">
          How Extra Efficiency handles account credentials, study diagnostics, spoken audio, and payment records.
        </p>
      </header>

      <Card className="border border-border">
        <Card.Content className="space-y-5 p-6 text-sm leading-relaxed text-foreground">
          <section className="space-y-1.5">
            <h2 className="font-display text-lg font-semibold">1. Session &amp; cookie handling</h2>
            <p className="text-muted">
              Browser sessions authenticate through an HttpOnly, SameSite=Lax access token cookie that client scripts cannot read. We do not store authentication tokens in browser localStorage or sessionStorage.
            </p>
          </section>

          <section className="space-y-1.5">
            <h2 className="font-display text-lg font-semibold">2. Study &amp; grading records</h2>
            <p className="text-muted">
              Answers, timing data, writing drafts, and speaking recordings submitted for AI grading are used to compute your diagnostic accuracy, rubric scores, and personalized learning paths. Standalone practice history is pruned automatically beyond your certificate retention limit.
            </p>
          </section>

          <section className="space-y-1.5">
            <h2 className="font-display text-lg font-semibold">3. Payment events</h2>
            <p className="text-muted">
              Checkout orders record transaction references and ledger entries for auditability without storing raw bank card credentials on our servers.
            </p>
          </section>
        </Card.Content>
      </Card>
    </div>
  );
}
