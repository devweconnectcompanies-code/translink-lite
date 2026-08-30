# TRANSLINK — Documento Maestro de Producto y Arquitectura v0.3

**Estado:** Documento fuente de verdad para desarrollo<br>
**Producto activo:** TransLink-Lite<br>
**Stack principal:** ASP.NET Core / C# / .NET 10 / PostgreSQL / React / TypeScript / Vite / Manifest V3 / AWS<br>
**Principio técnico:** Diseñar para escala masiva, desplegar según demanda real.

---

## 1. Visión

TransLink es una plataforma de comunicación multilingüe en tiempo real orientada a eliminar la barrera del idioma en reuniones, llamadas, contenido multimedia y comunicaciones entre usuarios.

TransLink no se diseñará como un simple traductor. Debe evolucionar como un ecosistema completo de comunicación multilingüe.

### Ecosistema previsto

- TransLink-Lite
- TransLink-Pro
- TransLink Enterprise
- Web App
- Browser Extension
- Desktop App
- Mobile App
- API pública
- SDK para terceros

---

## 2. Objetivo inmediato de TransLink-Lite

El primer Alpha end-to-end debe demostrar este flujo real:

```text
YouTube / Google Meet / otra pestaña
        ↓
TransLink Extension detecta pestañas reales
        ↓
Usuario selecciona una pestaña
        ↓
Usuario selecciona idioma destino
        ↓
Usuario inicia captura
        ↓
Chrome captura audio real de esa pestaña
        ↓
Audio se transmite al backend
        ↓
ASP.NET Core recibe el stream
        ↓
AWS Transcribe Streaming
        ↓
Texto original
        ↓
AWS Translate
        ↓
Texto traducido
        ↓
TransLink Web recibe resultado
        ↓
Usuario ve traducción en tiempo real
```

### Métrica principal del Alpha

**End-to-End Translation Latency**

Objetivo de ingeniería:

```text
Target: ~0.8–1.0 s cuando las condiciones técnicas lo permitan
```

Debe medirse. No se asumirá ni prometerá sin pruebas.

---

## 3. Principio de escalabilidad

TransLink debe construirse bajo esta regla:

> Diseñar para millones. Desplegar para la demanda actual.

Esto implica:

- servicios stateless cuando sea razonable;
- escalamiento horizontal;
- I/O asíncrono;
- bounded buffers;
- backpressure;
- idempotencia;
- aislamiento de fallos;
- observabilidad;
- contratos versionables;
- separación clara de responsabilidades;
- evitar estado global en memoria de una sola instancia;
- evitar dependencias arquitectónicas que obliguen a reescribir el núcleo al crecer.

No significa desplegar hoy infraestructura para millones de usuarios.

---

## 4. Arquitectura objetivo de TransLink-Lite

```text
Extension / Web App
        ↓
HTTPS / WSS
        ↓
TransLink Lite Backend
        ↓
PostgreSQL
        ↓
Realtime / Translation Pipeline
        ↓
AWS Transcribe
        ↓
AWS Translate
        ↓
AWS Polly (fase TTS)
```

### Regla

Extension y Web consumen el mismo backend lógico.

No se crearán dos APIs independientes.

---

## 5. Estrategia arquitectónica

### Modular Monolith First

La primera etapa continuará como un backend modular.

No se introducirán microservicios solo por anticipar escala.

Se diseñarán límites de dominio que permitan extraer componentes cuando exista evidencia operativa.

Módulos previstos:

- Identity
- Users
- TranslationSessions
- AudioIngestion
- Transcription
- Translation
- Realtime
- TTS
- Calls
- Usage
- Billing
- Organizations
- Notifications

---

## 6. CURRENT IMPLEMENTATION BASELINE

Este apartado describe lo que existe realmente hoy en el repositorio, no la arquitectura futura.

### 6.1 Backend real

Actualmente existen cuatro proyectos .NET 10:

```text
TransLink.Lite.Domain
TransLink.Lite.Application
TransLink.Lite.Infrastructure
TransLink.Lite.API
```

Características reales:

- ASP.NET Core
- .NET 10
- Nullable reference types
- Implicit usings
- PostgreSQL mediante Npgsql / EF Core
- JWT Bearer
- BCrypt
- Swagger/OpenAPI en Development
- Usuarios
- Perfil
- TranslationSession como metadata

