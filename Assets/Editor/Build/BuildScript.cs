using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildScript
{
    public const string BuildFolder = "Builds/Android";

    // ── Keystore ───────────────────────────────────────────────────────────
    private const string KeystorePath = "/Users/addi/brokendreams/StackSurge/keystore/stacksurge.keystore";
    private const string KeyAlias     = "stacksurge";

    // Passwords are not kept in source. Each one comes from its environment
    // variable if set, otherwise from SigningFile in the project root, which is
    // git-ignored. Unity opened from the Hub or Dock does not see variables
    // exported in a shell profile, so the file is the simpler route for BuildWindow.
    private const string KeystorePassEnv = "STACKSURGE_KEYSTORE_PASS";
    private const string KeyPassEnv      = "STACKSURGE_KEY_PASS";
    private const string SigningFile     = "android-signing.json";

#pragma warning disable CS0649 // fields are filled by JsonUtility
    [Serializable]
    private class SigningFileData
    {
        public string keystorePass;
        public string keyPass;
    }
#pragma warning restore CS0649

    // ── Version bump enum ──────────────────────────────────────────────────
    [Flags]
    public enum VersionBump { None = 0, Major = 1, Minor = 2, Patch = 4 }

    // ── Core build ─────────────────────────────────────────────────────────

    public static void Build(bool aab, VersionBump bump)
    {
        string label = aab ? "AAB" : "APK";
        string ext   = aab ? ".aab" : ".apk";

        // ── Signing passwords (checked before the version is bumped) ───────
        string keystorePass = null, keyPass = null, passSource = null;
        if (aab && !TryLoadSigningPasswords(out keystorePass, out keyPass, out passSource))
        {
            Debug.LogError(MissingPasswordsMessage(label, keystorePass, keyPass));
            return;
        }

        // ── Bump semantic version ──────────────────────────────────────────
        string oldVersion = PlayerSettings.bundleVersion;
        string newVersion = BumpVersion(oldVersion, bump);
        PlayerSettings.bundleVersion = newVersion;
        Debug.Log($"[Build] [{label}] Version {oldVersion} → {newVersion} (flags: {bump})");

        // ── Increment bundle version code ──────────────────────────────────
        int newCode = PlayerSettings.Android.bundleVersionCode + 1;
        PlayerSettings.Android.bundleVersionCode = newCode;
        Debug.Log($"[Build] [{label}] Bundle version code → {newCode}");

        // ── Output filename ────────────────────────────────────────────────
        string fileName = $"StackSurge_{newVersion}{ext}";

        // ── Toggle AAB vs APK ──────────────────────────────────────────────
        EditorUserBuildSettings.buildAppBundle = aab;

        // ── Signing ────────────────────────────────────────────────────────
        if (aab)
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName      = KeystorePath;
            PlayerSettings.Android.keystorePass      = keystorePass;
            PlayerSettings.Android.keyaliasName      = KeyAlias;
            PlayerSettings.Android.keyaliasPass      = keyPass;
            Debug.Log($"[Build] [{label}] Signing with alias: {KeyAlias} (passwords from {passSource})");
        }
        else
        {
            PlayerSettings.Android.useCustomKeystore = false;
            Debug.Log($"[Build] [{label}] Using Unity debug keystore.");
        }

        // ── Collect scenes ─────────────────────────────────────────────────
        string[] scenes = GetEnabledScenes();
        Debug.Log($"[Build] [{label}] Scenes ({scenes.Length}): {string.Join(", ", scenes)}");

        // ── Output path ────────────────────────────────────────────────────
        string outputDir  = Path.GetFullPath(BuildFolder);
        Directory.CreateDirectory(outputDir);
        string outputPath = Path.Combine(outputDir, fileName);

        // ── Build options ──────────────────────────────────────────────────
        BuildOptions buildOptions = aab
            ? BuildOptions.None
            : BuildOptions.Development | BuildOptions.AllowDebugging;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = outputPath,
            target           = BuildTarget.Android,
            options          = buildOptions,
        };

        Debug.Log($"[Build] [{label}] Starting → {outputPath}");
        BuildPipeline.BuildPlayer(options);
        Debug.Log($"[Build] [{label}] Done → {outputPath}");
    }

    // ── Signing helpers ────────────────────────────────────────────────────

    private static string SigningFilePath() =>
        Path.Combine(Path.GetDirectoryName(Application.dataPath), SigningFile);

    /// <summary>
    /// Takes each password from its environment variable, falling back to SigningFile.
    /// Returns false if either is still missing. Never logs the values.
    /// </summary>
    private static bool TryLoadSigningPasswords(out string keystorePass, out string keyPass, out string source)
    {
        keystorePass = Environment.GetEnvironmentVariable(KeystorePassEnv);
        keyPass      = Environment.GetEnvironmentVariable(KeyPassEnv);
        bool usedEnv  = !string.IsNullOrEmpty(keystorePass) || !string.IsNullOrEmpty(keyPass);
        bool usedFile = false;

        if (string.IsNullOrEmpty(keystorePass) || string.IsNullOrEmpty(keyPass))
        {
            SigningFileData file = ReadSigningFile();
            if (file != null)
            {
                if (string.IsNullOrEmpty(keystorePass) && !string.IsNullOrEmpty(file.keystorePass))
                {
                    keystorePass = file.keystorePass;
                    usedFile = true;
                }
                if (string.IsNullOrEmpty(keyPass) && !string.IsNullOrEmpty(file.keyPass))
                {
                    keyPass = file.keyPass;
                    usedFile = true;
                }
            }
        }

        source = usedEnv && usedFile ? $"environment and {SigningFile}"
               : usedFile            ? SigningFile
               :                       "environment";
        return !string.IsNullOrEmpty(keystorePass) && !string.IsNullOrEmpty(keyPass);
    }

    private static SigningFileData ReadSigningFile()
    {
        string path = SigningFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            return JsonUtility.FromJson<SigningFileData>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogError($"[Build] Could not parse {path}: {e.Message}");
            return null;
        }
    }

    private static string MissingPasswordsMessage(string label, string keystorePass, string keyPass)
    {
        var missing = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(keystorePass)) missing.Add($"keystore password ({KeystorePassEnv})");
        if (string.IsNullOrEmpty(keyPass))      missing.Add($"key password ({KeyPassEnv})");

        return $"[Build] [{label}] Build cancelled, version left unchanged. Missing {string.Join(" and ", missing)}. " +
               $"Set the environment variable, or add the value to {SigningFilePath()} (git-ignored) in the form " +
               "{ \"keystorePass\": \"...\", \"keyPass\": \"...\" }. " +
               "Unity opened from the Hub or Dock does not see variables exported in a shell profile.";
    }

    // ── Version helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Applies all flagged bumps in Major → Minor → Patch order.
    /// Each bump resets lower components. Falls back to "0.0.1" on parse failure.
    /// </summary>
    public static string BumpVersion(string current, VersionBump bump)
    {
        int major = 0, minor = 0, patch = 0;

        string[] parts = (current ?? "").Split('.');
        if (parts.Length == 3)
        {
            int.TryParse(parts[0], out major);
            int.TryParse(parts[1], out minor);
            int.TryParse(parts[2], out patch);
        }

        if ((bump & VersionBump.Major) != 0) { major++; minor = 0; patch = 0; }
        if ((bump & VersionBump.Minor) != 0) { minor++;             patch = 0; }
        if ((bump & VersionBump.Patch) != 0) { patch++;                        }

        return $"{major}.{minor}.{patch}";
    }

    public static string[] GetEnabledScenes()
    {
        var scenes = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled)
                scenes.Add(scene.path);
        return scenes.ToArray();
    }
}