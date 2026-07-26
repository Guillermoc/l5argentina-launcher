# CLAUDE.md — guía para agentes en este repo

Launcher WPF (.NET Framework 4.8, SDK-style) para la comunidad L5Argentina. Lanza la app
**Sun and Moon**, sincroniza la base de cartas/imágenes desde un bucket Cloudflare R2 e incluye
un Buscador de cartas y un editor de Mazos portados de la app Android.

---

## 1. Arranque de sesión

**Leer siempre al empezar una conversación nueva:**

1. [`docs/index/OVERVIEW.md`](docs/index/OVERVIEW.md) — estado actual, mapa de archivos, datos rápidos.
2. [`docs/ROADMAP.md`](docs/ROADMAP.md) — qué falta y cuál es el foco.

Con eso alcanza para responder qué es el proyecto y cómo está. Todo lo demás se lee **bajo demanda**,
según la tabla de abajo.

## 2. Índice del proyecto — qué leer según la tarea

> **Regla: leer el índice del dominio ANTES que el código fuente.** Los índices dicen dónde está cada
> cosa y qué NO hay que romper; el código no.

| Si la tarea toca… | Leer primero |
|---|---|
| Manifest, descargas, hash, aplicar base, packs de imágenes, config | [`docs/index/sincronizacion.md`](docs/index/sincronizacion.md) |
| Buscador, motor de filtros, catálogo, favoritas/listas/presets | [`docs/index/buscador.md`](docs/index/buscador.md) |
| Mazos, `DeckRepository`, `.txt` de la carpeta `Decks` | [`docs/index/mazos.md`](docs/index/mazos.md) |
| Ventanas, XAML, tema, textos de UI | [`docs/index/ui.md`](docs/index/ui.md) |
| Build, self-test, CI, release, firma | [`docs/index/build-verificacion.md`](docs/index/build-verificacion.md) |
| Cualquier cosa con red, descarga o escritura en disco | + [`docs/design/seguridad.md`](docs/design/seguridad.md) |
| Cualquier cosa portada de la app Android | + [`docs/design/paridad-app-android.md`](docs/design/paridad-app-android.md) |
| Agregar estado/archivos nuevos del usuario | + [`docs/design/persistencia.md`](docs/design/persistencia.md) |
| "¿Por qué está hecho así?" | [`docs/DECISIONS.md`](docs/DECISIONS.md) |
| Entender el proyecto entero de corrido | [`docs/technical/technical-specification.md`](docs/technical/technical-specification.md) |
| Intención original de una feature / semántica exacta de la app Android | [`docs/functional/`](docs/functional/) (incluye los paquetes de handoff: specs + fuentes Kotlin) |
| Golden tests, sus datos y el harness para regenerarlos | [`tests/`](tests/) |
| Formato del `manifest.json` (bucket y pool) | [`docs/technical/contratos/`](docs/technical/contratos/) |

**Las specs de `docs/functional/` describen la intención, no el estado.** Sus consideraciones
técnicas son **sugerencias, no instrucciones**: son corazonadas de alguien que no tenía el código
delante. Evaluá el enfoque vos contra el código real y proponé otro si conviene.

## 3. Build / run / test

```sh
dotnet build src/L5ArgentinaLauncher.csproj -c Debug
dotnet run  --project src/L5ArgentinaLauncher.csproj
dotnet build src/L5ArgentinaLauncher.csproj -c Release          # exe único en bin/Release/net48
src/bin/Debug/net48/L5ArgentinaLauncher.exe --selftest out.txt  # verificación (solo DEBUG)
src/bin/Debug/net48/L5ArgentinaLauncher.exe --buscador          # abre el Buscador directo (solo DEBUG)
src/bin/Debug/net48/L5ArgentinaLauncher.exe --mazos             # abre el editor de Mazos directo (solo DEBUG)
```

- El exe es **WinExe**: desde PowerShell hay que esperarlo con `Start-Process -Wait` para leer el
  exit code del selftest.
- Hay SDK global (`C:\Program Files\dotnet`) y uno user-local en
  `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`; cualquiera compila. CI usa `8.0.x`.
- `nuget.config` (raíz) fija nuget.org explícitamente porque el config global estaba vacío.

## 4. Reglas de código y arquitectura (no negociables)

1. **Solo datos, nunca código.** Sin auto-update del exe, sin plugins, sin WebView.
2. **SHA-256** de todo lo descargado, verificado antes de usar (`DownloadService`).
3. **HTTPS siempre.** `file://`/`localhost` solo bajo `#if DEBUG` (`ManifestService.EnsureAllowedScheme`).
4. **Confinamiento al origen del manifest** (`ResolveFileUri` / `ResolveSameOriginUrl`): nunca seguir
   URLs de terceros ni escapar del directorio del manifest.
5. **Zip-slip** obligatorio al extraer (`ImagePackService`).
6. **Link de update hardcodeado** (`AppConstants.ReleasesPageUrl`), nunca del manifest.
7. **Paridad con la app Android**: la semántica portada (`CardSearchEngine`, `CatalogImporter`,
   `DeckRepository`) no se "arregla". Los bugs conocidos se portan; la UI compensa.
