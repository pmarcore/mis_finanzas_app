// Wrapper mínimo sobre fetch para hablar con el backend .NET.

export const API_URL = (process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5080').replace(/\/+$/, '');

// TODO: reemplazar por el token real cuando exista autenticación.
let authToken: string | null = null;
export function setAuthToken(token: string | null) {
  authToken = token;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly body?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

type Query = Record<string, string | number | boolean | undefined | null>;

function buildUrl(path: string, query?: Query): string {
  const url = `${API_URL}${path.startsWith('/') ? path : `/${path}`}`;
  if (!query) return url;
  const params = Object.entries(query)
    .filter(([, v]) => v !== undefined && v !== null && v !== '')
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    .join('&');
  return params ? `${url}?${params}` : url;
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  query?: Query;
  body?: unknown;
  signal?: AbortSignal;
}

export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json';
  if (authToken) headers.Authorization = `Bearer ${authToken}`;

  let res: Response;
  try {
    res = await fetch(buildUrl(path, opts.query), {
      method: opts.method ?? 'GET',
      headers,
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
      signal: opts.signal,
    });
  } catch (e) {
    if (opts.signal?.aborted) throw e;
    // Sin respuesta del servidor: mostrar a qué URL se intentó llegar ayuda a detectar un .env viejo o una IP equivocada.
    throw new ApiError(0, `No se pudo conectar con la API en ${API_URL}.`);
  }

  const text = await res.text();
  let data: unknown = undefined;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }

  if (!res.ok) {
    // El backend responde los 400/404 con un string en español (Results.BadRequest("...")),
    // o con ProblemDetails ({ title, detail }) en errores del framework.
    let message: string | undefined;
    if (typeof data === 'string' && data.trim()) message = data;
    else if (data && typeof data === 'object') {
      const o = data as { detail?: unknown; title?: unknown };
      if (typeof o.detail === 'string') message = o.detail;
      else if (typeof o.title === 'string') message = o.title;
    }
    message ??= `Error ${res.status} en ${path}`;
    throw new ApiError(res.status, message, data);
  }
  return data as T;
}

export const api = {
  get: <T>(path: string, query?: Query, signal?: AbortSignal) =>
    request<T>(path, { query, signal }),
  post: <T>(path: string, body?: unknown) => request<T>(path, { method: 'POST', body }),
  put: <T>(path: string, body?: unknown) => request<T>(path, { method: 'PUT', body }),
  delete: <T = void>(path: string) => request<T>(path, { method: 'DELETE' }),
};
