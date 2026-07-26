// ui/screens/search/SearchViewModel.kt
package com.guillermoc.l5argentina.ui.screens.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.guillermoc.l5argentina.data.datastore.AppPreferences
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.db.entity.FormatEntity
import com.guillermoc.l5argentina.data.db.entity.KeywordEntity
import com.guillermoc.l5argentina.data.db.entity.SetEntity
import com.guillermoc.l5argentina.data.model.CardSearchCriteria
import com.guillermoc.l5argentina.data.model.Clan
import com.guillermoc.l5argentina.data.model.CardType
import com.guillermoc.l5argentina.data.repository.CardRepository
import com.guillermoc.l5argentina.data.repository.DeckRepository
import com.guillermoc.l5argentina.data.repository.UserCardDataRepository
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.flow.onStart
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

enum class BarSuggestionMode { NAME, KEYWORD, CLAN, TYPE, TEXT }

sealed class FilterModalState {
    data object Closed : FilterModalState()
    data class Open(val draft: CardSearchCriteria) : FilterModalState()
}

sealed class CardViewerState {
    data object Closed : CardViewerState()
    data class Open(val index: Int) : CardViewerState()
}

data class SearchTabState(
    val criteria: CardSearchCriteria = CardSearchCriteria(),
    val barText: String = "",
    val filterModal: FilterModalState = FilterModalState.Closed,
    val cardViewer: CardViewerState = CardViewerState.Closed,
    val scrollIndex: Int = 0,
    val scrollOffset: Int = 0,
    val soloListActive: Boolean = false,
    val selectedListId: String? = null,
    val selectedListName: String? = null,
    val carouselIndex: Int = 0,
    val gridZoomLevel: Int = 4,    // 0=1col  1=2cols  2=3cols  3=4cols  4=5cols
    val gridScrollIndex: Int = 0,
    val gridScrollOffset: Int = 0,
    // Visualización: expande cada carta en una entrada por impresión (solo UI, no afecta la query)
    val expandPrintings: Boolean = false,
    // Incrementa al aplicar filtros: fuerza el re-anclaje del grid aunque carouselIndex no cambie
    val searchEpoch: Int = 0
)

