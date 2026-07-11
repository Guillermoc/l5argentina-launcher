// ui/screens/decks/DecksViewModel.kt
package com.guillermoc.l5argentina.ui.screens.decks

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.guillermoc.l5argentina.data.datastore.AppPreferences
import com.guillermoc.l5argentina.data.db.entity.DeckGroupEntity
import com.guillermoc.l5argentina.data.model.CardType
import com.guillermoc.l5argentina.data.model.DeckGroup
import com.guillermoc.l5argentina.data.repository.DeckRepository
import com.guillermoc.l5argentina.ui.components.ActiveDeckInfo
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

// ── Modal state ──────────────────────────────────────────────────────────────

sealed class DecksModalState {
    data object Closed : DecksModalState()
    data object FilterOpen : DecksModalState()
    data object NewDeckOpen : DecksModalState()
    data class DeleteConfirm(val deckId: String, val deckName: String) : DecksModalState()
    data class ImportDeck(val deckId: String, val deckName: String) : DecksModalState()
    data class ImportResult(
        val imported: Int,
        val notFound: Int,
        val notFoundNames: List<String>
    ) : DecksModalState()
    data class RenameDeck(val deckId: String, val currentName: String) : DecksModalState()
    data class EditNotes(val deckId: String, val currentNotes: String) : DecksModalState()
    data class AssignGroup(
        val deckId: String,
        val currentGroupId: String?,
        val sectionType: String
    ) : DecksModalState()
    data class AddGroup(val sectionType: String) : DecksModalState()
    data class RenameGroup(
        val groupId: String,
        val currentName: String
    ) : DecksModalState()
    data class DeleteGroupConfirm(
        val groupId: String,
        val groupName: String
    ) : DecksModalState()
}

// ── Data models ──────────────────────────────────────────────────────────────

data class DeckSuggestions(
    val matchingDecks: List<ActiveDeckInfo>,
    val matchingCardNames: List<String>
)

data class DeckUserGroup(
    val groupId: String?,   // null = "Sin Grupo"
    val groupName: String,
    val decks: List<ActiveDeckInfo>
)

data class DecksSection(
    val sectionType: String,    // "deck" or "list"
    val groups: List<DeckUserGroup>
)

// ── New deck form ────────────────────────────────────────────────────────────

data class NewDeckForm(
    val name: String = "",
    val isList: Boolean = false,
    val listName: String = "",
    val selectedGroupId: String? = null
)

// ── ViewModel ────────────────────────────────────────────────────────────────

