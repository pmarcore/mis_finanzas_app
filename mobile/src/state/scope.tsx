import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import type { ISODate, ScopeView } from '../api/types';
import { todayISO } from '../utils/format';

interface ScopeState {
  scope: ScopeView;
  setScope: (s: ScopeView) => void;
  /** Fecha de corte para dashboard y reportes. Por defecto: hoy (fecha local). */
  cutoff: ISODate;
  setCutoff: (d: ISODate) => void;
  resetCutoff: () => void;
}

const ScopeContext = createContext<ScopeState | null>(null);

export function ScopeProvider({ children }: { children: ReactNode }) {
  const [scope, setScope] = useState<ScopeView>('familia');
  const [cutoff, setCutoff] = useState<ISODate>(todayISO);

  const value = useMemo<ScopeState>(
    () => ({ scope, setScope, cutoff, setCutoff, resetCutoff: () => setCutoff(todayISO()) }),
    [scope, cutoff],
  );
  return <ScopeContext.Provider value={value}>{children}</ScopeContext.Provider>;
}

export function useScope(): ScopeState {
  const ctx = useContext(ScopeContext);
  if (!ctx) throw new Error('useScope debe usarse dentro de <ScopeProvider>');
  return ctx;
}
