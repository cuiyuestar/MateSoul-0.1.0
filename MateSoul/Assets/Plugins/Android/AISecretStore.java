package com.matesoul.player;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Stores player credentials using a non-exportable Android Keystore AES key. */
public final class AISecretStore {
    private static final String ALIAS = "matesoul.player.ai.v1";
    private static final String STORE = "matesoul-ai";

    private static SecretKey key() throws Exception {
        KeyStore keys = KeyStore.getInstance("AndroidKeyStore");
        keys.load(null);
        if (keys.containsAlias(ALIAS)) return (SecretKey) keys.getKey(ALIAS, null);
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build());
        return generator.generateKey();
    }

    public static synchronized void save(Context context, String json) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        String iv = Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP);
        String encrypted = Base64.encodeToString(
                cipher.doFinal(json.getBytes(StandardCharsets.UTF_8)), Base64.NO_WRAP);
        if (!context.getSharedPreferences(STORE, Context.MODE_PRIVATE).edit()
                .putString("iv", iv).putString("data", encrypted).commit())
            throw new IllegalStateException("AI configuration could not be saved.");
    }

    public static synchronized String load(Context context) throws Exception {
        SharedPreferences prefs = context.getSharedPreferences(STORE, Context.MODE_PRIVATE);
        String data = prefs.getString("data", null);
        if (data == null) return "";
        String iv = prefs.getString("iv", null);
        if (iv == null) throw new IllegalStateException("Incomplete AI configuration.");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Base64.decode(iv, Base64.NO_WRAP)));
        return new String(cipher.doFinal(Base64.decode(data, Base64.NO_WRAP)), StandardCharsets.UTF_8);
    }
}
