import { AuthSessionCsrfHeaderName } from "./constants";

export interface HttpResult<T> {
  readonly ok: boolean;
  readonly status: number;
  readonly data?: T;
  readonly error?: unknown;
}

export interface HttpClient {
  get<T>(path: string): Promise<HttpResult<T>>;
  post<T>(path: string, body?: unknown): Promise<HttpResult<T>>;
}

export interface HttpClientOptions {
  readonly baseUrl: string;
  /** Reads the CSRF token issued by the most recent successful `/auth/login`, if any. */
  readonly getCsrfToken: () => string | null;
}

type HttpMethod = "GET" | "POST";

function safeJsonParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

async function performRequest<T>(
  options: HttpClientOptions,
  method: HttpMethod,
  path: string,
  body: unknown,
): Promise<HttpResult<T>> {
  const headers = new Headers();
  if (body !== undefined) {
    headers.set("Content-Type", "application/json");
  }
  if (method !== "GET") {
    const csrfToken = options.getCsrfToken();
    if (csrfToken) {
      headers.set(AuthSessionCsrfHeaderName, csrfToken);
    }
  }

  let response: Response;
  try {
    response = await fetch(`${options.baseUrl}${path}`, {
      method,
      credentials: "include",
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch (networkError) {
    return { ok: false, status: 0, error: networkError };
  }

  const text = await response.text();
  const parsed = text.length > 0 ? safeJsonParse(text) : undefined;

  return response.ok
    ? { ok: true, status: response.status, data: parsed as T }
    : { ok: false, status: response.status, error: parsed };
}

export function createHttpClient(options: HttpClientOptions): HttpClient {
  return {
    get: <T>(path: string) => performRequest<T>(options, "GET", path, undefined),
    post: <T>(path: string, body?: unknown) => performRequest<T>(options, "POST", path, body),
  };
}
