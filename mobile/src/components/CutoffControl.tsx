import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useScope } from '../state/scope';
import { spacing, useTheme } from '../theme';
import { isValidISODate, todayISO } from '../utils/format';
import { DateInput } from './Field';

/** Fecha de corte global (por defecto hoy). Se aplica sólo cuando la fecha es válida. */
export function CutoffControl() {
  const { cutoff, setCutoff, resetCutoff } = useScope();
  const { colors } = useTheme();
  const [draft, setDraft] = useState(cutoff);
  useEffect(() => setDraft(cutoff), [cutoff]);

  const invalid = draft.length === 10 && !isValidISODate(draft);
  const isToday = cutoff === todayISO();

  return (
    <View style={styles.row}>
      <Text style={{ color: colors.inkMuted, fontWeight: '600' }}>Corte</Text>
      <DateInput
        value={draft}
        onChangeText={(t) => {
          setDraft(t);
          if (isValidISODate(t)) setCutoff(t);
        }}
        style={[styles.input, invalid && { borderColor: colors.bad }]}
      />
      {!isToday && (
        <Pressable onPress={resetCutoff} hitSlop={8}>
          <Text style={{ color: colors.familia, fontWeight: '600' }}>Hoy</Text>
        </Pressable>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  input: { flex: 1 },
});
