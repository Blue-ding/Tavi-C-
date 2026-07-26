import { z } from 'zod';

export type TaviProblem = {
  status: number;
  title: string;
  type?: string;
  error: {
    code: string;
    message: string;
    category: string;
    operation?: string | null;
    isTransient: boolean;
    details: Record<string, string>;
    traceId: string;
  };
};

export class ContractError extends Error {
  constructor(
    message: string,
    public readonly path: string,
    public readonly traceId?: string
  ) {
    super(message);
    this.name = 'ContractError';
  }
}

export class HostUnavailableError extends Error {
  constructor(public readonly path: string) {
    super('本地服务连接中断');
    this.name = 'HostUnavailableError';
  }
}

export class ConflictError extends Error {
  constructor(
    message: string,
    public readonly problem: TaviProblem
  ) {
    super(message);
    this.name = 'ConflictError';
  }
}

const errorSchema = z.object({
  code: z.string(),
  message: z.string(),
  category: z.string(),
  operation: z.string().nullable().optional(),
  isTransient: z.boolean(),
  details: z.record(z.string()),
  traceId: z.string(),
});

const problemSchema = z.object({
  status: z.number(),
  title: z.string(),
  type: z.string().optional(),
  error: errorSchema,
});

function parseProblem(body: unknown): TaviProblem | null {
  const result = problemSchema.safeParse(body);
  return result.success ? result.data : null;
}

export type RequestOptions<T> = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  schema: z.ZodType<T>;
  signal?: AbortSignal;
};

export async function request<T>(path: string, options: RequestOptions<T>): Promise<T> {
  const url = path.startsWith('/') ? path : `/${path}`;
  const headers: Record<string, string> = {};
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  let response: Response;
  try {
    response = await fetch(url, {
      method: options.method ?? 'GET',
      headers,
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
      signal: options.signal,
    });
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') {
      throw e;
    }
    throw new HostUnavailableError(path);
  }

  if (!response.ok) {
    let body: unknown;
    try {
      body = await response.json();
    } catch {
      body = undefined;
    }
    const problem = parseProblem(body);
    if (response.status === 409 && problem) {
      throw new ConflictError(problem.error.message, problem);
    }
    if (problem) {
      throw new Error(`${problem.error.code}: ${problem.error.message}`);
    }
    throw new Error(`HTTP ${response.status}: ${response.statusText}`);
  }

  if (response.status === 204) {
    const result = options.schema.safeParse(undefined);
    if (!result.success) {
      throw new ContractError('Schema mismatch on 204', path);
    }
    return result.data;
  }

  let data: unknown;
  try {
    data = await response.json();
  } catch {
    throw new ContractError('Invalid JSON in response', path);
  }

  const parsed = options.schema.safeParse(data);
  if (!parsed.success) {
    const traceId = response.headers.get('x-trace-id') ?? undefined;
    throw new ContractError(parsed.error.message, path, traceId);
  }

  return parsed.data;
}

export async function requestVoid(
  path: string,
  options: Omit<RequestOptions<void>, 'schema'> & { schema?: z.ZodType<void> }
): Promise<void> {
  await request(path, {
    ...options,
    schema: options.schema ?? z.void(),
  });
}