8. **Textos de UI solo en `Strings.resx`** (`{loc:Loc Clave}` / `Strings.Get`). Cero literales.
9. **Rutas de estado solo desde `AppConstants`** (respetan `L5A_DATA_ROOT`).

## 5. Cuándo correr la batería de verificación

Medido en esta máquina: `dotnet build -c Debug` incremental ≈ **1 s**, `--selftest` ≈ **3 s**. El
costo no es el reloj: es el ida y vuelta (compilar → lanzar el exe → leer un reporte de ~180 líneas)
repetido varias veces dentro de la misma tarea. Por eso la verificación se **agrupa al final**, no se
espolvorea.

**Durante el desarrollo → compilar sí, selftest no.**
- Después de tocar C#/XAML: `dotnet build -c Debug`. Es barato y atrapa lo que rompe de verdad.
- **No** correr `--selftest` "por las dudas" después de cada edición, ni para ver si "sigue todo bien".

**Al cerrar un desarrollo → `--selftest` obligatorio.** Cuenta como cierre cualquiera de estas:
- el usuario dice que terminamos, o pasa a otro tema;
- se va a commitear, taggear o publicar;
- se terminó un cambio en el **área crítica** —red/descarga/extracción, `CardSearchEngine`,
  `CatalogImporter`, `DeckRepository`, `SelfTest.cs`, `AppConstants`, o mover `docs/functional/*`—:
  **una sola corrida al terminar ese cambio**, no una por edición.

**Nunca hace falta** para cambios que no tocan código compilado: documentación, README, comentarios,
workflows. Para textos del `.resx` alcanza con compilar.

**Si no se corrió, se dice.** Nunca reportar "listo" o "verificado" sin haberlo corrido. La frase
correcta es: *"compilé; no corrí el selftest, corresponde al cerrar"*.

**Al correrlo, reportar poco:** la línea `RESULTADO: N PASS, M FAIL` y, si hay rojos, solo las líneas
`FAIL`. No volcar el reporte entero. Un resultado válido es **0 FAIL y ningún SKIP** (un SKIP
significa que no encontró las fixtures de `tests/fixtures/`). El total de checks **no es una
constante**: sube a medida que la batería crece (hoy 177).

**Red de seguridad:** `.github/workflows/build.yml` corre el selftest en cada push y PR a `main`, así
que saltear una corrida local nunca llega a producción sin ser detectado.

### 5.1 Qué se agrega a la batería (y qué no)

Son dos mitades con reglas opuestas:

| Mitad | De dónde sale | Quién la toca |
|---|---|---|
| **Golden (78: 53 + 25)** | generados con la lógica real de la app Android | **nadie a mano**; solo se regeneran con el harness de `tests/fixtures/*/generator/` |
| **Checks propios (el resto)** | escritos en `src/SelfTest.cs` | crecen con el proyecto |

**Sumá un check propio** cuando el cambio toca:
- escritura sobre la instalación o el estado del usuario (bases, backups, packs, `.txt` de mazos,
  config, stores);
- validación de algo que viene de la red (hash, esquema, origen, entradas de zip);
- lógica con semántica no obvia y verificable de a una (comparar versiones, parsers, resolución de
  nombres, round-trip de persistencia).

**No sumes checks** para UI/XAML/layout, textos del `.resx`, wiring de eventos ni nada que ya atrape
el compilador: el harness es un test de **pipeline**, no un framework de UI. Inflarlo lo vuelve caro
de leer y de mantener sin agregar señal.

**Nunca agregues golden a mano.** Una feature que la app Android no tiene (duplicar mazo, orden por
cabecera, presets) se cubre con checks propios. Los golden solo se regeneran cuando cambia la app o
cuando se decide cambiar una semántica portada — y eso requiere confirmación (§7).

### 5.2 Cuándo se toca o se da de baja un check

- **Cambió el comportamiento a propósito** → actualizá el check al comportamiento nuevo, en el mismo
  cambio. Dejarlo rojo "por ahora" no es una opción.
- **Desapareció la funcionalidad** → borrá el check y decilo explícitamente en el reporte (el total
  baja, y eso está bien).
- **El check quedó desnaturalizado** (sigue verde pero ya no prueba lo que decía: cambió el flujo,
  quedó tautológico) → reescribilo o borralo, no lo dejes ahí dando falsa confianza. Vale decirlo
  como hallazgo aunque no sea parte del pedido.
- **Un golden en rojo** → nunca editar el `expected` a mano. O el cambio no era intencional (se
  arregla el código), o sí lo era y hay que regenerar con el harness y anotarlo en `DECISIONS.md` y
  en la doc de paridad.
- **Nunca** borrar, aflojar ni comentar un check para llegar al verde. Si un check molesta, la
  conversación es sobre el check, no un `//`.

Si cambió la cantidad de checks, actualizá los números en los docs que la citan (§8).

## 6. Cómo responder

