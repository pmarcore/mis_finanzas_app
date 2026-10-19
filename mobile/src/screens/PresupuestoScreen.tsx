import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useBudgets, useCategories, useUpdateBudgets } from '../api/endpoints';
import type { Budget, Id, Month, Scope } from '../api/types';
import { Card } from '../components/Card';
import { Input } from '../components/Field';
import { MoneyText } from '../components/MoneyText';
import { MonthStepper } from '../components/MonthStepper';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, spacing, useTheme } from '../theme';
import { amountToInput, monthOf, parseAmount } from '../utils/format';

export function PresupuestoScreen({ navigation }: TabScreenProps<'Presupuesto'>) {
  const { cutoff, scope: currentScope } = useScope();
  const { colors } = useTheme();
  const [month, setMonth] = useState<Month>(() => monthOf(cutoff));
  // Cada entidad tiene su propio presupuesto: se carga y se guarda el de Familia o el de Memey, sin mezclar.
  const [scope, setScope] = useState<Scope>(currentScope);
  const q = useBudgets(month, scope);
  const categories = useCategories();
  const save = useUpdateBudgets(month, scope);
  const accent = scope === 'memey' ? colors.memey : colors.familia;

  // Borrador editable: categoryId → texto del monto.
  const [draft, setDraft] = useState<Record<Id, string>>({});
  useEffect(() => {
    setDraft(Object.fromEntries((q.data ?? []).map((b) => [b.categoryId, amountToInput(b.amount)])));
  }, [q.data]);

  // Las categorías de gasto activas de la entidad (para poder presupuestar un mes vacío) más las que ya tengan monto.
  const rows = useMemo(() => {
    const all = categories.data ?? [];
    const ids = new Set(all.filter((c) => c.scope === scope && c.kind === 'gasto' && c.active).map((c) => c.id));
    (q.data ?? []).forEach((b) => ids.add(b.categoryId));
    return [...ids].map((id) => ({ id, name: all.find((x) => x.id === id)?.name ?? `#${id}` }));
  }, [categories.data, q.data, scope]);

  const total = Object.values(draft).reduce((sum, text) => sum + (parseAmount(text) ?? 0), 0);

  const onSave = () => {
    const budgets: Budget[] = Object.entries(draft)
      .map(([categoryId, text]) => ({ month, categoryId: Number(categoryId), amount: parseAmount(text) ?? 0 }))
      .filter((b) => b.amount > 0);
    save.mutate(budgets);
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <Segmented<Scope>
        value={scope}
        onChange={(s) => {
          setScope(s);
          save.reset();
        }}
        options={[
          { value: 'familia', label: 'Familia', color: colors.familia },
          { value: 'memey', label: 'Memey', color: colors.memey },
        ]}
      />
      <MonthStepper value={month} onChange={setMonth} />
      <QueryState
        isLoading={q.isLoading || categories.isLoading}
        error={q.error ?? categories.error}
        onRetry={q.refetch}
        isEmpty={rows.length === 0}
        emptyText={`No hay categorías de gasto de ${scope === 'memey' ? 'Memey' : 'Familia'}.`}
      >
        <Card accent={accent}>
          {rows.map((r) => (
            <View key={r.id} style={styles.line}>
              <Text style={{ color: colors.ink, flex: 1 }}>{r.name}</Text>
              <Input
                value={draft[r.id] ?? ''}
                onChangeText={(t) => setDraft((d) => ({ ...d, [r.id]: t }))}
                placeholder="0"
                keyboardType="decimal-pad"
                style={styles.amount}
              />
            </View>
          ))}
          <View style={[styles.line, styles.total, { borderTopColor: colors.border }]}>
            <Text style={{ color: colors.ink, fontWeight: '700', flex: 1 }}>Total</Text>
            <MoneyText value={total} size="sm" />
          </View>
        </Card>
        <Pressable
          onPress={onSave}
          disabled={save.isPending}
          style={[styles.button, { backgroundColor: accent, opacity: save.isPending ? 0.6 : 1 }]}
        >
          <Text style={{ color: colors.surface, fontWeight: '700' }}>
            Guardar presupuesto {scope === 'memey' ? 'Memey' : 'Familia'}
          </Text>
        </Pressable>
        {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
        {save.isSuccess && <Text style={{ color: colors.good }}>Presupuesto guardado.</Text>}
      </QueryState>
      <Pressable onPress={() => navigation.navigate('Reporte', { month, scope })} style={styles.link}>
        <Text style={{ color: accent, fontWeight: '600' }}>Ver presupuesto vs. real →</Text>
      </Pressable>
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  amount: { width: 130, textAlign: 'right' },
  button: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  link: { alignItems: 'center', padding: spacing.md },
  total: { borderTopWidth: StyleSheet.hairlineWidth, paddingTop: spacing.sm },
});
