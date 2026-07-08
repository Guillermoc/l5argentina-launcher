package com.guillermoc.l5argentina.data.model

enum class CardType(val key: String) {
    PERSONALITY("personality"),
    HOLDING("holding"),
    EVENT("event"),
    STRATEGY("strategy"),
    ITEM("item"),
    SPELL("spell"),
    FOLLOWER("follower"),
    RING("ring"),
    ANCESTOR("ancestor"),
    STRONGHOLD("stronghold"),
    SENSEI("sensei"),
    WIND("wind"),
    REGION("region"),
    CELESTIAL("celestial");

    companion object {
        fun fromKey(key: String): CardType? =
            entries.firstOrNull { it.key.equals(key, ignoreCase = true) }
    }
}
