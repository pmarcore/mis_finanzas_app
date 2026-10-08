# Importación desde la planilla

`planilla-2026-10.json` es la foto de octubre 2026 de la planilla "Finanzas familiares y Memey"
(Registro, Presupuesto, Tarjetas y Configuración), con corte al 08/10/2026. Es la misma base
que usó el prototipo aprobado, más el retiro de Memey del 08/10.

Qué trae:

- Saldos iniciales al 01/10: familia −$143.464 y Memey $0 (decisión de Pablo; la planilla dice $100).
- Cierres y vencimientos de las 4 tarjetas de septiembre a marzo (octubre vence el 9, el resto el 11; editables en Configuración).
- Categorías y presupuesto de octubre.
- 7 gastos fijos: sueldos, alquiler, alquiler cobrado, cuota del auto y seguro desde noviembre; Educación desde octubre, el 20 con Visa Santander.
- 59 movimientos de octubre y 21 compras en cuotas anteriores a octubre (solo proyectan cuotas, no consumen presupuesto).

Para cargarla en una base vacía, con la API corriendo:

```sh
curl -X POST http://localhost:5080/import/sheet \
  -H 'content-type: application/json' \
  --data @data/import/planilla-2026-10.json
```

Si la base ya tiene movimientos, la API responde 409. Con `?replace=true` borra movimientos, fijos,
presupuestos, categorías y ciclos de tarjeta y vuelve a cargar la foto.

`backend/tests/Finanzas.Tests/PlanillaOctubreTests.cs` importa este archivo y compara contra la planilla:
caja familiar al corte, saldo a fin de mes, reales del presupuesto y vencimientos de noviembre.
