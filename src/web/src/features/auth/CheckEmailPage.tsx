import React, { useState } from 'react';
import { Link, useSearch } from '@tanstack/react-router';
import { Alert, Button, Card, FieldError, Form, Input, Label, TextField, buttonVariants } from '@heroui/react';
import { MailCheck, Send } from 'lucide-react';
import { ApiClientError, api } from '../../lib/api';
import { useResendCooldown } from '../../lib/useResendCooldown';

/**
 * ADR-0018: the destination after registering and after a refused sign-in, because verification now
 * gates the session itself. Deliberately reachable while signed out: the person here has no account
 * session yet, which is precisely the state this screen exists to get them out of.
 *
 * `resend` answers 204 whether or not the address has an account, so this screen never learns the
 * difference — and neither does anyone trying addresses.
 */
export function CheckEmailPage() {
  const search = useSearch({ strict: false }) as { email?: unknown };
  const initialEmail = String(search?.email ?? '').trim();

  const [email, setEmail] = useState(initialEmail);
  const [emailError, setEmailError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [sent, setSent] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const { remaining, isCoolingDown, start: startCooldown } = useResendCooldown(60);

  // One send path for both shapes of this screen: the form (address typed) and the fixed-address
  // card (address already known). Sharing it keeps the 429 wording and the error handling identical.
  const handleResendAddress = async (address: string) => {
    if (isSubmitting || isCoolingDown) return;
    setFormError(null);

    setIsSubmitting(true);
    try {
      await api.auth.resendActivation(address.trim());
      setSent(true);
      startCooldown();
    } catch (err) {
      if (err instanceof ApiClientError && err.status === 429) {
        setFormError('Too many requests. Please wait a while before asking for another link.');
        startCooldown();
      } else {
        setFormError('Could not send the email. Please try again in a few minutes.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleResend = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = email.trim();
    if (!trimmed) {
      setEmailError('Enter the address you registered with.');
      return;
    }
    await handleResendAddress(trimmed);
  };

  return (
    <div className="max-w-5xl mx-auto py-4">
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div className="lg:col-span-7 flex flex-col gap-4">
          <div>
            <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0 flex items-center gap-2">
              <MailCheck className="w-6 h-6 text-accent" />
              Check your email
            </h1>
            <p className="text-sm text-muted mt-1 mb-0">
              Signing in needs a verified address. Open the activation link we sent to finish setup.
            </p>
          </div>

          {sent ? (
            <Alert status="success" role="status">
              <Alert.Indicator>
                <MailCheck className="w-4 h-4" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Activation email sent</Alert.Title>
                <Alert.Description>
                  If an unverified account exists for {email.trim()}, a fresh link is on its way. It
                  works once and expires after 24 hours. Check the spam folder first — these links are
                  easy to miss.
                </Alert.Description>
              </Alert.Content>
            </Alert>
          ) : initialEmail ? (
            // The address is known (came from signing up or from a refused sign-in), so it is shown as
            // fixed text rather than an editable box: this screen verifies THAT account, and an input
            // here read as "did it send already, or must I press this?".
            <Card className="p-5 sm:p-6">
              <Card.Content className="flex flex-col gap-4">
                <p className="text-sm text-muted m-0">
                  We already sent an activation link to this address. The link works once and expires
                  after 24 hours. Nothing happening? Send a fresh one — the older link stops working.
                </p>
                <div className="rounded-lg border border-border bg-surface-secondary px-3 py-2">
                  <span className="text-xs text-muted block">Activation link sent to</span>
                  <span className="font-medium text-foreground break-all">{initialEmail}</span>
                </div>
                {formError && (
                  <Alert status="danger" role="alert">
                    <Alert.Content>
                      <Alert.Description>{formError}</Alert.Description>
                    </Alert.Content>
                  </Alert>
                )}
              </Card.Content>
              <Card.Footer className="flex flex-col sm:flex-row gap-2 pt-4">
                <Button
                  variant="primary"
                  size="lg"
                  className="w-full sm:flex-1"
                  isPending={isSubmitting}
                  isDisabled={isSubmitting || isCoolingDown}
                  onPress={() => void handleResendAddress(initialEmail)}
                >
                  <Send className="w-4 h-4" />
                  {isSubmitting
                    ? 'Sending...'
                    : isCoolingDown
                      ? `Send a new link in ${remaining}s`
                      : 'Send a new link'}
                </Button>
                <Link
                  to="/sign-in"
                  className={buttonVariants({ variant: 'secondary', size: 'lg', className: 'no-underline w-full sm:w-auto' })}
                >
                  Back to sign in
                </Link>
              </Card.Footer>
            </Card>
          ) : (
            <Card className="p-5 sm:p-6">
              <Form className="flex flex-col gap-5" onSubmit={handleResend} validationBehavior="aria">
                <Card.Content className="flex flex-col gap-4">
                  <p className="text-sm text-muted m-0">
                    Enter the address you registered with and we will send a fresh activation link.
                  </p>
                  <TextField
                    isRequired
                    name="email"
                    type="email"
                    value={email}
                    onChange={(value) => {
                      setEmail(value);
                      if (emailError) setEmailError(null);
                    }}
                    isInvalid={Boolean(emailError)}
                  >
                    <Label>Email</Label>
                    <Input autoComplete="email" placeholder="you@example.com" />
                    {emailError && <FieldError>{emailError}</FieldError>}
                  </TextField>
                  {formError && (
                    <Alert status="danger" role="alert">
                      <Alert.Content>
                        <Alert.Description>{formError}</Alert.Description>
                      </Alert.Content>
                    </Alert>
                  )}
                </Card.Content>
                <Card.Footer className="flex flex-col sm:flex-row gap-2">
                  <Button
                    type="submit"
                    variant="primary"
                    size="lg"
                    className="w-full sm:flex-1"
                    isPending={isSubmitting}
                    isDisabled={isSubmitting || isCoolingDown}
                  >
                    <Send className="w-4 h-4" />
                    {isSubmitting
                      ? 'Sending...'
                      : isCoolingDown
                        ? `Send activation link in ${remaining}s`
                        : 'Send activation link'}
                  </Button>
                  <Link
                    to="/sign-in"
                    className={buttonVariants({ variant: 'secondary', size: 'lg', className: 'no-underline w-full sm:w-auto' })}
                  >
                    Back to sign in
                  </Link>
                </Card.Footer>
              </Form>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
