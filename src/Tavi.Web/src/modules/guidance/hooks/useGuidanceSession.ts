import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { queryKeys } from '@/shared/query';
import { guidanceClient } from '../client/guidanceClient';
import type { GuidanceSnapshotViewModel } from '../client/guidanceSchemas';

export function useGuidanceSession() {
  const queryClient = useQueryClient();
  const [draftMessage, setDraftMessage] = useState('');

  const availabilityQuery = useQuery({
    queryKey: queryKeys.guidance.availability,
    queryFn: () => guidanceClient.getAvailability(),
    staleTime: 30_000,
  });

  const sessionQuery = useQuery({
    queryKey: queryKeys.guidance.session,
    queryFn: () => guidanceClient.getSession(),
    enabled: availabilityQuery.data?.available === true,
    staleTime: 10_000,
  });

  const setSession = useCallback(
    (data: GuidanceSnapshotViewModel) => {
      queryClient.setQueryData(queryKeys.guidance.session, data);
    },
    [queryClient]
  );

  const startMutation = useMutation({
    mutationFn: (potential: string) => guidanceClient.startSession(potential),
    onSuccess: (res) => {
      setSession(res.snapshot);
    },
  });

  const sendMutation = useMutation({
    mutationFn: (message: string) => {
      const sessionId = sessionQuery.data?.sessionId;
      if (!sessionId) throw new Error('No session');
      return guidanceClient.sendMessage(sessionId, message);
    },
    onSuccess: (res) => {
      setSession(res.snapshot);
    },
  });

  const retryMutation = useMutation({
    mutationFn: (message: string) => {
      const sessionId = sessionQuery.data?.sessionId;
      if (!sessionId) throw new Error('No session');
      return guidanceClient.retryMessage(sessionId, message);
    },
    onSuccess: (res) => {
      setSession(res.snapshot);
    },
  });

  const commitMutation = useMutation({
    mutationFn: (acceptedChangeIds: string[]) => {
      const sessionId = sessionQuery.data?.sessionId;
      if (!sessionId) throw new Error('No session');
      return guidanceClient.commit(sessionId, acceptedChangeIds);
    },
    onSuccess: (res) => {
      setSession(res.snapshot);
      queryClient.invalidateQueries({ queryKey: queryKeys.world.workspace });
    },
  });

  const refreshMutation = useMutation({
    mutationFn: () => {
      const sessionId = sessionQuery.data?.sessionId;
      if (!sessionId) throw new Error('No session');
      return guidanceClient.refreshSession(sessionId);
    },
    onSuccess: (res) => {
      setSession(res);
    },
  });

  const cancelMutation = useMutation({
    mutationFn: () => {
      const sessionId = sessionQuery.data?.sessionId;
      if (!sessionId) throw new Error('No session');
      return guidanceClient.cancelOperation(sessionId);
    },
    onSuccess: (res) => {
      setSession(res);
    },
  });

  const forgetMutation = useMutation({
    mutationFn: () => {
      const sessionId = sessionQuery.data?.sessionId;
      if (!sessionId) throw new Error('No session');
      return guidanceClient.deleteSession(sessionId);
    },
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: queryKeys.guidance.session });
    },
  });

  return {
    availability: availabilityQuery.data,
    session: sessionQuery.data,
    isLoading: availabilityQuery.isLoading || sessionQuery.isLoading,
    draftMessage,
    setDraftMessage,
    start: startMutation.mutateAsync,
    send: sendMutation.mutateAsync,
    retry: retryMutation.mutateAsync,
    commit: commitMutation.mutateAsync,
    refresh: refreshMutation.mutateAsync,
    cancel: cancelMutation.mutateAsync,
    forget: forgetMutation.mutateAsync,
    isProcessing:
      startMutation.isPending ||
      sendMutation.isPending ||
      retryMutation.isPending ||
      commitMutation.isPending ||
      refreshMutation.isPending ||
      cancelMutation.isPending,
  };
}
