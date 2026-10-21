import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import {
  useInvestmentSettings,
  useInvestmentTargets,
  useManualHoldingMutations,
  useManualHoldings,
  useSaveInvestmentSettings,
  useSaveInvestmentTargets,
} from '../api/endpoints';
import type { AssetType, Id, ManualHolding } from '../api/types';
import { Card } from '../components/Card';
import { Field, Input } from '../components/Field';
import { FormButtons, NewButton, SwitchRow } from '../components/FormBits';
import { QueryState } from '../components/QueryState';
import { Screen } from '../components/Screen';
import { radius, spacing, useTheme } from '../theme';
import { Alert } from '../utils/alert';
import { amountToInput, formatNumber, formatUSD, parseAmount, parsePercent } from '../utils/format';

/** Configuración del dashboard de inversiones: tenencias de Ripio, cartera objetivo, umbrales y lista de seguimiento. */
export function InversionesConfigScreen() {
  return (
    <Screen>
      <RipioSection />
      <TargetsSection />
      <SettingsSection />
    </Screen>
  );
}

/** Cantidades con decimales (cripto): acepta "0,015" o "0.015". */
function parseQuantity(text: string): number | null {
  const clean = text.trim().replace(',', '.');
  if (!clean) return null;
  const n = Number(clean);
  return Number.isFinite(n) ? n : null;
}

interface HoldingDraft {
  id?: Id;
  symbol: string;
  quantity: string;
  cost: string;
  active: boolean;
}

function RipioSection() {
  const { colors } = useTheme();
  const q = useManualHoldings();
  const { create, update, remove } = useManualHoldingMutations();
  const [draft, setDraft] = useState<HoldingDraft | null>(null);
  const [error, setError] = useState<string>();

  const edit = (h?: ManualHolding) => {
    setError(undefined);
    setDraft(
      h
        ? { id: h.id, symbol: h.symbol, quantity: String(h.quantity).replace('.', ','), cost: amountToInput(h.costUsd), active: h.active }
        : { symbol: '', quantity: '', cost: '', active: true },
    );
  };

  const save = () => {
    if (!draft) return;
    const quantity = parseQuantity(draft.quantity);
    const cost = draft.cost.trim() ? parseAmount(draft.cost) : null;
    if (!draft.symbol.trim() || quantity === null || quantity < 0 || (draft.cost.trim() && cost === null)) {
      setError('Cargá el activo (por ejemplo BTC), una cantidad válida y, si querés, el costo total en US$.');
      return;
    }
    const body = { symbol: draft.symbol.trim().toUpperCase(), quantity, costUsd: cost, active: draft.active };
    const done = { onSuccess: () => setDraft(null), onError: (e: Error) => setError(e.message) };
    if (draft.id) update.mutate({ id: draft.id, ...body }, done);
    else create.mutate(body, done);
  };

  const del = () => {
    if (!draft?.id) return;
    const id = draft.id;
    Alert.alert('Borrar tenencia', `¿Borrar ${draft.symbol} de Ripio?`, [
      { text: 'Cancelar', style: 'cancel' },
      { text: 'Borrar', style: 'destructive', onPress: () => remove.mutate(id, { onSuccess: () => setDraft(null) }) },
    ]);
  };

  return (
    <Card title="Tenencias de Ripio">
      <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
        Ripio no tiene API para cuentas personales: cargá cuánto tenés de cada cripto y la app busca la cotización sola. Cuando
        compres o vendas, actualizá la cantidad.
      </Text>
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        {(q.data ?? []).map((h) => (
          <Pressable key={h.id} onPress={() => edit(h)} style={[styles.row, { borderBottomColor: colors.border }]} accessibilityRole="button">
            <Text style={{ color: h.active ? colors.ink : colors.inkMuted, fontWeight: '700', width: 64 }}>{h.symbol}</Text>
            <Text style={{ color: colors.ink, flex: 1, fontVariant: ['tabular-nums'] }}>{formatNumber(h.quantity, 8)}</Text>
            <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
              {h.costUsd !== null ? `costo ${formatUSD(h.costUsd)}` : 'sin costo'}
              {h.active ? '' : ' · inactiva'}
            </Text>
          </Pressable>
        ))}
      </QueryState>
      {draft ? (
        <View style={{ gap: spacing.sm }}>
          <Field label="Activo">
            <Input value={draft.symbol} onChangeText={(t) => setDraft({ ...draft, symbol: t })} autoCapitalize="characters" placeholder="BTC" />
          </Field>
          <Field label="Cantidad">
            <Input value={draft.quantity} onChangeText={(t) => setDraft({ ...draft, quantity: t })} keyboardType="decimal-pad" placeholder="0,015" />
          </Field>
          <Field label="Costo total en US$ (opcional)" hint="Con el costo, la app calcula el rendimiento y lo marca con *.">
            <Input value={draft.cost} onChangeText={(t) => setDraft({ ...draft, cost: t })} keyboardType="decimal-pad" />
          </Field>
          <SwitchRow label="Activa" value={draft.active} onChange={(v) => setDraft({ ...draft, active: v })} />
          {error && <Text style={{ color: colors.bad }}>{error}</Text>}
          <FormButtons onCancel={() => setDraft(null)} onSave={save} onDelete={draft.id ? del : undefined} pending={create.isPending || update.isPending} />
        </View>
      ) : (
        <NewButton label="+ Agregar cripto" onPress={() => edit()} />
      )}
    </Card>
  );
}