### 6.2 Arquitectura real actual

La separación física por capas existe, pero todavía no existe Clean Architecture completa.

Actualmente:

- Domain contiene entidades anémicas.
- Application contiene DTOs y contratos.
- Infrastructure contiene EF Core, JWT y BCrypt.
- API contiene gran parte de la orquestación.
- Controladores acceden directamente a AppDbContext.
- No existen repositorios ni servicios de aplicación.
- No existen casos de uso/handlers formales.
- No existen configuraciones IEntityTypeConfiguration.

### 6.3 Funcionalidad implementada

Autenticación:

- Register
- Login
- JWT
- BCrypt

Usuarios:

```text
GET  /api/Users
POST /api/Users
GET  /api/Users/me
PUT  /api/Users/me
```

Sesiones:

```text
POST /api/TranslationSessions
GET  /api/TranslationSessions
GET  /api/TranslationSessions/{id}
```

### 6.4 Aún NO existe

- AWS Transcribe
- AWS Translate
- AWS Polly
- WebSocket realtime
- Audio streaming
- Pipeline de traducción
- Observabilidad externa
- Redis/cache distribuido
- Colas
- CI
- Tests automatizados
- Integración cloud

---

## 7. Problemas técnicos prioritarios detectados

### P0 — Seguridad y repositorio

- secretos PostgreSQL versionados;
- JWT signing key versionada;
- no existe `.gitignore`;
- `bin/` y `obj/` están rastreados por Git;
- credenciales expuestas deben considerarse comprometidas y rotarse.

### P0 — Autorización

Actualmente un usuario autenticado puede listar todos los usuarios y crear usuarios.

Esto no puede permanecer así.

### P0 — Integridad de datos

Faltan:

- FK entre TranslationSession y User;
- índice sobre TranslationSession.UserId;
- índice único de Users.Email;
- límites de strings;
- restricciones de estado e idiomas.

### P1 — Validación

Falta validación robusta de:

- email;
- longitud;
- password;
- idioma;
- strings vacíos;
- normalización de email.

### P1 — Arquitectura

Los controladores contienen demasiada lógica y acceden directamente a EF Core.

### P1 — Calidad

No existen:

- unit tests;
- integration tests;
- API tests;
- CI;
- Testcontainers.

### P1 — Configuración

No existe configuración de producción documentada ni SDK fijado con `global.json`.

### P1 — Build reproducible

Aún no se ha certificado un `restore/build/test` limpio reproducible.

---

## 8. Seguridad obligatoria

### 8.1 Secretos

Nunca almacenar secretos en:

- código fuente;
- extensión;
- Web App;
- appsettings versionados;
- repositorio Git.

Usar:

- environment variables;
- .NET User Secrets en desarrollo;
- AWS Secrets Manager / Parameter Store en entornos cloud cuando corresponda.

Toda credencial expuesta debe rotarse.

### 8.2 Base de datos

Extension y Web App nunca se conectan directamente a PostgreSQL.

No existe concepto de "clave pública de DB" para clientes TransLink.

Toda operación de datos pasa por backend autenticado y autorizado.

### 8.3 RLS

PostgreSQL RLS podrá evaluarse como defensa adicional cuando el modelo multi-tenant lo justifique.

Nunca sustituye la autorización server-side.

### 8.4 Server-side authorization

Cada petición debe validar:

- identidad;
- permisos;
- ownership;
- organización/tenant cuando exista.

Nunca confiar en controles visuales del frontend.

### 8.5 Passwords

- BCrypt actualmente.
- Mantener algoritmo seguro.
- Nunca almacenar password plano.
- Aplicar políticas razonables de longitud y seguridad.

### 8.6 Rate limiting

Aplicar límites a:

- login;
- registro;
- endpoints costosos;
- WebSockets;
- traducción;
- uso por plan.

### 8.7 Bots

Aplicar protección adaptativa en endpoints expuestos a automatización abusiva.

No agregar CAPTCHA indiscriminadamente a cada flujo.

### 8.8 Input validation

Validar en backend:

