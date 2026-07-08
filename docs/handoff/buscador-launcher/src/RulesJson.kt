package com.guillermoc.l5argentina.data.remote

import kotlinx.serialization.Serializable

/**
 * Format per annexo rules v1.0.
 * Unknown fields in CardRule are silently ignored via @Serializable + ignoreUnknownKeys.
 */
@Serializable
data class RulesJson(
    val rules: List<CardRule> = emptyList()
)

@Serializable
data class CardRule(
    val title: String,
    /** Override max copies. 999 = unlimited, 0 = banned. */
    val maxCopies: Int? = null,
    /** Set code to use for image instead of most recent. */
    val preferredPrinting: String? = null,
    /** If true, card cannot be added to any deck. */
    val banned: Boolean? = null,
    /** Informational text shown below image in card viewer. */
    val note: String? = null,
    /** Short text drawn over the card image. */
    val label: LabelRule? = null,
    /** Colored border drawn over the card image. */
    val highlight: HighlightRule? = null
)

@Serializable
data class LabelRule(
    val text: String,
    val color: String = "#FFFFFF"
)

@Serializable
data class HighlightRule(
    val color: String
)
