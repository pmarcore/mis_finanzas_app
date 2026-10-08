import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useDashboard } from '../api/endpoints';
import { Card } from '../components/Card';
import { CutoffControl } from '../components/CutoffControl';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Screen } from '../components/Screen';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { scopeColor, spacing, useTheme } from '../theme';
import { formatDate, formatMonth } from '../utils/format';

export function InicioScreen({ navigation }: TabScreenProps<'Inicio'>) {
  const { scope, cutoff } = useScope();
  const { colors } = useTheme();
  const accent = scopeColor(colors, scope);
  const q = useDashboard(scope, cutoff);
  const d = q.data;

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <ScopeSelector />
      <CutoffControl />

      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        {d && (
          <>
            {d.alerts.length > 0 && (
              <Card title="Alertas" accent={colors.warn}>
                {d.alerts.map((a, i) => (
                  <Text key={i} style={{ color: colors.ink }}>
                    • {a}
                  </Text>
                ))}
              </Card>
            )}

            <View style={styles.grid}>
              <Card title="Caja actual" accent={accent} style={styles.half}>
                <MoneyText value={d.cashNow} size="lg" />
              </Card>
              <Card title="Fin de mes" accent={accent} style={styles.half}>
                <MoneyText value={d.projectedEndOfMonth} size="lg" />
              </Card>
            </View>

            <Card title="Caja libre" accent={accent}>
              <MoneyText value={d.freeCash} size="xl" />
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>Proyección por encima del piso de caja</Text>
            </Card>

            <Card title="Proyección trimestral" accent={accent}>
              {d.quarter.map((m) => (
                <View key={m.month} style={styles.line}>
                  <Text style={{ color: colors.ink }}>{formatMonth(m.month)}</Text>
                  <MoneyText value={m.balance} size="sm" />
                </View>
              ))}
            </Card>

            <Card title="Caja Memey" accent={colors.memey}>
              <View style={styles.line}>
                <Text style={{ color: colors.ink }}>Actual</Text>
                <MoneyText value={d.memey.cashNow} size="sm" />
              </View>
              <View style={styles.line}>
                <Text style={{ color: colors.ink }}>Fin de mes</Text>
                <MoneyText value={d.memey.projectedEndOfMonth} size="sm" />
              </View>
            </Card>

            <Card
              title="Pagos de tarjeta"
              accent={accent}
              right={
                <Pressable onPress={() => navigation.navigate('Tarjetas')} hitSlop={8}>
                  <Text style={{ color: accent, fontWeight: '600' }}>Ver</Text>
                </Pressable>
              }
            >
              {d.cardPayments.length === 0 ? (
                <Text style={{ color: colors.inkMuted }}>Sin vencimientos próximos.</Text>
              ) : (
                d.cardPayments.map((p) => (
                  <View key={p.dueDate} style={styles.line}>
                    <View style={{ flexShrink: 1 }}>
                      <Text style={{ color: colors.ink }}>{formatDate(p.dueDate)}</Text>
                      <Text style={{ color: colors.inkMuted, fontSize: 12 }}>{p.cards.join(', ')}</Text>
                    </View>
                    <MoneyText value={p.total} size="sm" />
                  </View>
                ))
              )}
            </Card>

            <Pressable onPress={() => navigation.navigate('Reporte')} style={styles.link}>
              <Text style={{ color: accent, fontWeight: '600' }}>Ver presupuesto vs. real →</Text>
            </Pressable>
          </>
        )}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  grid: { flexDirection: 'row', gap: spacing.md },
  half: { flex: 1 },
  line: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  link: { alignItems: 'center', padding: spacing.md },
});
