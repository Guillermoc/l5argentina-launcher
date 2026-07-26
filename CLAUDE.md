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
4. **Confinamiento al origen del manifest**: nunca seguir URLs de terceros ni escapar del directorio
   del manifest. Dos variantes: `ResolveFileUri` para los `file` relativos del manifest del launcher
   (mismo esquema+host+puerto **y** bajo su directorio) y `ResolveSameOriginUrl` para las URLs
   absolutas del pool (mismo esquema+host+puerto, sin exigir directorio: el pool publica el manifest
   en `/production/` y los archivos en `/pool/`).
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

**Al cerrar un desarrollo → `--selftest`.** Cuenta como cierre cualquiera de estas:
- el usuario dice que terminamos, o pasa a otro tema;
- se va a commitear, taggear o publicar;
- se terminó un cambio en el **área crítica** —red/descarga/extracción, `CardSearchEngine`,
  `CatalogImporter`, `DeckRepository`, `SelfTest.cs`, `AppConstants`, o mover `docs/functional/*`—:
  **una sola corrida al terminar ese cambio**, no una por edición.

**El orden es: selftest → commit → push.** Primero se verifica, después se pide permiso para
commitear. Si el cambio ameritaba selftest y no se corrió, **no lo corras por tu cuenta ni lo
omitas**: avisale al usuario antes de commitear. Si él pide "commiteemos" y falta la corrida, la
respuesta correcta es *"antes corro el selftest, ¿dale?"*, no commitear callado ni recordárselo
después. Si no está claro si el cambio amerita la corrida, se decide **entre los dos**: proponé y
esperá.

**Nunca hace falta** para cambios que no tocan código compilado: documentación, README, comentarios,
workflows. Para textos del `.resx` alcanza con compilar. **Esta exención gana sobre la regla de
arriba**: un commit de solo documentación no dispara el selftest por el hecho de ser un commit. Si el
cambio incluye aunque sea un `.cs`, aunque solo cambie un comentario, corré una vez y listo.

**Si no se corrió, se dice.** Nunca reportar "listo" o "verificado" sin haberlo corrido. La frase
correcta es: *"compilé; no corrí el selftest, corresponde al cerrar"*.

**Al correrlo, reportar poco:** la línea `RESULTADO: N PASS, M FAIL` y, si hay rojos, solo las líneas
`FAIL`. No volcar el reporte entero. Un resultado válido es **0 FAIL y ningún SKIP** (un SKIP
significa que no encontró las fixtures de `tests/fixtures/`). El total de checks **no es una
constante**: sube a medida que la batería crece. El número al día vive en
[`docs/index/build-verificacion.md`](docs/index/build-verificacion.md).

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
  expected con el harness. Nunca "ajustar" el golden para que pase. *Agregar* un criterio nuevo sin
  tocar los existentes **no** cuenta como cambio de semántica y no necesita confirmación — salvo que
  toque los nombres JSON de `CardSearchCriteria`, de los que depende la deserialización de los
  golden.
- **Borrar checks del selftest** o bajar la cobertura de un área crítica (§5.2).
- **Tocar cualquiera de las reglas de §4**, incluido relajar validaciones de origen, hash o zip-slip.
- **Commitear. Siempre.** No hay commit sin un sí explícito, por chico que sea el cambio.
- **Pushear. Siempre** — y antes de pushear, el chequeo de Dependabot de §7.1.
- **Taggear o publicar un release.**
- **Bumpear la versión** (`AppConstants.LauncherVersion` + `<Version>` del csproj).
- **Borrar o mover archivos del usuario** en pruebas manuales. Para el estado propio del launcher
  alcanza con `L5A_DATA_ROOT`; **para los mazos NO alcanza**: viven en `<instalación>\Decks` y esa
  variable no los redirige (ver §10). Antes de probar mazos a mano, apuntá `sunAndMoonPath` a una
  copia del juego o dejalo vacío (así caen en `%APPDATA%\...\Decks`).
- **Reestructurar carpetas de `docs/`**, o mover/renombrar `tests/fixtures/*` (rompe las rutas que
  `SelfTest.cs` resuelve para los golden tests).

### 7.1 Git: a `main` directo, sin PRs — pero preguntando siempre

**El mecanismo** ([ADR-034](docs/DECISIONS.md#adr-034)): commits directos a `main`, sin ramas ni pull
requests. El repo lo mantiene una sola persona y los PR están deshabilitados en GitHub; un PR sin
revisor es ceremonia sin beneficio.

**Eso no es autorización.** Commitear y pushear **siempre** requieren un sí explícito del usuario
(§7). "Directo a `main`" describe adónde va el commit, no que se haga solo.

Secuencia al cerrar un trabajo:

1. Selftest si corresponde (§5). Reportar el resultado.
2. **Proponer el commit** —qué entra y con qué mensaje— y esperar el sí.
3. Commit. Si son varios cambios independientes, **commits separados** para que el diff se lea.
   Mensaje que explique **por qué**, no solo qué.
4. **Antes de pushear, chequear Dependabot** (no necesita `gh`):
   ```sh
   git ls-remote origin 'refs/heads/dependabot/*'
   ```
   Si hay ramas pendientes, **decírselo al usuario y preguntar**: ¿pusheamos igual, o primero
   actualizamos lo que Dependabot está proponiendo? No decidirlo por cuenta propia.
   (Ojo: esto lista los bumps de versión que el bot dejó como rama. Las *alertas de seguridad*
   requieren autenticación y no se ven desde acá.)
5. Push, con el sí del usuario.

La branch protection de `main` sigue activa **contra force-push y borrado**. Nunca reescribir
historia publicada (`--force`, `reset --hard` sobre lo pusheado, `commit --amend` de algo ya remoto):
eso requiere confirmación aparte y explícita, aunque ya haya un sí para pushear.

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
| **Cantidad de checks del selftest** (agregaste o borraste checks) | solo dos lugares: `docs/index/build-verificacion.md` (dueño del dato) y `README.md`. El resto de los docs dice "la batería en verde" a propósito; las menciones en `DECISIONS.md` son fotos fechadas y **no** se actualizan |
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
- **Hay dos raíces de archivos y no se comportan igual.** (a) *Lo nuestro* —config, caché, backups,
  `usercards.json`, `presets.json`, `decksmeta.json`— cuelga del perfil del usuario y **sí** lo
  redirige `L5A_DATA_ROOT` (hook de testing y de uso portable, activo también en Release).
  (b) *Lo del juego* —`database.xml`, `images\`, los `.txt` de `Decks\`— cuelga de la instalación
  configurada (`sunAndMoonPath`) y **no** se redirige: si `Decks` se moviera, el juego dejaría de
  leer el mazo, que es todo el punto de [ADR-022](docs/DECISIONS.md#adr-022). Único fallback:
  sin instalación configurada, los mazos caen en `%APPDATA%\...\Decks`.
- Nada de esto depende de dónde esté el `.exe`: moverlo no cambia ninguna ruta. La única ruta
  relativa al exe es el fallback de lectura de imágenes del Buscador.
- Empaquetado: Costura.Fody embebe los DLLs → exe único. Se distribuye **solo** el `.exe`.
- Los mazos son `.txt` en la carpeta `Decks` **del juego**: nunca romper un archivo ajeno (las líneas
  no reconocidas se reescriben verbatim).
- El `SelfTest` (en `src/SelfTest.cs`, compilado dentro del exe) lee sus datos de
  `tests/fixtures/buscador|deckbuilder/`: mover esas carpetas rompe los golden tests.
- Noticias = placeholder (v2). El formato de los `.md` y su sección en el manifest está sin definir.
