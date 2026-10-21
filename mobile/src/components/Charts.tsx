import { useState } from 'react';
import { Pressable, StyleSheet, Text, View, type GestureResponderEvent, type LayoutChangeEvent } from 'react-native';
import Svg, { Circle, Line, Path } from 'react-native-svg';
import type { AssetType } from '../api/types';
import { spacing, useTheme } from '../theme';

// Gráficos chicos hechos con react-native-svg (funcionan en el teléfono y en Expo web).
// Colores categóricos en orden fijo y siempre el mismo por tipo de activo, nunca según el ranking.

const CATEGORICAL = {
  light: ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#6250d6', '#e34948'],
  dark: ['#3987e5', '#d95926', '#199e70', '#c98500', '#d55181', '#008300', '#9085e9', '#e66767'],
};

const ASSET_ORDER: AssetType[] = ['acciones', 'cedears', 'bonos', 'ons', 'fci', 'cripto', 'efectivo', 'otros'];

/** Color fijo de un tipo de activo, de una moneda ('ars'/'usd') o de un origen ('invertirOnline'/'ripio'). */
export function useSeriesColor() {
  const { dark } = useTheme();
  const palette = dark ? CATEGORICAL.dark : CATEGORICAL.light;
  return (key: string): string => {
    const i = ASSET_ORDER.indexOf(key as AssetType);
    if (i >= 0) return palette[i];
    if (key === 'ars' || key === 'invertirOnline') return palette[0];
    if (key === 'usd' || key === 'ripio') return palette[1];
    return palette[7];
  };
}

export interface DonutSlice {
  key: string;
  label: string;
  value: number;
  color: string;
  /** Texto a la derecha de la leyenda (monto ya formateado). */
  valueText: string;
  pct: number;
}

/** Anillo con separación entre segmentos y leyenda con monto y % (la leyenda hace de tabla). */
export function DonutChart({ slices, size = 168, center }: { slices: DonutSlice[]; size?: number; center?: { label: string; value: string } }) {
  const { colors } = useTheme();
  const stroke = 22;
  const r = (size - stroke) / 2;
  const circumference = 2 * Math.PI * r;
  const total = slices.reduce((s, x) => s + Math.max(0, x.value), 0);
  const gap = slices.length > 1 ? 2 : 0;
  let offset = 0;

  return (
    <View style={styles.donutRow}>
      <View style={{ width: size, height: size }}>
        <Svg width={size} height={size}>
          <Circle cx={size / 2} cy={size / 2} r={r} stroke={colors.ground} strokeWidth={stroke} fill="none" />
          {total > 0 &&
            slices.map((s) => {
              const len = (Math.max(0, s.value) / total) * circumference;
              const dash = Math.max(0, len - gap);
              const el = (
                <Circle
                  key={s.key}
                  cx={size / 2}
                  cy={size / 2}
                  r={r}
                  stroke={s.color}
                  strokeWidth={stroke}
                  fill="none"
                  strokeDasharray={`${dash} ${circumference - dash}`}
                  strokeDashoffset={-offset}
                  transform={`rotate(-90 ${size / 2} ${size / 2})`}
                />
              );
              offset += len;
              return el;
            })}
        </Svg>
        {center && (
          <View style={[StyleSheet.absoluteFill, styles.center]} pointerEvents="none">
            <Text style={{ color: colors.inkMuted, fontSize: 11 }}>{center.label}</Text>
            <Text style={{ color: colors.ink, fontSize: 15, fontWeight: '700', fontVariant: ['tabular-nums'] }}>{center.value}</Text>
          </View>
        )}
      </View>
      <View style={styles.legend}>
        {slices.map((s) => (
          <View key={s.key} style={styles.legendRow}>
            <View style={[styles.dot, { backgroundColor: s.color }]} />
            <Text style={{ color: colors.ink, flex: 1 }} numberOfLines={1}>
              {s.label}
            </Text>
            <Text style={{ color: colors.inkMuted, fontVariant: ['tabular-nums'] }}>{Math.round(s.pct)} %</Text>
          </View>
        ))}
      </View>
    </View>
  );
}

export interface LinePoint {
  label: string;
  value: number;
}

/**
 * Línea con área suave. Tocar o pasar el dedo (o el mouse apretado en web) muestra el punto más cercano.
 * `reference` dibuja una línea horizontal punteada (por ejemplo, el precio promedio de compra).
 */
