// DRIVER del harness de golden tests del DECKBUILDER.
// Ejecuta la LÓGICA REAL (DeckRepository, CardImporter, RulesImporter — compilados sin modificar)
// contra SQLite (JDBC). Los DAOs son dobles JDBC (en la app real, Room los genera).
// El SQL de cada @Query se implementa acá replicando la anotación real (citada en comentarios).
package harness

import androidx.room.RoomDatabase
import androidx.sqlite.db.SupportSQLiteQuery
import com.guillermoc.l5argentina.data.db.dao.CardDao
import com.guillermoc.l5argentina.data.db.dao.DeckCardGroupDao
import com.guillermoc.l5argentina.data.db.dao.DeckDao
import com.guillermoc.l5argentina.data.db.dao.DeckGroupDao
import com.guillermoc.l5argentina.data.db.dao.EligibleCardSlot
import com.guillermoc.l5argentina.data.db.dao.FormatInitialHoldingDao
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.db.entity.DeckCardEntity
import com.guillermoc.l5argentina.data.db.entity.DeckCardGroupEntity
import com.guillermoc.l5argentina.data.db.entity.DeckEntity
import com.guillermoc.l5argentina.data.db.entity.DeckGroupEntity
import com.guillermoc.l5argentina.data.db.entity.FormatInitialHoldingEligibleCardEntity
import com.guillermoc.l5argentina.data.db.entity.FormatInitialHoldingSlotEntity
import com.guillermoc.l5argentina.data.db.entity.UserCardDataEntity
import com.guillermoc.l5argentina.data.db.relation.DeckCardWithCardRow
import com.guillermoc.l5argentina.data.remote.FiltersJson
import com.guillermoc.l5argentina.data.repository.AddCardResult
import com.guillermoc.l5argentina.data.repository.DeckRepository
import com.guillermoc.l5argentina.sync.importer.CardImporter
import com.guillermoc.l5argentina.sync.importer.RulesImporter
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import java.sql.PreparedStatement

// ─────────────────────────────────────────────────────────────────────────────
// Schema
// ─────────────────────────────────────────────────────────────────────────────
private val DDL = listOf(
    """CREATE TABLE cards (
        id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, type TEXT NOT NULL, clan TEXT,
        deck_group TEXT NOT NULL, rarity TEXT NOT NULL, sets TEXT NOT NULL, legality TEXT NOT NULL,
        keywords TEXT NOT NULL, text TEXT, flavor TEXT, force INTEGER, chi INTEGER, cost INTEGER,
        honor INTEGER, personal_honor INTEGER, focus INTEGER, gold_production INTEGER,
        province_strength INTEGER, starting_honor INTEGER, artist TEXT, image_path TEXT,
        max_copies INTEGER NOT NULL, banned INTEGER NOT NULL, note TEXT, label_text TEXT,
        label_color TEXT, highlight_color TEXT, initial_holding_slot TEXT, force_special INTEGER NOT NULL,
        cost_special INTEGER NOT NULL, gold_production_special INTEGER NOT NULL, honor_req_negative INTEGER NOT NULL,
        personal_honor_negative INTEGER NOT NULL, action_keywords TEXT NOT NULL, subtitle TEXT,
        printing_images TEXT NOT NULL)""",
    """CREATE TABLE decks (
        id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, created_at INTEGER NOT NULL,
        notes TEXT, type TEXT NOT NULL DEFAULT 'deck', list_name TEXT, list_description TEXT,
        group_id TEXT, sort_order INTEGER NOT NULL DEFAULT 0, sort_mode TEXT)""",
    """CREATE TABLE deck_cards (
        deck_id TEXT NOT NULL, card_id TEXT NOT NULL, quantity INTEGER NOT NULL,
        card_group_id TEXT, sort_order INTEGER NOT NULL DEFAULT 0,
        PRIMARY KEY(deck_id, card_id))""",
    """CREATE TABLE deck_groups (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, section_type TEXT NOT NULL)""",
    """CREATE TABLE deck_card_groups (id TEXT NOT NULL PRIMARY KEY, deck_id TEXT NOT NULL, name TEXT NOT NULL, parent_group TEXT NOT NULL)""",
    """CREATE TABLE format_initial_holding_slots (format_id TEXT NOT NULL, slot_id TEXT NOT NULL, label TEXT NOT NULL, required INTEGER NOT NULL, PRIMARY KEY(format_id, slot_id))""",
    """CREATE TABLE format_initial_holding_eligible_cards (format_id TEXT NOT NULL, slot_id TEXT NOT NULL, card_title TEXT NOT NULL, PRIMARY KEY(format_id, slot_id, card_title))""",
    """CREATE TABLE user_card_data (card_id TEXT NOT NULL, type TEXT NOT NULL, PRIMARY KEY(card_id, type))"""
)

private val CARD_COLUMNS = listOf(
    "id","name","type","clan","deck_group","rarity","sets","legality","keywords","text","flavor",
    "force","chi","cost","honor","personal_honor","focus","gold_production","province_strength",
    "starting_honor","artist","image_path","max_copies","banned","note","label_text","label_color",
    "highlight_color","initial_holding_slot","force_special","cost_special","gold_production_special",
    "honor_req_negative","personal_honor_negative","action_keywords","subtitle","printing_images"
)

private fun CardEntity.cardValues(): List<Any?> = listOf(
    id, name, type, clan, deckGroup, rarity, sets, legality, keywords, text, flavor,
    force, chi, cost, honor, personalHonor, focus, goldProduction, provinceStrength,
    startingHonor, artist, imagePath, maxCopies, banned, note, labelText, labelColor,
    highlightColor, initialHoldingSlot, forceSpecial, costSpecial, goldProductionSpecial,
    honorReqNegative, personalHonorNegative, actionKeywords, subtitle, printingImages
)

