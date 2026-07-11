package com.guillermoc.l5argentina.data.db.relation

import androidx.room.Embedded
import androidx.room.Relation
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.db.entity.DeckCardEntity

/**
 * Fila cruda devuelta por DeckDao. `card` es nullable porque deck_cards puede
 * referenciar cartas ausentes del catálogo (catálogo vacío tras un import
 * fallido, carta eliminada del cards.json, wipe de catálogo). Un null acá no
 * debe crashear la app: DeckRepository filtra estas filas.
 */
data class DeckCardWithCardRow(
    @Embedded val deckCard: DeckCardEntity,
    @Relation(
        parentColumn = "card_id",
        entityColumn = "id"
    )
    val card: CardEntity?
)

/**
 * Tipo expuesto por DeckRepository hacia ViewModels/export: `card` garantizada
 * non-null (las filas huérfanas ya fueron filtradas).
 */
data class DeckCardWithCard(
    val deckCard: DeckCardEntity,
    val card: CardEntity
)
