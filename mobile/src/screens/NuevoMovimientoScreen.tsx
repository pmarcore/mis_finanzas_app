import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text } from 'react-native';
import {
  useCardCycles,
  useCardDueDate,
  useCategories,
  useCreateTransaction,
  usePaymentMethods,
  usePeople,
} from '../api/endpoints';
import {
  OPERATION_LABELS,
  OPERATIONS,
  STATUS_LABELS,
  type CategoryKind,
  type Id,
  type ISODate,
  type NewTransaction,
  type Operation,
  type Scope,
  type TransactionStatus,
} from '../api/types';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { DateInput, Field, Input } from '../components/Field';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { addMonths, formatDate, isValidISODate, monthOf, parseAmount, todayISO } from '../utils/format';

/** Operaciones que necesitan categoría; en el resto es opcional. */
const NEEDS_CATEGORY: Operation[] = ['ingreso', 'gasto'];

export function NuevoMovimientoScreen({ navigation }: TabScreenProps<'Nuevo'>) {
  const { colors } = useTheme();
  const { scope: currentScope } = useScope();

  const people = usePeople();
  const categories = useCategories();
  const paymentMethods = usePaymentMethods();
  const create = useCreateTransaction();

  const [operation, setOperation] = useState<Operation>('gasto');
  const [status, setStatus] = useState<TransactionStatus>('realizado');
  const [amountText, setAmountText] = useState('');
  const [description, setDescription] = useState('');
  const [scope, setScope] = useState<Scope>(currentScope === 'memey' ? 'memey' : 'familia');
  const [personId, setPersonId] = useState<Id>();
  const [date, setDate] = useState<ISODate>(todayISO);
  const [categoryId, setCategoryId] = useState<Id>();
  const [paymentMethodId, setPaymentMethodId] = useState<Id>();
  const [installmentsText, setInstallmentsText] = useState('1');
  const [firstDueDate, setFirstDueDate] = useState('');
  const [dueEdited, setDueEdited] = useState(false);
  const [paidCardId, setPaidCardId] = useState<Id>();
  const [submitted, setSubmitted] = useState(false);

  const isCardPayment = operation === 'pagoTarjeta';
  const kind: CategoryKind = operation === 'ingreso' ? 'ingreso' : 'gasto';
  const needsCategory = NEEDS_CATEGORY.includes(operation);
  const visibleCategories = useMemo(
    () => (isCardPayment ? [] : (categories.data ?? []).filter((c) => c.kind === kind && c.scope === scope)),
    [categories.data, kind, scope, isCardPayment],
  );
  const allMethods = paymentMethods.data ?? [];
  const cards = allMethods.filter((m) => m.isCard);
  // Un pago de tarjeta sale de una caja: no se paga con otra tarjeta.
  const visibleMethods = isCardPayment ? allMethods.filter((m) => !m.isCard) : allMethods;
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

  useEffect(() => {
    setDueEdited(false);
    setFirstDueDate('');
  }, [paymentMethodId, date, operation, paidCardId]);
  useEffect(() => {
    if (isCardPurchase && !dueEdited && !firstDueDate && due.data?.firstDueDate) setFirstDueDate(due.data.firstDueDate);
  }, [isCardPurchase, dueEdited, firstDueDate, due.data?.firstDueDate]);

  // Si cambia la operación/destino y la categoría o el medio ya no aplican, se limpian.
  useEffect(() => {
    if (categoryId !== undefined && !visibleCategories.some((c) => c.id === categoryId)) setCategoryId(undefined);
  }, [visibleCategories, categoryId]);
  useEffect(() => {
    if (paymentMethodId !== undefined && !visibleMethods.some((m) => m.id === paymentMethodId))
      setPaymentMethodId(undefined);
  }, [visibleMethods, paymentMethodId]);

  const amount = parseAmount(amountText);
  const installments = Number.parseInt(installmentsText, 10);
  const errors = {
    amount: amount === null || amount <= 0 ? 'Ingresá un monto mayor a cero.' : undefined,
    person: personId === undefined ? 'Elegí una persona.' : undefined,
    date: !isValidISODate(date) ? 'Fecha inválida (AAAA-MM-DD).' : undefined,
    category: needsCategory && categoryId === undefined ? 'Elegí una categoría.' : undefined,
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
    if (hasErrors || amount === null || personId === undefined || paymentMethodId === undefined) return;
    const tx: NewTransaction = {
      scope,
      personId,
      operation,
      date,
      description: description.trim(),
      categoryId: categoryId ?? null,
      amount,
      paymentMethodId,
      installments: isCardPurchase ? installments : 1,
      // Compra con tarjeta: si queda vacío, el backend propone el vencimiento según los cierres.
      firstDueDate: (isCardPurchase || isCardPayment) && firstDueDate ? firstDueDate : undefined,
      status,
      paidCardId: isCardPayment ? paidCardId : undefined,
    };
    create.mutate(tx, {
      onSuccess: () => {
        reset();
        navigation.navigate('Movimientos');
      },
    });
  };

  const accent = scopeColor(colors, scope);
  const dueHint = due.isFetching
    ? 'Calculando según cierre de la tarjeta…'
    : due.error
      ? 'Falta cargar el cierre de la tarjeta para esa fecha en Configuración.'
      : 'Sugerido según el cierre; podés editarlo.';

  return (
    <Screen>
      <Field label="Operación">
        <ChipPicker
          items={[...OPERATIONS]}
          getKey={(o) => o}
          getLabel={(o) => OPERATION_LABELS[o]}
          selectedKey={operation}
          onSelect={setOperation}
          accent={accent}
        />
      </Field>

      <Card>
        <Field label="Monto" error={show(errors.amount)}>
          <Input value={amountText} onChangeText={setAmountText} placeholder="$ 0" keyboardType="decimal-pad" />
        </Field>
        <Field label="Descripción">
          <Input value={description} onChangeText={setDescription} placeholder="Ej.: supermercado" />
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
        {!isCardPayment && (
          <Field
            label={needsCategory ? 'Categoría' : 'Categoría (opcional)'}
            error={show(errors.category)}
          >
            <ChipPicker
              items={visibleCategories}
              getKey={(c) => c.id}
              getLabel={(c) => c.name}
              selectedKey={categoryId}
              // Tocar la elegida la deselecciona (sólo cuando es opcional).
              onSelect={(c) => setCategoryId(!needsCategory && c.id === categoryId ? undefined : c.id)}
              accent={accent}
            />
          </Field>
        )}
        <Field label={isCardPayment ? 'Pagás desde' : 'Medio de pago'} error={show(errors.paymentMethod)}>
          <ChipPicker
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
            <ChipPicker
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

      {create.error && (
        <Text style={{ color: colors.bad }}>No se pudo guardar: {create.error.message}</Text>
      )}

      <Pressable
        onPress={onSubmit}
        disabled={create.isPending}
        style={[styles.submit, { backgroundColor: accent, opacity: create.isPending ? 0.6 : 1 }]}
      >
        {create.isPending ? (
          <ActivityIndicator color={colors.surface} />
        ) : (
          <Text style={[styles.submitText, { color: colors.surface }]}>
            Guardar {OPERATION_LABELS[operation].toLowerCase()}
          </Text>
        )}
      </Pressable>
    </Screen>
  );
}

const styles = StyleSheet.create({
  submit: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  submitText: { fontSize: 16, fontWeight: '700' },
});
