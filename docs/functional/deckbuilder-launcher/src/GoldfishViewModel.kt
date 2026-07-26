// ui/screens/goldfish/GoldfishViewModel.kt
package com.guillermoc.l5argentina.ui.screens.goldfish

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.db.relation.DeckCardWithCard
import com.guillermoc.l5argentina.data.repository.DeckRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.util.UUID

sealed class GoldfishModal {
    data object Closed : GoldfishModal()
    data object ResetConfirm : GoldfishModal()
    data class CardPreview(val card: CardEntity) : GoldfishModal()
}

/**
 * Sandbox de testeo en solitario (goldfish). Todo el estado vive en memoria:
 * no persiste nada ni muta el mazo — DeckRepository se usa solo para leerlo.
 */
class GoldfishViewModel(
    private val deckId: String,
    private val deckRepository: DeckRepository
) : ViewModel() {

    private val _deckName = MutableStateFlow("")
    val deckName: StateFlow<String> = _deckName

    private val _state = MutableStateFlow(GoldfishState())
    val state: StateFlow<GoldfishState> = _state

    private val _modal = MutableStateFlow<GoldfishModal>(GoldfishModal.Closed)
    val modal: StateFlow<GoldfishModal> = _modal

    private var deckCards: List<DeckCardWithCard> = emptyList()

    init {
        viewModelScope.launch(Dispatchers.IO) {
            _deckName.value = deckRepository.getDeckById(deckId)?.name.orEmpty()
            deckCards = deckRepository.getDeckCardsWithCards(deckId).first()
                .filter { it.deckCard.quantity > 0 }
            _state.value = buildInitialState()
        }
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private fun buildInitialState(): GoldfishState {
        fun instancesOf(group: String): List<GfCardInstance> =
            deckCards.filter { it.card.deckGroup == group }.flatMap { row ->
                List(row.deckCard.quantity) { GfCardInstance(UUID.randomUUID().toString(), row.card) }
            }

        val starter = instancesOf("starter").sortedWith(
            compareBy<GfCardInstance>(
                { starterRank(it.card.type) },
                { it.card.initialHoldingSlot ?: "zzz" },
                { it.card.name }
            )
        )
        val dynasty = instancesOf("dynasty").shuffled()
        val fate    = instancesOf("fate").shuffled()

        val provinces = List(GoldfishState.PROVINCE_COUNT) { i ->
            GfProvince(card = dynasty.getOrNull(i), faceUp = false)
        }
        return GoldfishState(
            dynastyDeck    = dynasty.drop(GoldfishState.PROVINCE_COUNT),
            fateDeck       = fate.drop(GoldfishState.INITIAL_HAND_SIZE),
            hand           = fate.take(GoldfishState.INITIAL_HAND_SIZE),
            provinces      = provinces,
            strongholdArea = starter.map { GfPlacedCard(it) },
            honorCounter   = starter.firstOrNull { it.card.type == "stronghold" }?.card?.startingHonor ?: 0,
            isLoaded       = true
        )
    }

    private fun starterRank(type: String): Int = when (type) {
        "stronghold" -> 0
        "sensei" -> 1
        "holding" -> 2
        else -> 3
    }

    // ── Controles generales ───────────────────────────────────────────────────

    fun requestReset() { _modal.value = GoldfishModal.ResetConfirm }

    fun confirmReset() {
        _modal.value = GoldfishModal.Closed
        _state.value = buildInitialState()
    }

    fun openPreview(card: CardEntity) { _modal.value = GoldfishModal.CardPreview(card) }

    fun closeModal() { _modal.value = GoldfishModal.Closed }

    fun endTurn() = _state.update { it.copy(turn = it.turn + 1) }

    fun adjustHonor(delta: Int) =
        _state.update { it.copy(honorCounter = it.honorCounter + delta) }

    fun revealAllProvinces() = _state.update { s ->
        s.copy(provinces = s.provinces.map { if (it.card != null) it.copy(faceUp = true) else it })
    }

    // ── Mazos ─────────────────────────────────────────────────────────────────

    fun shuffleDynasty() = _state.update { it.copy(dynastyDeck = it.dynastyDeck.shuffled()) }

    fun shuffleFate() = _state.update { it.copy(fateDeck = it.fateDeck.shuffled()) }

    /** Robar de Fate: la carta del tope va a la mano. */
    fun drawFateCard() = _state.update { s ->
        val top = s.fateDeck.firstOrNull() ?: return@update s
        s.copy(fateDeck = s.fateDeck.drop(1), hand = s.hand + top)
    }

    /** Robar de Dynasty: repone la primera provincia vacía, boca abajo. */
    fun fillNextProvince() = _state.update { s ->
        val idx = s.provinces.indexOfFirst { it.card == null }
        val top = s.dynastyDeck.firstOrNull()
        if (idx < 0 || top == null) return@update s
        s.copy(
            dynastyDeck = s.dynastyDeck.drop(1),
            provinces   = s.provinces.toMutableList().apply { this[idx] = GfProvince(top, faceUp = false) }
        )
    }

    // ── Interacciones con cartas ──────────────────────────────────────────────

    /** Flippea la carta de una provincia (boca arriba ↔ boca abajo). */
    fun tapProvince(index: Int) = _state.update { s ->
        val p = s.provinces.getOrNull(index)?.takeIf { it.card != null } ?: return@update s
        s.copy(provinces = s.provinces.toMutableList().apply { this[index] = p.copy(faceUp = !p.faceUp) })
    }

    /** Rota la carta (enderezada ↔ 90°) en Stronghold/Holdings o Personalidades. */
    fun toggleBow(instanceId: String) = _state.update { s ->
        fun flip(p: GfPlacedCard) = if (p.instance.instanceId == instanceId) p.copy(bowed = !p.bowed) else p
        s.copy(
            strongholdArea  = s.strongholdArea.map(::flip),
            personalityArea = s.personalityArea.map { st ->
                st.copy(root = flip(st.root), attachments = st.attachments.map(::flip))
            }
        )
    }

    /** Mueve una carta (esté donde esté) a la zona destino. Provincias ocupadas rechazan el drop. */
    fun moveCard(instanceId: String, target: GfZone) = _state.update { s ->
        if (target is GfZone.Province) {
            val slot = s.provinces.getOrNull(target.index) ?: return@update s
            if (slot.card != null) return@update s   // ocupada (incluye soltarla sobre sí misma)
        }
        val removal = s.removeInstance(instanceId) ?: return@update s
        removal.state.insert(removal.instance, target)
    }

    /** Attach: agrupa la carta arrastrada detrás/debajo de la carta destino en Personalidades. */
    fun attachCard(instanceId: String, targetInstanceId: String) {
        if (instanceId == targetInstanceId) return
        _state.update { s ->
            val removal = s.removeInstance(instanceId) ?: return@update s
            val after = removal.state
            val idx = after.personalityArea.indexOfFirst { st ->
                st.root.instance.instanceId == targetInstanceId ||
                    st.attachments.any { it.instance.instanceId == targetInstanceId }
            }
            if (idx < 0) return@update s   // el destino desapareció al remover — se cancela el movimiento
            val area = after.personalityArea.toMutableList()
            area[idx] = area[idx].copy(attachments = area[idx].attachments + GfPlacedCard(removal.instance))
            after.copy(personalityArea = area)
        }
    }

    // ── Reducers ──────────────────────────────────────────────────────────────

    private data class Removal(val state: GoldfishState, val instance: GfCardInstance)

    /**
     * Quita la instancia de la zona donde esté. Al salir de una provincia se repone
     * automáticamente desde el Dynasty deck (boca abajo). Si sale la raíz de un stack,
     * el primer attachment se promueve a raíz.
     */
    private fun GoldfishState.removeInstance(instanceId: String): Removal? {
        hand.find { it.instanceId == instanceId }?.let {
            return Removal(copy(hand = hand - it), it)
        }
        provinces.forEachIndexed { i, p ->
            if (p.card?.instanceId == instanceId) {
                val refill = dynastyDeck.firstOrNull()
                val newProvinces = provinces.toMutableList().apply { this[i] = GfProvince(refill, faceUp = false) }
                return Removal(
                    copy(provinces = newProvinces, dynastyDeck = dynastyDeck.drop(1)),
                    p.card
                )
            }
        }
        strongholdArea.find { it.instance.instanceId == instanceId }?.let {
            return Removal(copy(strongholdArea = strongholdArea - it), it.instance)
        }
        personalityArea.forEachIndexed { si, stack ->
            if (stack.root.instance.instanceId == instanceId) {
                val newArea = personalityArea.toMutableList()
                if (stack.attachments.isEmpty()) newArea.removeAt(si)
                else newArea[si] = GfStack(stack.attachments.first(), stack.attachments.drop(1))
                return Removal(copy(personalityArea = newArea), stack.root.instance)
            }
            stack.attachments.find { it.instance.instanceId == instanceId }?.let { att ->
                val newArea = personalityArea.toMutableList()
                newArea[si] = stack.copy(attachments = stack.attachments - att)
                return Removal(copy(personalityArea = newArea), att.instance)
            }
        }
        dynastyDiscard.find { it.instanceId == instanceId }?.let {
            return Removal(copy(dynastyDiscard = dynastyDiscard - it), it)
        }
        fateDiscard.find { it.instanceId == instanceId }?.let {
            return Removal(copy(fateDiscard = fateDiscard - it), it)
        }
        return null
    }

    private fun GoldfishState.insert(instance: GfCardInstance, zone: GfZone): GoldfishState = when (zone) {
        GfZone.Hand            -> copy(hand = hand + instance)
        is GfZone.Province     -> copy(
            provinces = provinces.toMutableList().apply { this[zone.index] = GfProvince(instance, faceUp = false) }
        )
        GfZone.StrongholdArea  -> copy(strongholdArea = strongholdArea + GfPlacedCard(instance))
        GfZone.PersonalityArea -> copy(personalityArea = personalityArea + GfStack(GfPlacedCard(instance)))
        GfZone.DynastyDiscard  -> copy(dynastyDiscard = listOf(instance) + dynastyDiscard)
        GfZone.FateDiscard     -> copy(fateDiscard = listOf(instance) + fateDiscard)
        GfZone.DynastyDeckTop  -> copy(dynastyDeck = listOf(instance) + dynastyDeck)
        GfZone.FateDeckTop     -> copy(fateDeck = listOf(instance) + fateDeck)
    }

    class Factory(
        private val deckId: String,
        private val deckRepository: DeckRepository
    ) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T =
            GoldfishViewModel(deckId, deckRepository) as T
    }
}
