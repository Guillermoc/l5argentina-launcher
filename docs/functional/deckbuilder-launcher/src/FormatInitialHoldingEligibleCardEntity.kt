package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity

@Entity(
    tableName = "format_initial_holding_eligible_cards",
    primaryKeys = ["format_id", "slot_id", "card_title"]
)
data class FormatInitialHoldingEligibleCardEntity(
    @ColumnInfo(name = "format_id") val formatId: String,
    @ColumnInfo(name = "slot_id") val slotId: String,
    @ColumnInfo(name = "card_title") val cardTitle: String
)
