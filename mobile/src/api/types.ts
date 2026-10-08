// Tipos del dominio, espejo de los DTOs del backend .NET (Finanzas.Api/Dtos.cs y Endpoints.cs).
// JSON en camelCase; enums como strings camelCase; ids numéricos.
// Montos en pesos (number, hasta 2 decimales). Fechas 'YYYY-MM-DD'; meses 'YYYY-MM'.
// El cliente NO calcula saldos ni proyecciones: todo viene del backend.

export type ISODate = string; // 'YYYY-MM-DD'
export type Month = string; // 'YYYY-MM'
export type Id = number;

export type Scope = 'familia' | 'memey';
/** Vista seleccionada en la app: un ámbito o la suma de ambos (query param `scope=total`). */
export type ScopeView = Scope | 'total';

export const SCOPE_LABELS: Record<ScopeView, string> = {
  familia: 'Familia',
  memey: 'Memey',
  total: 'Total',
};

export interface Person {
  id: Id;
  name: string;
}

export interface CashBox {
  id: Id;
  scope: Scope;
  name: string;
  openingBalance: number;
  openingDate: ISODate;
  observedBalance?: number | null;
  observedDate?: ISODate | null;
}

export type PaymentMethodType = 'debito' | 'efectivo' | 'billetera' | 'tarjeta';

export interface PaymentMethod {
  id: Id;
  name: string;
  type: PaymentMethodType;
  cashBoxId: Id;
  isCard: boolean;
}

export interface CardCycle {
  cardId: Id;
  month: Month;
  closingDate: ISODate;
  dueDate: ISODate;
}

export type CategoryKind = 'ingreso' | 'gasto';
export type BudgetType = 'fijo' | 'variable' | 'deuda';

export const BUDGET_TYPE_LABELS: Record<BudgetType, string> = {
  fijo: 'Fijo',
  variable: 'Variable',
  deuda: 'Deuda',
};

export interface Category {
  id: Id;
  name: string;
  scope: Scope;
  kind: CategoryKind;
  budgetType: BudgetType;
}

export const OPERATIONS = [
  'ingreso',
  'gasto',
  'pagoTarjeta',
  'cuotaAuto',
  'ahorro',
  'aporteAMemey',
  'retiroDeMemey',
] as const;
export type Operation = (typeof OPERATIONS)[number];

/** Etiquetas para mostrar/elegir operaciones (el valor de la API es la clave). */
export const OPERATION_LABELS: Record<Operation, string> = {
  ingreso: 'Ingreso',
  gasto: 'Gasto',
  pagoTarjeta: 'Pago tarjeta',
  cuotaAuto: 'Cuota auto',
  ahorro: 'Ahorro',
  aporteAMemey: 'Aporte a Memey',
  retiroDeMemey: 'Retiro de Memey',
};

export type TransactionStatus = 'realizado' | 'previsto';

export const STATUS_LABELS: Record<TransactionStatus, string> = {
  realizado: 'Realizado',
  previsto: 'Previsto',
};

export interface Transaction {
  id: Id;
  scope: Scope;
  personId: Id;
  operation: Operation;
  date: ISODate;
  description: string;
  categoryId: Id | null;
  amount: number;
  paymentMethodId: Id;
  installments: number;
  firstDueDate?: ISODate | null;
  status: TransactionStatus;
  recurringRuleId?: Id | null;
  paidCardId?: Id | null;
  isPriorCommitment: boolean;
}

/**
 * Payload de POST /transactions.
 * - Compra con tarjeta: `firstDueDate` puede omitirse (el backend lo propone según los cierres).
 * - `pagoTarjeta`: `paidCardId` y `firstDueDate` (vencimiento que se paga) son obligatorios.
 */
export interface NewTransaction {
  scope: Scope;
  personId: Id;
  operation: Operation;
  date: ISODate;
  description: string;
  categoryId?: Id | null;
  amount: number;
  paymentMethodId: Id;
  installments: number;
  firstDueDate?: ISODate;
  status?: TransactionStatus;
  paidCardId?: Id;
}

/** Body opcional de POST /transactions/{id}/confirm. */
export interface ConfirmTransaction {
  date?: ISODate;
  amount?: number;
}

export interface RecurringAmount {
  fromMonth: Month;
  amount: number;
}

export interface RecurringRule {
  id: Id;
  scope: Scope;
  personId: Id;
  operation: Operation;
  description: string;
  categoryId: Id;
  paymentMethodId: Id;
  dayOfMonth: number;
  startMonth: Month;
  endMonth?: Month | null;
  amounts: RecurringAmount[];
}

/** En el POST el backend ignora el id. */
export type NewRecurringRule = Omit<RecurringRule, 'id'> & { id?: Id };

/** Respuesta de POST/PUT /recurring-rules: meses sin cierre de tarjeta cargado. */
export interface RecurringRuleSaveResult {
  rule: RecurringRule;
  missingCycles: Month[];
}

export interface Budget {
  month: Month;
  categoryId: Id;
  amount: number;
}

export interface CardPaymentByCard {
  cardId: Id;
  card: string;
  dueDate: ISODate;
  total: number;
}

export interface CardPayment {
  dueDate: ISODate;
  total: number;
  cards: string[];
  byCard: CardPaymentByCard[];
}

export interface Dashboard {
  cutoff: ISODate;
  scope: ScopeView;
  cashNow: number;
  projectedEndOfMonth: number;
  freeCash: number;
  quarter: { month: Month; balance: number }[];
  estimatedMonthlySpend: { month: Month; amount: number }[];
  memey: { cashNow: number; projectedEndOfMonth: number };
  cardPayments: CardPayment[];
  alerts: string[];
}

export type BudgetAlert = 'Normal' | 'Preventiva' | 'Control' | 'Excedido' | 'Critico' | 'Proyecta exceso';

export interface BudgetVsActualRow {
  categoryId: Id;
  category: string;
  budgetType: BudgetType;
  budget: number;
  actual: number;
  available: number;
  usePct: number;
  projected: number;
  alert: BudgetAlert;
}

export interface BudgetVsActual {
  month: Month;
  cutoff: ISODate;
  scope: Scope;
  rows: BudgetVsActualRow[];
  totalBudget: number;
  totalActual: number;
  totalProjected: number;
}

export interface CardDueDate {
  cardId: Id;
  purchaseDate: ISODate;
  firstDueDate: ISODate;
}

export interface Thresholds {
  cashFloor: number;
  savingsGoal: number;
  alertAttentionPct: number;
  alertControlPct: number;
  alertExceededPct: number;
  alertCriticalPct: number;
}

/** GET /settings. PUT /settings recibe sólo `UpdateSettings`. */
export interface Settings extends Thresholds {
  cashBoxes: CashBox[];
}

export type UpdateSettings = Thresholds;