- tipo;
- formato;
- longitud;
- rango;
- tamaño.

### 8.9 Mass Assignment

Nunca bindear directamente entidades de dominio desde requests.

Usar DTOs explícitos y campos permitidos.

### 8.10 XSS

Todo contenido del usuario deberá renderizarse de forma segura.

Aplicar CSP y encoding apropiado.

### 8.11 Files

Cuando exista upload:

- MIME real;
- extensión;
- tamaño;
- malware scanning según riesgo;
- nombre generado;
- almacenamiento aislado.

### 8.12 API minimization

- retornar únicamente campos necesarios;
- paginar colecciones;
- no exponer PasswordHash ni datos internos.

### 8.13 HTTP security

Producción deberá usar:

- HTTPS obligatorio;
- HSTS;
- CSP;
- protección frame-ancestors / clickjacking;
- CORS restrictivo.

### 8.14 Dependencies

Automatizar auditoría:

- dotnet package vulnerability scanning;
- npm audit;
- Dependabot u otra herramienta equivalente;
- actualización controlada de dependencias.

### 8.15 Logging

Nunca registrar:

- passwords;
- JWT completo;
- claves;
- audio raw;
- secretos;
- PII innecesaria.

Acceso a logs deberá estar restringido.

### 8.16 AWS IAM

Aplicar mínimo privilegio.

Un servicio solo debe tener permisos para los recursos AWS que necesita.

### 8.17 WebSocket security

- WSS;
- autenticación;
- autorización de TranslationSession;
- límites de mensaje;
- heartbeat;
- timeout;
- backpressure;
- validación de origen cuando aplique.

### 8.18 Audio

Por defecto:

```text
capture → process → transcribe → discard
```

No almacenar audio salvo función explícita con consentimiento.

---

## 9. Persistencia y PostgreSQL

PostgreSQL será el datastore transaccional principal.

No usar PostgreSQL como:

- buffer de audio;
- message bus realtime;
- almacenamiento por chunk.

Persistir:

- Users
- TranslationSessions
- configuración
- historial final cuando corresponda
- uso
- auditoría relevante

---

## 10. Backend y Clean Architecture objetivo

```text
Domain
  ↑
Application
  ↑
Infrastructure
  ↑
API
```

### Domain

No depender de:

- EF Core
- AWS
- HTTP
- ASP.NET Core

### Application

Contendrá:

- casos de uso;
- contratos;
- validación;
- orquestación de negocio;
- DTOs.

### Infrastructure

Contendrá:

- PostgreSQL;
- repositorios;
- AWS;
- implementaciones externas.

### API

Contendrá:

- endpoints;
- auth HTTP;
- WebSocket endpoint;
- middleware;
- DI;
- transporte.

La API no deberá convertirse en la capa de negocio.

---

## 11. Async y performance

En rutas I/O:

- `async/await`
- `CancellationToken`
- streaming
- `IAsyncEnumerable` cuando aporte valor

Evitar en rutas críticas:

- `.Result`
- `.Wait()`
- `Thread.Sleep()`

---

## 12. Realtime y audio

El audio debe procesarse en streaming.

No esperar archivos completos.

Pipeline conceptual:

```text
Audio
↓
Chunks
↓
Audio Ingestion
↓
AWS Transcribe Streaming
↓
Partial / Final transcript
↓
AWS Translate
↓
Realtime Gateway
↓
Web App
```

Tamaño inicial de chunk: aproximadamente 100–200 ms.

Este valor será configurable y validado mediante benchmark.

---

## 13. Backpressure

No permitir colas infinitas.

Implementar:

- bounded buffers;
- límites;
- timeouts;
- cancelación;
- métricas de saturación;
- descarte controlado cuando corresponda.

---

## 14. Observabilidad

Desde primeras versiones:

### Logs

Estructurados.

### Metrics

- sesiones activas;
- conexiones WebSocket;
- errores;
- latencia;
- reconnects;
- STT latency;
- translation latency;
- request rate;
- saturación;
- costo estimado.

### Tracing

Cada operación debe poder correlacionarse mediante:

- TraceId
- UserId
- TranslationSessionId
- ConnectionId

---

## 15. Extension

La Browser Extension será thin client.

Sí hace:

