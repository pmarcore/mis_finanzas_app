import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { useBudgetVsActual } from '../api/endpoints';
import { BUDGET_TYPE_LABELS, type BudgetAlert, type BudgetVsActualRow, type Month, type Scope } from '../api/types';
import { Card } from '../components/Card';
import { CutoffControl } from '../components/CutoffControl';
import { MoneyText } from '../components/MoneyText';
import { MonthStepper } from '../components/MonthStepper';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import type { RootStackScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme, type Palette } from '../theme';
import { formatPct, monthOf } from '../utils/format';

// El color sólo refleja la alerta que calcula el backend.
function alertColor(colors: Palette, alert: BudgetAlert) {
  switch (alert) {
    case 'Normal':
      return colors.good;
    case 'Preventiva':
    case 'Control':
    case 'Proyecta exceso':
      return colors.warn;
    default:
      return colors.bad;
  }
}

const ALERT_LABELS: Record<BudgetAlert, string> = {
  Normal: 'Normal',
  Preventiva: 'Preventiva',
  Control: 'Control',
  Excedido: 'Excedido',
  Critico: 'Crítico',
  'Proyecta exceso': 'Proyecta exceso',
};

export function ReporteScreen({ route }: RootStackScreenProps<'Reporte'>) {
  const { cutoff, scope: currentScope } = useScope(); // corte por defecto hoy
  const { colors } = useTheme();
  const [month, setMonth] = useState<Month>(route.params?.month ?? monthOf(cutoff));
  // El reporte es por ámbito; "total" lo toma el backend como familia.
  const [scope, setScope] = useState<Scope>(currentScope === 'memey' ? 'memey' : 'familia');
  const q = useBudgetVsActual(month, cutoff, scope);
  const d = q.data;

  const Row = ({ r }: { r: BudgetVsActualRow }) => {
    const tint = alertColor(colors, r.alert);
    return (
      <View style={[styles.row, { borderBottomColor: colors.border }]}>
        <View style={{ flex: 1 }}>
          <Text style={{ color: colors.ink, fontWeight: '600' }}>{r.category}</Text>
          <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
            {BUDGET_TYPE_LABELS[r.budgetType]} · presup. <MoneyText value={r.budget} size="sm" color={colors.inkMuted} />
          </Text>
          <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
            disponible <MoneyText value={r.available} size="sm" color={colors.inkMuted} /> · proyectado{' '}
            <MoneyText value={r.projected} size="sm" color={colors.inkMuted} />
          </Text>
        </View>
        <View style={{ alignItems: 'flex-end', gap: 2 }}>
          <MoneyText value={r.actual} size="sm" />
          <Text style={{ color: tint, fontWeight: '700' }}>{formatPct(r.usePct)}</Text>
          <View style={[styles.badge, { borderColor: tint }]}>
            <Text style={{ color: tint, fontSize: 11, fontWeight: '600' }}>{ALERT_LABELS[r.alert] ?? r.alert}</Text>
          </View>
        </View>
      </View>
    );
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <Segmented<Scope>
        value={scope}
        onChange={setScope}
        options={[
          { value: 'familia', label: 'Familia', color: colors.familia },
          { value: 'memey', label: 'Memey', color: colors.memey },
        ]}
      />
      <MonthStepper value={month} onChange={setMonth} />
      <CutoffControl />
      <QueryState
        isLoading={q.isLoading}
        error={q.error}
        onRetry={q.refetch}
        isEmpty={d?.rows.length === 0}
        emptyText="Sin datos para este mes."
      >
        {d && (
          <>
            <Card title="Totales" accent={scopeColor(colors, d.scope)}>
              <View style={styles.line}>
                <Text style={{ color: colors.ink }}>Presupuesto</Text>
                <MoneyText value={d.totalBudget} size="sm" />
              </View>
              <View style={styles.line}>
                <Text style={{ color: colors.ink }}>Real</Text>
                <MoneyText value={d.totalActual} size="sm" />
              </View>
              <View style={styles.line}>
                <Text style={{ color: colors.ink }}>Proyectado</Text>
                <MoneyText value={d.totalProjected} size="sm" />
              </View>
            </Card>
            <Card>
              {d.rows.map((r) => (
                <Row key={r.categoryId} r={r} />
              ))}
            </Card>
          </>
        )}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.xs,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  line: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  badge: { borderWidth: 1, borderRadius: radius.sm, paddingHorizontal: spacing.xs },
});
