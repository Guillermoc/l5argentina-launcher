// STUB — CardImporter recibe AppDatabase solo para withTransaction {}; acá alcanza con
// una subclase vacía de RoomDatabase (la transacción real la maneja el driver por JDBC).
package com.guillermoc.l5argentina.data.db

import androidx.room.RoomDatabase

class AppDatabase : RoomDatabase()
