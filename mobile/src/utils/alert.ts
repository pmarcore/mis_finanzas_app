import { Alert as RNAlert, Platform, type AlertButton } from 'react-native';

// En web Alert.alert de React Native no hace nada: ahí usamos confirm/alert del navegador.
// Un botón de acción (además de Cancelar) se vuelve un confirm; sin acciones, un aviso simple.
export const Alert = {
  alert(title: string, message?: string, buttons?: AlertButton[]) {
    if (Platform.OS !== 'web') return RNAlert.alert(title, message, buttons);
    const text = [title, message].filter(Boolean).join('\n\n');
    const cancel = buttons?.find((b) => b.style === 'cancel');
    const action = buttons?.find((b) => b.style !== 'cancel');
    if (!action) {
      window.alert(text);
      cancel?.onPress?.();
    } else if (window.confirm(text)) {
      action.onPress?.();
    } else {
      cancel?.onPress?.();
    }
  },
};
