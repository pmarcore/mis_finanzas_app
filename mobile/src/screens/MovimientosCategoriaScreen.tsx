import { FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import { useBudgetCategoryTransactions, useOperationTypes, usePaymentMethods } from '../api/endpoints';
import type { Id, Transaction } from '../api/types';
import { Card } from '../components/Card';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import type { RootStackScreenProps } from '../navigation/types';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { formatDate, formatMonth } from '../utils/format';

/**
 * Detalle de una fila del presupuesto vs. real: los movimientos que suman el real de la categoría
 * en el mes que se está mirando, hasta la fecha de corte. Tocar uno abre su edición.
 */
export function MovimientosCategoriaScreen({ route, navigation }: RootStackScreenProps<'MovimientosCategoria'>) {
  const { categoryId, month, cutoff, scope } = route.params;
  const { colors } = useTheme();
  const q = useBudgetCategoryTransactions(categoryId, month, cutoff);
  const methods = usePaymentMethods();
  const types = useOperationTypes();
  const methodName = (id: Id) => methods.data?.find((m) => m.id === id)?.name ?? '';
  const typeName = (id: Id) => types.data?.find((t) => t.id === id)?.name ?? '';
  const total = (q.data ?? []).reduce((sum, t) => sum + t.amount, 0);

  const renderItem = ({ item }: { item: Transaction }) => (
    <Pressable
      onPress={() => navigation.navigate('EditarMovimiento', { id: item.id })}
      style={[styles.row, { backgroundColor: colors.surface, borderColor: colors.border }]}
    >
      <View style={{ flex: 1 }}>
        <Text style={{ color: colors.ink, fontWeight: '600' }} numberOfLines={1}>
          {item.description || typeName(item.operationTypeId)}
        </Text>
        <Text style={{ color: colors.inkMuted, fontSize: 12 }} numberOfLines={1}>
          {formatDate(item.date)} · {methodName(item.paymentMethodId)}
          {item.installments > 1 ? ` · ${item.installments} cuotas` : ''}
        </Text>
      </View>
      <MoneyText value={item.amount} size="sm" signColor={false} />
    </Pressable>
  );

  return (
    <View style={{ flex: 1, backgroundColor: colors.ground }}>
      <QueryState
        isLoading={q.isLoading}
        error={q.error}
        onRetry={q.refetch}
        isEmpty={q.data?.length === 0}
        emptyText="No hay movimientos de esta categoría en el mes."
      >
        <FlatList
          data={q.data ?? []}
          keyExtractor={(t) => String(t.id)}
          renderItem={renderItem}
          contentContainerStyle={styles.list}
          refreshing={q.isRefetching}
          onRefresh={q.refetch}
          ListHeaderComponent={
            <Card accent={scopeColor(colors, scope)}>
              <View style={styles.line}>
                <Text style={{ color: colors.ink, fontWeight: '700' }}>Real de {formatMonth(month)}</Text>
                <MoneyText value={total} size="sm" />
              </View>
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                {q.data?.length ?? 0} movimientos hasta el {formatDate(cutoff)}. Las compras con tarjeta cuentan el día
                de la compra.
              </Text>
            </Card>
          }
        />
      </QueryState>
    </View>
  );
}

const styles = StyleSheet.create({
  list: { padding: spacing.lg, gap: spacing.sm },
  line: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: StyleSheet.hairlineWidth,
  },
});