private fun PreparedStatement.bindAll(args: List<Any?>) {
    args.forEachIndexed { i, v ->
        when (v) {
            null -> setObject(i + 1, null)
            is Boolean -> setInt(i + 1, if (v) 1 else 0)
            is Int -> setInt(i + 1, v)
            is Long -> setLong(i + 1, v)
            is String -> setString(i + 1, v)
            else -> setObject(i + 1, v)
        }
    }
}

private fun java.sql.ResultSet.intOrNull(col: String): Int? { val v = getInt(col); return if (wasNull()) null else v }

private fun java.sql.ResultSet.toCard(): CardEntity = CardEntity(
    id = getString("id"), name = getString("name"), type = getString("type"), clan = getString("clan"),
    deckGroup = getString("deck_group"), rarity = getString("rarity"), sets = getString("sets"),
    legality = getString("legality"), keywords = getString("keywords"), text = getString("text"),
    flavor = getString("flavor"), force = intOrNull("force"), chi = intOrNull("chi"), cost = intOrNull("cost"),
    honor = intOrNull("honor"), personalHonor = intOrNull("personal_honor"), focus = intOrNull("focus"),
    goldProduction = intOrNull("gold_production"), provinceStrength = intOrNull("province_strength"),
    startingHonor = intOrNull("starting_honor"), artist = getString("artist"), imagePath = getString("image_path"),
    maxCopies = getInt("max_copies"), banned = getInt("banned") == 1, note = getString("note"),
    labelText = getString("label_text"), labelColor = getString("label_color"), highlightColor = getString("highlight_color"),
    initialHoldingSlot = getString("initial_holding_slot"), forceSpecial = getInt("force_special") == 1,
    costSpecial = getInt("cost_special") == 1, goldProductionSpecial = getInt("gold_production_special") == 1,
    honorReqNegative = getInt("honor_req_negative") == 1, personalHonorNegative = getInt("personal_honor_negative") == 1,
    actionKeywords = getString("action_keywords"), subtitle = getString("subtitle"), printingImages = getString("printing_images")
)

private fun java.sql.ResultSet.toDeck(): DeckEntity = DeckEntity(
    id = getString("id"), name = getString("name"), createdAt = getLong("created_at"),
    notes = getString("notes"), type = getString("type"), listName = getString("list_name"),
    listDescription = getString("list_description"), groupId = getString("group_id"),
    sortOrder = getInt("sort_order"), sortMode = getString("sort_mode")
)

private fun java.sql.ResultSet.toDeckCard(): DeckCardEntity = DeckCardEntity(
    deckId = getString("deck_id"), cardId = getString("card_id"), quantity = getInt("quantity"),
    cardGroupId = getString("card_group_id"), sortOrder = getInt("sort_order")
)

// ─────────────────────────────────────────────────────────────────────────────
// DAOs JDBC. Cada método replica el SQL de la @Query real (citada). Los @Insert
// (sin SQL) se implementan como INSERT OR REPLACE con las columnas de la entidad.
// ─────────────────────────────────────────────────────────────────────────────
private class Db(val conn: Connection) {
    fun exec(sql: String, args: List<Any?> = emptyList()): Int =
        conn.prepareStatement(sql).use { it.bindAll(args); it.executeUpdate() }
    fun <T> query(sql: String, args: List<Any?> = emptyList(), map: (java.sql.ResultSet) -> T): List<T> =
        conn.prepareStatement(sql).use { st -> st.bindAll(args); st.executeQuery().use { rs ->
            val out = mutableListOf<T>(); while (rs.next()) out.add(map(rs)); out } }
    fun scalarInt(sql: String, args: List<Any?> = emptyList()): Int =
        conn.prepareStatement(sql).use { st -> st.bindAll(args); st.executeQuery().use { rs -> rs.next(); rs.getInt(1) } }
}

private class JdbcCardDao(private val db: Db) : CardDao {
    // El SQL de resetAllRules/applyCardRule* se lee de las @Query REALES de CardDao por reflexión
    // (cero transcripción): así el estado del catálogo post-import es idéntico al de producción.
    private fun querySqlOf(method: String): String =
        CardDao::class.java.methods.first { it.name == method }.getAnnotation(androidx.room.Query::class.java)!!.value
    private val namedRe = Regex(":([A-Za-z_][A-Za-z0-9_]*)")
    private fun bindNamed(sql: String, values: Map<String, Any?>): Pair<String, List<Any?>> {
        val order = mutableListOf<Any?>()
        val positional = namedRe.replace(sql) { m -> order.add(values.getValue(m.groupValues[1])); "?" }
        return positional to order
    }
    override suspend fun getById(id: String): CardEntity? =
        db.query("SELECT * FROM cards WHERE id = ?", listOf(id)) { it.toCard() }.firstOrNull()
    override suspend fun getByName(name: String): CardEntity? =
        db.query("SELECT * FROM cards WHERE name = ? LIMIT 1", listOf(name)) { it.toCard() }.firstOrNull()
    override suspend fun count(): Int = db.scalarInt("SELECT COUNT(*) FROM cards")
    override suspend fun insertAll(cards: List<CardEntity>) {
        val sql = "INSERT OR REPLACE INTO cards (${CARD_COLUMNS.joinToString(",")}) VALUES (${CARD_COLUMNS.joinToString(","){"?"}})"
        db.conn.prepareStatement(sql).use { st -> for (c in cards) { st.bindAll(c.cardValues()); st.addBatch() }; st.executeBatch() }
    }
    override suspend fun deleteAll() { db.exec("DELETE FROM cards") }
    override fun search(query: SupportSQLiteQuery): Flow<List<CardEntity>> = throw UnsupportedOperationException()
    override fun countInitialHoldingCards(): Flow<Int> = throw UnsupportedOperationException()
    override suspend fun resetAllRules() { db.exec(querySqlOf("resetAllRules")) }
    override suspend fun applyCardRule(name: String, banned: Boolean, maxCopies: Int, note: String?, labelText: String?, labelColor: String?, highlightColor: String?): Int {
        val (sql, args) = bindNamed(querySqlOf("applyCardRule"), mapOf("name" to name, "banned" to banned, "maxCopies" to maxCopies, "note" to note, "labelText" to labelText, "labelColor" to labelColor, "highlightColor" to highlightColor))
        return db.exec(sql, args)
    }
    override suspend fun applyCardRuleKeepMaxCopies(name: String, banned: Boolean, note: String?, labelText: String?, labelColor: String?, highlightColor: String?): Int {
        val (sql, args) = bindNamed(querySqlOf("applyCardRuleKeepMaxCopies"), mapOf("name" to name, "banned" to banned, "note" to note, "labelText" to labelText, "labelColor" to labelColor, "highlightColor" to highlightColor))
        return db.exec(sql, args)
    }
}

