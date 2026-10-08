import { useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, Text, View } from 'react-native';
import {
  useCategories,
  useCreateRecurringRule,
  useDeleteRecurringRule,
  usePaymentMethods,
  usePeople,
  useRecurringRules,
  useUpdateRecurringRule,
} from '../api/endpoints';
import {
  OPERATION_LABELS,
  OPERATIONS,
  type Id,
  type Month,
  type NewRecurringRule,
  type Operation,
  type RecurringRule,
  type RecurringRuleSaveResult,
  type Scope,
} from '../api/types';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { Field, Input } from '../components/Field';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { amountToInput, formatMonth, isValidMonth, monthOf, parseAmount, todayISO } from '../utils/format';

interface AmountDraft {
  fromMonth: Month;
  amountText: string;
}

/** Alta/edición de un gasto fijo (regla recurrente). */
function RuleForm({
  initial,
  defaultScope,
  onSaved,
  onCancel,
}: {
  initial?: RecurringRule;
  defaultScope: Scope;
  onSaved: (r: RecurringRuleSaveResult) => void;
  onCancel: () => void;
}) {
  const { colors } = useTheme();
  const people = usePeople();
  const categories = useCategories();
  const methods = usePaymentMethods();
  const create = useCreateRecurringRule();
  const update = useUpdateRecurringRule();
  const save = initial ? update : create;

  const [description, setDescription] = useState(initial?.description ?? '');
  const [scope, setScope] = useState<Scope>(initial?.scope ?? defaultScope);
  const [personId, setPersonId] = useState<Id | undefined>(initial?.personId);
  const [operation, setOperation] = useState<Operation>(initial?.operation ?? 'gasto');
  const [categoryId, setCategoryId] = useState<Id | undefined>(initial?.categoryId);
  const [paymentMethodId, setPaymentMethodId] = useState<Id | undefined>(initial?.paymentMethodId);
  const [dayText, setDayText] = useState(String(initial?.dayOfMonth ?? 1));
  const [startMonth, setStartMonth] = useState<Month>(initial?.startMonth ?? monthOf(todayISO()));
  const [endMonth, setEndMonth] = useState<string>(initial?.endMonth ?? '');
  const [amounts, setAmounts] = useState<AmountDraft[]>(
    initial?.amounts.map((a) => ({ fromMonth: a.fromMonth, amountText: amountToInput(a.amount) })) ?? [
      { fromMonth: monthOf(todayISO()), amountText: '' },
    ],
  );
  const [submitted, setSubmitted] = useState(false);

  const kind = operation === 'ingreso' ? 'ingreso' : 'gasto';
  const visibleCategories = useMemo(
    () => (categories.data ?? []).filter((c) => c.scope === scope && c.kind === kind),
    [categories.data, scope, kind],
  );
  const accent = scopeColor(colors, scope);
  // Pago de tarjeta necesita tarjeta y vencimiento puntual: no se modela como fijo.
  const operations = OPERATIONS.filter((o) => o !== 'pagoTarjeta' || initial?.operation === 'pagoTarjeta');

  const day = Number.parseInt(dayText, 10);
  const parsedAmounts = amounts.map((a) => ({ fromMonth: a.fromMonth, amount: parseAmount(a.amountText) }));
  const errors = {
    description: !description.trim() ? 'Poné una descripción.' : undefined,
    person: personId === undefined ? 'Elegí una persona.' : undefined,
    category:
      categoryId === undefined || !visibleCategories.some((c) => c.id === categoryId)
        ? 'Elegí una categoría.'
        : undefined,
    paymentMethod: paymentMethodId === undefined ? 'Elegí un medio de pago.' : undefined,
    day: !(day >= 1 && day <= 31) ? 'Día entre 1 y 31.' : undefined,
    startMonth: !isValidMonth(startMonth) ? 'Mes inválido (AAAA-MM).' : undefined,
    endMonth:
      endMonth && (!isValidMonth(endMonth) || endMonth < startMonth) ? 'Mes inválido (AAAA-MM, desde el inicio).' : undefined,
    amounts:
      parsedAmounts.length === 0 || parsedAmounts.some((a) => !isValidMonth(a.fromMonth) || !(a.amount !== null && a.amount > 0))
        ? 'Cada monto necesita mes (AAAA-MM) y un importe mayor a cero.'
        : undefined,
  };
  const hasErrors = Object.values(errors).some(Boolean);
  const show = (e?: string) => (submitted ? e : undefined);

  const onSubmit = () => {
    setSubmitted(true);
    if (hasErrors || personId === undefined || categoryId === undefined || paymentMethodId === undefined) return;
    const body: NewRecurringRule = {
      scope,
      personId,
      operation,
      description: description.trim(),
      categoryId,
      paymentMethodId,
      dayOfMonth: day,
      startMonth,
      endMonth: endMonth || null,
      amounts: parsedAmounts
        .map((a) => ({ fromMonth: a.fromMonth, amount: a.amount ?? 0 }))
        .sort((a, b) => a.fromMonth.localeCompare(b.fromMonth)),
    };
    if (initial) update.mutate({ ...body, id: initial.id }, { onSuccess: onSaved });
    else create.mutate(body, { onSuccess: onSaved });
  };

  const patchAmount = (i: number, p: Partial<AmountDraft>) =>
    setAmounts((as) => as.map((a, k) => (k === i ? { ...a, ...p } : a)));

  return (
    <Card title={initial ? 'Editar fijo' : 'Nuevo fijo'} accent={accent}>
      <Field label="Descripción" error={show(errors.description)}>
        <Input value={description} onChangeText={setDescription} placeholder="Ej.: alquiler" />
      </Field>
      <Field label="Destino">
        <Segmented<Scope>
          value={scope}
          onChange={setScope}
          options={[
            { value: 'familia', label: 'Familia', color: colors.familia },
            { value: 'memey', label: 'Memey', color: colors.memey },
          ]}
        />
      </Field>
      <Field label="Operación">
        <ChipPicker
          items={operations}
          getKey={(o) => o}
          getLabel={(o) => OPERATION_LABELS[o]}
          selectedKey={operation}
          onSelect={setOperation}
          accent={accent}
        />
      </Field>
      <Field label="Persona" error={show(errors.person)}>
        <ChipPicker
          items={people.data ?? []}
          getKey={(p) => p.id}
          getLabel={(p) => p.name}
          selectedKey={personId}
          onSelect={(p) => setPersonId(p.id)}
          accent={accent}
        />
      </Field>
      <Field label="Categoría" error={show(errors.category)}>
        <ChipPicker
          items={visibleCategories}
          getKey={(c) => c.id}
          getLabel={(c) => c.name}
          selectedKey={categoryId}
          onSelect={(c) => setCategoryId(c.id)}
          accent={accent}
        />
      </Field>
      <Field label="Medio de pago" error={show(errors.paymentMethod)}>
        <ChipPicker
          items={methods.data ?? []}
          getKey={(m) => m.id}
          getLabel={(m) => m.name}
          selectedKey={paymentMethodId}
          onSelect={(m) => setPaymentMethodId(m.id)}
          accent={accent}
        />
      </Field>
      <View style={styles.row}>
        <View style={{ flex: 1 }}>
          <Field label="Día del mes" error={show(errors.day)}>
            <Input value={dayText} onChangeText={setDayText} keyboardType="number-pad" maxLength={2} />
          </Field>
        </View>
        <View style={{ flex: 1 }}>
          <Field label="Desde" error={show(errors.startMonth)}>
            <Input value={startMonth} onChangeText={setStartMonth} placeholder="AAAA-MM" maxLength={7} />
          </Field>
        </View>
        <View style={{ flex: 1 }}>
          <Field label="Hasta" error={show(errors.endMonth)}>
            <Input value={endMonth} onChangeText={setEndMonth} placeholder="(sin fin)" maxLength={7} />
          </Field>
        </View>
      </View>
      <Field label="Montos (desde mes)" error={show(errors.amounts)}>
        {amounts.map((a, i) => (
          <View key={i} style={styles.row}>
            <Input
              value={a.fromMonth}
              onChangeText={(t) => patchAmount(i, { fromMonth: t })}
              placeholder="AAAA-MM"
              maxLength={7}
              style={{ width: 100 }}
            />
            <Input
              value={a.amountText}
              onChangeText={(t) => patchAmount(i, { amountText: t })}
              placeholder="$ 0"
              keyboardType="decimal-pad"
              style={{ flex: 1 }}
            />
            {amounts.length > 1 && (
              <Pressable onPress={() => setAmounts((as) => as.filter((_, k) => k !== i))} hitSlop={8}>
                <Text style={{ color: colors.bad, fontWeight: '700' }}>✕</Text>
              </Pressable>
            )}
          </View>
        ))}
        <Pressable
          onPress={() =>
            setAmounts((as) => [...as, { fromMonth: monthOf(todayISO()), amountText: '' }])
          }
          hitSlop={8}
        >
          <Text style={{ color: accent, fontWeight: '600' }}>+ Agregar cambio de monto</Text>
        </Pressable>
      </Field>
      {save.error && <Text style={{ color: colors.bad }}>No se pudo guardar: {save.error.message}</Text>}
      <View style={styles.row}>
        <Pressable onPress={onCancel} style={[styles.button, { borderColor: colors.border, borderWidth: 1 }]}>
          <Text style={{ color: colors.ink, fontWeight: '600' }}>Cancelar</Text>
        </Pressable>
        <Pressable
          onPress={onSubmit}
          disabled={save.isPending}
          style={[styles.button, { backgroundColor: accent, opacity: save.isPending ? 0.6 : 1 }]}
        >
          <Text style={{ color: colors.surface, fontWeight: '700' }}>{save.isPending ? 'Guardando…' : 'Guardar'}</Text>
        </Pressable>
      </View>
    </Card>
  );
}

