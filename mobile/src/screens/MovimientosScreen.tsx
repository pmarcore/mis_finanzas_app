import { useEffect, useMemo, useRef, useState } from 'react';
import { Alert, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import {
  useCashFlow,
  useCategories,
  useConfirmTransaction,
  useDeleteTransaction,
  usePaymentMethods,
  useTransactions,
} from '../api/endpoints';
import { OPERATION_LABELS, SCOPE_LABELS, type CashFlowLine, type Id, type Transaction } from '../api/types';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Segmented } from '../components/Segmented';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { endOfMonth, formatARS, formatDate, monthOf } from '../utils/format';

type Vista = 'lista' | 'caja';

export function MovimientosScreen({ route }: TabScreenProps<'Movimientos'>) {
  const { scope, cutoff } = useScope();
  const { colors } = useTheme();
  const params = route.params;
  const [vista, setVista] = useState<Vista>(params?.vista ?? 'lista');
  // Cada toque en una card del inicio trae params nuevos: se respeta la vista pedida.
  useEffect(() => {
    if (params) setVista(params.vista);
  }, [params]);
  const marca = params?.vista === 'caja' ? params : undefined;
  // Mes de la fecha de corte; el backend filtra.
  const month = monthOf(cutoff);
  // `from` y `to` son inclusivos en el backend.
  const q = useTransactions({ scope, from: `${month}-01`, to: endOfMonth(month) });
  const categories = useCategories();
  const methods = usePaymentMethods();
  const confirm = useConfirmTransaction();
  const remove = useDeleteTransaction();

  const categoryName = (id: Id | null) =>
    id === null ? '' : (categories.data?.find((c) => c.id === id)?.name ?? '');
  const methodName = (id: Id | null | undefined) =>
    id == null ? '' : (methods.data?.find((m) => m.id === id)?.name ?? '');

  const askDelete = (t: Transaction) =>
    Alert.alert('Borrar movimiento', `¿Borrar "${t.description || OPERATION_LABELS[t.operation]}"?`, [
      { text: 'Cancelar', style: 'cancel' },
      {
        text: 'Borrar',
        style: 'destructive',
        onPress: () =>
          remove.mutate(t.id, {
            onError: (e) => Alert.alert('No se pudo borrar', e.message),
          }),
      },
    ]);

  const renderItem = ({ item }: { item: Transaction }) => {
    const previsto = item.status === 'previsto';
    const label = OPERATION_LABELS[item.operation];
    return (
      <Pressable
        onLongPress={() => askDelete(item)}
        style={[styles.row, { backgroundColor: colors.surface, borderColor: colors.border }]}
      >
        <View style={[styles.dot, { backgroundColor: scopeColor(colors, item.scope) }]} />
        <View style={{ flex: 1 }}>
          <Text style={{ color: colors.ink, fontWeight: '600' }} numberOfLines={1}>
            {item.description || label}
          </Text>
          <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
            {formatDate(item.date)} · {label}
            {item.operation === 'pagoTarjeta' && methodName(item.paidCardId) ? ` ${methodName(item.paidCardId)}` : ''}
            {categoryName(item.categoryId) ? ` · ${categoryName(item.categoryId)}` : ''}
            {item.installments > 1 ? ` · ${item.installments} cuotas` : ''}
          </Text>
        </View>
        <View style={{ alignItems: 'flex-end', gap: 2 }}>
          <MoneyText value={item.amount} size="sm" signColor={false} />
          {previsto && (
            <Pressable
              onPress={() =>
                confirm.mutate({ id: item.id }, { onError: (e) => Alert.alert('No se pudo confirmar', e.message) })
              }
              disabled={confirm.isPending}
              hitSlop={6}
            >
              <Text style={{ color: colors.warn, fontSize: 12, fontWeight: '600' }}>Previsto · Confirmar</Text>
            </Pressable>
          )}
        </View>
      </Pressable>
    );
  };

  return (
    <View style={{ flex: 1, backgroundColor: colors.ground }}>
      <View style={styles.header}>
        <ScopeSelector />
        <Segmented<Vista>
          value={vista}
          onChange={setVista}
          options={[
            { value: 'lista', label: 'Movimientos', color: scopeColor(colors, scope) },
            { value: 'caja', label: 'Flujo de caja', color: scopeColor(colors, scope) },
          ]}
        />
      </View>
      {vista === 'caja' ? (
        <FlujoDeCaja marca={marca?.marca ?? 'corte'} nonce={marca?.n} methodName={methodName} categoryName={categoryName} />
      ) : (
        <QueryState
          isLoading={q.isLoading}
          error={q.error}
          onRetry={q.refetch}
          isEmpty={q.data?.length === 0}
          emptyText="No hay movimientos este mes."
        >
          <FlatList
            data={q.data ?? []}
            keyExtractor={(t) => String(t.id)}
            renderItem={renderItem}
            contentContainerStyle={styles.list}
            refreshing={q.isRefetching}
            onRefresh={q.refetch}
          />
        </QueryState>
      )}
    </View>
  );
}

type Fila =
  | { tipo: 'saldo'; key: string; titulo: string; fecha: string; saldo: number; marca?: 'corte' | 'finDeMes' }
  | { tipo: 'linea'; key: string; linea: CashFlowLine };

/**
 * Flujo de caja del mes del corte en orden de fecha, con saldo acumulado.
 * La línea de corte muestra la caja actual y la última, la caja a fin de mes (los mismos números del inicio).
 */
