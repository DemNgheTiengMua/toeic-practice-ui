import React, { useEffect, useState } from 'react';
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
  Switch,
  Table,
  TextArea,
  TextField,
} from '@heroui/react';
import {
  Calendar,
  CheckCircle2,
  Clock,
  Coins,
  Download,
  Edit2,
  ExternalLink,
  Plus,
  Star,
  Trash2,
  User,
  Video,
} from 'lucide-react';
import {
  api,
  type AdminMentorListItemDto,
  type MentorWeeklySlotInputDto,
} from '../../../lib/api';
import { creditsToVnd, formatVnd, safeExternalHref, VND_PER_CREDIT } from '../../../lib/security';
import {
  formatDateTime,
  formatWeekdayLabel,
  getDefaultUtc7MonthRange,
  normalizeWeeklyTime,
  triggerBlobDownload,
  validateCsvExportDates,
  validateWeeklySlots,
  WEEKDAY_OPTIONS,
} from './opsHelpers';

export function MentorsTab() {
  const queryClient = useQueryClient();
  const defaultMonthRange = getDefaultUtc7MonthRange();

  // Create / Edit mentor state
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingMentorId, setEditingMentorId] = useState<number | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [selectedCertId, setSelectedCertId] = useState<number>(0);
  const [selectedSkillId, setSelectedSkillId] = useState<number | null>(null);
  const [priceCredits, setPriceCredits] = useState('3');
  const [sessionMinutes, setSessionMinutes] = useState('60');
  const [meetLink, setMeetLink] = useState('https://meet.google.com/new');
  const [headline, setHeadline] = useState('');
  const [bio, setBio] = useState('');
  const [expertise, setExpertise] = useState('');
  const [isActive, setIsActive] = useState(true);
  const [formWeeklySlots, setFormWeeklySlots] = useState<MentorWeeklySlotInputDto[]>([]);
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [apiError, setApiError] = useState<string | null>(null);

  // Weekly Schedule modal state
  const [scheduleMentor, setScheduleMentor] = useState<AdminMentorListItemDto | null>(null);
  const [scheduleWindows, setScheduleWindows] = useState<MentorWeeklySlotInputDto[]>([]);
  const [newDayOfWeek, setNewDayOfWeek] = useState<number>(1);
  const [newStartTime, setNewStartTime] = useState('08:00');
  const [newEndTime, setNewEndTime] = useState('09:00');
  const [scheduleError, setScheduleError] = useState<string | null>(null);

  // Payouts & CSV Export state
  const [payoutFrom, setPayoutFrom] = useState(defaultMonthRange.from);
  const [payoutTo, setPayoutTo] = useState(defaultMonthRange.to);
  const [payoutError, setPayoutError] = useState<string | null>(null);
  const [isExportingCsv, setIsExportingCsv] = useState(false);

  const { data: mentors, isLoading, error } = useQuery({
    queryKey: ['admin-mentors'],
    queryFn: () => api.admin.mentors(),
  });

  const { data: certOptions } = useQuery({
    queryKey: ['admin-mentor-options'],
    queryFn: () => api.admin.mentorOptions(),
  });

  const { data: fetchedWeeklySlots, isLoading: isLoadingSchedule } = useQuery({
    queryKey: ['admin-mentor-weekly-slots', scheduleMentor?.mentorId],
    queryFn: () => api.admin.mentorWeeklySlots(scheduleMentor!.mentorId),
    enabled: scheduleMentor !== null,
  });

  useEffect(() => {
    if (fetchedWeeklySlots) {
      setScheduleWindows(
        fetchedWeeklySlots.map((w) => ({
          dayOfWeek: w.dayOfWeek,
          startTime: normalizeWeeklyTime(w.startTime),
          endTime: normalizeWeeklyTime(w.endTime),
        })),
      );
    }
  }, [fetchedWeeklySlots]);

  const { data: payoutReport, isLoading: isLoadingPayouts } = useQuery({
    queryKey: ['admin-mentor-payouts', payoutFrom, payoutTo],
    queryFn: () => api.admin.mentorPayouts(payoutFrom || undefined, payoutTo || undefined),
  });

  const saveMutation = useMutation({
    mutationFn: () => {
      const errors: Record<string, string> = {};
      if (!displayName.trim()) {
        errors.displayName = 'Display name is required.';
      }

      const certIdToUse = selectedCertId || certOptions?.[0]?.certId || 1;
      if (!certIdToUse) {
        errors.certId = 'Please select a certificate.';
      }

      const pCredits = parseInt(priceCredits, 10);
      if (isNaN(pCredits) || pCredits <= 0) {
        errors.priceCredits = 'Price in credits must be greater than 0.';
      }

      const sMins = parseInt(sessionMinutes, 10);
      if (isNaN(sMins) || sMins <= 0) {
        errors.sessionMinutes = 'Duration must be greater than 0 minutes.';
      }

      const sanitizedMeetLink = safeExternalHref(meetLink);
      if (!sanitizedMeetLink) {
        errors.meetLink = 'A valid meeting link (https://...) is required.';
      }

      const weeklyVal = validateWeeklySlots(formWeeklySlots);
      if (!weeklyVal.valid) {
        errors.weeklySlots = weeklyVal.error || 'Invalid weekly schedule.';
      }

      if (Object.keys(errors).length > 0) {
        setFormErrors(errors);
        throw new Error('Please fix the form errors.');
      }

      const payload = {
        displayName: displayName.trim(),
        bio: bio.trim() || null,
        certId: certIdToUse,
        skillId: selectedSkillId || null,
        priceCredits: pCredits,
        meetLink: meetLink.trim(),
        headline: headline.trim() || null,
        expertise: expertise.trim() || null,
        sessionMinutes: sMins,
        isActive,
        weeklySlots: formWeeklySlots.map((w) => ({
          dayOfWeek: w.dayOfWeek,
          startTime: normalizeWeeklyTime(w.startTime),
          endTime: normalizeWeeklyTime(w.endTime),
        })),
      };

      if (editingMentorId !== null) {
        return api.admin.updateMentor(editingMentorId, payload);
      }
      return api.admin.createMentor(payload);
    },
    onSuccess: () => {
      setIsFormOpen(false);
      resetForm();
      void queryClient.invalidateQueries({ queryKey: ['admin-mentors'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-mentor-payouts'] });
    },
    onError: (err: any) => {
      if (err?.message !== 'Please fix the form errors.') {
        setApiError(err?.message || 'Could not save mentor profile.');
      }
    },
  });

  const saveWeeklyScheduleMutation = useMutation({
    mutationFn: () => {
      if (!scheduleMentor) {
        throw new Error('No mentor selected.');
      }
      const val = validateWeeklySlots(scheduleWindows);
      if (!val.valid) {
        throw new Error(val.error || 'Invalid weekly schedule.');
      }
      return api.admin.replaceMentorWeeklySlots(
        scheduleMentor.mentorId,
        scheduleWindows.map((w) => ({
          dayOfWeek: w.dayOfWeek,
          startTime: normalizeWeeklyTime(w.startTime),
          endTime: normalizeWeeklyTime(w.endTime),
        })),
      );
    },
    onSuccess: () => {
      setScheduleError(null);
      void queryClient.invalidateQueries({
        queryKey: ['admin-mentor-weekly-slots', scheduleMentor?.mentorId],
      });
      void queryClient.invalidateQueries({ queryKey: ['admin-mentors'] });
      setScheduleMentor(null);
    },
    onError: (err: any) => {
      setScheduleError(err?.message || 'Could not save weekly schedule.');
    },
  });

  function resetForm() {
    setEditingMentorId(null);
    setDisplayName('');
    setSelectedCertId(certOptions?.[0]?.certId || 1);
    setSelectedSkillId(null);
    setPriceCredits('3');
    setSessionMinutes('60');
    setMeetLink('https://meet.google.com/new');
    setHeadline('');
    setBio('');
    setExpertise('');
    setIsActive(true);
    setFormWeeklySlots([]);
    setFormErrors({});
    setApiError(null);
  }

  function openCreate() {
    resetForm();
    if (certOptions && certOptions.length > 0) {
      setSelectedCertId(certOptions[0].certId);
    }
    setIsFormOpen(true);
  }

  function openEdit(m: AdminMentorListItemDto) {
    setEditingMentorId(m.mentorId);
    setDisplayName(m.displayName);
    setSelectedCertId(m.certId);
    setSelectedSkillId(m.skillId || null);
    setPriceCredits(String(m.priceCredits));
    setSessionMinutes(String(m.sessionMinutes));
    setMeetLink(m.meetLink || '');
    setHeadline(m.headline || '');
    setBio(m.bio || '');
    setExpertise(m.expertise || '');
    setIsActive(m.isActive);
    setFormWeeklySlots(
      (m.weeklySlots ?? []).map((w) => ({
        dayOfWeek: w.dayOfWeek,
        startTime: normalizeWeeklyTime(w.startTime),
        endTime: normalizeWeeklyTime(w.endTime),
      })),
    );
    setFormErrors({});
    setApiError(null);
    setIsFormOpen(true);
  }

  function openScheduleModal(m: AdminMentorListItemDto) {
    setScheduleMentor(m);
    setScheduleWindows(
      (m.weeklySlots ?? []).map((w) => ({
        dayOfWeek: w.dayOfWeek,
        startTime: normalizeWeeklyTime(w.startTime),
        endTime: normalizeWeeklyTime(w.endTime),
      })),
    );
    setNewDayOfWeek(1);
    setNewStartTime('08:00');
    setNewEndTime('09:00');
    setScheduleError(null);
  }

  function handleAddScheduleWindow() {
    const candidate: MentorWeeklySlotInputDto = {
      dayOfWeek: newDayOfWeek,
      startTime: normalizeWeeklyTime(newStartTime),
      endTime: normalizeWeeklyTime(newEndTime),
    };
    const nextList = [...scheduleWindows, candidate];
    const val = validateWeeklySlots(nextList);
    if (!val.valid) {
      setScheduleError(val.error || 'Invalid weekly window.');
      return;
    }
    setScheduleError(null);
    setScheduleWindows(
      nextList.sort(
        (a, b) => a.dayOfWeek - b.dayOfWeek || a.startTime.localeCompare(b.startTime),
      ),
    );
  }

  function handleRemoveScheduleWindow(index: number) {
    setScheduleError(null);
    setScheduleWindows((prev) => prev.filter((_, idx) => idx !== index));
  }

  async function handleExportPayoutCsv() {
    const val = validateCsvExportDates(payoutFrom || undefined, payoutTo || undefined);
    if (!val.valid) {
      setPayoutError(val.error || 'Invalid date range.');
      return;
    }
    try {
      setIsExportingCsv(true);
      setPayoutError(null);
      const { blob, filename } = await api.admin.exportMentorPayoutsCsv(
        payoutFrom || undefined,
        payoutTo || undefined,
      );
      triggerBlobDownload(blob, filename);
    } catch (err: any) {
      setPayoutError(err?.message || 'Failed to export mentor payouts CSV.');
    } finally {
      setIsExportingCsv(false);
    }
  }

  const selectedCert = certOptions?.find((c) => c.certId === selectedCertId);
  const availableSkills = selectedCert?.skills || [];
  const parsedFormCredits = Math.max(0, parseInt(priceCredits, 10) || 0);
  const liveVndEquivalent = creditsToVnd(parsedFormCredits);

  return (
    <div className="space-y-8">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h3 className="text-base font-semibold text-foreground">Mentor roster & coaching</h3>
          <p className="text-xs text-muted">
            Manage human mentors, fixed Google Meet links, recurring weekly availability schedules (UTC+7), and payout settlements.
          </p>
        </div>

        <Button size="sm" variant="primary" onPress={openCreate}>
          <Plus className="h-4 w-4 mr-1.5" />
          Create mentor profile
        </Button>
      </div>

      {/* Mentors Table */}
      {isLoading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : error ? (
        <Alert status="danger">Could not load mentors roster. Please try again.</Alert>
      ) : mentors?.length === 0 ? (
        <Card className="border border-border p-8 text-center text-muted">
          No mentors registered yet.
        </Card>
      ) : (
        <Table>
          <Table.ScrollContainer>
            <Table.Content aria-label="Mentors roster table">
              <Table.Header>
                <Table.Column isRowHeader>Mentor</Table.Column>
                <Table.Column>Certificate & skill</Table.Column>
                <Table.Column>Session pricing</Table.Column>
                <Table.Column>Duration</Table.Column>
                <Table.Column>Meet room</Table.Column>
                <Table.Column>Rating</Table.Column>
                <Table.Column>Weekly schedule</Table.Column>
                <Table.Column>Completed</Table.Column>
                <Table.Column>Status</Table.Column>
                <Table.Column>Actions</Table.Column>
              </Table.Header>
              <Table.Body>
                {mentors?.map((m) => {
                  const safeMeetHref = safeExternalHref(m.meetLink);
                  const weeklyCount = m.weeklySlots?.length ?? 0;
                  const computedVnd = m.priceVnd ?? creditsToVnd(m.priceCredits);
                  return (
                    <Table.Row key={m.mentorId}>
                      <Table.Cell>
                        <div className="flex items-center gap-2.5">
                          <div className="p-2 rounded-full bg-accent/10 text-accent">
                            <User className="h-4 w-4" />
                          </div>
                          <div>
                            <p className="font-semibold text-foreground text-sm">{m.displayName}</p>
                            <p className="text-xs text-muted font-mono-tabular">Mentor #{m.mentorId}</p>
                          </div>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <div>
                          <p className="text-sm font-semibold text-foreground">{m.certCode}</p>
                          <p className="text-xs text-muted">{m.skillName || 'General coaching'}</p>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <div>
                          <p className="text-sm font-semibold font-mono-tabular text-foreground">
                            {m.priceCredits} {m.priceCredits === 1 ? 'credit' : 'credits'}
                          </p>
                          <p className="text-xs font-mono-tabular text-muted">
                            {formatVnd(computedVnd)}
                          </p>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs font-mono-tabular text-foreground">
                          {m.sessionMinutes} min
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        {safeMeetHref ? (
                          <a
                            href={safeMeetHref}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 text-xs font-medium text-accent hover:underline"
                          >
                            <Video className="h-3.5 w-3.5" />
                            Meet link
                            <ExternalLink className="h-3 w-3" />
                          </a>
                        ) : (
                          <span className="text-xs text-muted">—</span>
                        )}
                      </Table.Cell>
                      <Table.Cell>
                        <div className="flex items-center gap-1 text-xs">
                          <Star className="h-3.5 w-3.5 text-warning fill-warning" />
                          <span className="font-semibold font-mono-tabular">
                            {m.avgRating ? m.avgRating.toFixed(1) : '—'}
                          </span>
                          <span className="text-muted font-mono-tabular">({m.reviewCount})</span>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <span className="text-xs font-mono-tabular text-foreground">
                          {weeklyCount} {weeklyCount === 1 ? 'window' : 'windows'} / week
                        </span>
                      </Table.Cell>
                      <Table.Cell>
                        <div>
                          <p className="text-xs font-semibold font-mono-tabular text-foreground">
                            {m.completedSessionsCount ?? 0} sessions
                          </p>
                          <p className="text-xs font-mono-tabular text-muted">
                            {formatVnd(m.totalEarnedVnd ?? creditsToVnd(m.totalEarnedCredits ?? 0))}
                          </p>
                        </div>
                      </Table.Cell>
                      <Table.Cell>
                        <Chip size="sm" color={m.isActive ? 'success' : 'default'} variant="soft">
                          {m.isActive ? 'Active' : 'Inactive'}
                        </Chip>
                      </Table.Cell>
                      <Table.Cell>
                        <div className="flex items-center gap-2">
                          <Button size="sm" variant="secondary" onPress={() => openEdit(m)}>
                            <Edit2 className="h-3.5 w-3.5 mr-1" />
                            Edit
                          </Button>
                          <Button size="sm" variant="secondary" onPress={() => openScheduleModal(m)}>
                            <Calendar className="h-3.5 w-3.5 mr-1" />
                            Weekly schedule
                          </Button>
                        </div>
                      </Table.Cell>
                    </Table.Row>
                  );
                })}
              </Table.Body>
            </Table.Content>
          </Table.ScrollContainer>
        </Table>
      )}

      {/* Mentor Payouts & Completed Sessions Section */}
      <Card className="border border-border">
        <Card.Content className="p-6 space-y-6">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <h3 className="text-base font-semibold text-foreground">
                Mentor payouts & completed sessions
              </h3>
              <p className="text-xs text-muted mt-0.5">
                Track completed 1-on-1 coaching sessions and export UTF-8 BOM CSV payout sheets ({formatVnd(VND_PER_CREDIT)} per credit).
              </p>
            </div>

            <div className="flex flex-wrap items-end gap-3">
              <TextField className="w-40" name="payoutFrom">
                <Label className="text-xs">From date</Label>
                <Input
                  type="date"
                  value={payoutFrom}
                  onChange={(e) => {
                    setPayoutFrom(e.target.value);
                    setPayoutError(null);
                  }}
                />
              </TextField>

              <TextField className="w-40" name="payoutTo">
                <Label className="text-xs">To date</Label>
                <Input
                  type="date"
                  value={payoutTo}
                  onChange={(e) => {
                    setPayoutTo(e.target.value);
                    setPayoutError(null);
                  }}
                />
              </TextField>

              <Button
                size="sm"
                variant="secondary"
                isDisabled={isExportingCsv}
                onPress={() => void handleExportPayoutCsv()}
              >
                <Download className="h-4 w-4 mr-1.5" />
                {isExportingCsv ? 'Exporting...' : 'Export payout CSV'}
              </Button>
            </div>
          </div>

          {payoutError && <Alert status="danger">{payoutError}</Alert>}

          {/* Payout Summary Metrics */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <Card className="border border-border bg-surface-secondary/40">
              <Card.Content className="p-4 flex items-center justify-between">
                <div>
                  <p className="text-xs uppercase tracking-wider text-muted font-medium">
                    Completed sessions
                  </p>
                  <p className="text-2xl font-semibold font-mono-tabular text-foreground mt-1">
                    {payoutReport ? payoutReport.totalSessions : '—'}
                  </p>
                </div>
                <div className="p-2.5 rounded-full bg-success/10 text-success">
                  <CheckCircle2 className="h-5 w-5" />
                </div>
              </Card.Content>
            </Card>

            <Card className="border border-border bg-surface-secondary/40">
              <Card.Content className="p-4 flex items-center justify-between">
                <div>
                  <p className="text-xs uppercase tracking-wider text-muted font-medium">
                    Total credits earned
                  </p>
                  <p className="text-2xl font-semibold font-mono-tabular text-accent mt-1">
                    {payoutReport ? `${payoutReport.totalCredits} credits` : '—'}
                  </p>
                </div>
                <div className="p-2.5 rounded-full bg-accent/10 text-accent">
                  <Coins className="h-5 w-5" />
                </div>
              </Card.Content>
            </Card>

            <Card className="border border-border bg-surface-secondary/40">
              <Card.Content className="p-4 flex items-center justify-between">
                <div>
                  <p className="text-xs uppercase tracking-wider text-muted font-medium">
                    Total VND payout
                  </p>
                  <p className="text-2xl font-semibold font-mono-tabular text-success mt-1">
                    {payoutReport ? formatVnd(payoutReport.totalAmountVnd) : '—'}
                  </p>
                </div>
                <div className="p-2.5 rounded-full bg-success/10 text-success">
                  <Clock className="h-5 w-5" />
                </div>
              </Card.Content>
            </Card>
          </div>

          {isLoadingPayouts ? (
            <div className="flex justify-center py-8">
              <Spinner size="md" />
            </div>
          ) : (
            <>
              {/* Per-Mentor Payout Summary */}
              {payoutReport && payoutReport.mentors.length > 0 && (
                <div className="space-y-2">
                  <h4 className="text-xs font-semibold uppercase tracking-wider text-muted">
                    Per-mentor payout breakdown
                  </h4>
                  <Table>
                    <Table.ScrollContainer>
                      <Table.Content aria-label="Per-mentor payout summary table">
                        <Table.Header>
                          <Table.Column isRowHeader>Mentor</Table.Column>
                          <Table.Column>Certificate</Table.Column>
                          <Table.Column>Completed sessions</Table.Column>
                          <Table.Column>Total credits</Table.Column>
                          <Table.Column>Payout amount (VND)</Table.Column>
                        </Table.Header>
                        <Table.Body>
                          {payoutReport.mentors.map((ms) => (
                            <Table.Row key={ms.mentorId}>
                              <Table.Cell>
                                <span className="font-semibold text-sm text-foreground">
                                  {ms.mentorName}
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="text-xs font-medium text-foreground">
                                  {ms.certCode}
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="font-mono-tabular text-xs text-foreground">
                                  {ms.completedSessionsCount}
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="font-mono-tabular text-xs font-semibold text-foreground">
                                  {ms.totalCredits} credits
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="font-mono-tabular text-sm font-semibold text-success">
                                  {formatVnd(ms.totalAmountVnd)}
                                </span>
                              </Table.Cell>
                            </Table.Row>
                          ))}
                        </Table.Body>
                      </Table.Content>
                    </Table.ScrollContainer>
                  </Table>
                </div>
              )}

              {/* Completed Session Line Items */}
              <div className="space-y-2">
                <h4 className="text-xs font-semibold uppercase tracking-wider text-muted">
                  Completed session line items
                </h4>
                {!payoutReport || payoutReport.sessions.length === 0 ? (
                  <p className="text-xs text-muted py-4">
                    No completed mentor sessions in the selected date range.
                  </p>
                ) : (
                  <Table>
                    <Table.ScrollContainer>
                      <Table.Content aria-label="Completed mentor sessions payout table">
                        <Table.Header>
                          <Table.Column isRowHeader>Booking ID</Table.Column>
                          <Table.Column>Session window</Table.Column>
                          <Table.Column>Mentor</Table.Column>
                          <Table.Column>Learner</Table.Column>
                          <Table.Column>Certificate & skill</Table.Column>
                          <Table.Column>Credits</Table.Column>
                          <Table.Column>Amount (VND)</Table.Column>
                        </Table.Header>
                        <Table.Body>
                          {payoutReport.sessions.map((item) => (
                            <Table.Row key={item.bookingId}>
                              <Table.Cell>
                                <span className="font-mono-tabular font-semibold text-foreground">
                                  #{item.bookingId}
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <div>
                                  <p className="text-xs font-mono-tabular text-foreground">
                                    {formatDateTime(item.startAt)}
                                  </p>
                                  <p className="text-xs font-mono-tabular text-muted">
                                    to {formatDateTime(item.endAt)}
                                  </p>
                                </div>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="text-sm font-medium text-foreground">
                                  {item.mentorName}
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <div>
                                  <p className="text-sm font-medium text-foreground">
                                    {item.learnerName}
                                  </p>
                                  <p className="text-xs font-mono-tabular text-muted">
                                    {item.learnerEmail}
                                  </p>
                                </div>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="text-xs text-foreground">
                                  {item.certCode}
                                  {item.skillName ? ` · ${item.skillName}` : ''}
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="font-mono-tabular text-xs font-semibold text-foreground">
                                  {item.priceCredits} credits
                                </span>
                              </Table.Cell>
                              <Table.Cell>
                                <span className="font-mono-tabular text-sm font-semibold text-success">
                                  {formatVnd(item.amountVnd)}
                                </span>
                              </Table.Cell>
                            </Table.Row>
                          ))}
                        </Table.Body>
                      </Table.Content>
                    </Table.ScrollContainer>
                  </Table>
                )}
              </div>
            </>
          )}
        </Card.Content>
      </Card>

      {/* Create / Edit Mentor Modal */}
      {isFormOpen && (
        <Modal.Backdrop
          isOpen={isFormOpen}
          onOpenChange={(open) => {
            if (!open) setIsFormOpen(false);
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>
                  {editingMentorId !== null ? `Edit mentor — ${displayName}` : 'Create mentor profile'}
                </Modal.Heading>
                <p className="text-xs text-muted mt-1">
                  Human mentors conduct live 1-on-1 coaching via a fixed Google Meet room and are paid {formatVnd(VND_PER_CREDIT)} per credit.
                </p>
              </Modal.Header>

              <Modal.Body className="space-y-4">
                {apiError && <Alert status="danger">{apiError}</Alert>}

                <TextField
                  className="w-full"
                  name="displayName"
                  isRequired
                  isInvalid={!!formErrors.displayName}
                >
                  <Label htmlFor="mentor-display-name">Display name</Label>
                  <Input
                    id="mentor-display-name"
                    aria-label="Display name"
                    placeholder="e.g. Cô Lan Anh"
                    value={displayName}
                    onChange={(e) => {
                      setDisplayName(e.target.value);
                      if (formErrors.displayName) {
                        setFormErrors((prev) => ({ ...prev, displayName: '' }));
                      }
                    }}
                  />
                  {formErrors.displayName && (
                    <p className="text-xs text-danger mt-1">{formErrors.displayName}</p>
                  )}
                </TextField>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <Label htmlFor="mentor-certificate" className="text-sm font-medium">
                      Certificate
                    </Label>
                    <select
                      id="mentor-certificate"
                      aria-label="Certificate"
                      className="mt-1.5 w-full h-9 px-3 rounded-md border border-border bg-background text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-focus"
                      value={selectedCertId}
                      onChange={(e) => {
                        const newCertId = parseInt(e.target.value, 10);
                        setSelectedCertId(newCertId);
                        setSelectedSkillId(null);
                      }}
                    >
                      {certOptions?.map((c) => (
                        <option key={c.certId} value={c.certId}>
                          {c.code} — {c.name}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <Label htmlFor="mentor-skill-focus" className="text-sm font-medium">
                      Skill focus
                    </Label>
                    <select
                      id="mentor-skill-focus"
                      aria-label="Skill focus"
                      className="mt-1.5 w-full h-9 px-3 rounded-md border border-border bg-background text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-focus"
                      value={selectedSkillId ?? ''}
                      onChange={(e) => {
                        const val = e.target.value ? parseInt(e.target.value, 10) : null;
                        setSelectedSkillId(val);
                      }}
                    >
                      <option value="">General (All skills)</option>
                      {availableSkills.map((s) => (
                        <option key={s.skillId} value={s.skillId}>
                          {s.name} ({s.code})
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <TextField
                    className="w-full"
                    name="priceCredits"
                    isRequired
                    isInvalid={!!formErrors.priceCredits}
                  >
                    <Label htmlFor="mentor-price-credits">Price in credits</Label>
                    <Input
                      id="mentor-price-credits"
                      aria-label="Price in credits"
                      type="number"
                      min={1}
                      placeholder="3"
                      value={priceCredits}
                      onChange={(e) => {
                        setPriceCredits(e.target.value);
                        if (formErrors.priceCredits) {
                          setFormErrors((prev) => ({ ...prev, priceCredits: '' }));
                        }
                      }}
                    />
                    <p className="text-xs font-mono-tabular text-muted mt-1">
                      Equivalent to {formatVnd(liveVndEquivalent)} ({formatVnd(VND_PER_CREDIT)} / credit)
                    </p>
                    {formErrors.priceCredits && (
                      <p className="text-xs text-danger mt-1">{formErrors.priceCredits}</p>
                    )}
                  </TextField>

                  <TextField
                    className="w-full"
                    name="sessionMinutes"
                    isRequired
                    isInvalid={!!formErrors.sessionMinutes}
                  >
                    <Label htmlFor="mentor-session-minutes">Session duration (mins)</Label>
                    <Input
                      id="mentor-session-minutes"
                      aria-label="Session duration (mins)"
                      type="number"
                      min={15}
                      placeholder="60"
                      value={sessionMinutes}
                      onChange={(e) => {
                        setSessionMinutes(e.target.value);
                        if (formErrors.sessionMinutes) {
                          setFormErrors((prev) => ({ ...prev, sessionMinutes: '' }));
                        }
                      }}
                    />
                    {formErrors.sessionMinutes && (
                      <p className="text-xs text-danger mt-1">{formErrors.sessionMinutes}</p>
                    )}
                  </TextField>
                </div>

                <TextField
                  className="w-full"
                  name="meetLink"
                  isRequired
                  isInvalid={!!formErrors.meetLink}
                >
                  <Label htmlFor="mentor-meet-link">Meeting link (Google Meet URL)</Label>
                  <Input
                    id="mentor-meet-link"
                    aria-label="Meeting link (Google Meet URL)"
                    type="url"
                    placeholder="https://meet.google.com/xxx-yyyy-zzz"
                    value={meetLink}
                    onChange={(e) => {
                      setMeetLink(e.target.value);
                      if (formErrors.meetLink) {
                        setFormErrors((prev) => ({ ...prev, meetLink: '' }));
                      }
                    }}
                  />
                  {formErrors.meetLink && (
                    <p className="text-xs text-danger mt-1">{formErrors.meetLink}</p>
                  )}
                </TextField>

                <div className="grid grid-cols-2 gap-4">
                  <TextField className="w-full" name="headline">
                    <Label htmlFor="mentor-headline">Headline / Tagline (optional)</Label>
                    <Input
                      id="mentor-headline"
                      aria-label="Headline / Tagline (optional)"
                      placeholder="e.g. 8.5 IELTS Speaking Specialist"
                      value={headline}
                      onChange={(e) => setHeadline(e.target.value)}
                    />
                  </TextField>

                  <TextField className="w-full" name="expertise">
                    <Label htmlFor="mentor-expertise">Key expertise (optional)</Label>
                    <Input
                      id="mentor-expertise"
                      aria-label="Key expertise (optional)"
                      placeholder="e.g. Pronunciation, Part 2-3 fluency"
                      value={expertise}
                      onChange={(e) => setExpertise(e.target.value)}
                    />
                  </TextField>
                </div>

                <TextField className="w-full" name="bio">
                  <Label htmlFor="mentor-bio">Biography & introduction (optional)</Label>
                  <TextArea
                    id="mentor-bio"
                    aria-label="Biography & introduction (optional)"
                    rows={3}
                    placeholder="Mentor background and coaching style..."
                    value={bio}
                    onChange={(e) => setBio(e.target.value)}
                  />
                </TextField>

                <div className="flex items-center justify-between p-3 rounded-md border border-border">
                  <div>
                    <p className="text-sm font-medium text-foreground">Active for booking</p>
                    <p className="text-xs text-muted">Learners can book sessions with this mentor</p>
                  </div>
                  <Switch isSelected={isActive} onChange={setIsActive} />
                </div>
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setIsFormOpen(false)}>
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  isDisabled={saveMutation.isPending}
                  onPress={() => saveMutation.mutate()}
                >
                  {saveMutation.isPending ? 'Saving...' : 'Save mentor'}
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}

      {/* Weekly Schedule Management Modal */}
      {scheduleMentor && (
        <Modal.Backdrop
          isOpen={scheduleMentor !== null}
          onOpenChange={(open) => {
            if (!open) {
              setScheduleMentor(null);
              setScheduleError(null);
            }
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-2xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>Weekly schedule — {scheduleMentor.displayName}</Modal.Heading>
                <p className="text-xs text-muted mt-1">
                  Configure recurring weekly availability windows (Thứ 2 .. Chủ Nhật in UTC+7). Concrete 7-day booking slots materialize automatically.
                </p>
              </Modal.Header>

              <Modal.Body className="space-y-6">
                {scheduleError && <Alert status="danger">{scheduleError}</Alert>}

                {/* Add Weekly Window Card */}
                <Card className="border border-border">
                  <Card.Content className="p-4 space-y-3">
                    <h4 className="text-sm font-semibold text-foreground">
                      Add recurring weekly window
                    </h4>
                    <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                      <div>
                        <Label htmlFor="schedule-weekday" className="text-xs font-medium">
                          Weekday
                        </Label>
                        <select
                          id="schedule-weekday"
                          aria-label="Weekday"
                          className="mt-1 w-full h-9 px-3 rounded-md border border-border bg-background text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-focus"
                          value={newDayOfWeek}
                          onChange={(e) => setNewDayOfWeek(parseInt(e.target.value, 10))}
                        >
                          {WEEKDAY_OPTIONS.map((opt) => (
                            <option key={opt.dayOfWeek} value={opt.dayOfWeek}>
                              {opt.label}
                            </option>
                          ))}
                        </select>
                      </div>

                      <TextField className="w-full" name="windowStart">
                        <Label htmlFor="schedule-window-start" className="text-xs">
                          Window start (UTC+7)
                        </Label>
                        <Input
                          id="schedule-window-start"
                          aria-label="Window start"
                          type="time"
                          value={newStartTime}
                          onChange={(e) => setNewStartTime(e.target.value)}
                        />
                      </TextField>

                      <TextField className="w-full" name="windowEnd">
                        <Label htmlFor="schedule-window-end" className="text-xs">
                          Window end (UTC+7)
                        </Label>
                        <Input
                          id="schedule-window-end"
                          aria-label="Window end"
                          type="time"
                          value={newEndTime}
                          onChange={(e) => setNewEndTime(e.target.value)}
                        />
                      </TextField>
                    </div>

                    <div className="flex justify-end pt-1">
                      <Button size="sm" variant="secondary" onPress={handleAddScheduleWindow}>
                        <Plus className="h-4 w-4 mr-1" />
                        Add window
                      </Button>
                    </div>
                  </Card.Content>
                </Card>

                {/* Configured Weekly Windows */}
                <div>
                  <h4 className="text-sm font-semibold text-foreground mb-3">
                    Configured weekly windows ({scheduleWindows.length})
                  </h4>
                  {isLoadingSchedule ? (
                    <div className="flex justify-center py-6">
                      <Spinner size="md" />
                    </div>
                  ) : scheduleWindows.length === 0 ? (
                    <p className="text-xs text-muted italic">
                      No recurring weekly windows configured yet.
                    </p>
                  ) : (
                    <div className="space-y-2">
                      {scheduleWindows.map((w, idx) => (
                        <div
                          key={`${w.dayOfWeek}-${w.startTime}-${w.endTime}-${idx}`}
                          className="flex items-center justify-between rounded-md border border-border bg-surface px-3.5 py-2.5"
                        >
                          <div className="flex items-center gap-3">
                            <Chip size="sm" color="accent" variant="soft">
                              {formatWeekdayLabel(w.dayOfWeek)}
                            </Chip>
                            <span className="font-mono-tabular text-sm font-semibold text-foreground">
                              {normalizeWeeklyTime(w.startTime)} – {normalizeWeeklyTime(w.endTime)}
                            </span>
                            <span className="text-xs text-muted">UTC+7</span>
                          </div>
                          <Button
                            size="sm"
                            variant="danger"
                            onPress={() => handleRemoveScheduleWindow(idx)}
                          >
                            <Trash2 className="h-3.5 w-3.5 mr-1" />
                            Remove
                          </Button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setScheduleMentor(null)}>
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  isDisabled={saveWeeklyScheduleMutation.isPending}
                  onPress={() => saveWeeklyScheduleMutation.mutate()}
                >
                  {saveWeeklyScheduleMutation.isPending ? 'Saving...' : 'Save weekly schedule'}
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}
    </div>
  );
}
