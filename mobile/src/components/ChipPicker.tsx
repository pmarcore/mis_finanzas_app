import { Pressable, StyleSheet, Text, View } from 'react-native';
import { radius, spacing, useTheme } from '../theme';

interface Props<T> {
  items: T[];
  getKey: (item: T) => string;
  getLabel: (item: T) => string;
  selectedKey: string | undefined;
  onSelect: (item: T) => void;
  accent?: string;
}

/** Selector de una opción entre muchas, como chips en filas. */
export function ChipPicker<T>({ items, getKey, getLabel, selectedKey, onSelect, accent }: Props<T>) {
  const { colors } = useTheme();
  const tint = accent ?? colors.familia;
  return (
    <View style={styles.wrap}>
      {items.map((item) => {
        const key = getKey(item);
        const selected = key === selectedKey;
        return (
          <Pressable
            key={key}
            onPress={() => onSelect(item)}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            style={[
              styles.chip,
              { borderColor: selected ? tint : colors.border, backgroundColor: selected ? tint : colors.surface },
            ]}
          >
            <Text style={{ color: selected ? colors.surface : colors.ink, fontSize: 14 }}>{getLabel(item)}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  chip: {
    borderWidth: 1,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs + 2,
  },
});
