import React, { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Spinner,
  Table,
  Tabs,
} from '@heroui/react';
import {
  AlertCircle,
  Award,
  CheckCircle2,
  Clock,
  CreditCard,
  FileText,
  GraduationCap,
  HelpCircle,
  History,
  LifeBuoy,
  MessageSquare,
  PlusCircle,
  Receipt,
  Send,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import { api, ApiClientError } from '../../lib/api';
import { formatVnd } from '../../lib/security';
import {
  formatComplaintStatus,
  formatComplaintType,
  formatDateTime,
} from '../../lib/domain-labels';

type SupportCategory = 'mentor' | 'exam' | 'payment';

export function SupportPage() {
  const queryClient = useQueryClient();

  const [activeTab, setActiveTab] = useState<'history' | 'new'>('history');

  // Form state
  const [category, setCategory] = useState<SupportCategory>('mentor');
  const [type, setType] = useState<string>('mentor_no_show');
  const [selectedSubjectId, setSelectedSubjectId] = useState<string>('');
  const [reason, setReason] = useState<string>('');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitSuccess, setSubmitSuccess] = useState<string | null>(null);

  const complaintsQuery = useQuery({
    queryKey: ['support', 'complaints'],
    queryFn: () => api.support.complaints(),
    staleTime: 15_000,
    retry: false,
  });

  const subjectsQuery = useQuery({
    queryKey: ['support', 'eligible-subjects'],
    queryFn: () => api.support.eligibleSubjects(),
    staleTime: 30_000,
    retry: false,
  });

  const fileComplaintMutation = useMutation({
    mutationFn: (payload: {
      type: string;
      reason: string;
      bookingId?: number | null;
      attemptId?: number | null;
      orderId?: number | null;
    }) => api.support.fileComplaint(payload),
    onSuccess: () => {
      setSubmitError(null);
      setSubmitSuccess('Report submitted successfully. Our operations team will review it shortly.');
      setReason('');
      setSelectedSubjectId('');
      void queryClient.invalidateQueries({ queryKey: ['support', 'complaints'] });
      setActiveTab('history');
    },
    onError: (err) => {
      setSubmitSuccess(null);
      if (err instanceof ApiClientError) {
        setSubmitError(err.title || err.detail || 'Could not submit support ticket.');
      } else {
        setSubmitError((err as Error).message || 'Failed to submit complaint.');
      }
    },
  });

  const handleCategoryChange = (cat: SupportCategory) => {
    setCategory(cat);
    setSelectedSubjectId('');
    if (cat === 'mentor') setType('mentor_no_show');
    else if (cat === 'exam') setType('grading_issue');
    else if (cat === 'payment') setType('payment_issue');
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitError(null);

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      setSubmitError('Please provide a description or reason for this ticket.');
      return;
    }
    if (trimmedReason.length > 4000) {
      setSubmitError('Description must not exceed 4000 characters.');
      return;
    }

    let bookingId: number | null = null;
    let attemptId: number | null = null;
    let orderId: number | null = null;

    if (category === 'mentor') {
      const defaultId =
        selectedSubjectId ||
        (subjectsQuery.data?.bookings?.[0]?.bookingId ? String(subjectsQuery.data.bookings[0].bookingId) : '');
      bookingId = defaultId ? Number(defaultId) : null;
    } else if (category === 'exam') {
      const defaultId =
        selectedSubjectId ||
        (subjectsQuery.data?.attempts?.[0]?.attemptId ? String(subjectsQuery.data.attempts[0].attemptId) : '');
      attemptId = defaultId ? Number(defaultId) : null;
    } else if (category === 'payment') {
      const defaultId =
        selectedSubjectId ||
        (subjectsQuery.data?.orders?.[0]?.orderId ? String(subjectsQuery.data.orders[0].orderId) : '');
      orderId = defaultId ? Number(defaultId) : null;
    }

    fileComplaintMutation.mutate({
      type,
      reason: trimmedReason,
      bookingId,
      attemptId,
      orderId,
    });
  };

  const complaints = complaintsQuery.data || [];
  const subjects = subjectsQuery.data;

  return (
    <div className="max-w-5xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <LifeBuoy className="w-5 h-5 text-accent" />
            <span className="text-xs font-semibold uppercase tracking-wider text-muted">
              Learner Support Desk
            </span>
          </div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            Support & complaints
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            Submit issues regarding mentor sessions, mock exam grading, or payment disputes.
          </p>
        </div>

        <div>
          <Button
            variant="primary"
            size="sm"
            onPress={() => setActiveTab('new')}
          >
            <PlusCircle className="w-4 h-4 mr-1.5" />
            File a report
          </Button>
        </div>
      </div>

      {submitSuccess && (
        <Alert status="success">
          <Alert.Indicator>
            <CheckCircle2 className="w-5 h-5 text-success" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Ticket created</Alert.Title>
            <Alert.Description>{submitSuccess}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Tabs */}
      <Tabs
        selectedKey={activeTab}
        onSelectionChange={(key) => setActiveTab(key as 'history' | 'new')}
        className="w-full"
      >
        <Tabs.ListContainer>
          <Tabs.List aria-label="Support tickets workspace tabs">
            <Tabs.Tab id="history">
              My reports
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="new">
              File a report
              <Tabs.Indicator />
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>

        {/* Tab 1: Ticket History */}
        <Tabs.Panel id="history" className="pt-6">
          <Card className="shadow-surface">
            <Card.Header>
              <Card.Title>Submitted tickets & resolutions</Card.Title>
              <Card.Description>Review status, resolution notes, and awarded credit refunds</Card.Description>
            </Card.Header>

            <Card.Content>
              {complaintsQuery.isLoading ? (
                <div className="flex items-center justify-center py-12 gap-3 text-muted text-sm">
                  <Spinner size="sm" />
                  <span>Loading ticket history...</span>
                </div>
              ) : complaintsQuery.isError ? (
                <Alert status="danger">
                  <Alert.Indicator>
                    <AlertCircle className="w-4 h-4" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Unable to load reports</Alert.Title>
                    <Alert.Description>Failed to fetch your support tickets. Please retry.</Alert.Description>
                  </Alert.Content>
                </Alert>
              ) : complaints.length === 0 ? (
                <div className="text-center py-12 space-y-3">
                  <LifeBuoy className="w-10 h-10 text-muted mx-auto" />
                  <p className="text-base font-semibold text-foreground m-0">No open reports</p>
                  <p className="text-sm text-muted m-0">
                    You have not submitted any complaints or support tickets.
                  </p>
                </div>
              ) : (
                <div className="space-y-4">
                  {complaints.map((c) => {
                    const statusDisplay = formatComplaintStatus(c.status);
                    const isResolved = (c.status || '').toLowerCase() === 'resolved';

                    return (
                      <div
                        key={c.complaintId}
                        className="p-4 sm:p-5 rounded-lg bg-surface-secondary border border-border space-y-3"
                      >
                        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                          <div className="flex items-center gap-2">
                            <span className="font-mono-tabular font-bold text-foreground">
                              #{c.complaintId}
                            </span>
                            <span className="text-muted">•</span>
                            <span className="font-semibold text-foreground text-sm">
                              {formatComplaintType(c.type)}
                            </span>
                            {c.subjectSummary && (
                              <span className="text-xs text-muted">
                                ({c.subjectSummary})
                              </span>
                            )}
                          </div>

                          <div className="flex items-center gap-2">
                            <Chip size="sm" color={statusDisplay.tone} variant="soft">
                              {statusDisplay.label}
                            </Chip>
                            <span className="text-xs font-mono-tabular text-muted">
                              {formatDateTime(c.createdAt)}
                            </span>
                          </div>
                        </div>

                        {/* Learner description / reason */}
                        <p className="text-sm text-foreground m-0 leading-normal">
                          {c.reason}
                        </p>

                        {/* Resolution details (if resolved) */}
                        {isResolved && c.resolution && (
                          <div className="mt-2 p-3 rounded bg-surface border border-border space-y-1.5 text-xs">
                            <div className="flex items-center justify-between">
                              <span className="font-semibold text-success flex items-center gap-1">
                                <CheckCircle2 className="w-3.5 h-3.5 text-success" />
                                Official resolution
                              </span>
                              {c.resolvedAt && (
                                <span className="font-mono-tabular text-muted">
                                  Resolved {formatDateTime(c.resolvedAt)}
                                </span>
                              )}
                            </div>
                            <p className="text-foreground m-0">{c.resolution}</p>
                            {c.grantedCredits != null && c.grantedCredits > 0 && (
                              <div className="pt-1">
                                <Chip size="sm" color="success" variant="soft" className="font-mono-tabular font-bold">
                                  +{c.grantedCredits} credits refunded
                                </Chip>
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </Card.Content>
          </Card>
        </Tabs.Panel>

        {/* Tab 2: File New Ticket */}
        <Tabs.Panel id="new" className="pt-6">
          <Card className="shadow-surface">
            <Card.Header>
              <Card.Title>Submit a new issue</Card.Title>
              <Card.Description>
                Provide details and select the related booking, exam attempt, or order receipt
              </Card.Description>
            </Card.Header>

            <Card.Content className="space-y-6">
              {submitError && (
                <Alert status="danger">
                  <Alert.Indicator>
                    <AlertCircle className="w-4 h-4 text-danger" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Submission error</Alert.Title>
                    <Alert.Description>{submitError}</Alert.Description>
                  </Alert.Content>
                </Alert>
              )}

              <form onSubmit={handleSubmit} className="space-y-5">
                {/* Category selector */}
                <div className="space-y-2">
                  <label className="text-xs font-semibold text-foreground uppercase tracking-wider block">
                    Issue category:
                  </label>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      type="button"
                      size="sm"
                      variant={category === 'mentor' ? 'primary' : 'secondary'}
                      onPress={() => handleCategoryChange('mentor')}
                    >
                      <GraduationCap className="w-3.5 h-3.5 mr-1.5" />
                      Mentor session
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      variant={category === 'exam' ? 'primary' : 'secondary'}
                      onPress={() => handleCategoryChange('exam')}
                    >
                      <Award className="w-3.5 h-3.5 mr-1.5" />
                      Exam & grading
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      variant={category === 'payment' ? 'primary' : 'secondary'}
                      onPress={() => handleCategoryChange('payment')}
                    >
                      <CreditCard className="w-3.5 h-3.5 mr-1.5" />
                      Billing & payment
                    </Button>
                  </div>
                </div>

                {/* Specific complaint type */}
                <div className="space-y-1.5">
                  <label htmlFor="complaintType" className="text-xs font-semibold text-foreground block">
                    Report type:
                  </label>
                  <select
                    id="complaintType"
                    value={type}
                    onChange={(e) => setType(e.target.value)}
                    className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                  >
                    {category === 'mentor' && (
                      <>
                        <option value="mentor_no_show">Mentor did not attend</option>
                        <option value="mentor_quality">Mentor quality or connection</option>
                      </>
                    )}
                    {category === 'exam' && (
                      <>
                        <option value="grading_issue">Grading issue</option>
                        <option value="technical_error">Technical error during exam</option>
                      </>
                    )}
                    {category === 'payment' && (
                      <option value="payment_issue">Payment or checkout issue</option>
                    )}
                  </select>
                </div>

                {/* Eligible subject selector */}
                <div className="space-y-1.5">
                  <label htmlFor="subjectSelect" className="text-xs font-semibold text-foreground block">
                    Related {category === 'mentor' ? 'booking' : category === 'exam' ? 'exam attempt' : 'order'}:
                  </label>

                  {subjectsQuery.isLoading ? (
                    <div className="text-xs text-muted flex items-center gap-2">
                      <Spinner size="sm" />
                      <span>Loading eligible activities...</span>
                    </div>
                  ) : category === 'mentor' ? (
                    (subjects?.bookings?.length ?? 0) === 0 ? (
                      <p className="text-xs text-muted m-0 p-2 bg-surface rounded border border-border">
                        No recent mentor bookings found.
                      </p>
                    ) : (
                      <select
                        id="subjectSelect"
                        value={selectedSubjectId}
                        onChange={(e) => setSelectedSubjectId(e.target.value)}
                        className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                      >
                        {subjects!.bookings.map((b) => (
                          <option key={b.bookingId} value={b.bookingId}>
                            Booking #{b.bookingId} — {b.mentorName} ({formatDateTime(b.scheduledAt)})
                          </option>
                        ))}
                      </select>
                    )
                  ) : category === 'exam' ? (
                    (subjects?.attempts?.length ?? 0) === 0 ? (
                      <p className="text-xs text-muted m-0 p-2 bg-surface rounded border border-border">
                        No recent exam attempts found.
                      </p>
                    ) : (
                      <select
                        id="subjectSelect"
                        value={selectedSubjectId}
                        onChange={(e) => setSelectedSubjectId(e.target.value)}
                        className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                      >
                        {subjects!.attempts.map((a) => (
                          <option key={a.attemptId} value={a.attemptId}>
                            Attempt #{a.attemptId} — {a.examTitle} ({formatDateTime(a.startedAt)})
                          </option>
                        ))}
                      </select>
                    )
                  ) : (
                    (subjects?.orders?.length ?? 0) === 0 ? (
                      <p className="text-xs text-muted m-0 p-2 bg-surface rounded border border-border">
                        No recent orders found.
                      </p>
                    ) : (
                      <select
                        id="subjectSelect"
                        value={selectedSubjectId}
                        onChange={(e) => setSelectedSubjectId(e.target.value)}
                        className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                      >
                        {subjects!.orders.map((o) => (
                          <option key={o.orderId} value={o.orderId}>
                            Order {o.orderCode} — {o.packageName} ({formatVnd(o.amount)})
                          </option>
                        ))}
                      </select>
                    )
                  )}
                </div>

                {/* Description & reason */}
                <div className="space-y-1.5">
                  <div className="flex items-center justify-between">
                    <label htmlFor="reason" className="text-xs font-semibold text-foreground">
                      Description & reason
                    </label>
                    <span className="text-xs font-mono-tabular text-muted">
                      {reason.length}/4000
                    </span>
                  </div>
                  <textarea
                    id="reason"
                    required
                    rows={4}
                    maxLength={4000}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    placeholder="Provide specific details about what occurred..."
                    className="w-full text-sm p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                  />
                </div>

                <div className="pt-2">
                  <Button
                    type="submit"
                    variant="primary"
                    isPending={fileComplaintMutation.isPending}
                  >
                    <Send className="w-4 h-4 mr-1.5" />
                    Submit report
                  </Button>
                </div>
              </form>
            </Card.Content>
          </Card>
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}
