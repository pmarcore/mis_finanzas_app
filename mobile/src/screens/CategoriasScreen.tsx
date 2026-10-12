import { useState } from 'react';
import { Alert, Pressable, Text } from 'react-native';
import { useCategories, useCategoryMutations } from '../api/endpoints';
import {
  BUDGET_TYPE_LABELS,
  CATEGORY_KIND_LABELS,
  type BudgetType,
  type Category,
  type CategoryKind,
  type Scope,
} from '../api/types';
import { Card } from '../components/Card';
import { Field, Input } from '../components/Field';
import { FormButtons, NewButton, SwitchRow } from '../components/FormBits';
import { QueryState } from '../components/QueryState';
import { ScopeSelector } from '../components/ScopeSelector';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { useScope } from '../state/scope';
import { scopeColor, useTheme } from '../theme';

function CategoryForm({ initial, defaultScope, onDone }: { initial?: Category; defaultScope: Scope; onDone: () => void }) {
  const { colors } = useTheme();
  const { create, update, remove } = useCategoryMutations();
  const save = initial ? update : create;
  const [name, setName] = useState(initial?.name ?? '');
  const [scope, setScope] = useState<Scope>(initial?.scope ?? defaultScope);
  const [kind, setKind] = useState<CategoryKind>(initial?.kind ?? 'gasto');
  const [budgetType, setBudgetType] = useState<BudgetType>(initial?.budgetType ?? 'variable');
  const [active, setActive] = useState(initial?.active ?? true);
  const [submitted, setSubmitted] = useState(false);
  const accent = scopeColor(colors, scope);
  const nameError = !name.trim() ? 'Poné un nombre.' : undefined;

  const onSave = () => {
    setSubmitted(true);
    if (nameError) return;
    const body = { name: name.trim(), scope, kind, budgetType, active };
    if (initial) update.mutate({ ...body, id: initial.id }, { onSuccess: onDone });
    else create.mutate(body, { onSuccess: onDone });
  };

  const onDelete = () =>
    initial &&
    Alert.alert('Borrar categoría', `¿Borrar "${initial.name}"?`, [
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
    <Card title={initial ? 'Editar categoría' : 'Nueva categoría'} accent={accent}>
      <Field label="Nombre" error={submitted ? nameError : undefined}>
        <Input value={name} onChangeText={setName} placeholder="Ej.: Mascotas" />
      </Field>
      <Field label="Ámbito">
        <Segmented<Scope>
          value={scope}
          onChange={setScope}
          options={[
            { value: 'familia', label: 'Familia', color: colors.familia },
            { value: 'memey', label: 'Memey', color: colors.memey },
          ]}
        />
      </Field>
      <Field label="Es de">
        <Segmented<CategoryKind>
          value={kind}
          onChange={setKind}
          options={[
            { value: 'gasto', label: CATEGORY_KIND_LABELS.gasto, color: accent },
            { value: 'ingreso', label: CATEGORY_KIND_LABELS.ingreso, color: accent },
          ]}
        />
      </Field>
      {kind === 'gasto' && (
        <Field label="En el presupuesto" hint="Variable proyecta el ritmo del mes; fijo y deuda toman el mayor entre presupuesto y real.">
          <Segmented<BudgetType>
            value={budgetType}
            onChange={setBudgetType}
            options={(['variable', 'fijo', 'deuda'] as BudgetType[]).map((b) => ({ value: b, label: BUDGET_TYPE_LABELS[b], color: accent }))}
          />
        </Field>
      )}
      <SwitchRow label="Activa (se ofrece al cargar)" value={active} onChange={setActive} accent={accent} />
      {save.error && <Text style={{ color: colors.bad }}>No se pudo guardar: {save.error.message}</Text>}
      <FormButtons onCancel={onDone} onSave={onSave} onDelete={initial ? onDelete : undefined} pending={save.isPending} accent={accent} />
    </Card>
  );
}

/** Configuración → Categorías del ámbito elegido, separadas en gastos e ingresos. */
export function CategoriasScreen() {
  const { colors } = useTheme();
  const { scope } = useScope();
  const q = useCategories(scope);
  const [editing, setEditing] = useState<Category | null | undefined>(undefined);
  const accent = scopeColor(colors, scope);

  const group = (kind: CategoryKind) => {
    const items = (q.data ?? []).filter((c) => c.kind === kind);
    if (items.length === 0) return null;
    return (
      <Card title={kind === 'gasto' ? 'Gastos' : 'Ingresos'} accent={accent}>
        {items.map((c) => (
          <Pressable key={c.id} onPress={() => setEditing(c)} hitSlop={4}>
            <Text style={{ color: colors.ink, fontSize: 16, opacity: c.active ? 1 : 0.5, paddingVertical: 4 }}>
              {c.name}
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
                {kind === 'gasto' ? `  ${BUDGET_TYPE_LABELS[c.budgetType]}` : ''}
                {c.active ? '' : ' · inactiva'}
              </Text>
            </Text>
          </Pressable>
        ))}
      </Card>
    );
  };

  return (
    <Screen refreshing={q.isRefetching} onRefresh={q.refetch}>
      <ScopeSelector />
      {editing === undefined ? (
        <NewButton label="+ Nueva categoría" onPress={() => setEditing(null)} accent={accent} />
      ) : (
        <CategoryForm key={editing?.id ?? 'nueva'} initial={editing ?? undefined} defaultScope={scope} onDone={() => setEditing(undefined)} />
      )}
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch} isEmpty={q.data?.length === 0} emptyText="No hay categorías.">
        {group('gasto')}
        {group('ingreso')}
        <Text style={{ color: colors.inkMuted, fontSize: 12, textAlign: 'center' }}>
          Tocá una categoría para editarla. Las que ya tienen movimientos o presupuesto no se borran: se desactivan.
        </Text>
      </QueryState>
    </Screen>
  );
}
