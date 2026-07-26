package com.guillermoc.l5argentina.data.repository

sealed class AddCardResult {
    data object Success : AddCardResult()
    data object Banned : AddCardResult()
    data object NoActiveDeck : AddCardResult()
    data class LimitReached(val max: Int) : AddCardResult()
    data object SlotAlreadyCovered : AddCardResult()
}
