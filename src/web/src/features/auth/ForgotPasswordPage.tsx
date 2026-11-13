import React, { useState } from 'react';
import { Link } from '@tanstack/react-router';
import {
  Alert,
  Button,
  Card,
  FieldError,
  Form,
  Input,
  Label,
  TextField,
} from '@heroui/react';
import { Mail, Send, ShieldAlert } from 'lucide-react';
import { ApiClientError, api } from '../../lib/api';

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [emailError, setEmailError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const trimmed = email.trim();
    if (!trimmed) {
      setEmailError('Email is required.');
      return;
    }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) {
      setEmailError("That doesn't look like an email address.");
      return;
    }
    setEmailError(null);

    setIsSubmitting(true);
    try {
      await api.auth.forgotPassword(trimmed);
      setSent(true);
    } catch (err) {
      if (err instanceof ApiClientError && err.status === 400) {
        setFormError(err.detail || err.title || 'Please enter a valid email address.');
      } else {
        setFormError('Could not send the password reset link. Try again in a few minutes.');
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
              Forgot password
            </h1>
            <p className="text-sm text-muted mt-1 mb-0">
              Reset your Extra Efficiency password by email.
            </p>
          </div>

          {sent ? (
            <Card>
              <Card.Content className="flex flex-col gap-4">
                <Alert status="success" role="status">
                  <Alert.Indicator>
                    <Mail className="w-4 h-4" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Link sent</Alert.Title>
                    <Alert.Description>
                      If <strong>{email.trim()}</strong> has an account, a reset link is on its way.
                      The link expires in 15 minutes.
                    </Alert.Description>
                  </Alert.Content>
                </Alert>
                <div className="flex gap-2">
                  <Link to="/sign-in" className="no-underline">
                    <Button variant="secondary">Back to sign in</Button>
                  </Link>
                </div>
              </Card.Content>
            </Card>
          ) : (
            <>
              {formError && (
                <Alert status="danger" role="alert">
                  <Alert.Indicator>
                    <ShieldAlert className="w-4 h-4" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Request failed</Alert.Title>
                    <Alert.Description>{formError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <Card>
                <Form onSubmit={handleSubmit} validationBehavior="aria">
                  <Card.Content className="flex flex-col gap-4">
                    <p className="text-sm text-muted m-0">
                      Enter your email and we will send you a password reset link.
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
                  </Card.Content>
                  <Card.Footer>
                    <Button
                      type="submit"
                      variant="primary"
                      size="lg"
                      className="w-full"
                      isPending={isSubmitting}
                      isDisabled={isSubmitting}
                    >
                      <Send className="w-4 h-4" />
                      {isSubmitting ? 'Sending...' : 'Send link'}
                    </Button>
                  </Card.Footer>
                </Form>
              </Card>
            </>
          )}
        </div>

        <div className="lg:col-span-5">
          <Card>
            <Card.Header>
              <Card.Title>What happens next</Card.Title>
            </Card.Header>
            <Card.Content className="flex flex-col gap-3 text-sm text-foreground">
              <ol className="list-decimal pl-5 m-0 flex flex-col gap-2">
                <li>
                  <strong>We email a reset link</strong> to the address you enter.
                </li>
                <li>
                  <strong>The link works for 15 minutes.</strong> After that, ask for a new one.
                </li>
                <li>
                  <strong>Choose a new password</strong> of at least 8 characters, with letters and
                  numbers, then sign in.
                </li>
              </ol>
              <p className="text-xs text-muted m-0">
                Remembered it?{' '}
                <Link to="/sign-in" className="text-accent font-medium hover:underline">
                  Back to sign in
                </Link>
                .
              </p>
            </Card.Content>
          </Card>
        </div>
      </div>
    </div>
  );
}
