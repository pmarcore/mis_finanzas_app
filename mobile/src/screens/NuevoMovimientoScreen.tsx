import { useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text } from 'react-native';
import {
  useCardCycles,
  useCardDueDate,
  useCategories,
  useCreateTransaction,
  useDeleteTransaction,
  useOperationTypes,
  usePaymentMethods,
  usePeople,
  useUpdateTransaction,
} from '../api/endpoints';
import {
  SCOPE_LABELS,
  STATUS_LABELS,
  type CategoryKind,
  type Id,
  type ISODate,
  type NewTransaction,
  type OperationType,
  type Scope,
  type Transaction,
  type TransactionStatus,
} from '../api/types';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { DateInput, Field, Input } from '../components/Field';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { Select } from '../components/Select';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { Alert } from '../utils/alert';
import { addMonths, amountToInput, formatDate, isValidISODate, monthOf, parseAmount, todayISO } from '../utils/format';

/** Tipo propuesto al abrir o al cambiar de destino: el primer gasto común ofrecido, si no el primero. */
function defaultType(types: OperationType[]): OperationType | undefined {
  return types.find((t) => t.kind === 'movimiento' && t.effect === 'resta') ?? types[0];
}

export function NuevoMovimientoScreen({ navigation }: TabScreenProps<'Nuevo'>) {
  return <MovimientoForm onSaved={() => navigation.navigate('Movimientos')} />;
}

/**
 * Alta o edición de un movimiento. Con `initial` edita: el backend rehace sus impactos en caja y en los resúmenes,
 * y permite borrarlo (con confirmación).
 */
