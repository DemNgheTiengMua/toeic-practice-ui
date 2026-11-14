import React, { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import {
  Alert,
  Button,
  Card,
  Description,
  FieldError,
  Form,
  Input,
  Label,
  TextField,
} from '@heroui/react';
import { KeyRound, ShieldAlert } from 'lucide-react';
import { ApiClientError, api } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';

export function ResetPasswordPage() {
  const search = useSearch({ strict: false }) as { token?: unknown };
  const token = String(search?.token ?? '').trim();
  const navigate = useNavigate();
  const { refreshSession } = useAuth();

  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [linkInvalid, setLinkInvalid] = useState(!token);
  const [formError, setFormError] = useState<string | null>(null);
  const [errors, setErrors] = useState<{ newPassword?: string; confirmPassword?: string }>({});
  const [isSubmitting, setIsSubmitting] = useState(false);

  const validate = () => {
    const next: typeof errors = {};
    if (!newPassword) {
      next.newPassword = 'Choose a new password.';
    } else if (newPassword.length < 8) {
      next.newPassword = 'Password must be at least 8 characters.';
    } else if (!/[A-Za-z]/.test(newPassword) || !/[0-9]/.test(newPassword)) {
      next.newPassword = 'Include at least one letter and one number.';
    }

    if (!confirmPassword) {
      next.confirmPassword = 'Type the new password again.';
    } else if (confirmPassword !== newPassword) {
      next.confirmPassword = "The two passwords don't match.";
    }

    setErrors(next);
    return Object.keys(next).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    if (!token) {
      setLinkInvalid(true);
      return;
    }
    if (!validate()) return;

    setIsSubmitting(true);
    try {
      await api.auth.resetPassword({ token, newPassword });
      await refreshSession();
      await navigate({ to: '/sign-in', search: { reset: 1 } as never });
    } catch (err) {
      if (err instanceof ApiClientError) {
        if (err.code === 'AUTH_TOKEN_INVALID') {
          setLinkInvalid(true);
        } else if (err.status === 429 || err.code === 'TOO_MANY_REQUESTS') {
          setFormError('Too many attempts. Please wait a moment before trying again.');
        } else {
          setFormError(err.detail || err.title || 'Could not reset password.');
        }
      } else {
        setFormError('Unable to reach the authentication service. Please try again.');
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
              Choose a new password
            </h1>
            <p className="text-sm text-muted mt-1 mb-0">
              Set a new password for your Extra Efficiency account.
            </p>
          </div>

          {linkInvalid ? (
            <Card>
              <Card.Content className="flex flex-col gap-4">
                <Alert status="danger" role="alert">
                  <Alert.Indicator>
                    <ShieldAlert className="w-4 h-4" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>This link no longer works</Alert.Title>
                    <Alert.Description>
                      Reset links work once and expire after 15 minutes. Ask for a new one.
                    </Alert.Description>
                  </Alert.Content>
                </Alert>
                <div>
                  <Link to="/forgot-password" className="no-underline">
                    <Button variant="primary">Send a new link</Button>
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
                    <Alert.Title>Could not update password</Alert.Title>
                    <Alert.Description>{formError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <Card className="p-5 sm:p-6">
                <Form className="flex flex-col gap-5" onSubmit={handleSubmit} validationBehavior="aria">
                  <Card.Content className="flex flex-col gap-4">
                    <TextField
                      isRequired
                      name="newPassword"
                      type="password"
                      value={newPassword}
                      onChange={(value) => {
                        setNewPassword(value);
                        if (errors.newPassword) {
                          setErrors((prev) => ({ ...prev, newPassword: undefined }));
                        }
                      }}
                      isInvalid={Boolean(errors.newPassword)}
                    >
                      <Label>New password</Label>
                      <Input autoComplete="new-password" />
                      {errors.newPassword ? (
                        <FieldError>{errors.newPassword}</FieldError>
                      ) : (
                        <Description>At least 8 characters, with letters and numbers.</Description>
                      )}
                    </TextField>

                    <TextField
                      isRequired
                      name="confirmPassword"
                      type="password"
                      value={confirmPassword}
                      onChange={(value) => {
                        setConfirmPassword(value);
                        if (errors.confirmPassword) {
                          setErrors((prev) => ({ ...prev, confirmPassword: undefined }));
                        }
                      }}
                      isInvalid={Boolean(errors.confirmPassword)}
                    >
                      <Label>Confirm new password</Label>
                      <Input autoComplete="new-password" />
                      {errors.confirmPassword && (
                        <FieldError>{errors.confirmPassword}</FieldError>
                      )}
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
                      <KeyRound className="w-4 h-4" />
                      {isSubmitting ? 'Saving...' : 'Save new password'}
                    </Button>
                  </Card.Footer>
                </Form>
              </Card>
            </>
          )}
        </div>

        <div className="lg:col-span-5 min-w-0">
          <Card className="p-5 sm:p-6">
            <Card.Header>
              <Card.Title>Password requirements</Card.Title>
            </Card.Header>
            <Card.Content className="flex flex-col gap-3 text-sm text-foreground">
              <ol className="list-decimal pl-5 m-0 flex flex-col gap-2">
                <li>
                  Use <strong>at least 8 characters</strong>.
                </li>
                <li>Include both letters and numbers for safety.</li>
                <li>After saving, sign in with your new password.</li>
              </ol>
              <p className="text-xs text-muted m-0">Never share your password with anyone.</p>
            </Card.Content>
          </Card>
        </div>
      </div>
    </div>
  );
}
