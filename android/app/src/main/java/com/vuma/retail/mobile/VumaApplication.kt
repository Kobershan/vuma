package com.vuma.retail.mobile

import android.app.Application
import androidx.room.Room

/** Process-scoped composition root for durable mobile state. */
class VumaApplication : Application() {
    val database: MobileDatabase by lazy {
        Room.databaseBuilder(this, MobileDatabase::class.java, "vuma-mobile.db")
            .fallbackToDestructiveMigrationOnDowngrade()
            .build()
    }

    val sessionStore: TenantSessionStore by lazy { TenantSessionStore(this) }
}
