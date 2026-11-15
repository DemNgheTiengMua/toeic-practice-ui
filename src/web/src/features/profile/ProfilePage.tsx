import React, { useEffect, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Description,
  FieldError,
  Form,
  Input,
  Label,
  ListBox,
  Select,
  Spinner,
  TextField,
  toast,
  buttonVariants,
} from '@heroui/react';
import {
  AlertCircle,
  Award,
  CheckCircle2,
  Globe,
  KeyRound,
  Lock,
  Mail,
  MailWarning,
  RefreshCw,
  Send,
  ShieldCheck,
  Sparkles,
  User,
  Wallet,
} from 'lucide-react';
import { api, ApiClientError } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { TIME_ZONE_IDS } from '../../lib/domain-labels';

const FULL_NAME_PATTERN = String.raw`^(?=.{2,70}$)[\p{L}\p{M}]+(?:['’.\- ][\p{L}\p{M}]+)*$`;

export function ProfilePage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { user, learnerContext, switchCertificate, signOut } = useAuth();

  const [fullName, setFullName] = useState<string>('');
  const [phone, setPhone] = useState<string>('');
  const [timeZoneId, setTimeZoneId] = useState<string>('Asia/Ho_Chi_Minh');
  const [profileError, setProfileError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<{
    fullName?: string;
    phone?: string;
    timeZoneId?: string;
  }>({});

  // Password fields
  const [currentPassword, setCurrentPassword] = useState<string>('');
  const [newPassword, setNewPassword] = useState<string>('');
  const [confirmPassword, setConfirmPassword] = useState<string>('');
  const [passwordError, setPasswordError] = useState<string | null>(null);

  // Email verification resend feedback
  const [resendError, setResendError] = useState<string | null>(null);

  const profileQuery = useQuery({
    queryKey: ['learner', 'profile'],
    queryFn: () => api.learner.profile(),
    staleTime: 60_000,
    retry: false,
  });

  // Populate form only when profileQuery successfully resolves
  useEffect(() => {
    if (profileQuery.data) {
      setFullName(profileQuery.data.fullName || '');
      setPhone(profileQuery.data.phone || '');
      setTimeZoneId(profileQuery.data.timeZoneId || 'Asia/Ho_Chi_Minh');
    }
  }, [profileQuery.data]);

  const updateProfileMutation = useMutation({
    mutationFn: () =>
      api.learner.updateProfile({
        fullName: fullName.trim(),
        phone: phone.trim() || null,
        timeZoneId,
      }),
    onSuccess: (data) => {
      setProfileError(null);
      setFieldErrors({});
      toast.success('Profile updated successfully.');
      queryClient.setQueryData(['learner', 'profile'], data);
      void queryClient.invalidateQueries({ queryKey: ['auth', 'me'] });
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        if (err.errors) {
          setFieldErrors({
            fullName: err.errors.fullName?.[0],
            phone: err.errors.phone?.[0],
            timeZoneId: err.errors.timeZoneId?.[0],
          });
        }
        setProfileError(err.title || err.detail || 'Could not update profile.');
      } else {
        setProfileError((err as Error).message || 'Failed to update profile.');
      }
    },
  });

  const resendActivationMutation = useMutation({
    mutationFn: () => api.auth.resendActivation(),
    onSuccess: () => {
      setResendError(null);
      toast.success('Verification email sent. Please check your inbox.');
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setResendError(err.title || err.detail || 'Failed to resend verification email.');
      } else {
        setResendError((err as Error).message || 'Failed to send verification email.');
      }
    },
  });

  const changePasswordMutation = useMutation({
    mutationFn: () =>
      api.learner.changePassword({
        currentPassword,
        newPassword,
      }),
    onSuccess: async () => {
      setPasswordError(null);
      try {
        await signOut();
      } finally {
        void navigate({ to: '/sign-in', search: { reset: '1' } });
      }
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setPasswordError(err.title || err.detail || 'Password change rejected.');
      } else {
        setPasswordError((err as Error).message || 'Failed to update password.');
      }
    },
  });

  const handlePasswordSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPasswordError(null);

    if (!currentPassword) {
      setPasswordError('Please provide your current password.');
      return;
    }
    if (newPassword.length < 8) {
      setPasswordError('New password must be at least 8 characters long.');
      return;
    }
    if (newPassword !== confirmPassword) {
      setPasswordError('New password and confirmation do not match.');
      return;
    }

    changePasswordMutation.mutate();
  };

  const isEmailVerified = user?.emailVerified !== false;

  // Timezones list, ensuring user's current timezone is available
  const availableTimezones = Array.from(
    new Set([timeZoneId, ...TIME_ZONE_IDS]),
  );

  return (
    <div className="max-w-4xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-8">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <User className="w-5 h-5 text-accent" />
            <span className="text-xs font-semibold uppercase tracking-wider text-muted">
              Account Management
            </span>
          </div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            Profile & settings
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            Manage your personal information, active certificate, and account security.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <Link
            to="/checkout"
            className={buttonVariants({ variant: 'secondary', size: 'sm', className: 'no-underline' })}
          >
            <Wallet className="w-4 h-4 mr-1.5" />
            Top up credits
          </Link>
        </div>
      </div>

      {/* Unverified Email Alert Banner */}
      {!isEmailVerified && (
        <Alert status="warning">
          <Alert.Indicator>
            <MailWarning className="w-5 h-5 text-warning" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Email verification pending</Alert.Title>
            <Alert.Description className="space-y-2">
              <p className="m-0 text-sm break-words">
                Your account email (
                <span className="inline-block max-w-[240px] truncate align-bottom font-medium" title={user?.email}>
                  {user?.email}
                </span>
                ) is not yet verified. Please verify your email to unlock credit purchases and exams.
              </p>
              {resendError && (
                <p className="text-xs text-danger font-semibold m-0">{resendError}</p>
              )}
              <div className="pt-1">
                <Button
                  size="sm"
                  variant="primary"
                  isPending={resendActivationMutation.isPending}
                  onPress={() => resendActivationMutation.mutate()}
                >
                  <Send className="w-3.5 h-3.5 mr-1" />
                  Resend verification email
                </Button>
              </div>
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Profile Loading State */}
      {profileQuery.isLoading ? (
        <Card>
          <Card.Content className="flex items-center justify-center py-12 gap-3 text-muted text-sm">
            <Spinner size="sm" />
            <span>Loading profile details...</span>
          </Card.Content>
        </Card>
      ) : profileQuery.isError ? (
        /* Failed initial load: Show retry button and DO NOT render blank form */
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5 text-danger" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Unable to load profile</Alert.Title>
            <Alert.Description className="space-y-3">
              <p className="m-0 text-sm">
                We encountered an error loading your profile information. Please retry to load your settings securely.
              </p>
              <div className="pt-1">
                <Button
                  size="sm"
                  variant="secondary"
                  onPress={() => profileQuery.refetch()}
                >
                  <RefreshCw className="w-3.5 h-3.5 mr-1.5" />
                  Retry
                </Button>
              </div>
            </Alert.Description>
          </Alert.Content>
        </Alert>
      ) : (
        /* Editable Profile Form */
        <div className="space-y-8">
          <Card className="shadow-surface">
            <Card.Header>
              <Card.Title>Personal information</Card.Title>
              <Card.Description>Update your display name, contact phone, and preferred timezone</Card.Description>
            </Card.Header>

            <Card.Content className="space-y-5">
              {profileError && (
                <Alert status="danger">
                  <Alert.Indicator>
                    <AlertCircle className="w-4 h-4 text-danger" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Update failed</Alert.Title>
                    <Alert.Description>{profileError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <Form
                onSubmit={(e) => {
                  e.preventDefault();
                  setFieldErrors({});
                  updateProfileMutation.mutate();
                }}
                validationBehavior="aria"
                className="space-y-4"
              >
                <TextField
                  isDisabled
                  name="email"
                  type="email"
                  value={user?.email || ''}
                  className="w-full"
                >
                  <Label>Account email</Label>
                  <Input className="bg-surface-secondary text-muted cursor-not-allowed" />
                  <Description>
                    Account email cannot be modified directly. Contact support for email changes.
                  </Description>
                </TextField>

                <TextField
                  isRequired
                  name="fullName"
                  value={fullName}
                  onChange={(value) => {
                    setFullName(value);
                    if (fieldErrors.fullName) {
                      setFieldErrors((prev) => ({ ...prev, fullName: undefined }));
                    }
                  }}
                  isInvalid={Boolean(fieldErrors.fullName)}
                  className="w-full"
                >
                  <Label>Full name</Label>
                  <Input
                    minLength={2}
                    maxLength={70}
                    pattern={FULL_NAME_PATTERN}
                    autoComplete="name"
                    placeholder="Nguyễn Văn A"
                  />
                  {fieldErrors.fullName && <FieldError>{fieldErrors.fullName}</FieldError>}
                </TextField>

                <TextField
                  name="phone"
                  type="tel"
                  value={phone}
                  onChange={(value) => {
                    setPhone(value);
                    if (fieldErrors.phone) {
                      setFieldErrors((prev) => ({ ...prev, phone: undefined }));
                    }
                  }}
                  isInvalid={Boolean(fieldErrors.phone)}
                  className="w-full"
                >
                  <Label>Phone number (optional)</Label>
                  <Input
                    maxLength={20}
                    placeholder="e.g. 0901234567"
                  />
                  {fieldErrors.phone && <FieldError>{fieldErrors.phone}</FieldError>}
                </TextField>

                <div className="space-y-1.5">
                  <Select
                    aria-label="Timezone"
                    className="w-full"
                    value={timeZoneId}
                    onChange={(key) => {
                      if (key != null) {
                        setTimeZoneId(String(key));
                        if (fieldErrors.timeZoneId) {
                          setFieldErrors((prev) => ({ ...prev, timeZoneId: undefined }));
                        }
                      }
                    }}
                    isInvalid={Boolean(fieldErrors.timeZoneId)}
                  >
                    <Label className="text-xs font-semibold text-foreground block">
                      Timezone
                    </Label>
                    <Select.Trigger>
                      <Select.Value />
                      <Select.Indicator />
                    </Select.Trigger>
                    <Select.Popover>
                      <ListBox>
                        {availableTimezones.map((tz) => (
                          <ListBox.Item key={tz} id={tz} textValue={tz}>
                            {tz}
                            <ListBox.ItemIndicator />
                          </ListBox.Item>
                        ))}
                      </ListBox>
                    </Select.Popover>
                  </Select>
                  {fieldErrors.timeZoneId && (
                    <p className="text-xs text-danger mt-1" role="alert">
                      {fieldErrors.timeZoneId}
                    </p>
                  )}
                  <span className="text-xs text-muted block">
                    All mentor sessions and exam countdowns are synchronized to this timezone.
                  </span>
                </div>

                <div className="pt-2">
                  <Button
                    type="submit"
                    variant="primary"
                    isPending={updateProfileMutation.isPending}
                  >
                    Save changes
                  </Button>
                </div>
              </Form>
            </Card.Content>
          </Card>

          {/* Active Certificate Switcher Card */}
          {learnerContext && (
            <Card className="shadow-surface">
              <Card.Header>
                <div className="flex items-center gap-2">
                  <Award className="w-5 h-5 text-accent" />
                  <Card.Title>Active assessment certificate</Card.Title>
                </div>
                <Card.Description>Switch your current diagnostic and practice focus</Card.Description>
              </Card.Header>

              <Card.Content className="space-y-4">
                <div className="p-3.5 rounded-lg bg-surface-secondary border border-border flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <span className="text-xs text-muted block">Current certificate</span>
                    <span
                      className="font-bold text-foreground text-sm block truncate max-w-[260px] sm:max-w-[380px]"
                      title={`${learnerContext.active?.code ?? ''} — ${learnerContext.active?.name ?? ''}`}
                    >
                      {learnerContext.active?.code} — {learnerContext.active?.name}
                    </span>
                  </div>
                  <Chip size="sm" color="success" variant="soft">
                    Active
                  </Chip>
                </div>

                <div className="space-y-2">
                  <label htmlFor="active-cert-select" className="text-xs font-semibold text-foreground block">
                    Select target certificate:
                  </label>
                  <div className="flex flex-wrap gap-2">
                    {(learnerContext.certificates ?? learnerContext.available ?? []).map((cert) => {
                      const isActive = cert.code === learnerContext.active?.code;
                      return (
                        <Button
                          key={cert.code}
                          size="sm"
                          variant={isActive ? 'primary' : 'secondary'}
                          isDisabled={isActive}
                          onPress={() => switchCertificate(cert.code)}
                        >
                          {cert.code} — {cert.name}
                        </Button>
                      );
                    })}
                  </div>
                </div>
              </Card.Content>
            </Card>
          )}

          {/* Change Password Card */}
          <Card className="shadow-surface">
            <Card.Header>
              <div className="flex items-center gap-2">
                <KeyRound className="w-5 h-5 text-accent" />
                <Card.Title>Change password</Card.Title>
              </div>
              <Card.Description>
                Updating your password terminates all other active sessions for account security
              </Card.Description>
            </Card.Header>

            <Card.Content className="space-y-4">
              {passwordError && (
                <Alert status="danger">
                  <Alert.Indicator>
                    <AlertCircle className="w-4 h-4 text-danger" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Password change failed</Alert.Title>
                    <Alert.Description>{passwordError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <form onSubmit={handlePasswordSubmit} className="space-y-4">
                <div className="space-y-1.5">
                  <label htmlFor="currentPassword" className="text-xs font-semibold text-foreground block">
                    Current password
                  </label>
                  <input
                    id="currentPassword"
                    type="password"
                    required
                    value={currentPassword}
                    onChange={(e) => setCurrentPassword(e.target.value)}
                    className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                  />
                </div>

                <div className="space-y-1.5">
                  <label htmlFor="newPassword" className="text-xs font-semibold text-foreground block">
                    New password
                  </label>
                  <input
                    id="newPassword"
                    type="password"
                    required
                    minLength={8}
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                  />
                </div>

                <div className="space-y-1.5">
                  <label htmlFor="confirmPassword" className="text-xs font-semibold text-foreground block">
                    Confirm new password
                  </label>
                  <input
                    id="confirmPassword"
                    type="password"
                    required
                    minLength={8}
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                  />
                </div>

                <div className="pt-2">
                  <Button
                    type="submit"
                    variant="primary"
                    isPending={changePasswordMutation.isPending}
                  >
                    Change password
                  </Button>
                </div>
              </form>
            </Card.Content>
          </Card>
        </div>
      )}
    </div>
  );
}
