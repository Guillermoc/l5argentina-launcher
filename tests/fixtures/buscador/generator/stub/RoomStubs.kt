// STUB — reemplaza androidx.room SOLO para compilar los fuentes reales fuera de Android.
// Las anotaciones tienen retención RUNTIME para que el driver pueda leer el SQL de @Query
// directamente de la interfaz real (CardDao) sin transcribirlo a mano.
package androidx.room

import kotlin.reflect.KClass

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.CLASS)
annotation class Dao

object OnConflictStrategy {
    const val REPLACE = 1
    const val IGNORE = 5
}

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FUNCTION)
annotation class Insert(val onConflict: Int = 3)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FUNCTION)
annotation class Query(val value: String)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FUNCTION)
annotation class RawQuery(val observedEntities: Array<KClass<*>> = [])

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.CLASS)
annotation class Entity(
    val tableName: String = "",
    val indices: Array<Index> = [],
    val primaryKeys: Array<String> = []
)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.ANNOTATION_CLASS, AnnotationTarget.CLASS)
annotation class Index(val value: Array<String> = [], val unique: Boolean = false)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FIELD, AnnotationTarget.PROPERTY, AnnotationTarget.VALUE_PARAMETER)
annotation class PrimaryKey(val autoGenerate: Boolean = false)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FIELD, AnnotationTarget.PROPERTY, AnnotationTarget.VALUE_PARAMETER)
annotation class ColumnInfo(val name: String)

abstract class RoomDatabase

// En Room real esto abre una transacción SQLite; acá el driver la maneja por JDBC.
suspend fun <R> RoomDatabase.withTransaction(block: suspend () -> R): R = block()
