# Documentación técnica

App de finanzas personales para la familia y Memey: backend .NET 10 con SQLite y app mobile Expo (React Native).
Los diagramas están en Mermaid y GitHub los muestra dibujados.

| Documento | Contenido |
| --- | --- |
| [Arquitectura](arquitectura.md) | Vista general, capas, flujo de un movimiento con tarjeta, despliegue del ambiente de pruebas, reglas de negocio y dónde viven, endpoints de la API, integración con Invertir Online |
| [Base de datos](base-de-datos.md) | DER, convenciones, diccionario de tablas, enums, datos iniciales y migraciones |
| [Clases del backend](clases-backend.md) | Proyectos y dependencias, diagramas de clases de entidades, reglas de dominio, infraestructura y API |
| [App mobile](app-mobile.md) | Componentes, navegación, pantallas y los endpoints que usa cada una |
| [Tecnologías y versiones](tecnologias.md) | Frameworks, librerías y herramientas con sus versiones |
| [Desarrollo, pruebas y CI](desarrollo.md) | Estructura del repo, convenciones, tests, CI, cambios en la base y pendientes conocidos |

Otros documentos del repo:

- [README](../README.md): cómo correr backend, app y ambiente de pruebas.
- [mobile/README.md](../mobile/README.md): estructura y contrato de la app con el backend.
- [data/import/README.md](../data/import/README.md): formato de la foto de la planilla.
