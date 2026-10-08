import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useBudgets, useCategories, useUpdateBudgets } from '../api/endpoints';
import type { Budget, Id, Month } from '../api/types';
import { Card } from '../components/Card';
import { Input } from '../components/Field';
import { MonthStepper } from '../components/MonthStepper';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, spacing, useTheme } from '../theme';
import { amountToInput, monthOf, parseAmount } from '../utils/format';

export function PresupuestoScreen({ navigation }: TabScreenProps<'Presupuesto'>) {
  const { cutoff } = useScope();
  const { colors } = useTheme();
  const [month, setMonth] = useState<Month>(() => monthOf(cutoff));
  const q = useBudgets(month);
  const categories = useCategories();
  const save = useUpdateBudgets(month);

  // Borrador editable: categoryId → texto del monto.
  const [draft, setDraft] = useState<Record<Id, string>>({});
  useEffect(() => {
    setDraft(Object.fromEntries((q.data ?? []).map((b) => [b.categoryId, amountToInput(b.amount)])));
  }, [q.data]);

  // Todas las categorías de gasto (para poder presupuestar un mes vacío) más las que ya tengan monto.
  const rows = useMemo(() => {
    const all = categories.data ?? [];
    const ids = new Set(all.filter((c) => c.kind === 'gasto').map((c) => c.id));
    (q.data ?? []).forEach((b) => ids.add(b.categoryId));
    return [...ids].map((id) => {
      const c = all.find((x) => x.id === id);
      return { id, name: c ? (c.scope === 'memey' ? `${c.name} (Memey)` : c.name) : `#${id}`, scope: c?.scope };
    });
  }, [categories.data, q.data]);

  const onSave = () => {
    const budgets: Budget[] = Object.entries(draft)
      .map(([categoryId, text]) => ({ month, categoryId: Number(categoryId), amount: parseAmount(text) ?? 0 }))
      .filter((b) => b.amount > 0);
    save.mutate(budgets);
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <MonthStepper value={month} onChange={setMonth} />
      <QueryState
        isLoading={q.isLoading || categories.isLoading}
        error={q.error ?? categories.error}
        onRetry={q.refetch}
        isEmpty={rows.length === 0}
        emptyText="No hay categorías de gasto."
      >
        <Card>
          {rows.map((r) => (
            <View key={r.id} style={styles.line}>
              <Text style={{ color: r.scope === 'memey' ? colors.memey : colors.ink, flex: 1 }}>{r.name}</Text>
              <Input
                value={draft[r.id] ?? ''}
                onChangeText={(t) => setDraft((d) => ({ ...d, [r.id]: t }))}
                placeholder="0"
                keyboardType="decimal-pad"
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
        {save.isSuccess && <Text style={{ color: colors.good }}>Presupuesto guardado.</Text>}
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
