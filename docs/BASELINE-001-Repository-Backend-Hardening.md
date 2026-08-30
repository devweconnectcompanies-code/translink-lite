# BASELINE-001 — Repository & Backend Hardening

**Proyecto:** TransLink-Lite<br>
**Prioridad:** P0/P1<br>
**Estado:** Completed and published (2026-08-30)<br>
**Objetivo:** dejar el repositorio y backend en una base segura, reproducible y mantenible antes de implementar audio realtime y AWS.

---

## 1. Regla de ejecución

Codex NO debe implementar esta tarea completa de una sola vez sin presentar primero un plan.

Primero:

1. leer `DOCS/TRANSLINK-MASTER.md`;
2. inspeccionar estado actual;
3. verificar Git root;
4. verificar archivos rastreados;
5. identificar secretos;
6. presentar plan por subfases;
7. esperar aprobación antes de cambios destructivos o de historial Git.

No ejecutar migraciones contra una base real sin autorización.

No reescribir historial Git sin autorización explícita.

No hacer commit.

---

## 2. Objetivos

### Fase A — Git hygiene

- crear `.gitignore` apropiado para .NET, IDEs y frontend;
- sacar `bin/` y `obj/` del tracking sin eliminar fuentes;
- revisar archivos generados versionados;
- reportar cualquier otro artefacto innecesario.

### Fase B — Secret management

- identificar credenciales versionadas;
- retirar secretos de archivos versionados;
- preparar estrategia de configuración local segura;
- documentar variables necesarias;
- usar User Secrets / variables de entorno para Development;
- no introducir AWS Secrets Manager todavía si no existe infraestructura;
- indicar exactamente qué credenciales deben rotarse.

IMPORTANTE:
Las credenciales ya versionadas deben considerarse comprometidas.
Codex no debe inventar ni generar credenciales reales.

### Fase C — Repository structure

Analizar:

- Git root actual;
- solución raíz vacía;
- solución funcional dentro de `backend`.

Proponer una estructura final coherente para:

```text
TransLink-Lite/
├── API/
├── APP/
├── DOCS/
└── ...
```

No mover `.git` ni reestructurar masivamente hasta aprobación.

La prioridad es preservar historial y evitar romper paths.

### Fase D — SDK reproducible

- agregar `global.json` apropiado para .NET 10;
- evitar fijar una patch inexistente en otros entornos si no es necesario;
- documentar SDK requerido.

### Fase E — Authorization hardening

Revisar:

```text
GET /api/Users
POST /api/Users
```

No deben quedar disponibles para cualquier usuario autenticado.

Proponer una de estas estrategias:

- eliminar endpoints si no son necesarios;
- restringirlos mediante roles/policies;
- moverlos a administración futura.

No inventar sistema completo de roles sin aprobación.

### Fase F — Input validation

Agregar validación server-side para:

- email;
- FirstName;
- LastName;
- PreferredLanguage;
- password;
- TranslationSession fields.

Normalizar email:

- trim;
- canonicalización consistente;
- comparación case-insensitive mediante estrategia adecuada.

Evitar mass assignment.

### Fase G — PostgreSQL integrity

Preparar cambios de modelo para:

- FK `TranslationSessions.UserId -> Users.Id`;
- índice en `TranslationSessions.UserId`;
- unique index en `Users.Email`;
- límites de longitud apropiados;
- configuraciones EF mediante `IEntityTypeConfiguration`;
- restricciones coherentes.

No aplicar migración sin aprobación.

Crear migración solo cuando la modificación del modelo esté aprobada.

### Fase H — Application separation

Reducir acceso directo de controllers a `AppDbContext`.

Introducir casos de uso/servicios de aplicación de forma incremental.

No crear una capa de repositorios genéricos artificiales.

Preferir abstracciones específicas donde aporten valor.

Objetivo:

```text
Controller
↓
Application use case/service
↓
Abstraction
↓
Infrastructure
```

### Fase I — Global error handling

Crear contrato consistente para errores HTTP.

Usar mecanismo moderno de ASP.NET Core apropiado para .NET 10.

No filtrar:

- stack traces;
- detalles internos;
- secretos.

### Fase J — Tests

Crear proyecto(s) de tests.

Prioridad inicial:

- register;
- login;
- email duplicate;
- `/me`;
- session ownership;
- unauthorized access;
- validation;
- authorization de Users;
- modelo/integridad cuando sea posible.

