import React from 'react';
import { Link } from '@tanstack/react-router';
import { Breadcrumbs, Card } from '@heroui/react';

export function TermsPage() {
  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <Breadcrumbs>
        <Breadcrumbs.Item>
          <Link to="/" className="no-underline text-inherit">
            Home
          </Link>
        </Breadcrumbs.Item>
        <Breadcrumbs.Item>Terms of Use</Breadcrumbs.Item>
      </Breadcrumbs>

      <header className="space-y-2 border-b border-border pb-4">
        <p className="text-xs font-medium uppercase tracking-wider text-muted">
          Legal
        </p>
        <h1 className="font-display text-3xl font-semibold tracking-tight text-foreground">
          Terms of Use
        </h1>
        <p className="text-sm text-muted">
          Rules governing accounts, exam attempts, credits, and AI diagnostic feedback on Extra Efficiency.
        </p>
      </header>

      <Card className="border border-border">
        <Card.Content className="space-y-5 p-6 text-sm leading-relaxed text-foreground">
          <section className="space-y-1.5">
            <h2 className="font-display text-lg font-semibold">1. Accounts &amp; verification</h2>
            <p className="text-muted">
              You are responsible for keeping your sign-in credentials confidential and providing an accurate email address for activation and password recovery. Accounts with unverified email addresses have restricted access to paid or scored actions.
            </p>
          </section>

          <section className="space-y-1.5">
            <h2 className="font-display text-lg font-semibold">2. Credits &amp; ledger integrity</h2>
            <p className="text-muted">
              Credits are issued upon confirmed payment settlement and recorded in an append-only ledger. Purchased credits do not expire. Preconditions are validated before any debit transaction is recorded.
            </p>
          </section>

          <section className="space-y-1.5">
            <h2 className="font-display text-lg font-semibold">3. Exam &amp; AI grading use</h2>
            <p className="text-muted">
              Practice items, mock exams, and AI rubric gradings are provided strictly for personal exam preparation. Automated scraping, bulk redistribution of item banks, or tampering with sitting timers is prohibited.
            </p>
          </section>
        </Card.Content>
      </Card>
    </div>
  );
}
