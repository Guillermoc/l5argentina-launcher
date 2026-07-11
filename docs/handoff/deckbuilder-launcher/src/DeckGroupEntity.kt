package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "deck_groups")
data class DeckGroupEntity(
    @PrimaryKey val id: String,
    val name: String,
    @ColumnInfo(name = "section_type") val sectionType: String // "deck" or "list"
)
