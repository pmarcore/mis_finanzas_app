import type { ScopeView } from '../api/types';
import { useScope } from '../state/scope';
import { useTheme } from '../theme';
import { Segmented } from './Segmented';

/** Familia / Memey / Total, ligado al contexto global de ámbito. */
export function ScopeSelector() {
  const { scope, setScope } = useScope();
  const { colors } = useTheme();
  return (
    <Segmented<ScopeView>
      value={scope}
      onChange={setScope}
      options={[
        { value: 'familia', label: 'Familia', color: colors.familia },
        { value: 'memey', label: 'Memey', color: colors.memey },
        { value: 'total', label: 'Total', color: colors.ink },
      ]}
    />
  );
}
