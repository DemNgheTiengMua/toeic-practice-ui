import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Description,
  EmptyState,
  Input,
  Label,
  ListBox,
  Modal,
  SearchField,
  Select,
  Spinner,
  Table,
  TextArea,
  TextField,
} from '@heroui/react';
import {
  ArrowDownToLine,
  CheckCircle2,
  Clock,
  Download,
  FileSpreadsheet,
  Plus,
  RefreshCw,
  User,
  XCircle,
} from 'lucide-react';
import { OpsPagination } from './OpsPagination';
import {
  api,
  type AdminOrderDto,
  type AdminRefundListItemDto,
  type AdminRefundDetailDto,
  type AdminUserDto,
  type RefundMethodDto,
} from '../../../lib/api';
import { formatVnd } from '../../../lib/security';
import {
  formatDateOnly,
  formatDateTime,
  formatRefundMethodLabel,
  formatRefundStatusLabel,
  getRefundStatusColor,
  triggerBlobDownload,
  validateCsvExportDates,
  validateRefundRequest,
  DEFAULT_ORDERLESS_CEILING,
  MAX_REFUND_REASON_LENGTH,
} from './opsHelpers';

export interface RefundsTabProps {
  /** #350 carries this in from the complaint screen. It is never typed: a refund created from a
   * complaint must point at that complaint, and a hand-typed identifier is how the two disagree. */
  complaintId?: number | null;
  /** #350: the learner the complaint belongs to, so the desk opens on the right person. Also never
   * typed - the complaint already knows who it is about. */
  learnerId?: number | null;
  /** #345: the subject's order, when the complaint has one. The desk picks it from the learner's paid
   * orders and pre-fills the amount from it, so no figure is transcribed from another screen. */
  subjectOrderId?: number | null;
  /** #345: the credits the subject cost, when it is an attempt or a booking. */
  subjectCreditCount?: number | null;
}

