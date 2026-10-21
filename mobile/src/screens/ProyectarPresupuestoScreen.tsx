import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useBudgetProposal, useSaveBudgetMonths } from '../api/endpoints';
import type { BudgetProposalQuery, Id, Month, ProposalSource, Scope } from '../api/types';
import { PROPOSAL_ORIGIN_LABELS } from '../api/types';
import { Card } from '../components/Card';
import { Field, Input } from '../components/Field';
import { SwitchRow } from '../components/FormBits';
import { MoneyText } from '../components/MoneyText';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { Segmented } from '../components/Segmented';
import { Select } from '../components/Select';
import type { RootStackScreenProps } from '../navigation/types';
import { radius, spacing, useTheme } from '../theme';
import { addMonths, amountToInput, formatMonth, monthOf, parseAmount, parsePercent, todayISO } from '../utils/format';

const SOURCES: { value: ProposalSource; label: string }[] = [
  { value: 'budget', label: 'Presupuesto de un mes' },
  { value: 'actual', label: 'Real de un mes cerrado' },
];

const range = (from: Month, count: number) => Array.from({ length: count }, (_, i) => addMonths(from, i));

/**
 * Borrador del presupuesto de meses futuros: base (presupuesto de un mes o real de un mes cerrado) con ajuste % y,
 * opcionalmente, los gastos fijos de cada mes. Se revisa y edita mes a mes y se guardan todos juntos.
 */