- login;
- detectar tabs;
- seleccionar tab;
- seleccionar idioma;
- capturar audio;
- detectar audio;
- transmitir;
- mostrar estado;
- reconectar;
- detener;
- abrir Web App.

No hace:

- historial completo;
- notas;
- PDF;
- Word;
- reglas de negocio;
- AWS directo;
- billing.

---

## 16. Web App

Será la interfaz principal.

Funciones previstas:

- traducción realtime;
- historial;
- notas;
- TTS;
- exportaciones;
- sesiones;
- configuración;
- llamadas internas.

---

## 17. Llamadas internas Lite

TransLink-Lite incorporará llamadas internas entre usuarios registrados.

Primera versión:

- voz;
- sin video;
- traducción bidireccional;
- texto;
- TTS;
- límite comercial reducido.

No forman parte del camino crítico del primer Alpha.

---

## 18. TransLink-Pro

Evolución:

- más participantes;
- reuniones nativas;
- voz;
- video;
- screen sharing;
- chat;
- organizaciones;
- administración;
- IA;
- integraciones;
- controles empresariales.

Lite no debe introducir restricciones que impidan esta evolución.

---

## 19. ADR iniciales

- ADR-001 — Backend lógico único.
- ADR-002 — ASP.NET Core / C#.
- ADR-003 — PostgreSQL datastore transaccional.
- ADR-004 — Extension thin client.
- ADR-005 — Web App interfaz principal.
- ADR-006 — Audio streaming realtime.
- ADR-007 — Audio no persistente por defecto.
- ADR-008 — Modular Monolith First.
- ADR-009 — Horizontal scalability.
- ADR-010 — AWS cloud inicial.
- ADR-011 — No direct DB access from clients.
- ADR-012 — Security-first baseline before realtime pipeline.

---

## 20. Roadmap inmediato

### BASELINE-001 — Repository & Backend Hardening

Primero:

- limpiar repositorio;
- secretos;
- Git;
- configuración;
- autorización;
- integridad DB;
- validación;
- arquitectura base;
- tests;
- build reproducible;
- documentación.

### EXT-001A — Chrome Integration Foundation

Después:

- tabs reales;
- Manifest V3;
- chrome.storage;
- selección real;
- idioma persistente.

### EXT-001B — Real Tab Audio Capture

Después:

- tabCapture;
- audio lifecycle;
- AudioContext/Worklet/MediaRecorder según decisión;
- detección de audio;
- cleanup.

### RT-001 — Realtime Audio Transport

- WSS;
- authentication;
- TranslationSession;
- binary audio;
- reconnect;
- backpressure.

### AWS-STT-001

- AWS Transcribe Streaming.

### AWS-TR-001

- AWS Translate.

### WEB-RT-001

- Web recibe y renderiza traducción realtime.

---

## 21. Definition of Done — Alpha

Alpha completo cuando sea reproducible:

1. login;
2. tabs reales;
3. seleccionar tab;
4. idioma destino;
5. iniciar captura;
6. audio real;
7. backend;
8. JWT;
9. TranslationSession;
10. AWS Transcribe;
11. transcript;
12. AWS Translate;
13. Web realtime;
14. detener;
15. cleanup;
16. reconexión básica;
17. latencia instrumentada.

---

## 22. Reglas para Codex

Antes de cualquier cambio relevante:

1. leer este documento;
2. inspeccionar código existente;
3. no inventar requisitos;
4. presentar plan breve;
5. implementar solo la tarea asignada;
6. mantener arquitectura;
7. no exponer secretos;
8. no agregar dependencias sin necesidad;
9. mantener I/O async;
10. agregar pruebas donde corresponda;
11. ejecutar build/tests/lint;
12. documentar cambios;
13. no hacer commits hasta aprobación;
14. no modificar ADRs sin autorización;
15. reportar contradicciones antes de resolverlas unilateralmente.

---

## 23. Flujo de trabajo oficial

```text
SPEC
↓
Plan
↓
Codex
↓
Build/Test
↓
Review
↓
Commit
↓
Documentation update
↓
Next task
```

Este documento es la fuente de verdad de producto y arquitectura de TransLink mientras no exista una versión posterior aprobada.
