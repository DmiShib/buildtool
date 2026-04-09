using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SuperUnityBuild.BuildTool
{
    internal static class AndroidSigningValidator
    {
        internal static bool ValidateOrShowDialog(string[] buildConfigs)
        {
            if (buildConfigs == null || buildConfigs.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < buildConfigs.Length; i++)
            {
                string configKey = buildConfigs[i];
                BuildSettings.projectConfigurations.ParseKeychain(configKey, out BuildReleaseType releaseType, out BuildPlatform platform,
                    out BuildArchitecture architecture, out BuildScriptingBackend scriptingBackend, out BuildDistribution distribution);

                if (platform == null || platform.targetGroup != BuildTargetGroup.Android)
                {
                    continue;
                }

                if (!PlayerSettings.Android.useCustomKeystore)
                {
                    continue;
                }

                string keystoreName = PlayerSettings.Android.keystoreName;
                string keystorePass = PlayerSettings.Android.keystorePass;
                string keyaliasName = PlayerSettings.Android.keyaliasName;
                string keyaliasPass = PlayerSettings.Android.keyaliasPass;

                if (string.IsNullOrEmpty(keystoreName) ||
                    string.IsNullOrEmpty(keystorePass) ||
                    string.IsNullOrEmpty(keyaliasName) ||
                    string.IsNullOrEmpty(keyaliasPass))
                {
                    string message = "Unable to sign the application; please provide passwords!\n\n" +
                                     $"Keystore: {(string.IsNullOrEmpty(keystoreName) ? "<empty>" : keystoreName)}\n" +
                                     $"Keystore password: {(string.IsNullOrEmpty(keystorePass) ? "<empty>" : "<set>")}\n" +
                                     $"Key alias: {(string.IsNullOrEmpty(keyaliasName) ? "<empty>" : keyaliasName)}\n" +
                                     $"Key alias password: {(string.IsNullOrEmpty(keyaliasPass) ? "<empty>" : "<set>")}";

                    EditorUtility.DisplayDialog("Can not sign the application", message, "OK");
                    BuildNotificationList.instance.AddNotification(new BuildNotification(
                        BuildNotification.Category.Error,
                        "Can not sign the application",
                        message,
                        true,
                        null));
                    return false;
                }

                if (!TryValidateKeystorePasswords(keystoreName, keystorePass, keyaliasName, keyaliasPass, out string validationError))
                {
                    string message = "Unable to sign the application; keystore/alias password seems invalid.\n\n" + validationError;
                    EditorUtility.DisplayDialog("Can not sign the application", message, "OK");
                    BuildNotificationList.instance.AddNotification(new BuildNotification(
                        BuildNotification.Category.Error,
                        "Can not sign the application",
                        message,
                        true,
                        null));
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateKeystorePasswords(
            string keystorePath,
            string keystorePass,
            string alias,
            string aliasPass,
            out string error)
        {
            error = null;

            if (!File.Exists(keystorePath))
            {
                error = "Keystore file not found:\n" + keystorePath;
                return false;
            }

            string keytoolPath = GetKeytoolPath();
            if (string.IsNullOrEmpty(keytoolPath) || !File.Exists(keytoolPath))
            {
                return true;
            }

            string arguments = "-list" +
                               " -keystore " + QuoteArg(keystorePath) +
                               " -storepass " + QuoteArg(keystorePass) +
                               " -alias " + QuoteArg(alias) +
                               " -keypass " + QuoteArg(aliasPass);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = keytoolPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var p = new Process { StartInfo = psi };
                p.Start();
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    return true;
                }

                string combined = (stdout + "\n" + stderr).Trim();
                if (string.IsNullOrEmpty(combined))
                {
                    combined = "keytool returned non-zero exit code.";
                }

                error = "Keystore: " + keystorePath + "\n" +
                        "Alias: " + alias + "\n\n" +
                        combined;
                return false;
            }
            catch (Exception e)
            {
                error = "Password validation failed to execute keytool: " + e.Message;
                return false;
            }
        }

        private static string GetKeytoolPath()
        {
            string jdkPath = null;

#if UNITY_2019_3_OR_NEWER && UNITY_ANDROID
            try
            {
                jdkPath = UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath;
            }
            catch
            {
                jdkPath = null;
            }
#endif

            if (!string.IsNullOrEmpty(jdkPath) && Directory.Exists(jdkPath))
            {
                return Path.Combine(jdkPath, Path.Combine("bin", GetToolNameWithExtension("keytool")));
            }

            string preferencesJdkPath = EditorPrefs.GetString("JdkPath");
            if (!string.IsNullOrEmpty(preferencesJdkPath) && Directory.Exists(preferencesJdkPath))
            {
                return Path.Combine(preferencesJdkPath, Path.Combine("bin", GetToolNameWithExtension("keytool")));
            }

            string environmentJdkPath = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(environmentJdkPath) && Directory.Exists(environmentJdkPath))
            {
                return Path.Combine(environmentJdkPath, Path.Combine("bin", GetToolNameWithExtension("keytool")));
            }

            return null;
        }

        private static string QuoteArg(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\\\"", "\\\\\"") + "\"";
        }

        private static string GetToolNameWithExtension(string toolName)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return toolName + ".exe";
            }

            return toolName;
        }
    }
}
