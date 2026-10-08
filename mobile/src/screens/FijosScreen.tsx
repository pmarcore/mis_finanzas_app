import { StyleSheet, Text, View } from 'react-native';
import { useCategories, useRecurringRules } from '../api/endpoints';
import { Card } from '../components/Card';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Screen } from '../components/Screen';
import { useScope } from '../state/scope';
import { scopeColor, spacing, useTheme } from '../theme';
import { formatMonth } from '../utils/format';

// TODO: alta/edición de reglas (useCreateRecurringRule / useUpdateRecurringRule).
export function FijosScreen() {
  const { scope } = useScope();
  const { colors } = useTheme();
  const q = useRecurringRules(scope);
  const categories = useCategories();
  const categoryName = (id: string) => categories.data?.find((c) => c.id === id)?.name ?? id;

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <ScopeSelector />
      <QueryState
        isLoading={q.isLoading}
        error={q.error}
        onRetry={q.refetch}
        isEmpty={q.data?.length === 0}
        emptyText="No hay movimientos fijos cargados."
      >
        {q.data?.map((r) => (
          <Card key={r.id} accent={scopeColor(colors, r.scope)}>
            <View style={styles.line}>
              <Text style={[styles.title, { color: colors.ink }]}>{categoryName(r.categoryId)}</Text>
              <Text style={{ color: colors.inkMuted }}>día {r.dayOfMonth}</Text>
            </View>
            <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
              {r.operation} · desde {formatMonth(r.startMonth)}
              {r.endMonth ? ` hasta ${formatMonth(r.endMonth)}` : ''}
            </Text>
            {r.amounts.map((a) => (
              <View key={a.fromMonth} style={styles.line}>
                <Text style={{ color: colors.ink }}>desde {formatMonth(a.fromMonth)}</Text>
                <MoneyText value={a.amount} size="sm" />
              </View>
            ))}
          </Card>
        ))}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  title: { fontSize: 16, fontWeight: '600' },
});
