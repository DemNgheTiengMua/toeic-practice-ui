import React, { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  Spinner,
} from '@heroui/react';
import {
  AlertCircle,
  ArrowRight,
  BookOpen,
  CalendarCheck,
  GraduationCap,
  Sparkles,
  Star,
  UserCheck,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { formatVnd } from '../../lib/security';
import { formatMentorType, formatModality } from '../../lib/domain-labels';

export function MentorsCatalogPage() {
  const { learnerContext } = useAuth();
  const certCode = learnerContext?.active?.code || '';

  const [selectedSkill, setSelectedSkill] = useState<string>('all');

  const mentorsQuery = useQuery({
    queryKey: ['mentors', 'list', certCode, selectedSkill],
    queryFn: () =>
      api.mentors.list(
        certCode || undefined,
        selectedSkill !== 'all' ? selectedSkill : undefined,
      ),
    staleTime: 30_000,
    retry: false,
  });

  const mentors = mentorsQuery.data || [];

  return (
    <div className="max-w-6xl mx-auto py-8 px-4 sm:px-6 flex flex-col gap-8">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <GraduationCap className="w-5 h-5 text-accent" />
            <span className="text-xs font-semibold uppercase tracking-wider text-muted">
              Expert Guidance
            </span>
          </div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            Mentors & instructors
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            Connect with certified master mentors and AI coaches for 1-on-1 diagnostic sessions.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <Link to="/mentors/bookings" className="no-underline">
            <Button variant="secondary" size="sm">
              <CalendarCheck className="w-4 h-4 mr-1.5" />
              My bookings
            </Button>
          </Link>
          <Link to="/wallet" className="no-underline">
            <Button variant="ghost" size="sm">
              <Sparkles className="w-4 h-4 mr-1.5 text-warning" />
              Wallet: {learnerContext ? `${learnerContext.creditBalance} credits` : 'Credits'}
            </Button>
          </Link>
        </div>
      </div>

      {/* Filter Bar */}
      <div className="flex flex-wrap items-center gap-2 pb-2 border-b border-separator">
        <span className="text-xs font-semibold text-muted uppercase tracking-wider mr-2">
          Filter by skill:
        </span>
        {['all', 'listening', 'reading', 'speaking', 'writing'].map((skillKey) => (
          <Button
            key={skillKey}
            size="sm"
            variant={selectedSkill === skillKey ? 'primary' : 'ghost'}
            onPress={() => setSelectedSkill(skillKey)}
          >
            {skillKey === 'all' ? 'All skills' : formatModality(skillKey)}
          </Button>
        ))}
      </div>

      {/* Catalog Grid */}
      {mentorsQuery.isLoading ? (
        <div className="flex items-center justify-center py-16 gap-3 text-muted text-sm">
          <Spinner size="sm" />
          <span>Finding available mentors...</span>
        </div>
      ) : mentorsQuery.isError ? (
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Unable to load mentors</Alert.Title>
            <Alert.Description>
              Failed to load mentors for {certCode || 'your active certificate'}. Please try again.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      ) : mentors.length === 0 ? (
        <Card className="text-center py-12">
          <Card.Content className="space-y-3">
            <BookOpen className="w-10 h-10 text-muted mx-auto" />
            <p className="text-base font-semibold text-foreground m-0">No mentors available</p>
            <p className="text-sm text-muted m-0">
              There are currently no active mentors matching the selected skill filter.
            </p>
          </Card.Content>
        </Card>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {mentors.map((mentor) => {
            const isAi = (mentor.type || '').toLowerCase() === 'ai';
            return (
              <Card
                key={mentor.mentorId}
                className="flex flex-col justify-between shadow-surface hover:border-accent transition-colors"
              >
                <Card.Header className="space-y-3">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <Card.Title className="text-lg font-bold">{mentor.displayName}</Card.Title>
                      <Card.Description className="text-xs line-clamp-1">
                        {mentor.headline || (isAi ? 'AI Diagnostic Coach' : 'Certified Instructor')}
                      </Card.Description>
                    </div>

                    <Chip
                      size="sm"
                      color={isAi ? 'accent' : 'default'}
                      variant="soft"
                    >
                      {formatMentorType(mentor.type)}
                    </Chip>
                  </div>

                  <div className="flex items-center gap-3 text-xs">
                    <div className="flex items-center gap-1 text-warning font-semibold">
                      <Star className="w-3.5 h-3.5 fill-current" />
                      <span>{mentor.avgRating ? mentor.avgRating.toFixed(1) : 'New'}</span>
                    </div>
                    <span className="text-muted">
                      ({mentor.reviewCount} {mentor.reviewCount === 1 ? 'review' : 'reviews'})
                    </span>
                    <span className="text-separator">•</span>
                    <span className="text-muted font-mono-tabular">{mentor.sessionMinutes} min/session</span>
                  </div>
                </Card.Header>

                <Card.Content className="space-y-4 pt-1 flex-1 flex flex-col justify-between">
                  <p className="text-sm text-muted line-clamp-3 m-0">
                    {mentor.bio || mentor.expertise || 'Dedicated mentorship for target score attainment.'}
                  </p>

                  <div className="pt-4 border-t border-separator space-y-3">
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted">Session cost:</span>
                      <div className="flex items-baseline gap-1.5">
                        <span className="font-mono-tabular font-bold text-accent text-base">
                          {mentor.priceCredits} credits
                        </span>
                        <span className="text-xs text-muted font-mono-tabular">
                          ({formatVnd(mentor.pricePerSlot)})
                        </span>
                      </div>
                    </div>

                    <Link
                      to={`/mentors/${mentor.mentorId}`}
                      className="no-underline w-full block"
                    >
                      <Button
                        variant={isAi ? 'primary' : 'secondary'}
                        className="w-full justify-center"
                      >
                        {isAi ? 'Start AI session' : 'Book session'}
                        <ArrowRight className="w-4 h-4 ml-1.5" />
                      </Button>
                    </Link>
                  </div>
                </Card.Content>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
