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
  Switch,
  Table,
  TextArea,
  TextField,
} from '@heroui/react';
import {
  Bot,
  Calendar,
  CheckCircle2,
  Clock,
  Edit2,
  Plus,
  RefreshCw,
  Star,
  User,
  Users,
  XCircle,
} from 'lucide-react';
import {
  api,
  type AdminMentorListItemDto,
  type AdminMentorSlotDto,
  type MentorCertificateOptionDto,
  type MentorTypeDto,
} from '../../../lib/api';
import { formatVnd } from '../../../lib/security';
import {
  formatDateTime,
  formatMentorSlotStatusLabel,
  formatMentorTypeLabel,
  getSlotStatusColor,
} from './opsHelpers';

export function MentorsTab() {
  const queryClient = useQueryClient();

  // Create / Edit mentor state
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingMentorId, setEditingMentorId] = useState<number | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [mentorType, setMentorType] = useState<MentorTypeDto>('human');
  const [userId, setUserId] = useState('');
  const [selectedCertId, setSelectedCertId] = useState<number>(0);
  const [selectedSkillId, setSelectedSkillId] = useState<number | null>(null);
  const [pricePerSlot, setPricePerSlot] = useState('250000');
  const [priceCredits, setPriceCredits] = useState('2');
  const [sessionMinutes, setSessionMinutes] = useState('60');
  const [headline, setHeadline] = useState('');
  const [bio, setBio] = useState('');
  const [expertise, setExpertise] = useState('');
  const [isActive, setIsActive] = useState(true);
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [apiError, setApiError] = useState<string | null>(null);

  // Slots management modal state
  const [slotsMentor, setSlotsMentor] = useState<AdminMentorListItemDto | null>(null);
  const [newSlotStart, setNewSlotStart] = useState('');
  const [newSlotEnd, setNewSlotEnd] = useState('');
  const [slotActionError, setSlotActionError] = useState<string | null>(null);

  const { data: mentors, isLoading, error } = useQuery({
    queryKey: ['admin-mentors'],
    queryFn: () => api.admin.mentors(),
  });

  const { data: certOptions } = useQuery({
    queryKey: ['admin-mentor-options'],
    queryFn: () => api.admin.mentorOptions(),
  });

  const { data: slots, isLoading: isLoadingSlots } = useQuery({
    queryKey: ['admin-mentor-slots', slotsMentor?.mentorId],
    queryFn: () => api.admin.mentorSlots(slotsMentor!.mentorId),
    enabled: slotsMentor !== null,
  });

  const saveMutation = useMutation({
    mutationFn: () => {
      const errors: Record<string, string> = {};
      if (!displayName.trim()) {
        errors.displayName = 'Display name is required.';
      }

      const isHuman = mentorType === 'human';
      const parsedUserId = userId.trim() ? parseInt(userId, 10) : null;
      if (isHuman && (!parsedUserId || parsedUserId <= 0)) {
        errors.userId = 'User account ID is required for human mentors.';
      }

      if (!selectedCertId) {
        errors.certId = 'Please select a certificate.';
      }

      const pSlot = parseFloat(pricePerSlot);
      if (isNaN(pSlot) || pSlot < 0) {
        errors.pricePerSlot = 'Price must be non-negative.';
      }

      const pCredits = parseInt(priceCredits, 10);
      if (isNaN(pCredits) || pCredits < 0) {
        errors.priceCredits = 'Credits must be non-negative.';
      }

      const sMins = parseInt(sessionMinutes, 10);
      if (isNaN(sMins) || sMins <= 0) {
        errors.sessionMinutes = 'Duration must be greater than 0 minutes.';
      }

      if (Object.keys(errors).length > 0) {
        setFormErrors(errors);
        throw new Error('Please fix the form errors.');
      }

      const payload = {
        userId: isHuman ? parsedUserId : null,
        displayName: displayName.trim(),
        bio: bio.trim() || null,
        certId: selectedCertId,
        skillId: selectedSkillId || null,
        pricePerSlot: pSlot,
        priceCredits: pCredits,
        headline: headline.trim() || null,
        expertise: expertise.trim() || null,
        sessionMinutes: sMins,
        isActive,
      };

      if (editingMentorId !== null) {
        return api.admin.updateMentor(editingMentorId, payload);
      } else {
        return api.admin.createMentor({
          ...payload,
          type: mentorType,
        });
      }
    },
    onSuccess: () => {
      setIsFormOpen(false);
      resetForm();
      void queryClient.invalidateQueries({ queryKey: ['admin-mentors'] });
    },
    onError: (err: any) => {
      setApiError(err?.message || 'Could not save mentor profile.');
    },
  });

  const createSlotMutation = useMutation({
    mutationFn: () => {
      if (!newSlotStart) {
        throw new Error('Start time is required.');
      }
      return api.admin.createMentorSlot(slotsMentor!.mentorId, {
        localStartAt: newSlotStart,
        localEndAt: newSlotEnd ? newSlotEnd : null,
      });
    },
    onSuccess: () => {
      setNewSlotStart('');
      setNewSlotEnd('');
      setSlotActionError(null);
      void queryClient.invalidateQueries({ queryKey: ['admin-mentor-slots', slotsMentor?.mentorId] });
      void queryClient.invalidateQueries({ queryKey: ['admin-mentors'] });
    },
    onError: (err: any) => {
      setSlotActionError(err?.message || 'Could not schedule slot.');
    },
  });

  const closeSlotMutation = useMutation({
    mutationFn: (slotId: number) =>
      api.admin.closeMentorSlot(slotsMentor!.mentorId, slotId),
    onSuccess: () => {
      setSlotActionError(null);
      void queryClient.invalidateQueries({ queryKey: ['admin-mentor-slots', slotsMentor?.mentorId] });
      void queryClient.invalidateQueries({ queryKey: ['admin-mentors'] });
    },
    onError: (err: any) => {
      setSlotActionError(err?.message || 'Could not close slot.');
    },
  });

  function resetForm() {
    setEditingMentorId(null);
    setDisplayName('');
    setMentorType('human');
    setUserId('');
    setSelectedCertId(certOptions?.[0]?.certId || 1);
    setSelectedSkillId(null);
    setPricePerSlot('250000');
    setPriceCredits('2');
    setSessionMinutes('60');
    setHeadline('');
    setBio('');
    setExpertise('');
    setIsActive(true);
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
    setMentorType(m.type);
    setUserId(m.userId ? String(m.userId) : '');
    setSelectedCertId(m.certId);
    setSelectedSkillId(m.skillId || null);
    setPricePerSlot(String(m.pricePerSlot));
    setPriceCredits(String(m.priceCredits));
    setSessionMinutes(String(m.sessionMinutes));
    setHeadline('');
    setBio('');
    setExpertise('');
    setIsActive(m.isActive);
    setFormErrors({});
    setApiError(null);
    setIsFormOpen(true);
  }

  const selectedCert = certOptions?.find((c) => c.certId === selectedCertId);
  const availableSkills = selectedCert?.skills || [];

  return (
    <div className="space-y-6">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h3 className="text-base font-semibold text-foreground">Mentor roster & coaching</h3>
          <p className="text-xs text-muted">
            Manage human instructors, AI conversational coaches, and appointment calendars.
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
                <Table.Column>Type</Table.Column>
                <Table.Column>Certificate & skill</Table.Column>
                <Table.Column>Session pricing</Table.Column>
                <Table.Column>Duration</Table.Column>
                <Table.Column>Rating</Table.Column>
                <Table.Column>Slots</Table.Column>
                <Table.Column>Status</Table.Column>
                <Table.Column>Actions</Table.Column>
              </Table.Header>
              <Table.Body>
                {mentors?.map((m) => (
                  <Table.Row key={m.mentorId}>
                    <Table.Cell>
                      <div className="flex items-center gap-2.5">
                        <div
                          className={`p-2 rounded-full ${
                            m.type === 'ai' ? 'bg-accent/10 text-accent' : 'bg-primary/10 text-primary'
                          }`}
                        >
                          {m.type === 'ai' ? <Bot className="h-4 w-4" /> : <User className="h-4 w-4" />}
                        </div>
                        <div>
                          <p className="font-semibold text-foreground text-sm">{m.displayName}</p>
                          {m.userId && (
                            <p className="text-xs text-muted font-mono-tabular">User #{m.userId}</p>
                          )}
                        </div>
                      </div>
                    </Table.Cell>
                    <Table.Cell>
                      <span className="text-xs text-foreground font-medium">
                        {formatMentorTypeLabel(m.type)}
                      </span>
                    </Table.Cell>
                    <Table.Cell>
                      <div>
                        <p className="text-sm font-semibold text-foreground">{m.certCode}</p>
                        <p className="text-xs text-muted">{m.skillName || 'General coaching'}</p>
                      </div>
                    </Table.Cell>
                    <Table.Cell>
                      <div>
                        <p className="text-sm font-mono-tabular text-foreground">
                          {formatVnd(m.pricePerSlot)}
                        </p>
                        <p className="text-xs font-mono-tabular text-muted">
                          {m.priceCredits} credits
                        </p>
                      </div>
                    </Table.Cell>
                    <Table.Cell>
                      <span className="text-xs font-mono-tabular text-foreground">
                        {m.sessionMinutes} min
                      </span>
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
                        {m.type === 'ai'
                          ? 'On demand'
                          : `${m.openSlotsCount} open / ${m.totalSlotsCount} total`}
                      </span>
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
                        {m.type !== 'ai' && (
                          <Button size="sm" variant="secondary" onPress={() => setSlotsMentor(m)}>
                            <Calendar className="h-3.5 w-3.5 mr-1" />
                            Manage slots
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
      )}

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
                  Human mentors conduct live 1-on-1 sessions. AI coaches provide automated practice.
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
                  <Label>Display name</Label>
                  <Input
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
                    <Label className="text-sm font-medium">Mentor type</Label>
                    <select
                      disabled={editingMentorId !== null}
                      className="mt-1.5 w-full h-9 px-3 rounded-md border border-border bg-background text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-focus disabled:opacity-50"
                      value={mentorType}
                      onChange={(e) => setMentorType(e.target.value as MentorTypeDto)}
                    >
                      <option value="human">Human mentor</option>
                      <option value="ai">AI coach</option>
                    </select>
                  </div>

                  {mentorType === 'human' && (
                    <TextField
                      className="w-full"
                      name="userId"
                      isRequired
                      isInvalid={!!formErrors.userId}
                    >
                      <Label>Associated user ID</Label>
                      <Input
                        type="number"
                        placeholder="e.g. 25"
                        value={userId}
                        onChange={(e) => {
                          setUserId(e.target.value);
                          if (formErrors.userId) {
                            setFormErrors((prev) => ({ ...prev, userId: '' }));
                          }
                        }}
                      />
                      {formErrors.userId && (
                        <p className="text-xs text-danger mt-1">{formErrors.userId}</p>
                      )}
                    </TextField>
                  )}
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <Label className="text-sm font-medium">Certificate</Label>
                    <select
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
                    <Label className="text-sm font-medium">Skill focus</Label>
                    <select
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

                <div className="grid grid-cols-3 gap-3">
                  <TextField className="w-full" name="pricePerSlot" isRequired>
                    <Label>Price per session (VND)</Label>
                    <Input
                      type="number"
                      placeholder="250000"
                      value={pricePerSlot}
                      onChange={(e) => setPricePerSlot(e.target.value)}
                    />
                  </TextField>

                  <TextField className="w-full" name="priceCredits" isRequired>
                    <Label>Price in credits</Label>
                    <Input
                      type="number"
                      placeholder="2"
                      value={priceCredits}
                      onChange={(e) => setPriceCredits(e.target.value)}
                    />
                  </TextField>

                  <TextField className="w-full" name="sessionMinutes" isRequired>
                    <Label>Session duration (mins)</Label>
                    <Input
                      type="number"
                      placeholder="60"
                      value={sessionMinutes}
                      onChange={(e) => setSessionMinutes(e.target.value)}
                    />
                  </TextField>
                </div>

                <TextField className="w-full" name="headline">
                  <Label>Headline / Tagline (optional)</Label>
                  <Input
                    placeholder="e.g. 8.5 IELTS Speaking Specialist"
                    value={headline}
                    onChange={(e) => setHeadline(e.target.value)}
                  />
                </TextField>

                <TextField className="w-full" name="bio">
                  <Label>Biography & introduction (optional)</Label>
                  <TextArea
                    rows={3}
                    placeholder="Instructor background and teaching style..."
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

      {/* Slots Management Modal */}
      {slotsMentor && (
        <Modal.Backdrop
          isOpen={slotsMentor !== null}
          onOpenChange={(open) => {
            if (!open) {
              setSlotsMentor(null);
              setSlotActionError(null);
            }
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-2xl">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>Schedule slots — {slotsMentor.displayName}</Modal.Heading>
                <p className="text-xs text-muted mt-1">
                  Manage availability slots for learners to book 1-on-1 coaching sessions.
                </p>
              </Modal.Header>

              <Modal.Body className="space-y-6">
                {slotActionError && <Alert status="danger">{slotActionError}</Alert>}

                {/* Add Slot Card */}
                <Card className="border border-border">
                  <Card.Content className="p-4 space-y-3">
                    <h4 className="text-sm font-semibold text-foreground">Add new appointment slot</h4>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                      <TextField className="w-full" name="slotStart" isRequired>
                        <Label>Start time</Label>
                        <Input
                          type="datetime-local"
                          value={newSlotStart}
                          onChange={(e) => setNewSlotStart(e.target.value)}
                        />
                      </TextField>

                      <TextField className="w-full" name="slotEnd">
                        <Label>End time (optional)</Label>
                        <Input
                          type="datetime-local"
                          value={newSlotEnd}
                          onChange={(e) => setNewSlotEnd(e.target.value)}
                        />
                      </TextField>
                    </div>

                    <div className="flex justify-end pt-1">
                      <Button
                        size="sm"
                        variant="primary"
                        isDisabled={createSlotMutation.isPending || !newSlotStart}
                        onPress={() => createSlotMutation.mutate()}
                      >
                        <Plus className="h-4 w-4 mr-1" />
                        {createSlotMutation.isPending ? 'Scheduling...' : 'Add slot'}
                      </Button>
                    </div>
                  </Card.Content>
                </Card>

                {/* Slots Table */}
                <div>
                  <h4 className="text-sm font-semibold text-foreground mb-3">Existing slots</h4>
                  {isLoadingSlots ? (
                    <div className="flex justify-center py-6">
                      <Spinner size="md" />
                    </div>
                  ) : slots?.length === 0 ? (
                    <p className="text-xs text-muted italic">No slots scheduled yet.</p>
                  ) : (
                    <Table>
                      <Table.ScrollContainer>
                        <Table.Content aria-label="Mentor slots schedule">
                          <Table.Header>
                            <Table.Column isRowHeader>Slot ID</Table.Column>
                            <Table.Column>Start time</Table.Column>
                            <Table.Column>End time</Table.Column>
                            <Table.Column>Status</Table.Column>
                            <Table.Column>Action</Table.Column>
                          </Table.Header>
                          <Table.Body>
                            {slots?.map((s) => (
                              <Table.Row key={s.slotId}>
                                <Table.Cell>
                                  <span className="font-mono-tabular font-medium text-foreground">
                                    #{s.slotId}
                                  </span>
                                </Table.Cell>
                                <Table.Cell>
                                  <span className="font-mono-tabular text-xs">
                                    {formatDateTime(s.startAt)}
                                  </span>
                                </Table.Cell>
                                <Table.Cell>
                                  <span className="font-mono-tabular text-xs">
                                    {formatDateTime(s.endAt)}
                                  </span>
                                </Table.Cell>
                                <Table.Cell>
                                  <Chip
                                    size="sm"
                                    color={getSlotStatusColor(s.status)}
                                    variant="soft"
                                  >
                                    {formatMentorSlotStatusLabel(s.status)}
                                  </Chip>
                                </Table.Cell>
                                <Table.Cell>
                                  {s.status === 'open' && (
                                    <Button
                                      size="sm"
                                      variant="danger"
                                      isDisabled={closeSlotMutation.isPending}
                                      onPress={() => closeSlotMutation.mutate(s.slotId)}
                                    >
                                      Close slot
                                    </Button>
                                  )}
                                </Table.Cell>
                              </Table.Row>
                            ))}
                          </Table.Body>
                        </Table.Content>
                      </Table.ScrollContainer>
                    </Table>
                  )}
                </div>
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setSlotsMentor(null)}>
                  Done
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}
    </div>
  );
}
