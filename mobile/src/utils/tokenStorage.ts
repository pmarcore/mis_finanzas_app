import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

// Dónde queda guardada la sesión: en el celular, en el almacenamiento seguro del sistema;
// en la web (PWA), en el almacenamiento del navegador de ese celular.
const KEY = 'finanzas.session';

export interface StoredSession {
  token: string;
  username: string;
  displayName: string;
}

export async function loadSession(): Promise<StoredSession | null> {
  try {
    const raw = Platform.OS === 'web' ? window.localStorage.getItem(KEY) : await SecureStore.getItemAsync(KEY);
    return raw ? (JSON.parse(raw) as StoredSession) : null;
  } catch {
    return null;
  }
}

export async function saveSession(session: StoredSession | null): Promise<void> {
  try {
    if (Platform.OS === 'web') {
      if (session) window.localStorage.setItem(KEY, JSON.stringify(session));
      else window.localStorage.removeItem(KEY);
    } else if (session) await SecureStore.setItemAsync(KEY, JSON.stringify(session));
    else await SecureStore.deleteItemAsync(KEY);
  } catch {
    // Sin almacenamiento (ventana privada, etc.): la sesión dura hasta cerrar la app.
  }
}
