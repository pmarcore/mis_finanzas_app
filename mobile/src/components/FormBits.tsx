import { Pressable, StyleSheet, Switch, Text, View } from 'react-native';
import { radius, spacing, useTheme } from '../theme';

/** Fila con interruptor (sí/no) para formularios de configuración. */
export function SwitchRow({
  label,
  value,
  onChange,
  accent,
}: {
  label: string;
  value: boolean;
  onChange: (v: boolean) => void;
  accent?: string;
}) {
  const { colors } = useTheme();
  return (
    <View style={styles.switchRow}>
      <Text style={{ color: colors.ink, fontSize: 15, flex: 1 }}>{label}</Text>
      <Switch value={value} onValueChange={onChange} trackColor={{ true: accent ?? colors.familia }} />
    </View>
  );
}

/** Cancelar / Guardar y, al editar, Borrar. */
export function FormButtons({
  onCancel,
  onSave,
  onDelete,
  pending,
  accent,
}: {
  onCancel: () => void;
  onSave: () => void;
  onDelete?: () => void;
  pending: boolean;
  accent?: string;
}) {
  const { colors } = useTheme();
  return (
    <View style={{ gap: spacing.sm }}>
      <View style={styles.row}>
        <Pressable onPress={onCancel} style={[styles.button, { borderColor: colors.border, borderWidth: 1 }]}>
          <Text style={{ color: colors.ink, fontWeight: '600' }}>Cancelar</Text>
        </Pressable>
        <Pressable
          onPress={onSave}
          disabled={pending}
          style={[styles.button, { backgroundColor: accent ?? colors.familia, opacity: pending ? 0.6 : 1 }]}
        >
          <Text style={{ color: colors.surface, fontWeight: '700' }}>{pending ? 'Guardando…' : 'Guardar'}</Text>
        </Pressable>
      </View>
      {onDelete && (
        <Pressable onPress={onDelete} hitSlop={8} style={{ alignItems: 'center', padding: spacing.xs }}>
          <Text style={{ color: colors.bad, fontWeight: '600' }}>Borrar</Text>
        </Pressable>
      )}
    </View>
  );
}

/** Botón "+ Nuevo …" con borde del color del ámbito. */
export function NewButton({ label, onPress, accent }: { label: string; onPress: () => void; accent?: string }) {
  const { colors } = useTheme();
  const tint = accent ?? colors.familia;
  return (
    <Pressable onPress={onPress} style={[styles.newButton, { borderColor: tint }]}>
      <Text style={{ color: tint, fontWeight: '700' }}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.xs },
  button: { flex: 1, borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  newButton: { borderWidth: 1, borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
});
