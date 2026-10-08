import { Pressable, StyleSheet, Text, View } from 'react-native';
import { radius, spacing, useTheme } from '../theme';

export interface SegmentOption<T extends string> {
  value: T;
  label: string;
  color?: string;
}

interface Props<T extends string> {
  options: SegmentOption<T>[];
  value: T | undefined;
  onChange: (v: T) => void;
}

/** Control segmentado genérico (también sirve como selector de opciones cortas). */
export function Segmented<T extends string>({ options, value, onChange }: Props<T>) {
  const { colors } = useTheme();
  return (
    <View style={[styles.row, { backgroundColor: colors.ground, borderColor: colors.border }]}>
      {options.map((o) => {
        const selected = o.value === value;
        const accent = o.color ?? colors.ink;
        return (
          <Pressable
            key={o.value}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            onPress={() => onChange(o.value)}
            style={[styles.item, selected && { backgroundColor: accent }]}
          >
            <Text
              numberOfLines={1}
              style={[styles.label, { color: selected ? colors.surface : colors.ink }]}
            >
              {o.label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    borderRadius: radius.md,
    borderWidth: StyleSheet.hairlineWidth,
    padding: 2,
  },
  item: {
    flex: 1,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.xs,
    borderRadius: radius.md - 2,
    alignItems: 'center',
  },
  label: { fontSize: 14, fontWeight: '600' },
});
