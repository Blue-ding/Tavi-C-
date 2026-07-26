import { useEffect } from 'react';

export function useHotkeys(
  map: Record<
    string,
    { action: () => void; predicate?: () => boolean }
  >
) {
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const key = [
        e.ctrlKey ? 'Ctrl+' : '',
        e.shiftKey ? 'Shift+' : '',
        e.altKey ? 'Alt+' : '',
        e.key.length === 1 ? e.key.toUpperCase() : e.key,
      ].join('');

      const entry = map[key];
      if (!entry) return;

      if (entry.predicate && !entry.predicate()) return;

      const target = e.target as HTMLElement | null;
      if (
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.isContentEditable)
      ) {
        if (key !== 'Ctrl+S' && key !== 'Ctrl+Z' && key !== 'Ctrl+Shift+Z') {
          return;
        }
      }

      e.preventDefault();
      entry.action();
    };

    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [map]);
}
