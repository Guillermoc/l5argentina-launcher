package com.guillermoc.l5argentina.sync.importer

import com.guillermoc.l5argentina.data.db.dao.CardDao
import com.guillermoc.l5argentina.data.remote.CardRule
import com.guillermoc.l5argentina.data.remote.RulesJson
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json

class RulesImporter(private val json: Json, private val cardDao: CardDao) {

    /** Returns map title→CardRule; used by CardImporter for preferredPrinting. */
    fun parse(rulesJsonString: String): Map<String, CardRule> {
        val rulesJson = json.decodeFromString<RulesJson>(rulesJsonString)
        return rulesJson.rules.associateBy { it.title }
    }

    /**
     * Wipes all rule fields on every card, then applies each rule from the JSON.
     * Returns the list of titles that didn't match any card in the DB.
     */
    suspend fun applyRules(rulesJsonString: String): List<String> = withContext(Dispatchers.IO) {
        val rulesJson = json.decodeFromString<RulesJson>(rulesJsonString)
        cardDao.resetAllRules()
        val unmatched = mutableListOf<String>()
        for (rule in rulesJson.rules) {
            try {
                val banned = rule.banned == true || rule.maxCopies == 0
                val maxCopiesOverride = rule.maxCopies?.coerceAtLeast(0)
                val updated = if (maxCopiesOverride != null) {
                    cardDao.applyCardRule(
                        rule.title, banned, maxCopiesOverride,
                        rule.note, rule.label?.text, rule.label?.color, rule.highlight?.color
                    )
                } else {
                    cardDao.applyCardRuleKeepMaxCopies(
                        rule.title, banned,
                        rule.note, rule.label?.text, rule.label?.color, rule.highlight?.color
                    )
                }
                if (updated == 0) unmatched.add(rule.title)
            } catch (e: Exception) {
                unmatched.add(rule.title)
            }
        }
        unmatched
    }
}
