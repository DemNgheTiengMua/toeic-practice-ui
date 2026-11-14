import React, { useEffect, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import {
  Alert,
  Button,
  Card,
  Chip,
  Modal,
  Tabs,
} from '@heroui/react';
import {
  CheckCircle,
  Coins,
  Lock,
  Mail,
  Phone,
  RefreshCw,
  Search,
  Shield,
  ShieldAlert,
  Unlock,
  UserCheck,
  UserX,
  Users,
  X,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import {
  api,
  type AdminPlacementHistoryItemDto,
  type AdminUserDetailDto,
  type AdminUserDto,
  type AdminUsersSummaryDto,
} from '../../lib/api';
import { canManageUsers } from '../../lib/security';
import {
  formatPlacementScopeLabel,
  formatPlacementStatusLabel,
  STAFF_CAPABILITY_CATALOG,
  validateCreditAdjustment,
} from './adminHelpers';
import { AdminAccessGuard } from './AdminAccessGuard';

export function AdminUsersPage() {
  const { user: currentUser } = useAuth();
  const navigate = useNavigate();

  const [activeTab, setActiveTab] = useState<string>('users');

  // Search & Filter state
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'locked' | 'not_activated'>('all');
  const [page, setPage] = useState(1);
  const pageSize = 25;

  // Data state
  const [summary, setSummary] = useState<AdminUsersSummaryDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  // Modals state
  const [selectedUserDetail, setSelectedUserDetail] = useState<AdminUserDetailDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailModalOpen, setDetailModalOpen] = useState(false);

  const [adjustTargetUser, setAdjustTargetUser] = useState<AdminUserDto | null>(null);
  const [adjustDelta, setAdjustDelta] = useState<string>('');
  const [adjustReason, setAdjustReason] = useState<string>('');
  const [adjustValidationError, setAdjustValidationError] = useState<string | null>(null);
  const [adjustSubmitting, setAdjustSubmitting] = useState(false);
  const [adjustModalOpen, setAdjustModalOpen] = useState(false);

  async function loadUsers(currentQuery = query, currentStatus = statusFilter, currentPage = page) {
    try {
      setLoading(true);
      setActionError(null);
      const res = await api.admin.users({
        q: currentQuery.trim() || undefined,
        status: currentStatus === 'all' ? undefined : currentStatus,
        page: currentPage,
        pageSize,
      });
      setSummary(res);
    } catch (err: any) {
      setActionError(err.detail || err.message || 'Failed to load user list.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (canManageUsers(currentUser)) {
      void loadUsers();
    }
  }, [currentUser, page, statusFilter]);

  function handleSearchSubmit(e: React.FormEvent) {
    e.preventDefault();
    setPage(1);
    void loadUsers(query, statusFilter, 1);
  }

  async function handleOpenDetail(user: AdminUserDto) {
    try {
      setDetailLoading(true);
      setActionError(null);
      setDetailModalOpen(true);
      const detail = await api.admin.userDetail(user.userId);
      setSelectedUserDetail(detail);
    } catch (err: any) {
      setActionError(err.detail || err.message || 'Failed to load user details.');
      setDetailModalOpen(false);
    } finally {
      setDetailLoading(false);
    }
  }

  async function handleResendActivation(userId: number) {
    try {
      setActionError(null);
      await api.admin.resendUserActivation(userId);
      setActionSuccess('Activation email resent successfully.');
    } catch (err: any) {
      setActionError(err.detail || err.message || 'Failed to resend activation email.');
    }
  }

  async function handleToggleLock(user: AdminUserDto) {
    try {
      setActionError(null);
      if (user.isLocked) {
        await api.admin.unlockUser(user.userId);
        setActionSuccess(`User ${user.email} unlocked.`);
      } else {
        await api.admin.lockUser(user.userId);
        setActionSuccess(`User ${user.email} locked.`);
      }
      void loadUsers();
    } catch (err: any) {
      setActionError(err.detail || err.message || 'Action failed.');
    }
  }

  function handleOpenAdjustModal(user: AdminUserDto) {
    setAdjustTargetUser(user);
    setAdjustDelta('');
    setAdjustReason('');
    setAdjustValidationError(null);
    setAdjustModalOpen(true);
  }

  async function handleApplyAdjustment() {
    if (!adjustTargetUser) return;
    const deltaNum = parseInt(adjustDelta, 10);
    const validation = validateCreditAdjustment(deltaNum, adjustReason);
    if (!validation.valid) {
      setAdjustValidationError(validation.error || 'Invalid adjustment parameters.');
      return;
    }

    try {
      setAdjustSubmitting(true);
      setAdjustValidationError(null);
      const result = await api.admin.adjustCredits(adjustTargetUser.userId, {
        delta: deltaNum,
        reason: adjustReason.trim(),
      });

      // Update in table
      if (summary) {
        setSummary({
          ...summary,
          users: summary.users.map((u) =>
            u.userId === result.userId ? { ...u, creditsLeft: result.newBalance } : u,
          ),
        });
      }

      setActionSuccess(
        `Successfully adjusted ${result.delta > 0 ? `+${result.delta}` : result.delta} credits. New balance: ${result.newBalance} cr.`,
      );
      setAdjustModalOpen(false);
      setAdjustTargetUser(null);
    } catch (err: any) {
      setAdjustValidationError(err.detail || err.message || 'Failed to adjust user credits.');
    } finally {
      setAdjustSubmitting(false);
    }
  }

  return (
    <AdminAccessGuard predicate={(u) => canManageUsers(u)}>
      <div className="space-y-6">
        {/* Header */}
        <div>
          <h1 className="font-display text-2xl font-bold tracking-tight text-foreground sm:text-3xl">
            People &amp; audit
          </h1>
          <p className="text-sm text-muted">
            Learner directory, manual credit grants, composable role governance, and content audit.
          </p>
        </div>

        {/* Global Notifications */}
        {actionError && (
          <Alert status="danger">
            <Alert.Title className="text-sm font-semibold">Error</Alert.Title>
            <Alert.Description className="text-xs">{actionError}</Alert.Description>
          </Alert>
        )}

        {actionSuccess && (
          <Alert status="success">
            <Alert.Title className="text-sm font-semibold">Success</Alert.Title>
            <Alert.Description className="text-xs">{actionSuccess}</Alert.Description>
          </Alert>
        )}

        {/* Tab Navigation */}
        <Tabs
          selectedKey={activeTab}
          onSelectionChange={(k) => setActiveTab(String(k))}
          className="w-full"
        >
          <div className="max-w-full overflow-x-auto">
            <Tabs.List aria-label="People and governance navigation">
              <Tabs.Tab id="users">Users &amp; credits</Tabs.Tab>
              <Tabs.Tab id="roles">Roles &amp; capabilities</Tabs.Tab>
            </Tabs.List>
          </div>

          {/* TAB 1: USERS & CREDITS */}
          <Tabs.Panel id="users" className="pt-4 space-y-6">
            {/* Status Summary Cards */}
            <div className="grid gap-3 grid-cols-2 lg:grid-cols-4">
              <Card
                className={`cursor-pointer border transition-colors ${statusFilter === 'all' ? 'border-accent bg-surface-secondary/40' : 'border-border'}`}
                onClick={() => {
                  setStatusFilter('all');
                  setPage(1);
                }}
              >
                <Card.Content className="p-3.5 flex items-center justify-between">
                  <div>
                    <div className="text-xs font-medium text-muted">Total users</div>
                    <div className="text-xl font-bold font-mono-tabular text-foreground">
                      {summary?.totalCount ?? '—'}
                    </div>
                  </div>
                  <Users className="h-5 w-5 text-muted" />
                </Card.Content>
              </Card>

              <Card
                className={`cursor-pointer border transition-colors ${statusFilter === 'active' ? 'border-accent bg-surface-secondary/40' : 'border-border'}`}
                onClick={() => {
                  setStatusFilter('active');
                  setPage(1);
                }}
              >
                <Card.Content className="p-3.5 flex items-center justify-between">
                  <div>
                    <div className="text-xs font-medium text-muted">Active accounts</div>
                    <div className="text-xl font-bold font-mono-tabular text-success">
                      {summary?.activeCount ?? '—'}
                    </div>
                  </div>
                  <UserCheck className="h-5 w-5 text-success" />
                </Card.Content>
              </Card>

              <Card
                className={`cursor-pointer border transition-colors ${statusFilter === 'locked' ? 'border-accent bg-surface-secondary/40' : 'border-border'}`}
                onClick={() => {
                  setStatusFilter('locked');
                  setPage(1);
                }}
              >
                <Card.Content className="p-3.5 flex items-center justify-between">
                  <div>
                    <div className="text-xs font-medium text-muted">Locked accounts</div>
                    <div className="text-xl font-bold font-mono-tabular text-danger">
                      {summary?.lockedCount ?? '—'}
                    </div>
                  </div>
                  <UserX className="h-5 w-5 text-danger" />
                </Card.Content>
              </Card>

              <Card
                className={`cursor-pointer border transition-colors ${statusFilter === 'not_activated' ? 'border-accent bg-surface-secondary/40' : 'border-border'}`}
                onClick={() => {
                  setStatusFilter('not_activated');
                  setPage(1);
                }}
              >
                <Card.Content className="p-3.5 flex items-center justify-between">
                  <div>
                    <div className="text-xs font-medium text-muted">Unverified</div>
                    <div className="text-xl font-bold font-mono-tabular text-warning">
                      {summary?.notActivatedCount ?? '—'}
                    </div>
                  </div>
                  <Mail className="h-5 w-5 text-warning" />
                </Card.Content>
              </Card>
            </div>

            {/* Filter Bar & Search */}
            <form onSubmit={handleSearchSubmit} className="flex flex-col gap-3 sm:flex-row sm:items-center">
              <div className="relative flex-1">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted pointer-events-none" />
                <input
                  type="text"
                  placeholder="Search by email or name..."
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  className="w-full pl-9 pr-3 py-2 text-sm bg-surface rounded-lg border border-border focus:border-accent focus:outline-none"
                />
              </div>

              <div className="flex items-center gap-2">
                <Button type="submit" variant="primary">
                  Search
                </Button>
                {query && (
                  <Button
                    type="button"
                    variant="secondary"
                    onPress={() => {
                      setQuery('');
                      setPage(1);
                      void loadUsers('', statusFilter, 1);
                    }}
                  >
                    Reset
                  </Button>
                )}
              </div>
            </form>

            {/* Users Table */}
            <Card className="border border-border min-w-0">
              <Card.Content className="p-0 min-w-0">
                <div className="overflow-x-auto max-w-full">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-border text-xs text-muted uppercase bg-surface-secondary/20">
                      <tr>
                        <th className="py-3 px-4 font-medium">User / Email</th>
                        <th className="py-3 px-3 font-medium">Status</th>
                        <th className="py-3 px-3 text-right font-medium">Credits</th>
                        <th className="py-3 px-3 text-right font-medium">Exams</th>
                        <th className="py-3 px-3 text-right font-medium">Best score</th>
                        <th className="py-3 px-3 font-medium">Last active</th>
                        <th className="py-3 px-4 text-right font-medium">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/60">
                      {loading ? (
                        <tr>
                          <td colSpan={7} className="py-8 text-center text-xs text-muted">
                            Loading accounts...
                          </td>
                        </tr>
                      ) : !summary?.users || summary.users.length === 0 ? (
                        <tr>
                          <td colSpan={7} className="py-8 text-center text-xs text-muted">
                            No users matched the criteria.
                          </td>
                        </tr>
                      ) : (
                        summary.users.map((u) => (
                          <tr key={u.userId} className="hover:bg-surface-secondary/30 transition-colors">
                            <td className="py-3 px-4">
                              <div className="font-medium text-foreground">{u.fullName || '—'}</div>
                              <div className="text-xs text-muted font-mono-tabular">{u.email}</div>
                              {u.phone && <div className="text-[11px] text-muted">{u.phone}</div>}
                            </td>
                            <td className="py-3 px-3">
                              {u.isLocked ? (
                                <Chip color="danger" variant="soft" className="text-xs">
                                  Locked
                                </Chip>
                              ) : !u.emailVerifiedAt ? (
                                <Chip color="warning" variant="soft" className="text-xs">
                                  Unverified
                                </Chip>
                              ) : (
                                <Chip color="success" variant="soft" className="text-xs">
                                  Active
                                </Chip>
                              )}
                            </td>
                            <td className="py-3 px-3 text-right font-mono-tabular font-medium text-foreground">
                              {u.creditsLeft.toLocaleString()}
                            </td>
                            <td className="py-3 px-3 text-right font-mono-tabular text-muted">
                              {u.examsTaken}
                            </td>
                            <td className="py-3 px-3 text-right font-mono-tabular font-medium text-foreground">
                              {u.bestScore != null ? Math.round(u.bestScore) : '—'}
                            </td>
                            <td className="py-3 px-3 text-xs text-muted font-mono-tabular">
                              {u.lastLoginAt
                                ? new Date(u.lastLoginAt).toLocaleDateString('en-US', {
                                    month: 'short',
                                    day: 'numeric',
                                    year: 'numeric',
                                  })
                                : 'Never'}
                            </td>
                            <td className="py-3 px-4 text-right">
                              <div className="flex items-center justify-end gap-1.5">
                                <Button
                                  size="sm"
                                  variant="secondary"
                                  onPress={() => void handleOpenDetail(u)}
                                >
                                  Details
                                </Button>
                                <Button
                                  size="sm"
                                  variant="secondary"
                                  onPress={() => handleOpenAdjustModal(u)}
                                >
                                  Adjust credits
                                </Button>
                                <Button
                                  size="sm"
                                  variant={u.isLocked ? 'secondary' : 'danger'}
                                  onPress={() => void handleToggleLock(u)}
                                >
                                  {u.isLocked ? 'Unlock' : 'Lock'}
                                </Button>
                              </div>
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </Card.Content>
            </Card>
          </Tabs.Panel>

          {/* TAB 2: ROLES & CAPABILITIES */}
          <Tabs.Panel id="roles" className="pt-4 space-y-6">
            <Card className="border border-border">
              <Card.Header>
                <Card.Title className="font-display text-lg font-semibold flex items-center gap-2">
                  <Shield className="h-5 w-5 text-accent" />
                  Staff capability architecture (D16 / ADR-0015)
                </Card.Title>
                <Card.Description className="text-xs text-muted">
                  Extra Efficiency uses fine-grained, composable capabilities rather than monolithic roles. Staff accounts are assigned specific permissions.
                </Card.Description>
              </Card.Header>
              <Card.Content className="space-y-4">
                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                  {STAFF_CAPABILITY_CATALOG.map((meta) => {
                    const isHeldByMe = (currentUser?.permissions ?? []).includes(meta.code);
                    return (
                      <div
                        key={meta.code}
                        className={`rounded-lg border p-4 space-y-2 ${
                          isHeldByMe ? 'border-accent bg-surface-secondary/40' : 'border-border'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <span className="font-mono-tabular text-xs font-semibold text-accent">
                            {meta.code}
                          </span>
                          {isHeldByMe && (
                            <Chip color="accent" variant="soft" className="text-[10px]">
                              Held by you
                            </Chip>
                          )}
                        </div>
                        <div className="text-sm font-semibold text-foreground">{meta.name}</div>
                        <p className="text-xs text-muted leading-relaxed">{meta.description}</p>
                      </div>
                    );
                  })}
                </div>
              </Card.Content>
            </Card>
          </Tabs.Panel>
        </Tabs>

        {/* MODAL 1: USER DETAILS & PLACEMENT HISTORY */}
        {detailModalOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
            <div className="w-full max-w-3xl rounded-xl border border-border bg-surface p-6 shadow-2xl space-y-4 max-h-[90vh] overflow-y-auto">
              <div className="flex items-center justify-between border-b border-border pb-3">
                <h2 className="font-display text-lg font-semibold text-foreground">
                  User details &amp; billing history
                </h2>
                <button
                  type="button"
                  aria-label="Dismiss"
                  onClick={() => setDetailModalOpen(false)}
                  className="text-muted hover:text-foreground"
                >
                  <X className="h-5 w-5" />
                </button>
              </div>

              {detailLoading || !selectedUserDetail ? (
                <div className="py-8 text-center text-xs text-muted">Loading profile...</div>
              ) : (
                <div className="space-y-5">
                  {/* Account overview */}
                  <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 p-3 rounded-lg bg-surface-secondary/40 border border-border/60 text-xs">
                    <div>
                      <span className="text-muted block">Full name</span>
                      <span className="font-medium text-foreground">{selectedUserDetail.user.fullName || '—'}</span>
                    </div>
                    <div>
                      <span className="text-muted block">Email</span>
                      <span className="font-medium text-foreground font-mono-tabular">{selectedUserDetail.user.email}</span>
                    </div>
                    <div>
                      <span className="text-muted block">Credit balance</span>
                      <span className="font-bold text-accent font-mono-tabular">{selectedUserDetail.user.creditsLeft} cr</span>
                    </div>
                    <div>
                      <span className="text-muted block">Status</span>
                      <span className="font-medium">
                        {selectedUserDetail.user.isLocked
                          ? 'Locked'
                          : selectedUserDetail.user.emailVerifiedAt
                            ? 'Active'
                            : 'Unverified'}
                      </span>
                    </div>
                    <div>
                      <span className="text-muted block">Account created</span>
                      <span className="font-medium font-mono-tabular">
                        {new Date(selectedUserDetail.user.createdAt).toLocaleDateString()}
                      </span>
                    </div>
                    {!selectedUserDetail.user.emailVerifiedAt && (
                      <div className="flex items-center">
                        <Button
                          size="sm"
                          variant="secondary"
                          onPress={() => void handleResendActivation(selectedUserDetail.user.userId)}
                        >
                          Resend activation
                        </Button>
                      </div>
                    )}
                  </div>

                  {/* Placement history */}
                  <div className="space-y-2">
                    <h3 className="text-sm font-semibold text-foreground">Placement test history</h3>
                    {selectedUserDetail.placements.length === 0 ? (
                      <div className="text-xs text-muted py-4 text-center">No placement tests taken.</div>
                    ) : (
                      <div className="overflow-x-auto border border-border rounded-lg">
                        <table className="w-full text-left text-xs">
                          <thead className="bg-surface-secondary/30 border-b border-border text-muted uppercase">
                            <tr>
                              <th className="p-2.5">Certificate / Exam</th>
                              <th className="p-2.5">Status</th>
                              <th className="p-2.5">Scope</th>
                              <th className="p-2.5">Billing</th>
                              <th className="p-2.5 text-right">Score</th>
                              <th className="p-2.5 text-right">Band</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-border/60">
                            {selectedUserDetail.placements.map((p) => (
                              <tr key={p.placementId}>
                                <td className="p-2.5">
                                  <div className="font-medium text-foreground">{p.examName}</div>
                                  <div className="text-[11px] text-muted font-mono-tabular">{p.certCode}</div>
                                </td>
                                <td className="p-2.5">
                                  <span className="font-medium text-foreground">
                                    {formatPlacementStatusLabel(p.status)}
                                  </span>
                                </td>
                                <td className="p-2.5 text-muted">
                                  {formatPlacementScopeLabel(p.scope)}
                                </td>
                                <td className="p-2.5">
                                  {p.paid ? (
                                    <span className="text-accent font-mono-tabular">
                                      Paid ({p.creditsCharged} cr)
                                    </span>
                                  ) : (
                                    <span className="text-success font-medium">Free first attempt</span>
                                  )}
                                </td>
                                <td className="p-2.5 text-right font-mono-tabular font-medium">
                                  {p.totalScore != null ? Math.round(p.totalScore) : '—'}
                                </td>
                                <td className="p-2.5 text-right font-mono-tabular font-bold text-accent">
                                  {p.resultBandCode || '—'}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>
                </div>
              )}

              <div className="flex justify-end pt-3 border-t border-border">
                <Button variant="secondary" onPress={() => setDetailModalOpen(false)}>
                  Close
                </Button>
              </div>
            </div>
          </div>
        )}

        {/* MODAL 2: ADJUST CREDITS (Delta + Reason) */}
        {adjustModalOpen && adjustTargetUser && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
            <div className="w-full max-w-md rounded-xl border border-border bg-surface p-6 shadow-2xl space-y-4">
              <div className="flex items-center justify-between border-b border-border pb-3">
                <h2 className="font-display text-lg font-semibold text-foreground">
                  Adjust credits: {adjustTargetUser.fullName || adjustTargetUser.email}
                </h2>
                <button
                  type="button"
                  aria-label="Dismiss"
                  onClick={() => setAdjustModalOpen(false)}
                  className="text-muted hover:text-foreground"
                >
                  <X className="h-5 w-5" />
                </button>
              </div>

              {adjustValidationError && (
                <Alert status="danger">
                  <Alert.Title className="text-xs font-semibold">Validation error</Alert.Title>
                  <Alert.Description className="text-xs">{adjustValidationError}</Alert.Description>
                </Alert>
              )}

              <div className="space-y-3">
                <div className="text-xs text-muted">
                  Current ledger balance:{' '}
                  <span className="font-bold font-mono-tabular text-accent">
                    {adjustTargetUser.creditsLeft} cr
                  </span>
                </div>

                <div className="space-y-1">
                  <label htmlFor="credit-delta-input" className="text-xs font-medium text-foreground">
                    Credit delta
                  </label>
                  <input
                    id="credit-delta-input"
                    type="number"
                    value={adjustDelta}
                    onChange={(e) => setAdjustDelta(e.target.value)}
                    placeholder="e.g. 50 (grant) or -20 (debit)"
                    className="w-full px-3 py-2 text-sm bg-surface rounded-lg border border-border focus:border-accent focus:outline-none font-mono-tabular"
                  />
                  <p className="text-[11px] text-muted">
                    Positive integer to grant credits; negative integer to debit. Cannot be zero.
                  </p>
                </div>

                <div className="space-y-1">
                  <label htmlFor="credit-reason-input" className="text-xs font-medium text-foreground">
                    Reason
                  </label>
                  <textarea
                    id="credit-reason-input"
                    rows={3}
                    value={adjustReason}
                    onChange={(e) => setAdjustReason(e.target.value)}
                    placeholder="Describe reason for this manual ledger transaction (audit log)"
                    className="w-full px-3 py-2 text-sm bg-surface rounded-lg border border-border focus:border-accent focus:outline-none"
                  />
                  <p className="text-[11px] text-muted">
                    Mandatory for ledger audit trail (SRS D-09 / CreditTransactions).
                  </p>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-3 border-t border-border">
                <Button
                  variant="secondary"
                  onPress={() => setAdjustModalOpen(false)}
                  isDisabled={adjustSubmitting}
                >
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  onPress={() => void handleApplyAdjustment()}
                  isDisabled={adjustSubmitting}
                >
                  {adjustSubmitting ? 'Applying...' : 'Apply adjustment'}
                </Button>
              </div>
            </div>
          </div>
        )}
      </div>
    </AdminAccessGuard>
  );
}
