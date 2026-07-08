package com.guillermoc.l5argentina.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.RawQuery
import androidx.sqlite.db.SupportSQLiteQuery
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.db.entity.UserCardDataEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface CardDao {

    /**
     * Dynamic search query built by CardRepository.buildFilterQuery().
     * observedEntities includes UserCardDataEntity so Room re-emits when
     * favorites/hidden change (the query uses subqueries against that table).
     */
    @RawQuery(observedEntities = [CardEntity::class, UserCardDataEntity::class])
    fun search(query: SupportSQLiteQuery): Flow<List<CardEntity>>

    @Query("SELECT * FROM cards WHERE id = :id")
    suspend fun getById(id: String): CardEntity?

    @Query("SELECT * FROM cards WHERE name = :name LIMIT 1")
    suspend fun getByName(name: String): CardEntity?

    @Query("SELECT COUNT(*) FROM cards")
    suspend fun count(): Int

    @Query("SELECT COUNT(*) FROM cards WHERE initial_holding_slot IS NOT NULL")
    fun countInitialHoldingCards(): Flow<Int>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(cards: List<CardEntity>)

    @Query("DELETE FROM cards")
    suspend fun deleteAll()

    @Query("""
        UPDATE cards SET
            banned = 0,
            max_copies = CASE
                WHEN type IN ('stronghold', 'sensei') THEN 1
                WHEN keywords LIKE '%|Unique|%' THEN 1
                ELSE 3
            END,
            note = NULL,
            label_text = NULL,
            label_color = NULL,
            highlight_color = NULL
    """)
    suspend fun resetAllRules()

    @Query("""UPDATE cards SET banned = :banned, max_copies = :maxCopies, note = :note,
        label_text = :labelText, label_color = :labelColor, highlight_color = :highlightColor
        WHERE name = :name""")
    suspend fun applyCardRule(
        name: String, banned: Boolean, maxCopies: Int,
        note: String?, labelText: String?, labelColor: String?, highlightColor: String?
    ): Int

    @Query("""UPDATE cards SET banned = :banned, note = :note,
        label_text = :labelText, label_color = :labelColor, highlight_color = :highlightColor
        WHERE name = :name""")
    suspend fun applyCardRuleKeepMaxCopies(
        name: String, banned: Boolean,
        note: String?, labelText: String?, labelColor: String?, highlightColor: String?
    ): Int
}
