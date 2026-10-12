import { Ionicons } from '@expo/vector-icons';
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
import { formatARS, formatDate, formatMonth } from '../utils/format';

export function InicioScreen({ navigation }: TabScreenProps<'Inicio'>) {
  const { scope, setScope, cutoff } = useScope();
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
            <View style={styles.grid}>
              <Pressable
                style={styles.half}
                onPress={() => navigation.navigate('Movimientos', { vista: 'caja', marca: 'corte', n: Date.now() })}
                accessibilityHint="Abre el flujo de caja marcando el corte"
              >
                <Card title="Caja actual" accent={accent} right={<Chevron color={colors.inkMuted} />}>
                  <MoneyText value={d.cashNow} size="lg" />
                </Card>
              </Pressable>
              <Pressable
                style={styles.half}
                onPress={() => navigation.navigate('Movimientos', { vista: 'caja', marca: 'finDeMes', n: Date.now() })}
                accessibilityHint="Abre el flujo de caja hasta fin de mes"
              >
                <Card title="Fin de mes" accent={accent} right={<Chevron color={colors.inkMuted} />}>
                  <MoneyText value={d.projectedEndOfMonth} size="lg" />
                </Card>
              </Pressable>
            </View>

            <Card title="Proyección trimestral" accent={accent}>
              {d.quarter.map((m) => {
                const spend = d.estimatedMonthlySpend.find((e) => e.month === m.month);
                return (
                  <View key={m.month} style={styles.line}>
                    <View style={{ flexShrink: 1 }}>
                      <Text style={{ color: colors.ink }}>{formatMonth(m.month)}</Text>
                      {spend && (
                        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                          Gasto estimado familia {formatARS(spend.amount)}
                        </Text>
                      )}
                    </View>
                    <MoneyText value={m.balance} size="sm" />
                  </View>
                );
              })}
            </Card>

            <Pressable
              onPress={() => {
                setScope('memey');
                navigation.navigate('Movimientos', { vista: 'lista' });
              }}
              accessibilityHint="Abre los movimientos de Memey"
            >
              <Card title="Caja Memey" accent={colors.memey} right={<Chevron color={colors.inkMuted} />}>
                <View style={styles.line}>
                  <Text style={{ color: colors.ink }}>Actual</Text>
                  <MoneyText value={d.memey.cashNow} size="sm" />
                </View>
                <View style={styles.line}>
                  <Text style={{ color: colors.ink }}>Fin de mes</Text>
                  <MoneyText value={d.memey.projectedEndOfMonth} size="sm" />
                </View>
              </Card>
            </Pressable>

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
                      <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                        {p.byCard.length > 0
                          ? p.byCard.map((c) => `${c.card} ${formatARS(c.total)}`).join(' · ')
                          : p.cards.join(', ')}
                      </Text>
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

function Chevron({ color }: { color: string }) {
  return <Ionicons name="chevron-forward" size={16} color={color} />;
}

const styles = StyleSheet.create({
  grid: { flexDirection: 'row', gap: spacing.md },
  half: { flex: 1 },
  line: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  link: { alignItems: 'center', padding: spacing.md },
});
