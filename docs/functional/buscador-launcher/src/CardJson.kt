package com.guillermoc.l5argentina.data.remote

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class PrintingJson(
    val id: String,
    val edition: String,
    val image: String? = null,
    val legalities: List<String> = emptyList(),
    val current: Boolean = false,
)

@Serializable
data class CardJson(
    val id: String,
    val name: String,
    val type: String? = null,
    val clan: String? = null,
    val rarity: String? = null,
    val edition: String? = null,
    val image: String? = null,
    val legal: String? = null,
    val text: String? = null,
    val flavor: String? = null,
    val force: String? = null,
    val chi: String? = null,
    val cost: String? = null,
    val focus: String? = null,
    @SerialName("personal_honor") val personalHonor: String? = null,
    @SerialName("honor_req") val honorReq: String? = null,
    val artist: String? = null,
    val keywords: List<String>? = null,
    @SerialName("gold_production") val goldProduction: String? = null,
    @SerialName("province_strength") val provinceStrength: String? = null,
    @SerialName("starting_honor") val startingHonor: String? = null,
    val editions: List<String> = emptyList(),
    val legalities: List<String> = emptyList(),
    val printings: List<PrintingJson> = emptyList(),
)
