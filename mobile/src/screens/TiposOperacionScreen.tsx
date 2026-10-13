import { useState } from 'react';
import { Alert, Pressable, Text, View } from 'react-native';
import { useOperationTypeMutations, useOperationTypes } from '../api/endpoints';
import {
  CASH_EFFECT_LABELS,
  OPERATION_KIND_LABELS,
  SCOPE_LABELS,
  SCOPES,
  type CashEffect,
  type OperationKind,
  type OperationType,
  type Scope,
} from '../api/types';
import { Card } from '../components/Card';
import { Field, Input } from '../components/Field';
import { FormButtons, NewButton, SwitchRow } from '../components/FormBits';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { Select } from '../components/Select';
import { useTheme } from '../theme';

/** El pago de tarjeta es único y lo trae el sistema: no se ofrece al crear. */
const NEW_KINDS: OperationKind[] = ['movimiento', 'ahorro', 'inversion', 'transferencia'];

/** Cómo mueve la caja, en palabras: "Resta de la caja", "Familia → Memey". */
export function describeEffect(t: Pick<OperationType, 'kind' | 'effect' | 'transferTo'>): string {
  if (t.kind === 'transferencia' && t.transferTo) {
    const from: Scope = t.transferTo === 'familia' ? 'memey' : 'familia';
    return `${SCOPE_LABELS[from]} → ${SCOPE_LABELS[t.transferTo]}`;
  }
  return CASH_EFFECT_LABELS[t.effect];
}

