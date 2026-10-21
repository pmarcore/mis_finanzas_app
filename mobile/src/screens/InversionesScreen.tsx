import { Ionicons } from '@expo/vector-icons';
import { useNavigation } from '@react-navigation/native';
import { useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { useInvestmentDashboard, useInvestmentHistory, useRefreshInvestments } from '../api/endpoints';
import type { Holding, InvestmentCurrency, InvestmentDashboard, InvestmentSignal, InvestmentSlice } from '../api/types';
import { Card } from '../components/Card';
import { DivergingBars, DonutChart, LineChart, useSeriesColor } from '../components/Charts';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { spacing, radius, useTheme } from '../theme';
import { formatDate, formatMoney, formatNumber, formatSignedPct, todayISO } from '../utils/format';

/**
 * Dashboard de inversiones de Familia (Spec/inversiones-dashboard.md): IOL por su API (solo lectura) y Ripio cargado a mano.
 * Los rendimientos por activo son los que informa IOL, sin recalcular.
 */
export function InversionesScreen() {
  const { colors } = useTheme();
  const q = useInvestmentDashboard();
  const refresh = useRefreshInvestments();
  const [currency, setCurrency] = useState<InvestmentCurrency>('ars');
  const d = q.data;

  return (
    <Screen refreshing={refresh.isPending} onRefresh={() => refresh.mutate()}>
      <View style={styles.header}>
        <Text style={{ color: colors.inkMuted, fontSize: 12, flex: 1 }}>
          {d?.asOf ? `Datos ${formatDateTime(d.asOf)}` : 'Sin datos todavía'}
          {d?.mep ? ` · MEP ${formatMoney(d.mep, 'ars')}` : ''}
        </Text>
        <Pressable
          onPress={() => refresh.mutate()}
          disabled={refresh.isPending}
          style={[styles.refresh, { borderColor: colors.border }]}
          accessibilityRole="button"
        >
          {refresh.isPending ? <ActivityIndicator size="small" color={colors.familia} /> : <Ionicons name="refresh" size={16} color={colors.familia} />}
          <Text style={{ color: colors.familia, fontWeight: '600' }}>Actualizar</Text>
        </Pressable>
      </View>
      <Segmented
        options={[
          { value: 'ars', label: '$', color: colors.familia },
          { value: 'usd', label: 'US$', color: colors.familia },
        ]}
        value={currency}
        onChange={setCurrency}
      />
      {(d?.error || refresh.error) && (
        <Text style={{ color: colors.warn, fontSize: 13 }}>
          {d?.stale ? 'No se pudo actualizar, se muestran los últimos datos guardados. ' : ''}
          {d?.error ?? (refresh.error instanceof Error ? refresh.error.message : '')}
        </Text>
      )}
      {d && !d.mep && d.byCurrency.length > 1 && (
        <Text style={{ color: colors.warn, fontSize: 13 }}>
          Sin dólar MEP: lo que está en la otra moneda no se suma a los totales hasta que IOL informe el AL30 y el AL30D.
        </Text>
      )}
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        {d && (d.holdings.length === 0 ? <Empty d={d} /> : <Dashboard d={d} currency={currency} />)}
      </QueryState>
    </Screen>
  );
}

function Empty({ d }: { d: InvestmentDashboard }) {
  const { colors } = useTheme();
  const navigation = useNavigation();
  return (
    <Card title="Inversiones">
      <Text style={{ color: colors.ink }}>
        {d.providerConnected
          ? 'IOL no informó tenencias todavía.'
          : 'Invertir Online no está conectado. Cargá IOL_USERNAME e IOL_PASSWORD en el archivo .env de la raíz del proyecto (al lado de docker-compose.yml) y reiniciá con docker compose up -d.'}
      </Text>
      <Pressable onPress={() => navigation.navigate('InversionesConfig')} accessibilityRole="button">
        <Text style={{ color: colors.familia, fontWeight: '600' }}>Cargar tenencias de Ripio y configurar señales</Text>
      </Pressable>
    </Card>
  );
}

function Dashboard({ d, currency }: { d: InvestmentDashboard; currency: InvestmentCurrency }) {
  const navigation = useNavigation();
  const value = (h: Holding) => (currency === 'ars' ? h.valueArs : h.valueUsd);
  const openAsset = (h: Holding) => navigation.navigate('Activo', { source: h.source, symbol: h.symbol, market: h.market });

  return (
    <>
      <Kpis d={d} currency={currency} />
      <Composition d={d} currency={currency} />
      <Evolution currency={currency} />
      <Card title="Rendimiento por activo">
        <DivergingBars
          items={d.holdings
            .filter((h) => h.gainPct !== null && h.type !== 'efectivo')
            .sort((a, b) => (b.gainPct ?? 0) - (a.gainPct ?? 0))
            .map((h) => ({ key: `${h.source}:${h.symbol}`, label: h.symbol, value: h.gainPct ?? 0, valueText: formatSignedPct(h.gainPct ?? 0) }))}
          onPress={(key) => {
            const h = d.holdings.find((x) => `${x.source}:${x.symbol}` === key);
            if (h) openAsset(h);
          }}
        />
        <Note text="Ganancia % desde la compra, tal cual la informa IOL. En Ripio la calcula la app con el costo que cargaste (*)." />
      </Card>
      <Holdings holdings={d.holdings} value={value} currency={currency} onPress={openAsset} />
      <Quotes d={d} />
      <Signals signals={d.signals} targetsConfigured={d.targetsConfigured} />
    </>
  );
}

function Kpis({ d, currency }: { d: InvestmentDashboard; currency: InvestmentCurrency }) {
  const { colors } = useTheme();
  const t = currency === 'ars' ? d.ars : d.usd;
  const sign = (v: number) => (v > 0 ? colors.good : v < 0 ? colors.bad : colors.ink);
  const tiles = [
    { label: 'Valor total', value: formatMoney(t.value, currency), sub: undefined, color: colors.ink },
    {
      label: 'Ganancia total',
      value: formatMoney(t.gain, currency),
      sub: t.gainPct !== null ? formatSignedPct(t.gainPct) : undefined,
      color: sign(t.gain),
    },
    {
      label: 'Variación del día',
      value: formatMoney(t.dailyChange, currency),
      sub: t.dailyChangePct !== null ? formatSignedPct(t.dailyChangePct) : undefined,
      color: sign(t.dailyChange),
    },
    { label: 'Sin invertir', value: formatMoney(t.cash, currency), sub: undefined, color: colors.ink },
  ];
  return (
    <View style={{ gap: spacing.xs }}>
      <View style={styles.tiles}>
        {tiles.map((k) => (
          <View key={k.label} style={[styles.tile, { backgroundColor: colors.surface, borderColor: colors.border }]}>
            <Text style={{ color: colors.inkMuted, fontSize: 12, fontWeight: '600' }}>{k.label}</Text>
            <Text style={{ color: k.color, fontSize: 19, fontWeight: '700', fontVariant: ['tabular-nums'] }} numberOfLines={1} adjustsFontSizeToFit>
              {k.value}
            </Text>
            {k.sub && <Text style={{ color: k.color, fontSize: 13, fontVariant: ['tabular-nums'] }}>{k.sub}</Text>}
          </View>
        ))}
      </View>
      <Note text="Total: suma de lo informado por IOL (y Ripio). Lo que está en la otra moneda se convierte con el dólar MEP." />
    </View>
  );
}

type Breakdown = 'byType' | 'byCurrency' | 'bySource';

function Composition({ d, currency }: { d: InvestmentDashboard; currency: InvestmentCurrency }) {
  const color = useSeriesColor();
  const [by, setBy] = useState<Breakdown>('byType');
  const slices: InvestmentSlice[] = d[by];
  const t = currency === 'ars' ? d.ars : d.usd;
  return (
    <Card title="Composición">
      <Segmented
        options={[
          { value: 'byType', label: 'Tipo' },
          { value: 'byCurrency', label: 'Moneda' },
          { value: 'bySource', label: 'Broker' },
        ]}
        value={by}
        onChange={setBy}
      />
      <DonutChart
        center={{ label: 'Total', value: formatMoney(t.value, currency) }}
        slices={slices.map((s) => ({
          key: s.key,
          label: s.label,
          value: currency === 'ars' ? s.ars : s.usd,
          valueText: formatMoney(currency === 'ars' ? s.ars : s.usd, currency),
          pct: s.pct,
          color: color(s.key),
        }))}
      />
    </Card>
  );
}

const RANGES = [
  { value: '1', label: '1M' },
  { value: '3', label: '3M' },
  { value: '12', label: '1A' },
  { value: 'all', label: 'Todo' },
] as const;
type Range = (typeof RANGES)[number]['value'];

function Evolution({ currency }: { currency: InvestmentCurrency }) {
  const { colors } = useTheme();
  const q = useInvestmentHistory();
  const [range, setRange] = useState<Range>('3');
  const points = useMemo(() => {
    const all = q.data ?? [];
    if (range === 'all') return all;
    const from = new Date();
    from.setMonth(from.getMonth() - Number(range));
    const iso = from.toISOString().slice(0, 10);
    return all.filter((p) => p.date >= iso);
  }, [q.data, range]);

  return (
    <Card title="Evolución">
      <Segmented options={[...RANGES]} value={range} onChange={setRange} />
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        {points.length < 2 ? (
          <Text style={{ color: colors.inkMuted }}>
            IOL no da el historial de la cartera: la app guarda una foto por día desde la primera sincronización. Con dos días ya
            aparece el gráfico.
          </Text>
        ) : (
          <LineChart
            color={colors.familia}
            points={points.map((p) => ({ label: formatDate(p.date), value: currency === 'ars' ? p.ars : p.usd }))}
            format={(v) => formatMoney(v, currency)}
          />
        )}
      </QueryState>
    </Card>
  );
}

function Holdings({
  holdings,
  value,
  currency,
  onPress,
}: {
  holdings: Holding[];
  value: (h: Holding) => number;
  currency: InvestmentCurrency;
  onPress: (h: Holding) => void;
}) {
  const { colors } = useTheme();
  const color = useSeriesColor();
  return (
    <Card title="Tenencias">
      {holdings.map((h) => {
        const gainColor = (h.gainPct ?? 0) > 0 ? colors.good : (h.gainPct ?? 0) < 0 ? colors.bad : colors.inkMuted;
        const money = (v: number) => `${h.currency === 'usd' ? 'US$' : '$'} ${formatNumber(v)}`;
        const dayColor = (h.dailyChangePct ?? 0) > 0 ? colors.good : (h.dailyChangePct ?? 0) < 0 ? colors.bad : colors.inkMuted;
        return (
          <Pressable
            key={`${h.source}:${h.symbol}`}
            onPress={() => onPress(h)}
            disabled={h.type === 'efectivo'}
            style={[styles.holding, { borderBottomColor: colors.border }]}
            accessibilityRole="button"
          >
            <View style={[styles.typeBar, { backgroundColor: color(h.type) }]} />
            <View style={{ flex: 1, gap: 2 }}>
              <View style={styles.between}>
                <Text style={{ color: colors.ink, fontWeight: '700' }}>
                  {h.symbol}
                  <Text style={{ color: colors.inkMuted, fontWeight: '400', fontSize: 12 }}>
                    {'  '}
                    {h.source === 'ripio' ? 'Ripio' : 'IOL'}
                  </Text>
                </Text>
                <Text style={{ color: colors.ink, fontWeight: '700', fontVariant: ['tabular-nums'] }}>{formatMoney(value(h), currency)}</Text>
              </View>
              <View style={styles.between}>
                <Text style={{ color: colors.inkMuted, fontSize: 12, flex: 1 }} numberOfLines={1}>
                  {h.description}
                </Text>
                <Text style={{ color: colors.inkMuted, fontSize: 12, fontVariant: ['tabular-nums'] }}>{formatNumber(h.pct, 1)} % de la cartera</Text>
              </View>
              {h.type !== 'efectivo' && (
                <>
                  <Text style={{ color: colors.inkMuted, fontSize: 12, fontVariant: ['tabular-nums'] }}>
                    {formatNumber(h.quantity, 4)} × {money(h.lastPrice)}
                    {h.avgCost !== null ? ` · PPC ${money(h.avgCost)}` : ''}
                  </Text>
                  <View style={styles.between}>
                    <Text style={{ color: dayColor, fontSize: 12, fontVariant: ['tabular-nums'] }}>
                      {h.dailyChangePct !== null ? `Hoy ${formatSignedPct(h.dailyChangePct)}` : ''}
                    </Text>
                    {h.gainPct !== null && (
                      <Text style={{ color: gainColor, fontSize: 12, fontWeight: '600', fontVariant: ['tabular-nums'] }}>
                        {formatSignedPct(h.gainPct)}
                        {h.gainAmount !== null ? ` (${formatMoney(h.gainAmount, h.currency)})` : ''}
                        {h.gainIsComputed ? ' *' : ''}
                      </Text>
                    )}
                  </View>
                </>
              )}
            </View>
          </Pressable>
        );
      })}
    </Card>
  );
}

function Quotes({ d }: { d: InvestmentDashboard }) {
  const { colors } = useTheme();
  const navigation = useNavigation();
  const rows = [
    ...(d.mep ? [{ key: 'mep', symbol: 'Dólar MEP', text: formatMoney(d.mep, 'ars'), change: null as number | null, market: null as string | null }] : []),
    ...d.quotes.map((q) => ({
      key: `${q.market}:${q.symbol}`,
      symbol: q.symbol,
      text: q.currency === 'usd' ? `US$ ${formatNumber(q.lastPrice)}` : `$ ${formatNumber(q.lastPrice)}`,
      change: q.dailyChangePct,
      market: q.market,
    })),
  ];
  return (
    <Card
      title="Cotizaciones"
      right={
        <Pressable onPress={() => navigation.navigate('InversionesConfig')} hitSlop={8} accessibilityLabel="Editar lista de seguimiento">
          <Ionicons name="create-outline" size={18} color={colors.inkMuted} />
        </Pressable>
      }
    >
      <View style={styles.quotes}>
        {rows.map((r) => (
          <Pressable
            key={r.key}
            disabled={!r.market}
            onPress={() => r.market && navigation.navigate('Activo', { source: r.market === 'cripto' ? 'ripio' : 'invertirOnline', symbol: r.symbol, market: r.market })}
            style={[styles.quote, { backgroundColor: colors.ground }]}
          >
            <Text style={{ color: colors.inkMuted, fontSize: 12, fontWeight: '600' }}>{r.symbol}</Text>
            <Text style={{ color: colors.ink, fontWeight: '700', fontVariant: ['tabular-nums'] }}>{r.text}</Text>
            {r.change !== null && (
              <Text style={{ color: r.change > 0 ? colors.good : r.change < 0 ? colors.bad : colors.inkMuted, fontSize: 12 }}>
                {formatSignedPct(r.change)}
              </Text>
            )}
          </Pressable>
        ))}
      </View>
      <Note text="Las cotizaciones de tus activos están en Tenencias. Acá va el MEP y tu lista de seguimiento." />
    </Card>
  );
}

function Signals({ signals, targetsConfigured }: { signals: InvestmentSignal[]; targetsConfigured: boolean }) {
  const { colors } = useTheme();
  const navigation = useNavigation();
  const icon = (s: InvestmentSignal) =>
    s.kind === 'objetivo' ? 'git-compare-outline' : s.kind === 'plataQuieta' ? 'wallet-outline' : 'pie-chart-outline';
  return (
    <Card title="Oportunidades y rotación">
      <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
        Análisis informativo con los datos de la app. No es una recomendación de compra o venta ni asesoramiento financiero.
      </Text>
      {signals.length === 0 && <Text style={{ color: colors.ink }}>Nada para marcar con los umbrales actuales.</Text>}
      {signals.map((s, i) => (
        <View key={i} style={[styles.signal, { borderColor: s.severity === 'atencion' ? colors.warn : colors.border }]}>
          <Ionicons name={icon(s)} size={18} color={s.severity === 'atencion' ? colors.warn : colors.familia} />
          <View style={{ flex: 1, gap: 2 }}>
            <Text style={{ color: colors.ink, fontWeight: '600' }}>{s.title}</Text>
            <Text style={{ color: colors.inkMuted, fontSize: 13 }}>{s.detail}</Text>
          </View>
        </View>
      ))}
      <Pressable onPress={() => navigation.navigate('InversionesConfig')} accessibilityRole="button">
        <Text style={{ color: colors.familia, fontWeight: '600' }}>
          {targetsConfigured ? 'Editar cartera objetivo y umbrales' : 'Definí tu cartera objetivo para ver cuánto rotar'}
        </Text>
      </Pressable>
    </Card>
  );
}

function Note({ text }: { text: string }) {
  const { colors } = useTheme();
  return <Text style={{ color: colors.inkMuted, fontSize: 11 }}>{text}</Text>;
}

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  const date = iso.slice(0, 10) === todayISO() ? 'de hoy' : `al ${formatDate(iso.slice(0, 10))}`;
  return `${date} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  refresh: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  tiles: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  tile: {
    flexBasis: '47%',
    flexGrow: 1,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.lg,
    padding: spacing.md,
    gap: 2,
  },
  holding: { flexDirection: 'row', gap: spacing.sm, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth },
  typeBar: { width: 4, borderRadius: 2 },
  between: { flexDirection: 'row', justifyContent: 'space-between', gap: spacing.sm },
  quotes: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  quote: { borderRadius: radius.md, padding: spacing.sm, minWidth: 96, gap: 2 },
  signal: { flexDirection: 'row', gap: spacing.sm, borderWidth: 1, borderRadius: radius.md, padding: spacing.md },
});
