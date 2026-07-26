import { useQuery } from '@tanstack/react-query';

export function useOnlineHostStatus(): boolean {
  const { isSuccess, failureCount } = useQuery({
    queryKey: ['host-status'],
    queryFn: async () => {
      const res = await fetch('/api/v1/world/', { method: 'HEAD' });
      return res.ok;
    },
    refetchInterval: 5000,
    retry: false,
    staleTime: 0,
  });

  return isSuccess || failureCount === 0;
}
