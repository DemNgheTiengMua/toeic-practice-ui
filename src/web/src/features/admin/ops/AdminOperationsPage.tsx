import React, { useState } from 'react';
import { Breadcrumbs, Tabs } from '@heroui/react';
import { MessageSquare, Users } from 'lucide-react';
import { useAuth } from '../../../context/AuthContext';
import { canManageUsers, canTriageSupport } from '../../../lib/security';
import { AccessDeniedPage } from '../../public/AccessDeniedPage';
import { ComplaintsTab } from './ComplaintsTab';
import { MentorsTab } from './MentorsTab';

export interface AdminOperationsPageProps {
  initialTab?: 'complaints' | 'mentors';
}

export function AdminOperationsPage({ initialTab }: AdminOperationsPageProps) {
  const { user } = useAuth();
  const [selectedTab, setSelectedTab] = useState<string | null>(initialTab ?? null);

  const isTriageAllowed = canTriageSupport(user) || canManageUsers(user);

  if (!isTriageAllowed) {
    return <AccessDeniedPage />;
  }

  const allowedTabs: Array<{
    id: 'complaints' | 'mentors';
    label: string;
    icon: React.ComponentType<{ className?: string }>;
  }> = [
    { id: 'complaints', label: 'Complaints triage', icon: MessageSquare },
    { id: 'mentors', label: 'Mentors & schedules', icon: Users },
  ];

  const activeTab =
    selectedTab && allowedTabs.some((t) => t.id === selectedTab)
      ? selectedTab
      : allowedTabs[0]?.id || 'complaints';

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="space-y-1">
        <Breadcrumbs>
          <Breadcrumbs.Item href="/admin">Admin Studio</Breadcrumbs.Item>
          <Breadcrumbs.Item>Operations desk</Breadcrumbs.Item>
        </Breadcrumbs>
        <h1 className="font-display text-3xl font-bold tracking-tight text-foreground">
          Operations desk
        </h1>
        <p className="text-sm text-muted">
          Customer support triage, incident investigation, make-good credit grants, and mentor calendar operations.
        </p>
      </div>

      {/* Tabs Container */}
      <Tabs
        className="w-full"
        selectedKey={activeTab}
        onSelectionChange={(key) => setSelectedTab(String(key))}
      >
        <Tabs.ListContainer>
          <Tabs.List aria-label="Admin operations navigation tabs">
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

        <Tabs.Panel id="complaints" className="pt-6">
          <ComplaintsTab />
        </Tabs.Panel>

        <Tabs.Panel id="mentors" className="pt-6">
          <MentorsTab />
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}
