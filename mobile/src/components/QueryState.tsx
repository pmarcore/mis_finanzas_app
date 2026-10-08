import type { ReactNode } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { spacing, useTheme } from '../theme';

interface Props {
  isLoading: boolean;
  error: unknown;
  isEmpty?: boolean;
  emptyText?: string;
  onRetry?: () => void;
  children: ReactNode;
}

/** Renderiza carga / error / vacío de forma uniforme; si no, los children. */
export function QueryState({ isLoading, error, isEmpty, emptyText = 'Sin datos.', onRetry, children }: Props) {
  const { colors } = useTheme();
  if (isLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator color={colors.familia} />
      </View>
    );
  }
  if (error) {
    return (
      <View style={styles.center}>
        <Text style={{ color: colors.bad, textAlign: 'center' }}>
          No se pudo cargar. {error instanceof Error ? error.message : ''}
        </Text>
        {onRetry && (
          <Pressable onPress={onRetry} style={styles.retry}>
            <Text style={{ color: colors.familia, fontWeight: '600' }}>Reintentar</Text>
          </Pressable>
        )}
      </View>
    );
  }
  if (isEmpty) {
    return (
      <View style={styles.center}>
        <Text style={{ color: colors.inkMuted }}>{emptyText}</Text>
      </View>
    );
  }
  return <>{children}</>;
}

const styles = StyleSheet.create({
  center: { padding: spacing.xl, alignItems: 'center', justifyContent: 'center', gap: spacing.sm },
  retry: { padding: spacing.sm },
});
