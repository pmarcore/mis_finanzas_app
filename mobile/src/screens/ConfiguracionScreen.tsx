import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import {
  useCardCycles,
  usePaymentMethods,
  useSettings,
  useUpdateCardCycles,
  useUpdateCashBox,
  useUpdateSettings,
} from '../api/endpoints';
import type { CardCycle, CashBox, Id, Thresholds } from '../api/types';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { DateInput, Field, Input } from '../components/Field';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { addMonths, amountToInput, isValidISODate, monthOf, parseAmount, todayISO } from '../utils/format';

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

/** Borrador de una caja: montos como texto para no reformatear mientras se escribe. */
interface BoxDraft {
  box: CashBox;
  openingText: string;
  observedText: string;
  observedDate: string;
}

/** Cajas iniciales (saldo + fecha) y saldo observado. PUT /settings/cash-boxes/{id} por caja. */
function CajasSection() {
  const { colors } = useTheme();
  const q = useSettings();
  const save = useUpdateCashBox();
  const [drafts, setDrafts] = useState<BoxDraft[]>([]);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (!q.data) return;
    setDrafts(
      q.data.cashBoxes.map((b) => ({
        box: b,
        openingText: amountToInput(b.openingBalance),
        observedText: amountToInput(b.observedBalance),
        observedDate: b.observedDate ?? '',
      })),
    );
  }, [q.data]);

  const patch = (id: Id, p: Partial<Omit<BoxDraft, 'box'>> & { box?: Partial<CashBox> }) =>
    setDrafts((ds) => ds.map((d) => (d.box.id === id ? { ...d, ...p, box: { ...d.box, ...p.box } } : d)));

  const errorsOf = (d: BoxDraft) => ({
    opening: parseAmount(d.openingText) === null ? 'Monto inválido' : undefined,
    openingDate: isValidISODate(d.box.openingDate) ? undefined : 'Inválida',
    observed: d.observedText && parseAmount(d.observedText) === null ? 'Monto inválido' : undefined,
    observedDate:
      (d.observedText && !isValidISODate(d.observedDate)) || (d.observedDate && !isValidISODate(d.observedDate))
        ? 'Inválida'
        : undefined,
  });
  const valid = drafts.every((d) => !Object.values(errorsOf(d)).some(Boolean));

  const onSave = async () => {
    if (!valid) return;
    setSaving(true);
    setSaved(false);
    try {
      for (const d of drafts) {
        const observed = d.observedText ? parseAmount(d.observedText) : null;
        await save.mutateAsync({
          ...d.box,
          openingBalance: parseAmount(d.openingText) ?? 0,
          observedBalance: observed,
          observedDate: observed !== null && d.observedDate ? d.observedDate : null,
        });
      }
      setSaved(true);
    } catch {
      // El error queda en save.error.
    } finally {
      setSaving(false);
    }
  };

  return (
    <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
      <Card title="Cajas">
        {drafts.map((d) => {
          const e = errorsOf(d);
          return (
            <View key={d.box.id} style={[styles.box, { borderLeftColor: scopeColor(colors, d.box.scope) }]}>
              <Text style={{ color: colors.ink, fontWeight: '600' }}>{d.box.name}</Text>
              <View style={styles.row}>
                <View style={{ flex: 1 }}>
                  <Field label="Saldo inicial" error={e.opening}>
                    <Input
                      value={d.openingText}
                      onChangeText={(t) => patch(d.box.id, { openingText: t })}
                      keyboardType="numbers-and-punctuation"
                    />
                  </Field>
                </View>
                <View style={{ flex: 1 }}>
                  <Field label="Fecha" error={e.openingDate}>
                    <DateInput
                      value={d.box.openingDate}
                      onChangeText={(t) => patch(d.box.id, { box: { openingDate: t } })}
                    />
                  </Field>
                </View>
              </View>
              <View style={styles.row}>
                <View style={{ flex: 1 }}>
                  <Field label="Saldo observado" error={e.observed}>
                    <Input
                      value={d.observedText}
                      onChangeText={(t) => patch(d.box.id, { observedText: t })}
                      placeholder="(opcional)"
                      keyboardType="numbers-and-punctuation"
                    />
                  </Field>
                </View>
                <View style={{ flex: 1 }}>
                  <Field label="Fecha observada" error={e.observedDate}>
                    <DateInput value={d.observedDate} onChangeText={(t) => patch(d.box.id, { observedDate: t })} />
                  </Field>
                </View>
              </View>
            </View>
          );
        })}
      </Card>
      {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
      {saved && <Text style={{ color: colors.good }}>Cajas guardadas.</Text>}
      <SaveButton pending={saving} onPress={onSave} label="Guardar cajas" />
    </QueryState>
  );
}