private class JdbcDeckDao(private val db: Db) : DeckDao {
    override fun getAllDecks(): Flow<List<DeckEntity>> = flow { emit(db.query("SELECT * FROM decks ORDER BY created_at DESC") { it.toDeck() }) }
    override suspend fun getDeckById(id: String): DeckEntity? = db.query("SELECT * FROM decks WHERE id = ?", listOf(id)) { it.toDeck() }.firstOrNull()
    override suspend fun insertDeck(deck: DeckEntity) {
        db.exec("INSERT OR REPLACE INTO decks (id,name,created_at,notes,type,list_name,list_description,group_id,sort_order,sort_mode) VALUES (?,?,?,?,?,?,?,?,?,?)",
            listOf(deck.id, deck.name, deck.createdAt, deck.notes, deck.type, deck.listName, deck.listDescription, deck.groupId, deck.sortOrder, deck.sortMode))
    }
    override suspend fun deleteDeck(id: String) { db.exec("DELETE FROM decks WHERE id = ?", listOf(id)); db.exec("DELETE FROM deck_cards WHERE deck_id = ?", listOf(id)) }
    override suspend fun deleteAllDecks() { db.exec("DELETE FROM decks") }
    override suspend fun renameDeck(id: String, name: String) { db.exec("UPDATE decks SET name = ? WHERE id = ?", listOf(name, id)) }
    override suspend fun updateNotes(id: String, notes: String?) { db.exec("UPDATE decks SET notes = ? WHERE id = ?", listOf(notes, id)) }
    override suspend fun updateGroupId(id: String, groupId: String?) { db.exec("UPDATE decks SET group_id = ? WHERE id = ?", listOf(groupId, id)) }
    override suspend fun updateSortOrder(id: String, sortOrder: Int) { db.exec("UPDATE decks SET sort_order = ? WHERE id = ?", listOf(sortOrder, id)) }
    override suspend fun updateSortMode(id: String, sortMode: String?) { db.exec("UPDATE decks SET sort_mode = ? WHERE id = ?", listOf(sortMode, id)) }
    override suspend fun updateCardGroupId(deckId: String, cardId: String, cardGroupId: String?) { db.exec("UPDATE deck_cards SET card_group_id = ? WHERE deck_id = ? AND card_id = ?", listOf(cardGroupId, deckId, cardId)) }
    override suspend fun updateCardSortOrder(deckId: String, cardId: String, sortOrder: Int) { db.exec("UPDATE deck_cards SET sort_order = ? WHERE deck_id = ? AND card_id = ?", listOf(sortOrder, deckId, cardId)) }
    override suspend fun getMaxCardSortOrder(deckId: String): Int = db.scalarInt("SELECT COALESCE(MAX(sort_order), -1) FROM deck_cards WHERE deck_id = ?", listOf(deckId))
    override fun getDeckCardsWithCards(deckId: String): Flow<List<DeckCardWithCardRow>> = flow { emit(joinDeckCards("SELECT * FROM deck_cards WHERE deck_id = ?", listOf(deckId))) }
    override fun getAllDeckCardsWithCards(): Flow<List<DeckCardWithCardRow>> = flow { emit(joinDeckCards("SELECT * FROM deck_cards", emptyList())) }
    private fun joinDeckCards(sql: String, args: List<Any?>): List<DeckCardWithCardRow> =
        db.query(sql, args) { it.toDeckCard() }.map { dc ->
            val card = db.query("SELECT * FROM cards WHERE id = ?", listOf(dc.cardId)) { it.toCard() }.firstOrNull()
            DeckCardWithCardRow(dc, card)
        }
    override suspend fun getDeckCard(deckId: String, cardId: String): DeckCardEntity? =
        db.query("SELECT * FROM deck_cards WHERE deck_id = ? AND card_id = ?", listOf(deckId, cardId)) { it.toDeckCard() }.firstOrNull()
    override suspend fun upsertDeckCard(deckCard: DeckCardEntity) {
        db.exec("INSERT OR REPLACE INTO deck_cards (deck_id,card_id,quantity,card_group_id,sort_order) VALUES (?,?,?,?,?)",
            listOf(deckCard.deckId, deckCard.cardId, deckCard.quantity, deckCard.cardGroupId, deckCard.sortOrder))
    }
    override suspend fun deleteDeckCard(deckId: String, cardId: String) { db.exec("DELETE FROM deck_cards WHERE deck_id = ? AND card_id = ?", listOf(deckId, cardId)) }
    override suspend fun deleteTrimmedCards(deckId: String) { db.exec("DELETE FROM deck_cards WHERE deck_id = ? AND quantity = 0", listOf(deckId)) }
    override suspend fun deleteAllCardsForDeck(deckId: String) { db.exec("DELETE FROM deck_cards WHERE deck_id = ?", listOf(deckId)) }
    override fun getTotalCardCount(deckId: String): Flow<Int?> = flow { emit(db.scalarInt("SELECT COALESCE(SUM(quantity),0) FROM deck_cards WHERE deck_id = ?", listOf(deckId))) }
    override fun getOrphanDeckCards(deckId: String): Flow<List<DeckCardEntity>> = flow {
        emit(db.query("SELECT dc.* FROM deck_cards dc LEFT JOIN cards c ON c.id = dc.card_id WHERE dc.deck_id = ? AND c.id IS NULL AND (SELECT COUNT(*) FROM cards) > 0 ORDER BY dc.card_id ASC", listOf(deckId)) { it.toDeckCard() })
    }
    override suspend fun countOrphanDeckCards(): Int = db.scalarInt("SELECT COUNT(*) FROM deck_cards dc LEFT JOIN cards c ON c.id = dc.card_id WHERE c.id IS NULL AND (SELECT COUNT(*) FROM cards) > 0")
    override suspend fun countCardsInSlot(deckId: String, slot: String): Int =
        db.scalarInt("SELECT COUNT(*) FROM deck_cards INNER JOIN cards ON deck_cards.card_id = cards.id WHERE deck_cards.deck_id = ? AND cards.initial_holding_slot = ? AND deck_cards.quantity > 0", listOf(deckId, slot))
}

