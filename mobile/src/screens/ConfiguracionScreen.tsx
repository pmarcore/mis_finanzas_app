import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import {
  useCardCycles,
  usePaymentMethods,
  useSettings,
  useUpdateCardCycles,
  useUpdateSettings,
} from '../api/endpoints';
import type { CardCycle, CashBox } from '../api/types';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { DateInput, Field, Input } from '../components/Field';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { addMonths, isValidISODate, monthOf, todayISO } from '../utils/format';

const toNumber = (t: string) => Number(t.replace(/\./g, '').replace(',', '.')) || 0;

function SaveButton({ onPress, pending, label = 'Guardar' }: { onPress: () => void; pending: boolean; label?: string }) {
  const { colors } = useTheme();
  return (
    <Pressable
      onPress={onPress}
      disabled={pending}
      style={[styles.button, { backgroundColor: colors.familia, opacity: pending ? 0.6 : 1 }]}
    >
      <Text style={{ color: colors.surface, fontWeight: '700' }}>{pending ? 'Guardando…' : label}</Text>
    </Pressable>
  );
}

/** Cajas iniciales (saldo + fecha) y piso de caja. */
function CajasSection() {
  const { colors } = useTheme();
  const q = useSettings();
  const save = useUpdateSettings();
  const [boxes, setBoxes] = useState<CashBox[]>([]);
  const [floorText, setFloorText] = useState('');
  // Texto crudo de los saldos, para no reformatear mientras se escribe.
  const [balanceText, setBalanceText] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!q.data) return;
    setBoxes(q.data.cashBoxes);
    setBalanceText(Object.fromEntries(q.data.cashBoxes.map((b) => [b.id, String(b.openingBalance)])));
    setFloorText(String(q.data.cashFloor));
  }, [q.data]);

  const patch = (id: string, p: Partial<CashBox>) =>
    setBoxes((bs) => bs.map((b) => (b.id === id ? { ...b, ...p } : b)));

  const valid = boxes.every((b) => isValidISODate(b.openingDate));

  return (
    <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
      <Card title="Cajas iniciales">
        {boxes.map((b) => (
          <View key={b.id} style={[styles.box, { borderLeftColor: scopeColor(colors, b.scope) }]}>
            <Text style={{ color: colors.ink, fontWeight: '600' }}>{b.name}</Text>
            <View style={styles.row}>
              <View style={{ flex: 1 }}>
                <Field label="Saldo">
                  <Input
                    value={balanceText[b.id] ?? ''}
                    onChangeText={(t) => {
                      setBalanceText((m) => ({ ...m, [b.id]: t }));
                      patch(b.id, { openingBalance: toNumber(t) });
                    }}
                    keyboardType="numbers-and-punctuation"
                  />
                </Field>
              </View>
              <View style={{ flex: 1 }}>
                <Field label="Fecha" error={isValidISODate(b.openingDate) ? undefined : 'Inválida'}>
                  <DateInput value={b.openingDate} onChangeText={(t) => patch(b.id, { openingDate: t })} />
                </Field>
              </View>
            </View>
          </View>
        ))}
      </Card>
      <Card title="Piso de caja">
        <Field label="Monto mínimo a mantener" hint="La caja libre se calcula por encima de este piso.">
          <Input value={floorText} onChangeText={setFloorText} keyboardType="number-pad" />
        </Field>
      </Card>
      {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
      <SaveButton
        pending={save.isPending}
        onPress={() => valid && save.mutate({ cashBoxes: boxes, cashFloor: toNumber(floorText) })}
        label="Guardar cajas y piso"
      />
    </QueryState>
  );
}

/** Cierres y vencimientos por tarjeta, mes a mes. */
function CiclosSection() {
  const { colors } = useTheme();
  const methods = usePaymentMethods();
  const cards = (methods.data ?? []).filter((m) => m.type === 'tarjeta');
  const [cardId, setCardId] = useState<string>();
  const selected = cardId ?? cards[0]?.id;
  const q = useCardCycles(selected);
  const save = useUpdateCardCycles(selected ?? '');
  const [cycles, setCycles] = useState<CardCycle[]>([]);

  useEffect(() => setCycles(q.data ?? []), [q.data]);

  const patch = (month: string, p: Partial<CardCycle>) =>
    setCycles((cs) => cs.map((c) => (c.month === month ? { ...c, ...p } : c)));

  const addMonth = () => {
    if (!selected) return;
    const last = cycles[cycles.length - 1]?.month ?? addMonths(monthOf(todayISO()), -1);
    // Fechas vacías: el usuario las completa (no se infieren en el cliente).
    setCycles((cs) => [...cs, { cardId: selected, month: addMonths(last, 1), closingDate: '', dueDate: '' }]);
  };

  const valid = cycles.every((c) => isValidISODate(c.closingDate) && isValidISODate(c.dueDate));

  return (
    <Card title="Cierres y vencimientos">
      <QueryState
        isLoading={methods.isLoading}
        error={methods.error}
        isEmpty={cards.length === 0}
        emptyText="No hay tarjetas."
      >
        <ChipPicker
          items={cards}
          getKey={(c) => c.id}
          getLabel={(c) => c.name}
          selectedKey={selected}
          onSelect={(c) => setCardId(c.id)}
        />
        <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
          {cycles.map((c) => (
            <View key={c.month} style={styles.row}>
              <Text style={[styles.month, { color: colors.ink }]}>{c.month}</Text>
              <View style={{ flex: 1 }}>
                <DateInput
                  value={c.closingDate}
                  placeholder="Cierre"
                  onChangeText={(t) => patch(c.month, { closingDate: t })}
                />
              </View>
              <View style={{ flex: 1 }}>
                <DateInput
                  value={c.dueDate}
                  placeholder="Vence"
                  onChangeText={(t) => patch(c.month, { dueDate: t })}
                />
              </View>
            </View>
          ))}
          <Pressable onPress={addMonth} hitSlop={8}>
            <Text style={{ color: colors.familia, fontWeight: '600' }}>+ Agregar mes</Text>
          </Pressable>
          {!valid && <Text style={{ color: colors.warn, fontSize: 12 }}>Completá todas las fechas (AAAA-MM-DD).</Text>}
          {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
          <SaveButton pending={save.isPending} onPress={() => valid && selected && save.mutate(cycles)} />
        </QueryState>
      </QueryState>
    </Card>
  );
}

export function ConfiguracionScreen() {
  return (
    <Screen>
      <CajasSection />
      <CiclosSection />
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  box: { borderLeftWidth: 3, paddingLeft: spacing.md, gap: spacing.xs, marginBottom: spacing.sm },
  month: { width: 70, fontVariant: ['tabular-nums'] },
  button: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
});
