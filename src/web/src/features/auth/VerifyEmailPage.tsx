import React, { useState } from 'react';
import { Link, useSearch } from '@tanstack/react-router';
import { Alert, Button, Card, Form } from '@heroui/react';
import { CheckCircle2, MailCheck, ShieldAlert } from 'lucide-react';
import { ApiClientError, api } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';

export function VerifyEmailPage() {
  const search = useSearch({ strict: false }) as { token?: unknown };
  const token = String(search?.token ?? '').trim();
  const { isAuthenticated, refreshSession } = useAuth();

  const [status, setStatus] = useState<'pending' | 'verified' | 'invalid'>(
    token ? 'pending' : 'invalid',
  );
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleConfirm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!token || isSubmitting) return;

    setIsSubmitting(true);
    try {
      await api.auth.verifyEmail(token);
      if (isAuthenticated) {
        await refreshSession();
      }
      setStatus('verified');
    } catch (err) {
      if (err instanceof ApiClientError && (err.status === 400 || err.code === 'AUTH_TOKEN_INVALID')) {
        setStatus('invalid');
      } else {
        setStatus('invalid');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="max-w-5xl mx-auto py-4">
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div className="lg:col-span-7 flex flex-col gap-4">
          <div>
            <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
              Verify your email
            </h1>
            <p className="text-sm text-muted mt-1 mb-0">
              Confirm the email address on your Extra Efficiency account.
            </p>
          </div>

          <Card>
            <Card.Content className="flex flex-col gap-4">
              {status === 'verified' ? (
                <>
                  <Alert status="success" role="status">
                    <Alert.Indicator>
                      <CheckCircle2 className="w-4 h-4" />
                    </Alert.Indicator>
                    <Alert.Content>
                      <Alert.Title>Email verified</Alert.Title>
                      <Alert.Description>
                        Thanks. Your account is fully active, and credit package checkout is unlocked.
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>
                  <div className="flex flex-wrap gap-2">
                    <Link to={isAuthenticated ? '/dashboard' : '/sign-in'} className="no-underline">
                      <Button variant="primary">
                        {isAuthenticated ? 'Go to your dashboard' : 'Sign in to continue'}
                      </Button>
                    </Link>
                  </div>
                </>
              ) : status === 'invalid' ? (
                <>
                  <Alert status="danger" role="alert">
                    <Alert.Indicator>
                      <ShieldAlert className="w-4 h-4" />
                    </Alert.Indicator>
                    <Alert.Content>
                      <Alert.Title>This link no longer works</Alert.Title>
                      <Alert.Description>
                        Activation links work once and expire after 24 hours. Sign in and use{' '}
                        <strong>Resend email</strong> on your profile or dashboard for a fresh one.
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>
                  <div className="flex flex-wrap gap-2">
                    <Link to={isAuthenticated ? '/dashboard' : '/sign-in'} className="no-underline">
                      <Button variant="secondary">
                        {isAuthenticated ? 'Go to your dashboard' : 'Back to sign in'}
                      </Button>
                    </Link>
                  </div>
                </>
              ) : (
                <Form onSubmit={handleConfirm} className="flex flex-col gap-4">
                  <div className="flex items-start gap-3">
                    <MailCheck className="w-5 h-5 text-accent shrink-0 mt-0.5" />
                    <p className="text-sm text-foreground m-0">
                      Confirm that this is your email address. Pressing the button below completes
                      verification for this one-time link.
                    </p>
                  </div>
                  <Button
                    type="submit"
                    variant="primary"
                    size="lg"
                    className="w-full"
                    isPending={isSubmitting}
                    isDisabled={isSubmitting}
                  >
                    {isSubmitting ? 'Verifying...' : 'Verify my email'}
                  </Button>
                </Form>
              )}
            </Card.Content>
          </Card>
        </div>

        <div className="lg:col-span-5">
          <Card>
            <Card.Header>
              <Card.Title>Why verify your email?</Card.Title>
            </Card.Header>
            <Card.Content className="flex flex-col gap-3 text-sm text-foreground">
              <ol className="list-decimal pl-5 m-0 flex flex-col gap-2">
                <li>
                  Unlocks <strong>credit purchasing</strong> and full mock exams.
                </li>
                <li>Enables password reset links if you ever forget your password.</li>
                <li>Delivers AI grading reports and mentor booking confirmations.</li>
              </ol>
              <p className="text-xs text-muted m-0">
                Need help? Contact support if you did not receive an email.
              </p>
            </Card.Content>
          </Card>
        </div>
      </div>
    </div>
  );
}
