import { useColorScheme } from 'react-native';
import type { ScopeView } from './api/types';

export interface Palette {
  familia: string;
  memey: string;
  ink: string;
  inkMuted: string;
  ground: string;
  surface: string;
  border: string;
  good: string;
  warn: string;
  bad: string;
}

export const lightColors: Palette = {
  familia: '#1f6f78',
  memey: '#b0457a',
  ink: '#16202a',
  inkMuted: '#5b6773',
  ground: '#e9ecef',
  surface: '#ffffff',
  border: '#d3d8dd',
  good: '#2f7d4a',
  warn: '#b26b00',
  bad: '#c0392b',
};

export const darkColors: Palette = {
  familia: '#5fc0c9',
  memey: '#e58ab6',
  ink: '#e8edf1',
  inkMuted: '#9aa6b1',
  ground: '#0d1216',
  surface: '#161d23',
  border: '#26313a',
  good: '#5cb87a',
  warn: '#e0a040',
  bad: '#e8685a',
};

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24 } as const;
export const radius = { sm: 6, md: 10, lg: 14 } as const;

export function useTheme() {
  const scheme = useColorScheme();
  const dark = scheme === 'dark';
  return { dark, colors: dark ? darkColors : lightColors };
}

/** Color de acento para la vista seleccionada ('total' usa el tono de tinta). */
export function scopeColor(colors: Palette, scope: ScopeView): string {
  if (scope === 'familia') return colors.familia;
  if (scope === 'memey') return colors.memey;
  return colors.ink;
}
