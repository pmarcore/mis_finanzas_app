import { useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useCardCycles, usePaymentMethods } from '../api/endpoints';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import type { RootStackScreenProps } from '../navigation/types';
import { spacing, useTheme } from '../theme';
import { formatDate, formatMonth } from '../utils/format';

export function TarjetasScreen({ navigation }: RootStackScreenProps<'Tarjetas'>) {
  const { colors } = useTheme();
  const methods = usePaymentMethods();
  const cards = (methods.data ?? []).filter((m) => m.type === 'tarjeta');
  const [cardId, setCardId] = useState<string>();
  const selected = cardId ?? cards[0]?.id;
  const cycles = useCardCycles(selected);

  return (
    <Screen refreshing={cycles.isRefetching} onRefresh={cycles.refetch}>
      <QueryState
        isLoading={methods.isLoading}
        error={methods.error}
        onRetry={methods.refetch}
        isEmpty={cards.length === 0}
        emptyText="No hay tarjetas configuradas."
      >
        <ChipPicker
          items={cards}
          getKey={(c) => c.id}
          getLabel={(c) => c.name}
          selectedKey={selected}
          onSelect={(c) => setCardId(c.id)}
        />
        <Card
          title="Cierres y vencimientos"
          right={
            <Pressable onPress={() => navigation.navigate('Configuracion')} hitSlop={8}>
              <Text style={{ color: colors.familia, fontWeight: '600' }}>Editar</Text>
            </Pressable>
          }
        >
          <QueryState
            isLoading={cycles.isLoading}
            error={cycles.error}
            onRetry={cycles.refetch}
            isEmpty={cycles.data?.length === 0}
            emptyText="Sin ciclos cargados."
          >
            <View style={styles.line}>
              <Text style={[styles.head, { color: colors.inkMuted }]}>Mes</Text>
              <Text style={[styles.head, { color: colors.inkMuted }]}>Cierre</Text>
              <Text style={[styles.head, { color: colors.inkMuted }]}>Vence</Text>
            </View>
            {(cycles.data ?? []).map((c) => (
              <View key={c.month} style={styles.line}>
                <Text style={[styles.cell, { color: colors.ink }]}>{formatMonth(c.month)}</Text>
                <Text style={[styles.cell, { color: colors.ink }]}>{formatDate(c.closingDate)}</Text>
                <Text style={[styles.cell, { color: colors.ink }]}>{formatDate(c.dueDate)}</Text>
              </View>
            ))}
          </QueryState>
        </Card>
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', gap: spacing.sm },
  head: { flex: 1, fontSize: 12, fontWeight: '600' },
  cell: { flex: 1, fontVariant: ['tabular-nums'] },
});
