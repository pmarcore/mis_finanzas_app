import type { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import type { CompositeScreenProps, NavigatorScreenParams } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { Scope } from '../api/types';

/**
 * Movimientos: `vista: 'caja'` abre el flujo de caja con saldo acumulado y marca la línea pedida
 * (`corte` = caja actual, `finDeMes` = caja a fin de mes). `n` distingue toques repetidos sobre la misma card.
 */
export type MovimientosParams = { vista: 'lista' } | { vista: 'caja'; marca: 'corte' | 'finDeMes'; n: number };

export type TabParamList = {
  Inicio: undefined;
  Movimientos: MovimientosParams | undefined;
  Nuevo: undefined;
  Fijos: undefined;
  Presupuesto: undefined;
};

export type RootStackParamList = {
  Tabs: NavigatorScreenParams<TabParamList>;
  Reporte: { month?: string; scope?: Scope } | undefined;
  /** Movimientos que forman el real de una categoría del presupuesto vs. real en un mes al corte. */
  MovimientosCategoria: { categoryId: number; category: string; month: string; cutoff: string; scope: Scope };
  Tarjetas: undefined;
  Configuracion: undefined;
  TiposOperacion: undefined;
  Categorias: undefined;
  MediosPago: undefined;
  Ahorro: undefined;
  EditarMovimiento: { id: number };
};

export type RootStackScreenProps<T extends keyof RootStackParamList> = NativeStackScreenProps<RootStackParamList, T>;

export type TabScreenProps<T extends keyof TabParamList> = CompositeScreenProps<
  BottomTabScreenProps<TabParamList, T>,
  NativeStackScreenProps<RootStackParamList>
>;

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace ReactNavigation {
    // eslint-disable-next-line @typescript-eslint/no-empty-object-type
    interface RootParamList extends RootStackParamList {}
  }
}
