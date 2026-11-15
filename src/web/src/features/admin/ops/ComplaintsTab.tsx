import React, { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import {
  Alert,
  Button,
  Card,
  Chip,
  Description,
  Input,
  Label,
  ListBox,
  Modal,
  Radio,
  RadioGroup,
  SearchField,
  Select,
  Spinner,
  Table,
  TextArea,
  TextField,
} from '@heroui/react';
import {
  AlertCircle,
  CheckCircle2,
  Clock,
  HelpCircle,
  MessageSquare,
  RefreshCw,
  ShieldCheck,
  UserCheck,
} from 'lucide-react';
import { OpsPagination } from './OpsPagination';
import { formatVnd } from '../../../lib/security';
import {
  api,
  type AdminComplaintListItemDto,
  type AdminComplaintDetailDto,
  type ComplaintCompensationDto,
  type ComplaintStatusDto,
} from '../../../lib/api';
import {
  formatComplaintStatusLabel,
  formatComplaintTypeLabel,
  formatDateTime,
  formatRefundMethodLabel,
  formatRefundStatusLabel,
  getComplaintStatusColor,
  getRefundStatusColor,
  validateComplaintResolve,
} from './opsHelpers';

export function ComplaintsTab() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);

  // Detail & Resolve modal states
  const [selectedComplaintId, setSelectedComplaintId] = useState<number | null>(null);
  const [isResolveOpen, setIsResolveOpen] = useState(false);
  const [resolution, setResolution] = useState('');
  const [internalNotes, setInternalNotes] = useState('');
  // #350: one ending at a time. The amount is seeded from what the learner spent, never from zero.
  const [compensation, setCompensation] = useState<ComplaintCompensationDto>('none');
  const [grantCredits, setGrantCredits] = useState('0');
  const [resolveErrors, setResolveErrors] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);

  const { data: summary, isLoading, error } = useQuery({
    queryKey: ['admin-complaints', query, statusFilter, page],
    queryFn: () =>
      api.admin.complaints({
        q: query || undefined,
        status: statusFilter || undefined,
        page,
        pageSize: 20,
      }),
  });

  const { data: complaintDetail, isLoading: isLoadingDetail } = useQuery({
    queryKey: ['admin-complaint-detail', selectedComplaintId],
    queryFn: () => api.admin.complaintDetail(selectedComplaintId!),
    enabled: selectedComplaintId !== null,
  });

  const statusMutation = useMutation({
    mutationFn: ({ id, status }: { id: number; status: ComplaintStatusDto }) =>
      api.admin.updateComplaintStatus(id, status),
    onSuccess: (updated) => {
      setActionError(null);
      void queryClient.invalidateQueries({ queryKey: ['admin-complaints'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-complaint-detail', updated.complaintId] });
    },
    onError: (err: any) => {
      setActionError(err?.message || 'Could not update complaint status.');
    },
  });

  const resolveMutation = useMutation({
    mutationFn: async () => {
      const parsedCredits = compensation === 'make_good' ? parseInt(grantCredits, 10) || 0 : 0;
      const val = validateComplaintResolve({
        resolution,
        internalNotes,
        compensation,
        grantCredits: parsedCredits,
        creditsSpent: complaintDetail?.creditsSpent ?? 0,
        impliedRefundMethod: complaintDetail?.impliedRefundMethod ?? null,
        moneyPaid: complaintDetail?.moneyPaid ?? null,
      });

      if (!val.valid) {
        setResolveErrors(val.errors);
        throw new Error('Please resolve the validation issues.');
      }

      const updated = await api.admin.resolveComplaint(selectedComplaintId!, {
        resolution: resolution.trim(),
        internalNotes: internalNotes.trim(),
        compensation,
        grantCredits: parsedCredits,
      });

      // #350: recording a refund is a promise, not a payment. The desk owns creating it, with its own
      // permission, so this ending hands the incident over and moves no money.
      if (compensation === 'refund') {
        // #345: carry what the complaint already knows about the subject, not only who it is about. The
        // desk can then land the order pick and the pre-filled amount (or the credit count) without the
        // agent reading a figure off one screen and retyping it into another. It is convenience, not
        // authority: the refund route still resolves the subject's ownership server-side. The values come
        // from the server's post-resolve detail, not from this render's cached copy.
        const isOrderSubject = updated.subjectType === 'Order';
        await navigate({
          to: '/admin/finance',
          search: {
            complaintId: updated.complaintId,
            userId: updated.userId,
            orderId: isOrderSubject ? updated.subjectId : undefined,
            // Only a credit subject has a credit count to offer; an order's value is its money amount,
            // which the desk reads from the order rather than from a number in a URL.
            creditCount: !isOrderSubject && updated.creditsSpent
              ? updated.creditsSpent
              : undefined,
          } as never,
        });
      }

      return updated;
    },
    onSuccess: (updated) => {
      setIsResolveOpen(false);
      resetResolveForm();
      void queryClient.invalidateQueries({ queryKey: ['admin-complaints'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-complaint-detail', updated.complaintId] });
    },
    onError: (err: any) => {
      setActionError(err?.message || 'Could not resolve complaint.');
    },
  });

  function resetResolveForm() {
    setResolution('');
    setInternalNotes('');
    setCompensation('none');
    setGrantCredits('0');
    setResolveErrors({});
    setActionError(null);
  }

  function openResolve() {
    resetResolveForm();
    setIsResolveOpen(true);
  }

  /** #350: choosing the make-good ending seeds the amount with what was actually spent, so the
   * default is the correct figure rather than zero. */
  function chooseCompensation(next: string) {
    const ending = next as ComplaintCompensationDto;
    setCompensation(ending);
    setResolveErrors({});
    setGrantCredits(
      ending === 'make_good' ? String(complaintDetail?.creditsSpent ?? 0) : '0',
    );
  }

  // #350: what the subject implies, read from #347's projection rather than re-derived here.
  const creditsSpent = complaintDetail?.creditsSpent ?? 0;
  const moneyPaid = complaintDetail?.moneyPaid ?? null;
  const impliedMethod = complaintDetail?.impliedRefundMethod ?? null;
  // A make-good is credits, so a money subject has none to offer; nothing charged means neither.
  const canMakeGood = creditsSpent > 0 && impliedMethod !== 'money';
  const canRefund = impliedMethod != null && (creditsSpent > 0 || moneyPaid != null);

  return (
    <div className="space-y-6">
      {/* Metrics Row */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Pending triage</p>
              <p className="text-2xl font-semibold font-mono-tabular text-warning mt-1">
                {summary ? summary.pendingCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Open or under investigation</p>
            </div>
            <div className="p-2.5 rounded-full bg-warning/10 text-warning">
              <Clock className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Resolved</p>
              <p className="text-2xl font-semibold font-mono-tabular text-success mt-1">
                {summary ? summary.resolvedCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Settled with feedback</p>
            </div>
            <div className="p-2.5 rounded-full bg-success/10 text-success">
              <CheckCircle2 className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Oldest pending</p>
              <p className="text-sm font-semibold text-foreground mt-1 truncate max-w-[200px]">
                {summary?.oldestPendingCreatedAt
                  ? `${formatDateTime(summary.oldestPendingCreatedAt)}`
                  : 'None pending'}
              </p>
              <p className="text-xs text-muted mt-0.5">
                {summary?.oldestPendingType
                  ? formatComplaintTypeLabel(summary.oldestPendingType)
                  : 'SLA up to date'}
              </p>
            </div>
            <div className="p-2.5 rounded-full bg-accent/10 text-accent">
              <AlertCircle className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>
      </div>

      {/* Filter Toolbar */}
      <Card className="border border-border">
        <Card.Content className="p-4 flex flex-wrap gap-4 items-center justify-between">
          <div className="flex flex-wrap gap-3 items-center flex-1">
            <SearchField
              className="w-64"
              name="search"
              aria-label="Search complaints"
              value={query}
              onChange={(val) => {
                setQuery(val);
                setPage(1);
              }}
              onClear={() => {
                setQuery('');
                setPage(1);
              }}
            >
              <SearchField.Group>
                <SearchField.SearchIcon />
                <SearchField.Input placeholder="Learner name, email, subject..." />
                <SearchField.ClearButton />
              </SearchField.Group>
            </SearchField>

            <div className="flex items-center gap-2">
              <Select
                aria-label="Filter status"
                className="w-44"
                value={statusFilter || 'all'}
                onChange={(key) => {
                  setStatusFilter(key === 'all' || key == null ? '' : String(key));
                  setPage(1);
                }}
              >
                <Select.Trigger>
                  <Select.Value />
                  <Select.Indicator />
                </Select.Trigger>
                <Select.Popover>
                  <ListBox>
                    <ListBox.Item id="all" textValue="All statuses">
                      All statuses
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                    <ListBox.Item id="open" textValue="Open">
                      Open
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                    <ListBox.Item id="reviewing" textValue="Processing">
                      Processing
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                    <ListBox.Item id="resolved" textValue="Resolved">
                      Resolved
                      <ListBox.ItemIndicator />
                    </ListBox.Item>
                  </ListBox>
                </Select.Popover>
              </Select>
            </div>
          </div>

          <Button
            size="sm"
            variant="secondary"
            onPress={() => void queryClient.invalidateQueries({ queryKey: ['admin-complaints'] })}
          >
            <RefreshCw className="h-4 w-4 mr-1.5" />
            Refresh
          </Button>
        </Card.Content>
      </Card>

      {/* Complaints Table */}
      {isLoading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : error ? (
        <Alert status="danger">Could not load complaints. Please try again.</Alert>
      ) : summary?.complaints.length === 0 ? (
        <Card className="border border-border p-8 text-center text-muted">
          No complaints match the current filter criteria.
        </Card>
      ) : (
        <div className="space-y-4">
          <Table>
            <Table.ScrollContainer>
              <Table.Content aria-label="Customer support complaints table">
                <Table.Header>
                  <Table.Column isRowHeader>Complaint ID</Table.Column>
                  <Table.Column>Learner</Table.Column>
                  <Table.Column>Category</Table.Column>
                  <Table.Column>Subject</Table.Column>
                  <Table.Column>Status</Table.Column>
                  <Table.Column>Filed at</Table.Column>
                  <Table.Column>Actions</Table.Column>
                </Table.Header>
                <Table.Body>
                  {summary?.complaints.map((c) => (
                    <Table.Row key={c.complaintId}>
                      <Table.Cell>
                        <span className="font-mono-tabular font-semibold text-foreground">
                          #{c.complaintId}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <div>
                          <p className="text-sm font-medium text-foreground">{c.learnerName}</p>
                          <p className="text-xs text-muted font-mono-tabular">{c.learnerEmail}</p>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs font-medium text-foreground">
                          {formatComplaintTypeLabel(c.type)}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <p className="text-xs text-foreground max-w-xs truncate" title={c.subjectSummary}>
                          {c.subjectSummary}
                        </p>
                      </Table.Cell>
                      <Table.Cell>
                        <Chip size="sm" color={getComplaintStatusColor(c.status)} variant="soft">
                          {formatComplaintStatusLabel(c.status)}
                        </Chip>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs text-muted font-mono-tabular">
                          {formatDateTime(c.createdAt)}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <Button
                          size="sm"
                          variant="secondary"
                          onPress={() => setSelectedComplaintId(c.complaintId)}
                        >
                          View
                        </Button>
                      </Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Content>
            </Table.ScrollContainer>
          </Table>

          {/* Pagination */}
          {summary && (
            <OpsPagination
              page={summary.page}
              pageSize={summary.pageSize}
              totalCount={summary.totalCount}
              itemLabel="complaints"
              onPageChange={setPage}
            />
          )}
        </div>
      )}

      {/* Complaint Detail Modal */}
      {selectedComplaintId !== null && (
        <Modal.Backdrop
          isOpen={selectedComplaintId !== null}
          onOpenChange={(isOpen) => {
            if (!isOpen) {
              setSelectedComplaintId(null);
              setIsResolveOpen(false);
              setActionError(null);
            }
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-2xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>Complaint details — #{selectedComplaintId}</Modal.Heading>
              </Modal.Header>

              <Modal.Body className="space-y-6">
                {isLoadingDetail ? (
                  <div className="flex justify-center py-8">
                    <Spinner size="md" />
                  </div>
                ) : complaintDetail ? (
                  <>
                    {actionError && <Alert status="danger">{actionError}</Alert>}

                    <div className="grid grid-cols-2 sm:grid-cols-3 gap-4 p-4 rounded-lg bg-surface border border-border">
                      <div>
                        <p className="text-xs text-muted">Learner</p>
                        <p className="text-sm font-medium text-foreground">
                          {complaintDetail.learnerName}
                        </p>
                        <p className="text-xs font-mono-tabular text-muted">
                          {complaintDetail.learnerEmail} (User #{complaintDetail.userId})
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Category</p>
                        <p className="text-sm font-medium text-foreground">
                          {formatComplaintTypeLabel(complaintDetail.type)}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Status</p>
                        <Chip
                          size="sm"
                          color={getComplaintStatusColor(complaintDetail.status)}
                          variant="soft"
                        >
                          {formatComplaintStatusLabel(complaintDetail.status)}
                        </Chip>
                      </div>

                      <div className="col-span-2 sm:col-span-3">
                        <p className="text-xs text-muted">Subject matter</p>
                        <p className="text-sm font-medium text-foreground">
                          {complaintDetail.subjectSummary}
                        </p>
                        {complaintDetail.subjectDetails && (
                          <p className="text-xs text-muted mt-0.5">
                            {complaintDetail.subjectDetails}
                          </p>
                        )}
                        {/* #347: what the learner actually spent - read from the ledger, never guessed. */}
                        <p className="text-xs text-muted mt-1">
                          {(complaintDetail.creditsSpent ?? 0) > 0 ? (
                            <>
                              Learner spent{' '}
                              <span className="font-mono-tabular text-foreground">
                                {complaintDetail.creditsSpent} credit
                                {complaintDetail.creditsSpent === 1 ? '' : 's'}
                              </span>
                              {complaintDetail.moneyPaid != null &&
                                ` (${formatVnd(complaintDetail.moneyPaid)} paid)`}
                            </>
                          ) : complaintDetail.moneyPaid != null ? (
                            <>
                              Learner paid{' '}
                              <span className="font-mono-tabular text-foreground">
                                {formatVnd(complaintDetail.moneyPaid)}
                              </span>
                            </>
                          ) : (
                            'Nothing to refund on this subject.'
                          )}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Filed at</p>
                        <p className="text-xs font-mono-tabular text-foreground">
                          {formatDateTime(complaintDetail.createdAt)}
                        </p>
                      </div>

                      {complaintDetail.resolvedAt && (
                        <div>
                          <p className="text-xs text-muted">Resolved at</p>
                          <p className="text-xs font-mono-tabular text-foreground">
                            {formatDateTime(complaintDetail.resolvedAt)}
                          </p>
                          {complaintDetail.resolvedByUserName && (
                            <p className="text-xs text-muted">
                              by {complaintDetail.resolvedByUserName}
                            </p>
                          )}
                        </div>
                      )}
                    </div>

                    {/* Learner's Problem Description */}
                    <div className="p-4 rounded border border-border bg-surface-secondary text-sm space-y-1">
                      <p className="text-xs font-medium text-muted uppercase tracking-wider">
                        Learner description & reason
                      </p>
                      <p className="text-foreground leading-relaxed whitespace-pre-wrap">
                        {complaintDetail.reason}
                      </p>
                    </div>

                    {/* #345 story 11: the refund an agent created from this complaint, so the history
                        of one incident is in one place. A promise with no refund yet says so rather
                        than showing an empty box. */}
                    {complaintDetail.refunds && complaintDetail.refunds.length > 0 && (
                      <div className="p-4 rounded border border-border bg-surface space-y-2">
                        <p className="text-xs font-medium text-muted uppercase tracking-wider">
                          Refunds recorded against this complaint
                        </p>
                        {complaintDetail.refunds.map((refund) => (
                          <div
                            key={refund.refundId}
                            className="flex flex-wrap items-center justify-between gap-2 text-sm"
                          >
                            <div className="flex items-center gap-2">
                              <span className="font-mono-tabular font-semibold text-foreground">
                                #{refund.refundId}
                              </span>
                              <span className="text-foreground">
                                {formatRefundMethodLabel(refund.method)}
                              </span>
                              <Chip
                                size="sm"
                                color={getRefundStatusColor(refund.status)}
                                variant="soft"
                              >
                                {formatRefundStatusLabel(refund.status)}
                              </Chip>
                            </div>
                            <div className="flex items-center gap-3">
                              <span className="font-mono-tabular font-medium text-foreground">
                                {refund.method === 'money'
                                  ? formatVnd(refund.amount ?? 0)
                                  : `${refund.creditCount ?? 0} credits`}
                              </span>
                              <span className="text-xs text-muted font-mono-tabular">
                                {formatDateTime(refund.createdAt)}
                              </span>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}

                    {/* If Resolved, display resolution & notes */}
                    {complaintDetail.status === 'resolved' && (
                      <div className="p-4 rounded border border-success/30 bg-success/5 space-y-3">
                        <div>
                          <p className="text-xs font-semibold text-success uppercase tracking-wider">
                            Resolution message sent to learner
                          </p>
                          <p className="text-sm text-foreground mt-1 whitespace-pre-wrap">
                            {complaintDetail.resolution || '—'}
                          </p>
                        </div>

                        {complaintDetail.internalNotes && (
                          <div className="pt-2 border-t border-success/20">
                            <p className="text-xs font-semibold text-muted uppercase tracking-wider">
                              Internal staff notes
                            </p>
                            <p className="text-xs text-muted mt-1 whitespace-pre-wrap">
                              {complaintDetail.internalNotes}
                            </p>
                          </div>
                        )}

                        {complaintDetail.grantedCredits ? (
                          <div className="p-2 rounded bg-success/10 text-success text-xs font-medium">
                            Make-good granted: {complaintDetail.grantedCredits} credits
                            {complaintDetail.resolvedByGrantTxnId
                              ? ` (Ledger Txn #${complaintDetail.resolvedByGrantTxnId})`
                              : ''}
                          </div>
                        ) : null}
                      </div>
                    )}

                    {/* Triage Actions (When Not Resolved) */}
                    {complaintDetail.status !== 'resolved' && (
                      <div className="p-4 rounded-md border border-border bg-surface flex flex-wrap items-center justify-between gap-3">
                        <div className="text-xs text-muted">
                          {complaintDetail.status === 'open'
                            ? 'Start processing while investigating with mentor or checking grader logs.'
                            : 'Currently being processed.'}
                        </div>

                        <div className="flex gap-2">
                          {complaintDetail.status === 'open' && (
                            <Button
                              size="sm"
                              variant="secondary"
                              isDisabled={statusMutation.isPending}
                              onPress={() =>
                                statusMutation.mutate({
                                  id: complaintDetail.complaintId,
                                  status: 'reviewing',
                                })
                              }
                            >
                              Start processing
                            </Button>
                          )}

                          {complaintDetail.status === 'reviewing' && (
                            <Button
                              size="sm"
                              variant="secondary"
                              isDisabled={statusMutation.isPending}
                              onPress={() =>
                                statusMutation.mutate({
                                  id: complaintDetail.complaintId,
                                  status: 'open',
                                })
                              }
                            >
                              Return to open
                            </Button>
                          )}

                          <Button size="sm" variant="primary" onPress={openResolve}>
                            Resolve complaint
                          </Button>
                        </div>
                      </div>
                    )}
                  </>
                ) : null}
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setSelectedComplaintId(null)}>
                  Close
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}

      {/* Resolve Complaint Modal */}
      {isResolveOpen && selectedComplaintId !== null && (
        <Modal.Backdrop
          isOpen={isResolveOpen}
          onOpenChange={(isOpen) => {
            if (!isOpen) setIsResolveOpen(false);
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>Resolve complaint #{selectedComplaintId}</Modal.Heading>
                <p className="text-xs text-muted mt-1">
                  Send a response to the learner, record internal notes for auditing, and choose one
                  compensation ending.
                </p>
              </Modal.Header>

              <Modal.Body className="space-y-4">
                {actionError && <Alert status="danger">{actionError}</Alert>}

                <TextField
                  className="w-full"
                  name="resolution"
                  isRequired
                  isInvalid={!!resolveErrors.resolution}
                >
                  <Label>Message for learner</Label>
                  <TextArea
                    rows={4}
                    placeholder="Explain the outcome of the investigation and steps taken..."
                    value={resolution}
                    onChange={(e) => {
                      setResolution(e.target.value);
                      if (resolveErrors.resolution) {
                        setResolveErrors((prev) => ({ ...prev, resolution: '' }));
                      }
                    }}
                  />
                  {resolveErrors.resolution && (
                    <p className="text-xs text-danger mt-1">{resolveErrors.resolution}</p>
                  )}
                </TextField>

                <TextField
                  className="w-full"
                  name="internalNotes"
                  isRequired
                  isInvalid={!!resolveErrors.internalNotes}
                >
                  <Label>Internal investigation notes</Label>
                  <TextArea
                    rows={3}
                    placeholder="Audit trail: what logs or mentor statements were checked..."
                    value={internalNotes}
                    onChange={(e) => {
                      setInternalNotes(e.target.value);
                      if (resolveErrors.internalNotes) {
                        setResolveErrors((prev) => ({ ...prev, internalNotes: '' }));
                      }
                    }}
                  />
                  {resolveErrors.internalNotes && (
                    <p className="text-xs text-danger mt-1">{resolveErrors.internalNotes}</p>
                  )}
                </TextField>

                {/* #350: one compensation ending, in plain language. What is offered follows the
                    subject: an order was paid in money so it offers a money refund; an attempt or a
                    booking was paid in credits so it offers credits; a subject nobody charged offers
                    neither, and says so. */}
                <RadioGroup
                  name="compensation"
                  value={compensation}
                  onChange={chooseCompensation}
                  isInvalid={!!resolveErrors.compensation}
                >
                  <Label>Compensation</Label>
                  <Description>Choose exactly one ending.</Description>

                  <Radio value="none">
                    <Radio.Content>
                      <Radio.Control>
                        <Radio.Indicator />
                      </Radio.Control>
                      No compensation
                    </Radio.Content>
                    <Description>A reply to the learner, with no value returned.</Description>
                  </Radio>

                  {creditsSpent > 0 && impliedMethod !== 'money' && (
                    <Radio value="make_good">
                      <Radio.Content>
                        <Radio.Control>
                          <Radio.Indicator />
                        </Radio.Control>
                        Make-good credits
                      </Radio.Content>
                      <Description>
                        Up to {creditsSpent} credit{creditsSpent === 1 ? '' : 's'}, the amount spent
                        on this subject.
                      </Description>
                    </Radio>
                  )}

                  {canRefund && (
                    <Radio value="refund">
                      <Radio.Content>
                        <Radio.Control>
                          <Radio.Indicator />
                        </Radio.Control>
                        Record a refund
                      </Radio.Content>
                      <Description>
                        Hands this incident to the refund desk. Creates no refund by itself.
                      </Description>
                    </Radio>
                  )}
                </RadioGroup>

                {resolveErrors.compensation && (
                  <p className="text-xs text-danger">{resolveErrors.compensation}</p>
                )}

                {!canRefund && !canMakeGood && (
                  <p className="text-xs text-muted">
                    Nothing to refund: this subject was never charged.
                  </p>
                )}

                {compensation === 'make_good' && (
                  <TextField
                    className="w-full"
                    name="grantCredits"
                    isRequired
                    isInvalid={!!resolveErrors.grantCredits}
                  >
                    <Label>Credits to make good</Label>
                    <Input
                      type="number"
                      min={1}
                      max={creditsSpent}
                      value={grantCredits}
                      onChange={(e) => {
                        setGrantCredits(e.target.value);
                        if (resolveErrors.grantCredits) {
                          setResolveErrors((prev) => ({ ...prev, grantCredits: '' }));
                        }
                      }}
                    />
                    <Description>
                      Pre-filled with what the learner spent. At most {creditsSpent} credit
                      {creditsSpent === 1 ? '' : 's'} — compensation cannot exceed the loss.
                    </Description>
                    {resolveErrors.grantCredits && (
                      <p className="text-xs text-danger mt-1">{resolveErrors.grantCredits}</p>
                    )}
                  </TextField>
                )}

                {compensation === 'refund' && (
                  <div
                    role="group"
                    aria-label="Refund handoff"
                    className="p-3 rounded bg-surface-secondary border border-border space-y-1"
                  >
                    <p className="text-xs text-foreground">
                      {impliedMethod === 'money' ? (
                        <>
                          Money refund, pre-filled with the order amount:{' '}
                          <span className="font-mono-tabular font-semibold">
                            {formatVnd(moneyPaid ?? 0)}
                          </span>
                        </>
                      ) : (
                        <>
                          Credit refund, pre-filled with the credits spent:{' '}
                          <span className="font-mono-tabular font-semibold">
                            {creditsSpent} credit{creditsSpent === 1 ? '' : 's'}
                          </span>
                        </>
                      )}
                    </p>
                    <p className="text-xs text-muted">
                      Record a refund creates no refund. The refund desk opens with this learner and
                      this complaint already applied, and approval stays a separate decision.
                    </p>
                  </div>
                )}
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setIsResolveOpen(false)}>
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  isDisabled={resolveMutation.isPending}
                  onPress={() => resolveMutation.mutate()}
                >
                  {resolveMutation.isPending ? 'Resolving...' : 'Confirm resolution'}
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}
    </div>
  );
}
