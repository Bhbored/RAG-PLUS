import Bottleneck from 'bottleneck';

const limiters = new Map<string, Bottleneck>();

const DEFAULT_MIN_TIME = 2000; // 2s between requests (respects crawl-delay)

export function getDomainLimiter(domain: string, minTimeMs?: number): Bottleneck {
  const key = domain.toLowerCase();
  if (!limiters.has(key)) {
    limiters.set(
      key,
      new Bottleneck({
        minTime: minTimeMs ?? DEFAULT_MIN_TIME,
        maxConcurrent: 1,
        reservoir: 60,        // max 60 requests
        reservoirRefreshAmount: 60,
        reservoirRefreshInterval: 60 * 1000, // per minute
      })
    );
  }
  return limiters.get(key)!;
}

export async function destroyAllLimiters(): Promise<void> {
  for (const limiter of limiters.values()) {
    await limiter.stop();
  }
  limiters.clear();
}
