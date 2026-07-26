package com.guillermoc.l5argentina.data.model

enum class Clan(val key: String) {
    CRAB("crab"),
    CRANE("crane"),
    DRAGON("dragon"),
    LION("lion"),
    MANTIS("mantis"),
    PHOENIX("phoenix"),
    SCORPION("scorpion"),
    SPIDER("spider"),
    UNICORN("unicorn"),
    NO_CLAN("noclan");

    companion object {
        fun fromKey(key: String): Clan? =
            entries.firstOrNull { it.key.equals(key, ignoreCase = true) }
    }
}
