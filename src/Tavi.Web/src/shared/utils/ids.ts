export function generateId(): string {
  return crypto.randomUUID();
}

export function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString('zh-CN');
}