@OptIn(ExperimentalCoroutinesApi::class, FlowPreview::class)
class DecksViewModel(
    private val deckRepository: DeckRepository,
    private val preferences: AppPreferences
) : ViewModel() {

    private val allDecksFlow = deckRepository.getAllDecks()
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    private val allDeckCardsFlow = deckRepository.getAllDeckCardsWithCards()
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    private val allGroupsFlow = deckRepository.getAllGroups()
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    // ActiveDeckInfo for all decks (with computed clan/counts)
    val decks: StateFlow<List<ActiveDeckInfo>> = combine(allDecksFlow, allDeckCardsFlow) { allDecks, allCards ->
        val cardsByDeck = allCards.groupBy { it.deckCard.deckId }
        allDecks.map { deck ->
            val deckCards = cardsByDeck[deck.id] ?: emptyList()
            val totalCount = deckCards.sumOf { it.deckCard.quantity }
            if (deck.type == "list") {
                ActiveDeckInfo(
                    id              = deck.id,
                    name            = deck.name,
                    clanLabel       = "—",
                    dynastyCount    = 0,
                    fateCount       = 0,
                    deckType        = "list",
                    listName        = deck.listName,
                    listDescription = deck.listDescription,
                    totalCount      = totalCount,
                    groupId         = deck.groupId,
                    sortOrder       = deck.sortOrder,
                    notes           = deck.notes
                )
            } else {
                val strongholdCard = deckCards
                    .firstOrNull { it.card.type.equals(CardType.STRONGHOLD.key, ignoreCase = true) }
                    ?.card
                val clanLabel = strongholdCard?.clan?.replaceFirstChar { it.uppercase() } ?: "—"
                val dynastyCount = deckCards
                    .filter { it.card.deckGroup.equals(DeckGroup.DYNASTY.key, ignoreCase = true) }
                    .sumOf { it.deckCard.quantity }
                val fateCount = deckCards
                    .filter { it.card.deckGroup.equals(DeckGroup.FATE.key, ignoreCase = true) }
                    .sumOf { it.deckCard.quantity }
                ActiveDeckInfo(
                    id                  = deck.id,
                    name                = deck.name,
                    clanLabel           = clanLabel,
                    clanKey             = strongholdCard?.clan,
                    dynastyCount        = dynastyCount,
                    fateCount           = fateCount,
                    strongholdName      = strongholdCard?.name,
                    strongholdImagePath = strongholdCard?.imagePath,
                    deckType            = "deck",
                    totalCount          = totalCount,
                    groupId             = deck.groupId,
                    sortOrder           = deck.sortOrder,
                    notes               = deck.notes
                )
            }
        }
    }.stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    val activeDeckId: StateFlow<String?> = preferences.activeDeckId
        .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    // ── Clan filter (kept for FilterModal) ──────────────────────────────────

    private val _clanFilter = MutableStateFlow<Set<String>>(emptySet())
    val clanFilter: StateFlow<Set<String>> = _clanFilter

    // ── Search query ─────────────────────────────────────────────────────────

    private val _deckQuery = MutableStateFlow("")
    val deckQuery: StateFlow<String> = _deckQuery

    val deckSuggestions: StateFlow<DeckSuggestions> = _deckQuery
        .debounce(200)
        .flatMapLatest { query ->
            if (query.length < 2) flowOf(DeckSuggestions(emptyList(), emptyList()))
            else combine(decks, allDeckCardsFlow) { allDecks, allCards ->
                val matchDecks = allDecks
                    .filter { it.name.contains(query, ignoreCase = true) }
                    .take(5)
                val matchCardNames = allCards
                    .filter { it.card.name.contains(query, ignoreCase = true) }
                    .map { it.card.name }
                    .distinct()
                    .take(5)
                DeckSuggestions(matchDecks, matchCardNames)
            }
        }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), DeckSuggestions(emptyList(), emptyList()))

    // ── Sections: Mazos + Listas, each split by user groups ──────────────────

    val sections: StateFlow<List<DecksSection>> = combine(
        decks, allGroupsFlow, _clanFilter, _deckQuery, allDeckCardsFlow
    ) { allDecks, groups: List<DeckGroupEntity>, clanFilter, query, allCards ->
        val filtered = if (clanFilter.isEmpty()) allDecks
        else allDecks.filter { d -> d.deckType == "list" || (d.clanKey != null && d.clanKey in clanFilter) }

        val searched = if (query.length < 2) filtered
        else {
            val cardMatchingDeckIds = allCards
                .filter { it.card.name.contains(query, ignoreCase = true) }
                .map { it.deckCard.deckId }.toSet()
            filtered.filter { d ->
                d.name.contains(query, ignoreCase = true) || d.id in cardMatchingDeckIds
            }
        }

        listOf("deck", "list").map { sectionType ->
            val sectionDecks = searched
                .filter { it.deckType == sectionType }
                .sortedWith(compareBy({ it.groupId ?: "" }, { it.sortOrder }))

            val sectionGroups = groups
                .filter { it.sectionType == sectionType }
                .sortedBy { it.name }

            // "Sin Grupo" first, then named groups alphabetically
            val userGroups = mutableListOf<DeckUserGroup>()

            val ungrouped = sectionDecks.filter { it.groupId == null }
            userGroups.add(DeckUserGroup(groupId = null, groupName = "Sin Grupo", decks = ungrouped))

            sectionGroups.forEach { group ->
                val groupDecks = sectionDecks.filter { it.groupId == group.id }
                userGroups.add(DeckUserGroup(groupId = group.id, groupName = group.name, decks = groupDecks))
            }

            DecksSection(sectionType = sectionType, groups = userGroups)
        }
    }.stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    // ── Modal & form state ───────────────────────────────────────────────────

    private val _modalState = MutableStateFlow<DecksModalState>(DecksModalState.Closed)
    val modalState: StateFlow<DecksModalState> = _modalState

    private val _newDeckForm = MutableStateFlow(NewDeckForm())
    val newDeckForm: StateFlow<NewDeckForm> = _newDeckForm

    private val _exportText = MutableStateFlow<String?>(null)
    val exportText: StateFlow<String?> = _exportText

    // ── Actions ──────────────────────────────────────────────────────────────

    fun onDeckQueryChange(query: String) { _deckQuery.value = query }

    fun activateDeck(deckId: String) {
        viewModelScope.launch { preferences.setActiveDeckId(deckId) }
    }

    // New deck form
    fun openNewDeck() {
        _newDeckForm.value = NewDeckForm()
        _modalState.value = DecksModalState.NewDeckOpen
    }

    fun onNewDeckFormChange(form: NewDeckForm) { _newDeckForm.value = form }

    fun createDeck() {
        val form = _newDeckForm.value
        val name = form.name.trim()
        if (name.isEmpty()) return
        viewModelScope.launch {
            val type = if (form.isList) "list" else "deck"
            val newId = deckRepository.createDeck(
                name             = name,
                type             = type,
                listName         = if (form.isList) form.listName.trim().take(10).ifBlank { null } else null,
                listDescription  = null,
                groupId          = form.selectedGroupId
            )
            preferences.setActiveDeckId(newId)
            _modalState.value = DecksModalState.Closed
        }
    }

    // Deck options
    fun exportDeck(deckId: String) {
        viewModelScope.launch { _exportText.value = deckRepository.exportDeck(deckId) }
    }

    fun clearExportText() { _exportText.value = null }

    fun requestDelete(deckId: String, deckName: String) {
        _modalState.value = DecksModalState.DeleteConfirm(deckId, deckName)
    }

    fun confirmDelete(deckId: String) {
        viewModelScope.launch {
            deckRepository.deleteDeck(deckId)
            if (activeDeckId.value == deckId) preferences.setActiveDeckId(null)
        }
        _modalState.value = DecksModalState.Closed
    }

    fun openImport(deckId: String, deckName: String) {
        _modalState.value = DecksModalState.ImportDeck(deckId, deckName)
    }

    fun runImport(deckId: String, text: String) {
        viewModelScope.launch {
            val result = deckRepository.importCards(deckId, text)
            _modalState.value = DecksModalState.ImportResult(result.imported, result.notFound, result.notFoundNames)
        }
    }

    fun openRenameDeck(deckId: String, currentName: String) {
        _modalState.value = DecksModalState.RenameDeck(deckId, currentName)
    }

    fun confirmRenameDeck(deckId: String, newName: String) {
        val trimmed = newName.trim()
        if (trimmed.isBlank()) return
        viewModelScope.launch { deckRepository.renameDeck(deckId, trimmed) }
        _modalState.value = DecksModalState.Closed
    }

    fun openEditNotes(deckId: String, currentNotes: String) {
        _modalState.value = DecksModalState.EditNotes(deckId, currentNotes)
    }

    fun confirmEditNotes(deckId: String, notes: String) {
        viewModelScope.launch { deckRepository.updateNotes(deckId, notes.takeIf { it.isNotBlank() }) }
        _modalState.value = DecksModalState.Closed
    }

    fun openAssignGroup(deckId: String, currentGroupId: String?, sectionType: String) {
        _modalState.value = DecksModalState.AssignGroup(deckId, currentGroupId, sectionType)
    }

    fun confirmAssignGroup(deckId: String, groupId: String?) {
        viewModelScope.launch { deckRepository.assignDeckToGroup(deckId, groupId) }
        _modalState.value = DecksModalState.Closed
    }

    // Group management
    fun openAddGroup(sectionType: String) {
        _modalState.value = DecksModalState.AddGroup(sectionType)
    }

    fun confirmAddGroup(name: String, sectionType: String) {
        val trimmed = name.trim()
        if (trimmed.isBlank()) return
        viewModelScope.launch { deckRepository.createGroup(trimmed, sectionType) }
        _modalState.value = DecksModalState.Closed
    }

    fun openRenameGroup(groupId: String, currentName: String) {
        _modalState.value = DecksModalState.RenameGroup(groupId, currentName)
    }

    fun confirmRenameGroup(groupId: String, newName: String) {
        val trimmed = newName.trim()
        if (trimmed.isBlank()) return
        viewModelScope.launch { deckRepository.renameGroup(groupId, trimmed) }
        _modalState.value = DecksModalState.Closed
    }

    fun requestDeleteGroup(groupId: String, groupName: String) {
        _modalState.value = DecksModalState.DeleteGroupConfirm(groupId, groupName)
    }

    fun confirmDeleteGroup(groupId: String) {
        viewModelScope.launch { deckRepository.deleteGroup(groupId) }
        _modalState.value = DecksModalState.Closed
    }

    // Deck reordering (DnD result)
    fun reorderDecksInGroup(orderedIds: List<String>) {
        val updates = orderedIds.mapIndexed { index, id -> id to index }
        viewModelScope.launch { deckRepository.reorderDecks(updates) }
    }

    // Filter
    fun openClanFilter() { _modalState.value = DecksModalState.FilterOpen }
    fun toggleClanFilter(key: String) {
        _clanFilter.value = _clanFilter.value.let { if (key in it) it - key else it + key }
    }
    fun clearClanFilter() { _clanFilter.value = emptySet() }

    fun closeModal() { _modalState.value = DecksModalState.Closed }

    class Factory(
        private val deckRepository: DeckRepository,
        private val preferences: AppPreferences
    ) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T =
            DecksViewModel(deckRepository, preferences) as T
    }
}