const TARGET_TYPES: { type: AssetType; label: string }[] = [
  { type: 'bonos', label: 'Bonos' },
  { type: 'ons', label: 'ONs' },
  { type: 'cedears', label: 'CEDEARs' },
  { type: 'acciones', label: 'Acciones' },
  { type: 'fci', label: 'FCI' },
  { type: 'cripto', label: 'Cripto' },
  { type: 'efectivo', label: 'Efectivo' },
];

function TargetsSection() {
  const { colors } = useTheme();
  const q = useInvestmentTargets();
  const save = useSaveInvestmentTargets();
  const [text, setText] = useState<Partial<Record<AssetType, string>>>({});

  useEffect(() => {
    setText(Object.fromEntries((q.data ?? []).map((t) => [t.assetType, String(t.targetPct).replace('.', ',')])));
  }, [q.data]);

  const values = TARGET_TYPES.map((t) => ({ assetType: t.type, targetPct: parsePercent(text[t.type] ?? '') ?? 0 }));
  const sum = values.reduce((s, v) => s + v.targetPct, 0);
  const ok = sum === 0 || Math.abs(sum - 100) < 0.001;

  return (
    <Card title="Cartera objetivo">
      <Text style={{ color: colors.inkMuted, fontSize: 12 }}>
        El % que querés en cada tipo de activo. El dashboard muestra cuánto te desviás y cuánta plata habría que mover para volver.
        Dejalo todo vacío para no usarla.
      </Text>
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        <View style={styles.wrap}>
          {TARGET_TYPES.map((t) => (
            <View key={t.type} style={styles.pct}>
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>{t.label}</Text>
              <Input value={text[t.type] ?? ''} onChangeText={(v) => setText((m) => ({ ...m, [t.type]: v }))} keyboardType="decimal-pad" placeholder="0" />
            </View>
          ))}
        </View>
        <Text style={{ color: ok ? colors.inkMuted : colors.bad, fontWeight: '600' }}>Suma: {formatNumber(sum, 1)} %{ok ? '' : ' (tiene que dar 100 %)'}</Text>
        {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
        {save.isSuccess && <Text style={{ color: colors.good }}>Cartera objetivo guardada.</Text>}
        <SaveButton label="Guardar cartera objetivo" pending={save.isPending} disabled={!ok} onPress={() => save.mutate(values.filter((v) => v.targetPct > 0))} />
      </QueryState>
    </Card>
  );
}

function SettingsSection() {
  const { colors } = useTheme();
  const q = useInvestmentSettings();
  const save = useSaveInvestmentSettings();
  const [form, setForm] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!q.data) return;
    const s = q.data;
    setForm({
      maxAssetPct: String(s.maxAssetPct),
      maxGroupPct: String(s.maxGroupPct),
      targetDeviationPts: String(s.targetDeviationPts),
      idleCashArs: amountToInput(s.idleCashArs),
      idleCashUsd: amountToInput(s.idleCashUsd),
      watchList: s.watchList.split(',').join(', '),
    });
  }, [q.data]);

  const set = (k: string) => (v: string) => setForm((f) => ({ ...f, [k]: v }));
  const onSave = () => {
    const nums = {
      maxAssetPct: parsePercent(form.maxAssetPct ?? ''),
      maxGroupPct: parsePercent(form.maxGroupPct ?? ''),
      targetDeviationPts: parsePercent(form.targetDeviationPts ?? ''),
      idleCashArs: parseAmount(form.idleCashArs ?? ''),
      idleCashUsd: parseAmount(form.idleCashUsd ?? ''),
    };
    if (Object.values(nums).some((n) => n === null)) {
      Alert.alert('Revisá los valores', 'Completá todos los umbrales con números.');
      return;
    }
    save.mutate({ ...(nums as Record<keyof typeof nums, number>), watchList: form.watchList ?? '' });
  };

  return (
    <Card title="Señales y cotizaciones">
      <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
        <View style={styles.wrap}>
          <View style={styles.pct}>
            <Text style={{ color: colors.inkMuted, fontSize: 12 }}>Máx. por activo %</Text>
            <Input value={form.maxAssetPct ?? ''} onChangeText={set('maxAssetPct')} keyboardType="decimal-pad" />
          </View>
          <View style={styles.pct}>
            <Text style={{ color: colors.inkMuted, fontSize: 12 }}>Máx. por tipo o moneda %</Text>
            <Input value={form.maxGroupPct ?? ''} onChangeText={set('maxGroupPct')} keyboardType="decimal-pad" />
          </View>
          <View style={styles.pct}>
            <Text style={{ color: colors.inkMuted, fontSize: 12 }}>Desvío objetivo (puntos)</Text>
            <Input value={form.targetDeviationPts ?? ''} onChangeText={set('targetDeviationPts')} keyboardType="decimal-pad" />
          </View>
        </View>
        <Field label="Plata quieta en el broker a partir de" hint="Pesos y dólares. El excedente de caja usa el piso de caja de Configuración.">
          <View style={styles.wrap}>
            <View style={styles.pct}>
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>$</Text>
              <Input value={form.idleCashArs ?? ''} onChangeText={set('idleCashArs')} keyboardType="decimal-pad" />
            </View>
            <View style={styles.pct}>
              <Text style={{ color: colors.inkMuted, fontSize: 12 }}>US$</Text>
              <Input value={form.idleCashUsd ?? ''} onChangeText={set('idleCashUsd')} keyboardType="decimal-pad" />
            </View>
          </View>
        </Field>
        <Field label="Lista de seguimiento" hint='Separados por coma. "GGAL" es BYMA; "nyse:SPY" o "nasdaq:AAPL" para EE. UU.; "cripto:BTC" para cripto.'>
          <Input value={form.watchList ?? ''} onChangeText={set('watchList')} autoCapitalize="characters" autoCorrect={false} />
        </Field>
        {save.error && <Text style={{ color: colors.bad }}>{save.error.message}</Text>}
        {save.isSuccess && <Text style={{ color: colors.good }}>Guardado.</Text>}
        <SaveButton label="Guardar señales y cotizaciones" pending={save.isPending} onPress={onSave} />
      </QueryState>
    </Card>
  );
}

function SaveButton({ onPress, pending, label, disabled }: { onPress: () => void; pending: boolean; label: string; disabled?: boolean }) {
  const { colors } = useTheme();
  return (
    <Pressable
      onPress={onPress}
      disabled={pending || disabled}
      style={[styles.button, { backgroundColor: colors.familia, opacity: pending || disabled ? 0.5 : 1 }]}
      accessibilityRole="button"
    >
      <Text style={{ color: colors.surface, fontWeight: '700' }}>{pending ? 'Guardando…' : label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  pct: { flexBasis: '30%', flexGrow: 1, gap: 2 },
  button: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center' },
});
