package com.guillermoc.l5argentina.data.repository

import androidx.sqlite.db.SimpleSQLiteQuery
import com.guillermoc.l5argentina.data.db.CardQueryCompiler
import com.guillermoc.l5argentina.data.db.dao.CardDao
import com.guillermoc.l5argentina.data.db.dao.CatalogDao
import com.guillermoc.l5argentina.data.db.dao.KeywordDao
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.db.entity.FormatEntity
import com.guillermoc.l5argentina.data.db.entity.KeywordEntity
import com.guillermoc.l5argentina.data.db.entity.SetEntity
import com.guillermoc.l5argentina.data.model.CardSearchCriteria
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

class CardRepository(
    private val cardDao: CardDao,
    private val catalogDao: CatalogDao,
    private val keywordDao: KeywordDao,
    private val compiler: CardQueryCompiler = CardQueryCompiler()
) {

    // --- Search ---

    fun search(criteria: CardSearchCriteria): Flow<List<CardEntity>> =
        cardDao.search(compiler.compile(criteria))

    // --- Single card ---

    suspend fun getById(id: String): CardEntity? = cardDao.getById(id)

    suspend fun getByName(name: String): CardEntity? = cardDao.getByName(name)

    suspend fun count(): Int = cardDao.count()

    fun searchSuggestions(query: String): Flow<List<CardEntity>> {
        val q = SimpleSQLiteQuery(
            "SELECT * FROM cards WHERE name LIKE ? ORDER BY name ASC LIMIT 8",
            arrayOf("%$query%")
        )
        return cardDao.search(q)
    }

    // --- Catalog lists (for filter modal) ---

    fun getAllSets(): Flow<List<SetEntity>> = catalogDao.getAllSets()

    fun getAllFormats(): Flow<List<FormatEntity>> = catalogDao.getAllFormats()

    fun hasInitialHoldingCards(): Flow<Boolean> = cardDao.countInitialHoldingCards().map { it > 0 }

    fun getAllKeywords(): Flow<List<KeywordEntity>> = keywordDao.getAll()

    fun getAllKeywordCodes(): Flow<List<String>> = keywordDao.getAllCodesFlow()
}