export function FijosScreen() {
  const { scope } = useScope();
  const { colors } = useTheme();
  const q = useRecurringRules(scope);
  const categories = useCategories();
  const methods = usePaymentMethods();
  const remove = useDeleteRecurringRule();
  // undefined = sin formulario; null = alta; regla = edición.
  const [editing, setEditing] = useState<RecurringRule | null | undefined>(undefined);
  const [missingCycles, setMissingCycles] = useState<Month[]>([]);

  const categoryName = (id: Id) => categories.data?.find((c) => c.id === id)?.name ?? `#${id}`;
  const methodName = (id: Id) => methods.data?.find((m) => m.id === id)?.name ?? '';

  const askDelete = (r: RecurringRule) =>
    Alert.alert(
      'Borrar fijo',
      `¿Borrar "${r.description || categoryName(r.categoryId)}"? Se borran los previstos; los realizados quedan como movimientos sueltos.`,
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Borrar',
          style: 'destructive',
          onPress: () =>
            remove.mutate(r.id, {
              onSuccess: () => setEditing(undefined),
              onError: (e) => Alert.alert('No se pudo borrar', e.message),
            }),
        },
      ],
    );

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <ScopeSelector />

      {missingCycles.length > 0 && (
        <Card title="Faltan cierres de tarjeta" accent={colors.warn}>
          <Text style={{ color: colors.ink }}>
            No hay cierre cargado para {missingCycles.map(formatMonth).join(', ')}. Esos meses no tienen vencimiento
            calculado: cargalos en Configuración.
          </Text>
          <Pressable onPress={() => setMissingCycles([])} hitSlop={8}>
            <Text style={{ color: colors.warn, fontWeight: '600' }}>Entendido</Text>
          </Pressable>
        </Card>
      )}

      {editing === undefined ? (
        <Pressable
          onPress={() => setEditing(null)}
          style={[styles.newButton, { borderColor: scopeColor(colors, scope) }]}
        >
          <Text style={{ color: scopeColor(colors, scope), fontWeight: '700' }}>+ Nuevo fijo</Text>
        </Pressable>
      ) : (
        <RuleForm
          key={editing?.id ?? 'nuevo'}
          initial={editing ?? undefined}
          defaultScope={scope === 'memey' ? 'memey' : 'familia'}
          onCancel={() => setEditing(undefined)}
          onSaved={(res) => {
            setMissingCycles(res.missingCycles);
            setEditing(undefined);
          }}
        />
      )}

      <QueryState
        isLoading={q.isLoading}
        error={q.error}
        onRetry={q.refetch}
        isEmpty={q.data?.length === 0}
        emptyText="No hay movimientos fijos cargados."
      >
        {q.data?.map((r) => (
          <Pressable key={r.id} onPress={() => setEditing(r)} onLongPress={() => askDelete(r)}>
            <Card accent={scopeColor(colors, r.scope)}>
              <View style={styles.line}>
                <Text style={[styles.title, { color: colors.ink, flexShrink: 1 }]}>
                  {r.description || categoryName(r.categoryId)}
                </Text>
                <Text style={{ color: colors.inkMuted }}>día {r.dayOfMonth}</Text>
              </View>
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                {OPERATION_LABELS[r.operation]} · {categoryName(r.categoryId)}
                {methodName(r.paymentMethodId) ? ` · ${methodName(r.paymentMethodId)}` : ''} · desde{' '}
                {formatMonth(r.startMonth)}
                {r.endMonth ? ` hasta ${formatMonth(r.endMonth)}` : ''}
              </Text>
              {r.amounts.map((a) => (
                <View key={a.fromMonth} style={styles.line}>
                  <Text style={{ color: colors.ink }}>desde {formatMonth(a.fromMonth)}</Text>
                  <MoneyText value={a.amount} size="sm" />
                </View>
              ))}
            </Card>
          </Pressable>
        ))}
        {(q.data?.length ?? 0) > 0 && (
          <Text style={{ color: colors.inkMuted, fontSize: 12, textAlign: 'center' }}>
            Tocá un fijo para editarlo; mantenelo presionado para borrarlo.
          </Text>
        )}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  title: { fontSize: 16, fontWeight: '600' },
  button: { flex: 1, borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  newButton: { borderWidth: 1, borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
});
