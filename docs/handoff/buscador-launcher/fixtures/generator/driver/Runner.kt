// DRIVER del harness de golden tests.
// Ejecuta la LÓGICA REAL de la app (CardImporter, RulesImporter, CardQueryCompiler,
// SQL de CardDao leído por reflexión de las anotaciones @Query) contra SQLite (JDBC),
// con los datos reales de producción (cards 2.3.1, rules 2.3.1, filters 2.3.0).
package harness

import androidx.room.Query
import androidx.sqlite.db.SimpleSQLiteQuery
import androidx.sqlite.db.SupportSQLiteQuery
import com.guillermoc.l5argentina.data.db.AppDatabase
import com.guillermoc.l5argentina.data.db.CardQueryCompiler
import com.guillermoc.l5argentina.data.db.dao.CardDao
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.model.CardSearchCriteria
import com.guillermoc.l5argentina.data.model.SortField
import com.guillermoc.l5argentina.data.remote.FiltersJson
import com.guillermoc.l5argentina.sync.importer.CardImporter
import com.guillermoc.l5argentina.sync.importer.RulesImporter
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.encodeToJsonElement
import kotlinx.serialization.json.jsonObject
import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import java.sql.PreparedStatement

// ─────────────────────────────────────────────────────────────────────────────
// Schema: nombres de columna EXACTOS de CardEntity (@ColumnInfo) — ver CardEntity.kt
// ─────────────────────────────────────────────────────────────────────────────
private const val DDL = """
CREATE TABLE cards (
  id TEXT NOT NULL PRIMARY KEY,
  name TEXT NOT NULL,
  type TEXT NOT NULL,
  clan TEXT,
  deck_group TEXT NOT NULL,
  rarity TEXT NOT NULL,
  sets TEXT NOT NULL,
  legality TEXT NOT NULL,
  keywords TEXT NOT NULL,
  text TEXT,
  flavor TEXT,
  force INTEGER,
  chi INTEGER,
  cost INTEGER,
  honor INTEGER,
  personal_honor INTEGER,
  focus INTEGER,
  gold_production INTEGER,
  province_strength INTEGER,
  starting_honor INTEGER,
  artist TEXT,
  image_path TEXT,
  max_copies INTEGER NOT NULL,
  banned INTEGER NOT NULL,
  note TEXT,
  label_text TEXT,
  label_color TEXT,
  highlight_color TEXT,
  initial_holding_slot TEXT,
  force_special INTEGER NOT NULL,
  cost_special INTEGER NOT NULL,
  gold_production_special INTEGER NOT NULL,
  honor_req_negative INTEGER NOT NULL,
  personal_honor_negative INTEGER NOT NULL,
  action_keywords TEXT NOT NULL,
  subtitle TEXT,
  printing_images TEXT NOT NULL
)"""

private val COLUMNS = listOf(
    "id","name","type","clan","deck_group","rarity","sets","legality","keywords","text","flavor",
    "force","chi","cost","honor","personal_honor","focus","gold_production","province_strength",
    "starting_honor","artist","image_path","max_copies","banned","note","label_text","label_color",
    "highlight_color","initial_holding_slot","force_special","cost_special","gold_production_special",
    "honor_req_negative","personal_honor_negative","action_keywords","subtitle","printing_images"
)

private fun CardEntity.columnValues(): List<Any?> = listOf(
    id, name, type, clan, deckGroup, rarity, sets, legality, keywords, text, flavor,
    force, chi, cost, honor, personalHonor, focus, goldProduction, provinceStrength,
    startingHonor, artist, imagePath, maxCopies, banned, note, labelText, labelColor,
    highlightColor, initialHoldingSlot, forceSpecial, costSpecial, goldProductionSpecial,
    honorReqNegative, personalHonorNegative, actionKeywords, subtitle, printingImages
)

// ─────────────────────────────────────────────────────────────────────────────
// CardDao sobre JDBC. El SQL de resetAllRules/applyCardRule/applyCardRuleKeepMaxCopies
// se lee por reflexión de las anotaciones @Query de la interfaz REAL (CardDao.kt),
// así no hay transcripción manual.
// ─────────────────────────────────────────────────────────────────────────────
class JdbcCardDao(private val conn: Connection) : CardDao {