function TypeForm({ initial, onDone }: { initial?: OperationType; onDone: () => void }) {
  const { colors } = useTheme();
  const { create, update, remove } = useOperationTypeMutations();
  const save = initial ? update : create;
  const [name, setName] = useState(initial?.name ?? '');
  const [kind, setKind] = useState<OperationKind>(initial?.kind ?? 'movimiento');
  const [effect, setEffect] = useState<CashEffect>(initial?.effect ?? 'resta');
  const [scopes, setScopes] = useState<Scope[]>(initial?.scopes ?? ['familia']);
  const [transferTo, setTransferTo] = useState<Scope>(initial?.transferTo ?? 'memey');
  const [active, setActive] = useState(initial?.active ?? true);
  const [submitted, setSubmitted] = useState(false);

  const isCardPayment = kind === 'pagoTarjeta';
  const errors = {
    name: !name.trim() ? 'Poné un nombre.' : undefined,
    scopes: scopes.length === 0 ? 'Elegí al menos un ámbito.' : undefined,
  };
  const show = (e?: string) => (submitted ? e : undefined);
  const toggleScope = (s: Scope, on: boolean) =>
    setScopes((cur) => (on ? SCOPES.filter((x) => x === s || cur.includes(x)) : cur.filter((x) => x !== s)));

  const onSave = () => {
    setSubmitted(true);
    if (Object.values(errors).some(Boolean)) return;
    const body = {
      name: name.trim(),
      kind,
      effect,
      scopes,
      transferTo: kind === 'transferencia' ? transferTo : null,
      active,
    };
    if (initial) update.mutate({ ...body, id: initial.id }, { onSuccess: onDone });
    else create.mutate(body, { onSuccess: onDone });
  };

  const onDelete = () =>
    initial &&
    Alert.alert('Borrar tipo', `¿Borrar "${initial.name}"?`, [
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

  const kindHint: Record<OperationKind, string> = {
    movimiento: effect === 'resta' ? 'Gasto: usa categorías de gasto y cuenta en el presupuesto.' : 'Ingreso: usa categorías de ingreso.',
    pagoTarjeta: 'Paga el resumen de una tarjeta. Es único: se puede renombrar y elegir sus ámbitos.',
    transferencia: 'Pasa plata entre las cajas de Familia y Memey; el total no cambia.',
    ahorro: 'Resta: la plata sale de la caja al ahorro. Suma: vuelve del ahorro. Se ve aparte y no es gasto.',
    inversion: 'Resta: sale de la caja a inversiones. Suma: rescate. Se ve aparte y no es gasto.',
  };

  return (
    <Card title={initial ? 'Editar tipo' : 'Nuevo tipo'} accent={colors.familia}>
      <Field label="Nombre" error={show(errors.name)}>
        <Input value={name} onChangeText={setName} placeholder="Ej.: Venta Memey" />
      </Field>
      <Field label="Comportamiento" hint={kindHint[kind]}>
        {isCardPayment ? (
          <Text style={{ color: colors.ink, fontSize: 16 }}>{OPERATION_KIND_LABELS.pagoTarjeta}</Text>
        ) : (
          <Select
            title="Comportamiento"
            items={NEW_KINDS}
            getKey={(k) => k}
            getLabel={(k) => OPERATION_KIND_LABELS[k]}
            selectedKey={kind}
            onSelect={setKind}
          />
        )}
      </Field>
      {kind === 'transferencia' ? (
        <Field label="Caja que recibe" hint={describeEffect({ kind, effect, transferTo })}>
          <Segmented<Scope>
            value={transferTo}
            onChange={setTransferTo}
            options={[
              { value: 'familia', label: 'Familia', color: colors.familia },
              { value: 'memey', label: 'Memey', color: colors.memey },
            ]}
          />
        </Field>
      ) : (
        !isCardPayment && (
          <Field label="Efecto en la caja">
            <Segmented<CashEffect>
              value={effect}
              onChange={setEffect}
              options={[
                { value: 'resta', label: CASH_EFFECT_LABELS.resta, color: colors.bad },
                { value: 'suma', label: CASH_EFFECT_LABELS.suma, color: colors.good },
              ]}
            />
          </Field>
        )
      )}
      <Field label="Se ofrece en" error={show(errors.scopes)}>
        <SwitchRow label="Familia" value={scopes.includes('familia')} onChange={(v) => toggleScope('familia', v)} accent={colors.familia} />
        <SwitchRow label="Memey" value={scopes.includes('memey')} onChange={(v) => toggleScope('memey', v)} accent={colors.memey} />
      </Field>
      <SwitchRow label="Activo (se ofrece al cargar)" value={active} onChange={setActive} />
      {initial && (
        <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
          Si cambiás el efecto, se recalculan los movimientos ya cargados con este tipo.
        </Text>
      )}
      {save.error && <Text style={{ color: colors.bad }}>No se pudo guardar: {save.error.message}</Text>}
      <FormButtons
        onCancel={onDone}
        onSave={onSave}
        onDelete={initial && !isCardPayment ? onDelete : undefined}
        pending={save.isPending}
      />
    </Card>
  );
}

/** Configuración → Tipos de operación: por ámbito, con su efecto en la caja. */
export function TiposOperacionScreen() {
  const { colors } = useTheme();
  const q = useOperationTypes();
  // undefined = sin formulario; null = alta; tipo = edición.
  const [editing, setEditing] = useState<OperationType | null | undefined>(undefined);

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      {editing === undefined ? (
        <NewButton label="+ Nuevo tipo" onPress={() => setEditing(null)} />
      ) : (
        <TypeForm key={editing?.id ?? 'nuevo'} initial={editing ?? undefined} onDone={() => setEditing(undefined)} />
      )}
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        {q.data?.map((t) => (
          <Pressable key={t.id} onPress={() => setEditing(t)}>
            <Card style={{ opacity: t.active ? 1 : 0.55 }}>
              <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 8 }}>
                <Text style={{ color: colors.ink, fontSize: 16, fontWeight: '600', flexShrink: 1 }}>{t.name}</Text>
                <Text style={{ color: t.kind === 'transferencia' ? colors.inkMuted : t.effect === 'suma' ? colors.good : colors.bad }}>
                  {describeEffect(t)}
                </Text>
              </View>
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                {OPERATION_KIND_LABELS[t.kind]} · {t.scopes.map((s) => SCOPE_LABELS[s]).join(' y ')}
                {t.active ? '' : ' · inactivo'}
              </Text>
            </Card>
          </Pressable>
        ))}
        <Text style={{ color: colors.inkMuted, fontSize: 12, textAlign: 'center' }}>
          Tocá un tipo para editarlo. Los que ya tienen movimientos no se borran: se desactivan.
        </Text>
      </QueryState>
    </Screen>
  );
}