- **Español**, directo, sin relleno ni resúmenes de lo que ya se dijo.
- Cambios de código: qué cambió y por qué, en pocas líneas. Nada de pegar el diff entero.
- Si algo no se verificó, **decirlo explícito** (ver §5).
- Referenciar archivos como `ruta:línea`, no describir dónde queda algo.
- No inventar estado del proyecto: si no está en los índices ni en el código, se busca o se pregunta.

## 7. Confirmación obligatoria antes de actuar

Parar y esperar un sí explícito para:

- **Implementar una feature no trivial sin spec funcional**: resumir el enfoque propuesto (qué
  archivos, qué estructura, qué riesgos) y **esperar confirmación** antes de escribir código.
- **Cambiar una semántica cubierta por goldens** (Buscador o deckbuilder): implica regenerar
  expected con el harness. Nunca "ajustar" el golden para que pase.
- **Borrar checks del selftest** o bajar la cobertura de un área crítica (§5.2).
- **Tocar cualquiera de las reglas de §4**, incluido relajar validaciones de origen, hash o zip-slip.
- **Commitear, pushear, taggear o publicar un release.**
- **Bumpear la versión** (`AppConstants.LauncherVersion` + `<Version>` del csproj).
- **Borrar o mover archivos del usuario** en pruebas manuales (usar `L5A_DATA_ROOT`, nunca
  `%APPDATA%` real).
- **Reestructurar carpetas de `docs/`**, o mover/renombrar `tests/fixtures/*` (rompe las rutas que
  `SelfTest.cs` resuelve para los golden tests).

## 8. Después de cambiar código — qué índice actualizar

Al terminar un cambio, actualizar el índice correspondiente **en el mismo trabajo**, no después:

| Si cambiaste… | Actualizar |
|---|---|
| Servicios de red/descarga/archivos, `LauncherEngine`, modelos de manifest | `docs/index/sincronizacion.md` |
| `Services/Search/*`, `SearchWindow`, `CardCatalog` | `docs/index/buscador.md` |
| `Services/Deck/*`, `DeckWindow`, `DeckModels` | `docs/index/mazos.md` |
| Ventanas, `Theme.xaml`, `CardUiStyles.xaml`, `Strings.resx` | `docs/index/ui.md` |
| csproj, `SelfTest.cs`, workflows, flags de debug | `docs/index/build-verificacion.md` |
| Rutas/archivos de estado del usuario | `docs/design/persistencia.md` + `docs/index/OVERVIEW.md` |
| Reglas de seguridad o su verificación | `docs/design/seguridad.md` |
| Una divergencia nueva respecto de la app Android | `docs/design/paridad-app-android.md` + índice del dominio |
| **Cantidad de checks del selftest** (agregaste o borraste checks) | `docs/index/build-verificacion.md`, `docs/index/mazos.md`, `docs/index/OVERVIEW.md`, `docs/ROADMAP.md`, `tests/README.md`, `README.md` y `docs/technical/technical-specification.md` |
| Agregaste/moviste archivos fuente | `docs/index/OVERVIEW.md` (mapa de archivos) |
| **Tomaste una decisión no obvia** (tradeoff, camino descartado) | **entrada nueva en `docs/DECISIONS.md`** |

Si un cambio no encaja en ninguna fila, probablemente igual toca `OVERVIEW.md`. Preguntar antes de
cerrar: *"¿qué índice hay que actualizar?"*.

## 9. Cierre de fase / release — checklist

Todos los ítems pesan lo mismo; ninguno es opcional:

1. `--selftest` en verde (**0 FAIL, sin SKIP**) y build Release sin warnings.
2. Índices de los dominios tocados, actualizados (§8).
3. `docs/DECISIONS.md`: entradas nuevas de lo que se decidió en la fase.
4. `docs/ROADMAP.md`: mover lo hecho, actualizar el foco, agregar lo que apareció.
5. **`README.md`: tabla de estado de features actualizada.**
6. **`docs/technical/technical-specification.md`: pasada sección por sección contra el estado real**
   (o contra los índices, que ya deberían estar al día) + fecha de revisión.
7. Versión bumpeada en `AppConstants.LauncherVersion` **y** `<Version>` del csproj (con confirmación).
8. Si es release: tag `vX.Y.Z`, y publicar el **SHA-256 del exe** en el segundo canal.

## 10. Cosas puntuales que conviene saber

- "Original" NO se hostea: es el backup local capturado en el primer arranque (`DatabaseService`).
  "Comunidad" se descarga del bucket.
- `L5A_DATA_ROOT` redirige config/caché/backups/`Decks` a otra carpeta — hook de testing y de uso
  portable. Está activo también en Release.
- Empaquetado: Costura.Fody embebe los DLLs → exe único. Se distribuye **solo** el `.exe`.
- Los mazos son `.txt` en la carpeta `Decks` **del juego**: nunca romper un archivo ajeno (las líneas
  no reconocidas se reescriben verbatim).
- El `SelfTest` (en `src/SelfTest.cs`, compilado dentro del exe) lee sus datos de
  `tests/fixtures/buscador|deckbuilder/`: mover esas carpetas rompe los golden tests.
- Noticias = placeholder (v2). El formato de los `.md` y su sección en el manifest está sin definir.
