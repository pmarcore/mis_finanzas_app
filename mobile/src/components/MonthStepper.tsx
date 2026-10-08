import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import type { Month } from '../api/types';
import { spacing, useTheme } from '../theme';
import { addMonths, formatMonth } from '../utils/format';

export function MonthStepper({ value, onChange }: { value: Month; onChange: (m: Month) => void }) {
  const { colors } = useTheme();
  return (
    <View style={styles.row}>
      <Pressable onPress={() => onChange(addMonths(value, -1))} hitSlop={10} accessibilityLabel="Mes anterior">
        <Ionicons name="chevron-back" size={22} color={colors.ink} />
      </Pressable>
      <Text style={[styles.label, { color: colors.ink }]}>{formatMonth(value)}</Text>
      <Pressable onPress={() => onChange(addMonths(value, 1))} hitSlop={10} accessibilityLabel="Mes siguiente">
        <Ionicons name="chevron-forward" size={22} color={colors.ink} />
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.lg },
  label: { fontSize: 17, fontWeight: '700', minWidth: 110, textAlign: 'center' },
});
