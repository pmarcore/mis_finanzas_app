import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { useAssetHistory, useInvestmentDashboard } from '../api/endpoints';
import { Card } from '../components/Card';
import { LineChart } from '../components/Charts';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import type { RootStackScreenProps } from '../navigation/types';
import { spacing, useTheme } from '../theme';
import { formatDate, formatMoney, formatNumber, formatSignedPct } from '../utils/format';

const MONTHS = [
  { value: '1', label: '1M' },
  { value: '3', label: '3M' },
  { value: '6', label: '6M' },
  { value: '12', label: '1A' },
] as const;

/** Detalle de un activo: cifras del broker y gráfico de precio con una línea en el precio promedio de compra. */
export function ActivoScreen({ route }: RootStackScreenProps<'Activo'>) {
  const { source, symbol, market } = route.params;
  const { colors } = useTheme();
  const [months, setMonths] = useState<(typeof MONTHS)[number]['value']>('6');
  const dashboard = useInvestmentDashboard();
  const history = useAssetHistory(symbol, market, Number(months));
  const h = dashboard.data?.holdings.find((x) => x.source === source && x.symbol === symbol);
  const quote = dashboard.data?.quotes.find((x) => x.symbol === symbol);
  const currency = h?.currency ?? quote?.currency ?? 'ars';
  const price = (v: number) => `${currency === 'usd' ? 'US$' : '$'} ${formatNumber(v)}`;

  const rows: [string, string, string?][] = h
    ? [
        ['Cantidad', formatNumber(h.quantity, 4)],
        ['Último precio', price(h.lastPrice)],
        ['Precio promedio de compra', h.avgCost !== null ? price(h.avgCost) : '—'],
        ['Variación del día', h.dailyChangePct !== null ? formatSignedPct(h.dailyChangePct) : '—', signColor(h.dailyChangePct)],
        ['Ganancia', h.gainAmount !== null ? formatMoney(h.gainAmount, h.currency) : '—', signColor(h.gainAmount)],
        ['Ganancia %', h.gainPct !== null ? formatSignedPct(h.gainPct) : '—', signColor(h.gainPct)],
        ['Valorizado', formatMoney(h.marketValue, h.currency)],
        ['% de la cartera', `${formatNumber(h.pct, 1)} %`],
      ]
    : quote
      ? [
          ['Último precio', price(quote.lastPrice)],
          ['Variación del día', quote.dailyChangePct !== null ? formatSignedPct(quote.dailyChangePct) : '—', signColor(quote.dailyChangePct)],
        ]
      : [];

  function signColor(v: number | null): string | undefined {
    return v === null || v === 0 ? undefined : v > 0 ? colors.good : colors.bad;
  }

  return (
    <Screen refreshing={history.isRefetching} onRefresh={history.refetch}>
      <Text style={{ color: colors.ink, fontSize: 22, fontWeight: '700' }}>{symbol}</Text>
      {(h?.description || quote?.description) && <Text style={{ color: colors.inkMuted }}>{h?.description ?? quote?.description}</Text>}
      <Card title="Precio">
        <Segmented options={[...MONTHS]} value={months} onChange={setMonths} />
        <QueryState isLoading={history.isLoading} error={history.error} onRetry={history.refetch}>
          {(history.data?.length ?? 0) < 2 ? (
            <Text style={{ color: colors.inkMuted }}>No hay serie de precios para este activo.</Text>
          ) : (
            <LineChart
              color={colors.familia}
              points={(history.data ?? []).map((p) => ({ label: formatDate(p.date), value: p.price }))}
              format={price}
              reference={h?.avgCost != null && !h.gainIsComputed ? { value: h.avgCost, label: `PPC ${price(h.avgCost)}` } : undefined}
            />
          )}
        </QueryState>
      </Card>
      {rows.length > 0 && (
        <Card title={h ? (h.source === 'ripio' ? 'Tu tenencia en Ripio' : 'Tu tenencia en IOL') : 'Cotización'}>
          {rows.map(([label, value, color]) => (
            <View key={label} style={[styles.row, { borderBottomColor: colors.border }]}>
              <Text style={{ color: colors.inkMuted }}>{label}</Text>
              <Text style={{ color: color ?? colors.ink, fontWeight: '600', fontVariant: ['tabular-nums'] }}>{value}</Text>
            </View>
          ))}
          {h?.gainIsComputed && (
            <Text style={{ color: colors.inkMuted, fontSize: 11 }}>Ripio no informa rendimiento: lo calcula la app con el costo que cargaste.</Text>
          )}
        </Card>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
});
