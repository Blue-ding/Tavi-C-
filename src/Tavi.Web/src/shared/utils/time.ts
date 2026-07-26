export function elapsedSeconds(start: number): number {
  return Math.floor((Date.now() - start) / 1000);
}
