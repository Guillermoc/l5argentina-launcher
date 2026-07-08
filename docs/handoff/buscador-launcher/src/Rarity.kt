package com.guillermoc.l5argentina.data.model

enum class Rarity(val key: String) {
    P("P"),
    R("R"),
    U("U"),
    C("C"),
    F("F");

    companion object {
        fun fromKey(key: String): Rarity? =
            entries.firstOrNull { it.key.equals(key, ignoreCase = true) }
    }
}
