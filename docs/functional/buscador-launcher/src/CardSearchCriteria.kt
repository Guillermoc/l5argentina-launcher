package com.guillermoc.l5argentina.data.model

import kotlinx.serialization.Serializable

@Serializable
enum class SortField(val column: String) {
    NAME("name"),
    COST("cost"),
    FORCE("force"),
    CHI("chi"),
    PERSONAL_HONOR("personal_honor"),
    SET("sets")
}

@Serializable
data class CardSearchCriteria(
    val name: String = "",
    // Keywords (reemplaza keywordSearch)
    val keywords: List<String> = emptyList(),
    val negativeKeywords: List<String> = emptyList(),
    val keywordMatchAll: Boolean = true,
    val includeActionKeywords: Boolean = true,
    val includeSubtitleKeywords: Boolean = true,
    val text: String = "",
    // Categóricos
    val types: List<String> = emptyList(),
    val clans: List<String> = emptyList(),
    val rarities: List<String> = emptyList(),
    val sets: List<String> = emptyList(),
    val formats: List<String> = emptyList(),
    // Stats — Force
    val forceMin: Int? = null,
    val forceMax: Int? = null,
    val includeSpecialForce: Boolean = false,
    // Stats — Chi
    val chiMin: Int? = null,
    val chiMax: Int? = null,
    // Stats — Cost
    val costMin: Int? = null,
    val costMax: Int? = null,
    val includeSpecialCost: Boolean = false,
    // Stats — Honor Requirement
    val honorReqMin: Int? = null,
    val honorReqMax: Int? = null,
    val includeNegativeHonorReq: Boolean = false,
    // Stats — Personal Honor
    val personalHonorMin: Int? = null,
    val personalHonorMax: Int? = null,
    val includeNegativePersonalHonor: Boolean = false,
    // Stats — Focus
    val focusMin: Int? = null,
    val focusMax: Int? = null,
    // Designators (action_keywords: Open, Limited, Battle, etc.)
    val designators: List<String> = emptyList(),
    // Otros
    val includeBanned: Boolean = false,
    val onlyInitialHoldings: Boolean = false,
    val onlyHidden: Boolean = false,
    // Quick filters (fuera del modal)
    val onlyFavorites: Boolean = false,
    val includeHidden: Boolean = false,
    val listId: String? = null,
    // Sort
    val sortField: SortField = SortField.NAME,
    val sortAscending: Boolean = true,
) {
    // isEmpty = true sólo cuando todo es default (includeBanned=false es el default)
    val isEmpty: Boolean get() =
        name.isBlank() && keywords.isEmpty() && negativeKeywords.isEmpty() && text.isBlank() &&
        types.isEmpty() && clans.isEmpty() && rarities.isEmpty() &&
        sets.isEmpty() && formats.isEmpty() &&
        forceMin == null && forceMax == null && !includeSpecialForce &&
        chiMin == null && chiMax == null &&
        costMin == null && costMax == null && !includeSpecialCost &&
        honorReqMin == null && honorReqMax == null && !includeNegativeHonorReq &&
        personalHonorMin == null && personalHonorMax == null && !includeNegativePersonalHonor &&
        focusMin == null && focusMax == null &&
        !onlyInitialHoldings && !onlyHidden && !onlyFavorites && !includeHidden &&
        listId == null && !includeBanned && designators.isEmpty()
}
