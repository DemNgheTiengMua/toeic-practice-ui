import React, { useState } from 'react';
import { Breadcrumbs, Tabs } from '@heroui/react';
import { Coins, FileText, Package, Receipt } from 'lucide-react';
import { useAuth } from '../../../context/AuthContext';
import { canManageBilling, canManageUsers, canTriageSupport } from '../../../lib/security';
import { AccessDeniedPage } from '../../public/AccessDeniedPage';
import { OrdersTab } from './OrdersTab';
import { RefundsTab } from './RefundsTab';
import { PackagesTab } from './PackagesTab';

export interface AdminFinancePageProps {
  initialTab?: 'orders' | 'refunds' | 'packages';
}

export function AdminFinancePage({ initialTab }: AdminFinancePageProps) {
  const { user } = useAuth();
  const [selectedTab, setSelectedTab] = useState<string | null>(initialTab ?? null);

  const isBillingAllowed = canManageBilling(user);
  const isSystemAdmin = canManageUsers(user);
  const isSupportTriage = canTriageSupport(user);

  if (!isBillingAllowed) {
    return <AccessDeniedPage />;
  }

  // Determine allowed tabs based on permissions
  // SystemAdmin gets orders, refunds, and packages. SupportTriage gets refunds.
  const allowedTabs: Array<{ id: 'orders' | 'refunds' | 'packages'; label: string; icon: React.ComponentType<{ className?: string }> }> = [];

  if (isSystemAdmin) {
    allowedTabs.push({ id: 'orders', label: 'Orders', icon: FileText });
  }

  if (isSupportTriage || isSystemAdmin) {
    allowedTabs.push({ id: 'refunds', label: 'Refunds desk', icon: Receipt });
  }

  if (isSystemAdmin) {
    allowedTabs.push({ id: 'packages', label: 'Credit packages', icon: Package });
  }

  const activeTab =
    selectedTab && allowedTabs.some((t) => t.id === selectedTab)
      ? selectedTab
      : allowedTabs[0]?.id || 'refunds';

  return (
    <div className="max-w-7xl mx-auto px-4 py-8 space-y-6">
      {/* Page Header */}
      <div className="space-y-1">
        <Breadcrumbs>
          <Breadcrumbs.Item href="/admin">Admin Studio</Breadcrumbs.Item>
          <Breadcrumbs.Item>Finance & refunds</Breadcrumbs.Item>
        </Breadcrumbs>
        <h1 className="font-display text-3xl font-bold tracking-tight text-foreground">
          Finance & refunds
        </h1>
        <p className="text-sm text-muted">
          Manage learner payments, order reconciliation, refund processing, and credit package catalog.
        </p>
      </div>

      {/* Tabs Container */}
      <Tabs
        className="w-full"
        selectedKey={activeTab}
        onSelectionChange={(key) => setSelectedTab(String(key))}
      >
        <Tabs.ListContainer>
          <Tabs.List aria-label="Admin finance navigation tabs">
            {allowedTabs.map((tab) => {
              const Icon = tab.icon;
              return (
                <Tabs.Tab key={tab.id} id={tab.id}>
                  <div className="flex items-center gap-2">
                    <Icon className="h-4 w-4" />
                    <span>{tab.label}</span>
                  </div>
                  <Tabs.Indicator />
                </Tabs.Tab>
              );
            })}
          </Tabs.List>
        </Tabs.ListContainer>

        {isSystemAdmin && (
          <Tabs.Panel id="orders" className="pt-6">
            <OrdersTab />
          </Tabs.Panel>
        )}

        {(isSupportTriage || isSystemAdmin) && (
          <Tabs.Panel id="refunds" className="pt-6">
            <RefundsTab />
          </Tabs.Panel>
        )}

        {isSystemAdmin && (
          <Tabs.Panel id="packages" className="pt-6">
            <PackagesTab />
          </Tabs.Panel>
        )}
      </Tabs>
    </div>
  );
}
