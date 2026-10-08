import { Text, type StyleProp, type TextStyle } from 'react-native';
import { useTheme } from '../theme';
import { formatARS } from '../utils/format';

interface Props {
  value: number | null | undefined;
  size?: 'sm' | 'md' | 'lg' | 'xl';
  /** Colorea en rojo los negativos (por defecto sí). */
  signColor?: boolean;
  color?: string;
  style?: StyleProp<TextStyle>;
}

const SIZES = { sm: 14, md: 17, lg: 22, xl: 30 } as const;

export function MoneyText({ value, size = 'md', signColor = true, color, style }: Props) {
  const { colors } = useTheme();
  const isNeg = typeof value === 'number' && value < 0;
  return (
    <Text
      style={[
        {
          fontSize: SIZES[size],
          fontWeight: size === 'sm' ? '500' : '700',
          fontVariant: ['tabular-nums'],
          color: color ?? (signColor && isNeg ? colors.bad : colors.ink),
        },
        style,
      ]}
    >
      {typeof value === 'number' ? formatARS(value) : '—'}
    </Text>
  );
}
