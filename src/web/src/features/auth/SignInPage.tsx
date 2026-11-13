import React, { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
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
import { CheckCircle2, LockKeyhole, ShieldAlert } from 'lucide-react';
import { ApiClientError, api } from '../../lib/api';
import { isLocalPath, isStaffSubject, resolveSafeReturnUrl } from '../../lib/security';
import { useAuth } from '../../context/AuthContext';
import { AuthCostSummaryCard } from './AuthCostSummaryCard';

function parseLocalDestination(localPath: string): {
  pathname: string;
  search?: Record<string, string>;
} {
  const url = new URL(localPath, 'http://localhost');
  const entries = Array.from(url.searchParams.entries());
  return {
    pathname: url.pathname,
    search: entries.length > 0 ? Object.fromEntries(entries) : undefined,
  };
}

export function SignInPage() {
  const search = useSearch({ strict: false }) as {
    returnUrl?: string;
    locked?: string;
    reset?: string;
  };
  const navigate = useNavigate();
  const { refreshSession } = useAuth();

  const rawReturnUrl =
    typeof search?.returnUrl === 'string' ? search.returnUrl : undefined;
  const safeReturnParam = isLocalPath(rawReturnUrl) ? rawReturnUrl.trim() : undefined;

  const lockedFlag = String(search?.locked ?? '').toLowerCase();
  const resetFlag = String(search?.reset ?? '').toLowerCase();
  const isLockedNotice = lockedFlag === '1' || lockedFlag === 'true';
  const isResetNotice = resetFlag === '1' || resetFlag === 'true';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [formError, setFormError] = useState<string | null>(
    isLockedNotice ? 'This account is locked. Contact support.' : null,
  );
  const [emailError, setEmailError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const trimmedEmail = email.trim();
    const nextEmailErr = !trimmedEmail
      ? 'Email is required.'
      : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)
        ? "That doesn't look like an email address."
        : null;
    const nextPassErr = !password ? 'Password is required.' : null;

    setEmailError(nextEmailErr);
    setPasswordError(nextPassErr);
    if (nextEmailErr || nextPassErr) {
      return;
    }

    setIsSubmitting(true);
    try {
      const response = await api.auth.signIn({ email: trimmedEmail, password });
      await refreshSession();

      const fallbackDest = isStaffSubject(response.user) ? '/admin' : '/dashboard';
      const destination = resolveSafeReturnUrl(rawReturnUrl, fallbackDest);
      const parsed = parseLocalDestination(destination);
      await navigate({
        to: parsed.pathname as never,
        ...(parsed.search ? { search: parsed.search as never } : {}),
      });
    } catch (err) {
      if (err instanceof ApiClientError) {
        if (err.status === 429 || err.code === 'TOO_MANY_REQUESTS') {
          setFormError('Too many attempts. Please wait a moment before trying again.');
        } else {
          setFormError(err.detail || err.title || 'Sign-in failed. Please try again.');
        }
      } else {
        setFormError('Unable to reach the authentication service. Please try again.');
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
              Sign in
            </h1>
            <p className="text-sm text-muted mt-1 mb-0">Pick up where you left off.</p>
          </div>

          {isResetNotice && (
            <Alert status="success" role="status">
              <Alert.Indicator>
                <CheckCircle2 className="w-4 h-4" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Password updated</Alert.Title>
                <Alert.Description>
                  Your password has been reset successfully. Please sign in with your new password.
                </Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          {formError && (
            <Alert status="danger" role="alert">
              <Alert.Indicator>
                <ShieldAlert className="w-4 h-4" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Could not sign in</Alert.Title>
                <Alert.Description>{formError}</Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          <Card>
            <Form onSubmit={handleSubmit} validationBehavior="aria">
              <Card.Content className="flex flex-col gap-4">
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
                  <Input
                    autoComplete="email"
                    placeholder="you@example.com"
                  />
                  {emailError && <FieldError>{emailError}</FieldError>}
                </TextField>

                <div className="flex flex-col gap-1.5">
                  <TextField
                    isRequired
                    name="password"
                    type="password"
                    value={password}
                    onChange={(value) => {
                      setPassword(value);
                      if (passwordError) setPasswordError(null);
                    }}
                    isInvalid={Boolean(passwordError)}
                  >
                    <Label>Password</Label>
                    <Input autoComplete="current-password" />
                    {passwordError && <FieldError>{passwordError}</FieldError>}
                  </TextField>
                  <div className="flex justify-end">
                    <Link
                      to="/forgot-password"
                      className="text-xs font-medium text-accent hover:underline"
                    >
                      Forgot your password?
                    </Link>
                  </div>
                </div>
              </Card.Content>

              <Card.Footer className="flex flex-col gap-3">
                <Button
                  type="submit"
                  variant="primary"
                  size="lg"
                  className="w-full"
                  isPending={isSubmitting}
                  isDisabled={isSubmitting}
                >
                  <LockKeyhole className="w-4 h-4" />
                  {isSubmitting ? 'Signing in...' : 'Sign in'}
                </Button>
                <p className="text-xs text-muted text-center m-0">
                  No account yet?{' '}
                  <Link
                    to="/register"
                    search={safeReturnParam ? { returnUrl: safeReturnParam } : undefined}
                    className="font-semibold text-accent hover:underline"
                  >
                    Sign up
                  </Link>{' '}
                  — it's free.
                </p>
              </Card.Footer>
            </Form>
          </Card>
        </div>

        <div className="lg:col-span-5">
          <AuthCostSummaryCard />
        </div>
      </div>
    </div>
  );
}
