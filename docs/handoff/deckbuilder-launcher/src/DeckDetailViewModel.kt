// ui/screens/deckdetail/DeckDetailViewModel.kt
package com.guillermoc.l5argentina.ui.screens.deckdetail

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.guillermoc.l5argentina.data.datastore.AppPreferences
import com.guillermoc.l5argentina.data.db.entity.DeckCardEntity
import com.guillermoc.l5argentina.data.db.entity.DeckEntity
import com.guillermoc.l5argentina.data.db.entity.DeckGroupEntity
import com.guillermoc.l5argentina.data.db.relation.DeckCardWithCard
import com.guillermoc.l5argentina.data.repository.DeckRepository
import com.guillermoc.l5argentina.data.repository.UserCardDataRepository
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

// Orden dentro de cada subtipo (estado de UI, no se persiste)
enum class DeckCardSort { GOLD_COST, NAME, FORCE }

sealed class DeckDetailModalState {
    data object Closed : DeckDetailModalState()
    // Deck operation modals
    data class ImportDeck(val deckName: String) : DeckDetailModalState()
    data class ImportResult(val result: com.guillermoc.l5argentina.data.repository.ImportResult) : DeckDetailModalState()
    data class RenameDeck(val currentName: String) : DeckDetailModalState()
    data class EditNotes(val currentNotes: String) : DeckDetailModalState()
    data class AssignDeckToGroup(val currentGroupId: String?, val sectionType: String) : DeckDetailModalState()
    data class DeleteConfirm(val deckName: String) : DeckDetailModalState()
    // Recorte
    data class ClearTrimmedConfirm(val count: Int) : DeckDetailModalState()
    // Huérfanas (ADR-013): cartas del mazo ausentes del catálogo actual
    data object OrphanCards : DeckDetailModalState()
}

private val TYPE_ORDER_MAP = mapOf(
    "starter" to listOf("stronghold", "sensei", "wind", "holding"),
    "dynasty" to listOf("celestial", "event", "region", "holding", "personality"),
    "fate"    to listOf("follower", "item", "spell", "strategy")
)

internal fun typeOrderInGroup(deckGroup: String, type: String): Int =
    TYPE_ORDER_MAP[deckGroup]?.indexOf(type)?.takeIf { it >= 0 } ?: Int.MAX_VALUE

internal fun groupOrder(group: String) = when (group) {
    "starter" -> 0; "dynasty" -> 1; "fate" -> 2; else -> 3
}

private fun sortComparator(sort: DeckCardSort, ascending: Boolean): Comparator<DeckCardWithCard> {
    val base = compareBy<DeckCardWithCard>(
        { groupOrder(it.card.deckGroup) },
        { typeOrderInGroup(it.card.deckGroup, it.card.type) }
    )
    // Solo el criterio de campo se invierte; el orden lado→subtipo es fijo
    val field: Comparator<DeckCardWithCard> = when (sort) {
        DeckCardSort.GOLD_COST -> compareBy { it.card.cost ?: Int.MAX_VALUE }
        DeckCardSort.NAME      -> compareBy { it.card.name }
        DeckCardSort.FORCE     -> compareBy { it.card.force ?: Int.MIN_VALUE }
    }
    return base.then(if (ascending) field else field.reversed()).thenBy { it.card.name }
}

