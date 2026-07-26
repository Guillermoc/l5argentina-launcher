package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "decks")
data class DeckEntity(
    @PrimaryKey val id: String,
    val name: String,
    @ColumnInfo(name = "created_at") val createdAt: Long = System.currentTimeMillis(),
    val notes: String? = null,
    @ColumnInfo(name = "type", defaultValue = "deck") val type: String = "deck",
    @ColumnInfo(name = "list_name") val listName: String? = null,
    @ColumnInfo(name = "list_description") val listDescription: String? = null,
    @ColumnInfo(name = "group_id") val groupId: String? = null,
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Int = 0,
    @ColumnInfo(name = "sort_mode") val sortMode: String? = null
)
