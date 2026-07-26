package com.guillermoc.l5argentina.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import com.guillermoc.l5argentina.data.db.entity.DeckGroupEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface DeckGroupDao {

    @Query("SELECT * FROM deck_groups WHERE section_type = :sectionType ORDER BY name ASC")
    fun getGroupsBySection(sectionType: String): Flow<List<DeckGroupEntity>>

    @Query("SELECT * FROM deck_groups ORDER BY section_type ASC, name ASC")
    fun getAllGroups(): Flow<List<DeckGroupEntity>>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(group: DeckGroupEntity)

    @Query("DELETE FROM deck_groups WHERE id = :id")
    suspend fun delete(id: String)

    @Query("UPDATE deck_groups SET name = :name WHERE id = :id")
    suspend fun rename(id: String, name: String)

    @Query("UPDATE decks SET group_id = NULL WHERE group_id = :groupId")
    suspend fun unlinkDecks(groupId: String)

    @Query("DELETE FROM deck_groups")
    suspend fun deleteAll()
}