private class JdbcDeckGroupDao(private val db: Db) : DeckGroupDao {
    override fun getGroupsBySection(sectionType: String): Flow<List<DeckGroupEntity>> = flow { emit(db.query("SELECT * FROM deck_groups WHERE section_type = ? ORDER BY name ASC", listOf(sectionType)) { DeckGroupEntity(it.getString("id"), it.getString("name"), it.getString("section_type")) }) }
    override fun getAllGroups(): Flow<List<DeckGroupEntity>> = flow { emit(db.query("SELECT * FROM deck_groups ORDER BY section_type ASC, name ASC") { DeckGroupEntity(it.getString("id"), it.getString("name"), it.getString("section_type")) }) }
    override suspend fun insert(group: DeckGroupEntity) { db.exec("INSERT OR REPLACE INTO deck_groups (id,name,section_type) VALUES (?,?,?)", listOf(group.id, group.name, group.sectionType)) }
    override suspend fun delete(id: String) { db.exec("DELETE FROM deck_groups WHERE id = ?", listOf(id)) }
    override suspend fun rename(id: String, name: String) { db.exec("UPDATE deck_groups SET name = ? WHERE id = ?", listOf(name, id)) }
    override suspend fun unlinkDecks(groupId: String) { db.exec("UPDATE decks SET group_id = NULL WHERE group_id = ?", listOf(groupId)) }
    override suspend fun deleteAll() { db.exec("DELETE FROM deck_groups") }
}

private class JdbcDeckCardGroupDao(private val db: Db) : DeckCardGroupDao {
    override fun getGroupsForDeck(deckId: String): Flow<List<DeckCardGroupEntity>> = flow { emit(db.query("SELECT * FROM deck_card_groups WHERE deck_id = ? ORDER BY parent_group ASC, name ASC", listOf(deckId)) { DeckCardGroupEntity(it.getString("id"), it.getString("deck_id"), it.getString("name"), it.getString("parent_group")) }) }
    override suspend fun insert(group: DeckCardGroupEntity) { db.exec("INSERT OR REPLACE INTO deck_card_groups (id,deck_id,name,parent_group) VALUES (?,?,?,?)", listOf(group.id, group.deckId, group.name, group.parentGroup)) }
    override suspend fun delete(id: String) { db.exec("DELETE FROM deck_card_groups WHERE id = ?", listOf(id)) }
    override suspend fun rename(id: String, name: String) { db.exec("UPDATE deck_card_groups SET name = ? WHERE id = ?", listOf(name, id)) }
    override suspend fun unlinkCards(groupId: String) { db.exec("UPDATE deck_cards SET card_group_id = NULL WHERE card_group_id = ?", listOf(groupId)) }
}

private class JdbcFormatInitialHoldingDao(private val db: Db) : FormatInitialHoldingDao {
    override suspend fun insertSlots(slots: List<FormatInitialHoldingSlotEntity>) { slots.forEach { db.exec("INSERT OR REPLACE INTO format_initial_holding_slots (format_id,slot_id,label,required) VALUES (?,?,?,?)", listOf(it.formatId, it.slotId, it.label, it.required)) } }
    override suspend fun insertEligibleCards(cards: List<FormatInitialHoldingEligibleCardEntity>) { cards.forEach { db.exec("INSERT OR REPLACE INTO format_initial_holding_eligible_cards (format_id,slot_id,card_title) VALUES (?,?,?)", listOf(it.formatId, it.slotId, it.cardTitle)) } }
    override suspend fun deleteAllSlots() { db.exec("DELETE FROM format_initial_holding_slots") }
    override suspend fun deleteAllEligibleCards() { db.exec("DELETE FROM format_initial_holding_eligible_cards") }
    override suspend fun getAllEligibleCardSlots(): List<EligibleCardSlot> = db.query("SELECT card_title, slot_id FROM format_initial_holding_eligible_cards") { EligibleCardSlot(it.getString("card_title"), it.getString("slot_id")) }
    override fun observeRequiredSlots(): Flow<List<FormatInitialHoldingSlotEntity>> = flow { emit(db.query("SELECT * FROM format_initial_holding_slots WHERE required = 1") { FormatInitialHoldingSlotEntity(it.getString("format_id"), it.getString("slot_id"), it.getString("label"), it.getInt("required") == 1) }) }
    override fun countRequiredSlots(): Flow<Int> = flow { emit(db.scalarInt("SELECT COUNT(*) FROM format_initial_holding_slots WHERE required = 1")) }
}

