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
  TextField,
} from '@heroui/react';
import {
  AlertCircle,
  ArrowRight,
  CheckCircle2,
  Clock,
  Filter,
  RefreshCw,
  Search,
  ShieldAlert,
} from 'lucide-react';
import { api, type AdminOrderDto, type AdminOrderDetailDto } from '../../../lib/api';
import { formatVnd } from '../../../lib/security';
import {
  formatDateTime,
  formatGatewayLabel,
  formatOrderStatusLabel,
  formatPaymentEventReportedStatusLabel,
  formatPaymentEventSourceLabel,
  getOrderStatusColor,
} from './opsHelpers';

export function OrdersTab() {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [gatewayFilter, setGatewayFilter] = useState('');
  const [page, setPage] = useState(1);
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null);
  const [reconcileError, setReconcileError] = useState<string | null>(null);

  const { data: summary, isLoading, error } = useQuery({
    queryKey: ['admin-orders', query, statusFilter, gatewayFilter, page],
    queryFn: () =>
      api.admin.orders({
        q: query || undefined,
        status: statusFilter || undefined,
        gateway: gatewayFilter || undefined,
        page,
        pageSize: 20,
      }),
  });

  const { data: orderDetail, isLoading: isLoadingDetail } = useQuery({
    queryKey: ['admin-order-detail', selectedOrderId],
    queryFn: () => api.admin.orderDetail(selectedOrderId!),
    enabled: selectedOrderId !== null,
  });

  const reconcileMutation = useMutation({
    mutationFn: (orderId: number) => api.admin.reconcileOrder(orderId),
    onSuccess: (updatedOrder) => {
      setReconcileError(null);
      void queryClient.invalidateQueries({ queryKey: ['admin-orders'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-order-detail', updatedOrder.orderId] });
    },
    onError: (err: any) => {
      setReconcileError(err?.message || 'Could not reconcile order with gateway.');
    },
  });

  return (
    <div className="space-y-6">
      {/* Metric Tiles */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Paid this month</p>
              <p className="text-2xl font-semibold font-mono-tabular text-foreground mt-1">
                {summary ? formatVnd(summary.paidThisMonthAmount) : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">
                {summary ? `${summary.paidThisMonthCount} orders completed` : ''}
              </p>
            </div>
            <div className="p-2.5 rounded-full bg-success/10 text-success">
              <CheckCircle2 className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Confirming with gateway</p>
              <p className="text-2xl font-semibold font-mono-tabular text-warning mt-1">
                {summary ? summary.reconcilingCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Awaiting webhook settlement</p>
            </div>
            <div className="p-2.5 rounded-full bg-warning/10 text-warning">
              <Clock className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Failed or expired</p>
              <p className="text-2xl font-semibold font-mono-tabular text-danger mt-1">
                {summary ? summary.failedOrExpiredCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Unsettled order intents</p>
            </div>
            <div className="p-2.5 rounded-full bg-danger/10 text-danger">
              <AlertCircle className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>
      </div>

      {/* Filter Toolbar */}
      <Card className="border border-border">
        <Card.Content className="p-4 flex flex-wrap gap-4 items-center justify-between">
          <div className="flex flex-wrap gap-3 items-center flex-1">
            <TextField className="w-64" name="search" aria-label="Search orders">
              <div className="relative flex items-center">
                <Search className="absolute left-2.5 h-4 w-4 text-muted pointer-events-none" />
                <Input
                  className="pl-9"
                  placeholder="Order code or email..."
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
                <option value="pending">Pending</option>
                <option value="reconciling">Confirming with the gateway</option>
                <option value="paid">Paid</option>
                <option value="failed">Failed</option>
                <option value="expired">Expired</option>
              </select>
            </div>

            <div className="flex items-center gap-2">
              <Label className="text-xs text-muted whitespace-nowrap">Gateway:</Label>
              <select
                aria-label="Filter gateway"
                className="h-9 px-3 rounded-md border border-border bg-background text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-focus"
                value={gatewayFilter}
                onChange={(e) => {
                  setGatewayFilter(e.target.value);
                  setPage(1);
                }}
              >
                <option value="">All gateways</option>
                <option value="payos">payOS</option>
                <option value="mock">Mock gateway</option>
              </select>
            </div>
          </div>

          <Button
            size="sm"
            variant="secondary"
            onPress={() => void queryClient.invalidateQueries({ queryKey: ['admin-orders'] })}
          >
            <RefreshCw className="h-4 w-4 mr-1.5" />
            Refresh
          </Button>
        </Card.Content>
      </Card>

      {/* Orders Table */}
      {isLoading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : error ? (
        <Alert status="danger">Could not load orders. Please try again.</Alert>
      ) : summary?.orders.length === 0 ? (
        <Card className="border border-border p-8 text-center text-muted">
          No orders match the current filter criteria.
        </Card>
      ) : (
        <div className="space-y-4">
          <Table>
            <Table.ScrollContainer>
              <Table.Content aria-label="Customer orders table">
                <Table.Header>
                  <Table.Column isRowHeader>Order code</Table.Column>
                  <Table.Column>Learner</Table.Column>
                  <Table.Column>Package</Table.Column>
                  <Table.Column>Amount</Table.Column>
                  <Table.Column>Status</Table.Column>
                  <Table.Column>Gateway</Table.Column>
                  <Table.Column>Created</Table.Column>
                  <Table.Column>Actions</Table.Column>
                </Table.Header>
                <Table.Body>
                  {summary?.orders.map((order) => (
                    <Table.Row key={order.orderId}>
                      <Table.Cell>
                        <span className="font-mono-tabular font-semibold text-foreground">
                          {order.orderCode}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <div>
                          <p className="text-sm font-medium text-foreground">
                            {order.userFullName || '—'}
                          </p>
                          <p className="text-xs text-muted font-mono-tabular">{order.userEmail}</p>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <p className="text-sm font-semibold font-mono-tabular text-foreground">
                          {order.credits} credits
                        </p>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="font-mono-tabular font-medium text-foreground">
                          {formatVnd(order.amount)}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <Chip size="sm" color={getOrderStatusColor(order.status)} variant="soft">
                          {formatOrderStatusLabel(order.status)}
                        </Chip>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs text-muted">
                          {formatGatewayLabel(order.gateway)}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs text-muted font-mono-tabular">
                          {formatDateTime(order.createdAt)}
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <div className="flex items-center gap-2">
                          <Button
                            size="sm"
                            variant="secondary"
                            onPress={() => setSelectedOrderId(order.orderId)}
                          >
                            View
                          </Button>
                          {(order.status === 'pending' || order.status === 'reconciling') && (
                            <Button
                              size="sm"
                              variant="primary"
                              isDisabled={reconcileMutation.isPending}
                              onPress={() => reconcileMutation.mutate(order.orderId)}
                            >
                              Reconcile
                            </Button>
                          )}
                        </div>
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
                {Math.min(summary.page * summary.pageSize, summary.totalCount)} of {summary.totalCount} orders
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

      {/* Order Detail Modal */}
      {selectedOrderId !== null && (
        <Modal.Backdrop
          isOpen={selectedOrderId !== null}
          onOpenChange={(isOpen) => {
            if (!isOpen) {
              setSelectedOrderId(null);
              setReconcileError(null);
            }
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-2xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>
                  Order details — {orderDetail?.order.orderCode || `#${selectedOrderId}`}
                </Modal.Heading>
              </Modal.Header>

              <Modal.Body className="space-y-6">
                {isLoadingDetail ? (
                  <div className="flex justify-center py-8">
                    <Spinner size="md" />
                  </div>
                ) : orderDetail ? (
                  <>
                    {reconcileError && (
                      <Alert status="danger">{reconcileError}</Alert>
                    )}

                    {/* Order Metadata Grid */}
                    <div className="grid grid-cols-2 sm:grid-cols-3 gap-4 p-4 rounded-lg bg-surface border border-border">
                      <div>
                        <p className="text-xs text-muted">Learner</p>
                        <p className="text-sm font-medium text-foreground">
                          {orderDetail.order.userFullName || '—'}
                        </p>
                        <p className="text-xs font-mono-tabular text-muted">
                          {orderDetail.order.userEmail}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Amount & credits</p>
                        <p className="text-sm font-semibold font-mono-tabular text-foreground">
                          {formatVnd(orderDetail.order.amount)}
                        </p>
                        <p className="text-xs text-muted font-mono-tabular">
                          {orderDetail.order.credits} credits
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Status</p>
                        <Chip
                          size="sm"
                          color={getOrderStatusColor(orderDetail.order.status)}
                          variant="soft"
                        >
                          {formatOrderStatusLabel(orderDetail.order.status)}
                        </Chip>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Gateway</p>
                        <p className="text-sm text-foreground">
                          {formatGatewayLabel(orderDetail.order.gateway)}
                        </p>
                        {orderDetail.order.gatewayTxnRef && (
                          <p className="text-xs font-mono-tabular text-muted truncate">
                            Ref: {orderDetail.order.gatewayTxnRef}
                          </p>
                        )}
                      </div>

                      <div>
                        <p className="text-xs text-muted">Created at</p>
                        <p className="text-xs font-mono-tabular text-foreground">
                          {formatDateTime(orderDetail.order.createdAt)}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs text-muted">Paid at</p>
                        <p className="text-xs font-mono-tabular text-foreground">
                          {formatDateTime(orderDetail.order.paidAt)}
                        </p>
                      </div>
                    </div>

                    {/* Reconcile Button if pending or reconciling */}
                    {(orderDetail.order.status === 'pending' ||
                      orderDetail.order.status === 'reconciling') && (
                      <div className="flex items-center justify-between p-3 rounded-md bg-warning/10 border border-warning/30">
                        <div className="flex items-center gap-2 text-warning">
                          <AlertCircle className="h-4 w-4 shrink-0" />
                          <span className="text-xs">
                            Order has not finalized yet. You can force-check status with the gateway.
                          </span>
                        </div>
                        <Button
                          size="sm"
                          variant="primary"
                          isDisabled={reconcileMutation.isPending}
                          onPress={() => reconcileMutation.mutate(orderDetail.order.orderId)}
                        >
                          {reconcileMutation.isPending ? 'Checking...' : 'Reconcile with gateway'}
                        </Button>
                      </div>
                    )}

                    {/* Payment Events Timeline */}
                    <div>
                      <h4 className="text-sm font-semibold text-foreground mb-3">
                        Payment events audit log
                      </h4>
                      {orderDetail.events.length === 0 ? (
                        <p className="text-xs text-muted italic">No payment events recorded yet.</p>
                      ) : (
                        <div className="space-y-3">
                          {orderDetail.events.map((evt) => (
                            <div
                              key={evt.eventId}
                              className="p-3 rounded border border-border text-xs space-y-1"
                            >
                              <div className="flex items-center justify-between">
                                <span className="font-semibold text-foreground">
                                  {formatPaymentEventSourceLabel(evt.source)}
                                </span>
                                <span className="font-mono-tabular text-muted">
                                  {formatDateTime(evt.createdAt)}
                                </span>
                              </div>

                              <div className="flex flex-wrap gap-2 items-center text-muted">
                                <span>Reported: {formatPaymentEventReportedStatusLabel(evt.reportedStatus)}</span>
                                <span>•</span>
                                <Chip
                                  size="sm"
                                  color={evt.signatureValid ? 'success' : 'danger'}
                                  variant="soft"
                                >
                                  {evt.signatureValid ? 'Signature valid' : 'Invalid signature'}
                                </Chip>
                                <Chip
                                  size="sm"
                                  color={evt.wasApplied ? 'accent' : 'default'}
                                  variant="soft"
                                >
                                  {evt.wasApplied ? 'Applied' : 'Ignored'}
                                </Chip>
                              </div>

                              {evt.note && (
                                <p className="text-muted pt-1 border-t border-border/50">
                                  Note: {evt.note}
                                </p>
                              )}
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </>
                ) : null}
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setSelectedOrderId(null)}>
                  Close
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}
    </div>
  );
}
