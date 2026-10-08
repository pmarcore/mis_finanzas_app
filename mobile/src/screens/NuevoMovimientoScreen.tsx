import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text } from 'react-native';
import {
  useCardDueDate,
  useCategories,
  useCreateTransaction,
  usePaymentMethods,
  usePeople,
} from '../api/endpoints';
import type { CategoryKind, ISODate, NewTransaction, Scope } from '../api/types';
import { Card } from '../components/Card';
import { ChipPicker } from '../components/ChipPicker';
import { DateInput, Field, Input } from '../components/Field';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import type { TabScreenProps } from '../navigation/types';
import { useScope } from '../state/scope';
import { radius, scopeColor, spacing, useTheme } from '../theme';
import { isValidISODate, todayISO } from '../utils/format';

type Tipo = 'Gasto' | 'Ingreso';

/** Interpreta montos escritos a la argentina: "1.234,50" → 1234.5. */
function parseAmount(text: string): number | null {
  const clean = text.replace(/[$\s.]/g, '').replace(',', '.');
  if (!clean) return null;
  const n = Number(clean);
  return Number.isFinite(n) && n > 0 ? n : null;
}

export function NuevoMovimientoScreen({ navigation }: TabScreenProps<'Nuevo'>) {
  const { colors } = useTheme();
  const { scope: currentScope } = useScope();

  const people = usePeople();
  const categories = useCategories();
  const paymentMethods = usePaymentMethods();
  const create = useCreateTransaction();

  const [tipo, setTipo] = useState<Tipo>('Gasto');
  const [amountText, setAmountText] = useState('');
  const [description, setDescription] = useState('');
  const [scope, setScope] = useState<Scope>(currentScope === 'memey' ? 'memey' : 'familia');
  const [personId, setPersonId] = useState<string>();
  const [date, setDate] = useState<ISODate>(todayISO);
  const [categoryId, setCategoryId] = useState<string>();
  const [paymentMethodId, setPaymentMethodId] = useState<string>();
  const [installmentsText, setInstallmentsText] = useState('1');
  const [firstDueDate, setFirstDueDate] = useState('');
  const [dueEdited, setDueEdited] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const kind: CategoryKind = tipo === 'Gasto' ? 'gasto' : 'ingreso';
  const visibleCategories = useMemo(
    () => (categories.data ?? []).filter((c) => c.kind === kind && c.scope === scope),
    [categories.data, kind, scope],
  );
  const method = paymentMethods.data?.find((m) => m.id === paymentMethodId);
  const isCard = tipo === 'Gasto' && method?.type === 'tarjeta';

  // Primer vencimiento sugerido por el backend según la tarjeta y la fecha de compra.
  const due = useCardDueDate(isCard ? method?.id : undefined, isValidISODate(date) ? date : undefined);
  useEffect(() => {
    setDueEdited(false);
  }, [paymentMethodId, date]);
  useEffect(() => {
    if (isCard && !dueEdited && due.data?.firstDueDate) setFirstDueDate(due.data.firstDueDate);
  }, [isCard, dueEdited, due.data?.firstDueDate]);

  // Si cambia tipo/destino y la categoría ya no aplica, se limpia.
  useEffect(() => {
    if (categoryId && !visibleCategories.some((c) => c.id === categoryId)) setCategoryId(undefined);
  }, [visibleCategories, categoryId]);

  const amount = parseAmount(amountText);
  const installments = Number.parseInt(installmentsText, 10);
  const errors = {
    amount: amount === null ? 'Ingresá un monto mayor a cero.' : undefined,
    person: !personId ? 'Elegí una persona.' : undefined,
    date: !isValidISODate(date) ? 'Fecha inválida (AAAA-MM-DD).' : undefined,
    category: !categoryId ? 'Elegí una categoría.' : undefined,
    paymentMethod: !paymentMethodId ? 'Elegí un medio de pago.' : undefined,
    installments: isCard && !(installments >= 1) ? 'Cuotas inválidas.' : undefined,
    firstDueDate: isCard && !isValidISODate(firstDueDate) ? 'El vencimiento es obligatorio para tarjetas.' : undefined,
  };
  const hasErrors = Object.values(errors).some(Boolean);
  const show = (e?: string) => (submitted ? e : undefined);

  const reset = () => {
    setAmountText('');
    setDescription('');
    setInstallmentsText('1');
    setFirstDueDate('');
    setDueEdited(false);
    setSubmitted(false);
  };

  const onSubmit = () => {
    setSubmitted(true);
    if (hasErrors || amount === null) return;
    const tx: NewTransaction = {
      scope,
      personId: personId!,
      operation: tipo,
      date,
      description: description.trim(),
      categoryId: categoryId!,
      amount,
      paymentMethodId: paymentMethodId!,
      installments: isCard ? installments : 1,
      firstDueDate: isCard ? firstDueDate : undefined,
    };
    create.mutate(tx, {
      onSuccess: () => {
        reset();
        navigation.navigate('Movimientos');
      },
    });
  };

  const accent = scopeColor(colors, scope);

  return (
    <Screen>
      <Segmented<Tipo>
        value={tipo}
        onChange={setTipo}
        options={[
          { value: 'Gasto', label: 'Gasto', color: colors.bad },
          { value: 'Ingreso', label: 'Ingreso', color: colors.good },
        ]}
      />

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
        <Field label={tipo === 'Gasto' ? 'Fecha de compra' : 'Fecha'} error={show(errors.date)}>
          <DateInput value={date} onChangeText={setDate} />
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
            items={paymentMethods.data ?? []}
            getKey={(m) => m.id}
            getLabel={(m) => m.name}
            selectedKey={paymentMethodId}
            onSelect={(m) => setPaymentMethodId(m.id)}
            accent={accent}
          />
        </Field>
      </Card>

      {isCard && (
        <Card title="Tarjeta" accent={accent}>
          <Field label="Cuotas" error={show(errors.installments)}>
            <Input value={installmentsText} onChangeText={setInstallmentsText} keyboardType="number-pad" maxLength={2} />
          </Field>
          <Field
            label="Primer vencimiento"
            error={show(errors.firstDueDate)}
            hint={due.isFetching ? 'Calculando según cierre de la tarjeta…' : 'Sugerido según el cierre; podés editarlo.'}
          >
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
          <Text style={[styles.submitText, { color: colors.surface }]}>Guardar {tipo.toLowerCase()}</Text>
        )}
      </Pressable>
    </Screen>
  );
}

const styles = StyleSheet.create({
  submit: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  submitText: { fontSize: 16, fontWeight: '700' },
});
