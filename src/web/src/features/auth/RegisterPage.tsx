import React, { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Description,
  FieldError,
  Form,
  Input,
  Label,
  TextField,
} from '@heroui/react';
import { ShieldAlert, UserPlus } from 'lucide-react';
import { ApiClientError, api } from '../../lib/api';
import { isLocalPath, resolveSafeReturnUrl } from '../../lib/security';
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

export function RegisterPage() {
  const search = useSearch({ strict: false }) as { returnUrl?: string };
  const navigate = useNavigate();
  const { refreshSession } = useAuth();

  const rawReturnUrl =
    typeof search?.returnUrl === 'string' ? search.returnUrl : undefined;
  const safeReturnParam = isLocalPath(rawReturnUrl) ? rawReturnUrl.trim() : undefined;

  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [password, setPassword] = useState('');
  const [termsAccepted, setTermsAccepted] = useState(false);

  const [formError, setFormError] = useState<string | null>(null);
  const [errors, setErrors] = useState<{
    fullName?: string;
    email?: string;
    phone?: string;
    password?: string;
    terms?: string;
  }>({});
  const [isSubmitting, setIsSubmitting] = useState(false);

  const validate = () => {
    const next: typeof errors = {};
    const trimmedName = fullName.trim();
    const trimmedEmail = email.trim();
    const trimmedPhone = phone.trim();

    if (!trimmedName) {
      next.fullName = 'Please tell us your name.';
    } else if (trimmedName.length > 120) {
      next.fullName = 'Name must be 120 characters or fewer.';
    }

    if (!trimmedEmail) {
      next.email = 'Email is required.';
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
      next.email = "That doesn't look like an email address.";
    }

    if (trimmedPhone.length > 20) {
      next.phone = 'Phone must be 20 characters or fewer.';
    }

    if (!password) {
      next.password = 'Choose a password.';
    } else if (password.length < 8) {
      next.password = 'Password must be at least 8 characters.';
    } else if (!/[A-Za-z]/.test(password) || !/[0-9]/.test(password)) {
      next.password = 'Include at least one letter and one number.';
    }

    if (!termsAccepted) {
      next.terms = 'Please agree to the terms of use.';
    }

    setErrors(next);
    return Object.keys(next).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    if (!validate()) return;

    setIsSubmitting(true);
    try {
      await api.auth.register({
        fullName: fullName.trim(),
        email: email.trim(),
        phone: phone.trim() || undefined,
        password,
        termsAccepted,
      });
      await refreshSession();

      const destination = resolveSafeReturnUrl(rawReturnUrl, '/dashboard');
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
          setFormError(err.detail || err.title || 'Could not create account.');
        }
      } else {
        setFormError('Unable to reach the registration service. Please try again.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="max-w-5xl mx-auto py-4 sm:py-8">
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div className="lg:col-span-7 flex flex-col gap-4 min-w-0">
          <div>
            <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
              Sign up
            </h1>
            <p className="text-sm text-muted mt-1 mb-0">
              Free placement exam, practice drills, and a learning path that follows your certificate.
            </p>
          </div>

          {formError && (
            <Alert status="danger" role="alert">
              <Alert.Indicator>
                <ShieldAlert className="w-4 h-4" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Registration could not be completed</Alert.Title>
                <Alert.Description>{formError}</Alert.Description>
              </Alert.Content>
            </Alert>
          )}

          <Card className="p-5 sm:p-6">
            <Form className="flex flex-col gap-5" onSubmit={handleSubmit} validationBehavior="aria">
              <Card.Content className="flex flex-col gap-4">
                <TextField
                  isRequired
                  name="fullName"
                  value={fullName}
                  onChange={(value) => {
                    setFullName(value);
                    if (errors.fullName) setErrors((prev) => ({ ...prev, fullName: undefined }));
                  }}
                  isInvalid={Boolean(errors.fullName)}
                >
                  <Label>Full name</Label>
                  <Input autoComplete="name" placeholder="Nguyễn Văn A" />
                  {errors.fullName && <FieldError>{errors.fullName}</FieldError>}
                </TextField>

                <TextField
                  isRequired
                  name="email"
                  type="email"
                  value={email}
                  onChange={(value) => {
                    setEmail(value);
                    if (errors.email) setErrors((prev) => ({ ...prev, email: undefined }));
                  }}
                  isInvalid={Boolean(errors.email)}
                >
                  <Label>Email</Label>
                  <Input autoComplete="email" placeholder="you@example.com" />
                  {errors.email && <FieldError>{errors.email}</FieldError>}
                </TextField>

                <TextField
                  name="phone"
                  type="tel"
                  value={phone}
                  onChange={(value) => {
                    setPhone(value);
                    if (errors.phone) setErrors((prev) => ({ ...prev, phone: undefined }));
                  }}
                  isInvalid={Boolean(errors.phone)}
                >
                  <Label>Phone number (optional)</Label>
                  <Input autoComplete="tel" placeholder="0901 234 567" />
                  {errors.phone && <FieldError>{errors.phone}</FieldError>}
                </TextField>

                <TextField
                  isRequired
                  name="password"
                  type="password"
                  value={password}
                  onChange={(value) => {
                    setPassword(value);
                    if (errors.password) setErrors((prev) => ({ ...prev, password: undefined }));
                  }}
                  isInvalid={Boolean(errors.password)}
                >
                  <Label>Password</Label>
                  <Input autoComplete="new-password" />
                  {errors.password ? (
                    <FieldError>{errors.password}</FieldError>
                  ) : (
                    <Description>At least 8 characters, with letters and numbers.</Description>
                  )}
                </TextField>

                <Checkbox
                  isSelected={termsAccepted}
                  onChange={(selected) => {
                    setTermsAccepted(selected);
                    if (errors.terms) setErrors((prev) => ({ ...prev, terms: undefined }));
                  }}
                  isInvalid={Boolean(errors.terms)}
                >
                  <Checkbox.Content>
                    <Checkbox.Control>
                      <Checkbox.Indicator />
                    </Checkbox.Control>
                    <span className="text-sm">
                      I agree to the{' '}
                      <Link to="/terms" className="text-accent underline">
                        terms of use
                      </Link>{' '}
                      and{' '}
                      <Link to="/privacy" className="text-accent underline">
                        privacy policy
                      </Link>
                    </span>
                  </Checkbox.Content>
                  {errors.terms && <FieldError>{errors.terms}</FieldError>}
                </Checkbox>
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
                  <UserPlus className="w-4 h-4" />
                  {isSubmitting ? 'Creating account...' : 'Create account'}
                </Button>
                <p className="text-xs text-muted text-center m-0">
                  Already have an account?{' '}
                  <Link
                    to="/sign-in"
                    search={safeReturnParam ? { returnUrl: safeReturnParam } : undefined}
                    className="font-semibold text-accent hover:underline"
                  >
                    Sign in
                  </Link>
                  .
                </p>
              </Card.Footer>
            </Form>
          </Card>
        </div>

        <div className="lg:col-span-5 min-w-0">
          <AuthCostSummaryCard
            title="What you get on day one"
            footerNote="Credits do not expire once bought. Start with your free placement exam and learning path."
          />
        </div>
      </div>
    </div>
  );
}
