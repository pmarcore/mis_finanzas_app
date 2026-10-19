import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Alert } from '../utils/alert';
import {
  useCategories,
  useCreateRecurringRule,
  useDeleteRecurringRule,
  useOperationTypes,
  usePaymentMethods,
  usePeople,
  useRecurringRules,
  useUpdateRecurringRule,
} from '../api/endpoints';
import type { Id, Month, NewRecurringRule, RecurringRule, RecurringRuleSaveResult, Scope } from '../api/types';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { Field, Input } from '../components/Field';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { Select } from '../components/Select';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import {
  addMonths,
  amountToInput,
  formatMonth,
  isValidMonth,
  monthOf,
  parseAmount,
  parsePercent,
  todayISO,
} from '../utils/format';

interface AmountDraft {
  fromMonth: Month;
  /** Monto en pesos, o porcentaje si isIncrease. */
  valueText: string;
  isIncrease: boolean;
}

interface AmountChange {
  fromMonth: Month;
  amount: number;
  increasePercent: number | null;
}

/** Igual que el backend: hasta fin de año y como mínimo tres meses adelante. */
function horizon(): Month {
  const today = monthOf(todayISO());
  const endOfYear = `${today.slice(0, 4)}-12`;
  const threeAhead = addMonths(today, 3);
  return endOfYear > threeAhead ? endOfYear : threeAhead;
}

/** Monto de cada mes de la vigencia: un monto reemplaza al anterior y los incrementos % se acumulan. */
function projectAmounts(startMonth: Month, endMonth: Month | null, changes: AmountChange[]) {
  const sorted = [...changes].sort((a, b) => a.fromMonth.localeCompare(b.fromMonth));
  const until = endMonth && endMonth < horizon() ? endMonth : horizon();
  const months: { month: Month; amount: number }[] = [];
  for (let m = startMonth; m <= until && months.length < 36; m = addMonths(m, 1)) {
    let amount = 0;
    for (const c of sorted)
      if (c.fromMonth <= m)
        amount = c.increasePercent !== null ? Math.round(amount * (1 + c.increasePercent / 100) * 100) / 100 : c.amount;
    months.push({ month: m, amount });
  }
  return months;
}

/** Agrupa meses consecutivos con el mismo monto: "nov 2026 – dic 2026 · $ 110.000". */
function segments(months: { month: Month; amount: number }[]) {
  const out: { from: Month; to: Month; amount: number }[] = [];
  for (const { month, amount } of months) {
    const last = out[out.length - 1];
    if (last && last.amount === amount) last.to = month;
    else out.push({ from: month, to: month, amount });
  }
  return out;
}