// ─────────────────────────────────────────────────────────────────────────────
// Serialización de resultados
// ─────────────────────────────────────────────────────────────────────────────
private fun addResultJson(r: AddCardResult): JsonObject = when (r) {
    AddCardResult.Success -> JsonObject(mapOf("result" to JsonPrimitive("Success")))
    AddCardResult.Banned -> JsonObject(mapOf("result" to JsonPrimitive("Banned")))
    AddCardResult.NoActiveDeck -> JsonObject(mapOf("result" to JsonPrimitive("NoActiveDeck")))
    AddCardResult.SlotAlreadyCovered -> JsonObject(mapOf("result" to JsonPrimitive("SlotAlreadyCovered")))
    is AddCardResult.LimitReached -> JsonObject(mapOf("result" to JsonPrimitive("LimitReached"), "max" to JsonPrimitive(r.max)))
}

fun main(args: Array<String>) = runBlocking {
    val dataDir = File(args[0]); val outFile = File(args[1]); val dbFile = File(args[2]); val commit = args.getOrElse(3){"unknown"}
    val json = Json { ignoreUnknownKeys = true; isLenient = true; coerceInputValues = true }
    if (dbFile.exists()) dbFile.delete()
    val conn = DriverManager.getConnection("jdbc:sqlite:${dbFile.absolutePath.replace('\\','/')}")
    conn.autoCommit = false
    val db = Db(conn)
    conn.createStatement().use { st -> DDL.forEach { st.execute(it) }; st.execute("CREATE INDEX index_cards_name ON cards(name)") }

    val cardDao = JdbcCardDao(db)
    val deckDao = JdbcDeckDao(db)
    val groupDao = JdbcDeckGroupDao(db)
    val cardGroupDao = JdbcDeckCardGroupDao(db)
    val holdingDao = JdbcFormatInitialHoldingDao(db)

    // 1. catálogo real (CardImporter + RulesImporter + slots de filters.json)
    val filters = json.decodeFromString<FiltersJson>(dataDir.resolve("filters.json").readText())
    val slots = filters.formats.flatMap { f -> f.initialHoldingSlots.map { FormatInitialHoldingSlotEntity(f.code, it.id, it.label, it.required) } }
    val eligible = filters.formats.flatMap { f -> f.initialHoldingSlots.flatMap { s -> s.eligibleCards.map { FormatInitialHoldingEligibleCardEntity(f.code, s.id, it) } } }
    holdingDao.insertSlots(slots); holdingDao.insertEligibleCards(eligible)
    val initialHoldingByCard = holdingDao.getAllEligibleCardSlots().associate { it.cardTitle to it.slotId }

    val rulesImporter = RulesImporter(json, cardDao)
    val rulesMap = rulesImporter.parse(dataDir.resolve("rules.json").readText())
    val importer = CardImporter(com.guillermoc.l5argentina.data.db.AppDatabase(), cardDao, json)
    val imported = importer.import(dataDir.resolve("cards-2.3.1.json").readText(), rulesMap, initialHoldingByCard)
    // rules aplicadas post-import (igual que el sync real: database → rules)
    rulesImporter.applyRules(dataDir.resolve("rules.json").readText())
    conn.commit()
    println("cartas importadas: $imported ; slots requeridos: ${slots.count{it.required}}")

    val repo = DeckRepository(deckDao, cardDao, groupDao, cardGroupDao, holdingDao)

    // Cartas concretas (verificadas contra la DB)
    val STRONGHOLD_CRAB = "Samurai395"        // The Seventh Tower — starting_honor=3, province_strength=8, gold=4
    val STRONGHOLD_ZERO = "Tomorrow054"       // Barracks of the Damned — starting_honor=0
    val STRONGHOLD_LION = "Samurai401"        // The Hall of Ancestors — starting_honor=7
    val PERSONALITY_MAX3 = "TDoW012"          // Hida Akeno — max 3
    val HOLDING_JADE = "Ivory032"             // Jade Works — max 3
    val UNIQUE_EVENT = "P232"                 // Redrawing the Maps — Unique, max 1
    val BANNED = "ALitS117"                   // A Kensai's Art — banned
    val STRATEGY_MAX3 = "P410"                // A Brave New World — max 3
    val BORDER_KEEP = "Emperor028"            // holding, slot 'border'
    val BORDER_KEEP_EXP = "Ivory402"          // holding, slot 'border'
    val BAMBOO = "HaT001"                     // holding, slot 'bamboo'

    val cases = mutableListOf<JsonObject>()
    var deckSeq = 0
    suspend fun newDeck(type: String = "deck"): String = repo.createDeck("Golden ${deckSeq++}", type = type)

    fun caseObj(name: String, note: String, ops: List<JsonObject>) =
        JsonObject(mapOf("name" to JsonPrimitive(name), "note" to JsonPrimitive(note), "operations" to JsonArray(ops)))
    fun op(vararg pairs: Pair<String, Any?>): JsonObject = JsonObject(pairs.associate { (k, v) -> k to when (v) {
        null -> JsonNull; is String -> JsonPrimitive(v); is Int -> JsonPrimitive(v); is Boolean -> JsonPrimitive(v)
        is JsonObject -> v; is JsonArray -> v; else -> JsonPrimitive(v.toString()) } })

    // ── addCard ────────────────────────────────────────────────────────────────
    run { val d = newDeck(); val r = repo.addCard(d, BANNED)
        cases += caseObj("add-banned-rechazado", "addCard sobre carta con banned=1 → Banned; ni siquiera consulta cantidad.", listOf(op("addCard" to BANNED, "expect" to addResultJson(r)))) }

    run { val d = newDeck(); val r = repo.addCard(d, PERSONALITY_MAX3)
        cases += caseObj("add-normal-exito", "addCard sobre carta normal en deck vacío → Success, quantity=1.", listOf(op("addCard" to PERSONALITY_MAX3, "expect" to addResultJson(r), "quantityAfter" to (deckDao.getDeckCard(d, PERSONALITY_MAX3)?.quantity ?: 0)))) }

    run { val d = newDeck(); val ops = mutableListOf<JsonObject>()
        repeat(4) { i -> val r = repo.addCard(d, PERSONALITY_MAX3); ops += op("addCard" to PERSONALITY_MAX3, "call" to (i+1), "expect" to addResultJson(r)) }
        cases += caseObj("add-max-copias-deck", "4× addCard de una carta max_copies=3 → Success×3, luego LimitReached(3).", ops) }

    run { val d = newDeck(); val o1 = repo.addCard(d, UNIQUE_EVENT); val o2 = repo.addCard(d, UNIQUE_EVENT)
        cases += caseObj("add-unique-max1", "Carta con keyword Unique (max_copies=1 asignado en import) → 2º addCard LimitReached(1).", listOf(op("addCard" to UNIQUE_EVENT, "expect" to addResultJson(o1)), op("addCard" to UNIQUE_EVENT, "expect" to addResultJson(o2)))) }

    run { val d = newDeck(type = "list"); val o1 = repo.addCard(d, PERSONALITY_MAX3); val o2 = repo.addCard(d, PERSONALITY_MAX3)
        cases += caseObj("add-lista-tope-1", "En un mazo type=list, maxCopies se fuerza a 1 sin importar card.maxCopies → 2º addCard LimitReached(1). DIFERENCIA CLAVE con una lista simple sin tope.", listOf(op("deckType" to "list"), op("addCard" to PERSONALITY_MAX3, "expect" to addResultJson(o1)), op("addCard" to PERSONALITY_MAX3, "expect" to addResultJson(o2)))) }

    run { val d = newDeck(); val r = repo.addCard(d, "NO_EXISTE_999")
        cases += caseObj("add-card-inexistente", "⚠️ addCard con cardId inexistente → NoActiveDeck (nombre engañoso: significa 'carta no encontrada', no 'sin mazo activo').", listOf(op("addCard" to "NO_EXISTE_999", "expect" to addResultJson(r)))) }

    run { val r = repo.addCard("deck-inexistente-xyz", PERSONALITY_MAX3)
        cases += caseObj("add-deck-inexistente", "addCard sobre deckId inexistente → NoActiveDeck (la carta existe, el mazo no).", listOf(op("addCard" to PERSONALITY_MAX3, "onDeck" to "deck-inexistente-xyz", "expect" to addResultJson(r)))) }

    run { val d = newDeck(); val r = repo.addCard(d, BORDER_KEEP)
        cases += caseObj("add-holding-inicial-ok", "addCard de una carta con initial_holding_slot cuyo slot está libre → Success.", listOf(op("addCard" to BORDER_KEEP, "expect" to addResultJson(r)))) }

    run { val d = newDeck(); val o1 = repo.addCard(d, BORDER_KEEP); val o2 = repo.addCard(d, BORDER_KEEP_EXP)
        cases += caseObj("add-holding-slot-ocupado", "Dos cartas distintas del MISMO slot ('border') → 2ª addCard SlotAlreadyCovered (exclusividad de holding inicial).", listOf(op("addCard" to BORDER_KEEP, "expect" to addResultJson(o1)), op("addCard" to BORDER_KEEP_EXP, "expect" to addResultJson(o2)))) }

    run { val d = newDeck(); val o1 = repo.addCard(d, BORDER_KEEP); val o2 = repo.addCard(d, BAMBOO)
        cases += caseObj("add-holding-slots-distintos", "Cartas de slots distintos ('border' + 'bamboo') → ambas Success.", listOf(op("addCard" to BORDER_KEEP, "expect" to addResultJson(o1)), op("addCard" to BAMBOO, "expect" to addResultJson(o2)))) }

    run { val d = newDeck(); val o1 = repo.addCard(d, STRONGHOLD_CRAB); val o2 = repo.addCard(d, STRONGHOLD_LION)
        cases += caseObj("add-dos-strongholds", "⚠️ addCard de DOS strongholds distintos → ambas Success. NO existe validación de 'un solo stronghold por mazo'.", listOf(op("addCard" to STRONGHOLD_CRAB, "expect" to addResultJson(o1)), op("addCard" to STRONGHOLD_LION, "expect" to addResultJson(o2)))) }

    // ── importCards ──────────────────────────────────────────────────────────────
    run { val d = newDeck(); val r = repo.importCards(d, "3 Hida Akeno\n2 Jade Works")
        cases += caseObj("import-basico", "Import '3 Hida Akeno\\n2 Jade Works' → imported=5, notFound=0.", listOf(op("import" to "3 Hida Akeno\\n2 Jade Works", "imported" to r.imported, "notFound" to r.notFound))) }

    run { val d = newDeck(); val r = repo.importCards(d, "5 Hida Akeno")
        cases += caseObj("import-clamp-max", "Import '5 Hida Akeno' (max 3) → imported=3 (clampeado a maxCopies), notFound=0.", listOf(op("import" to "5 Hida Akeno", "imported" to r.imported, "notFound" to r.notFound))) }

    run { val d = newDeck(); val r = repo.importCards(d, "1 A Kensai's Art")
        cases += caseObj("import-baneada", "Import de carta baneada → imported=0, notFound=1, notFoundNames=[A Kensai's Art] (baneada cuenta como notFound).", listOf(op("import" to "1 A Kensai's Art", "imported" to r.imported, "notFound" to r.notFound, "notFoundNames" to JsonArray(r.notFoundNames.map { JsonPrimitive(it) })))) }

    run { val d = newDeck(); val r = repo.importCards(d, "1 Zzz No Existe")
        cases += caseObj("import-desconocida", "Import de nombre inexistente → notFound=1.", listOf(op("import" to "1 Zzz No Existe", "imported" to r.imported, "notFound" to r.notFound, "notFoundNames" to JsonArray(r.notFoundNames.map { JsonPrimitive(it) })))) }

    run { val d = newDeck(type = "list"); val r = repo.importCards(d, "3 Hida Akeno")
        cases += caseObj("import-lista-tope-1", "Import '3 Hida Akeno' en type=list → imported=1 (tope 1 por carta en listas).", listOf(op("deckType" to "list"), op("import" to "3 Hida Akeno", "imported" to r.imported, "notFound" to r.notFound))) }

    run { val d = newDeck(); val r = repo.importCards(d, "1 Border Keep\n1 Border Keep - exp")
        cases += caseObj("import-holding-conflicto", "Import de 2 cartas del mismo slot 'border' → imported=1, notFound=1 (la 2ª rechazada por slot ya cubierto en la sesión de import).", listOf(op("import" to "1 Border Keep\\n1 Border Keep - exp", "imported" to r.imported, "notFound" to r.notFound, "notFoundNames" to JsonArray(r.notFoundNames.map { JsonPrimitive(it) })))) }

    run { val d = newDeck()
        val text = "# sort_mode: custom\n# Mi nota personal\n# Golden - (40) [20]\n2 Hida Akeno"
        val r = repo.importCards(d, text)
        val deck = deckDao.getDeckById(d)
        cases += caseObj("import-notas-headers-sortmode", "Import con '# sort_mode:' (aplica y NO va a notas), '# Mi nota' (va a notas), '# X - (N) [N]' (header auto, se ignora), y cartas. Verifica notes/sort_mode resultantes.",
            listOf(op("import" to text.replace("\n","\\n"), "imported" to r.imported), op("notesAfter" to (deck?.notes ?: ""), "sortModeAfter" to (deck?.sortMode ?: "")))) }

    run { val d = newDeck(); val r = repo.importCards(d, "Hida Akeno")
        cases += caseObj("import-qty-implicita", "Línea sin número → cantidad implícita 1. 'Hida Akeno' → imported=1.", listOf(op("import" to "Hida Akeno", "imported" to r.imported))) }

    // ── exportDeck (round-trip) ──────────────────────────────────────────────────
    run {
        val d = newDeck()
        repo.addCard(d, STRONGHOLD_CRAB)         // starter/stronghold
        repo.importCards(d, "3 Hida Akeno\n2 Jade Works\n2 A Brave New World")  // dynasty + fate
        val exportText = repo.exportDeck(d)
        // Re-import en un mazo nuevo y comparar conteo total
        val d2 = newDeck()
        val reimport = repo.importCards(d2, exportText)
        val total1 = deckDao.getTotalCardCountValue(d)
        val total2 = deckDao.getTotalCardCountValue(d2)
        cases += caseObj("export-roundtrip", "Deck stronghold+dynasty+fate exportado y reimportado en mazo nuevo → mismo total de cartas. Incluye el string exacto de export.",
            listOf(op("exportText" to exportText), op("totalOriginal" to total1, "reimported" to reimport.imported, "totalReimport" to total2))) }

    // ── conteos de validez (espejo de DeckDetailViewModel) ───────────────────────
    run {
        val d = newDeck()
        repo.addCard(d, STRONGHOLD_CRAB)
        repo.importCards(d, "3 Hida Akeno\n2 Jade Works\n3 A Brave New World")
        val cardsList = deckDao.getDeckCardsWithCards(d).first().mapNotNull { row -> row.card?.let { it to row.deckCard } }
        val dynasty = cardsList.filter { it.first.deckGroup == "dynasty" }.sumOf { it.second.quantity }
        val fate = cardsList.filter { it.first.deckGroup == "fate" }.sumOf { it.second.quantity }
        cases += caseObj("conteos-dynasty-fate", "Espejo de DeckDetailViewModel.dynastyCount/fateCount: suma de quantity por deck_group (solo quantity>0). El indicador de UI pinta ROJO si el conteo es menor a 40 pero NO bloquea nada.",
            listOf(op("dynastyCount" to dynasty, "fateCount" to fate, "sideTarget" to 40, "enforced" to false))) }

    // ── slots requeridos sin cubrir (espejo de uncoveredRequiredSlots) ───────────
    run {
        val d = newDeck()
        val required = repo.getRequiredSlots().first()
        suspend fun uncovered(deckId: String): List<String> {
            val covered = deckDao.getDeckCardsWithCards(deckId).first()
                .mapNotNull { row -> row.card?.takeIf { row.deckCard.quantity > 0 }?.initialHoldingSlot }.toSet()
            return required.filter { it.slotId !in covered }.map { it.slotId }
        }
        val before = uncovered(d)
        repo.addCard(d, BORDER_KEEP)
        val afterBorder = uncovered(d)
        cases += caseObj("slots-requeridos-sin-cubrir", "Espejo de uncoveredRequiredSlots: slots required=1 sin carta elegible en el mazo. Formato samurai pide 'bamboo'+'border'. Solo AVISA (PendingSlotRow), no bloquea guardar.",
            listOf(op("requiredSlots" to JsonArray(required.map { JsonPrimitive(it.slotId) }), "uncoveredEmpty" to JsonArray(before.map { JsonPrimitive(it) })),
                   op("afterAddBorderKeep" to JsonArray(afterBorder.map { JsonPrimitive(it) })))) }

    // ── honor inicial (espejo de GoldfishViewModel.buildInitialState) ────────────
    run {
        val stronghold = cardDao.getById(STRONGHOLD_CRAB)!!
        cases += caseObj("stat-inicial-honor-stronghold", "Espejo de GoldfishViewModel: honorCounter = startingHonor del PRIMER stronghold. The Seventh Tower → 3. ⚠️ gold_production y province_strength NO se agregan en ningún lado.",
            listOf(op("card" to STRONGHOLD_CRAB, "startingHonor" to stronghold.startingHonor, "provinceStrength" to stronghold.provinceStrength, "goldProduction" to stronghold.goldProduction, "honorCounterInGoldfish" to (stronghold.startingHonor ?: 0)))) }

    run {
        val z = cardDao.getById(STRONGHOLD_ZERO)!!
        cases += caseObj("stat-inicial-honor-cero", "Barracks of the Damned tiene starting_honor=0 → honorCounter=0 (distinto de 'sin stronghold', que también da 0 por el ?:0).",
            listOf(op("card" to STRONGHOLD_ZERO, "startingHonor" to z.startingHonor, "honorCounterInGoldfish" to (z.startingHonor ?: 0)))) }

    run {
        // dos strongholds: Goldfish toma firstOrNull{ type=="stronghold" } tras ordenar por starterRank+slot+name
        val a = cardDao.getById(STRONGHOLD_CRAB)!!; val b = cardDao.getById(STRONGHOLD_LION)!!
        cases += caseObj("stat-inicial-honor-multi-stronghold", "⚠️ Con 2 strongholds en el mazo, Goldfish usa el startingHonor del primero tras ordenar por (starterRank, slot, name). Documenta ambigüedad: qué stronghold gana depende del orden alfabético del nombre.",
            listOf(op("strongholdA" to STRONGHOLD_CRAB, "nameA" to a.name, "honorA" to a.startingHonor), op("strongholdB" to STRONGHOLD_LION, "nameB" to b.name, "honorB" to b.startingHonor),
                   op("goldfishPicksByNameOrder" to (if (a.name < b.name) a.name else b.name)))) }

    conn.commit()

    val root = JsonObject(mapOf(
        "generatedFrom" to JsonPrimitive(commit),
        "cardsVersion" to JsonPrimitive("2.3.1"),
        "rulesVersion" to JsonPrimitive("2.3.1"),
        "filtersVersion" to JsonPrimitive("2.3.0"),
        "generator" to JsonPrimitive("Harness JVM: DeckRepository + CardImporter + RulesImporter reales (kotlinc 2.0.21) sobre SQLite (sqlite-jdbc). DAOs = dobles JDBC que replican el SQL de cada @Query. Datos production 2.3.x."),
        "cardsUsed" to JsonObject(mapOf(
            "STRONGHOLD_CRAB (The Seventh Tower, honor 3)" to JsonPrimitive(STRONGHOLD_CRAB),
            "STRONGHOLD_ZERO (Barracks of the Damned, honor 0)" to JsonPrimitive(STRONGHOLD_ZERO),
            "STRONGHOLD_LION (The Hall of Ancestors, honor 7)" to JsonPrimitive(STRONGHOLD_LION),
            "PERSONALITY_MAX3 (Hida Akeno)" to JsonPrimitive(PERSONALITY_MAX3),
            "HOLDING (Jade Works)" to JsonPrimitive(HOLDING_JADE),
            "UNIQUE_EVENT (Redrawing the Maps, max 1)" to JsonPrimitive(UNIQUE_EVENT),
            "BANNED (A Kensai's Art)" to JsonPrimitive(BANNED),
            "STRATEGY_MAX3 (A Brave New World)" to JsonPrimitive(STRATEGY_MAX3),
            "BORDER_KEEP (slot border)" to JsonPrimitive(BORDER_KEEP),
            "BORDER_KEEP_EXP (slot border)" to JsonPrimitive(BORDER_KEEP_EXP),
            "BAMBOO (slot bamboo)" to JsonPrimitive(BAMBOO)
        )),
        "notes" to JsonArray(listOf(
            "operations = secuencia real ejecutada; 'expect' es el AddCardResult devuelto por DeckRepository.addCard.",
            "Todos los mazos arrancan vacíos; type=deck salvo indicación 'deckType: list'.",
            "conteos/slots/honor son ESPEJOS de expresiones de ViewModel/Goldfish (citadas en las specs), computadas con los mismos predicados.",
            "SIDE_TARGET=40 y los slots requeridos son indicadores de UI: nada bloquea guardar un mazo inválido."
        ).map { JsonPrimitive(it) }),
        "cases" to JsonArray(cases)
    ))
    outFile.parentFile?.mkdirs()
    outFile.writeText(Json { prettyPrint = true }.encodeToString(JsonObject.serializer(), root))
    conn.commit(); conn.close()
    println("golden tests escritos: ${outFile.absolutePath} (${cases.size} casos)")
}

// Helper: total count sin exponer el Flow crudo
private suspend fun DeckDao.getTotalCardCountValue(deckId: String): Int =
    getTotalCardCount(deckId).first() ?: 0
