import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useBudgets, useCategories, useUpdateBudgets } from '../api/endpoints';
import type { Budget, Month } from '../api/types';
import { Card } from '../components/Card';
import { Input } from '../components/Field';
import { MonthStepper } from '../components/MonthStepper';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, spacing, useTheme } from '../theme';
import { monthOf } from '../utils/format';

export function PresupuestoScreen({ navigation }: TabScreenProps<'Presupuesto'>) {
  const { cutoff } = useScope();
  const { colors } = useTheme();
  const [month, setMonth] = useState<Month>(() => monthOf(cutoff));
  const q = useBudgets(month);
  const categories = useCategories();
  const save = useUpdateBudgets(month);

  // Borrador editable: categoryId → texto del monto.
  const [draft, setDraft] = useState<Record<string, string>>({});
  useEffect(() => {
    setDraft(Object.fromEntries((q.data ?? []).map((b) => [b.categoryId, String(b.amount)])));
  }, [q.data]);

  const categoryName = (id: string) => categories.data?.find((c) => c.id === id)?.name ?? id;

  const onSave = () => {
    const budgets: Budget[] = Object.entries(draft).map(([categoryId, text]) => ({
      month,
      categoryId,
      amount: Number(text.replace(/\./g, '').replace(',', '.')) || 0,
    }));
    save.mutate(budgets);
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <MonthStepper value={month} onChange={setMonth} />
      <QueryState
        isLoading={q.isLoading}
        error={q.error}
        onRetry={q.refetch}
        isEmpty={q.data?.length === 0}
        emptyText="No hay presupuesto para este mes."
      >
        <Card>
          {(q.data ?? []).map((b) => (
            <View key={b.categoryId} style={styles.line}>
              <Text style={{ color: colors.ink, flex: 1 }}>{categoryName(b.categoryId)}</Text>
              <Input
                value={draft[b.categoryId] ?? ''}
                onChangeText={(t) => setDraft((d) => ({ ...d, [b.categoryId]: t }))}
                keyboardType="number-pad"
                style={styles.amount}
              />
            </View>
          ))}
        </Card>
        <Pressable
          onPress={onSave}
          disabled={save.isPending}
          style={[styles.button, { backgroundColor: colors.familia, opacity: save.isPending ? 0.6 : 1 }]}
        >
          <Text style={{ color: colors.surface, fontWeight: '700' }}>Guardar presupuesto</Text>
        </Pressable>
        {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
      </QueryState>
      <Pressable onPress={() => navigation.navigate('Reporte', { month })} style={styles.link}>
        <Text style={{ color: colors.familia, fontWeight: '600' }}>Ver presupuesto vs. real →</Text>
      </Pressable>
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  amount: { width: 130, textAlign: 'right' },
  button: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  link: { alignItems: 'center', padding: spacing.md },
});
