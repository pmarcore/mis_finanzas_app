// Tipos del dominio, espejo de los DTOs del backend .NET.
// Montos en pesos (number). Fechas como string ISO 'YYYY-MM-DD'; meses como 'YYYY-MM'.
// El cliente NO calcula saldos ni proyecciones: todo viene del backend.

export type ISODate = string; // 'YYYY-MM-DD'
export type Month = string; // 'YYYY-MM'

export type Scope = 'familia' | 'memey';
/** Vista seleccionada en la app: un ámbito o la suma de ambos. */
export type ScopeView = Scope | 'total';

export interface Person {
  id: string;
  name: string;
}

export interface CashBox {
  id: string;
  scope: Scope;
  name: string;
  openingBalance: number;
  openingDate: ISODate;
  observedBalance?: number;
}

export type PaymentMethodType = 'debito' | 'efectivo' | 'billetera' | 'tarjeta';

export interface PaymentMethod {
  id: string;
  name: string;
  type: PaymentMethodType;
  cashBoxId: string;
}

export interface CardCycle {
  cardId: string;
  month: Month;
  closingDate: ISODate;
  dueDate: ISODate;
}

export type CategoryKind = 'ingreso' | 'gasto';
export type BudgetType = 'Fijo' | 'Variable' | 'Deuda';

export interface Category {
  id: string;
  name: string;
  kind: CategoryKind;
  budgetType: BudgetType;
  scope: Scope;
}

export const OPERATIONS = [
  'Ingreso',
  'Gasto',
  'Pago tarjeta',
  'Cuota auto',
  'Ahorro',
  'Aporte a Memey',
  'Retiro de Memey',
] as const;
export type Operation = (typeof OPERATIONS)[number];

export type TransactionStatus = 'Realizado' | 'Previsto';

export interface Transaction {
  id: string;
  scope: Scope;
  personId: string;
  operation: Operation;
  date: ISODate;
  description: string;
  categoryId: string;
  amount: number;
  paymentMethodId: string;
  installments: number;
  firstDueDate?: ISODate;
  status: TransactionStatus;
  recurringRuleId?: string;
}

/** Payload para crear un movimiento (el backend asigna id y estado). */
export type NewTransaction = Omit<Transaction, 'id' | 'status' | 'recurringRuleId'> & {
  status?: TransactionStatus;
};

export interface RecurringAmount {
  fromMonth: Month;
  amount: number;
}

export interface RecurringRule {
  id: string;
  scope: Scope;
  personId: string;
  operation: Operation;
  categoryId: string;
  paymentMethodId: string;
  dayOfMonth: number;
  startMonth: Month;
  endMonth?: Month;
  amounts: RecurringAmount[];
}

export type NewRecurringRule = Omit<RecurringRule, 'id'>;

export interface Budget {
  month: Month;
  categoryId: string;
  amount: number;
}

export interface Dashboard {
  cutoff: ISODate;
  cashNow: number;
  projectedEndOfMonth: number;
  freeCash: number;
  quarter: { month: Month; balance: number }[];
  memey: { cashNow: number; projectedEndOfMonth: number };
  cardPayments: { dueDate: ISODate; total: number; cards: string[] }[];
  alerts: string[];
}

export interface BudgetVsActualRow {
  categoryId: string;
  category: string;
  budgetType: BudgetType;
  budget: number;
  actual: number;
  usePct: number;
  projected: number;
}

export interface CardDueDate {
  cardId: string;
  purchaseDate: ISODate;
  firstDueDate: ISODate;
}

export interface Settings {
  cashBoxes: CashBox[];
  cashFloor: number;
}
