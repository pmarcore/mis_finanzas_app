import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ApiError, api, setAuthToken, setUnauthorizedHandler } from '../api/client';
import { loadSession, saveSession, type StoredSession } from '../utils/tokenStorage';

/**
 * Sesión de la app. "open" = la API no pide login (ambiente viejo sin usuarios configurados).
 * Con una sesión guardada se entra directo; si la API la rechaza (vencida o contraseña cambiada) vuelve al login.
 */
type Status = 'loading' | 'signedOut' | 'signedIn' | 'open';

interface LoginResponse {
  token: string;
  username: string;
  displayName: string;
}

interface AuthState {
  status: Status;
  user: StoredSession | null;
  /** Mensaje para la pantalla de login (sesión vencida, sin conexión). */
  notice: string | null;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<StoredSession | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const signOut = useCallback(
    async (message: string | null) => {
      setAuthToken(null);
      await saveSession(null);
      queryClient.clear();
      setUser(null);
      setNotice(message);
      setStatus('signedOut');
    },
    [queryClient],
  );

  useEffect(() => {
    setUnauthorizedHandler(() => void signOut('Tu sesión venció. Volvé a entrar.'));
    return () => setUnauthorizedHandler(null);
  }, [signOut]);

  useEffect(() => {
    (async () => {
      const saved = await loadSession();
      setAuthToken(saved?.token ?? null);
      try {
        const me = await api.get<{ required?: boolean; username?: string; displayName?: string }>('/auth/me');
        if (me.required === false) {
          setStatus('open');
          return;
        }
        if (saved && me.username) {
          const fresh = { ...saved, username: me.username, displayName: me.displayName ?? saved.displayName };
          await saveSession(fresh);
          setUser(fresh);
          setStatus('signedIn');
        } else await signOut(null);
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) await signOut(saved ? 'Tu sesión venció. Volvé a entrar.' : null);
        else if (saved) {
          // Sin conexión con la API: se entra igual y cada pantalla muestra su error con "Reintentar".
          setUser(saved);
          setStatus('signedIn');
        } else await signOut(e instanceof Error ? e.message : null);
      }
    })();
  }, [signOut]);

  const login = useCallback(async (username: string, password: string) => {
    const res = await api.post<LoginResponse>('/auth/login', { username, password });
    const session = { token: res.token, username: res.username, displayName: res.displayName };
    setAuthToken(session.token);
    await saveSession(session);
    setUser(session);
    setNotice(null);
    setStatus('signedIn');
  }, []);

  const logout = useCallback(() => signOut(null), [signOut]);

  const value = useMemo(() => ({ status, user, notice, login, logout }), [status, user, notice, login, logout]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth fuera de AuthProvider');
  return ctx;
}
