// STUB — reemplaza androidx.sqlite.db para compilar CardQueryCompiler fuera de Android.
// SimpleSQLiteQuery real guarda query + bindArgs; acá los exponemos para que el driver
// los ejecute vía JDBC.
package androidx.sqlite.db

interface SupportSQLiteQuery {
    val sql: String
    val argCount: Int
}

class SimpleSQLiteQuery(
    private val query: String,
    private val bindArgs: Array<out Any?>? = null
) : SupportSQLiteQuery {
    override val sql: String get() = query
    override val argCount: Int get() = bindArgs?.size ?: 0
    val args: Array<out Any?> get() = bindArgs ?: emptyArray()
}