export function MovimientoForm({
  initial,
  onSaved,
  onDeleted,
}: {
  initial?: Transaction;
  onSaved: () => void;
  onDeleted?: () => void;
}) {
  const { colors } = useTheme();
  const { scope: currentScope } = useScope();

  const people = usePeople();
  const categories = useCategories();
  const paymentMethods = usePaymentMethods();
  const operationTypes = useOperationTypes();
  const create = useCreateTransaction();
  const update = useUpdateTransaction();
  const remove = useDeleteTransaction();
  const save = initial ? update : create;

  const [operationTypeId, setOperationTypeId] = useState<Id | undefined>(initial?.operationTypeId);
  const [status, setStatus] = useState<TransactionStatus>(initial?.status ?? 'realizado');
  const [amountText, setAmountText] = useState(amountToInput(initial?.amount));
  const [description, setDescription] = useState(initial?.description ?? '');
  const [scope, setScope] = useState<Scope>(initial?.scope ?? currentScope);
  const [personId, setPersonId] = useState<Id | undefined>(initial?.personId);
  const [date, setDate] = useState<ISODate>(initial?.date ?? todayISO);
  const [categoryId, setCategoryId] = useState<Id | undefined>(initial?.categoryId ?? undefined);
  const [paymentMethodId, setPaymentMethodId] = useState<Id | undefined>(initial?.paymentMethodId);
  const [installmentsText, setInstallmentsText] = useState(String(initial?.installments ?? 1));
  const [firstDueDate, setFirstDueDate] = useState(initial?.firstDueDate ?? '');
  // Al editar, el vencimiento guardado se respeta hasta que cambie la fecha, el medio o el tipo.
  const [dueEdited, setDueEdited] = useState(!!initial?.firstDueDate);
  const [paidCardId, setPaidCardId] = useState<Id | undefined>(initial?.paidCardId ?? undefined);
  const [submitted, setSubmitted] = useState(false);

  // Tipos configurados para el destino elegido (Configuración → Tipos de operación).
  // Al editar se ofrece también el tipo original aunque se haya desactivado; lo mismo con categoría y medio.
  const offeredTypes = useMemo(
    () =>
      (operationTypes.data ?? []).filter(
        (t) => (t.active || t.id === initial?.operationTypeId) && t.scopes.includes(scope),
      ),
    [operationTypes.data, scope, initial?.operationTypeId],
  );
  const type = offeredTypes.find((t) => t.id === operationTypeId);
  useEffect(() => {
    if (!type && offeredTypes.length > 0) setOperationTypeId(defaultType(offeredTypes)?.id);
  }, [type, offeredTypes]);

  const isCardPayment = type?.kind === 'pagoTarjeta';
  // Solo los ingresos y gastos comunes llevan categoría y se pueden pagar con tarjeta.
  const isCommon = type?.kind === 'movimiento';
  const kind: CategoryKind = type?.effect === 'suma' ? 'ingreso' : 'gasto';
  const visibleCategories = useMemo(
    () =>
      isCommon
        ? (categories.data ?? []).filter(
            (c) => (c.active || c.id === initial?.categoryId) && c.kind === kind && c.scope === scope,
          )
        : [],
    [categories.data, kind, scope, isCommon, initial?.categoryId],
  );
  const allMethods = paymentMethods.data ?? [];
  const cards = allMethods.filter((m) => m.isCard);
  // Pagos de tarjeta, transferencias, ahorro e inversión salen de una caja: no se pagan con tarjeta.
  const visibleMethods = allMethods.filter(
    (m) => (m.active || m.id === initial?.paymentMethodId) && (isCommon || !m.isCard),
  );
  const method = allMethods.find((m) => m.id === paymentMethodId);
  const isCardPurchase = !isCardPayment && !!method?.isCard;

  // Compra con tarjeta: primer vencimiento sugerido por el backend (404 si falta el cierre).
  const due = useCardDueDate(isCardPurchase ? method?.id : undefined, isValidISODate(date) ? date : undefined);
  // Pago de tarjeta: vencimientos cargados de la tarjeta pagada, para elegir rápido.
  const paidCycles = useCardCycles(isCardPayment ? paidCardId : undefined);
  const dueOptions = useMemo(
    () => (paidCycles.data ?? []).map((c) => c.dueDate).filter((d) => !isValidISODate(date) || monthOf(d) >= addMonths(monthOf(date), -2)),
    [paidCycles.data, date],
  );

  // El vencimiento se vuelve a proponer solo cuando cambia algo de lo que depende (no al abrir para editar).
  const dueInputs = useRef([paymentMethodId, date, operationTypeId, paidCardId]);
  useEffect(() => {
    const next = [paymentMethodId, date, operationTypeId, paidCardId];
    if (next.every((v, i) => v === dueInputs.current[i])) return;
    dueInputs.current = next;
    setDueEdited(false);
    setFirstDueDate('');
  }, [paymentMethodId, date, operationTypeId, paidCardId]);
  useEffect(() => {
    if (isCardPurchase && !dueEdited && !firstDueDate && due.data?.firstDueDate) setFirstDueDate(due.data.firstDueDate);
  }, [isCardPurchase, dueEdited, firstDueDate, due.data?.firstDueDate]);

  // Si cambia la operación/destino y la categoría o el medio ya no aplican, se limpian (una vez cargados los catálogos).
  const catalogsReady = !!type && !!categories.data && !!paymentMethods.data;
  useEffect(() => {
    if (catalogsReady && categoryId !== undefined && !visibleCategories.some((c) => c.id === categoryId))
      setCategoryId(undefined);
  }, [catalogsReady, visibleCategories, categoryId]);
  useEffect(() => {
    if (catalogsReady && paymentMethodId !== undefined && !visibleMethods.some((m) => m.id === paymentMethodId))
      setPaymentMethodId(undefined);
  }, [catalogsReady, visibleMethods, paymentMethodId]);

  const amount = parseAmount(amountText);
  const installments = Number.parseInt(installmentsText, 10);
  const errors = {
    amount: amount === null || amount <= 0 ? 'Ingresá un monto mayor a cero.' : undefined,
    type: type === undefined ? 'Elegí un tipo de operación.' : undefined,
    person: personId === undefined ? 'Elegí una persona.' : undefined,
    date: !isValidISODate(date) ? 'Fecha inválida (AAAA-MM-DD).' : undefined,
    category: isCommon && categoryId === undefined ? 'Elegí una categoría.' : undefined,
    paymentMethod: paymentMethodId === undefined ? 'Elegí un medio de pago.' : undefined,
    installments: isCardPurchase && !(installments >= 1) ? 'Cuotas inválidas.' : undefined,
    paidCard: isCardPayment && paidCardId === undefined ? 'Elegí la tarjeta que pagás.' : undefined,
    firstDueDate: isCardPayment
      ? !isValidISODate(firstDueDate)
        ? 'Indicá el vencimiento que pagás (AAAA-MM-DD).'
        : undefined
      : isCardPurchase && firstDueDate !== '' && !isValidISODate(firstDueDate)
        ? 'Fecha inválida (AAAA-MM-DD).'
        : undefined,
  };
  const hasErrors = Object.values(errors).some(Boolean);
  const show = (e?: string) => (submitted ? e : undefined);

  const reset = () => {
    setAmountText('');
    setDescription('');
    setInstallmentsText('1');
    setFirstDueDate('');
    setDueEdited(false);
    setPaidCardId(undefined);
    setSubmitted(false);
  };

  const onSubmit = () => {
    setSubmitted(true);
    if (hasErrors || amount === null || personId === undefined || paymentMethodId === undefined || !type) return;
    const tx: NewTransaction = {
      scope,
      personId,
      operationTypeId: type.id,
      date,
      description: description.trim(),
      categoryId: isCommon ? (categoryId ?? null) : null,
      amount,
      paymentMethodId,
      installments: isCardPurchase ? installments : 1,
      // Compra con tarjeta: si queda vacío, el backend propone el vencimiento según los cierres.
      firstDueDate: (isCardPurchase || isCardPayment) && firstDueDate ? firstDueDate : undefined,
      status,
      paidCardId: isCardPayment ? paidCardId : undefined,
    };
    const done = {
      onSuccess: () => {
        if (!initial) reset();
        onSaved();
      },
    };
    if (initial) update.mutate({ id: initial.id, ...tx }, done);
    else create.mutate(tx, done);
  };

  const askDelete = () => {
    if (!initial) return;
    Alert.alert(
      'Borrar movimiento',
      `¿Borrar "${initial.description || type?.name || 'movimiento'}"? Se recalculan la caja, los resúmenes de tarjeta y el presupuesto.`,
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Borrar',
          style: 'destructive',
          onPress: () =>
            remove.mutate(initial.id, {
              onSuccess: () => onDeleted?.(),
              onError: (e) => Alert.alert('No se pudo borrar', e.message),
            }),
        },
      ],
    );
  };

  const accent = scopeColor(colors, scope);
  const typeHint = !type
    ? undefined
    : type.kind === 'transferencia' && type.transferTo
      ? `Sale de ${SCOPE_LABELS[type.transferTo === 'familia' ? 'memey' : 'familia']} y entra en ${SCOPE_LABELS[type.transferTo]}.`
      : type.kind === 'ahorro' || type.kind === 'inversion'
        ? `${type.effect === 'resta' ? 'Sale de la caja hacia' : 'Vuelve a la caja desde'} ${type.kind === 'ahorro' ? 'el ahorro' : 'las inversiones'}; no es gasto.`
        : type.effect === 'suma'
          ? 'Suma a la caja.'
          : 'Resta de la caja.';
  const dueHint = due.isFetching
    ? 'Calculando según cierre de la tarjeta…'
    : due.error
      ? 'Falta cargar el cierre de la tarjeta para esa fecha en Configuración.'
      : 'Sugerido según el cierre; podés editarlo.';

  return (
    <Screen>
      <Card>
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
        <Field label="Tipo de movimiento" error={show(errors.type)} hint={typeHint}>
          <Select
            title="Tipo de movimiento"
            placeholder="Elegí un tipo"
            items={offeredTypes}
            getKey={(t) => t.id}
            getLabel={(t) => t.name}
            selectedKey={operationTypeId}
            onSelect={(t) => setOperationTypeId(t.id)}
            accent={accent}
          />
        </Field>
        <Field label="Monto" error={show(errors.amount)}>
          <Input value={amountText} onChangeText={setAmountText} placeholder="$ 0" keyboardType="decimal-pad" />
        </Field>
        <Field label="Descripción">
          <Input value={description} onChangeText={setDescription} placeholder="Ej.: supermercado" />
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
        <Field label={isCardPurchase ? 'Fecha de compra' : 'Fecha'} error={show(errors.date)}>
          <DateInput value={date} onChangeText={setDate} />
        </Field>
        <Field label="Estado">
          <Segmented<TransactionStatus>
            value={status}
            onChange={setStatus}
            options={[
              { value: 'realizado', label: STATUS_LABELS.realizado, color: accent },
              { value: 'previsto', label: STATUS_LABELS.previsto, color: colors.warn },
            ]}
          />
        </Field>
        {isCommon && (
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
        )}
        <Field label={isCardPayment ? 'Pagás desde' : 'Medio de pago'} error={show(errors.paymentMethod)}>
          <Select
            title={isCardPayment ? 'Pagás desde' : 'Medio de pago'}
            placeholder="Elegí un medio de pago"
            items={visibleMethods}
            getKey={(m) => m.id}
            getLabel={(m) => m.name}
            selectedKey={paymentMethodId}
            onSelect={(m) => setPaymentMethodId(m.id)}
            accent={accent}
          />
        </Field>
      </Card>

      {isCardPayment && (
        <Card title="Tarjeta que pagás" accent={accent}>
          <Field label="Tarjeta" error={show(errors.paidCard)}>
            <Select
              title="Tarjeta que pagás"
              placeholder="Elegí la tarjeta"
              items={cards}
              getKey={(c) => c.id}
              getLabel={(c) => c.name}
              selectedKey={paidCardId}
              onSelect={(c) => setPaidCardId(c.id)}
              accent={accent}
            />
          </Field>
          <Field
            label="Vencimiento que pagás"
            error={show(errors.firstDueDate)}
            hint={
              paidCardId !== undefined && !paidCycles.isLoading && dueOptions.length === 0
                ? 'Esta tarjeta no tiene cierres cargados; escribí la fecha.'
                : undefined
            }
          >
            {dueOptions.length > 0 && (
              <ChipPicker
                items={dueOptions}
                getKey={(d) => d}
                getLabel={formatDate}
                selectedKey={firstDueDate}
                onSelect={setFirstDueDate}
                accent={accent}
              />
            )}
            <DateInput value={firstDueDate} onChangeText={setFirstDueDate} />
          </Field>
        </Card>
      )}

      {isCardPurchase && (
        <Card title="Tarjeta" accent={accent}>
          <Field label="Cuotas" error={show(errors.installments)}>
            <Input value={installmentsText} onChangeText={setInstallmentsText} keyboardType="number-pad" maxLength={2} />
          </Field>
          <Field label="Primer vencimiento" error={show(errors.firstDueDate)} hint={dueHint}>
            <DateInput
              value={firstDueDate}
              onChangeText={(t) => {
                setDueEdited(true);
                setFirstDueDate(t);
              }}
            />
          </Field>
        </Card>
      )}

      {initial?.recurringRuleId != null && (
        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
          Viene de un gasto fijo: el cambio vale para este mes. Si después editás el gasto fijo en Fijos, sus previstos se vuelven a generar.
        </Text>
      )}

      {save.error && <Text style={{ color: colors.bad }}>No se pudo guardar: {save.error.message}</Text>}

      <Pressable
        onPress={onSubmit}
        disabled={save.isPending}
        style={[styles.submit, { backgroundColor: accent, opacity: save.isPending ? 0.6 : 1 }]}
      >
        {save.isPending ? (
          <ActivityIndicator color={colors.surface} />
        ) : (
          <Text style={[styles.submitText, { color: colors.surface }]}>
            {initial ? 'Guardar cambios' : `Guardar ${type ? type.name.toLowerCase() : 'movimiento'}`}
          </Text>
        )}
      </Pressable>

      {initial && (
        <Pressable
          onPress={askDelete}
          disabled={remove.isPending}
          style={[styles.submit, { borderWidth: 1, borderColor: colors.bad, opacity: remove.isPending ? 0.6 : 1 }]}
        >
          <Text style={[styles.submitText, { color: colors.bad }]}>Borrar movimiento</Text>
        </Pressable>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  submit: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  submitText: { fontSize: 16, fontWeight: '700' },
});
