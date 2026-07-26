package com.guillermoc.l5argentina.sync.importer

import com.guillermoc.l5argentina.data.db.dao.CatalogDao
import com.guillermoc.l5argentina.data.db.dao.FormatInitialHoldingDao
import com.guillermoc.l5argentina.data.db.dao.KeywordDao
import com.guillermoc.l5argentina.data.db.entity.FormatEntity
import com.guillermoc.l5argentina.data.db.entity.FormatInitialHoldingEligibleCardEntity
import com.guillermoc.l5argentina.data.db.entity.FormatInitialHoldingSlotEntity
import com.guillermoc.l5argentina.data.db.entity.KeywordEntity
import com.guillermoc.l5argentina.data.db.entity.SetEntity
import com.guillermoc.l5argentina.data.remote.FiltersJson
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json

class FiltersImporter(
    private val catalogDao: CatalogDao,
    private val formatInitialHoldingDao: FormatInitialHoldingDao,
    private val keywordDao: KeywordDao,
    private val json: Json
) {

    suspend fun import(filtersJsonString: String) = withContext(Dispatchers.IO) {
        val filters = json.decodeFromString<FiltersJson>(filtersJsonString)

        val sets = filters.sets.map { SetEntity(code = it.code, name = it.name) }
        val formats = filters.formats.map { FormatEntity(code = it.code, name = it.name) }
        val keywords = filters.keywords.map { KeywordEntity(code = it.code, name = it.name) }

        catalogDao.deleteAllSets()
        catalogDao.insertSets(sets)
        catalogDao.deleteAllFormats()
        catalogDao.insertFormats(formats)
        keywordDao.deleteAll()
        if (keywords.isNotEmpty()) keywordDao.insertAll(keywords)

        // Initial holding slots (catalog data — wipe and recreate)
        formatInitialHoldingDao.deleteAllSlots()
        formatInitialHoldingDao.deleteAllEligibleCards()

        val slots = filters.formats.flatMap { format ->
            format.initialHoldingSlots.map { slot ->
                FormatInitialHoldingSlotEntity(
                    formatId = format.code,
                    slotId   = slot.id,
                    label    = slot.label,
                    required = slot.required
                )
            }
        }
        val eligibleCards = filters.formats.flatMap { format ->
            format.initialHoldingSlots.flatMap { slot ->
                slot.eligibleCards.map { cardTitle ->
                    FormatInitialHoldingEligibleCardEntity(
                        formatId  = format.code,
                        slotId    = slot.id,
                        cardTitle = cardTitle
                    )
                }
            }
        }

        if (slots.isNotEmpty()) formatInitialHoldingDao.insertSlots(slots)
        if (eligibleCards.isNotEmpty()) formatInitialHoldingDao.insertEligibleCards(eligibleCards)
    }
}
