import { Link } from '@tanstack/react-router';
import { Card, Chip, buttonVariants } from '@heroui/react';
import { Compass, Sparkles, ArrowRight, Users } from 'lucide-react';

export function OobeSoftLockEmptyState() {
  return (
    <div className="mx-auto max-w-2xl py-10" data-testid="oobe-softlock-empty-state">
      <Card variant="default" className="border border-border/80 bg-surface shadow-sm">
        <Card.Header className="flex flex-col items-start gap-2 pb-2">
          <div className="flex items-center gap-2">
            <Chip size="sm" variant="soft" color="accent">
              <Sparkles className="h-3.5 w-3.5" />
              Guided Setup Required
            </Chip>
          </div>
          <Card.Title className="text-xl font-semibold">
            Complete your guided setup to unlock this area
          </Card.Title>
          <Card.Description>
            Finish the universal placement test, choose your target band, and generate your first learning path before accessing the dashboard and practice sessions.
          </Card.Description>
        </Card.Header>
        <Card.Content className="flex flex-wrap items-center gap-3 pt-2">
          <Link
            to="/onboarding"
            className={buttonVariants({ variant: 'primary', className: 'no-underline' })}
          >
            <Compass className="h-4 w-4" />
            Continue OOBE setup
            <ArrowRight className="h-4 w-4" />
          </Link>
          <Link
            to="/mentors"
            className={buttonVariants({ variant: 'secondary', className: 'no-underline' })}
          >
            <Users className="h-4 w-4" />
            Explore Mentors
          </Link>
        </Card.Content>
      </Card>
    </div>
  );
}
