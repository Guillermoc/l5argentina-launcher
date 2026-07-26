// ui/screens/search/SetOrdering.kt
package com.guillermoc.l5argentina.ui.screens.search

import com.guillermoc.l5argentina.data.db.entity.SetEntity

// Sets de la era Loto que en orden de DB (rowid) aparecen después de "Samurai";
// el modal de filtros los fuerza antes. Fuente única de verdad del orden de sets.
val FORCE_BEFORE_SAMURAI = setOf("Filters", "Tomorrow", "TTT", "CotM", "KD")

// Orden canónico de sets = el que muestra el modal de filtros.
// Base = rowid de la DB; los sets de FORCE_BEFORE_SAMURAI mal ubicados se adelantan
// a justo antes de "Samurai".
fun presortSets(sets: List<SetEntity>): List<SetEntity> {
    val samuraiIdx = sets.indexOfFirst { it.code == "Samurai" }
    if (samuraiIdx < 0) return sets
    val fromSamurai = sets.drop(samuraiIdx)
    val misplaced = fromSamurai.filter { it.code in FORCE_BEFORE_SAMURAI }
    if (misplaced.isEmpty()) return sets
    return sets.take(samuraiIdx) + misplaced + fromSamurai.filter { it.code !in FORCE_BEFORE_SAMURAI }
}

// Mapa code → índice en el orden canónico del modal.
fun setOrderMap(sets: List<SetEntity>): Map<String, Int> =
    presortSets(sets).withIndex().associate { (i, s) -> s.code to i }
