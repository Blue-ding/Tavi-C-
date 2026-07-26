import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { sseRegistry } from './sseSubscription';

export function useWorkspaceEvents(
  url: string,
  queryKey: readonly unknown[]
): { reconnecting: boolean } {
  const queryClient = useQueryClient();
  const reconnectingRef = useRef(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const hasConnectedRef = useRef(false);

  useEffect(() => {
    const invalidate = () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
      timeoutRef.current = setTimeout(() => {
        queryClient.invalidateQueries({ queryKey });
        timeoutRef.current = null;
      }, 100);
    };

    const unsubscribe = sseRegistry.subscribe({
      url,
      onMessage: () => {
        reconnectingRef.current = false;
        hasConnectedRef.current = true;
        invalidate();
      },
      onError: () => {
        reconnectingRef.current = true;
        invalidate();
      },
    });

    return () => {
      unsubscribe();
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
    };
  }, [url, queryKey, queryClient]);

  return { reconnecting: reconnectingRef.current };
}
