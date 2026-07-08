package com.guillermoc.l5argentina.data.db

import androidx.sqlite.db.SimpleSQLiteQuery
import com.guillermoc.l5argentina.data.model.CardSearchCriteria
import com.guillermoc.l5argentina.data.model.SortField

class CardQueryCompiler {

    fun compile(criteria: CardSearchCriteria): SimpleSQLiteQuery {
        val sb = StringBuilder("SELECT * FROM cards WHERE 1=1")
        val args = mutableListOf<Any>()

        // Name — each word must appear (AND)
        criteria.name.trim().split(Regex("\\s+")).filter { it.isNotBlank() }.forEach { word ->
            sb.append(" AND name LIKE ?")
            args.add("%$word%")
        }

        // Keywords — pipe-delimited columns; match mode toggles AND/OR across keyword list
        if (criteria.keywords.isNotEmpty()) {
            val separator = if (criteria.keywordMatchAll) " AND " else " OR "
            val kwClauses = criteria.keywords.map { kw ->
                val parts = mutableListOf<String>()
                parts += "keywords LIKE ?"
                args.add("%|$kw|%")
                if (criteria.includeActionKeywords) {
                    parts += "action_keywords LIKE ?"
                    args.add("%|$kw|%")
                }
                if (criteria.includeSubtitleKeywords) {
                    parts += "subtitle LIKE ?"
                    args.add("%$kw%")
                }
                "(${parts.joinToString(" OR ")})"
            }
            sb.append(" AND (${kwClauses.joinToString(separator)})")
        }

        // Negative keywords — exclude cards that have any of these keywords (pipe-delimited exact match only)
        if (criteria.negativeKeywords.isNotEmpty()) {
            criteria.negativeKeywords.forEach { kw ->
                val parts = mutableListOf<String>()
                parts += "keywords LIKE ?"
                args.add("%|$kw|%")
                if (criteria.includeActionKeywords) {
                    parts += "action_keywords LIKE ?"
                    args.add("%|$kw|%")
                }
                sb.append(" AND NOT (${parts.joinToString(" OR ")})")
            }
        }

        // Designators — el designador siempre es la última palabra del bloque bold.
        // 3 formatos posibles:
        //   <b>Dynasty:</b>          → "%Dynasty:</b>%"
        //   <b>Dynasty</b>:          → "%Dynasty</b>:%"
        //   <b>Dynasty</b>, [costo]: → "%Dynasty</b>,%"
        // Sin % entre el designador y el cierre de bold para evitar falsos positivos
        // (ej: "discard a Dynasty card ... <b>Limited</b>:" matcheaba "Dynasty" con filtro anterior).
        if (criteria.designators.isNotEmpty()) {
            sb.append(" AND (")
            criteria.designators.forEachIndexed { i, d ->
                if (i > 0) sb.append(" OR ")
                sb.append("(text LIKE ? OR text LIKE ? OR text LIKE ?)")
                args.add("%$d:</b>%")
                args.add("%$d</b>:%")
                args.add("%$d</b>,%")
            }
            sb.append(")")
        }

        // Text — quoted phrase → exact substring; unquoted → whole-word AND (space-padded)
        val rawText = criteria.text.trim()
        if (rawText.isNotBlank()) {
            if (rawText.length > 2 && rawText.startsWith("\"") && rawText.endsWith("\"")) {
                sb.append(" AND text LIKE ?")
                args.add("%${rawText.substring(1, rawText.length - 1)}%")
            } else {
                rawText.split(Regex("\\s+")).filter { it.isNotBlank() }.forEach { word ->
                    // Normalize punctuation and HTML tags to spaces before word-boundary matching
                    sb.append(
                        " AND (' ' || replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(" +
                        "text, '''', ' '), '.', ' '), ',', ' '), ':', ' '), ';', ' '), '!', ' '), '?', ' '), '(', ' '), ')', ' '), '<', ' '), '>', ' '), '[', ' '), ']', ' '), '/', ' '), '\"', ' ') || ' ') LIKE ?"
                    )
                    args.add("% $word %")
                }
            }
        }

        // Type
        if (criteria.types.isNotEmpty()) {
            sb.append(" AND type IN (${criteria.types.placeholders()})")
            args.addAll(criteria.types)
        }

        // Clan — "noclan" maps to NULL or minor-faction clans
        // Senseis have clan compatibility in keywords: Dragon, Dragon Clan, All Clans, Any
        if (criteria.clans.isNotEmpty()) {
            val hasNoClan = criteria.clans.any { it.equals("noclan", ignoreCase = true) }
            val namedClans = criteria.clans.filter { !it.equals("noclan", ignoreCase = true) }
            sb.append(" AND (")
            val parts = mutableListOf<String>()
            if (namedClans.isNotEmpty()) {
                parts += "clan IN (${namedClans.placeholders()})"
                args.addAll(namedClans)
                // Sensei: clan compatibility stored in keywords, not clan column
                val senseiKw = mutableListOf<String>()
                namedClans.forEach { clanKey ->
                    val clanDisplay = clanKey.replaceFirstChar { it.uppercase() }
                    senseiKw += "keywords LIKE ?"; args.add("%|$clanDisplay|%")
                    senseiKw += "keywords LIKE ?"; args.add("%|$clanDisplay Clan|%")
                }
                senseiKw += "keywords LIKE ?"; args.add("%|All Clans|%")
                senseiKw += "keywords LIKE ?"; args.add("%|Any|%")
                senseiKw += "keywords LIKE ?"; args.add("%|Any Clan|%")
                parts += "(type = 'sensei' AND (${senseiKw.joinToString(" OR ")}))"
            }
            if (hasNoClan) {
                val minor = listOf("shadowlands", "ratling", "naga", "hare", "ronin", "ninja", "monk", "spirit")
                parts += "clan IS NULL OR clan IN (${minor.placeholders()})"
                args.addAll(minor)
            }
            sb.append(parts.joinToString(" OR "))
            sb.append(")")
        }

        // Rarity
        if (criteria.rarities.isNotEmpty()) {
            sb.append(" AND rarity IN (${criteria.rarities.placeholders()})")
            args.addAll(criteria.rarities)
        }

        // Sets
        if (criteria.sets.isNotEmpty()) {
            sb.append(" AND (")
            criteria.sets.forEachIndexed { i, set ->
                if (i > 0) sb.append(" OR ")
                sb.append("sets LIKE ?")
                args.add("%|$set|%")
            }
            sb.append(")")
        }

        // Formats (legality)
        if (criteria.formats.isNotEmpty()) {
            sb.append(" AND (")
            criteria.formats.forEachIndexed { i, fmt ->
                if (i > 0) sb.append(" OR ")
                sb.append("legality LIKE ?")
                args.add("%|$fmt|%")
            }
            sb.append(")")
        }

        // Force
        appendSpecialOrRange(sb, args,
            column = "force", specialColumn = "force_special",
            min = criteria.forceMin, max = criteria.forceMax,
            includeSpecial = criteria.includeSpecialForce)

        // Chi (no special flag)
        criteria.chiMin?.let { sb.append(" AND chi >= ?"); args.add(it) }
        criteria.chiMax?.let { sb.append(" AND chi <= ?"); args.add(it) }

        // Cost
        appendSpecialOrRange(sb, args,
            column = "cost", specialColumn = "cost_special",
            min = criteria.costMin, max = criteria.costMax,
            includeSpecial = criteria.includeSpecialCost)

        // Honor requirement
        appendNegativeOrRange(sb, args,
            column = "honor", negativeColumn = "honor_req_negative",
            min = criteria.honorReqMin, max = criteria.honorReqMax,
            includeNegative = criteria.includeNegativeHonorReq)

        // Personal honor
        appendNegativeOrRange(sb, args,
            column = "personal_honor", negativeColumn = "personal_honor_negative",
            min = criteria.personalHonorMin, max = criteria.personalHonorMax,
            includeNegative = criteria.includeNegativePersonalHonor)

        // Focus
        criteria.focusMin?.let { sb.append(" AND focus >= ?"); args.add(it) }
        criteria.focusMax?.let { sb.append(" AND focus <= ?"); args.add(it) }

        // List filter
        criteria.listId?.let { lid ->
            sb.append(" AND id IN (SELECT card_id FROM deck_cards WHERE deck_id = ?)")
            args.add(lid)
        }

        // Banned
        if (!criteria.includeBanned) sb.append(" AND banned = 0")

        // Favorites
        if (criteria.onlyFavorites) {
            sb.append(" AND EXISTS (SELECT 1 FROM user_card_data WHERE card_id = cards.id AND type = 'favorite')")
        }

        // Hidden
        if (criteria.onlyHidden) {
            sb.append(" AND EXISTS (SELECT 1 FROM user_card_data WHERE card_id = cards.id AND type = 'hidden')")
        } else if (!criteria.includeHidden) {
            sb.append(" AND NOT EXISTS (SELECT 1 FROM user_card_data WHERE card_id = cards.id AND type = 'hidden')")
        }

        // Only initial holdings
        if (criteria.onlyInitialHoldings) sb.append(" AND initial_holding_slot IS NOT NULL")

        val dir = if (criteria.sortAscending) "ASC" else "DESC"
        if (criteria.sortField == SortField.NAME || criteria.sortField == SortField.SET) {
            // SET: orden base por nombre. El orden real por set (= orden del modal, presortSets)
            // se aplica en SearchViewModel.displayCards; el SQL no puede replicar presortSets.
            sb.append(" ORDER BY name $dir")
        } else {
            val col = criteria.sortField.column
            sb.append(" ORDER BY CASE WHEN $col IS NULL THEN 1 ELSE 0 END ASC, $col $dir, name ASC")
        }
        return SimpleSQLiteQuery(sb.toString(), args.toTypedArray())
    }

