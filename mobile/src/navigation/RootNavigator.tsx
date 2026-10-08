import { Ionicons } from '@expo/vector-icons';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { useNavigation } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { Pressable } from 'react-native';
import { useScope } from '../state/scope';
import { scopeColor, useTheme } from '../theme';
import { ConfiguracionScreen } from '../screens/ConfiguracionScreen';
import { FijosScreen } from '../screens/FijosScreen';
import { InicioScreen } from '../screens/InicioScreen';
import { MovimientosScreen } from '../screens/MovimientosScreen';
import { NuevoMovimientoScreen } from '../screens/NuevoMovimientoScreen';
import { PresupuestoScreen } from '../screens/PresupuestoScreen';
import { ReporteScreen } from '../screens/ReporteScreen';
import { TarjetasScreen } from '../screens/TarjetasScreen';
import type { RootStackParamList, TabParamList } from './types';

const Tab = createBottomTabNavigator<TabParamList>();
const Stack = createNativeStackNavigator<RootStackParamList>();

type IconName = keyof typeof Ionicons.glyphMap;
const TAB_ICONS: Record<keyof TabParamList, IconName> = {
  Inicio: 'home-outline',
  Movimientos: 'list-outline',
  Nuevo: 'add-circle',
  Fijos: 'repeat-outline',
  Presupuesto: 'pie-chart-outline',
};

function GearButton() {
  const navigation = useNavigation();
  const { colors } = useTheme();
  return (
    <Pressable
      onPress={() => navigation.navigate('Configuracion')}
      accessibilityLabel="Configuración"
      hitSlop={12}
      style={{ paddingHorizontal: 12 }}
    >
      <Ionicons name="settings-outline" size={22} color={colors.ink} />
    </Pressable>
  );
}

function Tabs() {
  const { colors } = useTheme();
  const { scope } = useScope();
  const accent = scopeColor(colors, scope);
  return (
    <Tab.Navigator
      screenOptions={({ route }) => ({
        headerRight: () => <GearButton />,
        headerStyle: { backgroundColor: colors.surface },
        headerTintColor: colors.ink,
        tabBarActiveTintColor: accent,
        tabBarInactiveTintColor: colors.inkMuted,
        tabBarStyle: { backgroundColor: colors.surface, borderTopColor: colors.border },
        tabBarIcon: ({ color, size }) => (
          <Ionicons
            name={TAB_ICONS[route.name]}
            color={route.name === 'Nuevo' ? accent : color}
            size={route.name === 'Nuevo' ? size + 10 : size}
          />
        ),
      })}
    >
      <Tab.Screen name="Inicio" component={InicioScreen} />
      <Tab.Screen name="Movimientos" component={MovimientosScreen} />
      <Tab.Screen
        name="Nuevo"
        component={NuevoMovimientoScreen}
        options={{ title: 'Nuevo movimiento', tabBarLabel: 'Nuevo' }}
      />
      <Tab.Screen name="Fijos" component={FijosScreen} />
      <Tab.Screen name="Presupuesto" component={PresupuestoScreen} />
    </Tab.Navigator>
  );
}

export function RootNavigator() {
  const { colors } = useTheme();
  return (
    <Stack.Navigator
      screenOptions={{
        headerStyle: { backgroundColor: colors.surface },
        headerTintColor: colors.ink,
        contentStyle: { backgroundColor: colors.ground },
        headerBackTitle: 'Volver',
      }}
    >
      <Stack.Screen name="Tabs" component={Tabs} options={{ headerShown: false }} />
      <Stack.Screen name="Reporte" component={ReporteScreen} options={{ title: 'Presupuesto vs. real' }} />
      <Stack.Screen name="Tarjetas" component={TarjetasScreen} />
      <Stack.Screen name="Configuracion" component={ConfiguracionScreen} options={{ title: 'Configuración' }} />
    </Stack.Navigator>
  );
}
