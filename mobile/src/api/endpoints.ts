// Hooks de react-query por endpoint. Sin lógica de negocio: sólo transporte y cache.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import type {
  Budget,
  BudgetVsActual,
  CardCycle,
  CardDueDate,
  CashBox,
  Category,
  ConfirmTransaction,
  Dashboard,
  Id,
  ISODate,
  Month,
  NewRecurringRule,
  NewTransaction,
  PaymentMethod,
  Person,
  RecurringRule,
  RecurringRuleSaveResult,
  ScopeView,
  Settings,
  Transaction,
  TransactionStatus,
  UpdateSettings,
} from './types';

export const queryKeys = {
  dashboard: (scope: ScopeView, date: ISODate) => ['dashboard', scope, date] as const,
  transactions: (filters: TransactionFilters) => ['transactions', filters] as const,
  cardDueDate: (cardId: Id, purchaseDate: ISODate) => ['cards', cardId, 'due-date', purchaseDate] as const,
  cardCycles: (cardId: Id) => ['cards', cardId, 'cycles'] as const,
  recurringRules: (scope?: ScopeView) => ['recurring-rules', scope ?? 'total'] as const,
  budgets: (month: Month) => ['budgets', month] as const,
  budgetVsActual: (month: Month, cutoff: ISODate, scope: ScopeView) =>
    ['reports', 'budget-vs-actual', month, cutoff, scope] as const,
  settings: ['settings'] as const,
  people: ['people'] as const,
  categories: (scope?: ScopeView) => ['categories', scope ?? 'total'] as const,
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
  /** Inclusivo. */
  from?: ISODate;
  /** Inclusivo. */
  to?: ISODate;
  status?: TransactionStatus;
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
    mutationFn: ({ id, ...body }: { id: Id } & ConfirmTransaction) =>
      api.post<Transaction>(`/transactions/${id}/confirm`, body),
    onSuccess: invalidate,
  });
}

export function useDeleteTransaction() {
  const invalidate = useInvalidateMovements();
  return useMutation({
    mutationFn: (id: Id) => api.delete(`/transactions/${id}`),
    onSuccess: invalidate,
  });
}

// ---------- Tarjetas ----------
/** Primer vencimiento propuesto. 404 (ApiError) si la tarjeta no tiene cierre cargado para esa fecha. */
export function useCardDueDate(cardId: Id | undefined, purchaseDate: ISODate | undefined) {
  return useQuery({
    queryKey: queryKeys.cardDueDate(cardId ?? 0, purchaseDate ?? ''),
    queryFn: ({ signal }) => api.get<CardDueDate>(`/cards/${cardId}/due-date`, { purchaseDate }, signal),
    enabled: cardId !== undefined && !!purchaseDate,
    retry: false,
  });
}

export function useCardCycles(cardId: Id | undefined) {
  return useQuery({
    queryKey: queryKeys.cardCycles(cardId ?? 0),
    queryFn: ({ signal }) => api.get<CardCycle[]>(`/cards/${cardId}/cycles`, undefined, signal),
    enabled: cardId !== undefined,
  });
}

export function useUpdateCardCycles(cardId: Id | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (cycles: CardCycle[]) => api.put<CardCycle[]>(`/cards/${cardId}/cycles`, cycles),
    onSuccess: () =>
      Promise.all([
        qc.invalidateQueries({ queryKey: ['cards', cardId] }),
        // Los gastos fijos con esta tarjeta se regeneran en el backend.
        qc.invalidateQueries({ queryKey: ['transactions'] }),
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
      qc.invalidateQueries({ queryKey: ['reports'] }),
    ]);
}

export function useCreateRecurringRule() {
  const invalidate = useInvalidateRules();
  return useMutation({
    mutationFn: (rule: NewRecurringRule) =>
      api.post<RecurringRuleSaveResult>('/recurring-rules', { ...rule, id: 0 }),
    onSuccess: invalidate,
  });
}

export function useUpdateRecurringRule() {
  const invalidate = useInvalidateRules();
  return useMutation({
    mutationFn: (rule: RecurringRule) => api.put<RecurringRuleSaveResult>(`/recurring-rules/${rule.id}`, rule),
    onSuccess: invalidate,
  });
}

export function useDeleteRecurringRule() {
  const invalidate = useInvalidateRules();
  return useMutation({
    mutationFn: (id: Id) => api.delete(`/recurring-rules/${id}`),
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
        qc.invalidateQueries({ queryKey: ['dashboard'] }),
      ]),
  });
}

// ---------- Reportes ----------
/** `scope=total` (o vacío) el backend lo toma como familia. */
export function useBudgetVsActual(month: Month, cutoff: ISODate, scope: ScopeView) {
  return useQuery({
    queryKey: queryKeys.budgetVsActual(month, cutoff, scope),
    queryFn: ({ signal }) =>
      api.get<BudgetVsActual>('/reports/budget-vs-actual', { month, cutoff, scope }, signal),
  });
}

// ---------- Configuración ----------
export function useSettings() {
  return useQuery({
    queryKey: queryKeys.settings,
    queryFn: ({ signal }) => api.get<Settings>('/settings', undefined, signal),
  });
}

function useInvalidateSettings() {
  const qc = useQueryClient();
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: queryKeys.settings }),
      qc.invalidateQueries({ queryKey: ['dashboard'] }),
      qc.invalidateQueries({ queryKey: ['reports'] }),
    ]);
}

/** PUT /settings: piso de caja, meta de ahorro y umbrales de alerta. Devuelve la configuración completa. */
export function useUpdateSettings() {
  const invalidate = useInvalidateSettings();
  return useMutation({
    mutationFn: (settings: UpdateSettings) => api.put<Settings>('/settings', settings),
    onSuccess: invalidate,
  });
}

/** PUT /settings/cash-boxes/{id}. */
export function useUpdateCashBox() {
  const invalidate = useInvalidateSettings();
  return useMutation({
    mutationFn: (box: CashBox) => api.put<CashBox>(`/settings/cash-boxes/${box.id}`, box),
    onSuccess: invalidate,
  });
}

// ---------- Catálogos ----------
export function usePeople() {
  return useQuery({
    queryKey: queryKeys.people,
    queryFn: ({ signal }) => api.get<Person[]>('/people', undefined, signal),
    staleTime: 5 * 60_000,
  });
}

/** Sin scope (o 'total') trae todas las categorías. */
export function useCategories(scope?: ScopeView) {
  return useQuery({
    queryKey: queryKeys.categories(scope),
    queryFn: ({ signal }) => api.get<Category[]>('/categories', { scope }, signal),
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
