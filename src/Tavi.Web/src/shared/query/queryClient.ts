import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 10_000,
      retry: (failureCount, error) => {
        if (error instanceof Error && error.name === 'HostUnavailableError') {
          return false;
        }
        return failureCount < 2;
      },
      retryDelay: (attemptIndex) => (attemptIndex === 0 ? 300 : 1000),
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: false,
    },
  },
});
