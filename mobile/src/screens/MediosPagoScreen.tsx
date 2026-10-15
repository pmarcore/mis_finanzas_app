import { useState } from 'react';
import { Pressable, Text, View } from 'react-native';
import { Alert } from '../utils/alert';
import { usePaymentMethodMutations, usePaymentMethods, useSettings } from '../api/endpoints';
import { PAYMENT_METHOD_TYPE_LABELS, type Id, type PaymentMethod, type PaymentMethodType } from '../api/types';
import { Card } from '../components/Card';
import { Field, Input } from '../components/Field';
import { FormButtons, NewButton, SwitchRow } from '../components/FormBits';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { Select } from '../components/Select';
import { scopeColor, useTheme } from '../theme';

const TYPES = Object.keys(PAYMENT_METHOD_TYPE_LABELS) as PaymentMethodType[];

function MethodForm({ initial, onDone }: { initial?: PaymentMethod; onDone: () => void }) {
  const { colors } = useTheme();
  const settings = useSettings();
  const boxes = settings.data?.cashBoxes ?? [];
  const { create, update, remove } = usePaymentMethodMutations();
  const save = initial ? update : create;
  const [name, setName] = useState(initial?.name ?? '');
  const [type, setType] = useState<PaymentMethodType>(initial?.type ?? 'debito');
  const [cashBoxId, setCashBoxId] = useState<Id | undefined>(initial?.cashBoxId);
  const [active, setActive] = useState(initial?.active ?? true);
  const [submitted, setSubmitted] = useState(false);
  const boxId = cashBoxId ?? boxes[0]?.id;
  const errors = {
    name: !name.trim() ? 'Poné un nombre.' : undefined,
    box: boxId === undefined ? 'Elegí la caja.' : undefined,
  };

  const onSave = () => {
    setSubmitted(true);
    if (Object.values(errors).some(Boolean) || boxId === undefined) return;
    const body = { name: name.trim(), type, cashBoxId: boxId, isCard: type === 'tarjeta', active };
    if (initial) update.mutate({ ...body, id: initial.id }, { onSuccess: onDone });
    else create.mutate(body, { onSuccess: onDone });
  };

  const onDelete = () =>
    initial &&
    Alert.alert('Borrar medio de pago', `¿Borrar "${initial.name}"?`, [
      { text: 'Cancelar', style: 'cancel' },
      {
        text: 'Borrar',
        style: 'destructive',
        onPress: () =>
          remove.mutate(initial.id, {
            onSuccess: onDone,
            onError: (e) => Alert.alert('No se pudo borrar', e.message),
          }),
      },
    ]);

  return (
    <Card title={initial ? 'Editar medio de pago' : 'Nuevo medio de pago'} accent={colors.familia}>
      <Field label="Nombre" error={submitted ? errors.name : undefined}>
        <Input value={name} onChangeText={setName} placeholder="Ej.: Visa BBVA" />
      </Field>
      <Field
        label="Tipo"
        hint={type === 'tarjeta' ? 'Las compras entran a la caja en el vencimiento: cargá sus cierres en Configuración.' : undefined}
      >
        <Select
          title="Tipo de medio de pago"
          items={TYPES}
          getKey={(t) => t}
          getLabel={(t) => PAYMENT_METHOD_TYPE_LABELS[t]}
          selectedKey={type}
          onSelect={setType}
        />
      </Field>
      <Field label="Caja habitual" error={submitted ? errors.box : undefined}>
        {boxes.length > 0 && (
          <Segmented<string>
            value={boxId === undefined ? undefined : String(boxId)}
            onChange={(v) => setCashBoxId(Number(v))}
            options={boxes.map((b) => ({ value: String(b.id), label: b.name, color: scopeColor(colors, b.scope) }))}
          />
        )}
      </Field>
      <SwitchRow label="Activo (se ofrece al cargar)" value={active} onChange={setActive} />
      {save.error && <Text style={{ color: colors.bad }}>No se pudo guardar: {save.error.message}</Text>}
      <FormButtons onCancel={onDone} onSave={onSave} onDelete={initial ? onDelete : undefined} pending={save.isPending} />
    </Card>
  );
}

/** Configuración → Medios de pago (débito, efectivo, billeteras y tarjetas). */
export function MediosPagoScreen() {
  const { colors } = useTheme();
  const q = usePaymentMethods();
  const settings = useSettings();
  const [editing, setEditing] = useState<PaymentMethod | null | undefined>(undefined);
  const boxOf = (id: Id) => settings.data?.cashBoxes.find((b) => b.id === id);

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      {editing === undefined ? (
        <NewButton label="+ Nuevo medio de pago" onPress={() => setEditing(null)} />
      ) : (
        <MethodForm key={editing?.id ?? 'nuevo'} initial={editing ?? undefined} onDone={() => setEditing(undefined)} />
      )}
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        {q.data?.map((m) => {
          const box = boxOf(m.cashBoxId);
          return (
            <Pressable key={m.id} onPress={() => setEditing(m)}>
              <Card accent={box ? scopeColor(colors, box.scope) : undefined} style={{ opacity: m.active ? 1 : 0.55 }}>
                <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 8 }}>
                  <Text style={{ color: colors.ink, fontSize: 16, fontWeight: '600', flexShrink: 1 }}>{m.name}</Text>
                  <Text style={{ color: colors.inkMuted }}>{PAYMENT_METHOD_TYPE_LABELS[m.type]}</Text>
                </View>
                <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                  {box?.name ?? ''}
                  {m.active ? '' : ' · inactivo'}
                </Text>
              </Card>
            </Pressable>
          );
        })}
        <Text style={{ color: colors.inkMuted, fontSize: 12, textAlign: 'center' }}>
          Tocá un medio para editarlo. Los que ya tienen movimientos no se borran: se desactivan.
        </Text>
      </QueryState>
    </Screen>
  );
}
