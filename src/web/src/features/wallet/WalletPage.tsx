import React, { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
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
  ArrowRight,
  Check,
  CreditCard,
  History,
  Package,
  Receipt,
  Sparkles,
  Wallet,
} from 'lucide-react';
import { api } from '../../lib/api';
import { formatVnd, formatSignedVnd, formatVndUnitPrice } from '../../lib/security';
import {
  formatCreditReason,
  formatDateTime,
  formatGatewayLabel,
  formatOrderStatus,
} from '../../lib/domain-labels';

export function WalletPage() {
  const [activeTab, setActiveTab] = useState<'packages' | 'ledger' | 'orders'>('packages');

  const walletQuery = useQuery({
    queryKey: ['finance', 'wallet'],
    queryFn: () => api.finance.wallet(),
    staleTime: 15_000,
    retry: false,
  });

  const packagesQuery = useQuery({
    queryKey: ['catalog', 'packages'],
    queryFn: () => api.catalog.packages(),
    staleTime: 60_000,
    retry: false,
  });

  if (walletQuery.isLoading) {
    return (
      <div className="max-w-6xl mx-auto py-8 px-4 sm:px-6">
        <Card>
          <Card.Content className="flex items-center gap-3 py-10 justify-center">
            <Spinner size="sm" />
            <span className="text-sm text-muted">Loading your wallet & billing data...</span>
          </Card.Content>
        </Card>
      </div>
    );
  }

  const wallet = walletQuery.data;

  return (
    <div className="max-w-6xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-8">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <Wallet className="w-5 h-5 text-accent" />
            <span className="text-xs font-semibold uppercase tracking-wider text-muted">
              Finance & Billing
            </span>
          </div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            Wallet & billing
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            Manage your credits, view purchase history, and inspect your append-only ledger.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <Link to="/pricing" className="no-underline">
            <Button variant="secondary" size="sm">
              <Package className="w-4 h-4 mr-1.5" />
              Pricing plans
            </Button>
          </Link>
          <Button
            variant="primary"
            size="sm"
            onPress={() => setActiveTab('packages')}
          >
            <Sparkles className="w-4 h-4 mr-1.5" />
            Buy credits
          </Button>
        </div>
      </div>

      {/* Balance & Statistics Overview */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="border-l-4 border-l-accent shadow-surface">
          <Card.Content className="py-4 px-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-muted">Credit balance</span>
              <Sparkles className="w-4 h-4 text-warning" />
            </div>
            <div className="mt-2 flex items-baseline gap-2">
              <span className="font-mono-tabular text-3xl font-bold text-foreground">
                {wallet ? wallet.balance : 0}
              </span>
              <span className="text-xs text-muted">available credits</span>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-surface">
          <Card.Content className="py-4 px-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-muted">Total purchased</span>
              <CreditCard className="w-4 h-4 text-muted" />
            </div>
            <div className="mt-2 flex items-baseline gap-2">
              <span className="font-mono-tabular text-2xl font-bold text-foreground">
                {wallet ? wallet.creditsPurchased : 0}
              </span>
              <span className="text-xs text-muted">credits</span>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-surface">
          <Card.Content className="py-4 px-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-muted">Total spent</span>
              <Receipt className="w-4 h-4 text-muted" />
            </div>
            <div className="mt-2 flex items-baseline gap-2">
              <span className="font-mono-tabular text-2xl font-bold text-foreground">
                {wallet ? formatVnd(wallet.amountPurchased) : formatVnd(0)}
              </span>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-surface">
          <Card.Content className="py-4 px-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-muted">Paid orders</span>
              <History className="w-4 h-4 text-muted" />
            </div>
            <div className="mt-2 flex items-baseline gap-2">
              <span className="font-mono-tabular text-2xl font-bold text-foreground">
                {wallet ? wallet.paidOrderCount : 0}
              </span>
              <span className="text-xs text-muted">receipts</span>
            </div>
          </Card.Content>
        </Card>
      </div>

      {/* Main Tabs Workspace */}
      <Tabs
        selectedKey={activeTab}
        onSelectionChange={(key) => setActiveTab(key as 'packages' | 'ledger' | 'orders')}
        className="w-full"
      >
        <Tabs.ListContainer>
          <Tabs.List aria-label="Wallet workspace tabs">
            <Tabs.Tab id="packages">
              Credit packages
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="ledger">
              Credit history
              <Tabs.Indicator />
            </Tabs.Tab>
            <Tabs.Tab id="orders">
              Orders & receipts
              <Tabs.Indicator />
            </Tabs.Tab>
          </Tabs.List>
        </Tabs.ListContainer>

        {/* Tab 1: Credit packages */}
        <Tabs.Panel id="packages" className="pt-6">
          {packagesQuery.isLoading ? (
            <div className="flex items-center justify-center py-12 gap-3 text-muted text-sm">
              <Spinner size="sm" />
              <span>Loading packages...</span>
            </div>
          ) : packagesQuery.isError ? (
            <Alert status="danger">
              <Alert.Indicator>
                <AlertCircle className="w-4 h-4" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Unable to load packages</Alert.Title>
                <Alert.Description>
                  Failed to fetch active credit packages. Please refresh the page to retry.
                </Alert.Description>
              </Alert.Content>
            </Alert>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
              {(packagesQuery.data ?? []).map((pkg) => (
                <Card
                  key={pkg.packageId}
                  className={`flex flex-col justify-between shadow-surface ${
                    pkg.isFeatured ? 'border-2 border-accent relative' : ''
                  }`}
                >
                  {pkg.isFeatured && (
                    <div className="absolute -top-3 right-4">
                      <Chip size="sm" color="accent" variant="primary">
                        Popular choice
                      </Chip>
                    </div>
                  )}
                  <Card.Header className="pb-2">
                    <Card.Title className="text-xl font-bold">{pkg.name || pkg.packageName}</Card.Title>
                    <div className="mt-3 flex items-baseline gap-2">
                      <span className="font-mono-tabular text-3xl font-extrabold text-foreground">
                        {formatVnd(pkg.price)}
                      </span>
                    </div>
                    <div className="mt-1 flex items-center gap-2">
                      <Chip size="sm" variant="soft" color="accent" className="font-mono-tabular">
                        {pkg.credits} credits
                      </Chip>
                      <span className="text-xs font-mono-tabular text-muted">
                        ({formatVndUnitPrice(pkg.price, pkg.credits)})
                      </span>
                    </div>
                  </Card.Header>

                  <Card.Content className="pt-2 flex-1 flex flex-col justify-between gap-6">
                    <ul className="space-y-2.5 text-sm text-foreground list-none p-0 m-0">
                      {(pkg.featureList && pkg.featureList.length > 0
                        ? pkg.featureList
                        : ['Platform mock exams', 'AI speaking & writing grading', 'Mentor bookings']
                      ).map((feat, idx) => (
                        <li key={idx} className="flex items-center gap-2.5 text-muted">
                          <Check className="w-4 h-4 text-success flex-shrink-0" />
                          <span>{feat}</span>
                        </li>
                      ))}
                    </ul>

                    <div className="pt-4 border-t border-separator">
                      <Link
                        to="/checkout"
                        search={{ packageId: pkg.packageId }}
                        className="no-underline w-full block"
                      >
                        <Button
                          variant={pkg.isFeatured ? 'primary' : 'secondary'}
                          className="w-full justify-center"
                        >
                          Buy {pkg.credits} credits
                          <ArrowRight className="w-4 h-4 ml-1.5" />
                        </Button>
                      </Link>
                    </div>
                  </Card.Content>
                </Card>
              ))}
            </div>
          )}
        </Tabs.Panel>

        {/* Tab 2: Append-only Credit Ledger */}
        <Tabs.Panel id="ledger" className="pt-6">
          <Card>
            <Card.Header>
              <Card.Title>Append-only credit ledger</Card.Title>
              <Card.Description>
                Every transaction, refund, or deduction recorded with an immutable audit delta.
              </Card.Description>
            </Card.Header>
            <Card.Content>
              {!wallet || wallet.ledger.length === 0 ? (
                <div className="text-center py-10 text-sm text-muted">
                  No credit transactions recorded yet.
                </div>
              ) : (
                <Table>
                  <Table.ScrollContainer>
                    <Table.Content aria-label="Credit ledger history">
                      <Table.Header>
                        <Table.Column isRowHeader>Date & time</Table.Column>
                        <Table.Column>Activity / Reason</Table.Column>
                        <Table.Column>Order / Reference</Table.Column>
                        <Table.Column>Notes</Table.Column>
                        <Table.Column className="text-right">Delta</Table.Column>
                      </Table.Header>
                      <Table.Body>
                        {wallet.ledger.map((entry) => {
                          const isPositive = entry.delta > 0;
                          return (
                            <Table.Row key={entry.txnId}>
                              <Table.Cell className="font-mono-tabular text-xs text-muted">
                                {formatDateTime(entry.createdAt)}
                              </Table.Cell>
                              <Table.Cell className="font-medium text-foreground">
                                {formatCreditReason(entry.reason)}
                              </Table.Cell>
                              <Table.Cell className="font-mono-tabular text-xs">
                                {entry.orderCode ? (
                                  <Link
                                    to={`/orders/${entry.orderCode}`}
                                    className="text-accent hover:underline font-mono"
                                  >
                                    {entry.orderCode}
                                  </Link>
                                ) : (
                                  '—'
                                )}
                              </Table.Cell>
                              <Table.Cell className="text-xs text-muted max-w-xs truncate">
                                {entry.note || '—'}
                              </Table.Cell>
                              <Table.Cell
                                className={`text-right font-mono-tabular font-bold text-sm ${
                                  isPositive ? 'text-success' : 'text-danger'
                                }`}
                              >
                                {isPositive ? `+${entry.delta}` : `${entry.delta}`}
                              </Table.Cell>
                            </Table.Row>
                          );
                        })}
                      </Table.Body>
                    </Table.Content>
                  </Table.ScrollContainer>
                </Table>
              )}
            </Card.Content>
          </Card>
        </Tabs.Panel>

        {/* Tab 3: Orders & Receipts */}
        <Tabs.Panel id="orders" className="pt-6">
          <Card>
            <Card.Header>
              <Card.Title>Orders & payment receipts</Card.Title>
              <Card.Description>
                Track status across payOS, VietQR, or simulated gateways.
              </Card.Description>
            </Card.Header>
            <Card.Content>
              {!wallet || wallet.orders.length === 0 ? (
                <div className="text-center py-10 text-sm text-muted">
                  No orders placed yet.
                </div>
              ) : (
                <Table>
                  <Table.ScrollContainer>
                    <Table.Content aria-label="Past orders">
                      <Table.Header>
                        <Table.Column isRowHeader>Order code</Table.Column>
                        <Table.Column>Package</Table.Column>
                        <Table.Column>Credits</Table.Column>
                        <Table.Column>Amount</Table.Column>
                        <Table.Column>Gateway</Table.Column>
                        <Table.Column>Status</Table.Column>
                        <Table.Column>Created at</Table.Column>
                        <Table.Column className="text-right">Action</Table.Column>
                      </Table.Header>
                      <Table.Body>
                        {wallet.orders.map((order) => {
                          const statusDisplay = formatOrderStatus(order.status);
                          return (
                            <Table.Row key={order.orderId}>
                              <Table.Cell className="font-mono-tabular font-semibold">
                                <Link
                                  to={`/orders/${order.orderCode}`}
                                  className="text-accent hover:underline"
                                >
                                  {order.orderCode}
                                </Link>
                              </Table.Cell>
                              <Table.Cell>{order.packageName}</Table.Cell>
                              <Table.Cell className="font-mono-tabular">
                                {order.credits}
                              </Table.Cell>
                              <Table.Cell className="font-mono-tabular">
                                {formatVnd(order.amount)}
                              </Table.Cell>
                              <Table.Cell className="text-xs text-muted">
                                {formatGatewayLabel(order.gateway)}
                              </Table.Cell>
                              <Table.Cell>
                                <Chip
                                  size="sm"
                                  color={statusDisplay.tone}
                                  variant="soft"
                                >
                                  {statusDisplay.label}
                                </Chip>
                              </Table.Cell>
                              <Table.Cell className="font-mono-tabular text-xs text-muted">
                                {formatDateTime(order.createdAt)}
                              </Table.Cell>
                              <Table.Cell className="text-right">
                                <Link
                                  to={`/orders/${order.orderCode}`}
                                  className="no-underline"
                                >
                                  <Button size="sm" variant="ghost">
                                    View
                                  </Button>
                                </Link>
                              </Table.Cell>
                            </Table.Row>
                          );
                        })}
                      </Table.Body>
                    </Table.Content>
                  </Table.ScrollContainer>
                </Table>
              )}
            </Card.Content>
          </Card>
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}
