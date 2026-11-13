import React, { useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Spinner,
} from '@heroui/react';
import {
  AlertCircle,
  ArrowLeft,
  Calendar,
  CheckCircle2,
  Clock,
  CreditCard,
  GraduationCap,
  MessageSquare,
  ShieldAlert,
  Sparkles,
  Star,
  User,
  Wallet,
} from 'lucide-react';
import { api, ApiClientError } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { formatVnd, resolveSafeReturnUrl } from '../../lib/security';
import { formatDateTime, formatMentorType } from '../../lib/domain-labels';

export function MentorDetailPage() {
  const params = useParams({ strict: false }) as { mentorId?: string };
  const mentorId = Number(params?.mentorId || 0);

  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { learnerContext } = useAuth();

  const [selectedSlotId, setSelectedSlotId] = useState<number | null>(null);
  const [focusArea, setFocusArea] = useState<string>('');
  const [bookError, setBookError] = useState<string | null>(null);

  const mentorQuery = useQuery({
    queryKey: ['mentors', 'detail', mentorId],
    queryFn: () => api.mentors.detail(mentorId),
    enabled: mentorId > 0,
    staleTime: 30_000,
    retry: false,
  });

  const bookMutation = useMutation({
    mutationFn: () =>
      api.mentors.book(mentorId, {
        slotId: selectedSlotId,
        focusArea: focusArea.trim() || undefined,
      }),
    onSuccess: (booking) => {
      void queryClient.invalidateQueries({ queryKey: ['learner', 'context'] });
      void queryClient.invalidateQueries({ queryKey: ['mentors', 'bookings'] });

      if (booking.mentorType === 'ai' && booking.redirectUrl) {
        const safeUrl = resolveSafeReturnUrl(
          booking.redirectUrl,
          `/mentors/bookings/${booking.bookingId}`,
        );
        window.location.assign(safeUrl);
      } else {
        void navigate({ to: `/mentors/bookings/${booking.bookingId}` });
      }
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setBookError(err.title || err.detail || 'Failed to book session.');
      } else {
        setBookError((err as Error).message || 'Booking request failed.');
      }
    },
  });

  if (mentorId <= 0) {
    return (
      <div className="max-w-3xl mx-auto py-12 px-4">
        <Alert status="danger">
          <Alert.Title>Invalid mentor identifier</Alert.Title>
        </Alert>
      </div>
    );
  }

  if (mentorQuery.isLoading) {
    return (
      <div className="max-w-4xl mx-auto py-16 px-4">
        <Card>
          <Card.Content className="flex items-center justify-center py-12 gap-3 text-muted text-sm">
            <Spinner size="sm" />
            <span>Loading mentor profile & available schedule...</span>
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (mentorQuery.isError || !mentorQuery.data) {
    return (
      <div className="max-w-3xl mx-auto py-12 px-4 flex flex-col gap-6">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Mentor not found</Alert.Title>
            <Alert.Description>
              We could not find an active instructor profile for ID #{mentorId}.
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <div>
          <Link to="/mentors" className="no-underline">
            <Button variant="secondary">
              <ArrowLeft className="w-4 h-4 mr-1.5" />
              Back to mentors catalog
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  const { mentor, availableSlots, reviews } = mentorQuery.data;
  const isAi = (mentor.type || '').toLowerCase() === 'ai';

  const userCredits = learnerContext?.creditBalance ?? 0;
  const hasInsufficientCredits = userCredits < mentor.priceCredits;

  const openSlots = availableSlots.filter((s) => (s.status || '').toLowerCase() === 'open');

  return (
    <div className="max-w-5xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-8">
      {/* Top back button */}
      <div>
        <Link to="/mentors" className="no-underline text-muted hover:text-foreground text-sm flex items-center gap-1.5">
          <ArrowLeft className="w-4 h-4" />
          Back to mentors
        </Link>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* Left 2 Cols: Mentor Details, Bio, Reviews */}
        <div className="lg:col-span-2 space-y-6">
          <Card className="shadow-surface">
            <Card.Header className="space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-4">
                <div>
                  <div className="flex items-center gap-2 mb-1">
                    <Chip size="sm" color={isAi ? 'accent' : 'default'} variant="soft">
                      {formatMentorType(mentor.type)}
                    </Chip>
                    <Chip size="sm" variant="secondary" className="font-mono-tabular">
                      {mentor.certCode}
                    </Chip>
                  </div>
                  <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
                    {mentor.displayName}
                  </h1>
                  <p className="text-sm font-medium text-accent mt-1 mb-0">
                    {mentor.headline || (isAi ? 'Autonomous AI Coaching Agent' : 'Certified Mentor')}
                  </p>
                </div>

                <div className="flex items-center gap-2 bg-surface-secondary px-3 py-2 rounded-lg border border-border">
                  <Star className="w-4 h-4 text-warning fill-current" />
                  <span className="font-mono-tabular font-bold text-foreground">
                    {mentor.avgRating ? mentor.avgRating.toFixed(1) : 'New'}
                  </span>
                  <span className="text-xs text-muted">
                    ({mentor.reviewCount} {mentor.reviewCount === 1 ? 'review' : 'reviews'})
                  </span>
                </div>
              </div>
            </Card.Header>

            <Card.Content className="space-y-6">
              <div>
                <h3 className="text-sm font-bold uppercase tracking-wider text-muted mb-2">
                  About mentor
                </h3>
                <p className="text-sm text-foreground whitespace-pre-line leading-relaxed m-0">
                  {mentor.bio || 'Experienced educator specialized in exam performance and targeted score recovery.'}
                </p>
              </div>

              {mentor.expertise && (
                <div>
                  <h3 className="text-sm font-bold uppercase tracking-wider text-muted mb-2">
                    Key expertise & focus areas
                  </h3>
                  <p className="text-sm text-foreground m-0">{mentor.expertise}</p>
                </div>
              )}
            </Card.Content>
          </Card>

          {/* Reviews section */}
          <Card className="shadow-surface">
            <Card.Header>
              <div className="flex items-center justify-between">
                <Card.Title className="text-lg font-bold">Learner reviews</Card.Title>
                <span className="text-xs text-muted">
                  {reviews.length} {reviews.length === 1 ? 'verified review' : 'verified reviews'}
                </span>
              </div>
            </Card.Header>

            <Card.Content className="space-y-4">
              {reviews.length === 0 ? (
                <p className="text-sm text-muted m-0">No learner reviews published yet.</p>
              ) : (
                <div className="space-y-4">
                  {reviews.map((rev) => (
                    <div
                      key={rev.reviewId}
                      className="p-4 rounded-lg bg-surface-secondary border border-border space-y-2"
                    >
                      <div className="flex items-center justify-between text-xs">
                        <span className="font-semibold text-foreground">{rev.userDisplayName}</span>
                        <div className="flex items-center gap-1 text-warning">
                          {Array.from({ length: 5 }).map((_, i) => (
                            <Star
                              key={i}
                              className={`w-3.5 h-3.5 ${
                                i < rev.rating ? 'fill-current' : 'text-muted opacity-30'
                              }`}
                            />
                          ))}
                        </div>
                      </div>
                      {rev.text && (
                        <p className="text-sm text-foreground m-0 leading-normal">{rev.text}</p>
                      )}
                      <span className="text-xs text-muted font-mono-tabular block">
                        {formatDateTime(rev.createdAt)}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </Card.Content>
          </Card>
        </div>

        {/* Right 1 Col: Booking & Paywall Card */}
        <div className="space-y-6">
          <Card className="shadow-surface border-2 border-accent">
            <Card.Header>
              <Card.Title className="text-xl font-bold">Book diagnostic session</Card.Title>
              <Card.Description>
                Duration: <span className="font-mono-tabular font-semibold text-foreground">{mentor.sessionMinutes} minutes</span>
              </Card.Description>
            </Card.Header>

            <Card.Content className="space-y-5">
              <div className="p-3.5 rounded-lg bg-surface-secondary border border-border flex items-center justify-between">
                <span className="text-sm text-muted">Session cost:</span>
                <div className="text-right">
                  <span className="font-mono-tabular font-bold text-accent text-lg block">
                    {mentor.priceCredits} credits
                  </span>
                  <span className="font-mono-tabular text-xs text-muted">
                    ({formatVnd(mentor.pricePerSlot)})
                  </span>
                </div>
              </div>

              {/* Block-before-charge Paywall Check */}
              {hasInsufficientCredits ? (
                <Alert status="warning">
                  <Alert.Indicator>
                    <ShieldAlert className="w-5 h-5 text-warning" />
                  </Alert.Indicator>
                  <Alert.Content>
                    <Alert.Title>Insufficient credits</Alert.Title>
                    <Alert.Description className="space-y-2">
                      <p className="m-0 text-xs">
                        You have <strong className="font-mono-tabular">{userCredits}</strong> credits, but this session requires <strong className="font-mono-tabular">{mentor.priceCredits}</strong> credits.
                      </p>
                      <div className="pt-1">
                        <Link to="/wallet" className="no-underline block">
                          <Button size="sm" variant="primary" className="w-full justify-center">
                            <Sparkles className="w-3.5 h-3.5 mr-1 text-warning" />
                            Buy credits & top up
                          </Button>
                        </Link>
                      </div>
                    </Alert.Description>
                  </Alert.Content>
                </Alert>
              ) : (
                <div className="space-y-4">
                  {bookError && (
                    <Alert status="danger">
                      <Alert.Title>Booking failed</Alert.Title>
                      <Alert.Description>{bookError}</Alert.Description>
                    </Alert>
                  )}

                  {/* Slot selection (for human mentors) */}
                  {!isAi && (
                    <div className="space-y-2">
                      <label className="text-xs font-semibold text-foreground uppercase tracking-wider block">
                        Available schedule slots:
                      </label>
                      {openSlots.length === 0 ? (
                        <p className="text-xs text-muted m-0 p-3 bg-surface-secondary rounded border border-border">
                          No open slots currently available. Please check back later.
                        </p>
                      ) : (
                        <div className="space-y-2 max-h-56 overflow-y-auto">
                          {openSlots.map((slot) => {
                            const isSelected = selectedSlotId === slot.slotId;
                            return (
                              <button
                                key={slot.slotId}
                                type="button"
                                onClick={() => setSelectedSlotId(slot.slotId)}
                                className={`w-full text-left p-2.5 rounded-md border text-xs transition-colors flex items-center justify-between ${
                                  isSelected
                                    ? 'bg-accent/10 border-accent font-semibold text-accent'
                                    : 'bg-surface hover:bg-surface-secondary border-border text-foreground'
                                }`}
                              >
                                <span className="font-mono-tabular">
                                  {formatDateTime(slot.startAt)}
                                </span>
                                {isSelected && <CheckCircle2 className="w-4 h-4 text-accent" />}
                              </button>
                            );
                          })}
                        </div>
                      )}
                    </div>
                  )}

                  {/* Focus area input */}
                  <div className="space-y-1.5">
                    <label htmlFor="focus-area" className="text-xs font-semibold text-foreground block">
                      Target focus or questions (optional):
                    </label>
                    <textarea
                      id="focus-area"
                      rows={3}
                      value={focusArea}
                      onChange={(e) => setFocusArea(e.target.value)}
                      placeholder="e.g. Weakness in TOEIC Part 3 audio dialogues..."
                      className="w-full text-xs p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                    />
                  </div>

                  <p className="text-xs text-muted m-0">
                    Free cancellation up to 24 hours before the session.
                  </p>

                  <Button
                    variant="primary"
                    size="lg"
                    className="w-full justify-center"
                    isDisabled={!isAi && (!selectedSlotId || openSlots.length === 0)}
                    isPending={bookMutation.isPending}
                    onPress={() => bookMutation.mutate()}
                  >
                    Confirm & book ({mentor.priceCredits} credits)
                  </Button>
                </div>
              )}
            </Card.Content>
          </Card>
        </div>
      </div>
    </div>
  );
}
