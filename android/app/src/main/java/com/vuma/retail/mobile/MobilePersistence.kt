package com.vuma.retail.mobile

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.RoomDatabase
import androidx.room.TypeConverter
import androidx.room.TypeConverters
import kotlinx.coroutines.flow.first
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

@Entity(tableName = "dashboard_cache", primaryKeys = ["tenantId", "companyId"])
data class DashboardCacheEntity(
    val tenantId: String,
    val companyId: String,
    val payloadJson: String,
    val syncedAtEpochMillis: Long,
)

@Entity(tableName = "pending_actions")
data class PendingActionEntity(
    @androidx.room.PrimaryKey val id: String,
    val tenantId: String,
    val userId: String,
    val companyId: String,
    val operation: String,
    val payloadJson: String,
    val state: String,
)

@Dao
interface MobileCacheDao {
    @Query("SELECT * FROM dashboard_cache WHERE tenantId = :tenantId AND companyId = :companyId")
    suspend fun dashboard(tenantId: String, companyId: String): DashboardCacheEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun saveDashboard(cache: DashboardCacheEntity)

    @Query("SELECT * FROM pending_actions WHERE tenantId = :tenantId AND userId = :userId AND companyId IN (:companyIds) AND state = 'Queued' ORDER BY rowid LIMIT 1")
    suspend fun nextAction(tenantId: String, userId: String, companyIds: List<String>): PendingActionEntity?

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insertAction(action: PendingActionEntity)

    @Query("UPDATE pending_actions SET state = :state WHERE id = :id AND state = :expectedState")
    suspend fun transition(id: String, expectedState: String, state: String): Int

    @Query("SELECT * FROM pending_actions WHERE id = :id")
    suspend fun action(id: String): PendingActionEntity?
}

class MobileConverters {
    @TypeConverter fun stateToString(state: PendingActionState): String = state.name
    @TypeConverter fun stringToState(value: String): PendingActionState = PendingActionState.valueOf(value)
}

@Database(entities = [DashboardCacheEntity::class, PendingActionEntity::class], version = 1, exportSchema = true)
@TypeConverters(MobileConverters::class)
abstract class MobileDatabase : RoomDatabase() {
    abstract fun cache(): MobileCacheDao
}

private val Context.mobileSessionDataStore by preferencesDataStore(name = "tenant_session")

/** Stores the refresh token encrypted with an Android Keystore AES key; DataStore holds ciphertext only. */
class TenantSessionStore(private val context: Context) {
    private val tokenKey = stringPreferencesKey("encrypted_refresh_token")

    suspend fun saveRefreshToken(token: String) {
        require(token.isNotBlank()) { "Refresh token is required" }
        context.mobileSessionDataStore.edit { it[tokenKey] = encrypt(token) }
    }

    suspend fun readRefreshToken(): String? = context.mobileSessionDataStore.data.first()[tokenKey]?.let(::decrypt)

    suspend fun clear() { context.mobileSessionDataStore.edit { it.remove(tokenKey) } }

    private fun encrypt(value: String): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val encrypted = cipher.iv + cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        return android.util.Base64.encodeToString(encrypted, android.util.Base64.NO_WRAP)
    }

    private fun decrypt(value: String): String {
        val bytes = android.util.Base64.decode(value, android.util.Base64.NO_WRAP)
        require(bytes.size > 12) { "Invalid encrypted session" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes, 0, 12))
        return cipher.doFinal(bytes, 12, bytes.size - 12).toString(Charsets.UTF_8)
    }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }

    private companion object { const val KEY_ALIAS = "vuma.mobile.refresh-token" }
}
