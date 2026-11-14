import React, { useEffect, useRef, useState } from 'react';
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
import {
  type CapabilitySubject,
  isLocalPath,
  isStaffSubject,
  resolveSafeReturnUrl,
} from '../../lib/security';
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

function resolvePostSignInDestination(
  rawReturnUrl: string | undefined,
  subject: CapabilitySubject | null | undefined,
): string {
  const fallbackDest = isStaffSubject(subject) ? '/admin' : '/dashboard';
  const resolved = resolveSafeReturnUrl(rawReturnUrl, fallbackDest);
  const parsed = parseLocalDestination(resolved);
  if (
    parsed.pathname === '/sign-in' ||
    parsed.pathname.startsWith('/sign-in/') ||
    parsed.pathname === '/auth/sign-in' ||
    parsed.pathname.startsWith('/auth/sign-in/')
  ) {
    return fallbackDest;
  }
  return resolved;
}

export function SignInPage() {
  const search = useSearch({ strict: false }) as {
    returnUrl?: string;
    locked?: string;
    reset?: string;
  };
  const navigate = useNavigate();
  const { user, isAuthenticated, isLoading, refreshSession } = useAuth();

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

  const emailInputRef = useRef<HTMLInputElement | null>(null);
  const passwordInputRef = useRef<HTMLInputElement | null>(null);
  const didSubmitRef = useRef(false);

  useEffect(() => {
    if (isLoading || !isAuthenticated || !user || didSubmitRef.current) {
      return;
    }

    const destination = resolvePostSignInDestination(rawReturnUrl, user);
    const parsed = parseLocalDestination(destination);
    void navigate({
      to: parsed.pathname as never,
      ...(parsed.search ? { search: parsed.search as never } : {}),
      replace: true,
    });
  }, [isLoading, isAuthenticated, user, rawReturnUrl, navigate]);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setFormError(null);

    const formData = new FormData(e.currentTarget);
    const domEmail = emailInputRef.current?.value ?? String(formData.get('email') ?? '');
    const domPassword =
      passwordInputRef.current?.value ?? String(formData.get('password') ?? '');
    const currentEmail = domEmail || email;
    const currentPassword = domPassword || password;

    if (currentEmail !== email) {
      setEmail(currentEmail);
    }
    if (currentPassword !== password) {
      setPassword(currentPassword);
    }

    const trimmedEmail = currentEmail.trim();
    const nextEmailErr = !trimmedEmail
      ? 'Email is required.'
      : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)
        ? "That doesn't look like an email address."
        : null;
    const nextPassErr = !currentPassword ? 'Password is required.' : null;

    setEmailError(nextEmailErr);
    setPasswordError(nextPassErr);
    if (nextEmailErr || nextPassErr) {
      return;
    }

    setIsSubmitting(true);
    try {
      const response = await api.auth.signIn({
        email: trimmedEmail,
        password: currentPassword,
      });
      didSubmitRef.current = true;
      await refreshSession(response.user);

      const destination = resolvePostSignInDestination(rawReturnUrl, response.user);
      const parsed = parseLocalDestination(destination);
      await navigate({
        to: parsed.pathname as never,
        ...(parsed.search ? { search: parsed.search as never } : {}),
        replace: true,
      });
    } catch (err) {
      didSubmitRef.current = false;
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

  if (!isLoading && isAuthenticated && user) {
    return null;
  }

  return (
    <div className="max-w-5xl mx-auto py-4 sm:py-8">
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div className="lg:col-span-7 flex flex-col gap-4 min-w-0">
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

          <Card className="p-5 sm:p-6">
            <Form className="flex flex-col gap-5" onSubmit={handleSubmit} validationBehavior="aria">
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
                    ref={emailInputRef}
                    autoComplete="email"
                    placeholder="you@example.com"
                    onInput={(e) => {
                      const nextValue = e.currentTarget.value;
                      if (nextValue !== email) {
                        setEmail(nextValue);
                      }
                      if (emailError) setEmailError(null);
                    }}
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
                    <Input
                      ref={passwordInputRef}
                      autoComplete="current-password"
                      onInput={(e) => {
                        const nextValue = e.currentTarget.value;
                        if (nextValue !== password) {
                          setPassword(nextValue);
                        }
                        if (passwordError) setPasswordError(null);
                      }}
                    />
                    {passwordError && <FieldError>{passwordError}</FieldError>}
                  </TextField>
                  <div className="flex justify-end pt-1">
                    <Link
                      to="/forgot-password"
                      className="text-xs font-medium text-accent hover:underline"
                    >
                      Forgot your password?
                    </Link>
                  </div>
                </div>
              </Card.Content>

              <Card.Footer className="flex flex-col gap-3.5 pt-1">
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

        <div className="lg:col-span-5 min-w-0">
          <AuthCostSummaryCard />
        </div>
      </div>
    </div>
  );
}
