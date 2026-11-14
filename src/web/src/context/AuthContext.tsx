import React, { createContext, useContext, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, AuthUserDto, LearnerContextDto, setUnauthorizedHandler } from '../lib/api';
import {
  STAFF_PERMISSIONS,
  hasCapability,
  isStaffSubject,
  resolveSafeReturnUrl,
} from '../lib/security';

export { STAFF_PERMISSIONS };

interface AuthContextValue {
  user: AuthUserDto | null;
  learnerContext: LearnerContextDto | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  isStaff: boolean;
  hasCapability: (capability: string) => boolean;
  refreshSession: (nextUser?: AuthUserDto | null) => Promise<void>;
  switchCertificate: (code: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const queryClient = useQueryClient();
  const isSigningOutRef = useRef(false);

  const meQuery = useQuery({
    queryKey: ['auth', 'me'],
    queryFn: async () => {
      try {
        return await api.auth.me();
      } catch {
        return null;
      }
    },
    staleTime: 60_000,
    retry: false,
  });

  const user = meQuery.data ?? null;
  const isStaff = isStaffSubject(user);
  const isLearner = Boolean(user && !isStaff && user.role?.toLowerCase() === 'learner');

  useEffect(() => {
    setUnauthorizedHandler(() => {
      if (isSigningOutRef.current) {
        return;
      }
      queryClient.setQueryData(['auth', 'me'], null);
      queryClient.removeQueries({ queryKey: ['learner', 'context'] });
      if (
        typeof window !== 'undefined' &&
        !window.location.pathname.startsWith('/sign-in') &&
        !window.location.pathname.startsWith('/auth/sign-in')
      ) {
        const current = resolveSafeReturnUrl(
          `${window.location.pathname}${window.location.search}`,
          '/dashboard',
        );
        window.location.assign(`/sign-in?returnUrl=${encodeURIComponent(current)}`);
      }
    });
    return () => setUnauthorizedHandler(null);
  }, [queryClient]);

  const learnerContextQuery = useQuery({
    queryKey: ['learner', 'context', user?.userId],
    queryFn: async () => {
      try {
        return await api.learner.context();
      } catch {
        return null;
      }
    },
    enabled: isLearner,
    staleTime: 30_000,
    retry: false,
  });

  const switchCertMutation = useMutation({
    mutationFn: (code: string) => api.learner.switchCertificate(code),
    onSuccess: () => {
      queryClient.invalidateQueries();
    },
  });

  const signOutMutation = useMutation({
    mutationFn: () => api.auth.signOut(),
    onSuccess: () => {
      queryClient.clear();
      queryClient.setQueryData(['auth', 'me'], null);
    },
  });

  const value: AuthContextValue = {
    user,
    learnerContext: learnerContextQuery.data ?? null,
    isLoading: meQuery.isLoading || (meQuery.isFetching && !user),
    isAuthenticated: Boolean(user),
    isStaff,
    hasCapability: (capability: string) => hasCapability(user, capability),
    refreshSession: async (nextUser?: AuthUserDto | null) => {
      if (nextUser) {
        queryClient.setQueryData(['auth', 'me'], nextUser);
      }
      await queryClient.invalidateQueries({ queryKey: ['auth', 'me'] });
      await queryClient.invalidateQueries({ queryKey: ['learner', 'context'] });
    },
    switchCertificate: async (code: string) => {
      await switchCertMutation.mutateAsync(code);
    },
    signOut: async () => {
      isSigningOutRef.current = true;
      try {
        await queryClient.cancelQueries();
        await signOutMutation.mutateAsync();
      } finally {
        isSigningOutRef.current = false;
      }
    },
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return ctx;
}
