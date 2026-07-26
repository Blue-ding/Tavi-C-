import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { queryKeys } from '@/shared/query';
import { worldClient } from '../client/worldClient';
import type { WorldGraphViewModel } from '../client/worldSchemas';

export function useWorldWorkspace() {
  const queryClient = useQueryClient();

  const workspaceQuery = useQuery({
    queryKey: queryKeys.world.workspace,
    queryFn: () => worldClient.getWorkspace(),
    staleTime: 10_000,
  });

  const typesQuery = useQuery({
    queryKey: queryKeys.world.types,
    queryFn: () => worldClient.getTypes(),
    staleTime: 5 * 60 * 1000,
  });

  const moduleActionsQuery = useQuery({
    queryKey: queryKeys.world.moduleActions,
    queryFn: () => worldClient.getModuleActions(),
    staleTime: 5 * 60 * 1000,
  });

  const getStateId = useCallback(() => {
    return queryClient.getQueryData<WorldGraphViewModel>(queryKeys.world.workspace)?.stateId;
  }, [queryClient]);

  const invalidate = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: queryKeys.world.workspace });
  }, [queryClient]);

  const setWorkspace = useCallback(
    (data: WorldGraphViewModel) => {
      queryClient.setQueryData(queryKeys.world.workspace, data);
    },
    [queryClient]
  );

  const addElementMutation = useMutation({
    mutationFn: (input: { name: string; description: string; type: string }) => {
      const stateId = getStateId();
      if (!stateId) throw new Error('No state id');
      return worldClient.addElement({ expectedStateId: stateId, ...input });
    },
    onSuccess: (res) => {
      setWorkspace(res.world);
    },
  });

  const updateElementMutation = useMutation({
    mutationFn: (input: {
      id: string;
      name?: string;
      description?: string;
      type?: string;
    }) => {
      const stateId = getStateId();
      if (!stateId) throw new Error('No state id');
      return worldClient.updateElement(input.id, {
        expectedStateId: stateId,
        name: input.name,
        description: input.description,
        type: input.type,
      });
    },
    onSuccess: (res) => {
      setWorkspace(res.world);
    },
  });

  const deleteElementMutation = useMutation({
    mutationFn: (id: string) => {
      const stateId = getStateId();
      if (!stateId) throw new Error('No state id');
      return worldClient.deleteElement(id, stateId);
    },
    onSuccess: (res) => {
      setWorkspace(res.world);
    },
  });

  const commitMutation = useMutation({
    mutationFn: (changeIds: string[]) => {
      const stateId = getStateId();
      if (!stateId) throw new Error('No state id');
      return worldClient.commitStaged(stateId, changeIds);
    },
    onSuccess: () => {
      invalidate();
    },
  });

  const undoMutation = useMutation({
    mutationFn: () => {
      const stateId = getStateId();
      if (!stateId) throw new Error('No state id');
      return worldClient.undo(stateId);
    },
    onSuccess: (res) => {
      setWorkspace(res.world);
    },
  });

  const redoMutation = useMutation({
    mutationFn: () => {
      const stateId = getStateId();
      if (!stateId) throw new Error('No state id');
      return worldClient.redo(stateId);
    },
    onSuccess: (res) => {
      setWorkspace(res.world);
    },
  });

  const saveMutation = useMutation({
    mutationFn: () => {
      const stateId = getStateId();
      if (!stateId) throw new Error('No state id');
      return worldClient.save(stateId);
    },
    onSuccess: () => {
      invalidate();
    },
  });

  return {
    workspace: workspaceQuery.data,
    isLoading: workspaceQuery.isLoading,
    error: workspaceQuery.error,
    types: typesQuery.data,
    moduleActions: moduleActionsQuery.data,
    addElement: addElementMutation.mutateAsync,
    updateElement: updateElementMutation.mutateAsync,
    deleteElement: deleteElementMutation.mutateAsync,
    commit: commitMutation.mutateAsync,
    undo: undoMutation.mutateAsync,
    redo: redoMutation.mutateAsync,
    save: saveMutation.mutateAsync,
    isCommitting: commitMutation.isPending,
    isSaving: saveMutation.isPending,
    isUndoing: undoMutation.isPending,
    isRedoing: redoMutation.isPending,
    invalidate,
  };
}