    private fun querySqlOf(method: String): String =
        CardDao::class.java.methods.first { it.name == method }
            .getAnnotation(Query::class.java)!!.value

    private val namedParamRe = Regex(":([A-Za-z_][A-Za-z0-9_]*)")

    private fun bindNamed(sql: String, values: Map<String, Any?>): Pair<String, List<Any?>> {
        val order = mutableListOf<Any?>()
        val positional = namedParamRe.replace(sql) { m ->
            val key = m.groupValues[1]
            require(values.containsKey(key)) { "Falta bind :$key en $sql" }
            order.add(values[key]); "?"
        }
        return positional to order
    }

    private fun PreparedStatement.bindAll(args: List<Any?>) {
        args.forEachIndexed { i, v ->
            when (v) {
                null -> setObject(i + 1, null)
                is Boolean -> setInt(i + 1, if (v) 1 else 0)   // Room persiste Boolean como 0/1
                is Int -> setInt(i + 1, v)
                is Long -> setLong(i + 1, v)
                is String -> setString(i + 1, v)
                else -> setObject(i + 1, v)
            }
        }
    }

    fun execUpdate(sql: String, args: List<Any?> = emptyList()): Int =
        conn.prepareStatement(sql).use { it.bindAll(args); it.executeUpdate() }

    fun queryIds(sql: String, args: List<Any?>): List<String> =
        conn.prepareStatement(sql).use { st ->
            st.bindAll(args)
            st.executeQuery().use { rs ->
                val ids = mutableListOf<String>()
                while (rs.next()) ids.add(rs.getString("id"))
                ids
            }
        }

    override fun search(query: SupportSQLiteQuery): Flow<List<CardEntity>> =
        throw UnsupportedOperationException("el driver ejecuta el SQL directamente")

    override suspend fun getById(id: String): CardEntity? = throw UnsupportedOperationException()
    override suspend fun getByName(name: String): CardEntity? = throw UnsupportedOperationException()

    override suspend fun count(): Int =
        conn.prepareStatement("SELECT COUNT(*) c FROM cards").use { st ->
            st.executeQuery().use { rs -> rs.next(); rs.getInt(1) }
        }

    override fun countInitialHoldingCards(): Flow<Int> = flowOf(0)

    override suspend fun insertAll(cards: List<CardEntity>) {
        val sql = "INSERT OR REPLACE INTO cards (${COLUMNS.joinToString(",")}) VALUES (${COLUMNS.joinToString(",") { "?" }})"
        conn.prepareStatement(sql).use { st ->
            for (c in cards) { st.bindAll(c.columnValues()); st.addBatch() }
            st.executeBatch()
        }
    }

    override suspend fun deleteAll() { execUpdate("DELETE FROM cards") }

    override suspend fun resetAllRules() { execUpdate(querySqlOf("resetAllRules")) }

    override suspend fun applyCardRule(
        name: String, banned: Boolean, maxCopies: Int,
        note: String?, labelText: String?, labelColor: String?, highlightColor: String?
    ): Int {
        val (sql, args) = bindNamed(querySqlOf("applyCardRule"), mapOf(
            "name" to name, "banned" to banned, "maxCopies" to maxCopies, "note" to note,
            "labelText" to labelText, "labelColor" to labelColor, "highlightColor" to highlightColor))
        return execUpdate(sql, args)
    }

