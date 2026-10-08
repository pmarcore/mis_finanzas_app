import type { ReactNode } from 'react';
import { StyleSheet, Text, TextInput, View, type TextInputProps } from 'react-native';
import { radius, spacing, useTheme } from '../theme';

interface FieldProps {
  label: string;
  error?: string;
  hint?: string;
  children: ReactNode;
}

export function Field({ label, error, hint, children }: FieldProps) {
  const { colors } = useTheme();
  return (
    <View style={styles.field}>
      <Text style={[styles.label, { color: colors.inkMuted }]}>{label}</Text>
      {children}
      {error ? (
        <Text style={[styles.help, { color: colors.bad }]}>{error}</Text>
      ) : hint ? (
        <Text style={[styles.help, { color: colors.inkMuted }]}>{hint}</Text>
      ) : null}
    </View>
  );
}

export function Input(props: TextInputProps) {
  const { colors } = useTheme();
  return (
    <TextInput
      placeholderTextColor={colors.inkMuted}
      {...props}
      style={[
        styles.input,
        { color: colors.ink, borderColor: colors.border, backgroundColor: colors.surface },
        props.style,
      ]}
    />
  );
}

/** Campo de fecha simple como texto 'AAAA-MM-DD' (sin dependencia nativa de date picker). */
export function DateInput(props: Omit<TextInputProps, 'keyboardType' | 'maxLength'>) {
  return <Input placeholder="AAAA-MM-DD" keyboardType="numbers-and-punctuation" maxLength={10} autoCorrect={false} {...props} />;
}

const styles = StyleSheet.create({
  field: { gap: spacing.xs },
  label: { fontSize: 13, fontWeight: '600' },
  help: { fontSize: 12 },
  input: {
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    fontSize: 16,
  },
});
