// Hooks de react-query por endpoint. Sin lógica de negocio: sólo transporte y cache.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import type {
  Budget,
  BudgetVsActual,
  CardCycle,
  CardDueDate,
  CashBox,
  CashFlow,
  Category,
  ConfirmTransaction,
  Dashboard,
  Id,
  ISODate,
  Month,
  NewRecurringRule,
  NewTransaction,
  OperationType,
  PaymentMethod,
  Person,
  RecurringRule,
  RecurringRuleSaveResult,
  Savings,
  Scope,
  Settings,
  Transaction,
  TransactionStatus,
  UpdateSettings,
} from './types';

export const queryKeys = {
  dashboard: (scope: Scope, date: ISODate) => ['dashboard', scope, date] as const,
  cashFlow: (scope: Scope, date: ISODate) => ['cashflow', scope, date] as const,
  transactions: (filters: TransactionFilters) => ['transactions', filters] as const,
  cardDueDate: (cardId: Id, purchaseDate: ISODate) => ['cards', cardId, 'due-date', purchaseDate] as const,
  cardCycles: (cardId: Id) => ['cards', cardId, 'cycles'] as const,
  recurringRules: (scope?: Scope) => ['recurring-rules', scope ?? 'todos'] as const,
  budgets: (month: Month) => ['budgets', month] as const,
  budgetVsActual: (month: Month, cutoff: ISODate, scope: Scope) =>
    ['reports', 'budget-vs-actual', month, cutoff, scope] as const,
  settings: ['settings'] as const,
  people: ['people'] as const,
  categories: (scope?: Scope) => ['categories', scope ?? 'todos'] as const,
  paymentMethods: ['payment-methods'] as const,
  operationTypes: ['operation-types'] as const,
  savings: (scope: Scope, date: ISODate) => ['savings', scope, date] as const,
};

// ---------- Dashboard ----------
export function useDashboard(scope: Scope, date: ISODate) {
  return useQuery({
    queryKey: queryKeys.dashboard(scope, date),
    queryFn: ({ signal }) => api.get<Dashboard>('/dashboard', { scope, date }, signal),
  });
}

/** Flujo de caja del mes del corte con saldo acumulado (cierra en la caja actual y en la de fin de mes). */
export function useCashFlow(scope: Scope, date: ISODate) {
  return useQuery({
    queryKey: queryKeys.cashFlow(scope, date),
    queryFn: ({ signal }) => api.get<CashFlow>('/cashflow', { scope, date }, signal),
  });
}

// ---------- Movimientos ----------
export interface TransactionFilters {
  scope?: Scope;
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
      qc.invalidateQueries({ queryKey: ['cashflow'] }),
      qc.invalidateQueries({ queryKey: ['reports'] }),
      qc.invalidateQueries({ queryKey: ['savings'] }),
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
        qc.invalidateQueries({ queryKey: ['cashflow'] }),
      ]),
  });
}

// ---------- Fijos (reglas recurrentes) ----------
export function useRecurringRules(scope?: Scope) {
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
      qc.invalidateQueries({ queryKey: ['cashflow'] }),
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
        qc.invalidateQueries({ queryKey: ['cashflow'] }),
      ]),
  });
}

// ---------- Reportes ----------
export function useBudgetVsActual(month: Month, cutoff: ISODate, scope: Scope) {
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
      qc.invalidateQueries({ queryKey: ['cashflow'] }),
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

/** Sin scope trae las categorías de los dos ámbitos (también las inactivas: filtrar al ofrecerlas). */
export function useCategories(scope?: Scope) {
  return useQuery({
    queryKey: queryKeys.categories(scope),
    queryFn: ({ signal }) => api.get<Category[]>('/categories', { scope }, signal),
    staleTime: 5 * 60_000,
  });
}

/** Incluye los inactivos (para mostrar movimientos viejos); filtrar `active` al ofrecerlos. */
export function usePaymentMethods() {
  return useQuery({
    queryKey: queryKeys.paymentMethods,
    queryFn: ({ signal }) => api.get<PaymentMethod[]>('/payment-methods', undefined, signal),
    staleTime: 5 * 60_000,
  });
}

/** Todos los tipos de operación, activos e inactivos. */
export function useOperationTypes() {
  return useQuery({
    queryKey: queryKeys.operationTypes,
    queryFn: ({ signal }) => api.get<OperationType[]>('/operation-types', undefined, signal),
    staleTime: 5 * 60_000,
  });
}

/** Alta/edición/baja de un catálogo. Cambiar el signo de un tipo recalcula sus movimientos: se invalida todo lo que depende. */
function useCatalogMutations<T extends { id: Id }>(path: string, key: readonly unknown[]) {
  const qc = useQueryClient();
  const invalidate = () =>
    Promise.all(
      [key, ['transactions'], ['dashboard'], ['cashflow'], ['reports'], ['savings'], ['recurring-rules'], ['cards']].map(
        (queryKey) => qc.invalidateQueries({ queryKey }),
      ),
    );
  return {
    create: useMutation({
      mutationFn: (item: Omit<T, 'id'>) => api.post<T>(path, item),
      onSuccess: invalidate,
    }),
    update: useMutation({
      mutationFn: (item: T) => api.put<T>(`${path}/${item.id}`, item),
      onSuccess: invalidate,
    }),
    remove: useMutation({
      mutationFn: (id: Id) => api.delete(`${path}/${id}`),
      onSuccess: invalidate,
    }),
  };
}

export const useOperationTypeMutations = () => useCatalogMutations<OperationType>('/operation-types', queryKeys.operationTypes);
export const useCategoryMutations = () => useCatalogMutations<Category>('/categories', ['categories']);
export const usePaymentMethodMutations = () => useCatalogMutations<PaymentMethod>('/payment-methods', queryKeys.paymentMethods);

// ---------- Ahorro e inversión ----------
export function useSavings(scope: Scope, date: ISODate) {
  return useQuery({
    queryKey: queryKeys.savings(scope, date),
    queryFn: ({ signal }) => api.get<Savings>('/savings', { scope, date }, signal),
  });
}
