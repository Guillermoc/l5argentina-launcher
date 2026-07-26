package com.guillermoc.l5argentina.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import com.guillermoc.l5argentina.data.db.entity.DeckCardEntity
import com.guillermoc.l5argentina.data.db.entity.DeckEntity
import com.guillermoc.l5argentina.data.db.relation.DeckCardWithCardRow
import kotlinx.coroutines.flow.Flow

@Dao
interface DeckDao {

    @Query("SELECT * FROM decks ORDER BY created_at DESC")
    fun getAllDecks(): Flow<List<DeckEntity>>

    @Query("SELECT * FROM decks WHERE id = :id")
    suspend fun getDeckById(id: String): DeckEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertDeck(deck: DeckEntity)

    @Query("DELETE FROM decks WHERE id = :id")
    suspend fun deleteDeck(id: String)

    @Query("DELETE FROM decks")
    suspend fun deleteAllDecks()

    @Query("UPDATE decks SET name = :name WHERE id = :id")
    suspend fun renameDeck(id: String, name: String)

    @Query("UPDATE decks SET notes = :notes WHERE id = :id")
    suspend fun updateNotes(id: String, notes: String?)

    @Query("UPDATE decks SET group_id = :groupId WHERE id = :id")
    suspend fun updateGroupId(id: String, groupId: String?)

    @Query("UPDATE decks SET sort_order = :sortOrder WHERE id = :id")
    suspend fun updateSortOrder(id: String, sortOrder: Int)

    @Query("UPDATE decks SET sort_mode = :sortMode WHERE id = :id")
    suspend fun updateSortMode(id: String, sortMode: String?)

    @Query("UPDATE deck_cards SET card_group_id = :cardGroupId WHERE deck_id = :deckId AND card_id = :cardId")
    suspend fun updateCardGroupId(deckId: String, cardId: String, cardGroupId: String?)

    @Query("UPDATE deck_cards SET sort_order = :sortOrder WHERE deck_id = :deckId AND card_id = :cardId")
    suspend fun updateCardSortOrder(deckId: String, cardId: String, sortOrder: Int)

    @Query("SELECT COALESCE(MAX(sort_order), -1) FROM deck_cards WHERE deck_id = :deckId")
    suspend fun getMaxCardSortOrder(deckId: String): Int

    // --- deck_cards ---

    @Transaction
    @Query("SELECT * FROM deck_cards WHERE deck_id = :deckId")
    fun getDeckCardsWithCards(deckId: String): Flow<List<DeckCardWithCardRow>>

    @Transaction
    @Query("SELECT * FROM deck_cards")
    fun getAllDeckCardsWithCards(): Flow<List<DeckCardWithCardRow>>

    @Query("SELECT * FROM deck_cards WHERE deck_id = :deckId AND card_id = :cardId")
    suspend fun getDeckCard(deckId: String, cardId: String): DeckCardEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertDeckCard(deckCard: DeckCardEntity)

    @Query("DELETE FROM deck_cards WHERE deck_id = :deckId AND card_id = :cardId")
    suspend fun deleteDeckCard(deckId: String, cardId: String)

    @Query("DELETE FROM deck_cards WHERE deck_id = :deckId AND quantity = 0")
    suspend fun deleteTrimmedCards(deckId: String)

    @Query("DELETE FROM deck_cards WHERE deck_id = :deckId")
    suspend fun deleteAllCardsForDeck(deckId: String)

    @Query("SELECT SUM(quantity) FROM deck_cards WHERE deck_id = :deckId")
    fun getTotalCardCount(deckId: String): Flow<Int?>

    // Huérfanas (ADR-013): filas cuyo card_id no existe en cards. El guard de
    // catálogo no-vacío evita falsos positivos transitorios (recovery, import fallido).
    @Query("""
        SELECT dc.* FROM deck_cards dc
        LEFT JOIN cards c ON c.id = dc.card_id
        WHERE dc.deck_id = :deckId AND c.id IS NULL
          AND (SELECT COUNT(*) FROM cards) > 0
        ORDER BY dc.card_id ASC
    """)
    fun getOrphanDeckCards(deckId: String): Flow<List<DeckCardEntity>>

    @Query("""
        SELECT COUNT(*) FROM deck_cards dc
        LEFT JOIN cards c ON c.id = dc.card_id
        WHERE c.id IS NULL AND (SELECT COUNT(*) FROM cards) > 0
    """)
    suspend fun countOrphanDeckCards(): Int

    @Query("""
        SELECT COUNT(*) FROM deck_cards
        INNER JOIN cards ON deck_cards.card_id = cards.id
        WHERE deck_cards.deck_id = :deckId AND cards.initial_holding_slot = :slot
          AND deck_cards.quantity > 0
    """)
    suspend fun countCardsInSlot(deckId: String, slot: String): Int
}