function FlujoDeCaja({
  marca,
  nonce,
  methodName,
  categoryName,
}: {
  marca: 'corte' | 'finDeMes';
  nonce?: number;
  methodName: (id: Id | null | undefined) => string;
  categoryName: (id: Id | null) => string;
}) {
  const { scope, cutoff } = useScope();
  const { colors } = useTheme();
  const accent = scopeColor(colors, scope);
  const q = useCashFlow(scope, cutoff);
  const f = q.data;
  const list = useRef<FlatList<Fila>>(null);

  const filas = useMemo<Fila[]>(() => {
    if (!f) return [];
    const hechas = f.lines.filter((l) => !l.pending);
    const pendientes = f.lines.filter((l) => l.pending);
    const linea = (l: CashFlowLine, i: number): Fila => ({ tipo: 'linea', key: `${l.pending ? 'p' : 'r'}${i}`, linea: l });
    return [
      { tipo: 'saldo', key: 'inicio', titulo: 'Saldo al inicio del mes', fecha: f.from, saldo: f.opening },
      ...hechas.map(linea),
      { tipo: 'saldo', key: 'corte', titulo: 'Corte · Caja actual', fecha: f.cutoff, saldo: f.cashNow, marca: 'corte' },
      ...pendientes.map(linea),
      { tipo: 'saldo', key: 'fin', titulo: 'Caja a fin de mes', fecha: f.until, saldo: f.projected, marca: 'finDeMes' },
    ];
  }, [f]);

  const target = filas.findIndex((r) => r.tipo === 'saldo' && r.marca === marca);
  useEffect(() => {
    if (target < 0) return;
    const t = setTimeout(() => list.current?.scrollToIndex({ index: target, viewPosition: 0.5, animated: true }), 250);
    return () => clearTimeout(t);
  }, [target, nonce]);

  const detalle = (l: CashFlowLine) => {
    const partes = [formatDate(l.date)];
    if (l.kind === 'resumenTarjeta') partes.push('vencimiento sin pagar');
    else if (l.kind === 'gastoEstimado') partes.push('presupuesto no cubierto por fijos');
    else {
      if (l.operation) partes.push(OPERATION_LABELS[l.operation]);
      if (l.categoryId != null && categoryName(l.categoryId)) partes.push(categoryName(l.categoryId));
      if (scope === 'total' && l.scope) partes.push(SCOPE_LABELS[l.scope]);
      if (l.status === 'previsto') partes.push('previsto');
    }
    return partes.join(' · ');
  };

  const renderFila = ({ item }: { item: Fila }) => {
    if (item.tipo === 'saldo') {
      const marcada = item.marca === marca;
      return (
        <View
          style={[
            styles.saldo,
            { borderColor: marcada ? accent : colors.border, backgroundColor: marcada ? accent : colors.surface },
          ]}
        >
          <View style={{ flex: 1 }}>
            <Text style={{ color: marcada ? colors.surface : colors.ink, fontWeight: '700' }}>{item.titulo}</Text>
            <Text style={{ color: marcada ? colors.surface : colors.inkMuted, fontSize: 12 }}>{formatDate(item.fecha)}</Text>
          </View>
          <Text style={{ color: marcada ? colors.surface : colors.ink, fontWeight: '700', fontSize: 16 }}>
            {formatARS(item.saldo)}
          </Text>
        </View>
      );
    }
    const l = item.linea;
    const title =
      l.kind === 'movimiento'
        ? l.description || (l.operation ? OPERATION_LABELS[l.operation] : '')
        : l.description;
    return (
      <View
        style={[
          styles.row,
          { backgroundColor: colors.surface, borderColor: colors.border, opacity: l.pending ? 0.75 : 1 },
        ]}
      >
        <View style={{ flex: 1 }}>
          <Text style={{ color: colors.ink, fontWeight: '600' }} numberOfLines={1}>
            {title}
            {l.kind === 'movimiento' && l.operation === 'pagoTarjeta' && methodName(l.paidCardId) ? ` ${methodName(l.paidCardId)}` : ''}
          </Text>
          <Text style={{ color: colors.inkMuted, fontSize: 12 }} numberOfLines={1}>
            {detalle(l)}
          </Text>
        </View>
        <View style={{ alignItems: 'flex-end', gap: 2 }}>
          <MoneyText value={l.amount} size="sm" />
          <Text style={{ color: colors.inkMuted, fontSize: 12 }}>Saldo {formatARS(l.balance)}</Text>
        </View>
      </View>
    );
  };

  return (
    <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
      <FlatList
        ref={list}
        data={filas}
        keyExtractor={(r) => r.key}
        renderItem={renderFila}
        contentContainerStyle={styles.list}
        refreshing={q.isRefetching}
        onRefresh={q.refetch}
        ListFooterComponent={
          <Text style={{ color: colors.inkMuted, fontSize: 12, textAlign: 'center', marginTop: spacing.sm }}>
            Las compras con tarjeta entran a la caja en el vencimiento del resumen.
          </Text>
        }
        onScrollToIndexFailed={(info) => {
          list.current?.scrollToOffset({ offset: info.averageItemLength * info.index, animated: false });
          setTimeout(() => list.current?.scrollToIndex({ index: info.index, viewPosition: 0.5, animated: true }), 100);
        }}
      />
    </QueryState>
  );
}

const styles = StyleSheet.create({
  header: { padding: spacing.lg, paddingBottom: spacing.sm, gap: spacing.sm },
  list: { paddingHorizontal: spacing.lg, paddingBottom: spacing.xl, gap: spacing.sm },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: StyleSheet.hairlineWidth,
  },
  dot: { width: 8, height: 8, borderRadius: 4 },
  saldo: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },
});
