import React, { useEffect } from 'react';
import { useLocation, useNavigate } from '@tanstack/react-router';
import { useAuth } from '../../context/AuthContext';

interface AdminAccessGuardProps {
  children: React.ReactNode;
  predicate?: (user: any) => boolean;
}

export function AdminAccessGuard({ children, predicate }: AdminAccessGuardProps) {
  const { user, isAuthenticated, isStaff, isLoading } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    if (isLoading) return;

    if (!isAuthenticated) {
      if (
        !location.pathname.startsWith('/sign-in') &&
        !location.pathname.startsWith('/auth/sign-in')
      ) {
        const returnUrl = `${location.pathname}${location.searchStr || ''}`;
        void navigate({
          to: '/sign-in',
          search: { returnUrl } as any,
          replace: true,
        });
      }
      return;
    }

    if (!isStaff) {
      void navigate({
        to: '/access-denied',
        replace: true,
      });
      return;
    }

    if (predicate && !predicate(user)) {
      void navigate({
        to: '/access-denied',
        replace: true,
      });
    }
  }, [
    isLoading,
    isAuthenticated,
    isStaff,
    user,
    predicate,
    location.pathname,
    location.searchStr,
    navigate,
  ]);

  if (isLoading || !isAuthenticated || !isStaff) {
    return null;
  }

  if (predicate && !predicate(user)) {
    return null;
  }

  return <>{children}</>;
}
