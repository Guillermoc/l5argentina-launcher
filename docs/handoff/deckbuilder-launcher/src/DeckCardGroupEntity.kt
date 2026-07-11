package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.PrimaryKey

@Entity(
    tableName = "deck_card_groups",
    foreignKeys = [
        ForeignKey(
            entity = DeckEntity::class,
            parentColumns = ["id"],
            childColumns = ["deck_id"],
            onDelete = ForeignKey.CASCADE
        )
    ]
)
data class DeckCardGroupEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "deck_id") val deckId: String,
    val name: String,
    @ColumnInfo(name = "parent_group") val parentGroup: String // "starter", "dynasty", "fate"
)
