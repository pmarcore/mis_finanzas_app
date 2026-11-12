import { useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Platform, Pressable, StyleSheet, Text, View } from 'react-native';
import { ApiError } from '../api/client';
import { Field, Input } from '../components/Field';
import { useAuth } from '../state/auth';
import { radius, spacing, useTheme } from '../theme';

/** Entrada a la app: usuario y contraseña de cada persona. Se pide una vez; la sesión dura 90 días. */
export function LoginScreen() {
  const { colors } = useTheme();
  const { login, notice } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canSubmit = username.trim().length > 0 && password.length > 0 && !pending;

  const onSubmit = async () => {
    if (!canSubmit) return;
    setPending(true);
    setError(null);
    try {
      await login(username.trim(), password);
    } catch (e) {
      if (e instanceof ApiError && e.status === 429) setError('Demasiados intentos. Esperá un minuto y probá de nuevo.');
      else if (e instanceof ApiError && e.status === 401) setError('Usuario o contraseña incorrectos.');
      else setError(e instanceof Error ? e.message : 'No se pudo entrar.');
    } finally {
      setPending(false);
    }
  };

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      style={[styles.root, { backgroundColor: colors.ground }]}
    >
      <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>
        <Text style={[styles.title, { color: colors.ink }]}>Finanzas</Text>
        <Text style={{ color: colors.inkMuted, marginBottom: spacing.md }}>Entrá con tu usuario.</Text>
        {notice ? <Text style={{ color: colors.warn, marginBottom: spacing.sm }}>{notice}</Text> : null}
        <Field label="Usuario">
          <Input
            value={username}
            onChangeText={setUsername}
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="username"
            textContentType="username"
            returnKeyType="next"
          />
        </Field>
        <Field label="Contraseña">
          <Input
            value={password}
            onChangeText={setPassword}
            secureTextEntry
            autoComplete="current-password"
            textContentType="password"
            returnKeyType="go"
            onSubmitEditing={onSubmit}
          />
        </Field>
        {error ? <Text style={{ color: colors.bad, marginBottom: spacing.sm }}>{error}</Text> : null}
        <Pressable
          onPress={onSubmit}
          disabled={!canSubmit}
          accessibilityRole="button"
          style={[styles.button, { backgroundColor: colors.familia, opacity: canSubmit ? 1 : 0.6 }]}
        >
          {pending ? (
            <ActivityIndicator color={colors.surface} />
          ) : (
            <Text style={{ color: colors.surface, fontWeight: '700' }}>Entrar</Text>
          )}
        </Pressable>
      </View>
    </KeyboardAvoidingView>
  );
}

export function LoadingScreen() {
  const { colors } = useTheme();
  return (
    <View style={[styles.root, { backgroundColor: colors.ground }]}>
      <ActivityIndicator color={colors.familia} />
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, justifyContent: 'center', padding: spacing.lg },
  card: {
    width: '100%',
    maxWidth: 420,
    alignSelf: 'center',
    borderRadius: radius.lg,
    borderWidth: StyleSheet.hairlineWidth,
    padding: spacing.lg,
    gap: spacing.xs,
  },
  title: { fontSize: 28, fontWeight: '800' },
  button: { borderRadius: radius.md, paddingVertical: spacing.md, alignItems: 'center', marginTop: spacing.sm },
});
