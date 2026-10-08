import type { ReactNode } from 'react';
import { RefreshControl, ScrollView, StyleSheet } from 'react-native';
import { spacing, useTheme } from '../theme';

interface Props {
  children: ReactNode;
  refreshing?: boolean;
  onRefresh?: () => void;
}

/** Contenedor con scroll, fondo del tema y pull-to-refresh opcional. */
export function Screen({ children, refreshing = false, onRefresh }: Props) {
  const { colors } = useTheme();
  return (
    <ScrollView
      style={{ backgroundColor: colors.ground }}
      contentContainerStyle={styles.content}
      keyboardShouldPersistTaps="handled"
      refreshControl={onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} /> : undefined}
    >
      {children}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, gap: spacing.md, paddingBottom: spacing.xl * 2 },
});
