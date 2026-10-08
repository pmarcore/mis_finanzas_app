// Hooks de react-query por endpoint. Sin lógica de negocio: sólo transporte y cache.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import type {
  Budget,
  BudgetVsActualRow,
  CardCycle,
  CardDueDate,
  Category,
  Dashboard,
  ISODate,
  Month,
  NewRecurringRule,
  NewTransaction,
  PaymentMethod,
  Person,
  RecurringRule,
  ScopeView,
  Settings,
  Transaction,
} from './types';

export const queryKeys = {
  dashboard: (scope: ScopeView, date: ISODate) => ['dashboard', scope, date] as const,
  transactions: (filters: TransactionFilters) => ['transactions', filters] as const,
  cardDueDate: (cardId: string, purchaseDate: ISODate) => ['cards', cardId, 'due-date', purchaseDate] as const,
  cardCycles: (cardId: string) => ['cards', cardId, 'cycles'] as const,
  recurringRules: (scope?: ScopeView) => ['recurring-rules', scope ?? 'all'] as const,
  budgets: (month: Month) => ['budgets', month] as const,
  budgetVsActual: (month: Month, cutoff: ISODate) => ['reports', 'budget-vs-actual', month, cutoff] as const,
  settings: ['settings'] as const,
  people: ['people'] as const,
  categories: ['categories'] as const,
  paymentMethods: ['payment-methods'] as const,
};

// ---------- Dashboard ----------
export function useDashboard(scope: ScopeView, date: ISODate) {
  return useQuery({
    queryKey: queryKeys.dashboard(scope, date),
    queryFn: ({ signal }) => api.get<Dashboard>('/dashboard', { scope, date }, signal),
  });
}

// ---------- Movimientos ----------
export interface TransactionFilters {
  scope?: ScopeView;
  from?: ISODate;
  to?: ISODate;
  status?: Transaction['status'];
}

export function useTransactions(filters: TransactionFilters = {}) {
  return useQuery({
    queryKey: queryKeys.transactions(filters),
    queryFn: ({ signal }) => api.get<Transaction[]>('/transactions', { ...filters }, signal),
  });
}

/** Invalida todo lo que depende de movimientos (saldos, reportes, listados). */
function useInvalidateMovements() {
  const qc = useQueryClient();
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: ['transactions'] }),
      qc.invalidateQueries({ queryKey: ['dashboard'] }),
      qc.invalidateQueries({ queryKey: ['reports'] }),
    ]);
}

export function useCreateTransaction() {
  const invalidate = useInvalidateMovements();
  return useMutation({
    mutationFn: (tx: NewTransaction) => api.post<Transaction>('/transactions', tx),
    onSuccess: invalidate,
  });
}

export function useConfirmTransaction() {
  const invalidate = useInvalidateMovements();
  return useMutation({
    mutationFn: (id: string) => api.post<Transaction>(`/transactions/${encodeURIComponent(id)}/confirm`),
    onSuccess: invalidate,
  });
}

// ---------- Tarjetas ----------
export function useCardDueDate(cardId: string | undefined, purchaseDate: ISODate | undefined) {
  return useQuery({
    queryKey: queryKeys.cardDueDate(cardId ?? '', purchaseDate ?? ''),
    queryFn: ({ signal }) =>
      api.get<CardDueDate>(`/cards/${encodeURIComponent(cardId!)}/due-date`, { purchaseDate }, signal),
    enabled: !!cardId && !!purchaseDate,
  });
}

export function useCardCycles(cardId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cardCycles(cardId ?? ''),
    queryFn: ({ signal }) => api.get<CardCycle[]>(`/cards/${encodeURIComponent(cardId!)}/cycles`, undefined, signal),
    enabled: !!cardId,
  });
}