export function LineChart({
  points,
  color,
  format,
  height = 160,
  reference,
}: {
  points: LinePoint[];
  color: string;
  format: (v: number) => string;
  height?: number;
  reference?: { value: number; label: string };
}) {
  const { colors } = useTheme();
  const [width, setWidth] = useState(0);
  const [active, setActive] = useState<number | null>(null);
  const pad = { top: 8, bottom: 8 };

  if (points.length === 0) return null;
  const values = points.map((p) => p.value).concat(reference ? [reference.value] : []);
  let min = Math.min(...values);
  let max = Math.max(...values);
  if (min === max) {
    min -= Math.abs(min) * 0.05 || 1;
    max += Math.abs(max) * 0.05 || 1;
  }
  const x = (i: number) => (points.length === 1 ? width / 2 : (i / (points.length - 1)) * width);
  const y = (v: number) => pad.top + (1 - (v - min) / (max - min)) * (height - pad.top - pad.bottom);
  const line = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(p.value).toFixed(1)}`).join(' ');
  const area = `${line} L${x(points.length - 1).toFixed(1)},${height} L${x(0).toFixed(1)},${height} Z`;

  const pick = (e: GestureResponderEvent) => {
    if (width === 0) return;
    const px = e.nativeEvent.locationX;
    setActive(Math.max(0, Math.min(points.length - 1, Math.round((px / width) * (points.length - 1)))));
  };
  const shown = active ?? points.length - 1;
  const p = points[shown];

  return (
    <View style={{ gap: spacing.xs }}>
      <View style={styles.lineHeader}>
        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>{p.label}</Text>
        <Text style={{ color: colors.ink, fontWeight: '700', fontVariant: ['tabular-nums'] }}>{format(p.value)}</Text>
      </View>
      <View
        style={{ height }}
        onLayout={(e: LayoutChangeEvent) => setWidth(e.nativeEvent.layout.width)}
        onStartShouldSetResponder={() => true}
        onMoveShouldSetResponder={() => true}
        onResponderGrant={pick}
        onResponderMove={pick}
        onResponderRelease={() => setActive(null)}
        accessibilityLabel={`Gráfico de ${points.length} puntos, de ${format(points[0].value)} a ${format(points[points.length - 1].value)}`}
      >
        {width > 0 && (
          <Svg width={width} height={height}>
            <Line x1={0} x2={width} y1={height - 0.5} y2={height - 0.5} stroke={colors.border} strokeWidth={1} />
            <Path d={area} fill={color} fillOpacity={0.12} />
            <Path d={line} stroke={color} strokeWidth={2} fill="none" strokeLinejoin="round" strokeLinecap="round" />
            {reference && (
              <Line x1={0} x2={width} y1={y(reference.value)} y2={y(reference.value)} stroke={colors.inkMuted} strokeWidth={1} strokeDasharray="4 4" />
            )}
            <Line x1={x(shown)} x2={x(shown)} y1={0} y2={height} stroke={colors.border} strokeWidth={1} />
            <Circle cx={x(shown)} cy={y(p.value)} r={5} fill={color} stroke={colors.surface} strokeWidth={2} />
          </Svg>
        )}
      </View>
      <View style={styles.lineHeader}>
        <Text style={{ color: colors.inkMuted, fontSize: 11 }}>{points[0].label}</Text>
        {reference && <Text style={{ color: colors.inkMuted, fontSize: 11 }}>- - {reference.label}</Text>}
        <Text style={{ color: colors.inkMuted, fontSize: 11 }}>{points[points.length - 1].label}</Text>
      </View>
    </View>
  );
}

export interface BarItem {
  key: string;
  label: string;
  value: number;
  valueText: string;
}

/** Barras horizontales desde un eje en cero: verdes a la derecha, rojas a la izquierda, con el valor escrito. */
export function DivergingBars({ items, onPress }: { items: BarItem[]; onPress?: (key: string) => void }) {
  const { colors } = useTheme();
  const maxAbs = Math.max(...items.map((i) => Math.abs(i.value)), 1);
  const hasNeg = items.some((i) => i.value < 0);
  const hasPos = items.some((i) => i.value > 0);
  const negShare = hasNeg && hasPos ? 0.5 : hasNeg ? 1 : 0;

  return (
    <View style={{ gap: 6 }}>
      {items.map((i) => {
        const share = Math.abs(i.value) / maxAbs;
        const color = i.value >= 0 ? colors.good : colors.bad;
        return (
          <Pressable key={i.key} style={styles.barRow} disabled={!onPress} onPress={() => onPress?.(i.key)}>
            <Text style={{ color: colors.ink, width: 64, fontWeight: '600' }} numberOfLines={1}>
              {i.label}
            </Text>
            <View style={styles.barTrack}>
              <View style={{ flex: negShare, flexDirection: 'row', justifyContent: 'flex-end' }}>
                {i.value < 0 && <View style={[styles.bar, styles.barLeft, { width: `${share * 100}%`, backgroundColor: color }]} />}
              </View>
              {hasNeg && hasPos && <View style={{ width: 1, backgroundColor: colors.inkMuted }} />}
              <View style={{ flex: 1 - negShare, flexDirection: 'row' }}>
                {i.value >= 0 && <View style={[styles.bar, styles.barRight, { width: `${share * 100}%`, backgroundColor: color }]} />}
              </View>
            </View>
            <Text style={{ color: colors.ink, width: 72, textAlign: 'right', fontVariant: ['tabular-nums'] }}>{i.valueText}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  donutRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.lg, flexWrap: 'wrap' },
  center: { alignItems: 'center', justifyContent: 'center' },
  legend: { flex: 1, minWidth: 140, gap: 6 },
  legendRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  dot: { width: 10, height: 10, borderRadius: 5 },
  lineHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
  barRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  barTrack: { flex: 1, flexDirection: 'row', height: 14 },
  bar: { height: 14 },
  barLeft: { borderTopLeftRadius: 4, borderBottomLeftRadius: 4 },
  barRight: { borderTopRightRadius: 4, borderBottomRightRadius: 4 },
});
