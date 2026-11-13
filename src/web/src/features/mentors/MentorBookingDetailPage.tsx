import React, { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
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
  ExternalLink,
  GraduationCap,
  MessageSquare,
  ShieldAlert,
  Sparkles,
  Star,
  User,
  Video,
  XCircle,
} from 'lucide-react';
import { api, ApiClientError } from '../../lib/api';
import { safeExternalHref } from '../../lib/security';
import {
  formatBookingStatus,
  formatDateTime,
  formatMentorType,
} from '../../lib/domain-labels';
import { MentorStandingCard } from './MentorStandingCard';

export function MentorBookingDetailPage() {
  const params = useParams({ strict: false }) as { bookingId?: string };
  const bookingId = Number(params?.bookingId || 0);

  const queryClient = useQueryClient();

  const [rating, setRating] = useState<number>(5);
  const [reviewText, setReviewText] = useState<string>('');
  const [actionError, setActionError] = useState<string | null>(null);
  const [refundNotice, setRefundNotice] = useState<string | null>(null);

  const bookingQuery = useQuery({
    queryKey: ['mentors', 'bookings', bookingId],
    queryFn: () => api.mentors.bookingDetail(bookingId),
    enabled: bookingId > 0,
    staleTime: 15_000,
    retry: false,
  });

  const cancelMutation = useMutation({
    mutationFn: () => api.mentors.cancelBooking(bookingId),
    onSuccess: (res) => {
      setActionError(null);
      setRefundNotice(`Booking cancelled: ${res.refundedCredits} credits refunded to your wallet.`);
      void queryClient.invalidateQueries({ queryKey: ['mentors', 'bookings'] });
      void queryClient.invalidateQueries({ queryKey: ['learner', 'context'] });
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setActionError(err.title || err.detail || 'Could not cancel booking.');
      } else {
        setActionError((err as Error).message || 'Failed to cancel session.');
      }
    },
  });

  const rateMutation = useMutation({
    mutationFn: () =>
      api.mentors.rateBooking(bookingId, {
        rating,
        text: reviewText.trim() || undefined,
      }),
    onSuccess: () => {
      setActionError(null);
      void queryClient.invalidateQueries({ queryKey: ['mentors', 'bookings', bookingId] });
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setActionError(err.title || err.detail || 'Could not submit review.');
      } else {
        setActionError((err as Error).message || 'Failed to submit review.');
      }
    },
  });

  if (bookingId <= 0) {
    return (
      <div className="max-w-3xl mx-auto py-12 px-4">
        <Alert status="danger">
          <Alert.Title>Invalid booking identifier</Alert.Title>
        </Alert>
      </div>
    );
  }

  if (bookingQuery.isLoading) {
    return (
      <div className="max-w-4xl mx-auto py-16 px-4">
        <Card>
          <Card.Content className="flex items-center justify-center py-12 gap-3 text-muted text-sm">
            <Spinner size="sm" />
            <span>Loading booking details...</span>
          </Card.Content>
        </Card>
      </div>
    );
  }

  if (bookingQuery.isError || !bookingQuery.data) {
    return (
      <div className="max-w-3xl mx-auto py-12 px-4 flex flex-col gap-6">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Booking not found</Alert.Title>
            <Alert.Description>
              We could not find booking #{bookingId}. It may have been deleted or does not belong to your account.
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <div>
          <Link to="/mentors/bookings" className="no-underline">
            <Button variant="secondary">
              <ArrowLeft className="w-4 h-4 mr-1.5" />
              Back to bookings
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  const booking = bookingQuery.data;
  const statusDisplay = formatBookingStatus(booking.status);
  const statusLower = (booking.status || '').toLowerCase();
  const isCancellable = statusLower === 'pending' || statusLower === 'confirmed';
  const isDone = statusLower === 'done';

  // Strict XSS link sanitation: only allow http: / https: URLs!
  const hasRawMeetLink = Boolean(booking.meetLink);
  const safeMeetHref = hasRawMeetLink ? safeExternalHref(booking.meetLink) : null;
  const isDangerousMeetLink = hasRawMeetLink && !safeMeetHref;

  return (
    <div className="max-w-4xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-6">
      <div>
        <Link to="/mentors/bookings" className="no-underline text-muted hover:text-foreground text-sm flex items-center gap-1.5 mb-2">
          <ArrowLeft className="w-4 h-4" />
          Back to bookings
        </Link>
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
              Booking #{booking.bookingId}
            </h1>
            <p className="text-sm text-muted mt-1 mb-0">
              Session with <strong className="font-semibold text-foreground">{booking.mentorName}</strong> ({formatMentorType(booking.mentorType)})
            </p>
          </div>

          <Chip size="md" color={statusDisplay.tone} variant="soft" className="font-semibold self-start sm:self-auto">
            {statusDisplay.label}
          </Chip>
        </div>
      </div>

      {refundNotice && (
        <Alert status="success">
          <Alert.Indicator>
            <CheckCircle2 className="w-5 h-5 text-success" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Session cancelled</Alert.Title>
            <Alert.Description>{refundNotice}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {actionError && (
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-5 h-5" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Action failed</Alert.Title>
            <Alert.Description>{actionError}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Main Booking Info Card */}
      <Card className="shadow-surface">
        <Card.Header>
          <Card.Title>Session details</Card.Title>
          <Card.Description>Schedule, focus area, and virtual meeting connection</Card.Description>
        </Card.Header>

        <Card.Content className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 p-4 rounded-lg bg-surface-secondary border border-border text-sm">
            <div>
              <span className="text-xs text-muted block mb-1">Scheduled time</span>
              <span className="font-mono-tabular font-semibold text-foreground">
                {formatDateTime(booking.startAt)}
              </span>
            </div>

            <div>
              <span className="text-xs text-muted block mb-1">Session cost</span>
              <span className="font-mono-tabular font-bold text-accent">
                {booking.priceCredits} credits
              </span>
            </div>

            {booking.focusArea && (
              <div className="sm:col-span-2 border-t border-separator pt-3">
                <span className="text-xs text-muted block mb-1">Focus area / questions</span>
                <span className="text-foreground">{booking.focusArea}</span>
              </div>
            )}
          </div>

          {/* Virtual Meeting Link Section */}
          <div className="space-y-2">
            <span className="text-xs font-semibold uppercase tracking-wider text-muted block">
              Meeting connection
            </span>

            {safeMeetHref ? (
              <a
                href={safeMeetHref}
                target="_blank"
                rel="noopener noreferrer"
                className="no-underline inline-block"
              >
                <Button variant="primary">
                  <Video className="w-4 h-4 mr-2" />
                  Join meeting
                  <ExternalLink className="w-3.5 h-3.5 ml-1.5" />
                </Button>
              </a>
            ) : isDangerousMeetLink ? (
              <Alert status="warning">
                <Alert.Indicator>
                  <ShieldAlert className="w-5 h-5 text-warning" />
                </Alert.Indicator>
                <Alert.Content>
                  <Alert.Title>Invalid or unverified meeting link</Alert.Title>
                  <Alert.Description>
                    The meeting link provided by the instructor could not be verified securely. Please contact support.
                  </Alert.Description>
                </Alert.Content>
              </Alert>
            ) : (
              <p className="text-xs text-muted m-0 p-3 bg-surface-secondary rounded border border-border">
                Meeting link has not been assigned yet or is managed in person.
              </p>
            )}
          </div>

          {/* Cancellation button if eligible */}
          {isCancellable && (
            <div className="pt-4 border-t border-separator flex items-center justify-between">
              <span className="text-xs text-muted">
                Free cancellation returns all credits to your ledger.
              </span>
              <Button
                variant="secondary"
                size="sm"
                isPending={cancelMutation.isPending}
                onPress={() => cancelMutation.mutate()}
                onClick={() => cancelMutation.mutate()}
              >
                <XCircle className="w-4 h-4 mr-1 text-danger" />
                Cancel session
              </Button>
            </div>
          )}
        </Card.Content>
      </Card>

      {/* Embedded Diagnostic Standing Card */}
      <MentorStandingCard standing={booking.standing} />

      {/* Review & Feedback Section (when done) */}
      {isDone && (
        <Card className="shadow-surface">
          <Card.Header>
            <Card.Title>Session rating & review</Card.Title>
            <Card.Description>Share your experience to help mentors improve</Card.Description>
          </Card.Header>

          <Card.Content className="space-y-4">
            {booking.review ? (
              <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-2">
                <div className="flex items-center gap-1 text-warning">
                  {Array.from({ length: 5 }).map((_, i) => (
                    <Star
                      key={i}
                      className={`w-4 h-4 ${
                        i < booking.review!.rating ? 'fill-current' : 'text-muted opacity-30'
                      }`}
                    />
                  ))}
                  <span className="text-xs font-mono-tabular text-foreground ml-2 font-bold">
                    {booking.review.rating}/5
                  </span>
                </div>
                {booking.review.text && (
                  <p className="text-sm text-foreground m-0 leading-normal">{booking.review.text}</p>
                )}
                <span className="text-xs text-muted font-mono-tabular block">
                  Reviewed on {formatDateTime(booking.review.createdAt)}
                </span>
              </div>
            ) : (
              <div className="space-y-4">
                <div>
                  <label className="text-xs font-semibold text-foreground block mb-1.5">
                    Rating (1 to 5 stars):
                  </label>
                  <div className="flex items-center gap-2">
                    {[1, 2, 3, 4, 5].map((star) => (
                      <button
                        key={star}
                        type="button"
                        onClick={() => setRating(star)}
                        className="p-1 text-warning hover:scale-110 transition-transform focus:outline-none"
                        aria-label={`${star} star`}
                      >
                        <Star
                          className={`w-6 h-6 ${
                            star <= rating ? 'fill-current' : 'text-muted opacity-30'
                          }`}
                        />
                      </button>
                    ))}
                    <span className="text-xs font-mono-tabular text-muted ml-2">
                      {rating} of 5 stars
                    </span>
                  </div>
                </div>

                <div className="space-y-1">
                  <label htmlFor="review-text" className="text-xs font-semibold text-foreground block">
                    Feedback (optional, max 2000 characters):
                  </label>
                  <textarea
                    id="review-text"
                    rows={3}
                    maxLength={2000}
                    value={reviewText}
                    onChange={(e) => setReviewText(e.target.value)}
                    placeholder="Describe how the mentor helped your exam readiness..."
                    className="w-full text-xs p-2.5 rounded-md border border-border bg-surface text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                  />
                </div>

                <Button
                  variant="primary"
                  size="sm"
                  isPending={rateMutation.isPending}
                  onPress={() => rateMutation.mutate()}
                >
                  Submit review
                </Button>
              </div>
            )}
          </Card.Content>
        </Card>
      )}
    </div>
  );
}
