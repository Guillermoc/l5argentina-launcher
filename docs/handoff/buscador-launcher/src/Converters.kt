package com.guillermoc.l5argentina.data.db

import androidx.room.TypeConverter

/**
 * Stores List<String> as "|val1|val2|val3|" so LIKE '%|val|%' works reliably.
 */
class Converters {

    @TypeConverter
    fun fromList(list: List<String>): String {
        if (list.isEmpty()) return ""
        return "|${list.joinToString("|")}|"
    }

    @TypeConverter
    fun toList(value: String): List<String> {
        if (value.isBlank()) return emptyList()
        return value.trim('|').split("|").filter { it.isNotBlank() }
    }
}