export function ProyectarPresupuestoScreen({ navigation, route }: RootStackScreenProps<'ProyectarPresupuesto'>) {
  const { colors } = useTheme();
  const current = monthOf(todayISO());
  const [scope, setScope] = useState<Scope>(route.params.scope);
  const [source, setSource] = useState<ProposalSource>('budget');
  const [sourceMonth, setSourceMonth] = useState<Month>(route.params.month);
  const [useFixed, setUseFixed] = useState(true);
  const [pctText, setPctText] = useState('');
  const [from, setFrom] = useState<Month>(addMonths(current, 1));
  const [to, setTo] = useState<Month>(addMonths(current, 1));
  const [query, setQuery] = useState<BudgetProposalQuery | null>(null);
  const accent = scope === 'memey' ? colors.memey : colors.familia;

  // Con "Real" solo se ofrecen meses cerrados (el último día ya pasó); con presupuesto, también los próximos.
  const sourceMonths = useMemo(
    () => (source === 'actual' ? range(addMonths(current, -12), 12) : range(addMonths(current, -12), 25)).reverse(),
    [source, current],
  );
  const targetMonths = useMemo(() => range(current, 13), [current]);
  const toMonths = targetMonths.filter((m) => m >= from && m < addMonths(from, 12));

  useEffect(() => {
    if (!sourceMonths.includes(sourceMonth)) setSourceMonth(sourceMonths[0]);
  }, [sourceMonths, sourceMonth]);
  useEffect(() => {
    if (to < from) setTo(from);
  }, [from, to]);

  const pct = parsePercent(pctText);
  const proposal = useBudgetProposal(query);
  const save = useSaveBudgetMonths(scope);

  // Borrador editable: mes → categoryId → texto del monto.
  const [draft, setDraft] = useState<Record<Month, Record<Id, string>>>({});
  const [viewMonth, setViewMonth] = useState<Month | null>(null);
  useEffect(() => {
    const data = proposal.data ?? [];
    setDraft(Object.fromEntries(data.map((m) => [m.month, Object.fromEntries(m.rows.map((r) => [r.categoryId, amountToInput(r.proposed)]))])));
    setViewMonth(data[0]?.month ?? null);
  }, [proposal.data]);

  const onPreview = () => {
    save.reset();
    setQuery({ scope, source, sourceMonth, from, to, useFixed, increasePct: pct ?? undefined });
  };

  // Cambiar cualquier parámetro invalida el borrador: hay que volver a pedirlo.
  const changed = (fn: () => void) => () => {
    fn();
    setQuery(null);
  };

  const shown = proposal.data?.find((m) => m.month === viewMonth);
  const monthDraft = (viewMonth && draft[viewMonth]) || {};
  const total = Object.values(monthDraft).reduce((sum, text) => sum + (parseAmount(text) ?? 0), 0);
  const count = proposal.data?.length ?? 0;

  const onSave = () => {
    const months = Object.entries(draft).map(([month, items]) => ({
      month,
      items: Object.entries(items)
        .map(([categoryId, text]) => ({ categoryId: Number(categoryId), amount: parseAmount(text) ?? 0 }))
        .filter((i) => i.amount > 0),
    }));
    save.mutate(months, { onSuccess: () => navigation.goBack() });
  };

  return (
    <Screen>
      <Segmented<Scope>
        value={scope}
        onChange={(s) => changed(() => setScope(s))()}
        options={[
          { value: 'familia', label: 'Familia', color: colors.familia },
          { value: 'memey', label: 'Memey', color: colors.memey },
        ]}
      />
      <Card accent={accent}>
        <Field label="Fuente">
          <Select
            title="Fuente"
            items={SOURCES}
            getKey={(s) => s.value}
            getLabel={(s) => s.label}
            selectedKey={source}
            onSelect={(s) => changed(() => setSource(s.value))()}
            accent={accent}
          />
        </Field>
        <Field label="Mes fuente">
          <Select
            title="Mes fuente"
            items={sourceMonths}
            getKey={(m) => m}
            getLabel={formatMonth}
            selectedKey={sourceMonth}
            onSelect={(m) => changed(() => setSourceMonth(m))()}
            accent={accent}
          />
        </Field>
        <SwitchRow label="Usar gastos fijos" value={useFixed} onChange={(v) => changed(() => setUseFixed(v))()} accent={accent} />
        <Field label="Ajuste % (opcional)" hint="Se aplica una vez sobre la base; los fijos ya tienen sus incrementos.">
          <Input value={pctText} onChangeText={(t) => changed(() => setPctText(t))()} placeholder="0" keyboardType="decimal-pad" />
        </Field>
        <View style={styles.pair}>
          <View style={{ flex: 1 }}>
            <Field label="Desde">
              <Select
                title="Desde"
                items={targetMonths}
                getKey={(m) => m}
                getLabel={formatMonth}
                selectedKey={from}
                onSelect={(m) => changed(() => setFrom(m))()}
                accent={accent}
              />
            </Field>
          </View>
          <View style={{ flex: 1 }}>
            <Field label="Hasta">
              <Select
                title="Hasta"
                items={toMonths}
                getKey={(m) => m}
                getLabel={formatMonth}
                selectedKey={to}
                onSelect={(m) => changed(() => setTo(m))()}
                accent={accent}
              />
            </Field>
          </View>
        </View>
      </Card>
      <Pressable onPress={onPreview} style={[styles.button, { backgroundColor: accent }]}>
        <Text style={{ color: colors.surface, fontWeight: '700' }}>Ver borrador</Text>
      </Pressable>

      {query && (
        <QueryState
          isLoading={proposal.isLoading}
          error={proposal.error}
          onRetry={proposal.refetch}
          isEmpty={count === 0}
          emptyText="No hay meses para proyectar."
        >
          {count > 1 && (
            <Field label="Mes del borrador">
              <Select
                title="Mes del borrador"
                items={proposal.data?.map((m) => m.month) ?? []}
                getKey={(m) => m}
                getLabel={formatMonth}
                selectedKey={viewMonth}
                onSelect={setViewMonth}
                accent={accent}
              />
            </Field>
          )}
          {shown && (
            <>
              <Text style={{ color: colors.ink, fontWeight: '700' }}>{formatMonth(shown.month)}</Text>
              {shown.hasOwnBudget && (
                <Text style={{ color: colors.bad }}>Este mes ya tiene presupuesto propio: al guardar se reemplaza.</Text>
              )}
              <Card accent={accent}>
                {shown.rows.length === 0 && <Text style={{ color: colors.inkMuted }}>La fuente no tiene montos.</Text>}
                {shown.rows.map((r) => (
                  <View key={r.categoryId} style={styles.line}>
                    <View style={{ flex: 1 }}>
                      <Text style={{ color: colors.ink }}>{r.category}</Text>
                      <Text style={{ color: colors.inkMuted, fontSize: 12 }}>{PROPOSAL_ORIGIN_LABELS[r.origin]}</Text>
                    </View>
                    <Input
                      value={monthDraft[r.categoryId] ?? ''}
                      onChangeText={(t) =>
                        setDraft((d) => ({ ...d, [shown.month]: { ...d[shown.month], [r.categoryId]: t } }))
                      }
                      placeholder="0"
                      keyboardType="decimal-pad"
                      style={styles.amount}
                    />
                  </View>
                ))}
                <View style={[styles.line, styles.total, { borderTopColor: colors.border }]}>
                  <Text style={{ color: colors.ink, fontWeight: '700', flex: 1 }}>Total</Text>
                  <MoneyText value={total} size="sm" />
                </View>
              </Card>
            </>
          )}
          <Pressable
            onPress={onSave}
            disabled={save.isPending || count === 0}
            style={[styles.button, { backgroundColor: accent, opacity: save.isPending ? 0.6 : 1 }]}
          >
            <Text style={{ color: colors.surface, fontWeight: '700' }}>
              Guardar {count} {count === 1 ? 'mes' : 'meses'} de {scope === 'memey' ? 'Memey' : 'Familia'}
            </Text>
          </Pressable>
          {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
        </QueryState>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  amount: { width: 130, textAlign: 'right' },
  pair: { flexDirection: 'row', gap: spacing.md },
  button: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
  total: { borderTopWidth: StyleSheet.hairlineWidth, paddingTop: spacing.sm },
});
