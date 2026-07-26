// ui/screens/goldfish/GoldfishModels.kt
package com.guillermoc.l5argentina.ui.screens.goldfish

import com.guillermoc.l5argentina.data.db.entity.CardEntity

/** Copia física de una carta en la sesión de testeo (un mazo puede tener N copias del mismo card.id). */
data class GfCardInstance(
    val instanceId: String,
    val card: CardEntity
)

/** Carta en un área de juego, con estado enderezada / girada 90°. */
data class GfPlacedCard(
    val instance: GfCardInstance,
    val bowed: Boolean = false
)

/** Grupo visual del área de Personalidades: carta raíz + cartas attacheadas (se muestran detrás/debajo). */
data class GfStack(
    val root: GfPlacedCard,
    val attachments: List<GfPlacedCard> = emptyList()
)

/** Slot de provincia: carta de Dynasty boca arriba/abajo, o vacío si el mazo se agotó. */
data class GfProvince(
    val card: GfCardInstance? = null,
    val faceUp: Boolean = false
)

/** Zonas destino de drag & drop. */
sealed interface GfZone {
    data object Hand : GfZone
    data class Province(val index: Int) : GfZone
    data object StrongholdArea : GfZone
    data object PersonalityArea : GfZone
    data object DynastyDiscard : GfZone
    data object FateDiscard : GfZone
    data object DynastyDeckTop : GfZone
    data object FateDeckTop : GfZone
}

/** Estado completo e inmutable de la sesión. Índice 0 = tope de cada pila. */
data class GoldfishState(
    val dynastyDeck: List<GfCardInstance> = emptyList(),
    val fateDeck: List<GfCardInstance> = emptyList(),
    val dynastyDiscard: List<GfCardInstance> = emptyList(),
    val fateDiscard: List<GfCardInstance> = emptyList(),
    val hand: List<GfCardInstance> = emptyList(),
    val provinces: List<GfProvince> = List(PROVINCE_COUNT) { GfProvince() },
    val strongholdArea: List<GfPlacedCard> = emptyList(),
    val personalityArea: List<GfStack> = emptyList(),
    val honorCounter: Int = 0,
    val turn: Int = 1,
    val isLoaded: Boolean = false
) {
    companion object {
        const val PROVINCE_COUNT = 4
        const val INITIAL_HAND_SIZE = 5
    }
}
