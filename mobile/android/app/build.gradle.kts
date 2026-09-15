plugins {
    id("com.android.application")
    id("kotlin-android")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

android {
    namespace = "com.vuma.vuma_client"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }

    kotlinOptions {
        jvmTarget = JavaVersion.VERSION_11.toString()
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.vuma.vuma_client"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    buildTypes {
        release {
            // CI supplies the private release keystore. Never publish an artifact signed by the
            // debug key; a missing production key must fail the release build.
            val releaseStore = providers.gradleProperty("vumaReleaseStoreFile")
            val releasePassword = providers.gradleProperty("vumaReleaseStorePassword")
            val releaseAlias = providers.gradleProperty("vumaReleaseKeyAlias")
            val releaseKeyPassword = providers.gradleProperty("vumaReleaseKeyPassword")
            if (releaseStore.isPresent && releasePassword.isPresent && releaseAlias.isPresent && releaseKeyPassword.isPresent) {
                signingConfigs.create("vumaRelease") {
                    storeFile = file(releaseStore.get())
                    storePassword = releasePassword.get()
                    keyAlias = releaseAlias.get()
                    keyPassword = releaseKeyPassword.get()
                }
                signingConfig = signingConfigs.getByName("vumaRelease")
            } else if (gradle.startParameter.taskNames.any { it.contains("release", ignoreCase = true) }) {
                throw GradleException("Release signing is not configured. Refusing to use the debug key.")
            }
        }
    }
}

flutter {
    source = "../.."
}
