import React from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Button, Card } from '@heroui/react';
import { ShieldAlert } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';

export function AccessDeniedPage() {
  const { isAuthenticated, isStaff } = useAuth();
  const navigate = useNavigate();

  const homeHref = isStaff ? '/admin' : isAuthenticated ? '/dashboard' : '/';
  const homeLabel = isAuthenticated ? 'Go back to my workspace' : 'Back to home';

  return (
    <div className="mx-auto flex max-w-lg flex-col justify-center py-12">
      <Card className="border border-border">
        <Card.Header className="flex flex-col items-center gap-3 pb-2 text-center">
          <span className="inline-flex h-12 w-12 items-center justify-center rounded-full bg-danger/10 text-danger">
            <ShieldAlert className="h-6 w-6" aria-hidden="true" />
          </span>
          <Card.Title className="font-display text-2xl font-semibold text-foreground">
            Access restricted
          </Card.Title>
        </Card.Header>

        <Card.Content className="space-y-4 text-center text-sm leading-relaxed text-muted">
          <p>
            This page is not available to your account. It may belong to a different kind of user, or to a part of the product your role does not cover.
          </p>
        </Card.Content>

        <Card.Footer className="flex flex-wrap justify-center gap-3 pt-2">
          <Button variant="primary" onPress={() => void navigate({ to: homeHref })}>
            {homeLabel}
          </Button>
          {!isAuthenticated && (
            <Button variant="secondary" onPress={() => void navigate({ to: '/sign-in' })}>
              Sign in
            </Button>
          )}
        </Card.Footer>
      </Card>
    </div>
  );
}
