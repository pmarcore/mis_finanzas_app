import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import {
  useCardCycles,
  useCardStatementMonths,
  useCardStatements,
  useCategories,
  usePaymentMethods,
} from '../api/endpoints';
import type { CardStatement, CardStatementLine, CardStatementMonth, Id, Month } from '../api/types';
import { Card } from '../components/Card';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { Select } from '../components/Select';
import type { RootStackScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { scopeColor, spacing, useTheme } from '../theme';
import { formatARS, formatDate, formatMonth, monthOf, todayISO } from '../utils/format';

type Filtro = 'todos' | 'fijos' | 'sinFijos';
const recurringOf: Record<Filtro, boolean | undefined> = { todos: undefined, fijos: true, sinFijos: false };

/**
 * Detalle de los resúmenes de tarjeta por mes de vencimiento: una línea por cuota, de una tarjeta o de todas
 * (vista unificada), con filtro de gastos fijos. Abajo, los cierres y vencimientos de la tarjeta elegida.
 */
export function TarjetasScreen({ navigation }: RootStackScreenProps<'Tarjetas'>) {
  const { colors } = useTheme();
  const { scope } = useScope();
  const accent = scopeColor(colors, scope);
  const methods = usePaymentMethods();
  const cards = useMemo(() => (methods.data ?? []).filter((m) => m.isCard), [methods.data]);
  const categories = useCategories();

  const [cardId, setCardId] = useState<Id | undefined>();
  const [filtro, setFiltro] = useState<Filtro>('todos');
  const [dueMonth, setDueMonth] = useState<Month | undefined>();

  const months = useCardStatementMonths(scope);
  // Por defecto, el próximo vencimiento desde el mes actual (o el último, si no hay futuros).
  useEffect(() => {
    const list = months.data ?? [];
    if (list.length === 0 || (dueMonth && list.some((m) => m.dueMonth === dueMonth))) return;
    const current = monthOf(todayISO());
    setDueMonth((list.find((m) => m.dueMonth >= current) ?? list[list.length - 1]).dueMonth);
  }, [months.data, dueMonth]);

  const detail = useCardStatements(
    dueMonth ? { scope, dueMonth, cardId, recurring: recurringOf[filtro] } : undefined,
  );
  const cycles = useCardCycles(cardId);
  const categoryName = (id: Id | null) => (id === null ? '' : (categories.data?.find((c) => c.id === id)?.name ?? ''));

  const refetch = () => {
    months.refetch();
    detail.refetch();
  };

  return (
    <Screen refreshing={detail.isRefetching} onRefresh={refetch}>
      <ScopeSelector />
      <QueryState
        isLoading={methods.isLoading || months.isLoading}
        error={methods.error ?? months.error}
        onRetry={refetch}
        isEmpty={cards.length === 0}
        emptyText="No hay tarjetas configuradas."
      >
        <View style={styles.filters}>
          <View style={{ flex: 1 }}>
            <Select
              title="Tarjeta"
              items={cards}
              getKey={(c) => c.id}
              getLabel={(c) => c.name}
              selectedKey={cardId}
              onSelect={(c) => setCardId(c.id)}
              placeholder="Todas las tarjetas"
              clearLabel="Todas las tarjetas"
              onClear={() => setCardId(undefined)}
              accent={accent}
            />
          </View>
          <View style={{ flex: 1 }}>
            <Select<CardStatementMonth>
              title="Resumen (mes de vencimiento)"
              items={months.data ?? []}
              getKey={(m) => m.dueMonth}
              getLabel={(m) => formatMonth(m.dueMonth)}
              selectedKey={dueMonth}
              onSelect={(m) => setDueMonth(m.dueMonth)}
              placeholder="Sin resúmenes"
              accent={accent}
            />
          </View>
        </View>
        <Segmented<Filtro>
          value={filtro}
          onChange={setFiltro}
          options={[
            { value: 'todos', label: 'Todos', color: accent },
            { value: 'fijos', label: 'Solo fijos', color: accent },
            { value: 'sinFijos', label: 'Sin fijos', color: accent },
          ]}
        />

        {dueMonth === undefined ? (
          <Text style={{ color: colors.inkMuted }}>No hay resúmenes de tarjeta para {scope === 'memey' ? 'Memey' : 'Familia'}.</Text>
        ) : (
          <QueryState
            isLoading={detail.isLoading}
            error={detail.error}
            onRetry={detail.refetch}
            isEmpty={detail.data?.cards.length === 0}
            emptyText="Esta tarjeta no tiene resumen ese mes."
          >
            {detail.data && (
              <>
                <Card accent={accent}>
                  <View style={styles.line}>
                    <Text style={{ color: colors.ink, fontWeight: '600' }}>
                      {cardId === undefined ? 'Todas las tarjetas' : 'Total'} · {formatMonth(detail.data.dueMonth)}
                    </Text>
                    <MoneyText value={detail.data.total} size="sm" signColor={false} />
                  </View>
                  {filtro !== 'todos' && (
                    <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                      {filtro === 'fijos' ? 'Fijos' : 'Sin fijos'}: {formatARS(detail.data.filteredTotal)} de{' '}
                      {formatARS(detail.data.total)}
                    </Text>
                  )}
                </Card>
                {/* Con filtro, en la vista unificada se ocultan las tarjetas sin líneas que mostrar. */}
                {detail.data.cards
                  .filter((s) => filtro === 'todos' || cardId !== undefined || s.lines.length > 0)
                  .map((s) => (
                  <Resumen
                    key={`${s.cardId}-${s.dueDate}`}
                    statement={s}
                    filtro={filtro}
                    accent={accent}
                    categoryName={categoryName}
                    onOpen={(id) => navigation.navigate('EditarMovimiento', { id })}
                  />
                ))}
              </>
            )}
          </QueryState>
        )}

        {cardId !== undefined && (
          <Card
            title="Cierres y vencimientos"
            right={
              <Pressable onPress={() => navigation.navigate('Configuracion')} hitSlop={8}>
                <Text style={{ color: accent, fontWeight: '600' }}>Editar</Text>
              </Pressable>
            }
          >
            <QueryState
              isLoading={cycles.isLoading}
              error={cycles.error}
              onRetry={cycles.refetch}
              isEmpty={cycles.data?.length === 0}
              emptyText="Sin ciclos cargados."
            >
              <View style={styles.line}>
                <Text style={[styles.head, { color: colors.inkMuted }]}>Mes</Text>
                <Text style={[styles.head, { color: colors.inkMuted }]}>Cierre</Text>
                <Text style={[styles.head, { color: colors.inkMuted }]}>Vence</Text>
              </View>
              {(cycles.data ?? []).map((c) => (
                <View key={c.month} style={styles.line}>
                  <Text style={[styles.cell, { color: colors.ink }]}>{formatMonth(c.month)}</Text>
                  <Text style={[styles.cell, { color: colors.ink }]}>{formatDate(c.closingDate)}</Text>
                  <Text style={[styles.cell, { color: colors.ink }]}>{formatDate(c.dueDate)}</Text>
                </View>
              ))}
            </QueryState>
          </Card>
        )}
      </QueryState>
    </Screen>
  );
}

function Resumen({
  statement: s,
  filtro,
  accent,
  categoryName,
  onOpen,
}: {
  statement: CardStatement;
  filtro: Filtro;
  accent: string;
  categoryName: (id: Id | null) => string;
  onOpen: (transactionId: Id) => void;
}) {
  const { colors } = useTheme();
  const dato = (label: string, value: number) => (
    <View style={styles.dato}>
      <Text style={{ color: colors.inkMuted, fontSize: 11 }}>{label}</Text>
      <Text style={{ color: colors.ink, fontVariant: ['tabular-nums'] }}>{formatARS(value)}</Text>
    </View>
  );
  return (
    <Card title={`${s.card} · vence ${formatDate(s.dueDate)}`} accent={accent}>
      <View style={styles.datos}>
        {dato('Modelado', s.modeled)}
        {s.confirmed !== null && dato('Resumen real', s.confirmed)}
        {dato('Pagado', s.paid)}
        {dato('Pendiente', s.outstanding)}
      </View>
      {filtro !== 'todos' && (
        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
          {filtro === 'fijos' ? 'Fijos' : 'Sin fijos'}: {formatARS(s.filteredTotal)} de {formatARS(s.expected)}
        </Text>
      )}
      {s.lines.length === 0 ? (
        <Text style={{ color: colors.inkMuted }}>
          {filtro === 'fijos' ? 'No hay gastos fijos en este resumen.' : 'No hay líneas con este filtro.'}
        </Text>
      ) : (
        s.lines.map((l) => <Linea key={`${l.transactionId}-${l.installment}`} line={l} categoryName={categoryName} onOpen={onOpen} />)
      )}
    </Card>
  );
}

function Linea({
  line: l,
  categoryName,
  onOpen,
}: {
  line: CardStatementLine;
  categoryName: (id: Id | null) => string;
  onOpen: (transactionId: Id) => void;
}) {
  const { colors } = useTheme();
  const marcas = [
    l.installments > 1 ? `cuota ${l.installment}/${l.installments}` : null,
    l.recurringRuleId !== null ? 'Fijo' : null,
    l.status === 'previsto' ? 'Previsto' : null,
    l.isPriorCommitment ? 'Compra anterior' : null,
  ].filter(Boolean);
  return (
    <Pressable onPress={() => onOpen(l.transactionId)} style={[styles.linea, { borderTopColor: colors.border }]}>
      <View style={{ flex: 1 }}>
        <Text style={{ color: colors.ink }} numberOfLines={1}>
          {l.description || categoryName(l.categoryId) || 'Compra'}
        </Text>
        <Text style={{ color: colors.inkMuted, fontSize: 12 }} numberOfLines={1}>
          {[formatDate(l.purchaseDate), categoryName(l.categoryId)].filter(Boolean).join(' · ')}
        </Text>
        {marcas.length > 0 && (
          <Text style={{ color: l.status === 'previsto' ? colors.warn : colors.inkMuted, fontSize: 12, fontWeight: '600' }}>
            {marcas.join(' · ')}
          </Text>
        )}
      </View>
      <MoneyText value={l.amount} size="sm" />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  filters: { flexDirection: 'row', gap: spacing.sm },
  line: { flexDirection: 'row', gap: spacing.sm, justifyContent: 'space-between', alignItems: 'center' },
  head: { flex: 1, fontSize: 12, fontWeight: '600' },
  cell: { flex: 1, fontVariant: ['tabular-nums'] },
  datos: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  dato: { minWidth: 90 },
  linea: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingTop: spacing.sm,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
});