    override suspend fun applyCardRuleKeepMaxCopies(
        name: String, banned: Boolean,
        note: String?, labelText: String?, labelColor: String?, highlightColor: String?
    ): Int {
        val (sql, args) = bindNamed(querySqlOf("applyCardRuleKeepMaxCopies"), mapOf(
            "name" to name, "banned" to banned, "note" to note,
            "labelText" to labelText, "labelColor" to labelColor, "highlightColor" to highlightColor))
        return execUpdate(sql, args)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Casos golden
// ─────────────────────────────────────────────────────────────────────────────
data class Case(
    val name: String,
    val note: String,
    val criteria: CardSearchCriteria,
    val orderMatters: Boolean = false
)

private fun cases(goldenListId: String): List<Case> = listOf(
    Case("query-vacia-defaults",
        "Sin filtros: excluye baneadas (banned=0) y ocultas; orden name ASC. Es el estado inicial de la pantalla.",
        CardSearchCriteria(), orderMatters = true),
    Case("nombre-una-palabra",
        "name='kisada' → substring case-insensitive sobre name (LIKE %kisada%).",
        CardSearchCriteria(name = "kisada")),
    Case("nombre-multi-palabra-AND",
        "name='hida kisada' → cada palabra por separado con AND; no es frase exacta.",
        CardSearchCriteria(name = "hida kisada")),
    Case("nombre-substring-interno",
        "name='isad' matchea dentro de la palabra (no hay word-boundary en name).",
        CardSearchCriteria(name = "isad")),
    Case("nombre-mayusculas",
        "name='KISADA' — LIKE de SQLite es case-insensitive solo ASCII; debe dar igual que 'kisada'.",
        CardSearchCriteria(name = "KISADA")),
    Case("tipo-uno",
        "types=[stronghold] → type IN ('stronghold').",
        CardSearchCriteria(types = listOf("stronghold"))),
    Case("tipo-multi-OR",
        "types=[stronghold,sensei] → OR dentro del filtro.",
        CardSearchCriteria(types = listOf("stronghold", "sensei"))),
    Case("clan-uno-con-sensei",
        "clans=[crab] → clan IN + rama especial de Sensei por keywords (Crab/Crab Clan/All Clans/Any/Any Clan).",
        CardSearchCriteria(clans = listOf("crab"))),
    Case("clan-noclan",
        "clans=[noclan] → clan IS NULL + facciones menores (shadowlands, ratling, naga, hare, ronin, ninja, monk, spirit).",
        CardSearchCriteria(clans = listOf("noclan"))),
    Case("clan-nombrado-mas-noclan",
        "clans=[dragon,noclan] → OR entre ambas ramas.",
        CardSearchCriteria(clans = listOf("dragon", "noclan"))),
    Case("clan-sensei-compat",
        "clans=[dragon] + types=[sensei]: los sensei matchean por keywords de compatibilidad, no por columna clan (que es NULL).",
        CardSearchCriteria(clans = listOf("dragon"), types = listOf("sensei"))),
    Case("rareza-P-case-sensitive",
        "⚠️ rarities=['P'] usa IN (case-SENSITIVE en SQLite): matchea rarity='P' pero NO las 338 cartas con rarity='p' minúscula.",
        CardSearchCriteria(rarities = listOf("P"))),
    Case("rareza-R",
        "rarities=['R'] — no matchea las 3 cartas con 'r' minúscula.",
        CardSearchCriteria(rarities = listOf("R"))),
    Case("edicion-una",
        "sets=['Ivory'] → sets LIKE '%|Ivory|%' sobre columna pipe-delimited (todas las ediciones de la carta, no solo la actual).",
        CardSearchCriteria(sets = listOf("Ivory"))),
    Case("edicion-multi-OR",
        "sets=['KD','Tomorrow'] → OR dentro del filtro.",
        CardSearchCriteria(sets = listOf("KD", "Tomorrow"))),
    Case("edicion-carta-multi-printing",
        "sets=['WoH'] debe incluir Border Keep (Emperor028) porque su lista de sets contiene WoH aunque su printing actual sea Emperor.",
        CardSearchCriteria(sets = listOf("WoH"))),
    Case("formato-samurai",
        "formats=['samurai'] → legality LIKE '%|samurai|%'. ⚠️ Sin UI actual: el modal no expone este filtro.",
        CardSearchCriteria(formats = listOf("samurai"))),
    Case("formato-20F-mismatch",
        "⚠️ formats=['20F'] (código de la tabla formats) da 0 resultados: las cartas guardan 'TwentyFestivals' en legality, no '20F'.",
        CardSearchCriteria(formats = listOf("20F"))),
    Case("formato-TwentyFestivals",
        "formats=['TwentyFestivals'] (valor real en legality) sí devuelve resultados.",
        CardSearchCriteria(formats = listOf("TwentyFestivals"))),
    Case("keyword-default",
        "keywords=['Ninja'] con defaults: busca en keywords (|Ninja|), action_keywords (|Ninja|) y subtitle (substring %Ninja%).",
        CardSearchCriteria(keywords = listOf("Ninja"))),
    Case("keyword-solo-columna",
        "keywords=['Ninja'] con includeActionKeywords=false e includeSubtitleKeywords=false: solo keywords LIKE '%|Ninja|%'.",
        CardSearchCriteria(keywords = listOf("Ninja"), includeActionKeywords = false, includeSubtitleKeywords = false)),
    Case("keywords-AND",
        "keywords=['Cavalry','Scout'] con keywordMatchAll=true → ambas requeridas.",
        CardSearchCriteria(keywords = listOf("Cavalry", "Scout"), includeSubtitleKeywords = false)),
    Case("keywords-OR",
        "mismas keywords con keywordMatchAll=false → cualquiera alcanza.",
        CardSearchCriteria(keywords = listOf("Cavalry", "Scout"), keywordMatchAll = false, includeSubtitleKeywords = false)),
    Case("keyword-negativa",
        "keywords=['Samurai'] + negativeKeywords=['Unique'] → AND NOT (keywords|action_keywords); el subtitle NO se usa en negativas.",
        CardSearchCriteria(keywords = listOf("Samurai"), negativeKeywords = listOf("Unique"), includeSubtitleKeywords = false)),
    Case("keyword-subtitle-substring",
        "⚠️ keywords=['Kami'] con includeSubtitleKeywords=true: subtitle usa LIKE %Kami% SIN pipes → matchea substrings del subtítulo.",
        CardSearchCriteria(keywords = listOf("Kami"))),
    Case("texto-una-palabra",
        "text='ranged' → normaliza puntuación/tags a espacios y busca '% ranged %' (palabra completa, case-insensitive).",
        CardSearchCriteria(text = "ranged")),
    Case("texto-multi-palabra-AND",
        "text='ranged attack' → cada palabra debe aparecer como palabra completa (AND); no es frase.",
        CardSearchCriteria(text = "ranged attack")),
    Case("texto-frase-exacta",
        "text con comillas dobles → substring exacto sin normalizar: '\"Ranged 3 Attack\"'.",
        CardSearchCriteria(text = "\"Ranged 3 Attack\"")),
    Case("texto-word-boundary",
        "text='honor' NO matchea 'honorable'/'dishonor' (word-boundary con espacios).",
        CardSearchCriteria(text = "honor", types = listOf("region"))),
    Case("designador-battle",
        "designators=['Battle'] → 3 patrones anclados al cierre del <b>: '%Battle:</b>%', '%Battle</b>:%', '%Battle</b>,%'.",
        CardSearchCriteria(designators = listOf("Battle"), types = listOf("event"))),
    Case("designador-dynasty-sin-falsos-positivos",
        "designators=['Dynasty']: el anclado evita matchear la palabra 'Dynasty' suelta en el texto.",
        CardSearchCriteria(designators = listOf("Dynasty"))),
    Case("fuerza-rango",
        "forceMin=5, forceMax=6 → force IS NOT NULL AND force BETWEEN; excluye '+*' (force NULL).",
        CardSearchCriteria(forceMin = 5, forceMax = 6, types = listOf("follower"))),
    Case("fuerza-rango-con-especial",
        "types=[item] forceMin=5..6 + includeSpecialForce=true → OR force_special=1 agrega los ítems con fuerza '+*' (TNO091, TwentyFestivals228).",
        CardSearchCriteria(forceMin = 5, forceMax = 6, includeSpecialForce = true, types = listOf("item"))),
    Case("fuerza-rango-item-sin-especial",
        "types=[item] forceMin=5..6 sin flag: los '+*' (force NULL) quedan afuera — comparar con el caso anterior.",
        CardSearchCriteria(forceMin = 5, forceMax = 6, types = listOf("item"))),
    Case("costo-cero-excluye-asterisco",
        "types=[strategy] costMin=0 → cost IS NOT NULL: las 9 strategies con costo '*' quedan afuera aunque el rango sea total.",
        CardSearchCriteria(costMin = 0, types = listOf("strategy"))),
    Case("costo-incluye-asterisco",
        "types=[strategy] costMin=0 + includeSpecialCost=true → OR cost_special=1 reincorpora los '*'.",
        CardSearchCriteria(costMin = 0, includeSpecialCost = true, types = listOf("strategy"))),
    Case("honor-req-guion",
        "honorReqMin=0..3 + includeNegativeHonorReq=true → OR honor_req_negative=1 agrega las cartas con '-';",
        CardSearchCriteria(honorReqMin = 0, honorReqMax = 3, includeNegativeHonorReq = true, clans = listOf("crane"), types = listOf("personality"))),
    Case("honor-req-solo-rango",
        "mismo rango sin includeNegativeHonorReq: los '-' (honor NULL) quedan afuera.",
        CardSearchCriteria(honorReqMin = 0, honorReqMax = 3, clans = listOf("crane"), types = listOf("personality"))),
    Case("chi-min",
        "chiMin=6 → chi >= 6 (sin flag especial; los '+N' importados como NULL quedan afuera).",
        CardSearchCriteria(chiMin = 6)),
    Case("focus-rango",
        "focusMin=5 → focus >= 5.",
        CardSearchCriteria(focusMin = 5)),
    Case("baneada-excluida-default",
        "name='kensai' con defaults: A Kensai's Art/Victory/Will están baneadas → no aparecen.",
        CardSearchCriteria(name = "kensai")),
    Case("baneada-incluida",
        "name='kensai' + includeBanned=true → aparecen las baneadas.",
        CardSearchCriteria(name = "kensai", includeBanned = true)),
    Case("solo-holdings-iniciales",
        "onlyInitialHoldings=true → initial_holding_slot IS NOT NULL (7 elegibles de filters.json; acá salen 6 porque Emperor028 está oculta por userDataSeed y el default excluye ocultas).",
        CardSearchCriteria(onlyInitialHoldings = true)),
    Case("combo-clan-tipo-fuerza",
        "AND entre filtros distintos: crab + personality + forceMin=6.",
        CardSearchCriteria(clans = listOf("crab"), types = listOf("personality"), forceMin = 6)),
    Case("cero-resultados",
        "name sin matches posibles.",
        CardSearchCriteria(name = "zzzznoexiste")),
    Case("solo-favoritas",
        "onlyFavorites=true con user_card_data sembrada (ver userDataSeed).",
        CardSearchCriteria(onlyFavorites = true)),
    Case("ocultas-excluidas-default",
        "name='border keep' con defaults: Emperor028 está oculta (seed) → excluida; includeHidden la reincorpora (ver caso siguiente).",
        CardSearchCriteria(name = "border keep")),
    Case("ocultas-incluidas",
        "name='border keep' + includeHidden=true.",
        CardSearchCriteria(name = "border keep", includeHidden = true)),
    Case("solo-ocultas",
        "onlyHidden=true → solo las ocultas (tiene prioridad sobre includeHidden).",
        CardSearchCriteria(onlyHidden = true)),
    Case("filtro-lista",
        "listId=golden-list → id IN (SELECT card_id FROM deck_cards WHERE deck_id=?); la oculta Emperor028 sigue excluida por default.",
        CardSearchCriteria(listId = goldenListId)),
    Case("orden-costo-desc",
        "types=[holding] ordenado por COST desc: NULLs SIEMPRE al final, desempate name ASC.",
        CardSearchCriteria(types = listOf("holding"), sets = listOf("Ivory"), sortField = SortField.COST, sortAscending = false),
        orderMatters = true),
    Case("orden-fuerza-asc-nulls-al-final",
        "types=[item] por FORCE asc: ítems sin fuerza (NULL) van al final aunque el orden sea ascendente.",
        CardSearchCriteria(types = listOf("item"), sets = listOf("TwentyFestivals"), sortField = SortField.FORCE, sortAscending = true),
        orderMatters = true),
    Case("orden-por-set-sql-base",
        "sortField=SET: el SQL solo ordena por name; el orden real por set se aplica en SearchViewModel.displayCards (capa UI, ver SPEC-FILTROS §orden). orderMatters=false: validar membresía.",
        CardSearchCriteria(types = listOf("stronghold"), sortField = SortField.SET)),
)

// ─────────────────────────────────────────────────────────────────────────────

fun main(args: Array<String>) = runBlocking {
    val dataDir = File(args[0])      // carpeta con cards-2.3.1.json, rules.json, filters.json
    val outFile = File(args[1])      // golden-tests.json de salida
    val dbFile = File(args[2])       // sqlite de trabajo (queda para inspección)
    val commit = args.getOrElse(3) { "unknown" }

    val json = Json {                 // idéntico a L5ArgentinaApp.jsonParser
        ignoreUnknownKeys = true
        isLenient = true
        coerceInputValues = true
    }

    if (dbFile.exists()) dbFile.delete()
    val conn = DriverManager.getConnection("jdbc:sqlite:${dbFile.absolutePath.replace('\\', '/')}")
    conn.autoCommit = false
    conn.createStatement().use {
        it.execute(DDL)
        it.execute("CREATE INDEX index_cards_name ON cards(name)")
        it.execute("CREATE TABLE user_card_data (card_id TEXT NOT NULL, type TEXT NOT NULL, PRIMARY KEY(card_id, type))")
        it.execute("CREATE TABLE deck_cards (deck_id TEXT NOT NULL, card_id TEXT NOT NULL, quantity INTEGER NOT NULL DEFAULT 1, PRIMARY KEY(deck_id, card_id))")
    }

    val dao = JdbcCardDao(conn)
    val cardsStr = dataDir.resolve("cards-2.3.1.json").readText(Charsets.UTF_8)
    val rulesStr = dataDir.resolve("rules.json").readText(Charsets.UTF_8)
    val filtersStr = dataDir.resolve("filters.json").readText(Charsets.UTF_8)

    // 1. filters → mapa cardTitle→slotId (réplica de FiltersImporter + getAllEligibleCardSlots + associate)
    val filters = json.decodeFromString<FiltersJson>(filtersStr)
    val initialHoldingByCard = filters.formats
        .flatMap { f -> f.initialHoldingSlots.flatMap { s -> s.eligibleCards.map { it to s.id } } }
        .associate { it }

    // 2. rules parseadas (mapa por título — igual que CatalogSyncService.loadCachedRulesMap)
    val rulesImporter = RulesImporter(json, dao)
    val rulesMap = rulesImporter.parse(rulesStr)

    // 3. import real de cartas (estado estable: rules cacheadas presentes → preferredPrinting aplicado)
    val importer = CardImporter(AppDatabase(), dao, json)
    val imported = importer.import(cardsStr, rulesMap, initialHoldingByCard)
    conn.commit()
    println("cartas importadas: $imported (dao.count=${dao.count()})")

    // 4. rules aplicadas post-import (mismo orden que el sync real: database → rules)
    val unmatched = rulesImporter.applyRules(rulesStr)
    conn.commit()
    println("rules aplicadas; títulos sin match: ${unmatched.size} $unmatched")

    // 5. seed de datos de usuario para los casos de favoritas/ocultas/lista
    val favorites = listOf("Emperor059", "Ivory032")   // Hida Kisada, Jade Works
    val hidden = listOf("Emperor028")                  // Border Keep
    val goldenListId = "golden-list"
    val goldenListCards = listOf("Emperor059", "Ivory032", "Emperor028")
    favorites.forEach { dao.execUpdate("INSERT INTO user_card_data (card_id, type) VALUES (?, 'favorite')", listOf(it)) }
    hidden.forEach { dao.execUpdate("INSERT INTO user_card_data (card_id, type) VALUES (?, 'hidden')", listOf(it)) }
    goldenListCards.forEach { dao.execUpdate("INSERT INTO deck_cards (deck_id, card_id, quantity) VALUES (?, ?, 1)", listOf(goldenListId, it)) }
    conn.commit()

    // 6. ejecutar los casos con el compilador REAL
    val compiler = CardQueryCompiler()
    val criteriaJson = Json { encodeDefaults = false }
    val caseObjects = mutableListOf<JsonObject>()
    for (c in cases(goldenListId)) {
        val q = compiler.compile(c.criteria) as SimpleSQLiteQuery
        val ids = dao.queryIds(q.sql, q.args.toList())
        val serialized = criteriaJson.encodeToJsonElement(c.criteria).jsonObject
        val filtersObj = JsonObject(serialized.filterKeys { it != "name" })
        caseObjects.add(JsonObject(mapOf(
            "name" to JsonPrimitive(c.name),
            "note" to JsonPrimitive(c.note),
            "query" to JsonPrimitive(c.criteria.name),
            "filters" to filtersObj,
            "sql" to JsonPrimitive(q.sql),
            "expectedCount" to JsonPrimitive(ids.size),
            "expectedIds" to JsonArray(ids.map { JsonPrimitive(it) }),
            "orderMatters" to JsonPrimitive(c.orderMatters)
        )))
        println("caso ${c.name}: ${ids.size} resultados")
    }

    val root = JsonObject(mapOf(
        "generatedFrom" to JsonPrimitive(commit),
        "cardsVersion" to JsonPrimitive("2.3.1"),
        "rulesVersion" to JsonPrimitive("2.3.1"),
        "filtersVersion" to JsonPrimitive("2.3.0"),
        "generator" to JsonPrimitive(
            "Harness JVM: CardImporter + RulesImporter (parse+applyRules) + CardQueryCompiler reales " +
            "compilados sin modificar (kotlinc 2.0.21, plugin kotlinx-serialization) y ejecutados contra " +
            "SQLite via sqlite-jdbc 3.46.1.3. SQL de CardDao leído por reflexión de las @Query reales. " +
            "Datos: production/cards_db 2.3.1 + rules 2.3.1 + filters 2.3.0."
        ),
        "userDataSeed" to JsonObject(mapOf(
            "favorites" to JsonArray(favorites.map { JsonPrimitive(it) }),
            "hidden" to JsonArray(hidden.map { JsonPrimitive(it) }),
            "lists" to JsonObject(mapOf(goldenListId to JsonArray(goldenListCards.map { JsonPrimitive(it) })))
        )),
        "notes" to JsonArray(listOf(
            "filters = CardSearchCriteria serializado sin defaults (campos ausentes = valor default, ver src/CardSearchCriteria.kt).",
            "query = criteria.name (texto de la barra de búsqueda en modo NAME).",
            "sql = query generada por CardQueryCompiler.compile() para ese criteria (args posicionales '?').",
            "orderMatters=true: la secuencia es la producida por SQLite con ORDER BY del criteria; en empates exactos (mismo valor de sort y mismo name) el desempate implícito es rowid (= orden de inserción del cards.json). Si el port no replica ese desempate, validar como sort estable por las claves explícitas.",
            "orderMatters=false: validar solo membresía (mismo conjunto de ids).",
            "Los casos de favoritas/ocultas/lista requieren sembrar user_card_data y deck_cards según userDataSeed antes de ejecutar."
        ).map { JsonPrimitive(it) }),
        "cases" to JsonArray(caseObjects)
    ))

    outFile.parentFile?.mkdirs()
    outFile.writeText(Json { prettyPrint = true }.encodeToString(JsonObject.serializer(), root), Charsets.UTF_8)
    conn.commit()
    conn.close()
    println("golden tests escritos en ${outFile.absolutePath} (${caseObjects.size} casos)")
}
