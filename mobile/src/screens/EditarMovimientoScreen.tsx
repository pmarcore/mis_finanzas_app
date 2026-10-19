import { useTransaction } from '../api/endpoints';
import { QueryState } from '../components/QueryState';
import type { RootStackScreenProps } from '../navigation/types';
import { MovimientoForm } from './NuevoMovimientoScreen';

/** Edición de un movimiento desde la lista o el flujo de caja; al guardar o borrar vuelve atrás. */
export function EditarMovimientoScreen({ navigation, route }: RootStackScreenProps<'EditarMovimiento'>) {
  const q = useTransaction(route.params.id);
  return (
    <QueryState isLoading={q.isLoading} error={q.error} onRetry={q.refetch}>
      {q.data && (
        <MovimientoForm
          key={q.data.id}
          initial={q.data}
          onSaved={() => navigation.goBack()}
          onDeleted={() => navigation.goBack()}
        />
      )}
    </QueryState>
  );
}