/** Mes propuesto para un cambio nuevo: el siguiente al último cargado. */
function nextChangeMonth(as: AmountDraft[]): Month {
  const last = as.map((a) => a.fromMonth).filter(isValidMonth).sort().pop();
  return last ? addMonths(last, 1) : monthOf(todayISO());
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
  const operationTypes = useOperationTypes();
  const create = useCreateRecurringRule();
  const update = useUpdateRecurringRule();
  const save = initial ? update : create;

  const [description, setDescription] = useState(initial?.description ?? '');
  const [scope, setScope] = useState<Scope>(initial?.scope ?? defaultScope);
  const [personId, setPersonId] = useState<Id | undefined>(initial?.personId);
  const [operationTypeId, setOperationTypeId] = useState<Id | undefined>(initial?.operationTypeId);
  const [categoryId, setCategoryId] = useState<Id | undefined>(initial?.categoryId);
  const [paymentMethodId, setPaymentMethodId] = useState<Id | undefined>(initial?.paymentMethodId);
  const [dayText, setDayText] = useState(String(initial?.dayOfMonth ?? 1));
  const [startMonth, setStartMonth] = useState<Month>(initial?.startMonth ?? monthOf(todayISO()));
  const [endMonth, setEndMonth] = useState<string>(initial?.endMonth ?? '');
  const [amounts, setAmounts] = useState<AmountDraft[]>(
    initial?.amounts.map((a) =>
      a.increasePercent != null
        ? { fromMonth: a.fromMonth, valueText: amountToInput(a.increasePercent), isIncrease: true }
        : { fromMonth: a.fromMonth, valueText: amountToInput(a.amount), isIncrease: false },
    ) ?? [{ fromMonth: monthOf(todayISO()), valueText: '', isIncrease: false }],
  );
  const [submitted, setSubmitted] = useState(false);

  // Pago de tarjeta necesita tarjeta y vencimiento puntual: no se modela como fijo.
  const operations = useMemo(
    () =>
      (operationTypes.data ?? []).filter(
        (t) => t.kind !== 'pagoTarjeta' && ((t.active && t.scopes.includes(scope)) || t.id === initial?.operationTypeId),
      ),
    [operationTypes.data, scope, initial?.operationTypeId],
  );
  const type = operations.find((t) => t.id === operationTypeId);
  useEffect(() => {
    if (!type && operations.length > 0)
      setOperationTypeId((operations.find((t) => t.kind === 'movimiento' && t.effect === 'resta') ?? operations[0]).id);
  }, [type, operations]);
  const kind = type?.effect === 'suma' ? 'ingreso' : 'gasto';
  const visibleCategories = useMemo(
    () =>
      (categories.data ?? []).filter(
        (c) => c.scope === scope && c.kind === kind && (c.active || c.id === initial?.categoryId),
      ),
    [categories.data, scope, kind, initial?.categoryId],
  );
  // Con tarjeta solo ingresos y gastos comunes; ahorro, inversión y transferencias salen de una caja.
  const visibleMethods = (methods.data ?? []).filter(
    (m) => (m.active || m.id === initial?.paymentMethodId) && (type?.kind === 'movimiento' || !m.isCard),
  );
  const accent = scopeColor(colors, scope);

  const day = Number.parseInt(dayText, 10);
  const parsedAmounts = amounts.map((a) => ({
    fromMonth: a.fromMonth,
    isIncrease: a.isIncrease,
    value: a.isIncrease ? parsePercent(a.valueText) : parseAmount(a.valueText),
  }));
  const firstChange = [...parsedAmounts].sort((a, b) => a.fromMonth.localeCompare(b.fromMonth))[0];
  const changes: AmountChange[] = parsedAmounts.map((a) => ({
    fromMonth: a.fromMonth,
    amount: a.isIncrease ? 0 : a.value ?? 0,
    increasePercent: a.isIncrease ? a.value ?? 0 : null,
  }));
  const errors = {
    description: !description.trim() ? 'Poné una descripción.' : undefined,
    type: type === undefined ? 'Elegí un tipo de operación.' : undefined,
    person: personId === undefined ? 'Elegí una persona.' : undefined,
    category:
      categoryId === undefined || !visibleCategories.some((c) => c.id === categoryId)
        ? 'Elegí una categoría.'
        : undefined,
    paymentMethod:
      paymentMethodId === undefined || !visibleMethods.some((m) => m.id === paymentMethodId)
        ? 'Elegí un medio de pago.'
        : undefined,
    day: !(day >= 1 && day <= 31) ? 'Día entre 1 y 31.' : undefined,
    startMonth: !isValidMonth(startMonth) ? 'Mes inválido (AAAA-MM).' : undefined,
    endMonth:
      endMonth && (!isValidMonth(endMonth) || endMonth < startMonth) ? 'Mes inválido (AAAA-MM, desde el inicio).' : undefined,
    amounts:
      parsedAmounts.length === 0 ||
      parsedAmounts.some(
        (a) => !isValidMonth(a.fromMonth) || a.value === null || (a.isIncrease ? a.value <= -100 : a.value <= 0),
      )
        ? 'Cada cambio necesita mes (AAAA-MM) y un monto mayor a cero o un incremento mayor a -100 %.'
        : new Set(parsedAmounts.map((a) => a.fromMonth)).size !== parsedAmounts.length
          ? 'Hay dos cambios en el mismo mes.'
          : firstChange?.isIncrease
            ? 'El primer cambio tiene que ser un monto; los incrementos se aplican sobre él.'
            : isValidMonth(startMonth) && firstChange && firstChange.fromMonth > startMonth
              ? `El primer monto tiene que regir desde el mes de inicio (${startMonth}); si no, esos meses quedan en $0.`
              : endMonth && parsedAmounts.some((a) => a.fromMonth > endMonth)
                ? 'Hay un cambio de monto posterior al mes hasta: nunca se aplicaría.'
                : undefined,
  };
  const hasErrors = Object.values(errors).some(Boolean);
  const show = (e?: string) => (submitted ? e : undefined);

  const onSubmit = () => {
    setSubmitted(true);
    if (hasErrors || !type || personId === undefined || categoryId === undefined || paymentMethodId === undefined) return;
    const body: NewRecurringRule = {
      scope,
      personId,
      operationTypeId: type.id,
      description: description.trim(),
      categoryId,
      paymentMethodId,
      dayOfMonth: day,
      startMonth,
      endMonth: endMonth || null,
      amounts: [...changes].sort((a, b) => a.fromMonth.localeCompare(b.fromMonth)),
    };
    if (initial) update.mutate({ ...body, id: initial.id }, { onSuccess: onSaved });
    else create.mutate(body, { onSuccess: onSaved });
  };

  const projected =
    errors.startMonth || errors.endMonth || errors.amounts ? [] : projectAmounts(startMonth, endMonth || null, changes);
  const preview = segments(projected);

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
      <Field label="Tipo de movimiento" error={show(errors.type)}>
        <Select
          title="Tipo de movimiento"
          placeholder="Elegí un tipo"
          items={operations}
          getKey={(t) => t.id}
          getLabel={(t) => t.name}
          selectedKey={operationTypeId}
          onSelect={(t) => setOperationTypeId(t.id)}
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
        <Select
          title="Categoría"
          placeholder="Elegí una categoría"
          items={visibleCategories}
          getKey={(c) => c.id}
          getLabel={(c) => c.name}
          selectedKey={categoryId}
          onSelect={(c) => setCategoryId(c.id)}
          accent={accent}
        />
      </Field>
      <Field label="Medio de pago" error={show(errors.paymentMethod)}>
        <Select
          title="Medio de pago"
          placeholder="Elegí un medio de pago"
          items={visibleMethods}
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
      <Field label="Monto e incrementos (desde mes)" error={show(errors.amounts)}>
        {amounts.map((a, i) => (
          <View key={i} style={styles.row}>
            <Input
              value={a.fromMonth}
              onChangeText={(t) => patchAmount(i, { fromMonth: t })}
              placeholder="AAAA-MM"
              maxLength={7}
              style={{ width: 100 }}
            />
            <Pressable
              onPress={() => patchAmount(i, { isIncrease: !a.isIncrease, valueText: '' })}
              hitSlop={6}
              accessibilityLabel={a.isIncrease ? 'Cambiar a monto' : 'Cambiar a incremento %'}
              style={[styles.kind, { borderColor: accent, backgroundColor: a.isIncrease ? accent : 'transparent' }]}
            >
              <Text style={{ color: a.isIncrease ? colors.surface : accent, fontWeight: '700' }}>
                {a.isIncrease ? '+%' : '$'}
              </Text>
            </Pressable>
            <Input
              value={a.valueText}
              onChangeText={(t) => patchAmount(i, { valueText: t })}
              placeholder={a.isIncrease ? 'Ej.: 10' : '$ 0'}
              keyboardType={a.isIncrease ? 'numbers-and-punctuation' : 'decimal-pad'}
              style={{ flex: 1, minWidth: 0 }}
            />
            {amounts.length > 1 && (
              <Pressable onPress={() => setAmounts((as) => as.filter((_, k) => k !== i))} hitSlop={8}>
                <Text style={{ color: colors.bad, fontWeight: '700' }}>✕</Text>
              </Pressable>
            )}
          </View>
        ))}
        <View style={[styles.row, { justifyContent: 'space-between' }]}>
          <Pressable
            onPress={() => setAmounts((as) => [...as, { fromMonth: nextChangeMonth(as), valueText: '', isIncrease: false }])}
            hitSlop={8}
          >
            <Text style={{ color: accent, fontWeight: '600' }}>+ Cambio de monto</Text>
          </Pressable>
          <Pressable
            onPress={() => setAmounts((as) => [...as, { fromMonth: nextChangeMonth(as), valueText: '', isIncrease: true }])}
            hitSlop={8}
          >
            <Text style={{ color: accent, fontWeight: '600' }}>+ Incremento %</Text>
          </Pressable>
        </View>
        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
          Los incrementos se acumulan sobre el monto vigente. Sin mes hasta, se proyecta hasta {formatMonth(horizon())}.
        </Text>
      </Field>
      {preview.length > 0 && (
        <Field label={`Proyección hasta ${formatMonth(preview[preview.length - 1].to)}`}>
          {preview.map((p) => (
            <View key={p.from} style={styles.line}>
              <Text style={{ color: colors.ink }}>
                {p.from === p.to ? formatMonth(p.from) : `${formatMonth(p.from)} – ${formatMonth(p.to)}`}
              </Text>
              <MoneyText value={p.amount} size="sm" />
            </View>
          ))}
        </Field>
      )}
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
  const types = useOperationTypes();
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
          defaultScope={scope}
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
                {types.data?.find((t) => t.id === r.operationTypeId)?.name ?? ''} · {categoryName(r.categoryId)}
                {methodName(r.paymentMethodId) ? ` · ${methodName(r.paymentMethodId)}` : ''} · desde{' '}
                {formatMonth(r.startMonth)}
                {r.endMonth ? ` hasta ${formatMonth(r.endMonth)}` : ''}
              </Text>
              {r.amounts.map((a) => (
                <View key={a.fromMonth} style={styles.line}>
                  <Text style={{ color: colors.ink }}>
                    desde {formatMonth(a.fromMonth)}
                    {a.increasePercent != null ? ` · ${a.increasePercent > 0 ? '+' : ''}${amountToInput(a.increasePercent)} %` : ''}
                  </Text>
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
  kind: { borderWidth: 1, borderRadius: radius.sm, paddingHorizontal: spacing.sm, paddingVertical: spacing.xs, minWidth: 36, alignItems: 'center' },
  newButton: { borderWidth: 1, borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
});
