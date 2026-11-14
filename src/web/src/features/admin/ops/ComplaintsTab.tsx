import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Input,
  Label,
  Modal,
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
  Search,
  ShieldCheck,
  UserCheck,
} from 'lucide-react';
import {
  api,
  type AdminComplaintListItemDto,
  type AdminComplaintDetailDto,
  type ComplaintStatusDto,
} from '../../../lib/api';
import {
  formatComplaintStatusLabel,
  formatComplaintTypeLabel,
  formatDateTime,
  getComplaintStatusColor,
  validateComplaintResolve,
  MAX_COMPLAINT_GRANT_CREDITS,
} from './opsHelpers';

export function ComplaintsTab() {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);

  // Detail & Resolve modal states
  const [selectedComplaintId, setSelectedComplaintId] = useState<number | null>(null);
  const [isResolveOpen, setIsResolveOpen] = useState(false);
  const [resolution, setResolution] = useState('');
  const [internalNotes, setInternalNotes] = useState('');
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
    mutationFn: () => {
      const parsedCredits = parseInt(grantCredits, 10) || 0;
      const val = validateComplaintResolve({
        resolution,
        internalNotes,
        grantCredits: parsedCredits,
      });

      if (!val.valid) {
        setResolveErrors(val.errors);
        throw new Error('Please resolve the validation issues.');
      }

      return api.admin.resolveComplaint(selectedComplaintId!, {
        resolution: resolution.trim(),
        internalNotes: internalNotes.trim(),
        grantCredits: parsedCredits,
      });
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
    setGrantCredits('0');
    setResolveErrors({});
    setActionError(null);
  }

  function openResolve() {
    resetResolveForm();
    setIsResolveOpen(true);
  }

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
            <TextField className="w-64" name="search" aria-label="Search complaints">
              <div className="relative flex items-center">
                <Search className="absolute left-2.5 h-4 w-4 text-muted pointer-events-none" />
                <Input
                  className="pl-9"
                  placeholder="Learner name, email, subject..."
                  value={query}
                  onChange={(e) => {
                    setQuery(e.target.value);
                    setPage(1);
                  }}
                />
              </div>
            </TextField>

            <div className="flex items-center gap-2">
              <Label className="text-xs text-muted whitespace-nowrap">Status:</Label>
              <select
                aria-label="Filter status"
                className="h-9 px-3 rounded-md border border-border bg-background text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-focus"
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPage(1);
                }}
              >
                <option value="">All statuses</option>
                <option value="open">Open</option>
                <option value="reviewing">Under review</option>
                <option value="resolved">Resolved</option>
              </select>
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
          {summary && summary.totalCount > summary.pageSize && (
            <div className="flex items-center justify-between text-xs text-muted px-2">
              <span>
                Showing {(summary.page - 1) * summary.pageSize + 1}–
                {Math.min(summary.page * summary.pageSize, summary.totalCount)} of {summary.totalCount} complaints
              </span>
              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant="secondary"
                  isDisabled={page <= 1}
                  onPress={() => setPage((p) => Math.max(1, p - 1))}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  isDisabled={page * summary.pageSize >= summary.totalCount}
                  onPress={() => setPage((p) => p + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
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
                            ? 'Mark as under review while investigating with mentor or checking grader logs.'
                            : 'Currently under active investigation.'}
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
                              Mark as under review
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
                  Send a response message to the learner, record internal notes for auditing, and
                  optionally issue make-good credits (capped at {MAX_COMPLAINT_GRANT_CREDITS} credits).
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

                <TextField
                  className="w-full"
                  name="grantCredits"
                  isInvalid={!!resolveErrors.grantCredits}
                >
                  <Label>Grant make-good credits (optional, 0–{MAX_COMPLAINT_GRANT_CREDITS})</Label>
                  <Input
                    type="number"
                    min={0}
                    max={MAX_COMPLAINT_GRANT_CREDITS}
                    value={grantCredits}
                    onChange={(e) => {
                      setGrantCredits(e.target.value);
                      if (resolveErrors.grantCredits) {
                        setResolveErrors((prev) => ({ ...prev, grantCredits: '' }));
                      }
                    }}
                  />
                  {resolveErrors.grantCredits && (
                    <p className="text-xs text-danger mt-1">{resolveErrors.grantCredits}</p>
                  )}
                </TextField>
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
