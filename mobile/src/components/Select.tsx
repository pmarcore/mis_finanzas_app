import { Ionicons } from '@expo/vector-icons';
import { useState } from 'react';
import { FlatList, Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { radius, spacing, useTheme } from '../theme';

type Key = string | number;

interface Props<T> {
  items: T[];
  getKey: (item: T) => Key;
  getLabel: (item: T) => string;
  selectedKey: Key | undefined | null;
  onSelect: (item: T) => void;
  /** Título de la lista desplegada. */
  title: string;
  placeholder?: string;
  /** Agrega una opción para dejar el campo vacío (campos opcionales). */
  clearLabel?: string;
  onClear?: () => void;
  accent?: string;
}

/** Combo: muestra la opción elegida y al tocarlo despliega la lista para elegir otra. */
export function Select<T>({
  items,
  getKey,
  getLabel,
  selectedKey,
  onSelect,
  title,
  placeholder = 'Elegí una opción',
  clearLabel,
  onClear,
  accent,
}: Props<T>) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const [open, setOpen] = useState(false);
  const tint = accent ?? colors.familia;
  const selected = items.find((i) => getKey(i) === selectedKey);

  const option = (label: string, isSelected: boolean, onPress: () => void, muted = false) => (
    <Pressable
      onPress={() => {
        onPress();
        setOpen(false);
      }}
      accessibilityRole="button"
      accessibilityState={{ selected: isSelected }}
      style={({ pressed }) => [styles.option, { borderBottomColor: colors.border }, pressed && { backgroundColor: colors.ground }]}
    >
      <Text
        style={{
          flex: 1,
          fontSize: 16,
          color: isSelected ? tint : muted ? colors.inkMuted : colors.ink,
          fontWeight: isSelected ? '600' : '400',
        }}
      >
        {label}
      </Text>
      {isSelected && <Ionicons name="checkmark" size={20} color={tint} />}
    </Pressable>
  );

  return (
    <>
      <Pressable
        onPress={() => setOpen(true)}
        accessibilityRole="button"
        accessibilityLabel={title}
        style={[styles.field, { borderColor: colors.border, backgroundColor: colors.surface }]}
      >
        <Text style={{ flex: 1, fontSize: 16, color: selected ? colors.ink : colors.inkMuted }} numberOfLines={1}>
          {selected ? getLabel(selected) : placeholder}
        </Text>
        <Ionicons name="chevron-down" size={18} color={colors.inkMuted} />
      </Pressable>

      <Modal visible={open} transparent animationType="slide" onRequestClose={() => setOpen(false)}>
        <Pressable style={styles.backdrop} onPress={() => setOpen(false)} />
        <View style={[styles.sheet, { backgroundColor: colors.surface, paddingBottom: insets.bottom + spacing.md }]}>
          <View style={[styles.sheetHeader, { borderBottomColor: colors.border }]}>
            <Text style={{ fontSize: 16, fontWeight: '700', color: colors.ink }}>{title}</Text>
            <Pressable onPress={() => setOpen(false)} hitSlop={12} accessibilityLabel="Cerrar">
              <Ionicons name="close" size={22} color={colors.inkMuted} />
            </Pressable>
          </View>
          <FlatList
            data={items}
            keyExtractor={(i) => String(getKey(i))}
            ListHeaderComponent={
              clearLabel && onClear ? option(clearLabel, selectedKey == null, onClear, true) : null
            }
            ListEmptyComponent={
              <Text style={{ color: colors.inkMuted, padding: spacing.lg }}>No hay opciones.</Text>
            }
            renderItem={({ item }) => option(getLabel(item), getKey(item) === selectedKey, () => onSelect(item))}
          />
        </View>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  field: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm + 2,
  },
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.35)' },
  sheet: {
    maxHeight: '70%',
    borderTopLeftRadius: radius.lg,
    borderTopRightRadius: radius.lg,
  },
  sheetHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: spacing.lg,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  option: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md + 2,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
});
