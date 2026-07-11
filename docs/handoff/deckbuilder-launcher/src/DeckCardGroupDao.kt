package com.guillermoc.l5argentina.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import com.guillermoc.l5argentina.data.db.entity.DeckCardGroupEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface DeckCardGroupDao {

    @Query("SELECT * FROM deck_card_groups WHERE deck_id = :deckId ORDER BY parent_group ASC, name ASC")
    fun getGroupsForDeck(deckId: String): Flow<List<DeckCardGroupEntity>>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(group: DeckCardGroupEntity)

    @Query("DELETE FROM deck_card_groups WHERE id = :id")
    suspend fun delete(id: String)

    @Query("UPDATE deck_card_groups SET name = :name WHERE id = :id")
    suspend fun rename(id: String, name: String)

    @Query("UPDATE deck_cards SET card_group_id = NULL WHERE card_group_id = :groupId")
    suspend fun unlinkCards(groupId: String)
}