Evaluar Testcontainers para integración PostgreSQL.

No introducirlo si el entorno actual impide ejecutarlo; documentar decisión.

### Fase K — Health checks

Separar:

- liveness;
- readiness.

Readiness debe poder comprobar dependencias esenciales como PostgreSQL cuando corresponda.

### Fase L — Build reproducible

Conseguir:

```text
dotnet restore
dotnet build
dotnet test
```

sin errores.

Si NuGet continúa bloqueado:

- investigar causa;
- no desactivar validaciones de seguridad;
- documentar bloqueo real;
- no declarar build limpio sin evidencia.

---

## 3. Seguridad obligatoria

Durante BASELINE-001:

- ningún secreto nuevo en Git;
- ningún password en logs;
- ningún JWT completo en logs;
- no exponer PasswordHash;
- server-side authorization;
- DTOs explícitos;
- CORS no debe abrirse indiscriminadamente;
- HTTPS en producción;
- preparar rate limiting para fase posterior o incluir login/register si encaja sin sobreingeniería.

---

## 4. No hacer todavía

NO implementar durante BASELINE-001:

- Chrome Extension integration;
- tabCapture;
- WebSockets;
- AWS Transcribe;
- AWS Translate;
- Polly;
- Redis;
- queues;
- microservices;
- Kubernetes;
- billing;
- calls.

---

## 5. Criterios de aceptación

BASELINE-001 se considera terminado cuando:

- `.gitignore` existe y funciona;
- `bin/obj` dejan de estar rastreados;
- secretos dejan de estar en archivos versionados;
- credenciales expuestas están identificadas para rotación;
- configuración Development es segura y documentada;
- SDK .NET está fijado de forma reproducible;
- endpoints de Users no están abiertos a cualquier autenticado;
- DTOs tienen validación server-side;
- emails se normalizan;
- DB model tiene FK/índices/unique email aprobados;
- controladores críticos ya no concentran toda la lógica;
- existe manejo global de errores;
- existe una base de tests;
- health checks son útiles;
- `dotnet restore/build/test` puede verificarse o existe un bloqueo técnico claramente documentado;
- documentación refleja el estado real;
- no se implementó funcionalidad fuera de alcance.

---

## 6. Primera instrucción a Codex

Realiza únicamente la fase de planificación de `BASELINE-001`.

No modifiques archivos todavía.

Entrega:

1. lista exacta de archivos afectados por cada subfase;
2. orden recomendado;
3. riesgos;
4. comandos Git que propones;
5. cambios de configuración;
6. cambios de modelo de datos;
7. estrategia de pruebas;
8. cualquier decisión que requiera aprobación.

Después de revisar ese plan se autorizará la primera subfase.

---

## 7. Completion record

`BASELINE-001` was executed incrementally, reviewed by subphase, published to `main`, and validated by a real GitHub Actions run. The original specification above is retained as the historical execution contract.

Completed outcomes:

- Git hygiene removed generated build artifacts from tracking and established repository-wide ignores.
- Tracked current-source secrets were removed; exposed PostgreSQL and JWT credentials were rotated; local development uses .NET User Secrets.
- `global.json` pins SDK `10.0.300` with compatible patch servicing within the `10.0.3xx` feature band.
- The vulnerable transitive `Microsoft.OpenApi 2.4.1` dependency was removed through a compatible Swashbuckle update.
- Users collection endpoints were removed; profile and session access is authenticated and ownership-scoped.
- Validation, normalized email handling, DTO boundaries, PostgreSQL constraints, and specific EF Core configurations are implemented.
- Controllers are thin adapters over application services and specific repository abstractions.
- Global `ProblemDetails`, authentication rate limiting, and separate liveness/readiness probes are implemented.
- The approved automated baseline is 30 unit tests plus 26 PostgreSQL/Testcontainers integration tests (56 total, none skipped).
- GitHub Actions restores, audits dependencies, builds Release, and runs all tests; the first published run succeeded.
- The approved top-level `API`, `APP`, `Tests`, `docs`, and root-solution layout is in place with history preserved.
- Documentation was reconciled in `BASELINE-001N`.

Intentionally deferred: realtime audio, WebSockets, AWS services, Extension integration, distributed infrastructure, billing, calls, and enterprise capabilities. The next milestone is `EXT-001`, beginning with `EXT-001A — Chrome Integration Foundation`.
