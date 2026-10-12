import { Text, View } from 'react-native';
import { useOperationTypes, useSavings } from '../api/endpoints';
import type { SavingsMovement } from '../api/types';
import { Card } from '../components/Card';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Screen } from '../components/Screen';
import { useScope } from '../state/scope';
import { scopeColor, spacing, useTheme } from '../theme';
import { formatDate } from '../utils/format';

/** Ahorro e inversión del ámbito, aparte de la caja: lo que salió hacia ellos menos lo que volvió. */
export function AhorroScreen() {
  const { scope, cutoff } = useScope();
  const { colors } = useTheme();
  const accent = scopeColor(colors, scope);
  const q = useSavings(scope, cutoff);
  const types = useOperationTypes();
  const s = q.data;

  const movements = (list: SavingsMovement[]) =>
    list.length === 0 ? (
      <Text style={{ color: colors.inkMuted }}>Sin movimientos.</Text>
    ) : (
      list.map((m) => (
        <View key={m.id} style={styles.line}>
          <View style={{ flexShrink: 1 }}>
            <Text style={{ color: colors.ink }} numberOfLines={1}>
              {m.description || types.data?.find((t) => t.id === m.operationTypeId)?.name}
            </Text>
            <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
              {formatDate(m.date)} · {types.data?.find((t) => t.id === m.operationTypeId)?.name ?? ''}
              {m.status === 'previsto' ? ' · previsto' : ''}
            </Text>
          </View>
          <MoneyText value={m.amount} size="sm" />
        </View>
      ))
    );

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <ScopeSelector />
      <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
        Al {formatDate(cutoff)}. Se cargan como movimientos con un tipo de Ahorro o Inversión: salen de la caja pero no son gasto.
      </Text>
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        {s && (
          <>
            <Card title="Ahorro" accent={accent}>
              <MoneyText value={s.ahorro.balance} size="lg" />
              {movements(s.ahorro.movements)}
            </Card>
            <Card title="Inversiones" accent={accent}>
              <MoneyText value={s.inversion.balance} size="lg" />
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>Aportado neto (aportes menos rescates).</Text>
              {s.inversion.marketValue !== null ? (
                <View style={styles.line}>
                  <Text style={{ color: colors.ink }}>
                    Valor de mercado{s.inversion.marketValueDate ? ` al ${formatDate(s.inversion.marketValueDate)}` : ''}
                  </Text>
                  <MoneyText value={s.inversion.marketValue} size="sm" />
                </View>
              ) : (
                <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                  {s.inversion.providerConnected
                    ? 'Todavía no se trajo el valor de mercado.'
                    : 'Valor de mercado: pendiente de conectar con Invertir Online.'}
                </Text>
              )}
              {movements(s.inversion.movements)}
            </Card>
          </>
        )}
      </QueryState>
    </Screen>
  );
}

const styles = {
  line: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
} as const;
