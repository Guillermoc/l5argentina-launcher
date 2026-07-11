package com.guillermoc.l5argentina.data.model

enum class DeckGroup(val key: String) {
    STARTER("starter"),
    DYNASTY("dynasty"),
    FATE("fate");

    companion object {
        fun fromKey(key: String): DeckGroup? =
            entries.firstOrNull { it.key.equals(key, ignoreCase = true) }
    }
}