const THRESHOLD_FIELDS: { key: keyof Omit<Thresholds, 'cashFloor' | 'savingsGoal'>; label: string }[] = [
  { key: 'alertAttentionPct', label: 'Preventiva (%)' },
  { key: 'alertControlPct', label: 'Control (%)' },
  { key: 'alertExceededPct', label: 'Excedido (%)' },
  { key: 'alertCriticalPct', label: 'Crítico (%)' },
];

/** Piso de caja, meta de ahorro y umbrales de alerta del presupuesto. PUT /settings. */
function UmbralesSection() {
  const { colors } = useTheme();
  const q = useSettings();
  const save = useUpdateSettings();
  const [floorText, setFloorText] = useState('');
  const [goalText, setGoalText] = useState('');
  const [pctText, setPctText] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!q.data) return;
    setFloorText(amountToInput(q.data.cashFloor));
    setGoalText(amountToInput(q.data.savingsGoal));
    setPctText(Object.fromEntries(THRESHOLD_FIELDS.map((f) => [f.key, String(q.data[f.key])])));
  }, [q.data]);

  const pct = (k: string) => {
    const n = Number.parseInt(pctText[k] ?? '', 10);
    return Number.isFinite(n) && n >= 0 ? n : null;
  };
  const pcts = THRESHOLD_FIELDS.map((f) => pct(f.key));
  const floor = parseAmount(floorText);
  const goal = parseAmount(goalText);
  const ascending = pcts.every((p, i) => p !== null && (i === 0 || p > (pcts[i - 1] ?? 0)));
  const errors = {
    floor: floor === null ? 'Monto inválido.' : undefined,
    goal: goal === null ? 'Monto inválido.' : undefined,
    pcts: !ascending ? 'Los umbrales tienen que ser números crecientes.' : undefined,
  };
  const valid = !Object.values(errors).some(Boolean);

  const onSave = () => {
    if (!valid || floor === null || goal === null) return;
    const [alertAttentionPct, alertControlPct, alertExceededPct, alertCriticalPct] = pcts as number[];
    save.mutate({ cashFloor: floor, savingsGoal: goal, alertAttentionPct, alertControlPct, alertExceededPct, alertCriticalPct });
  };

  return (
    <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
      <Card title="Piso de caja y alertas">
        <Field label="Piso de caja" error={errors.floor} hint="La caja libre se calcula por encima de este piso.">
          <Input value={floorText} onChangeText={setFloorText} keyboardType="decimal-pad" />
        </Field>
        <Field label="Meta de ahorro mensual" error={errors.goal}>
          <Input value={goalText} onChangeText={setGoalText} keyboardType="decimal-pad" />
        </Field>
        <Field
          label="Umbrales de uso del presupuesto"
          error={errors.pcts}
          hint="Porcentaje usado a partir del cual cada categoría cambia de alerta."
        >
          <View style={styles.wrap}>
            {THRESHOLD_FIELDS.map((f) => (
              <View key={f.key} style={styles.pct}>
                <Text style={{ color: colors.inkMuted, fontSize: 12 }}>{f.label}</Text>
                <Input
                  value={pctText[f.key] ?? ''}
                  onChangeText={(t) => setPctText((m) => ({ ...m, [f.key]: t }))}
                  keyboardType="number-pad"
                  maxLength={3}
                />
              </View>
            ))}
          </View>
        </Field>
      </Card>
      {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
      {save.isSuccess && <Text style={{ color: colors.good }}>Configuración guardada.</Text>}
      <SaveButton pending={save.isPending} onPress={onSave} label="Guardar piso y alertas" />
    </QueryState>
  );
}

/** Cierres y vencimientos por tarjeta, mes a mes. */
function CiclosSection() {
  const { colors } = useTheme();
  const methods = usePaymentMethods();
  const cards = (methods.data ?? []).filter((m) => m.isCard);
  const [cardId, setCardId] = useState<Id>();
  const selected = cardId ?? cards[0]?.id;
  const q = useCardCycles(selected);
  const save = useUpdateCardCycles(selected);
  const [cycles, setCycles] = useState<CardCycle[]>([]);

  useEffect(() => setCycles(q.data ?? []), [q.data]);

  const patch = (month: string, p: Partial<CardCycle>) =>
    setCycles((cs) => cs.map((c) => (c.month === month ? { ...c, ...p } : c)));

  const addMonth = () => {
    if (selected === undefined) return;
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
          <SaveButton pending={save.isPending} onPress={() => valid && selected !== undefined && save.mutate(cycles)} />
        </QueryState>
      </QueryState>
    </Card>
  );
}

export function ConfiguracionScreen() {
  return (
    <Screen>
      <UmbralesSection />
      <CajasSection />
      <CiclosSection />
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  box: { borderLeftWidth: 3, paddingLeft: spacing.md, gap: spacing.xs, marginBottom: spacing.sm },
  month: { width: 70, fontVariant: ['tabular-nums'] },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  pct: { width: '47%', gap: spacing.xs },
  button: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
});
