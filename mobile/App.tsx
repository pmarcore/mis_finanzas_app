import { DarkTheme, DefaultTheme, NavigationContainer, type Theme } from '@react-navigation/native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { RootNavigator } from './src/navigation/RootNavigator';
import { LoadingScreen, LoginScreen } from './src/screens/LoginScreen';
import { AuthProvider, useAuth } from './src/state/auth';
import { ScopeProvider } from './src/state/scope';
import { useTheme } from './src/theme';

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: 30_000, retry: 1 },
    },
  });
}

/** Login si hace falta; si no, la app de siempre. */
function Gate({ navTheme }: { navTheme: Theme }) {
  const { status } = useAuth();
  if (status === 'loading') return <LoadingScreen />;
  if (status === 'signedOut') return <LoginScreen />;
  return (
    <ScopeProvider>
      <NavigationContainer theme={navTheme}>
        <RootNavigator />
      </NavigationContainer>
    </ScopeProvider>
  );
}

export default function App() {
  const [queryClient] = useState(makeQueryClient);
  const { dark, colors } = useTheme();
  const base = dark ? DarkTheme : DefaultTheme;
  const navTheme: Theme = {
    ...base,
    colors: {
      ...base.colors,
      primary: colors.familia,
      background: colors.ground,
      card: colors.surface,
      text: colors.ink,
      border: colors.border,
      notification: colors.memey,
    },
  };

  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <Gate navTheme={navTheme} />
        </AuthProvider>
      </QueryClientProvider>
      <StatusBar style={dark ? 'light' : 'dark'} />
    </SafeAreaProvider>
  );
}
