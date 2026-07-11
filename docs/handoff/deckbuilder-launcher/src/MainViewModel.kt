package com.guillermoc.l5argentina.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.guillermoc.l5argentina.BuildConfig
import com.guillermoc.l5argentina.data.datastore.AppPreferences
import com.guillermoc.l5argentina.data.model.CardType
import com.guillermoc.l5argentina.data.model.DeckGroup
import com.guillermoc.l5argentina.data.repository.CardRepository
import com.guillermoc.l5argentina.data.repository.DeckRepository
import com.guillermoc.l5argentina.ui.components.ActiveDeckInfo
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

data class DrawerBadges(
    val cartas: Boolean = false,
    val novedades: Boolean = false,
    val historia: Boolean = false,
    val proximoTorneo: Boolean = false
)

@OptIn(ExperimentalCoroutinesApi::class)
class MainViewModel(
    private val preferences: AppPreferences,
    private val deckRepository: DeckRepository,
    private val cardRepository: CardRepository
) : ViewModel() {

    val activeDeckInfo: StateFlow<ActiveDeckInfo?> = preferences.activeDeckId
        .flatMapLatest { deckId ->
            if (deckId == null) flowOf(null)
            else combine(
                deckRepository.getDeckCardsWithCards(deckId),
                deckRepository.getAllDecks()
            ) { deckCards, allDecks ->
                val deck = allDecks.firstOrNull { it.id == deckId } ?: return@combine null
                val totalCount = deckCards.sumOf { it.deckCard.quantity }

                if (deck.type == "list") {
                    ActiveDeckInfo(
                        id               = deckId,
                        name             = deck.name,
                        clanLabel        = "—",
                        dynastyCount     = 0,
                        fateCount        = 0,
                        deckType         = "list",
                        listName         = deck.listName,
                        listDescription  = deck.listDescription,
                        totalCount       = totalCount,
                        groupId          = deck.groupId,
                        sortOrder        = deck.sortOrder,
                        notes            = deck.notes
                    )
                } else {
                    val strongholdCard = deckCards
                        .firstOrNull { it.card.type.equals(CardType.STRONGHOLD.key, ignoreCase = true) }
                        ?.card
                    val clanLabel = strongholdCard?.clan?.replaceFirstChar { it.uppercase() } ?: "No clan"
                    val dynastyCount = deckCards
                        .filter { it.card.deckGroup.equals(DeckGroup.DYNASTY.key, ignoreCase = true) }
                        .sumOf { it.deckCard.quantity }
                    val fateCount = deckCards
                        .filter { it.card.deckGroup.equals(DeckGroup.FATE.key, ignoreCase = true) }
                        .sumOf { it.deckCard.quantity }
                    ActiveDeckInfo(
                        id             = deckId,
                        name           = deck.name,
                        clanLabel      = clanLabel,
                        clanKey        = strongholdCard?.clan,
                        dynastyCount   = dynastyCount,
                        fateCount      = fateCount,
                        strongholdName = strongholdCard?.name,
                        deckType       = "deck",
                        totalCount     = totalCount,
                        groupId        = deck.groupId,
                        sortOrder      = deck.sortOrder,
                        notes          = deck.notes
                    )
                }
            }
        }
        .catch { emit(null) }
        .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    val recentDecks: StateFlow<List<ActiveDeckInfo>> =
        combine(
            preferences.recentDeckIds,
            deckRepository.getAllDecks(),
            deckRepository.getAllDeckCardsWithCards()
        ) { ids, decks, allDeckCards ->
            val deckMap = decks.associateBy { it.id }
            val cardsByDeck = allDeckCards.groupBy { it.deckCard.deckId }
            ids.mapNotNull { id ->
                val deck = deckMap[id] ?: return@mapNotNull null
                val deckCards = cardsByDeck[id].orEmpty()
                val totalCount = deckCards.sumOf { it.deckCard.quantity }
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
                    id             = id,
                    name           = deck.name,
                    clanLabel      = clanLabel,
                    clanKey        = strongholdCard?.clan,
                    dynastyCount   = dynastyCount,
                    fateCount      = fateCount,
                    strongholdName = strongholdCard?.name,
                    deckType       = deck.type,
                    listName       = deck.listName,
                    totalCount     = totalCount
                )
            }
        }
        .catch { emit(emptyList()) }
        .stateIn(viewModelScope, SharingStarted.Eagerly, emptyList())

    val drawerBadges: StateFlow<DrawerBadges> = MutableStateFlow(DrawerBadges())

    private val _settingsHintEvent = MutableStateFlow(false)
    val settingsHintEvent: StateFlow<Boolean> = _settingsHintEvent

    fun onDrawerOpened() {
        if (!BuildConfig.DEBUG) return
        viewModelScope.launch {
            if (!preferences.isSettingsHintShown()) {
                preferences.markSettingsHintShown()
                _settingsHintEvent.value = true
            }
        }
    }

    fun onSettingsHintConsumed() { _settingsHintEvent.value = false }

    fun setActiveDeck(deckId: String?) {
        viewModelScope.launch { preferences.setActiveDeckId(deckId) }
    }

    class Factory(
        private val preferences: AppPreferences,
        private val deckRepository: DeckRepository,
        private val cardRepository: CardRepository
    ) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T =
            MainViewModel(preferences, deckRepository, cardRepository) as T
    }
}
