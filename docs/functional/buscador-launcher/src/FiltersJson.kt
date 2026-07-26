package com.guillermoc.l5argentina.data.remote

import kotlinx.serialization.Serializable

@Serializable
data class FiltersJson(
    val sets: List<SetData> = emptyList(),
    val formats: List<FormatData> = emptyList(),
    val keywords: List<KeywordData> = emptyList()
)

@Serializable
data class SetData(
    val code: String,
    val name: String? = null
)

@Serializable
data class FormatData(
    val code: String,
    val name: String? = null,
    val initialHoldingSlots: List<InitialHoldingSlotData> = emptyList()
)

@Serializable
data class KeywordData(
    val code: String,
    val name: String? = null
)

@Serializable
data class InitialHoldingSlotData(
    val id: String,
    val label: String,
    val required: Boolean = true,
    val eligibleCards: List<String> = emptyList()
)
