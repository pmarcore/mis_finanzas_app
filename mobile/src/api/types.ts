// Tipos del dominio, espejo de los DTOs del backend .NET (Finanzas.Api/Dtos.cs y Endpoints.cs).
// JSON en camelCase; enums como strings camelCase; ids numéricos.
// Montos en pesos (number, hasta 2 decimales). Fechas 'YYYY-MM-DD'; meses 'YYYY-MM'.
// El cliente NO calcula saldos ni proyecciones: todo viene del backend.

export type ISODate = string; // 'YYYY-MM-DD'
export type Month = string; // 'YYYY-MM'
export type Id = number;

/** Familia y Memey se ven siempre por separado: no hay vista total. */
export type Scope = 'familia' | 'memey';
export const SCOPES: Scope[] = ['familia', 'memey'];

export const SCOPE_LABELS: Record<Scope, string> = {
  familia: 'Familia',
  memey: 'Memey',
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

export const PAYMENT_METHOD_TYPE_LABELS: Record<PaymentMethodType, string> = {
  debito: 'Débito / transferencia',
  efectivo: 'Efectivo',
  billetera: 'Billetera virtual',
  tarjeta: 'Tarjeta de crédito',
};

export interface PaymentMethod {
  id: Id;
  name: string;
  type: PaymentMethodType;
  cashBoxId: Id;
  isCard: boolean;
  /** Inactivo: no se ofrece al cargar; los movimientos existentes lo conservan. */
  active: boolean;
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
  active: boolean;
}

export const CATEGORY_KIND_LABELS: Record<CategoryKind, string> = {
  ingreso: 'Ingreso',
  gasto: 'Gasto',
};

/** Comportamiento de un tipo de operación. Los tipos en sí se configuran (GET /operation-types). */
export type OperationKind = 'movimiento' | 'pagoTarjeta' | 'transferencia' | 'ahorro' | 'inversion';
export type CashEffect = 'suma' | 'resta';

export const OPERATION_KIND_LABELS: Record<OperationKind, string> = {
  movimiento: 'Ingreso / gasto',
  pagoTarjeta: 'Pago de tarjeta',
  transferencia: 'Transferencia entre cajas',
  ahorro: 'Ahorro',
  inversion: 'Inversión',
};

export const CASH_EFFECT_LABELS: Record<CashEffect, string> = {
  suma: 'Suma a la caja',
  resta: 'Resta de la caja',
};

export interface OperationType {
  id: Id;
  name: string;
  kind: OperationKind;
  /** En transferencias no aplica: sale de una caja y entra en la otra. */
  effect: CashEffect;
  /** Ámbitos donde se ofrece al cargar. */
  scopes: Scope[];
  /** Solo transferencias: caja que recibe. */
  transferTo: Scope | null;
  active: boolean;
}

export type NewOperationType = Omit<OperationType, 'id'>;

export type TransactionStatus = 'realizado' | 'previsto';

export const STATUS_LABELS: Record<TransactionStatus, string> = {
  realizado: 'Realizado',
  previsto: 'Previsto',
};

export interface Transaction {
  id: Id;
  scope: Scope;
  personId: Id;
  operationTypeId: Id;
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
 * - Tipo de operación `pagoTarjeta`: `paidCardId` y `firstDueDate` (vencimiento que se paga) son obligatorios.
 */
export interface NewTransaction {
  scope: Scope;
  personId: Id;
  operationTypeId: Id;
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

/**
 * Cambio desde un mes: un monto fijo, o un incremento % (increasePercent) sobre el monto vigente.
 * En los incrementos el backend devuelve en amount el monto resultante; al guardar lo ignora.
 */
export interface RecurringAmount {
  fromMonth: Month;
  amount: number;
  increasePercent?: number | null;
}

export interface RecurringRule {
  id: Id;
  scope: Scope;
  personId: Id;
  operationTypeId: Id;
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
  scope: Scope;
  cashNow: number;
  projectedEndOfMonth: number;
  quarter: { month: Month; balance: number }[];
  estimatedMonthlySpend: { month: Month; amount: number }[];
  memey: { cashNow: number; projectedEndOfMonth: number };
  cardPayments: CardPayment[];
}

/**
 * Línea de GET /cashflow. `movimiento` trae el movimiento; `resumenTarjeta` es lo que falta pagar de un resumen
 * (las compras con tarjeta impactan en caja en su vencimiento); `gastoEstimado` es el presupuesto no cubierto por fijos.
 */
export interface CashFlowLine {
  date: ISODate;
  kind: 'movimiento' | 'resumenTarjeta' | 'gastoEstimado';
  transactionId: Id | null;
  scope: Scope | null;
  operationTypeId: Id | null;
  categoryId: Id | null;
  status: TransactionStatus | null;
  /** Tarjeta del resumen pendiente (`resumenTarjeta`). */
  cardId: Id | null;
  /** Tarjeta que paga un movimiento de pago de tarjeta. */
  paidCardId: Id | null;
  description: string;
  amount: number;
  /** Saldo acumulado después de esta línea. */
  balance: number;
  /** false: realizado hasta el corte (cuenta en la caja actual); true: después del corte, hasta `until`. */
  pending: boolean;
}

/** Flujo de caja del mes del corte. `cashNow` = caja actual del dashboard; `projected` = caja a fin de mes. */
export interface CashFlow {
  scope: Scope;
  from: ISODate;
  cutoff: ISODate;
  until: ISODate;
  opening: number;
  cashNow: number;
  projected: number;
  lines: CashFlowLine[];
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

/** Movimiento hacia (monto positivo) o desde (negativo) el ahorro o las inversiones. */
export interface SavingsMovement {
  id: Id;
  date: ISODate;
  operationTypeId: Id;
  description: string;
  amount: number;
  status: TransactionStatus;
}

/** GET /savings: saldos aparte de la caja. `marketValue` llega cuando haya un proveedor conectado (Invertir Online). */
export interface Savings {
  scope: Scope;
  cutoff: ISODate;
  ahorro: { balance: number; movements: SavingsMovement[] };
  inversion: {
    balance: number;
    marketValue: number | null;
    marketValueDate: ISODate | null;
    provider: string;
    providerConnected: boolean;
    movements: SavingsMovement[];
  };
}
