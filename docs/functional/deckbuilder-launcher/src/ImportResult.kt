package com.guillermoc.l5argentina.data.repository

data class ImportResult(
    val imported: Int,
    val notFound: Int,
    val notFoundNames: List<String> = emptyList()
)
