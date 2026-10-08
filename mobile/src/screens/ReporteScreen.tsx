import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { useBudgetVsActual } from '../api/endpoints';
import type { BudgetVsActualRow, Month } from '../api/types';
import { Card } from '../components/Card';
import { CutoffControl } from '../components/CutoffControl';
import { MoneyText } from '../components/MoneyText';
import { MonthStepper } from '../components/MonthStepper';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import type { RootStackScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { spacing, useTheme, type Palette } from '../theme';
import { formatPct, monthOf } from '../utils/format';

// El color de uso sólo refleja el % que calcula el backend.
function usageColor(colors: Palette, pct: number) {
  if (pct > 100) return colors.bad;
  if (pct >= 85) return colors.warn;
  return colors.good;
}

export function ReporteScreen({ route }: RootStackScreenProps<'Reporte'>) {
  const { cutoff } = useScope(); // por defecto hoy
  const { colors } = useTheme();
  const [month, setMonth] = useState<Month>(route.params?.month ?? monthOf(cutoff));
  const q = useBudgetVsActual(month, cutoff);

  const Row = ({ r }: { r: BudgetVsActualRow }) => (
    <View style={styles.row}>
      <View style={{ flex: 1 }}>
        <Text style={{ color: colors.ink, fontWeight: '600' }}>{r.category}</Text>
        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
          {r.budgetType} · presup. <MoneyText value={r.budget} size="sm" color={colors.inkMuted} />
        </Text>
        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
          proyectado <MoneyText value={r.projected} size="sm" color={colors.inkMuted} />
        </Text>
      </View>
      <View style={{ alignItems: 'flex-end' }}>
        <MoneyText value={r.actual} size="sm" />
        <Text style={{ color: usageColor(colors, r.usePct), fontWeight: '700' }}>{formatPct(r.usePct)}</Text>
      </View>
    </View>
  );

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <MonthStepper value={month} onChange={setMonth} />
      <CutoffControl />
      <QueryState
        isLoading={q.isLoading}
        error={q.error}
        onRetry={q.refetch}
        isEmpty={q.data?.length === 0}
        emptyText="Sin datos para este mes."
      >
        <Card>
          {(q.data ?? []).map((r) => (
            <Row key={r.categoryId} r={r} />
          ))}
        </Card>
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.xs },
});