class DeckDetailViewModel(
    val deckId: String,
    private val deckRepository: DeckRepository,
    private val userCardDataRepository: UserCardDataRepository,
    private val preferences: AppPreferences
) : ViewModel() {

    private val _deck = MutableStateFlow<DeckEntity?>(null)
    val deck: StateFlow<DeckEntity?> = _deck

    // ── Estado de UI de la grilla ─────────────────────────────────────────────

    private val _cardSort = MutableStateFlow(DeckCardSort.GOLD_COST)
    val cardSort: StateFlow<DeckCardSort> = _cardSort

    private val _sortAscending = MutableStateFlow(true)
    val sortAscending: StateFlow<Boolean> = _sortAscending

    private val _columns = MutableStateFlow(2)
    val columns: StateFlow<Int> = _columns

    private val _showFullCards = MutableStateFlow(false)
    val showFullCards: StateFlow<Boolean> = _showFullCards

    private val _showTrimmed = MutableStateFlow(false)
    val showTrimmed: StateFlow<Boolean> = _showTrimmed

    // Cartas recién recortadas, visibles durante el periodo de gracia (2.5s)
    private val _graceIds = MutableStateFlow<Set<String>>(emptySet())
    val graceIds: StateFlow<Set<String>> = _graceIds
    private val graceJobs = mutableMapOf<String, Job>()

    // Cartas con eliminación diferida (2.5s, tinte rojo); el [+] rescata
    private val _pendingDeleteIds = MutableStateFlow<Set<String>>(emptySet())
    val pendingDeleteIds: StateFlow<Set<String>> = _pendingDeleteIds
    private val deleteJobs = mutableMapOf<String, Job>()

    // ── Cartas (incluye recortadas; la visibilidad se decide en la UI) ────────

    val cards: StateFlow<List<DeckCardWithCard>> =
        combine(deckRepository.getDeckCardsWithCards(deckId), _cardSort, _sortAscending) { roomCards, sort, asc ->
            roomCards.sortedWith(sortComparator(sort, asc))
        }
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    val trimmedCount: StateFlow<Int> = cards
        .map { list -> list.count { it.deckCard.quantity == 0 } }
        .stateIn(viewModelScope, SharingStarted.Lazily, 0)

    // Huérfanas (ADR-013): vacío mientras el catálogo esté vacío (guard en el DAO)
    val orphanCards: StateFlow<List<DeckCardEntity>> =
        deckRepository.getOrphanDeckCards(deckId)
            .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    // Legalidad: solo quantity > 0 (la suma ya ignora las recortadas)
    val dynastyCount: StateFlow<Int> = combine(cards, _deck) { c, d ->
        if (d?.type == "list") 0
        else c.filter { it.card.deckGroup == "dynasty" }.sumOf { it.deckCard.quantity }
    }.stateIn(viewModelScope, SharingStarted.Lazily, 0)

    val fateCount: StateFlow<Int> = combine(cards, _deck) { c, d ->
        if (d?.type == "list") 0
        else c.filter { it.card.deckGroup == "fate" }.sumOf { it.deckCard.quantity }
    }.stateIn(viewModelScope, SharingStarted.Lazily, 0)

    val activeDeckId: StateFlow<String?> = preferences.activeDeckId
        .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    val favoriteIds: StateFlow<Set<String>> = userCardDataRepository.observeFavoriteIds()
        .stateIn(viewModelScope, SharingStarted.Eagerly, emptySet())

    val availableDeckGroups: StateFlow<List<DeckGroupEntity>> =
        deckRepository.getAllGroups()
            .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    val uncoveredRequiredSlots: StateFlow<List<Pair<String, String>>> =
        combine(cards, _deck, deckRepository.getRequiredSlots()) { deckCards, deck, required ->
            if (deck?.type == "list") return@combine emptyList()
            val coveredSlotIds = deckCards
                .filter { it.deckCard.quantity > 0 }
                .mapNotNull { it.card.initialHoldingSlot }.toSet()
            required.filter { it.slotId !in coveredSlotIds }.map { it.slotId to it.label }
        }
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    private val _modalState = MutableStateFlow<DeckDetailModalState>(DeckDetailModalState.Closed)
    val modalState: StateFlow<DeckDetailModalState> = _modalState

    private val _showStats = MutableStateFlow(false)
    val showStats: StateFlow<Boolean> = _showStats

    private val _viewerCardId = MutableStateFlow<String?>(null)
    val viewerCardId: StateFlow<String?> = _viewerCardId

    private val _notesText = MutableStateFlow("")
    val notesText: StateFlow<String> = _notesText

    private val _notesExpanded = MutableStateFlow(false)
    val notesExpanded: StateFlow<Boolean> = _notesExpanded

    private val _exportText = MutableStateFlow<String?>(null)
    val exportText: StateFlow<String?> = _exportText

    private var notesSaveJob: Job? = null

    init {
        viewModelScope.launch {
            val d = deckRepository.getDeckById(deckId)
            _deck.value = d
            val notes = d?.notes ?: ""
            _notesText.value = notes
            _notesExpanded.value = notes.isNotBlank()
        }
        viewModelScope.launch {
            _columns.value = preferences.deckColumns.first()
        }
    }

    // ── Grilla: columnas / orden / recortadas ─────────────────────────────────

    fun setColumns(n: Int) {
        val v = n.coerceIn(1, 3)
        _columns.value = v
        viewModelScope.launch { preferences.setDeckColumns(v) }
    }

    fun toggleShowFullCards() { _showFullCards.value = !_showFullCards.value }

    fun setCardSort(sort: DeckCardSort, ascending: Boolean) {
        _cardSort.value = sort
        _sortAscending.value = ascending
    }

    fun toggleShowTrimmed() {
        _showTrimmed.value = !_showTrimmed.value
        if (_showTrimmed.value) {
            // Todas visibles: los timers de gracia dejan de tener sentido
            graceJobs.values.forEach { it.cancel() }
            graceJobs.clear()
            _graceIds.value = emptySet()
        }
    }

    // ── Card qty ──────────────────────────────────────────────────────────────

    // El recorte persiste el 0 de inmediato; el timer de gracia es solo visual
    private fun startGrace(cardId: String) {
        if (_showTrimmed.value) return
        graceJobs.remove(cardId)?.cancel()
        graceJobs[cardId] = viewModelScope.launch {
            delay(2_500)
            _graceIds.value = _graceIds.value - cardId
            graceJobs.remove(cardId)
        }
        _graceIds.value = _graceIds.value + cardId
    }

    private fun cancelGrace(cardId: String) {
        graceJobs.remove(cardId)?.cancel()
        _graceIds.value = _graceIds.value - cardId
    }

    fun addCard(cardId: String) {
        // Rescate: cancela la gracia si estaba corriendo
        cancelGrace(cardId)
        // Rescate de eliminación diferida: cancela y conserva la cantidad actual
        if (cardId in deleteJobs) {
            restoreCard(cardId)
            return
        }
        viewModelScope.launch { deckRepository.addCard(deckId, cardId) }
    }

    // Revierte la eliminación diferida conservando la cantidad previa
    fun restoreCard(cardId: String) {
        deleteJobs.remove(cardId)?.let { job ->
            job.cancel()
            _pendingDeleteIds.value = _pendingDeleteIds.value - cardId
        }
    }

    fun removeCard(cardId: String) {
        if (cardId in deleteJobs) return
        val qty = cards.value.find { it.card.id == cardId }?.deckCard?.quantity ?: return
        if (qty <= 0) return
        if (qty == 1) {
            startGrace(cardId)
            viewModelScope.launch { deckRepository.removeCard(deckId, cardId, keepRowAtZero = true) }
        } else {
            viewModelScope.launch { deckRepository.removeCard(deckId, cardId) }
        }
    }

    // Drag-to-action: recortar directo a 0 desde cualquier cantidad
    fun trimCard(cardId: String) {
        if (cardId in deleteJobs) return
        val qty = cards.value.find { it.card.id == cardId }?.deckCard?.quantity ?: return
        if (qty <= 0) return
        startGrace(cardId)
        viewModelScope.launch { deckRepository.setCardQuantity(deckId, cardId, 0, keepRowAtZero = true) }
    }

    // Drag-to-action: fijar cantidad exacta (clampeada al máximo de copias)
    fun setCardQuantity(cardId: String, n: Int) {
        val item = cards.value.find { it.card.id == cardId } ?: return
        val max = if (_deck.value?.type == "list") 1 else item.card.maxCopies
        val target = n.coerceIn(0, max)
        if (target == 0) { trimCard(cardId); return }
        cancelGrace(cardId)
        viewModelScope.launch { deckRepository.setCardQuantity(deckId, cardId, target, keepRowAtZero = true) }
    }

    // ── Visor de carta (tap en celda; mismo modal que el buscador) ────────────

    fun openCardViewer(cardId: String) { _viewerCardId.value = cardId }
    fun closeCardViewer() { _viewerCardId.value = null }

    // ── Eliminar del mazo (sin confirmación: 2.5s de gracia con tinte rojo) ───

    fun deleteCard(cardId: String) {
        if (cardId in deleteJobs) return
        cancelGrace(cardId)
        if (_viewerCardId.value == cardId) _viewerCardId.value = null
        _pendingDeleteIds.value = _pendingDeleteIds.value + cardId
        deleteJobs[cardId] = viewModelScope.launch {
            delay(2_500)
            deckRepository.deleteCardEntry(deckId, cardId)
            _pendingDeleteIds.value = _pendingDeleteIds.value - cardId
            deleteJobs.remove(cardId)
        }
    }

    // ── Quitar todas las recortadas ───────────────────────────────────────────

    fun requestClearTrimmed() {
        val count = trimmedCount.value
        if (count > 0) _modalState.value = DeckDetailModalState.ClearTrimmedConfirm(count)
    }

    fun openOrphanCards() {
        if (orphanCards.value.isNotEmpty()) _modalState.value = DeckDetailModalState.OrphanCards
    }

    fun confirmClearTrimmed() {
        viewModelScope.launch {
            deckRepository.removeAllTrimmed(deckId)
            _modalState.value = DeckDetailModalState.Closed
        }
    }

    // ── Stats / notas / favoritos ─────────────────────────────────────────────

    fun openStats()  { _showStats.value = true }
    fun closeStats() { _showStats.value = false }

    fun toggleNotes() { _notesExpanded.value = !_notesExpanded.value }

    fun onNotesChange(text: String) {
        _notesText.value = text
        notesSaveJob?.cancel()
        notesSaveJob = viewModelScope.launch {
            delay(1_000)
            deckRepository.updateNotes(deckId, text.takeIf { it.isNotBlank() })
            _deck.value = _deck.value?.copy(notes = text.takeIf { it.isNotBlank() })
        }
    }

    fun toggleFavorite(cardId: String) {
        viewModelScope.launch {
            userCardDataRepository.setFavorite(cardId, !favoriteIds.value.contains(cardId))
        }
    }

    // ── Deck ops (menú ⋮) ─────────────────────────────────────────────────────

    fun openImport() {
        val deck = _deck.value ?: return
        _modalState.value = DeckDetailModalState.ImportDeck(deck.name)
    }

    fun runImport(text: String) {
        viewModelScope.launch {
            val result = deckRepository.importCards(deckId, text)
            // Refresh deck (notes might have changed)
            _deck.value = deckRepository.getDeckById(deckId)
            _notesText.value = _deck.value?.notes ?: ""
            _modalState.value = DeckDetailModalState.ImportResult(result)
        }
    }

    fun exportDeck() {
        viewModelScope.launch {
            _exportText.value = deckRepository.exportDeck(deckId)
        }
    }

    fun clearExportText() { _exportText.value = null }

    fun openRenameDeck() {
        val deck = _deck.value ?: return
        _modalState.value = DeckDetailModalState.RenameDeck(deck.name)
    }

    fun confirmRenameDeck(newName: String) {
        viewModelScope.launch {
            deckRepository.renameDeck(deckId, newName.trim())
            _deck.value = _deck.value?.copy(name = newName.trim())
            _modalState.value = DeckDetailModalState.Closed
        }
    }

    fun openEditNotes() {
        _modalState.value = DeckDetailModalState.EditNotes(_notesText.value)
    }

    fun confirmEditNotes(notes: String) {
        viewModelScope.launch {
            val trimmed = notes.takeIf { it.isNotBlank() }
            deckRepository.updateNotes(deckId, trimmed)
            _deck.value = _deck.value?.copy(notes = trimmed)
            _notesText.value = trimmed ?: ""
            _notesExpanded.value = trimmed != null
            _modalState.value = DeckDetailModalState.Closed
        }
    }

    fun openAssignDeckToGroup() {
        val deck = _deck.value ?: return
        _modalState.value = DeckDetailModalState.AssignDeckToGroup(deck.groupId, deck.type)
    }

    fun confirmAssignDeckToGroup(groupId: String?) {
        viewModelScope.launch {
            deckRepository.assignDeckToGroup(deckId, groupId)
            _deck.value = _deck.value?.copy(groupId = groupId)
            _modalState.value = DeckDetailModalState.Closed
        }
    }

    fun requestDelete() {
        val deck = _deck.value ?: return
        _modalState.value = DeckDetailModalState.DeleteConfirm(deck.name)
    }

    fun setAsActive() {
        viewModelScope.launch { preferences.setActiveDeckId(deckId) }
    }

    fun confirmDelete(onNavigateBack: () -> Unit) {
        viewModelScope.launch {
            if (activeDeckId.value == deckId) preferences.setActiveDeckId(null)
            deckRepository.deleteDeck(deckId)
            _modalState.value = DeckDetailModalState.Closed
            onNavigateBack()
        }
    }

    fun closeModal() { _modalState.value = DeckDetailModalState.Closed }

    // ── Factory ───────────────────────────────────────────────────────────────

    class Factory(
        private val deckId: String,
        private val deckRepository: DeckRepository,
        private val userCardDataRepository: UserCardDataRepository,
        private val preferences: AppPreferences
    ) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T =
            DeckDetailViewModel(deckId, deckRepository, userCardDataRepository, preferences) as T
    }
}
