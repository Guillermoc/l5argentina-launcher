package com.guillermoc.l5argentina.data.db.dao

import androidx.room.ColumnInfo
import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import com.guillermoc.l5argentina.data.db.entity.FormatInitialHoldingEligibleCardEntity
import com.guillermoc.l5argentina.data.db.entity.FormatInitialHoldingSlotEntity
import kotlinx.coroutines.flow.Flow

data class EligibleCardSlot(
    @ColumnInfo(name = "card_title") val cardTitle: String,
    @ColumnInfo(name = "slot_id") val slotId: String
)

@Dao
interface FormatInitialHoldingDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertSlots(slots: List<FormatInitialHoldingSlotEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertEligibleCards(cards: List<FormatInitialHoldingEligibleCardEntity>)

    @Query("DELETE FROM format_initial_holding_slots")
    suspend fun deleteAllSlots()

    @Query("DELETE FROM format_initial_holding_eligible_cards")
    suspend fun deleteAllEligibleCards()

    @Query("SELECT card_title, slot_id FROM format_initial_holding_eligible_cards")
    suspend fun getAllEligibleCardSlots(): List<EligibleCardSlot>

    @Query("SELECT * FROM format_initial_holding_slots WHERE required = 1")
    fun observeRequiredSlots(): Flow<List<FormatInitialHoldingSlotEntity>>

    @Query("SELECT COUNT(*) FROM format_initial_holding_slots WHERE required = 1")
    fun countRequiredSlots(): Flow<Int>
}