    /** Appends: AND (column_special=1 OR (column>=min AND column<=max)), only if any condition active. */
    private fun appendSpecialOrRange(
        sb: StringBuilder, args: MutableList<Any>,
        column: String, specialColumn: String,
        min: Int?, max: Int?, includeSpecial: Boolean
    ) {
        if (min == null && max == null && !includeSpecial) return
        sb.append(" AND (")
        val parts = mutableListOf<String>()
        if (includeSpecial) parts += "$specialColumn = 1"
        val numParts = mutableListOf<String>()
        min?.let { numParts += "$column >= ?"; args.add(it) }
        max?.let { numParts += "$column <= ?"; args.add(it) }
        if (numParts.isNotEmpty()) parts += "($column IS NOT NULL AND ${numParts.joinToString(" AND ")})"
        sb.append(parts.joinToString(" OR "))
        sb.append(")")
    }

    /** Appends: AND (negative_col=1 OR (column>=min AND column<=max)), only if any condition active. */
    private fun appendNegativeOrRange(
        sb: StringBuilder, args: MutableList<Any>,
        column: String, negativeColumn: String,
        min: Int?, max: Int?, includeNegative: Boolean
    ) {
        if (min == null && max == null && !includeNegative) return
        sb.append(" AND (")
        val parts = mutableListOf<String>()
        if (includeNegative) parts += "$negativeColumn = 1"
        val numParts = mutableListOf<String>()
        min?.let { numParts += "$column >= ?"; args.add(it) }
        max?.let { numParts += "$column <= ?"; args.add(it) }
        if (numParts.isNotEmpty()) parts += "($column IS NOT NULL AND ${numParts.joinToString(" AND ")})"
        sb.append(parts.joinToString(" OR "))
        sb.append(")")
    }

    private fun List<String>.placeholders() = joinToString(",") { "?" }
}
