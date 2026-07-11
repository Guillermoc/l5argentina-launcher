package com.guillermoc.l5argentina.data.repository

import com.guillermoc.l5argentina.data.db.dao.CardDao
import com.guillermoc.l5argentina.data.db.dao.DeckCardGroupDao
import com.guillermoc.l5argentina.data.db.dao.DeckDao
import com.guillermoc.l5argentina.data.db.dao.DeckGroupDao
import com.guillermoc.l5argentina.data.db.dao.FormatInitialHoldingDao
import com.guillermoc.l5argentina.data.db.entity.DeckCardEntity
import com.guillermoc.l5argentina.data.db.entity.DeckCardGroupEntity
import com.guillermoc.l5argentina.data.db.entity.DeckEntity
import com.guillermoc.l5argentina.data.db.entity.DeckGroupEntity
import com.guillermoc.l5argentina.data.db.entity.FormatInitialHoldingSlotEntity
import com.guillermoc.l5argentina.data.db.relation.DeckCardWithCard
import com.guillermoc.l5argentina.data.model.DeckGroup
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import java.util.UUID

class DeckRepository(
    private val deckDao: DeckDao,
    private val cardDao: CardDao,
    private val deckGroupDao: DeckGroupDao,
    private val deckCardGroupDao: DeckCardGroupDao,
    private val formatInitialHoldingDao: FormatInitialHoldingDao
) {

    // -------------------------------------------------------------------------
    // Deck CRUD
    // -------------------------------------------------------------------------

    fun getAllDecks(): Flow<List<DeckEntity>> = deckDao.getAllDecks()

    suspend fun getDeckById(id: String): DeckEntity? = deckDao.getDeckById(id)

    suspend fun createDeck(
        name: String,
        type: String = "deck",
        listName: String? = null,
        listDescription: String? = null,
        groupId: String? = null
    ): String {
        val id = UUID.randomUUID().toString()
        val maxSort = deckDao.getAllDecks().first()
            .filter { it.type == type }
            .maxOfOrNull { it.sortOrder } ?: -1
        deckDao.insertDeck(
            DeckEntity(
                id = id,
                name = name,
                type = type,
                listName = listName,
                listDescription = listDescription,
                groupId = groupId,
                sortOrder = maxSort + 1
            )
        )
        return id
    }

    suspend fun deleteDeck(id: String) = deckDao.deleteDeck(id)

    suspend fun deleteAllDecks() = deckDao.deleteAllDecks()

    suspend fun renameDeck(id: String, name: String) = deckDao.renameDeck(id, name)

    suspend fun updateNotes(deckId: String, notes: String?) = deckDao.updateNotes(deckId, notes)

    suspend fun assignDeckToGroup(deckId: String, groupId: String?) =
        deckDao.updateGroupId(deckId, groupId)

    suspend fun reorderDecks(updates: List<Pair<String, Int>>) {
        updates.forEach { (id, order) -> deckDao.updateSortOrder(id, order) }
    }

    suspend fun updateSortMode(deckId: String, sortMode: String?) =
        deckDao.updateSortMode(deckId, sortMode)

    suspend fun reorderCards(updates: List<Pair<String, Int>>, deckId: String) {
        updates.forEach { (cardId, order) -> deckDao.updateCardSortOrder(deckId, cardId, order) }
    }

    // -------------------------------------------------------------------------
    // Deck groups (user-defined, per section type)
    // -------------------------------------------------------------------------

    fun getAllGroups(): Flow<List<DeckGroupEntity>> = deckGroupDao.getAllGroups()

    fun getGroupsBySection(sectionType: String): Flow<List<DeckGroupEntity>> =
        deckGroupDao.getGroupsBySection(sectionType)

    suspend fun createGroup(name: String, sectionType: String): String {
        val id = UUID.randomUUID().toString()
        deckGroupDao.insert(DeckGroupEntity(id = id, name = name, sectionType = sectionType))
        return id
    }

    suspend fun renameGroup(groupId: String, name: String) = deckGroupDao.rename(groupId, name)

    suspend fun deleteGroup(groupId: String) {
        deckGroupDao.unlinkDecks(groupId)
        deckGroupDao.delete(groupId)
    }

    suspend fun deleteAllGroups() = deckGroupDao.deleteAll()

    // -------------------------------------------------------------------------
    // Card queries
    // -------------------------------------------------------------------------

    // Las filas huérfanas (card_id sin carta en el catálogo: catálogo vacío o
    // carta eliminada) se filtran acá; la fila de deck_cards se conserva y la
    // carta reaparece cuando el catálogo se repuebla.
    fun getDeckCardsWithCards(deckId: String): Flow<List<DeckCardWithCard>> =
        deckDao.getDeckCardsWithCards(deckId).map { rows ->
            rows.mapNotNull { row -> row.card?.let { DeckCardWithCard(row.deckCard, it) } }
        }

    fun getAllDeckCardsWithCards(): Flow<List<DeckCardWithCard>> =
        deckDao.getAllDeckCardsWithCards().map { rows ->
            rows.mapNotNull { row -> row.card?.let { DeckCardWithCard(row.deckCard, it) } }
        }

    fun getTotalCardCount(deckId: String): Flow<Int?> =
        deckDao.getTotalCardCount(deckId)

    // Huérfanas del mazo (ADR-013); emite vacío mientras el catálogo esté vacío
    fun getOrphanDeckCards(deckId: String): Flow<List<DeckCardEntity>> =
        deckDao.getOrphanDeckCards(deckId)

    fun getRequiredSlots(): Flow<List<FormatInitialHoldingSlotEntity>> =
        formatInitialHoldingDao.observeRequiredSlots()

    // -------------------------------------------------------------------------
    // Card mutations
    // -------------------------------------------------------------------------

    suspend fun addCard(deckId: String, cardId: String): AddCardResult {
        val card = cardDao.getById(cardId) ?: return AddCardResult.NoActiveDeck
        if (card.banned) return AddCardResult.Banned

        val deck = deckDao.getDeckById(deckId) ?: return AddCardResult.NoActiveDeck

        if (card.initialHoldingSlot != null) {
            if (deckDao.countCardsInSlot(deckId, card.initialHoldingSlot) > 0)
                return AddCardResult.SlotAlreadyCovered
        }

        val maxCopies = if (deck.type == "list") 1 else card.maxCopies
        val existing = deckDao.getDeckCard(deckId, cardId)
        val current = existing?.quantity ?: 0
        if (current >= maxCopies) return AddCardResult.LimitReached(maxCopies)

        val sortOrder = if (existing == null) deckDao.getMaxCardSortOrder(deckId) + 1 else existing.sortOrder
        deckDao.upsertDeckCard(DeckCardEntity(deckId, cardId, current + 1, sortOrder = sortOrder))
        return AddCardResult.Success
    }

    // keepRowAtZero = true (pantalla de mazo): quantity 0 = carta "recortada", la fila persiste.
    // keepRowAtZero = false (buscador): comportamiento histórico, la fila se borra al llegar a 0.
    suspend fun removeCard(deckId: String, cardId: String, keepRowAtZero: Boolean = false) {
        val existing = deckDao.getDeckCard(deckId, cardId) ?: return
        when {
            existing.quantity <= 0 -> return
            existing.quantity == 1 && !keepRowAtZero -> deckDao.deleteDeckCard(deckId, cardId)
            else -> deckDao.upsertDeckCard(existing.copy(quantity = existing.quantity - 1))
        }
    }

    // Eliminación dura: la entrada deja de existir en el mazo (distinto de recortar).
    suspend fun deleteCardEntry(deckId: String, cardId: String) =
        deckDao.deleteDeckCard(deckId, cardId)

    suspend fun removeAllTrimmed(deckId: String) = deckDao.deleteTrimmedCards(deckId)

    suspend fun setCardQuantity(deckId: String, cardId: String, n: Int, keepRowAtZero: Boolean = false) {
        require(n in 0..3)
        val existing = deckDao.getDeckCard(deckId, cardId)
        when {
            n == 0 && keepRowAtZero ->
                if (existing != null && existing.quantity != 0)
                    deckDao.upsertDeckCard(existing.copy(quantity = 0))
            n == 0 -> if (existing != null) deckDao.deleteDeckCard(deckId, cardId)
            existing == null -> {
                val sortOrder = deckDao.getMaxCardSortOrder(deckId) + 1
                deckDao.upsertDeckCard(DeckCardEntity(deckId, cardId, n, sortOrder = sortOrder))
            }
            else -> deckDao.upsertDeckCard(existing.copy(quantity = n))
        }
    }

    // -------------------------------------------------------------------------
    // Card groups (subgroups within deck detail sections)
    // -------------------------------------------------------------------------

    fun getCardGroupsForDeck(deckId: String): Flow<List<DeckCardGroupEntity>> =
        deckCardGroupDao.getGroupsForDeck(deckId)

    suspend fun createCardGroup(deckId: String, name: String, parentGroup: String): String {
        val id = UUID.randomUUID().toString()
        deckCardGroupDao.insert(DeckCardGroupEntity(id = id, deckId = deckId, name = name, parentGroup = parentGroup))
        return id
    }

    suspend fun renameCardGroup(groupId: String, name: String) = deckCardGroupDao.rename(groupId, name)

    suspend fun deleteCardGroup(groupId: String) {
        deckCardGroupDao.unlinkCards(groupId)
        deckCardGroupDao.delete(groupId)
    }

    suspend fun assignCardToGroup(deckId: String, cardId: String, cardGroupId: String?) =
        deckDao.updateCardGroupId(deckId, cardId, cardGroupId)

    // -------------------------------------------------------------------------
    // Import
    // -------------------------------------------------------------------------

    suspend fun importCards(deckId: String, text: String): ImportResult {
        val deck = deckDao.getDeckById(deckId) ?: return ImportResult(0, 0)
        val isList = deck.type == "list"
        var imported = 0
        var notFound = 0
        val notFoundNames = mutableListOf<String>()
        val noteLines = mutableListOf<String>()
        val lineRegex = Regex("""^(\d+)\s+(.+)$""")
        val coveredSlots = mutableSetOf<String>()

        val sortModeRegex = Regex("""^#\s*sort_mode:\s*(\S+)$""")
        val headerRegex = Regex("""^.*\s*-\s*\(\d+\)\s*\[\d+\]$""")
        for (line in text.lines()) {
            val trimmed = line.trim()
            if (trimmed.isBlank()) continue
            if (trimmed.startsWith("#")) {
                val sortModeMatch = sortModeRegex.find(trimmed)
                if (sortModeMatch != null) {
                    deckDao.updateSortMode(deckId, sortModeMatch.groupValues[1])
                } else {
                    val content = trimmed.removePrefix("#").trim()
                    if (!headerRegex.matches(content)) noteLines.add(content)
                }
                continue
            }

            val match = lineRegex.find(trimmed)
            val qty: Int
            val name: String
            if (match != null) {
                qty = match.groupValues[1].toIntOrNull() ?: continue
                name = match.groupValues[2].trim()
            } else {
                qty = 1
                name = trimmed
            }

            val card = cardDao.getByName(name)
            if (card == null || card.banned) { notFound++; notFoundNames.add(name); continue }

            if (card.initialHoldingSlot != null) {
                val alreadyInDeck = deckDao.countCardsInSlot(deckId, card.initialHoldingSlot) > 0
                if (alreadyInDeck || card.initialHoldingSlot in coveredSlots) {
                    notFound++; notFoundNames.add(name); continue
                }
                coveredSlots.add(card.initialHoldingSlot)
            }

            val maxCopies = if (isList) 1 else card.maxCopies
            val existing = deckDao.getDeckCard(deckId, card.id)
            val current = existing?.quantity ?: 0
            val allowed = qty.coerceAtMost(maxCopies - current)

            if (allowed > 0) {
                deckDao.upsertDeckCard(DeckCardEntity(deckId, card.id, current + allowed))
                imported += allowed
            }
        }
        if (noteLines.isNotEmpty()) {
            val combined = noteLines.joinToString("\n")
            val existing = deckDao.getDeckById(deckId)?.notes
            deckDao.updateNotes(deckId, if (existing.isNullOrBlank()) combined else "$existing\n$combined")
        }
        return ImportResult(imported, notFound, notFoundNames)
    }

    // -------------------------------------------------------------------------
    // Export
    // -------------------------------------------------------------------------

    suspend fun exportDeck(deckId: String): String {
        val deck = deckDao.getDeckById(deckId) ?: return ""
        // Las recortadas (quantity = 0) nunca se exportan
        val entries = getDeckCardsWithCards(deckId).toSnapshot()
            .filter { it.deckCard.quantity > 0 }
        val sb = StringBuilder()

        deck.notes?.takeIf { it.isNotBlank() }?.lines()?.forEach { sb.appendLine("# $it") }

        val totalCards = entries.sumOf { it.deckCard.quantity }
        val totalDistinct = entries.size
        sb.appendLine("# ${deck.name} - ($totalCards) [$totalDistinct]")

        DeckGroup.entries.forEach { group ->
            val groupEntries = entries.filter {
                it.card.deckGroup.equals(group.key, ignoreCase = true)
            }.sortedWith(compareBy({ it.card.type }, { it.card.name }))
            if (groupEntries.isEmpty()) return@forEach
            val gTotal = groupEntries.sumOf { it.deckCard.quantity }
            val gDistinct = groupEntries.size
            sb.appendLine("# ${group.key.replaceFirstChar { it.uppercase() }} - ($gTotal) [$gDistinct]")
            groupEntries.groupBy { it.card.type }.forEach { (type, typeEntries) ->
                val tTotal = typeEntries.sumOf { it.deckCard.quantity }
                val tDistinct = typeEntries.size
                sb.appendLine("# ${type.replaceFirstChar { it.uppercase() }} - ($tTotal) [$tDistinct]")
                typeEntries.forEach { entry ->
                    val q = entry.deckCard.quantity
                    if (q == 1) sb.appendLine(entry.card.name)
                    else sb.appendLine("$q ${entry.card.name}")
                }
            }
        }
        return sb.toString().trimEnd()
    }

    private suspend fun Flow<List<DeckCardWithCard>>.toSnapshot(): List<DeckCardWithCard> = first()

}
