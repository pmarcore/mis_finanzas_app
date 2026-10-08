import type { ISODate, Month } from '../api/types';

const arsFormatter = new Intl.NumberFormat('es-AR', {
  maximumFractionDigits: 0,
  minimumFractionDigits: 0,
});

/** 1234567 → "$ 1.234.567"; -500 → "-$ 500". Redondea a pesos enteros. */
export function formatARS(n: number): string {
  const rounded = Math.round(n);
  // Agrupación manual de miles como respaldo si el motor JS no trae datos de locale es-AR.
  const abs = Math.abs(rounded);
  let body = arsFormatter.format(abs);
  if (!body.includes('.') && abs >= 1000) body = abs.toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return `${rounded < 0 ? '-' : ''}$ ${body}`;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** Fecha local de hoy como 'YYYY-MM-DD' (sin pasar por UTC). */
export function todayISO(): ISODate {
  return toISODate(new Date());
}

export function toISODate(d: Date): ISODate {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** 'YYYY-MM-DD' → Date local (medianoche local). */
export function parseISODate(s: ISODate): Date {
  const [y, m, d] = s.split('-').map(Number);
  return new Date(y, (m ?? 1) - 1, d ?? 1);
}

export function isValidISODate(s: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return false;
  const d = parseISODate(s);
  return toISODate(d) === s;
}

/** 'YYYY-MM-DD' → 'dd/mm/aaaa'. */
export function formatDate(s: ISODate): string {
  const [y, m, d] = s.split('-');
  return `${d}/${m}/${y}`;
}

/** 'YYYY-MM-DD' | 'YYYY-MM' → 'YYYY-MM'. */
export function monthOf(s: ISODate | Month): Month {
  return s.slice(0, 7);
}

const MONTHS = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

/** 'YYYY-MM' → 'oct 2026'. */
export function formatMonth(m: Month): string {
  const [y, mm] = m.split('-').map(Number);
  return `${MONTHS[(mm ?? 1) - 1]} ${y}`;
}

/** Suma n meses a 'YYYY-MM'. */
export function addMonths(m: Month, n: number): Month {
  const [y, mm] = m.split('-').map(Number);
  const d = new Date(y, (mm ?? 1) - 1 + n, 1);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}`;
}

export function formatPct(p: number): string {
  return `${Math.round(p)} %`;
}
