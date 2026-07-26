type Subscription = {
  url: string;
  onMessage: (event: MessageEvent) => void;
  onError: (event: Event) => void;
};

class SseSubscriptionRegistry {
  private sources = new Map<string, EventSource>();
  private refCounts = new Map<string, number>();
  private handlers = new Map<
    string,
    { onMessage: (event: MessageEvent) => void; onError: (event: Event) => void }
  >();

  subscribe(sub: Subscription): () => void {
    const key = sub.url;
    this.handlers.set(key, { onMessage: sub.onMessage, onError: sub.onError });

    const count = this.refCounts.get(key) ?? 0;
    this.refCounts.set(key, count + 1);

    if (count === 0) {
      this.connect(key);
    } else {
      const existing = this.sources.get(key);
      if (existing && existing.readyState === EventSource.OPEN) {
        // Already connected
      }
    }

    return () => this.unsubscribe(key);
  }

  private connect(url: string): void {
    const source = new EventSource(url);
    this.sources.set(url, source);

    source.onmessage = (event) => {
      const handler = this.handlers.get(url);
      handler?.onMessage(event);
    };

    source.onerror = (event) => {
      const handler = this.handlers.get(url);
      handler?.onError(event);
    };
  }

  private unsubscribe(url: string): void {
    const count = (this.refCounts.get(url) ?? 1) - 1;
    if (count <= 0) {
      this.refCounts.delete(url);
      this.handlers.delete(url);
      const source = this.sources.get(url);
      if (source) {
        source.close();
        this.sources.delete(url);
      }
    } else {
      this.refCounts.set(url, count);
    }
  }
}

export const sseRegistry = new SseSubscriptionRegistry();
