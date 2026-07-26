import { useCallback, useState } from 'react';

export function useConfirm() {
  const [state, setState] = useState<{
    open: boolean;
    title: string;
    message: string;
    onConfirm: (() => void) | null;
  }>({ open: false, title: '', message: '', onConfirm: null });

  const ask = useCallback(
    (title: string, message: string): Promise<boolean> => {
      return new Promise((resolve) => {
        setState({
          open: true,
          title,
          message,
          onConfirm: () => {
            resolve(true);
            setState((s) => ({ ...s, open: false, onConfirm: null }));
          },
        });
      });
    },
    []
  );

  const close = useCallback(() => {
    setState((s) => ({ ...s, open: false, onConfirm: null }));
  }, []);

  return { state, ask, close };
}
