// STUB — reemplaza androidx.room SOLO para compilar los fuentes reales fuera de Android.
// Retención RUNTIME en las anotaciones con SQL para que el driver pueda leer @Query por reflexión.
package androidx.room

import kotlin.reflect.KClass

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.CLASS)
annotation class Dao

object OnConflictStrategy {
    const val REPLACE = 1
    const val IGNORE = 5
    const val ABORT = 3
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
@Target(AnnotationTarget.FUNCTION)
annotation class Transaction

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.CLASS)
annotation class Entity(
    val tableName: String = "",
    val indices: Array<Index> = [],
    val primaryKeys: Array<String> = [],
    val foreignKeys: Array<ForeignKey> = []
)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.ANNOTATION_CLASS, AnnotationTarget.CLASS)
annotation class Index(val value: Array<String> = [], val unique: Boolean = false)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.ANNOTATION_CLASS, AnnotationTarget.CLASS)
annotation class ForeignKey(
    val entity: KClass<*>,
    val parentColumns: Array<String>,
    val childColumns: Array<String>,
    val onDelete: Int = 1,
    val onUpdate: Int = 1
) {
    companion object { const val CASCADE = 5; const val NO_ACTION = 1; const val SET_NULL = 4 }
}

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FIELD, AnnotationTarget.PROPERTY, AnnotationTarget.VALUE_PARAMETER)
annotation class PrimaryKey(val autoGenerate: Boolean = false)

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FIELD, AnnotationTarget.PROPERTY, AnnotationTarget.VALUE_PARAMETER)
annotation class ColumnInfo(val name: String = "", val defaultValue: String = "")

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FIELD, AnnotationTarget.PROPERTY)
annotation class Embedded(val prefix: String = "")

@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.FIELD, AnnotationTarget.PROPERTY)
annotation class Relation(val parentColumn: String, val entityColumn: String)

abstract class RoomDatabase

suspend fun <R> RoomDatabase.withTransaction(block: suspend () -> R): R = block()
