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
  ShieldAlert,
  Sparkles,
  Star,
  Users,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import {
  canViewInstructorSessions,
  hasCapability,
  STAFF_PERMISSIONS,
} from '../../lib/security';
import { formatMentorType } from '../../lib/domain-labels';

export function InstructorSessionsPage() {
  const { user } = useAuth();

  const isPermitted = canViewInstructorSessions(user);
  const isSupportOrAdmin =
    hasCapability(user, STAFF_PERMISSIONS.SupportTriage) ||
    hasCapability(user, STAFF_PERMISSIONS.SystemAdmin);

  const adminMentorsQuery = useQuery({
    queryKey: ['admin', 'mentors', 'roster'],
    queryFn: () => api.admin.mentors(),
    enabled: isPermitted && isSupportOrAdmin,
    staleTime: 30_000,
    retry: false,
  });

  if (!isPermitted) {
    return (
      <div className="max-w-2xl mx-auto py-16 px-4 sm:px-6">
        <Card className="shadow-surface border-l-4 border-l-danger">
          <Card.Header>
            <div className="flex items-center gap-2 text-danger">
              <ShieldAlert className="w-5 h-5" />
              <Card.Title className="text-xl font-bold">Access denied</Card.Title>
            </div>
          </Card.Header>
          <Card.Content className="space-y-4">
            <p className="text-sm text-foreground m-0">
              You require instructor or operations permissions to access this workspace.
            </p>
            <div className="pt-2">
              <Link to="/dashboard" className="no-underline">
                <Button variant="primary">Return to dashboard</Button>
              </Link>
            </div>
          </Card.Content>
        </Card>
      </div>
    );
  }

  return (
    <div className="max-w-6xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <GraduationCap className="w-5 h-5 text-accent" />
            <span className="text-xs font-semibold uppercase tracking-wider text-muted">
              Instruction Operations
            </span>
          </div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            Instructor sessions
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            Operational schedule, roster availability, and session capacity across all certificates.
          </p>
        </div>

        <div>
          <Link to="/mentors/bookings" className="no-underline">
            <Button variant="secondary" size="sm">
              <CalendarCheck className="w-4 h-4 mr-1.5" />
              View learner bookings
            </Button>
          </Link>
        </div>
      </div>

      {/* When viewed by support staff / system admin: Roster and open slot monitor */}
      {isSupportOrAdmin ? (
        <Card className="shadow-surface">
          <Card.Header>
            <div className="flex items-center justify-between">
              <div>
                <Card.Title>Instructor capacity & slot roster</Card.Title>
                <Card.Description>Live slot inventory and consultation metrics</Card.Description>
              </div>
            </div>
          </Card.Header>

          <Card.Content>
            {adminMentorsQuery.isLoading ? (
              <div className="flex items-center justify-center py-12 gap-3 text-muted text-sm">
                <Spinner size="sm" />
                <span>Loading instructor slot roster...</span>
              </div>
            ) : adminMentorsQuery.isError ? (
              <Alert status="danger">
                <Alert.Indicator>
                  <AlertCircle className="w-4 h-4" />
                </Alert.Indicator>
                <Alert.Content>
                  <Alert.Title>Unable to load instructors</Alert.Title>
                  <Alert.Description>Failed to fetch administrative mentor roster.</Alert.Description>
                </Alert.Content>
              </Alert>
            ) : (
              <Table>
                <Table.ScrollContainer>
                  <Table.Content aria-label="Admin mentor roster">
                    <Table.Header>
                      <Table.Column isRowHeader>Instructor</Table.Column>
                      <Table.Column>Type</Table.Column>
                      <Table.Column>Certificate</Table.Column>
                      <Table.Column>Price</Table.Column>
                      <Table.Column>Open slots</Table.Column>
                      <Table.Column>Total slots</Table.Column>
                      <Table.Column>Rating</Table.Column>
                      <Table.Column className="text-right">Status</Table.Column>
                    </Table.Header>
                    <Table.Body>
                      {(adminMentorsQuery.data || []).map((m) => (
                        <Table.Row key={m.mentorId}>
                          <Table.Cell className="font-semibold text-foreground">
                            {m.displayName}
                          </Table.Cell>

                          <Table.Cell className="text-xs text-muted">
                            {formatMentorType(m.type)}
                          </Table.Cell>

                          <Table.Cell className="font-mono-tabular text-xs">
                            {m.certCode}
                          </Table.Cell>

                          <Table.Cell className="font-mono-tabular font-bold text-accent">
                            {m.priceCredits} credits
                          </Table.Cell>

                          <Table.Cell className="font-mono-tabular font-semibold text-foreground">
                            {m.openSlotsCount} open slots
                          </Table.Cell>

                          <Table.Cell className="font-mono-tabular text-muted">
                            {m.totalSlotsCount}
                          </Table.Cell>

                          <Table.Cell className="font-mono-tabular text-xs">
                            {m.avgRating ? `${m.avgRating.toFixed(1)} (${m.reviewCount})` : 'New'}
                          </Table.Cell>

                          <Table.Cell className="text-right">
                            <Chip
                              size="sm"
                              color={m.isActive ? 'success' : 'default'}
                              variant="soft"
                            >
                              {m.isActive ? 'Active' : 'Inactive'}
                            </Chip>
                          </Table.Cell>
                        </Table.Row>
                      ))}
                    </Table.Body>
                  </Table.Content>
                </Table.ScrollContainer>
              </Table>
            )}
          </Card.Content>
        </Card>
      ) : (
        /* Pure Instructor role schedule workbench */
        <Card className="shadow-surface">
          <Card.Header>
            <Card.Title>Instructor schedule workbench</Card.Title>
            <Card.Description>Your instructor console for 1-on-1 consultations</Card.Description>
          </Card.Header>
          <Card.Content className="space-y-6">
            <Alert status="accent">
              <Alert.Indicator>
                <Calendar className="w-5 h-5 text-accent" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>Session management active</Alert.Title>
                <Alert.Description>
                  Your consultations are synchronized with the learner booking calendar.
                  When learners book slots with you, meeting details appear in the bookings hub.
                </Alert.Description>
              </Alert.Content>
            </Alert>

            <div className="flex flex-wrap items-center gap-3">
              <Link to="/mentors/bookings" className="no-underline">
                <Button variant="primary">
                  <CalendarCheck className="w-4 h-4 mr-2" />
                  View scheduled sessions
                </Button>
              </Link>
              <Link to="/mentors" className="no-underline">
                <Button variant="secondary">
                  Preview public profile
                </Button>
              </Link>
            </div>
          </Card.Content>
        </Card>
      )}
    </div>
  );
}
