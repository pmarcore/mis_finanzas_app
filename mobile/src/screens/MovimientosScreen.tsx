import { Alert, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import {
  useCategories,
  useConfirmTransaction,
  useDeleteTransaction,
  usePaymentMethods,
  useTransactions,
} from '../api/endpoints';
import { OPERATION_LABELS, type Id, type Transaction } from '../api/types';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { endOfMonth, formatDate, monthOf } from '../utils/format';

export function MovimientosScreen() {
  const { scope, cutoff } = useScope();
  const { colors } = useTheme();
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
      </View>
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
    </View>
  );
}

const styles = StyleSheet.create({
  header: { padding: spacing.lg, paddingBottom: spacing.sm },
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
});