export function RefundsTab({
  complaintId = null,
  learnerId = null,
  subjectOrderId = null,
  subjectCreditCount = null,
}: RefundsTabProps) {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);

  // Modals state
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [isExportOpen, setIsExportOpen] = useState(false);
  const [selectedRefundId, setSelectedRefundId] = useState<number | null>(null);

  // Create form state
  const [learnerSearch, setLearnerSearch] = useState('');
  const [selectedLearner, setSelectedLearner] = useState<AdminUserDto | null>(null);
  const [selectedOrder, setSelectedOrder] = useState<AdminOrderDto | null>(null);
  const [createMethod, setCreateMethod] = useState<RefundMethodDto>('credit');
  const [createCreditCount, setCreateCreditCount] = useState('5');
  const [createAmount, setCreateAmount] = useState('100000');
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

  // #349: the learner picker searches the admin user list the Users screen already pages through,
  // rather than asking a staff member for a number they have no way to discover.
  // #345: it reads the desk's own lookup, not `/api/admin/users`, which is gated on system governance
  // and would 403 a support-only agent - the persona this screen exists for.
  // shortcut: one request per keystroke, no debounce — the query is indexed and the box is a modal;
  // add a debounce if a large user table ever makes this lag.
  const { data: learnerMatches } = useQuery({
    queryKey: ['admin-refund-learners', learnerSearch],
    queryFn: () => api.admin.refundLearners({ q: learnerSearch.trim(), pageSize: 20 }),
    enabled: isCreateOpen && learnerSearch.trim().length > 0,
  });

  // #349: the order list is that learner's paid orders. Paid only, because a refund against an unpaid
  // order would return money that never arrived.
  const { data: learnerOrders } = useQuery({
    queryKey: ['admin-refund-orders', selectedLearner?.userId],
    queryFn: () => api.admin.refundLearnerOrders(selectedLearner!.userId, { pageSize: 50 }),
    enabled: isCreateOpen && selectedLearner !== null,
  });

  const learners = learnerMatches?.users ?? [];

  // #350: arriving from a complaint, the learner is already known. The desk opens the create form with
  // that learner selected, so nothing is retyped and the wrong person cannot be picked by mistake.
  const { data: carriedLearner } = useQuery({
    queryKey: ['admin-refund-carried-learner', learnerId],
    queryFn: () => api.admin.refundLearner(learnerId!),
    enabled: learnerId !== null,
  });

  React.useEffect(() => {
    if (learnerId === null || selectedLearner !== null) return;
    if (!carriedLearner) return;
    setSelectedLearner(carriedLearner);
    setLearnerSearch(carriedLearner.email);
    setIsCreateOpen(true);
  }, [learnerId, carriedLearner, selectedLearner]);

  // #345: once that learner's paid orders have arrived, the complaint's subject order is picked and its
  // amount is filled by the same path a manual pick uses. A subject order that is not among them - an
  // unpaid one, or one belonging to somebody else - is refused rather than selected: the desk must not
  // pre-fill a refund against an order the API would reject.
  React.useEffect(() => {
    if (subjectOrderId === null || selectedOrder !== null) return;
    const carried = learnerOrders?.orders.find((o) => o.orderId === subjectOrderId);
    if (carried) pickOrder(carried);
  }, [subjectOrderId, learnerOrders, selectedOrder]);

  // #345: a credit subject hands over the count it cost, so the desk opens on the loss rather than on
  // the placeholder five.
  React.useEffect(() => {
    if (subjectCreditCount === null || subjectOrderId !== null) return;
    setCreateCreditCount(String(subjectCreditCount));
  }, [subjectCreditCount, subjectOrderId]);

  const createMutation = useMutation({
    mutationFn: () => {
      const uId = selectedLearner?.userId ?? 0;
      const oId = selectedOrder?.orderId ?? null;
      // A money refund cannot carry a credit count: the method owns the value column, and the API
      // refuses the other one rather than silently dropping it.
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
        // The identifier is never typed, so "pick a learner" is the message; the userId key exists
        // only because the shared validator speaks in terms of the payload.
        const { userId: _hidden, ...rest } = val.errors;
        setCreateErrors(selectedLearner ? rest : { learner: 'Select a learner.', ...rest });
        throw new Error('Please resolve form validation issues.');
      }

      return api.admin.requestRefund({
        userId: uId,
        method: createMethod,
        creditCount: cCount,
        amount: mAmount,
        reason: createReason.trim(),
        orderId: oId,
        complaintId,
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
    setLearnerSearch('');
    setSelectedLearner(null);
    setSelectedOrder(null);
    setCreateMethod('credit');
    setCreateCreditCount('5');
    setCreateAmount('100000');
    setCreateReason('');
    setCreateErrors({});
    setApiError(null);
  }

  /** #349: picking an order fills both value columns from the order and locks the method to money.
   * An order is money that arrived, so returning credits for it would return a currency the learner
   * never paid in. */
  function pickOrder(order: AdminOrderDto) {
    setSelectedOrder(order);
    setCreateMethod('money');
    setCreateAmount(String(order.amount));
    setCreateCreditCount(String(order.credits));
    setCreateErrors((prev) => ({ ...prev, amount: '', creditCount: '', method: '' }));
  }

  function pickLearner(learner: AdminUserDto) {
    setSelectedLearner(learner);
    // A different learner's orders are not this learner's orders, and a refund against the wrong one
    // is a paid insult to a stranger.
    setSelectedOrder(null);
    setCreateErrors((prev) => ({ ...prev, userId: '', learner: '' }));
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
            <SearchField
              className="w-64"
              name="search"
              aria-label="Search refunds"
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
                <SearchField.Input placeholder="Learner name, email, or order..." />
                <SearchField.ClearButton />
              </SearchField.Group>
            </SearchField>

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
                  <ListBox.Item id="requested" textValue="Requested">
                    Requested
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                  <ListBox.Item id="approved" textValue="Approved">
                    Approved
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                  <ListBox.Item id="settled" textValue="Settled">
                    Settled
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                  <ListBox.Item id="rejected" textValue="Rejected">
                    Rejected
                    <ListBox.ItemIndicator />
                  </ListBox.Item>
                </ListBox>
              </Select.Popover>
            </Select>
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
          {summary && (
            <OpsPagination
              page={summary.page}
              pageSize={summary.pageSize}
              totalCount={summary.totalCount}
              itemLabel="refunds"
              onPageChange={setPage}
            />
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

                {complaintId !== null && (
                  <Alert status="accent">
                    Carried from complaint #{complaintId}. The refund will be recorded against it.
                  </Alert>
                )}

                {/* #349: the learner is searched and picked, never typed as an internal identifier. */}
                <SearchField
                  className="w-full"
                  name="learnerSearch"
                  aria-label="Search learners by name or email"
                  value={learnerSearch}
                  onChange={setLearnerSearch}
                  onClear={() => setLearnerSearch('')}
                >
                  <Label>Learner</Label>
                  <SearchField.Group>
                    <SearchField.SearchIcon />
                    <SearchField.Input placeholder="Search by name or email…" />
                    <SearchField.ClearButton />
                  </SearchField.Group>
                </SearchField>

                {learnerSearch.trim().length > 0 && (
                  <ListBox
                    aria-label="Learner matches"
                    selectionMode="single"
                    selectedKeys={selectedLearner ? [String(selectedLearner.userId)] : []}
                    onSelectionChange={(keys) => {
                      const key = [...keys][0];
                      if (key == null) return;
                      const found = learners.find((l) => String(l.userId) === String(key));
                      if (found) pickLearner(found);
                    }}
                    renderEmptyState={() => (
                      <EmptyState>No learner matches that name or email.</EmptyState>
                    )}
                  >
                    {learners.map((learner) => (
                      <ListBox.Item
                        key={learner.userId}
                        id={String(learner.userId)}
                        textValue={`${learner.fullName} ${learner.email}`}
                      >
                        <div>
                          <p className="text-sm font-medium text-foreground">{learner.fullName}</p>
                          <p className="text-xs text-muted font-mono-tabular">{learner.email}</p>
                        </div>
                        <ListBox.ItemIndicator />
                      </ListBox.Item>
                    ))}
                  </ListBox>
                )}

                {createErrors.learner && (
                  <p className="text-xs text-danger">{createErrors.learner}</p>
                )}

                {selectedLearner && (
                  <div
                    role="group"
                    aria-label="Selected learner"
                    className="p-3 rounded bg-surface border border-border flex items-start gap-3"
                  >
                    <User className="h-4 w-4 text-accent mt-0.5" />
                    <div>
                      <p className="text-sm font-medium text-foreground">
                        {selectedLearner.fullName}
                      </p>
                      <p className="text-xs text-muted font-mono-tabular">
                        {selectedLearner.email}
                      </p>
                      <p className="text-xs text-foreground mt-1">
                        Current balance:{' '}
                        <span className="font-mono-tabular font-semibold">
                          {selectedLearner.creditsLeft}
                        </span>{' '}
                        credits
                      </p>
                    </div>
                  </div>
                )}

                {/* #349: the order is picked from that learner's paid orders. */}
                <Select
                  aria-label="Paid order"
                  className="w-full"
                  placeholder={
                    selectedLearner
                      ? learnerOrders?.orders.length === 0
                        ? 'This learner has no paid order'
                        : 'Pick a paid order'
                      : 'Pick a learner first'
                  }
                  isDisabled={!selectedLearner || (learnerOrders?.orders.length ?? 0) === 0}
                  value={selectedOrder ? String(selectedOrder.orderId) : null}
                  onChange={(key) => {
                    if (key == null) {
                      setSelectedOrder(null);
                      return;
                    }
                    const found = learnerOrders?.orders.find(
                      (o) => String(o.orderId) === String(key),
                    );
                    if (found) pickOrder(found);
                  }}
                >
                  <Label>Paid order</Label>
                  <Select.Trigger>
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      {(learnerOrders?.orders ?? []).map((order) => (
                        <ListBox.Item
                          key={order.orderId}
                          id={String(order.orderId)}
                          textValue={`${order.orderCode} ${order.paidAt ?? order.createdAt} ${order.credits} credits ${order.amount}`}
                        >
                          <div>
                            <p className="text-sm font-mono-tabular font-medium text-foreground">
                              {order.orderCode}
                            </p>
                            <p className="text-xs text-muted">
                              {formatDateOnly(order.paidAt ?? order.createdAt)} · {order.credits}{' '}
                              credits · {formatVnd(order.amount)}
                            </p>
                          </div>
                          <ListBox.ItemIndicator />
                        </ListBox.Item>
                      ))}
                    </ListBox>
                  </Select.Popover>
                  <Description>
                    Picking an order fills both values from what was paid and locks the method to
                    money.
                  </Description>
                </Select>

                <div>
                  <Label className="text-sm font-medium">Refund method</Label>
                  <div className="flex gap-4 mt-2">
                    <label className="flex items-center gap-2 text-sm cursor-pointer">
                      <input
                        type="radio"
                        name="method"
                        checked={createMethod === 'credit'}
                        disabled={selectedOrder !== null}
                        onChange={() => setCreateMethod('credit')}
                      />
                      <span>Credits (Wallet balance)</span>
                    </label>
                    <label className="flex items-center gap-2 text-sm cursor-pointer">
                      <input
                        type="radio"
                        name="method"
                        checked={createMethod === 'money'}
                        disabled={selectedOrder !== null}
                        onChange={() => setCreateMethod('money')}
                      />
                      <span>Money (Cash/Bank transfer)</span>
                    </label>
                  </div>
                  {selectedOrder && (
                    <p className="text-xs text-muted mt-1">
                      Locked to money: an order is money that arrived, so a refund against it returns
                      money.
                    </p>
                  )}
                </div>

                {selectedOrder ? (
                  <div className="grid grid-cols-2 gap-3">
                    <TextField className="w-full" name="amount" isRequired isInvalid={!!createErrors.amount}>
                      <Label>Money amount (VND)</Label>
                      <Input
                        type="number"
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

                    <TextField className="w-full" name="creditCount">
                      <Label>Credits to refund</Label>
                      <Input type="number" value={createCreditCount} readOnly />
                      <Description>
                        Filled from the order. A money refund carries the amount, not this count.
                      </Description>
                    </TextField>
                  </div>
                ) : createMethod === 'credit' ? (
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
                    maxLength={MAX_REFUND_REASON_LENGTH}
                    value={createReason}
                    onChange={(e) => {
                      setCreateReason(e.target.value);
                      if (createErrors.reason) {
                        setCreateErrors((prev) => ({ ...prev, reason: '' }));
                      }
                    }}
                  />
                  <Description>
                    {createReason.trim().length} / {MAX_REFUND_REASON_LENGTH} characters, the same
                    limit the API accepts.
                  </Description>
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
