import React, { useState } from 'react';
import { Breadcrumbs, Tabs } from '@heroui/react';
import { useSearch } from '@tanstack/react-router';
import { FileText, Receipt } from 'lucide-react';
import { useAuth } from '../../../context/AuthContext';
import { canManageBilling, canManageUsers, canTriageSupport } from '../../../lib/security';
import { AccessDeniedPage } from '../../public/AccessDeniedPage';
import { OrdersTab } from './OrdersTab';
import { RefundsTab } from './RefundsTab';

export interface AdminFinancePageProps {
  initialTab?: 'orders' | 'refunds';
}

export function AdminFinancePage({ initialTab }: AdminFinancePageProps) {
  const { user } = useAuth();
  // #349/#350/#345: a complaint's refund arrives with the incident already identified - the complaint,
  // the learner it belongs to, and the subject's order or credit count. Carried in the URL by the
  // screen that opened it, never retyped here.
  const search = useSearch({ strict: false }) as {
    complaintId?: number | string | null;
    userId?: number | string | null;
    orderId?: number | string | null;
    creditCount?: number | string | null;
  };
  const complaintId =
    search.complaintId != null && String(search.complaintId).trim() !== ''
      ? Number(search.complaintId)
      : null;
  const learnerId =
    search.userId != null && String(search.userId).trim() !== '' ? Number(search.userId) : null;
  const subjectOrderId =
    search.orderId != null && String(search.orderId).trim() !== '' ? Number(search.orderId) : null;
  const subjectCreditCount =
    search.creditCount != null && String(search.creditCount).trim() !== ''
      ? Number(search.creditCount)
      : null;
  const [selectedTab, setSelectedTab] = useState<string | null>(
    complaintId !== null ? 'refunds' : (initialTab ?? null),
  );

  const isBillingAllowed = canManageBilling(user);
  const isSystemAdmin = canManageUsers(user);
  const isSupportTriage = canTriageSupport(user);

  if (!isBillingAllowed) {
    return <AccessDeniedPage />;
  }

  // Determine allowed tabs based on permissions
  // SystemAdmin gets orders and refunds. SupportTriage gets refunds.
  const allowedTabs: Array<{ id: 'orders' | 'refunds'; label: string; icon: React.ComponentType<{ className?: string }> }> = [];

  if (isSystemAdmin) {
    allowedTabs.push({ id: 'orders', label: 'Orders', icon: FileText });
  }

  if (isSupportTriage || isSystemAdmin) {
    allowedTabs.push({ id: 'refunds', label: 'Refunds desk', icon: Receipt });
  }

  const activeTab =
    selectedTab && allowedTabs.some((t) => t.id === selectedTab)
      ? selectedTab
      : allowedTabs[0]?.id || 'refunds';

  return (
    <div className="space-y-6">
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
          Manage learner payments, order reconciliation, and refund processing.
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
            <RefundsTab
              complaintId={complaintId}
              learnerId={learnerId}
              subjectOrderId={subjectOrderId}
              subjectCreditCount={subjectCreditCount}
            />
          </Tabs.Panel>
        )}
      </Tabs>
    </div>
  );
}
