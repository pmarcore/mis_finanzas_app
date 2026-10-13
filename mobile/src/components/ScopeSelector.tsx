import type { Scope } from '../api/types';
import { useScope } from '../state/scope';
import { useTheme } from '../theme';
import { Segmented } from './Segmented';

/** Familia / Memey, ligado al contexto global de ámbito. No hay vista total: los ámbitos no se suman. */
export function ScopeSelector() {
  const { scope, setScope } = useScope();
  const { colors } = useTheme();
  return (
    <Segmented<Scope>
      value={scope}
      onChange={setScope}
      options={[
        { value: 'familia', label: 'Familia', color: colors.familia },
        { value: 'memey', label: 'Memey', color: colors.memey },
      ]}
    />
  );
}
