package com.guillermoc.l5argentina.sync.importer

import androidx.room.withTransaction
import com.guillermoc.l5argentina.data.db.AppDatabase
import com.guillermoc.l5argentina.data.db.dao.CardDao
import com.guillermoc.l5argentina.data.db.entity.CardEntity
import com.guillermoc.l5argentina.data.model.CardType
import com.guillermoc.l5argentina.data.remote.CardJson
import com.guillermoc.l5argentina.data.remote.CardRule
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject

/**
 * Parses cards.json, applies rules, and bulk-inserts into Room.
 *
 * JSON structure: flat array of CardJson objects.
 *
 * maxCopies logic:
 *   1. Base: Stronghold|Sensei → 1; "Unique" keyword in text → 1; else → 3
 *   2. rules.json maxCopies overrides all (0 = banned)
 *
 * imagePath: card.image strips "images/" prefix → "cards/{edition}/{id}.jpg"
 * deckGroup: derived from card type
 */
class CardImporter(
    private val database: AppDatabase,
    private val cardDao: CardDao,
    private val json: Json
) {

    companion object {
        private const val INSERT_BATCH_SIZE = 500
        private val ACTION_KW_LEGAL = setOf("ivory", "TwentyFestivals", "samurai")
        private val ACTION_DESIGNATORS = setOf("Open", "Limited", "Engage", "Battle", "Dynasty", "Reaction", "Interrupt")
        private val COMPOUND_ACTION_KEYWORDS = setOf("Bushido Virtue", "Dark Virtue")
        // Matches <b>Term:</b> or <b>Term</b>: (action keyword labels in card text body)
        private val BOLD_COLON_RE = Regex("""<b>([^<]+):</b>|<b>([^<]+)</b>:""")
    }

    suspend fun import(
        cardsJsonString: String,
        rules: Map<String, CardRule> = emptyMap(),
        initialHoldingByCard: Map<String, String> = emptyMap()
    ): Int = withContext(Dispatchers.IO) {
        val cards = parseCards(cardsJsonString)
        // Transacción: los observers de `cards` (relaciones DeckCardWithCard non-null)
        // no deben ver la ventana vacía entre deleteAll y el último chunk.
        database.withTransaction {
            cardDao.deleteAll()
            var count = 0
            cards.chunked(INSERT_BATCH_SIZE).forEach { batch ->
                val entities = batch.mapNotNull { toEntity(it, rules[it.name], initialHoldingByCard[it.name]) }
                cardDao.insertAll(entities)
                count += entities.size
            }
            count
        }
    }

    // ---------------------------------------------------------------------------

    private fun parseCards(raw: String): List<CardJson> {
        val element = json.parseToJsonElement(raw.removePrefix("﻿"))
        val array: JsonArray = when {
            element is JsonArray -> element
            element is JsonObject && element.containsKey("cards") ->
                element["cards"]!!.jsonArray
            else -> throw IllegalArgumentException("Unexpected cards.json root structure")
        }
        return array.map { json.decodeFromJsonElement<CardJson>(normalizePrintings(it.jsonObject)) }
    }

    private fun normalizePrintings(obj: JsonObject): JsonObject {
        var result = obj

        // printings: objeto suelto → array de un elemento
        val printings = result["printings"]
        if (printings != null && printings !is JsonArray) {
            result = JsonObject(result.toMutableMap().also { it["printings"] = JsonArray(listOf(printings)) })
        }

        // legalities dentro de cada printing: string → array
        val printingsArray = result["printings"] as? JsonArray
        if (printingsArray != null) {
            val fixed = JsonArray(printingsArray.map { elem ->
                val p = elem as? JsonObject ?: return@map elem
                val leg = p["legalities"]
                if (leg != null && leg !is JsonArray)
                    JsonObject(p.toMutableMap().also { it["legalities"] = JsonArray(listOf(leg)) })
                else p
            })
            result = JsonObject(result.toMutableMap().also { it["printings"] = fixed })
        }

        // legalities top-level en CardJson: string → array
        val topLeg = result["legalities"]
        if (topLeg != null && topLeg !is JsonArray) {
            result = JsonObject(result.toMutableMap().also { it["legalities"] = JsonArray(listOf(topLeg)) })
        }

        // keywords: cualquier valor no-array (string, string vacío) → array vacío
        val kw = result["keywords"]
        if (kw != null && kw !is JsonArray) {
            result = JsonObject(result.toMutableMap().also { it["keywords"] = JsonArray(emptyList()) })
        }

        return result
    }

    private fun toEntity(card: CardJson, rule: CardRule?, initialHoldingSlot: String?): CardEntity? {
        val type = card.type ?: return null
        return try {
            val allSets = card.editions.ifEmpty {
                card.printings.map { it.edition }.distinct().ifEmpty { listOfNotNull(card.edition) }
            }
            val allLegalities = card.legalities.ifEmpty {
                card.printings.flatMap { it.legalities }.distinct().ifEmpty { listOfNotNull(card.legal) }
            }

            val currentPrinting = card.printings.firstOrNull { it.current } ?: card.printings.lastOrNull()
            val imagePath: String? = when (val preferred = rule?.preferredPrinting) {
                null -> currentPrinting?.image ?: card.image
                else -> card.printings.find { it.edition == preferred }?.image
                    ?: "images/cards/$preferred/${card.id}.jpg"
            }
            val printingImages = serializeList(card.printings.mapNotNull { it.image }.distinct())

            val banned = (rule?.banned ?: false) || rule?.maxCopies == 0
            val maxCopies = when {
                banned -> 0
                rule?.maxCopies != null -> rule.maxCopies
                else -> calculateMaxCopies(card)
            }

            CardEntity(
                id                        = card.id,
                name                      = card.name,
                type                      = type,
                clan                      = card.clan,
                deckGroup                 = if (initialHoldingSlot != null) "starter" else deriveDeckGroup(type),
                rarity                    = card.rarity ?: "",
                sets                      = serializeList(allSets),
                legality                  = serializeList(allLegalities),
                keywords                  = serializeList(card.keywords ?: emptyList()),
                text                      = card.text,
                flavor                    = card.flavor,
                force                     = parseStatInt(card.force),
                forceSpecial              = isSpecialStat(card.force),
                chi                       = card.chi?.toIntOrNull(),
                cost                      = parseStatInt(card.cost),
                costSpecial               = isSpecialStat(card.cost),
                honor                     = parseHonorInt(card.honorReq),
                honorReqNegative          = isDashStat(card.honorReq),
                personalHonor             = parseHonorInt(card.personalHonor),
                personalHonorNegative     = isDashStat(card.personalHonor),
                focus                     = card.focus?.toIntOrNull(),
                goldProduction            = parseStatInt(card.goldProduction),
                goldProductionSpecial     = isSpecialStat(card.goldProduction),
                provinceStrength          = card.provinceStrength?.toIntOrNull(),
                startingHonor             = card.startingHonor?.toIntOrNull(),
                artist                    = card.artist,
                imagePath                 = imagePath,
                printingImages            = printingImages,
                maxCopies                 = maxCopies,
                banned                    = banned,
                note                      = rule?.note,
                labelText                 = rule?.label?.text,
                labelColor                = rule?.label?.color,
                highlightColor            = rule?.highlight?.color,
                initialHoldingSlot        = initialHoldingSlot,
                subtitle                  = extractSubtitle(card.name),
                actionKeywords            = extractActionKeywords(card.text, allLegalities),
            )
        } catch (e: Exception) {
            null
        }
    }

    /** Parses a stat string that may contain `*` or `+*` — returns null for those. */
    private fun parseStatInt(value: String?): Int? {
        if (value == null || value.contains('*')) return null
        return value.toIntOrNull()
    }

    /** True if the stat value contains `*` (e.g. `*`, `+*`, `1*`). */
    private fun isSpecialStat(value: String?): Boolean = value != null && value.contains('*')

    /** Parses honor_req / personal_honor: returns null for `-` (dash = any/special). */
    private fun parseHonorInt(value: String?): Int? {
        if (value == null || value == "-") return null
        return value.toIntOrNull()
    }

    /** True if the honor stat is `-` (no numeric value, special). */
    private fun isDashStat(value: String?): Boolean = value == "-"

    /** Everything after the first comma in the card name, trimmed. Null if no comma. */
    private fun extractSubtitle(name: String): String? {
        val idx = name.indexOf(',')
        return if (idx == -1) null else name.substring(idx + 1).trim()
    }

    private fun extractActionKeywords(text: String?, legalities: List<String>): String {
        if (text == null || legalities.none { it in ACTION_KW_LEGAL }) return ""
        val found = linkedSetOf<String>()
        for (match in BOLD_COLON_RE.findAll(text)) {
            val raw = (match.groupValues[1].ifEmpty { match.groupValues[2] }).trim()
            for (part in raw.split("&#8226;", "•")) {
                val words = part.trim().trimEnd(':').trim()
                    .split(Regex("\\s+")).filter { it.isNotBlank() }
                if (words.isEmpty() || words.last() !in ACTION_DESIGNATORS) continue
                val remaining = words.dropLast(1)
                var i = 0
                while (i < remaining.size) {
                    val twoWord = if (i + 1 < remaining.size) "${remaining[i]} ${remaining[i + 1]}" else ""
                    if (twoWord in COMPOUND_ACTION_KEYWORDS) { found.add(twoWord); i += 2 }
                    else { found.add(remaining[i]); i++ }
                }
            }
        }
        return serializeList(found.toList())
    }

    private fun calculateMaxCopies(card: CardJson): Int {
        val type = CardType.fromKey(card.type.orEmpty())
        if (type == CardType.STRONGHOLD || type == CardType.SENSEI) return 1
        if (isUnique(card)) return 1
        return 3
    }

    private fun isUnique(card: CardJson): Boolean {
        card.keywords?.let { return it.any { kw -> kw.equals("Unique", ignoreCase = true) } }
        // Fallback: parse first <b> block from text for cards without keywords field
        val text = card.text ?: return false
        val firstBold = Regex("<b>([^<]+)</b>").find(text)?.groupValues?.get(1) ?: return false
        return firstBold.split("&#8226;", "•").any { it.trim().equals("Unique", ignoreCase = true) }
    }

    private fun deriveDeckGroup(type: String): String = when (type.lowercase()) {
        "stronghold", "sensei", "wind"                              -> "starter"
        "celestial", "event", "holding", "region", "personality"   -> "dynasty"
        "ancestor", "follower", "ring", "item", "spell", "strategy" -> "fate"
        else -> ""
    }

    private fun serializeList(list: List<String>): String {
        if (list.isEmpty()) return ""
        return "|${list.joinToString("|")}|"
    }
}
