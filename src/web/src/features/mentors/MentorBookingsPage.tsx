import React from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Spinner,
  Table,
} from '@heroui/react';
import {
  AlertCircle,
  Calendar,
  CalendarCheck,
  CheckCircle2,
  Clock,
  ExternalLink,
  GraduationCap,
  Sparkles,
  User,
} from 'lucide-react';
import { api } from '../../lib/api';
import {
  formatBookingStatus,
  formatDateTimeRange,
} from '../../lib/domain-labels';

export function MentorBookingsPage() {
  const bookingsQuery = useQuery({
    queryKey: ['mentors', 'bookings'],
    queryFn: () => api.mentors.bookings(),
    staleTime: 15_000,
    retry: false,
  });

  const bookings = bookingsQuery.data || [];

  return (
    <div className="max-w-6xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <CalendarCheck className="w-5 h-5 text-accent" />
            <span className="text-xs font-semibold uppercase tracking-wider text-muted">
              Scheduled Mentorship
            </span>
          </div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            My mentor bookings
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            Review your upcoming 1-on-1 sessions, join meetings, or check past diagnostic feedback.
          </p>
        </div>

        <div>
          <Link to="/mentors" className="no-underline">
            <Button variant="primary" size="sm">
              <GraduationCap className="w-4 h-4 mr-1.5" />
              Browse mentors
            </Button>
          </Link>
        </div>
      </div>

      <Card className="shadow-surface">
        <Card.Header>
          <Card.Title>Booking history</Card.Title>
          <Card.Description>All scheduled and completed diagnostic consultations</Card.Description>
        </Card.Header>

        <Card.Content>
          {bookingsQuery.isLoading ? (
            <div className="flex items-center justify-center py-12 gap-3 text-muted text-sm">
              <Spinner size="sm" />
              <span>Loading your bookings...</span>
            </div>
          ) : bookingsQuery.isError ? (
            <Alert status="danger">
              <Alert.Indicator>
                <AlertCircle className="w-4 h-4" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Unable to load bookings</Alert.Title>
                <Alert.Description>Failed to fetch your scheduled sessions. Please retry.</Alert.Description>
              </Alert.Content>
            </Alert>
          ) : bookings.length === 0 ? (
            <div className="text-center py-12 space-y-3">
              <Calendar className="w-10 h-10 text-muted mx-auto" />
              <p className="text-base font-semibold text-foreground m-0">No bookings yet</p>
              <p className="text-sm text-muted m-0">
                You have not booked any mentor sessions yet.
              </p>
              <div className="pt-2">
                <Link to="/mentors" className="no-underline">
                  <Button variant="secondary" size="sm">
                    Find a mentor
                  </Button>
                </Link>
              </div>
            </div>
          ) : (
            <Table>
              <Table.ScrollContainer>
                <Table.Content aria-label="Bookings table">
                  <Table.Header>
                    <Table.Column isRowHeader>Booking #</Table.Column>
                    <Table.Column>Mentor</Table.Column>
                    <Table.Column>Focus area</Table.Column>
                    <Table.Column>Scheduled time</Table.Column>
                    <Table.Column>Credits</Table.Column>
                    <Table.Column>Status</Table.Column>
                    <Table.Column className="text-right">Action</Table.Column>
                  </Table.Header>
                  <Table.Body>
                    {bookings.map((booking) => {
                      const statusDisplay = formatBookingStatus(booking.status);
                      return (
                        <Table.Row key={booking.bookingId}>
                          <Table.Cell className="font-mono-tabular font-semibold">
                            <Link
                              to={`/mentors/bookings/${booking.bookingId}`}
                              className="text-accent hover:underline"
                            >
                              #{booking.bookingId}
                            </Link>
                          </Table.Cell>

                          <Table.Cell className="font-medium text-foreground">
                            {booking.mentorName}
                          </Table.Cell>

                          <Table.Cell className="text-xs text-muted">
                            {booking.focusArea || '1-on-1 consultation'}
                          </Table.Cell>

                          <Table.Cell className="font-mono-tabular text-xs">
                            {formatDateTimeRange(booking.startAt, booking.endAt)}
                          </Table.Cell>

                          <Table.Cell className="font-mono-tabular font-bold text-accent">
                            {booking.priceCredits}
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

                          <Table.Cell className="text-right">
                            <Link
                              to={`/mentors/bookings/${booking.bookingId}`}
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
    </div>
  );
}
