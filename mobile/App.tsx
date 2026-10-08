import { DarkTheme, DefaultTheme, NavigationContainer, type Theme } from '@react-navigation/native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { RootNavigator } from './src/navigation/RootNavigator';
import { ScopeProvider } from './src/state/scope';
import { useTheme } from './src/theme';

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: 30_000, retry: 1 },
    },
  });
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
        <ScopeProvider>
          <NavigationContainer theme={navTheme}>
            <RootNavigator />
          </NavigationContainer>
        </ScopeProvider>
      </QueryClientProvider>
      <StatusBar style={dark ? 'light' : 'dark'} />
    </SafeAreaProvider>
  );
}