@OptIn(ExperimentalCoroutinesApi::class, FlowPreview::class)
class SearchViewModel(
    private val cardRepository: CardRepository,
    private val deckRepository: DeckRepository,
    private val userCardDataRepository: UserCardDataRepository,
    private val preferences: AppPreferences
) : ViewModel() {

    private val _activeTabIndex = MutableStateFlow(0)
    val activeTabIndex: StateFlow<Int> = _activeTabIndex

    private val _tabs = MutableStateFlow(List(3) { SearchTabState() })

    val activeTab: StateFlow<SearchTabState> = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx]
    }.stateIn(viewModelScope, SharingStarted.Eagerly, SearchTabState())

    // Último resultado emitido por tab: al cambiar de tab se emite de inmediato el
    // resultado conocido del tab destino, en lugar de mostrar el del tab anterior
    // mientras Room resuelve la query nueva (flash de cartas ajenas en grilla/carousel).
    private val tabResultsCache = MutableList<List<CardEntity>>(3) { emptyList() }

    private val rawResults: StateFlow<List<CardEntity>> = combine(_activeTabIndex, _tabs) { idx, tabs ->
        idx to tabs[idx].criteria
    }
        .distinctUntilChanged()
        .flatMapLatest { (idx, criteria) ->
            cardRepository.search(criteria)
                .onEach { tabResultsCache[idx] = it }
                .onStart { emit(tabResultsCache[idx]) }
        }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val resultIds: StateFlow<Set<String>> = rawResults
        .map { cards -> cards.map { it.id }.toSet() }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptySet())

    val availableLists: StateFlow<List<Pair<String, String>>> = deckRepository.getAllDecks()
        .map { decks ->
            decks
                .filter { it.type == "list" }
                .map { deck -> deck.id to (deck.listName?.takeIf { it.isNotBlank() } ?: deck.name) }
        }
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    val listCardIds: StateFlow<Set<String>> = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx].selectedListId
    }
        .distinctUntilChanged()
        .flatMapLatest { listId ->
            if (listId == null) flowOf(emptySet())
            else deckRepository.getDeckCardsWithCards(listId)
                .map { cards -> cards.map { it.deckCard.cardId }.toSet() }
        }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptySet())

    private val listFilterState = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx].soloListActive to tabs[idx].selectedListId
    }.distinctUntilChanged()

    val results: StateFlow<List<CardEntity>> = combine(rawResults, listFilterState, listCardIds) { cards, (soloActive, listId), listIds ->
        if (soloActive && listId != null) cards.filter { it.id in listIds } else cards
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val sets: StateFlow<List<SetEntity>> = cardRepository.getAllSets()
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    // ── Vista expandida por impresión (solo visualización) ────────────────────
    // Cada carta se expande en 1 entrada por impresión (card.copy(imagePath=...)).
    // El id se conserva → qty/favorito/oculta/lista se propagan a todas las entradas
    // del mismo id, y agregar/ocultar afecta a todas por igual.
    private val expandFlow = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx].expandPrintings
    }.distinctUntilChanged()

    private val sortSpec = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx].criteria.sortField to tabs[idx].criteria.sortAscending
    }.distinctUntilChanged()

    // Sets filtrados: en modo expandido solo se muestran las impresiones de estos sets.
    private val setFilterFlow = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx].criteria.sets
    }.distinctUntilChanged()

    val displayCards: StateFlow<List<CardEntity>> =
        combine(results, expandFlow, sets, sortSpec, setFilterFlow) { cards, expand, setList, spec, selectedSets ->
            val (field, asc) = spec
            // Orden canónico de sets = el del modal de filtros (presortSets, no rowid crudo).
            val order = setOrderMap(setList)
            // Rank por set de la impresión mostrada; si la carpeta no coincide con un set
            // (p.ej. sets de Loto), cae al set más temprano de card.sets (coincide con el modal).
            // MAX_VALUE = set desconocido (no está en la tabla `sets`).
            val setRank = { c: CardEntity ->
                order[editionOf(c.imagePath)] ?: cardSetRank(c, order)
            }
            // Orden por Set: los sets desconocidos van SIEMPRE al final (después de Promo),
            // en ambas direcciones. El resto por rank (dir) + printingId como desempate.
            val bySet = { base: List<CardEntity> ->
                base.map { it to setRank(it) }
                    .sortedWith(
                        compareByDescending<Pair<CardEntity, Int>> { it.second != Int.MAX_VALUE }
                            .then(if (asc) compareBy { it.second } else compareByDescending { it.second })
                            .thenBy { printingIdOf(it.first.imagePath) }
                    )
                    .map { it.first }
            }
            val isSet = field == com.guillermoc.l5argentina.data.model.SortField.SET
            if (!expand) {
                // El orden por Set se resuelve acá (el SQL no puede replicar presortSets).
                if (isSet) bySet(cards) else cards
            } else {
                // Con filtro de set activo, solo las impresiones de esos sets. Si ninguna carpeta
                // coincide (mismatch carpeta/código), se muestra la impresión preferida para no
                // perder una carta que sí matcheó el filtro a nivel SQL.
                val selected = selectedSets.toSet()
                val printsOf = { card: CardEntity ->
                    val prints = expandCard(card)
                    if (selected.isEmpty()) prints
                    else prints.filter { editionOf(it.imagePath) in selected }.ifEmpty { listOf(card) }
                }
                if (isSet) {
                    bySet(cards.flatMap { printsOf(it) })
                } else {
                    // Preserva el orden SQL de las cartas; agrupa las impresiones (set → printingId)
                    val within = compareBy<CardEntity>({ setRank(it) }, { printingIdOf(it.imagePath) })
                    cards.flatMap { card -> printsOf(card).sortedWith(within) }
                }
            }
        }
            .flowOn(Dispatchers.Default)
            .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val formats: StateFlow<List<FormatEntity>> = cardRepository.getAllFormats()
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    val availableKeywords: StateFlow<List<KeywordEntity>> = cardRepository.getAllKeywords()
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    val keywordCodes: StateFlow<List<String>> = cardRepository.getAllKeywordCodes()
        .stateIn(viewModelScope, SharingStarted.Lazily, emptyList())

    val hasInitialHoldingCards: StateFlow<Boolean> = cardRepository.hasInitialHoldingCards()
        .stateIn(viewModelScope, SharingStarted.Lazily, false)

    val savedPreset: StateFlow<CardSearchCriteria?> = preferences.savedFilterPreset
        .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    val activeDeckId: StateFlow<String?> = preferences.activeDeckId
        .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    val favoriteIds: StateFlow<Set<String>> = userCardDataRepository.observeFavoriteIds()
        .stateIn(viewModelScope, SharingStarted.Eagerly, emptySet())

    val hiddenIds: StateFlow<Set<String>> = userCardDataRepository.observeHiddenIds()
        .stateIn(viewModelScope, SharingStarted.Eagerly, emptySet())

    private val _pendingHideIds = MutableStateFlow<Set<String>>(emptySet())
    val pendingHideIds: StateFlow<Set<String>> = _pendingHideIds
    private val pendingHideJobs = mutableMapOf<String, Job>()

    val deckCardQuantities: StateFlow<Map<String, Int>> = preferences.activeDeckId
        .flatMapLatest { deckId ->
            if (deckId == null) flowOf(emptyMap<String, Int>())
            else deckRepository.getDeckCardsWithCards(deckId)
                .map { list -> list.associate { it.deckCard.cardId to it.deckCard.quantity } }
        }
        .stateIn(viewModelScope, SharingStarted.Eagerly, emptyMap())

    private val activeBarText: StateFlow<String> = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx].barText
    }.stateIn(viewModelScope, SharingStarted.Eagerly, "")

    val currentSuggestionMode: StateFlow<BarSuggestionMode> = activeBarText
        .map { parseBarText(it).first }
        .stateIn(viewModelScope, SharingStarted.Eagerly, BarSuggestionMode.NAME)

    val nameSuggestions: StateFlow<List<CardEntity>> = activeBarText
        .debounce(200)
        .distinctUntilChanged()
        .flatMapLatest { barText ->
            val (mode, query) = parseBarText(barText)
            if (mode != BarSuggestionMode.NAME || query.length < 2) flowOf(emptyList())
            else cardRepository.searchSuggestions(query)
        }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val operatorSuggestions: StateFlow<List<String>> = combine(
        activeBarText.debounce(150).distinctUntilChanged(),
        keywordCodes
    ) { barText: String, codes: List<String> ->
        val (mode, query) = parseBarText(barText)
        val q = query.lowercase()
        when (mode) {
            BarSuggestionMode.KEYWORD ->
                codes.filter { it.lowercase().contains(q) }.take(8)
            BarSuggestionMode.CLAN ->
                Clan.entries.map { it.key }.filter { it.lowercase().contains(q) }.take(8)
            BarSuggestionMode.TYPE ->
                CardType.entries
                    .filter { it != CardType.WIND }
                    .map { it.key }
                    .filter { it.lowercase().contains(q) }
                    .take(8)
            else -> emptyList()
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val activeSearchSummary: StateFlow<String> = combine(_activeTabIndex, _tabs) { idx, tabs ->
        tabs[idx].criteria
    }.map { summaryOf(it) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), "")

    // -------------------------------------------------------------------------

    private fun updateActiveTab(transform: (SearchTabState) -> SearchTabState) {
        val idx = _activeTabIndex.value
        _tabs.value = _tabs.value.toMutableList().also { it[idx] = transform(it[idx]) }
    }

    fun updateCriteria(fn: (CardSearchCriteria) -> CardSearchCriteria) {
        updateActiveTab { it.copy(criteria = fn(it.criteria)) }
    }

    fun onSearchBarChange(text: String) {
        val (mode, query) = parseBarText(text)
        updateActiveTab { t ->
            when (mode) {
                BarSuggestionMode.NAME -> t.copy(barText = text, criteria = t.criteria.copy(name = text))
                BarSuggestionMode.TEXT -> t.copy(barText = text, criteria = t.criteria.copy(text = query))
                else -> t.copy(barText = text, criteria = t.criteria.copy(name = ""))
            }
        }
    }

    fun selectSuggestion(name: String) {
        updateActiveTab { t -> t.copy(barText = name, criteria = t.criteria.copy(name = name)) }
    }

    fun commitOperatorSuggestion(value: String) {
        val tab = _tabs.value[_activeTabIndex.value]
        val (mode, _) = parseBarText(tab.barText)
        updateActiveTab { t ->
            when (mode) {
                BarSuggestionMode.KEYWORD -> t.copy(
                    barText = "",
                    criteria = t.criteria.copy(keywords = (t.criteria.keywords + value).distinct())
                )
                BarSuggestionMode.CLAN -> t.copy(
                    barText = "",
                    criteria = t.criteria.copy(clans = (t.criteria.clans + value).distinct())
                )
                BarSuggestionMode.TYPE -> t.copy(
                    barText = "",
                    criteria = t.criteria.copy(types = (t.criteria.types + value).distinct())
                )
                BarSuggestionMode.TEXT -> t.copy(
                    barText = "",
                    criteria = t.criteria.copy(text = value)
                )
                BarSuggestionMode.NAME -> t.copy(
                    barText = value,
                    criteria = t.criteria.copy(name = value)
                )
            }
        }
    }

    fun switchTab(index: Int, currentScrollIndex: Int, currentScrollOffset: Int) {
        updateActiveTab { it.copy(scrollIndex = currentScrollIndex, scrollOffset = currentScrollOffset) }
        _activeTabIndex.value = index
    }

    fun saveScrollPosition(index: Int, offset: Int) {
        updateActiveTab { it.copy(scrollIndex = index, scrollOffset = offset) }
    }

    fun setCarouselIndex(index: Int) {
        updateActiveTab { it.copy(carouselIndex = index) }
    }

    fun setGridZoomLevel(level: Int) {
        updateActiveTab { it.copy(gridZoomLevel = level.coerceIn(0, 4)) }
    }

    fun saveGridScroll(index: Int, offset: Int) {
        updateActiveTab { it.copy(gridScrollIndex = index, gridScrollOffset = offset) }
    }

    fun toggleExpandPrintings() {
        // Cambia el tamaño de la lista → re-ancla carousel/grid y resetea posiciones
        updateActiveTab {
            it.copy(
                expandPrintings = !it.expandPrintings,
                carouselIndex = 0,
                scrollIndex = 0, scrollOffset = 0,
                gridScrollIndex = 0, gridScrollOffset = 0,
                searchEpoch = it.searchEpoch + 1
            )
        }
    }

    fun setCardQuantity(cardId: String, n: Int) {
        val deckId = activeDeckId.value ?: return
        viewModelScope.launch { deckRepository.setCardQuantity(deckId, cardId, n) }
    }

    fun toggleOnlyFavorites() {
        updateCriteria { it.copy(onlyFavorites = !it.onlyFavorites) }
    }

    fun toggleIncludeHidden() {
        updateCriteria { it.copy(includeHidden = !it.includeHidden) }
    }

    fun toggleOnlyInitialHoldings() {
        updateCriteria { it.copy(onlyInitialHoldings = !it.onlyInitialHoldings) }
    }

    fun openFilters() {
        val current = _tabs.value[_activeTabIndex.value].criteria
        updateActiveTab { it.copy(filterModal = FilterModalState.Open(current)) }
    }

    fun updateDraft(criteria: CardSearchCriteria) {
        val current = _tabs.value[_activeTabIndex.value].filterModal
        if (current is FilterModalState.Open) {
            updateActiveTab { it.copy(filterModal = FilterModalState.Open(criteria)) }
        }
    }

    fun applyFilters() {
        val idx = _activeTabIndex.value
        val current = _tabs.value[idx].filterModal
        if (current is FilterModalState.Open) {
            val allFormats = formats.value.map { it.code }.toSet()
            val draft = current.draft
            val normalized = if (draft.formats.toSet() == allFormats) draft.copy(formats = emptyList()) else draft
            viewModelScope.launch {
                // Precarga la query con el modal aún abierto y prima el caché del tab:
                // al cerrar, el onStart del pipeline emite directo la lista nueva
                // en lugar de mostrar la vieja mientras Room resuelve.
                tabResultsCache[idx] = cardRepository.search(normalized).first()
                updateActiveTab { it.copy(criteria = normalized, barText = normalized.name, filterModal = FilterModalState.Closed, carouselIndex = 0, searchEpoch = it.searchEpoch + 1) }
            }
        } else {
            updateActiveTab { it.copy(filterModal = FilterModalState.Closed) }
        }
    }

    fun clearFilters() {
        updateActiveTab { t ->
            val blank = CardSearchCriteria()
            t.copy(criteria = blank, filterModal = FilterModalState.Open(blank))
        }
    }

    fun clearFiltersQuick() {
        updateActiveTab { t ->
            val (mode, _) = parseBarText(t.barText)
            val preservedName = if (mode == BarSuggestionMode.NAME) t.barText else ""
            t.copy(
                barText = preservedName,
                criteria = CardSearchCriteria(name = preservedName)
            )
        }
    }

    fun savePreset(criteria: CardSearchCriteria) {
        viewModelScope.launch { preferences.saveFilterPreset(criteria) }
    }

    fun loadPreset() {
        val preset = savedPreset.value ?: return
        val current = _tabs.value[_activeTabIndex.value].filterModal
        if (current is FilterModalState.Open) {
            updateActiveTab { it.copy(filterModal = FilterModalState.Open(preset)) }
        }
    }

    fun resetPreset() {
        viewModelScope.launch { preferences.clearFilterPreset() }
    }

    fun closeFilters() {
        updateActiveTab { it.copy(filterModal = FilterModalState.Closed) }
    }

    fun openCardViewer(index: Int) {
        updateActiveTab { it.copy(cardViewer = CardViewerState.Open(index), carouselIndex = index) }
    }

    fun closeCardViewer() {
        updateActiveTab { it.copy(cardViewer = CardViewerState.Closed) }
    }

    fun nextCard() {
        val state = _tabs.value[_activeTabIndex.value].cardViewer as? CardViewerState.Open ?: return
        val next = state.index + 1
        if (next < displayCards.value.size) updateActiveTab {
            it.copy(cardViewer = CardViewerState.Open(next), carouselIndex = next)
        }
    }

    fun prevCard() {
        val state = _tabs.value[_activeTabIndex.value].cardViewer as? CardViewerState.Open ?: return
        val prev = state.index - 1
        if (prev >= 0) updateActiveTab {
            it.copy(cardViewer = CardViewerState.Open(prev), carouselIndex = prev)
        }
    }

    fun addCard(cardId: String) {
        val deckId = activeDeckId.value ?: return
        viewModelScope.launch { deckRepository.addCard(deckId, cardId) }
    }

    fun removeCard(cardId: String) {
        val deckId = activeDeckId.value ?: return
        viewModelScope.launch { deckRepository.removeCard(deckId, cardId) }
    }

    fun toggleFavorite(cardId: String) {
        viewModelScope.launch {
            userCardDataRepository.setFavorite(cardId, !favoriteIds.value.contains(cardId))
        }
    }

    fun toggleHidden(cardId: String) {
        val wasHidden = hiddenIds.value.contains(cardId)
        if (wasHidden) {
            pendingHideJobs.remove(cardId)?.cancel()
            _pendingHideIds.value = _pendingHideIds.value - cardId
            viewModelScope.launch { userCardDataRepository.setHidden(cardId, false) }
            return
        }
        if (cardId in pendingHideJobs) {
            pendingHideJobs.remove(cardId)?.cancel()
            _pendingHideIds.value = _pendingHideIds.value - cardId
            return
        }
        _pendingHideIds.value = _pendingHideIds.value + cardId
        pendingHideJobs[cardId] = viewModelScope.launch {
            delay(2_000)
            userCardDataRepository.setHidden(cardId, true)
            _pendingHideIds.value = _pendingHideIds.value - cardId
            pendingHideJobs.remove(cardId)
            closeCardViewer()
        }
    }

    fun toggleSoloList() {
        updateActiveTab { it.copy(soloListActive = !it.soloListActive) }
    }

    fun selectList(id: String, name: String) {
        updateActiveTab {
            it.copy(selectedListId = id, selectedListName = name, soloListActive = true)
        }
    }

    fun clearListSelection() {
        updateActiveTab { it.copy(selectedListId = null, selectedListName = null, soloListActive = false) }
    }

    fun toggleInList(cardId: String) {
        val listId = _tabs.value[_activeTabIndex.value].selectedListId ?: return
        viewModelScope.launch {
            if (cardId in listCardIds.value) deckRepository.removeCard(listId, cardId)
            else deckRepository.addCard(listId, cardId)
        }
    }

    // -------------------------------------------------------------------------

    // ── Helpers de expansión por impresión ────────────────────────────────────
    // image_path = "images/cards/{edition}/{printingId}.jpg"
    private fun editionOf(imagePath: String?): String =
        imagePath?.substringAfter("images/cards/", "")?.substringBefore("/")?.takeIf { it.isNotBlank() } ?: ""

    private fun printingIdOf(imagePath: String?): String =
        imagePath?.substringAfterLast("/", "")?.removeSuffix(".jpg") ?: ""

    // Fallback de orden por set: rowid más temprano (orden del modal) entre los sets de la carta.
    private fun cardSetRank(card: CardEntity, order: Map<String, Int>): Int =
        card.sets.split("|").filter { it.isNotBlank() }
            .mapNotNull { order[it] }
            .minOrNull() ?: Int.MAX_VALUE

    // Una entrada por impresión (imagePath preferida + printing_images, sin duplicados).
    // Sin imágenes → una sola entrada con la carta tal cual (placeholder).
    private fun expandCard(card: CardEntity): List<CardEntity> {
        val imgs = buildList {
            card.imagePath?.let { add(it) }
            card.printingImages.split("|").forEach { p ->
                if (p.isNotBlank() && p !in this) add(p)
            }
        }
        return if (imgs.isEmpty()) listOf(card) else imgs.map { card.copy(imagePath = it) }
    }

    private fun parseBarText(text: String): Pair<BarSuggestionMode, String> = when {
        text.startsWith("k.") -> BarSuggestionMode.KEYWORD to text.removePrefix("k.")
        text.startsWith("c.") -> BarSuggestionMode.CLAN to text.removePrefix("c.")
        text.startsWith("tx.") -> BarSuggestionMode.TEXT to text.removePrefix("tx.")
        text.startsWith("t.") -> BarSuggestionMode.TYPE to text.removePrefix("t.")
        else -> BarSuggestionMode.NAME to text
    }

    private fun summaryOf(c: CardSearchCriteria): String {
        val parts = mutableListOf<String>()
        if (c.types.isNotEmpty()) parts += c.types.joinToString("/")
        if (c.clans.isNotEmpty()) parts += c.clans.joinToString("/")
        if (c.designators.isNotEmpty()) parts += c.designators.joinToString("/")
        if (c.keywords.isNotEmpty()) parts += c.keywords.joinToString(" ")
        if (c.text.isNotBlank()) parts += "\"${c.text.take(15)}\""
        if (c.sets.isNotEmpty()) parts += c.sets.joinToString("/")
        if (c.formats.isNotEmpty()) parts += c.formats.joinToString("/")
        val stats = mutableListOf<String>()
        c.forceMin?.let { stats += "F≥$it" }; c.forceMax?.let { stats += "F≤$it" }
        if (c.includeSpecialForce) stats += "F*"
        c.chiMin?.let { stats += "Chi≥$it" }; c.chiMax?.let { stats += "Chi≤$it" }
        c.costMin?.let { stats += "C≥$it" }; c.costMax?.let { stats += "C≤$it" }
        if (c.includeSpecialCost) stats += "C*"
        c.honorReqMin?.let { stats += "H≥$it" }; c.honorReqMax?.let { stats += "H≤$it" }
        c.personalHonorMin?.let { stats += "PH≥$it" }; c.personalHonorMax?.let { stats += "PH≤$it" }
        c.focusMin?.let { stats += "Foc≥$it" }; c.focusMax?.let { stats += "Foc≤$it" }
        if (stats.isNotEmpty()) parts += stats.joinToString(" ")
        if (c.includeBanned) parts += "incl.baneadas"
        if (c.onlyInitialHoldings) parts += "solo holdings"
        return parts.joinToString(" · ")
    }

    // -------------------------------------------------------------------------

    class Factory(
        private val cardRepository: CardRepository,
        private val deckRepository: DeckRepository,
        private val userCardDataRepository: UserCardDataRepository,
        private val preferences: AppPreferences
    ) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T =
            SearchViewModel(cardRepository, deckRepository, userCardDataRepository, preferences) as T
    }
}
