// data/db/entity/UserCardDataEntity.kt
package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity

@Entity(tableName = "user_card_data", primaryKeys = ["card_id", "type"])
data class UserCardDataEntity(
    @ColumnInfo(name = "card_id") val cardId: String,
    val type: String
)