export function useUpdateCardCycles(cardId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (cycles: CardCycle[]) =>
      api.put<CardCycle[]>(`/cards/${encodeURIComponent(cardId)}/cycles`, cycles),
    onSuccess: () =>
      Promise.all([
        qc.invalidateQueries({ queryKey: queryKeys.cardCycles(cardId) }),
        qc.invalidateQueries({ queryKey: ['cards', cardId, 'due-date'] }),
        qc.invalidateQueries({ queryKey: ['dashboard'] }),
      ]),
  });
}

// ---------- Fijos (reglas recurrentes) ----------
export function useRecurringRules(scope?: ScopeView) {
  return useQuery({
    queryKey: queryKeys.recurringRules(scope),
    queryFn: ({ signal }) => api.get<RecurringRule[]>('/recurring-rules', { scope }, signal),
  });
}

function useInvalidateRules() {
  const qc = useQueryClient();
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: ['recurring-rules'] }),
      qc.invalidateQueries({ queryKey: ['transactions'] }),
      qc.invalidateQueries({ queryKey: ['dashboard'] }),
    ]);
}

export function useCreateRecurringRule() {
  const invalidate = useInvalidateRules();
  return useMutation({
    mutationFn: (rule: NewRecurringRule) => api.post<RecurringRule>('/recurring-rules', rule),
    onSuccess: invalidate,
  });
}

export function useUpdateRecurringRule() {
  const invalidate = useInvalidateRules();
  return useMutation({
    mutationFn: (rule: RecurringRule) =>
      api.put<RecurringRule>(`/recurring-rules/${encodeURIComponent(rule.id)}`, rule),
    onSuccess: invalidate,
  });
}

// ---------- Presupuesto ----------
export function useBudgets(month: Month) {
  return useQuery({
    queryKey: queryKeys.budgets(month),
    queryFn: ({ signal }) => api.get<Budget[]>(`/budgets/${month}`, undefined, signal),
  });
}

export function useUpdateBudgets(month: Month) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (budgets: Budget[]) => api.put<Budget[]>(`/budgets/${month}`, budgets),
    onSuccess: () =>
      Promise.all([
        qc.invalidateQueries({ queryKey: queryKeys.budgets(month) }),
        qc.invalidateQueries({ queryKey: ['reports'] }),
      ]),
  });
}

// ---------- Reportes ----------
export function useBudgetVsActual(month: Month, cutoff: ISODate) {
  return useQuery({
    queryKey: queryKeys.budgetVsActual(month, cutoff),
    queryFn: ({ signal }) =>
      api.get<BudgetVsActualRow[]>('/reports/budget-vs-actual', { month, cutoff }, signal),
  });
}

// ---------- Configuración ----------
export function useSettings() {
  return useQuery({
    queryKey: queryKeys.settings,
    queryFn: ({ signal }) => api.get<Settings>('/settings', undefined, signal),
  });
}

export function useUpdateSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (settings: Settings) => api.put<Settings>('/settings', settings),
    onSuccess: () =>
      Promise.all([
        qc.invalidateQueries({ queryKey: queryKeys.settings }),
        qc.invalidateQueries({ queryKey: ['dashboard'] }),
      ]),
  });
}

// ---------- Catálogos ----------
// SUPUESTO: estos endpoints no estaban en la especificación inicial; los formularios los necesitan.
// Ajustar rutas si el backend los expone de otra forma (p.ej. dentro de /settings).
export function usePeople() {
  return useQuery({
    queryKey: queryKeys.people,
    queryFn: ({ signal }) => api.get<Person[]>('/people', undefined, signal),
    staleTime: 5 * 60_000,
  });
}

export function useCategories() {
  return useQuery({
    queryKey: queryKeys.categories,
    queryFn: ({ signal }) => api.get<Category[]>('/categories', undefined, signal),
    staleTime: 5 * 60_000,
  });
}

export function usePaymentMethods() {
  return useQuery({
    queryKey: queryKeys.paymentMethods,
    queryFn: ({ signal }) => api.get<PaymentMethod[]>('/payment-methods', undefined, signal),
    staleTime: 5 * 60_000,
  });
}
