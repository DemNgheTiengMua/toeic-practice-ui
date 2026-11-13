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
  ArrowDownToLine,
  CheckCircle2,
  Clock,
  Download,
  FileSpreadsheet,
  Plus,
  RefreshCw,
  Search,
  XCircle,
} from 'lucide-react';
import {
  api,
  type AdminRefundListItemDto,
  type AdminRefundDetailDto,
  type RefundMethodDto,
} from '../../../lib/api';
import { formatVnd } from '../../../lib/security';
import {
  formatDateTime,
  formatRefundMethodLabel,
  formatRefundStatusLabel,
  getRefundStatusColor,
  triggerBlobDownload,
  validateCsvExportDates,
  validateRefundRequest,
  DEFAULT_ORDERLESS_CEILING,
} from './opsHelpers';

export function RefundsTab() {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);

  // Modals state
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [isExportOpen, setIsExportOpen] = useState(false);
  const [selectedRefundId, setSelectedRefundId] = useState<number | null>(null);

  // Create form state
  const [createUserId, setCreateUserId] = useState('');
  const [createMethod, setCreateMethod] = useState<RefundMethodDto>('credit');
  const [createCreditCount, setCreateCreditCount] = useState('5');
  const [createAmount, setCreateAmount] = useState('100000');
  const [createOrderId, setCreateOrderId] = useState('');
  const [createComplaintId, setCreateComplaintId] = useState('');
  const [createReason, setCreateReason] = useState('');
  const [createErrors, setCreateErrors] = useState<Record<string, string>>({});
  const [apiError, setApiError] = useState<string | null>(null);

  // Reject prompt state in Detail modal
  const [isRejecting, setIsRejecting] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [rejectError, setRejectError] = useState<string | null>(null);

  // CSV Export state
  const [exportFrom, setExportFrom] = useState('');
  const [exportTo, setExportTo] = useState('');
  const [exportError, setExportError] = useState<string | null>(null);
  const [isExporting, setIsExporting] = useState(false);

  const { data: summary, isLoading, error } = useQuery({
    queryKey: ['admin-refunds', query, statusFilter, page],
    queryFn: () =>
      api.admin.refunds({
        q: query || undefined,
        status: statusFilter || undefined,
        page,
        pageSize: 20,
      }),
  });

  const { data: refundDetail, isLoading: isLoadingDetail } = useQuery({
    queryKey: ['admin-refund-detail', selectedRefundId],
    queryFn: () => api.admin.refundDetail(selectedRefundId!),
    enabled: selectedRefundId !== null,
  });

  const createMutation = useMutation({
    mutationFn: () => {
      const uId = parseInt(createUserId, 10);
      const oId = createOrderId.trim() ? parseInt(createOrderId, 10) : null;
      const cId = createComplaintId.trim() ? parseInt(createComplaintId, 10) : null;
      const cCount = createMethod === 'credit' ? parseInt(createCreditCount, 10) : 0;
      const mAmount = createMethod === 'money' ? parseFloat(createAmount) : null;

      const val = validateRefundRequest({
        userId: uId,
        method: createMethod,
        creditCount: cCount,
        amount: mAmount,
        reason: createReason,
        orderId: oId,
      });

      if (!val.valid) {
        setCreateErrors(val.errors);
        throw new Error('Please resolve form validation issues.');
      }

      return api.admin.requestRefund({
        userId: uId,
        method: createMethod,
        creditCount: cCount,
        amount: mAmount,
        reason: createReason.trim(),
        orderId: oId,
        complaintId: cId,
      });
    },
    onSuccess: () => {
      setIsCreateOpen(false);
      resetCreateForm();
      void queryClient.invalidateQueries({ queryKey: ['admin-refunds'] });
    },
    onError: (err: any) => {
      setApiError(err?.message || 'Could not submit refund request.');
    },
  });

  const approveMutation = useMutation({
    mutationFn: (refundId: number) => api.admin.approveRefund(refundId),
    onSuccess: (updated) => {
      void queryClient.invalidateQueries({ queryKey: ['admin-refunds'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-refund-detail', updated.refundId] });
    },
  });

  const rejectMutation = useMutation({
    mutationFn: (refundId: number) => {
      if (!rejectReason.trim()) {
        setRejectError('Rejection reason is mandatory.');
        throw new Error('Rejection reason is required.');
      }
      return api.admin.rejectRefund(refundId, rejectReason.trim());
    },
    onSuccess: (updated) => {
      setIsRejecting(false);
      setRejectReason('');
      setRejectError(null);
      void queryClient.invalidateQueries({ queryKey: ['admin-refunds'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-refund-detail', updated.refundId] });
    },
    onError: (err: any) => {
      setRejectError(err?.message || 'Could not reject refund.');
    },
  });

  const settleMutation = useMutation({
    mutationFn: (refundId: number) => api.admin.settleRefund(refundId),
    onSuccess: (updated) => {
      void queryClient.invalidateQueries({ queryKey: ['admin-refunds'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-refund-detail', updated.refundId] });
    },
  });

  function resetCreateForm() {
    setCreateUserId('');
    setCreateMethod('credit');
    setCreateCreditCount('5');
    setCreateAmount('100000');
    setCreateOrderId('');
    setCreateComplaintId('');
    setCreateReason('');
    setCreateErrors({});
    setApiError(null);
  }

  async function handleExportCsv() {
    const val = validateCsvExportDates(exportFrom || undefined, exportTo || undefined);
    if (!val.valid) {
      setExportError(val.error || 'Invalid date range.');
      return;
    }

    try {
      setIsExporting(true);
      setExportError(null);
      const { blob, filename } = await api.admin.exportRefundsCsv(
        exportFrom || undefined,
        exportTo || undefined,
      );
      triggerBlobDownload(blob, filename);
      setIsExportOpen(false);
    } catch (err: any) {
      setExportError(err?.message || 'Failed to generate CSV export.');
    } finally {
      setIsExporting(false);
    }
  }

  return (
    <div className="space-y-6">
      {/* Metrics Row */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Requested</p>
              <p className="text-2xl font-semibold font-mono-tabular text-warning mt-1">
                {summary ? summary.requestedCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Awaiting triage decision</p>
            </div>
            <div className="p-2.5 rounded-full bg-warning/10 text-warning">
              <Clock className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Approved</p>
              <p className="text-2xl font-semibold font-mono-tabular text-success mt-1">
                {summary ? summary.approvedCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Approved or settled</p>
            </div>
            <div className="p-2.5 rounded-full bg-success/10 text-success">
              <CheckCircle2 className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Rejected</p>
              <p className="text-2xl font-semibold font-mono-tabular text-danger mt-1">
                {summary ? summary.rejectedCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Refused with explanation</p>
            </div>
            <div className="p-2.5 rounded-full bg-danger/10 text-danger">
              <XCircle className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Credits refunded</p>
              <p className="text-2xl font-semibold font-mono-tabular text-foreground mt-1">
                {summary ? summary.creditsRefundedTotal : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Returned to wallets</p>
            </div>
            <div className="p-2.5 rounded-full bg-accent/10 text-accent">
              <RefreshCw className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>
      </div>

      {/* Action and Filter Toolbar */}
      <Card className="border border-border">
        <Card.Content className="p-4 flex flex-wrap gap-4 items-center justify-between">
          <div className="flex flex-wrap gap-3 items-center flex-1">
            <TextField className="w-64" name="search" aria-label="Search refunds">
              <div className="relative flex items-center">
                <Search className="absolute left-2.5 h-4 w-4 text-muted pointer-events-none" />
                <Input
                  className="pl-9"
                  placeholder="Learner name, email, or order..."
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
                <option value="requested">Requested</option>
                <option value="approved">Approved</option>
                <option value="settled">Settled</option>
                <option value="rejected">Rejected</option>
              </select>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="secondary"
              onPress={() => setIsExportOpen(true)}
            >
              <Download className="h-4 w-4 mr-1.5" />
              Export CSV
            </Button>

            <Button
              size="sm"
              variant="primary"
              onPress={() => {
                resetCreateForm();
                setIsCreateOpen(true);
              }}
            >
              <Plus className="h-4 w-4 mr-1.5" />
              New refund request
            </Button>
          </div>
        </Card.Content>
      </Card>

      {/* Refunds Table */}
      {isLoading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : error ? (
        <Alert status="danger">Could not load refunds. Please try again.</Alert>
      ) : summary?.refunds.length === 0 ? (
        <Card className="border border-border p-8 text-center text-muted">
          No refund requests match the current criteria.
        </Card>
      ) : (
        <div className="space-y-4">
          <Table>
            <Table.ScrollContainer>
              <Table.Content aria-label="Customer refunds desk table">
                <Table.Header>
                  <Table.Column isRowHeader>Refund ID</Table.Column>
                  <Table.Column>Learner</Table.Column>
                  <Table.Column>Method</Table.Column>
                  <Table.Column>Value</Table.Column>
                  <Table.Column>Reason</Table.Column>
                  <Table.Column>Order</Table.Column>
                  <Table.Column>Status</Table.Column>
                  <Table.Column>Requested</Table.Column>
                  <Table.Column>Actions</Table.Column>
                </Table.Header>
                <Table.Body>
                  {summary?.refunds.map((refund) => (
                    <Table.Row key={refund.refundId}>
                      <Table.Cell>
                        <span className="font-mono-tabular font-semibold text-foreground">
                          #{refund.refundId}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <div>
                          <p className="text-sm font-medium text-foreground">{refund.learnerName}</p>
                          <p className="text-xs text-muted font-mono-tabular">{refund.learnerEmail}</p>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs font-medium text-foreground">
                          {formatRefundMethodLabel(refund.method)}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="font-mono-tabular font-medium text-foreground">
                          {refund.method === 'money'
                            ? formatVnd(refund.amount)
                            : `${refund.creditCount} credits`}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <p className="text-xs text-foreground max-w-xs truncate" title={refund.reason}>
                          {refund.reason}
                        </p>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs font-mono-tabular text-muted">
                          {refund.orderCode || '—'}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <Chip size="sm" color={getRefundStatusColor(refund.status)} variant="soft">
                          {formatRefundStatusLabel(refund.status)}
                        </Chip>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs text-muted font-mono-tabular">
                          {formatDateTime(refund.createdAt)}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <Button
                          size="sm"
                          variant="secondary"
                          onPress={() => setSelectedRefundId(refund.refundId)}
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
                {Math.min(summary.page * summary.pageSize, summary.totalCount)} of {summary.totalCount} refunds
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

      {/* Create Refund Request Modal */}
      {isCreateOpen && (
        <Modal.Backdrop
          isOpen={isCreateOpen}
          onOpenChange={(isOpen) => {
            if (!isOpen) setIsCreateOpen(false);
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>Create refund request</Modal.Heading>
                <p className="text-xs text-muted mt-1">
                  Refunds return credits to learner wallet or record cash return by accounting.
                  Orderless ceiling: at most {DEFAULT_ORDERLESS_CEILING} credits / {DEFAULT_ORDERLESS_CEILING} ₫.
                </p>
              </Modal.Header>

              <Modal.Body className="space-y-4">
                {apiError && <Alert status="danger">{apiError}</Alert>}

                <TextField
                  className="w-full"
                  name="userId"
                  isRequired
                  isInvalid={!!createErrors.userId}
                >
                  <Label>Learner user ID</Label>
                  <Input
                    type="number"
                    placeholder="e.g. 12"
                    value={createUserId}
                    onChange={(e) => {
                      setCreateUserId(e.target.value);
                      if (createErrors.userId) {
                        setCreateErrors((prev) => ({ ...prev, userId: '' }));
                      }
                    }}
                  />
                  {createErrors.userId && (
                    <p className="text-xs text-danger mt-1">{createErrors.userId}</p>
                  )}
                </TextField>

                <div>
                  <Label className="text-sm font-medium">Refund method</Label>
                  <div className="flex gap-4 mt-2">
                    <label className="flex items-center gap-2 text-sm cursor-pointer">
                      <input
                        type="radio"
                        name="method"
                        checked={createMethod === 'credit'}
                        onChange={() => setCreateMethod('credit')}
                      />
                      <span>Credits (Wallet balance)</span>
                    </label>
                    <label className="flex items-center gap-2 text-sm cursor-pointer">
                      <input
                        type="radio"
                        name="method"
                        checked={createMethod === 'money'}
                        onChange={() => setCreateMethod('money')}
                      />
                      <span>Money (Cash/Bank transfer)</span>
                    </label>
                  </div>
                </div>

                {createMethod === 'credit' ? (
                  <TextField
                    className="w-full"
                    name="creditCount"
                    isRequired
                    isInvalid={!!createErrors.creditCount}
                  >
                    <Label>Credits to refund</Label>
                    <Input
                      type="number"
                      placeholder="e.g. 5"
                      value={createCreditCount}
                      onChange={(e) => {
                        setCreateCreditCount(e.target.value);
                        if (createErrors.creditCount) {
                          setCreateErrors((prev) => ({ ...prev, creditCount: '' }));
                        }
                      }}
                    />
                    {createErrors.creditCount && (
                      <p className="text-xs text-danger mt-1">{createErrors.creditCount}</p>
                    )}
                  </TextField>
                ) : (
                  <TextField
                    className="w-full"
                    name="amount"
                    isRequired
                    isInvalid={!!createErrors.amount}
                  >
                    <Label>Money amount (VND)</Label>
                    <Input
                      type="number"
                      placeholder="e.g. 150000"
                      value={createAmount}
                      onChange={(e) => {
                        setCreateAmount(e.target.value);
                        if (createErrors.amount) {
                          setCreateErrors((prev) => ({ ...prev, amount: '' }));
                        }
                      }}
                    />
                    {createErrors.amount && (
                      <p className="text-xs text-danger mt-1">{createErrors.amount}</p>
                    )}
                  </TextField>
                )}

                <div className="grid grid-cols-2 gap-3">
                  <TextField className="w-full" name="orderId">
                    <Label>Linked order ID (optional)</Label>
                    <Input
                      type="number"
                      placeholder="e.g. 101"
                      value={createOrderId}
                      onChange={(e) => setCreateOrderId(e.target.value)}
                    />
                  </TextField>

                  <TextField className="w-full" name="complaintId">
                    <Label>Linked complaint ID (optional)</Label>
                    <Input
                      type="number"
                      placeholder="e.g. 42"
                      value={createComplaintId}
                      onChange={(e) => setCreateComplaintId(e.target.value)}
                    />
                  </TextField>
                </div>

                <TextField
                  className="w-full"
                  name="reason"
                  isRequired
                  isInvalid={!!createErrors.reason}
                >
                  <Label>Reason</Label>
                  <TextArea
                    placeholder="Describe why this refund is being issued..."
                    rows={3}
                    value={createReason}
                    onChange={(e) => {
                      setCreateReason(e.target.value);
                      if (createErrors.reason) {
                        setCreateErrors((prev) => ({ ...prev, reason: '' }));
                      }
                    }}
                  />
                  {createErrors.reason && (
                    <p className="text-xs text-danger mt-1">{createErrors.reason}</p>
                  )}
                </TextField>
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setIsCreateOpen(false)}>
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  isDisabled={createMutation.isPending}
                  onPress={() => createMutation.mutate()}
                >
                  {createMutation.isPending ? 'Submitting...' : 'Submit refund request'}
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}

      {/* Refund Detail Modal */}
      {selectedRefundId !== null && (
        <Modal.Backdrop
          isOpen={selectedRefundId !== null}
          onOpenChange={(isOpen) => {
            if (!isOpen) {
              setSelectedRefundId(null);
              setIsRejecting(false);
              setRejectReason('');
              setRejectError(null);
            }
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-2xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>Refund details — #{selectedRefundId}</Modal.Heading>
              </Modal.Header>

              <Modal.Body className="space-y-6">
                {isLoadingDetail ? (
                  <div className="flex justify-center py-8">
                    <Spinner size="md" />
                  </div>
                ) : refundDetail ? (
                  <>
                    <div className="grid grid-cols-2 sm:grid-cols-3 gap-4 p-4 rounded-lg bg-surface border border-border">
                      <div>
                        <p className="text-xs text-muted">Learner</p>
                        <p className="text-sm font-medium text-foreground">
                          {refundDetail.learnerName}
                        </p>
                        <p className="text-xs font-mono-tabular text-muted">
                          {refundDetail.learnerEmail} (ID #{refundDetail.userId})
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Method & value</p>
                        <p className="text-sm font-semibold font-mono-tabular text-foreground">
                          {refundDetail.method === 'money'
                            ? formatVnd(refundDetail.amount)
                            : `${refundDetail.creditCount} credits`}
                        </p>
                        <p className="text-xs text-muted">
                          {formatRefundMethodLabel(refundDetail.method)}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Status</p>
                        <Chip
                          size="sm"
                          color={getRefundStatusColor(refundDetail.status)}
                          variant="soft"
                        >
                          {formatRefundStatusLabel(refundDetail.status)}
                        </Chip>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Linked order</p>
                        <p className="text-sm font-mono-tabular text-foreground">
                          {refundDetail.orderCode
                            ? `${refundDetail.orderCode} (#${refundDetail.orderId})`
                            : 'None (Orderless)'}
                        </p>
                        {refundDetail.orderPaidAmount !== null &&
                          refundDetail.orderPaidAmount !== undefined && (
                            <p className="text-xs text-muted font-mono-tabular">
                              Paid: {formatVnd(refundDetail.orderPaidAmount)}
                            </p>
                          )}
                      </div>

                      <div>
                        <p className="text-xs text-muted">Requested by</p>
                        <p className="text-sm text-foreground">
                          {refundDetail.requestedByUserName || `User #${refundDetail.requestedByUserId}`}
                        </p>
                        <p className="text-xs text-muted font-mono-tabular">
                          {formatDateTime(refundDetail.createdAt)}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Decision</p>
                        <p className="text-sm text-foreground">
                          {refundDetail.decidedByUserName
                            ? `${refundDetail.decidedByUserName}`
                            : 'Pending'}
                        </p>
                        <p className="text-xs text-muted font-mono-tabular">
                          {formatDateTime(refundDetail.decidedAt)}
                        </p>
                      </div>
                    </div>

                    {/* Reason box */}
                    <div className="p-3 rounded border border-border bg-surface-secondary text-sm">
                      <p className="text-xs font-medium text-muted uppercase tracking-wider mb-1">
                        Refund reason
                      </p>
                      <p className="text-foreground leading-relaxed">{refundDetail.reason}</p>
                    </div>

                    {/* Settlement and Ledger details */}
                    {(refundDetail.creditTxnId || refundDetail.settledAt) && (
                      <div className="p-3 rounded border border-success/30 bg-success/5 text-xs space-y-1">
                        {refundDetail.creditTxnId && (
                          <p className="text-success font-medium">
                            Credit ledger transaction #{refundDetail.creditTxnId} ({refundDetail.grantedCredits} credits granted)
                          </p>
                        )}
                        {refundDetail.settledAt && (
                          <p className="text-success font-medium">
                            Settled by bank transfer on {formatDateTime(refundDetail.settledAt)} by{' '}
                            {refundDetail.settledByUserName || `User #${refundDetail.settledByUserId}`}
                          </p>
                        )}
                      </div>
                    )}

                    {/* Actions if Requested */}
                    {refundDetail.status === 'requested' && !isRejecting && (
                      <div className="flex items-center justify-between p-4 rounded-md bg-warning/10 border border-warning/30">
                        <div className="text-xs text-warning">
                          <p className="font-semibold">Review requested refund</p>
                          <p className="mt-0.5">
                            Approving immediately grants credits to wallet or queues cash for settlement.
                          </p>
                        </div>
                        <div className="flex gap-2">
                          <Button
                            size="sm"
                            variant="danger"
                            onPress={() => setIsRejecting(true)}
                          >
                            Reject refund
                          </Button>
                          <Button
                            size="sm"
                            variant="primary"
                            isDisabled={approveMutation.isPending}
                            onPress={() => approveMutation.mutate(refundDetail.refundId)}
                          >
                            {approveMutation.isPending ? 'Approving...' : 'Approve refund'}
                          </Button>
                        </div>
                      </div>
                    )}

                    {/* Reject reason prompt */}
                    {isRejecting && (
                      <div className="p-4 rounded-md border border-danger/30 bg-danger/5 space-y-3">
                        <h4 className="text-sm font-semibold text-danger">
                          Provide rejection reason
                        </h4>
                        <p className="text-xs text-muted">
                          The reason explains to the learner and accounting why this refund was refused.
                        </p>
                        {rejectError && <Alert status="danger">{rejectError}</Alert>}
                        <TextField className="w-full" name="rejectionReason">
                          <Label>Rejection reason</Label>
                          <TextArea
                            rows={3}
                            placeholder="State the justification for refusing this refund..."
                            value={rejectReason}
                            onChange={(e) => setRejectReason(e.target.value)}
                          />
                        </TextField>
                        <div className="flex justify-end gap-2">
                          <Button
                            size="sm"
                            variant="secondary"
                            onPress={() => {
                              setIsRejecting(false);
                              setRejectError(null);
                            }}
                          >
                            Back
                          </Button>
                          <Button
                            size="sm"
                            variant="danger"
                            isDisabled={rejectMutation.isPending}
                            onPress={() => rejectMutation.mutate(refundDetail.refundId)}
                          >
                            {rejectMutation.isPending ? 'Rejecting...' : 'Confirm rejection'}
                          </Button>
                        </div>
                      </div>
                    )}

                    {/* Settlement Button for Approved Money Refunds */}
                    {refundDetail.status === 'approved' && refundDetail.method === 'money' && (
                      <div className="flex items-center justify-between p-4 rounded-md bg-success/10 border border-success/30">
                        <div className="text-xs text-success">
                          <p className="font-semibold">Accountant settlement</p>
                          <p className="mt-0.5">
                            Bank transfer has been approved. Record settlement date once payment is processed.
                          </p>
                        </div>
                        <Button
                          size="sm"
                          variant="primary"
                          isDisabled={settleMutation.isPending}
                          onPress={() => settleMutation.mutate(refundDetail.refundId)}
                        >
                          {settleMutation.isPending ? 'Recording...' : 'Record bank settlement'}
                        </Button>
                      </div>
                    )}
                  </>
                ) : null}
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setSelectedRefundId(null)}>
                  Close
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}

      {/* Export CSV Modal */}
      {isExportOpen && (
        <Modal.Backdrop
          isOpen={isExportOpen}
          onOpenChange={(isOpen) => {
            if (!isOpen) {
              setIsExportOpen(false);
              setExportError(null);
            }
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-md">
              <Modal.CloseTrigger />
              <Modal.Header>
                <div className="flex items-center gap-2">
                  <FileSpreadsheet className="h-5 w-5 text-accent" />
                  <Modal.Heading>Export accounting CSV</Modal.Heading>
                </div>
                <p className="text-xs text-muted mt-1">
                  Downloads approved money refunds as UTF-8 BOM CSV. Vietnamese names and diacritics
                  are preserved for Excel and accounting spreadsheets.
                </p>
              </Modal.Header>

              <Modal.Body className="space-y-4">
                {exportError && <Alert status="danger">{exportError}</Alert>}

                <TextField className="w-full" name="from">
                  <Label>From date (YYYY-MM-DD)</Label>
                  <Input
                    type="date"
                    value={exportFrom}
                    onChange={(e) => setExportFrom(e.target.value)}
                  />
                </TextField>

                <TextField className="w-full" name="to">
                  <Label>To date (YYYY-MM-DD)</Label>
                  <Input
                    type="date"
                    value={exportTo}
                    onChange={(e) => setExportTo(e.target.value)}
                  />
                </TextField>
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setIsExportOpen(false)}>
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  isDisabled={isExporting}
                  onPress={handleExportCsv}
                >
                  <ArrowDownToLine className="h-4 w-4 mr-1.5" />
                  {isExporting ? 'Exporting...' : 'Download CSV'}
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}
    </div>
  );
}
