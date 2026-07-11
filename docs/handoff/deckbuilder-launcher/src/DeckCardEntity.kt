package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.ForeignKey

@Entity(
    tableName = "deck_cards",
    primaryKeys = ["deck_id", "card_id"],
    foreignKeys = [
        ForeignKey(
            entity = DeckEntity::class,
            parentColumns = ["id"],
            childColumns = ["deck_id"],
            onDelete = ForeignKey.CASCADE
        )
    ]
)
data class DeckCardEntity(
    @ColumnInfo(name = "deck_id") val deckId: String,
    @ColumnInfo(name = "card_id") val cardId: String,
    val quantity: Int,
    @ColumnInfo(name = "card_group_id") val cardGroupId: String? = null,
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Int = 0
)
