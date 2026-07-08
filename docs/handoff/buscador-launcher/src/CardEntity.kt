package com.guillermoc.l5argentina.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

@Entity(
    tableName = "cards",
    indices = [Index(value = ["name"])]
)
data class CardEntity(
    @PrimaryKey val id: String,
    val name: String,
    val type: String,
    val clan: String?,
    @ColumnInfo(name = "deck_group") val deckGroup: String,
    val rarity: String,
    /** Stored as "|EE|ST|AU|" — see Converters */
    val sets: String,
    /** Stored as "|samurai|20f|" — see Converters */
    val legality: String,
    /** Stored as "|Unique|Weapon|" — see Converters */
    val keywords: String,
    val text: String?,
    val flavor: String?,
    val force: Int?,
    val chi: Int?,
    val cost: Int?,
    val honor: Int?,
    @ColumnInfo(name = "personal_honor") val personalHonor: Int? = null,
    val focus: Int?,
    @ColumnInfo(name = "gold_production") val goldProduction: Int?,
    @ColumnInfo(name = "province_strength") val provinceStrength: Int? = null,
    @ColumnInfo(name = "starting_honor") val startingHonor: Int? = null,
    val artist: String?,
    @ColumnInfo(name = "image_path") val imagePath: String?,
    @ColumnInfo(name = "max_copies") val maxCopies: Int = 3,
    // rules.json fields
    val banned: Boolean = false,
    val note: String? = null,
    @ColumnInfo(name = "label_text") val labelText: String? = null,
    @ColumnInfo(name = "label_color") val labelColor: String? = null,
    @ColumnInfo(name = "highlight_color") val highlightColor: String? = null,
    @ColumnInfo(name = "initial_holding_slot") val initialHoldingSlot: String? = null,
    // Valores especiales de stats (no parseables como Int)
    @ColumnInfo(name = "force_special") val forceSpecial: Boolean = false,
    @ColumnInfo(name = "cost_special") val costSpecial: Boolean = false,
    @ColumnInfo(name = "gold_production_special") val goldProductionSpecial: Boolean = false,
    @ColumnInfo(name = "honor_req_negative") val honorReqNegative: Boolean = false,
    @ColumnInfo(name = "personal_honor_negative") val personalHonorNegative: Boolean = false,
    // Keywords derivados en importación
    @ColumnInfo(name = "action_keywords") val actionKeywords: String = "",
    val subtitle: String? = null,
    @ColumnInfo(name = "printing_images") val printingImages: String = "",
)
