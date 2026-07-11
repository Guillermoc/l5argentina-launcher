package com.guillermoc.l5argentina.data.model

enum class DeckType(val key: String) {
    DECK("deck"),
    LIST("list");

    companion object {
        fun fromKey(key: String): DeckType =
            entries.firstOrNull { it.key.equals(key, ignoreCase = true) } ?: DECK
    }
}
