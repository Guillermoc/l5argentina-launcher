package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity

@Entity(
    tableName = "format_initial_holding_slots",
    primaryKeys = ["format_id", "slot_id"]
)
data class FormatInitialHoldingSlotEntity(
    @ColumnInfo(name = "format_id") val formatId: String,
    @ColumnInfo(name = "slot_id") val slotId: String,
    val label: String,
    val required: Boolean = true
)
