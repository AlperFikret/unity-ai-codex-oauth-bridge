using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityAiCodexOAuthBridge.Editor
{
    internal static class UnityAiCodexOAuthBridge
    {
        const string LogPrefix = "[Unity AI Codex OAuth Bridge]";
        const string MenuRoot = "Tools/Unity AI Codex OAuth/";
        const string AssistantPackageName = "com.unity.ai.assistant";
        const string SupportedAssistantVersion = "2.7.0-pre.1";
        const string RelayExecutableName = "relay_win.exe";
        const string BackupExtension = ".codex-oauth-backup";
        const string ProviderId = "codex";
        const string PatchedHelpText = "Uses Codex ChatGPT OAuth";
        const string GatewayPrefsKey = "Unity.AI.Gateway.Relay.envVars.preferences";
        const string DefaultCodexModelId = "gpt-5.5";
        const string SelectedModelPrefsPrefix = "AIAssistant.SelectedModel.";
        const string ProviderEnabledPrefsPrefix = "AIAssistant.ProviderEnabled.";
        const string GatewayDisclaimerAcceptedPrefsPrefix = "AIAssistant.AiGatewayDisclaimerAccepted.";
        const string LastActiveProviderSessionKey = "AssistantUserSession_LastActiveProviderId";
        const string LastActiveModeSessionKey = "AssistantUserSession_LastActiveMode";
        const string BridgePrefsPrefix = "UnityAiCodexOAuthBridge.";
        const string AuthStatusPrefsKey = BridgePrefsPrefix + "AuthStatus";
        const string AuthStatusCheckedUtcPrefsKey = BridgePrefsPrefix + "AuthStatusCheckedUtc";
        const string CodexExecutablePrefsKey = BridgePrefsPrefix + "CodexExecutable";
        const string CodexModelPrefsKey = BridgePrefsPrefix + "CodexModel";
        const string RelayPathPrefsKey = BridgePrefsPrefix + "RelayPath";
        const string RelayPatchedUtcPrefsKey = BridgePrefsPrefix + "RelayPatchedUtc";

        // Unity AI Assistant 2.7.0-pre.1 bundled relay Codex provider literal.
        const string OriginalCodexProviderLiteral =
            "nk={id:\"codex\",displayName:\"Codex\",envVarNames:[\"OPENAI_API_KEY\"],keychainEnvVar:[\"OPENAI_API_KEY\"],importEnv:[\"OPENAI_*\"],helpText:\"Requires OPENAI_API_KEY\",startupTroubleshootingHint:\"Ensure OPENAI_API_KEY environment variable is set in the <link=open-gateway-preferences><color=#7BAEFA>Gateway preferences</color></link>.\",isCustom:!0,agentsMdFilename:\"AGENTS.md\",postInstall:{message:`To use Codex, you need to add an API key. Follow these steps:\n- Create one by visiting <a href=\"https://platform.openai.com/api-keys\">OpenAI's settings</a>\n- Paste your API key below and hit enter to start using the agent`,envVarName:\"OPENAI_API_KEY\"},start:Od,onBeforeCredentialStore:(v)=>{if(v===\"OPENAI_API_KEY\")$z4()}}});";

        const string PatchedCodexProviderLiteral =
            "nk={id:\"codex\",displayName:\"Codex\",envVarNames:[],keychainEnvVar:[],importEnv:[],helpText:\"Uses Codex ChatGPT OAuth\",startupTroubleshootingHint:\"Run Setup Codex OAuth from the Unity Tools menu.\",isCustom:!0,agentsMdFilename:\"AGENTS.md\",postInstall:null,start:Od,onBeforeCredentialStore:void 0}});";

        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static double s_LoginPollStartedAt;
        static double s_NextLoginPollAt;
        static int s_ActivationReapplyCount;
        static double s_NextActivationReapplyAt;

        enum RelayPatchState
        {
            Missing,
            SupportedUnpatched,
            Patched,
            Unsupported
        }

        [MenuItem(MenuRoot + "Setup Codex OAuth")]
        static async void SetupCodexOAuthMenu()
        {
            var report = await SetupCodexOAuth();
            ShowReport("Unity AI Codex OAuth Setup", report);
        }

        [MenuItem(MenuRoot + "Diagnostics")]
        static void DiagnosticsMenu()
        {
            ShowReport("Unity AI Codex OAuth Diagnostics", BuildDiagnosticsReport());
        }

        [MenuItem(MenuRoot + "Restore Original Unity Relay")]
        static async void RestoreOriginalRelayMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Restore Original Unity Relay",
                    "This will stop Unity AI Relay, restore relay_win.exe from the bridge backup, clear only bridge-owned cache keys, and restart the relay. Codex OAuth login data will not be deleted.",
                    "Restore",
                    "Cancel"))
                return;

            var report = await RestoreOriginalRelay();
            ShowReport("Unity AI Relay Restore", report);
        }

        [MenuItem(MenuRoot + "Advanced/Restart Unity AI Relay")]
        static async void RestartUnityAiRelayMenu()
        {
            var report = new StringBuilder();
            await RestartUnityAiRelay(report);
            ShowReport("Unity AI Relay Restart", report.ToString());
        }

        static async Task<string> SetupCodexOAuth()
        {
            var report = new StringBuilder();
            report.AppendLine("Setup started.");

            if (!IsWindowsEditor())
            {
                report.AppendLine("ERROR: v0.1 supports Windows Editor only.");
                return report.ToString();
            }

            var packageInfo = ResolveAssistantPackage(report);
            if (packageInfo == null)
                return report.ToString();

            if (!string.Equals(packageInfo.version, SupportedAssistantVersion, StringComparison.Ordinal))
                report.AppendLine($"WARNING: Tested target is {SupportedAssistantVersion}; installed version is {packageInfo.version}. The binary signature check will still prevent unsupported patches.");

            var relayPath = ResolveRelayPath(packageInfo);
            report.AppendLine($"Relay: {relayPath}");

            var patchState = GetRelayPatchState(relayPath);
            report.AppendLine($"Relay patch state: {patchState}");
            if (patchState == RelayPatchState.Missing)
            {
                report.AppendLine("ERROR: relay_win.exe was not found.");
                return report.ToString();
            }

            if (patchState == RelayPatchState.Unsupported)
            {
                report.AppendLine("ERROR: This relay binary does not match the supported v0.1 patch signature. No changes were made.");
                return report.ToString();
            }

            if (patchState == RelayPatchState.SupportedUnpatched)
            {
                var confirm = EditorUtility.DisplayDialog(
                    "Patch Unity AI Relay",
                    "The bridge will patch your local Unity AI Assistant relay binary after creating a backup next to it. No Unity, Codex, OAuth, or API key data is bundled or uploaded.",
                    "Patch",
                    "Cancel");
                if (!confirm)
                {
                    report.AppendLine("Canceled before patching.");
                    return report.ToString();
                }
            }

            await StopUnityAiRelay(report);

            if (!PatchRelayBinary(relayPath, report))
            {
                await StartUnityAiRelay(report);
                return report.ToString();
            }

            PatchGatewayPreferences(report);
            RefreshGatewayPreferences(report);
            ActivateCodexInAssistant(report);

            await StartUnityAiRelay(report);

            var codex = FindCodexExecutable();
            if (string.IsNullOrEmpty(codex))
            {
                report.AppendLine("ERROR: Codex CLI was not found. Install or open Codex, then run setup again.");
                return report.ToString();
            }

            report.AppendLine($"Codex CLI: {codex}");
            var loggedIn = IsCodexLoggedIn(codex);
            UpdateCachedAuthStatus(loggedIn, codex, relayPath);

            if (loggedIn)
            {
                report.AppendLine("Codex CLI login: logged in.");
                SyncAuthAndEnableCodex(showPreferences: false, report: report);
            }
            else
            {
                report.AppendLine("Codex CLI login: not logged in. Opening codex login.");
                StartCodexBrowserLogin(codex);
                StartLoginStatusPolling();
            }

            SettingsService.OpenUserPreferences("Preferences/AI/Gateway");
            report.AppendLine("Setup finished.");
            return report.ToString();
        }

        static async Task<string> RestoreOriginalRelay()
        {
            var report = new StringBuilder();

            var packageInfo = ResolveAssistantPackage(report);
            if (packageInfo == null)
                return report.ToString();

            var relayPath = ResolveRelayPath(packageInfo);
            var backupPath = relayPath + BackupExtension;
            report.AppendLine($"Relay: {relayPath}");
            report.AppendLine($"Backup: {backupPath}");

            if (!File.Exists(backupPath))
            {
                report.AppendLine("ERROR: Backup file was not found. Nothing was restored.");
                return report.ToString();
            }

            await StopUnityAiRelay(report);

            try
            {
                File.Copy(backupPath, relayPath, overwrite: true);
                report.AppendLine("Original relay restored from backup.");
            }
            catch (Exception ex)
            {
                report.AppendLine($"ERROR: Failed to restore relay: {ex.Message}");
            }

            ClearBridgeEditorPrefs();
            RefreshGatewayPreferences(report);
            await StartUnityAiRelay(report);
            report.AppendLine("Restore finished.");
            return report.ToString();
        }

        static string BuildDiagnosticsReport()
        {
            var report = new StringBuilder();
            report.AppendLine("Unity AI Codex OAuth Bridge diagnostics");
            report.AppendLine($"OS support: {(IsWindowsEditor() ? "Windows Editor" : "Unsupported platform")}");

            var packageInfo = ResolveAssistantPackage(report);
            if (packageInfo != null)
            {
                report.AppendLine($"Unity AI Assistant: {packageInfo.version}");
                report.AppendLine($"Unity AI Assistant path: {packageInfo.resolvedPath}");

                var relayPath = ResolveRelayPath(packageInfo);
                report.AppendLine($"Relay path: {relayPath}");
                report.AppendLine($"Relay patch state: {GetRelayPatchState(relayPath)}");
                report.AppendLine($"Relay backup exists: {File.Exists(relayPath + BackupExtension)}");
            }

            var codex = FindCodexExecutable();
            report.AppendLine($"Codex CLI path: {(string.IsNullOrEmpty(codex) ? "(not found)" : codex)}");
            report.AppendLine($"Codex CLI login: {(!string.IsNullOrEmpty(codex) && IsCodexLoggedIn(codex) ? "logged in" : "not verified")}");
            report.AppendLine($"Gateway prefs: {GetGatewayPreferencesStatus()}");
            report.AppendLine($"Relay service: {GetRelayServiceStatus()}");
            return report.ToString();
        }

        static bool PatchRelayBinary(string relayPath, StringBuilder report)
        {
            var state = GetRelayPatchState(relayPath);
            if (state == RelayPatchState.Patched)
            {
                report.AppendLine("Relay is already patched.");
                return true;
            }

            if (state != RelayPatchState.SupportedUnpatched)
            {
                report.AppendLine($"ERROR: Relay cannot be patched from state {state}.");
                return false;
            }

            try
            {
                var backupPath = relayPath + BackupExtension;
                if (!File.Exists(backupPath))
                {
                    File.Copy(relayPath, backupPath);
                    report.AppendLine($"Backup created: {backupPath}");
                }
                else
                {
                    report.AppendLine($"Backup already exists: {backupPath}");
                }

                var originalBytes = Utf8.GetBytes(OriginalCodexProviderLiteral);
                var patchedBytes = Utf8.GetBytes(PatchedCodexProviderLiteral);
                if (patchedBytes.Length > originalBytes.Length)
                {
                    report.AppendLine("ERROR: Internal patch literal is longer than original literal.");
                    return false;
                }

                var relayBytes = File.ReadAllBytes(relayPath);
                var index = IndexOf(relayBytes, originalBytes);
                if (index < 0)
                {
                    report.AppendLine("ERROR: Supported Codex provider literal was not found.");
                    return false;
                }

                var paddedPatch = new byte[originalBytes.Length];
                for (var i = 0; i < paddedPatch.Length; i++)
                    paddedPatch[i] = 0x20;
                Array.Copy(patchedBytes, paddedPatch, patchedBytes.Length);
                Array.Copy(paddedPatch, 0, relayBytes, index, paddedPatch.Length);
                File.WriteAllBytes(relayPath, relayBytes);

                EditorPrefs.SetString(RelayPathPrefsKey, relayPath);
                EditorPrefs.SetString(RelayPatchedUtcPrefsKey, DateTime.UtcNow.ToString("O"));
                report.AppendLine("Relay binary patched successfully.");
                return true;
            }
            catch (Exception ex)
            {
                report.AppendLine($"ERROR: Failed to patch relay binary: {ex.Message}");
                return false;
            }
        }

        static RelayPatchState GetRelayPatchState(string relayPath)
        {
            if (string.IsNullOrEmpty(relayPath) || !File.Exists(relayPath))
                return RelayPatchState.Missing;

            try
            {
                var bytes = File.ReadAllBytes(relayPath);
                if (IndexOf(bytes, Utf8.GetBytes(PatchedCodexProviderLiteral)) >= 0 ||
                    IndexOf(bytes, Utf8.GetBytes("envVarNames:[],keychainEnvVar:[],importEnv:[],helpText:\"Uses Codex ChatGPT OAuth\"")) >= 0)
                    return RelayPatchState.Patched;

                if (IndexOf(bytes, Utf8.GetBytes(OriginalCodexProviderLiteral)) >= 0)
                    return RelayPatchState.SupportedUnpatched;

                return RelayPatchState.Unsupported;
            }
            catch
            {
                return RelayPatchState.Unsupported;
            }
        }

        static void PatchGatewayPreferences(StringBuilder report)
        {
            var rawPrefs = EditorPrefs.GetString(GatewayPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(rawPrefs))
            {
                report.AppendLine("Gateway prefs: not initialized yet. They will be populated by Unity AI Relay after restart.");
                return;
            }

            try
            {
                var prefs = JObject.Parse(rawPrefs);
                var providers = prefs["ProviderInfoList"] as JArray;
                var codex = FindProvider(providers, ProviderId);
                if (codex == null)
                {
                    report.AppendLine("Gateway prefs: Codex provider not found.");
                    return;
                }

                codex["HelpText"] = PatchedHelpText;
                codex["Variables"] = new JArray();
                codex["RequiredEnvVarNames"] = new JArray();
                EditorPrefs.SetString(GatewayPrefsKey, prefs.ToString(Formatting.None));
                PatchLiveGatewayPreferences(report);
                report.AppendLine("Gateway prefs patched.");
            }
            catch (JsonException ex)
            {
                report.AppendLine($"Gateway prefs: JSON parse failed: {ex.Message}");
            }
        }

        static void ActivateCodexInAssistant(StringBuilder report)
        {
            var modelId = GetPreferredCodexModelId();
            PatchLiveGatewayPreferences(report);
            WriteAssistantStatePrefs(modelId);
            NotifyAssistantPreferences(modelId, report);
            ScheduleActivationReapply();
            report.AppendLine($"Codex provider enabled. Selected model: {modelId}.");
        }

        static bool SyncAuthAndEnableCodex(bool showPreferences, StringBuilder report)
        {
            var codex = FindCodexExecutable();
            var loggedIn = !string.IsNullOrEmpty(codex) && IsCodexLoggedIn(codex);
            UpdateCachedAuthStatus(loggedIn, codex, EditorPrefs.GetString(RelayPathPrefsKey, string.Empty));
            PatchGatewayPreferences(report);
            ActivateCodexInAssistant(report);

            if (showPreferences)
                SettingsService.OpenUserPreferences("Preferences/AI/Gateway");

            return loggedIn;
        }

        static void WriteAssistantStatePrefs(string modelId)
        {
            EditorPrefs.SetString(CodexModelPrefsKey, modelId);
            EditorPrefs.SetString(SelectedModelPrefsPrefix + ProviderId, modelId);
            SessionState.SetString(LastActiveProviderSessionKey, ProviderId);
            SessionState.SetString(LastActiveModeSessionKey, "Agent");

            var userId = CloudProjectSettings.userId;
            if (!string.IsNullOrEmpty(userId))
            {
                EditorPrefs.SetBool(GatewayDisclaimerAcceptedPrefsPrefix + userId, true);
                EditorPrefs.SetBool($"{ProviderEnabledPrefsPrefix}{userId}.{ProviderId}", true);
            }
        }

        static void NotifyAssistantPreferences(string modelId, StringBuilder report)
        {
            InvokeAssistantPreferenceMethod("SetAiGatewayDisclaimerAccepted", report, true);
            InvokeAssistantPreferenceMethod("SetProviderEnabled", report, ProviderId, true);
            InvokeAssistantPreferenceMethod("SetSelectedModel", report, ProviderId, modelId);
        }

        static void ScheduleActivationReapply()
        {
            s_ActivationReapplyCount = 12;
            s_NextActivationReapplyAt = 0;
            EditorApplication.update -= ReapplyCodexActivation;
            EditorApplication.update += ReapplyCodexActivation;
        }

        static void ReapplyCodexActivation()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < s_NextActivationReapplyAt)
                return;

            s_NextActivationReapplyAt = now + 0.5;
            PatchLiveGatewayPreferences(null);
            WriteAssistantStatePrefs(GetPreferredCodexModelId());

            s_ActivationReapplyCount--;
            if (s_ActivationReapplyCount <= 0)
                EditorApplication.update -= ReapplyCodexActivation;
        }

        static void PatchLiveGatewayPreferences(StringBuilder report)
        {
            try
            {
                var serviceType = Type.GetType("Unity.AI.Assistant.Editor.Settings.GatewayPreferenceService, Unity.AI.Assistant.Editor");
                var instance = serviceType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var preferences = serviceType?.GetProperty("Preferences", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                var value = preferences?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(preferences);
                var providers = value?.GetType().GetProperty("ProviderInfoList", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value) as System.Collections.IEnumerable;
                if (providers == null)
                    return;

                foreach (var provider in providers)
                {
                    var providerType = provider?.GetType().GetProperty("ProviderType", BindingFlags.Public | BindingFlags.Instance)?.GetValue(provider)?.ToString();
                    if (!string.Equals(providerType, ProviderId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    SetProperty(provider, "HelpText", PatchedHelpText);
                    SetProperty(provider, "IsCustom", true);
                    SetProperty(provider, "Variables", CreateEmptyListForProperty(provider, "Variables"));
                    SetProperty(provider, "RequiredEnvVarNames", CreateEmptyListForProperty(provider, "RequiredEnvVarNames"));
                }
            }
            catch (Exception ex)
            {
                report?.AppendLine($"Live Gateway prefs patch warning: {ex.Message}");
            }
        }

        static async Task StopUnityAiRelay(StringBuilder report)
        {
            try
            {
                var relayType = Type.GetType("Unity.Relay.Editor.RelayService, Unity.AI.Assistant.Editor");
                var instance = relayType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var stop = relayType?.GetMethod("StopAsync", BindingFlags.Public | BindingFlags.Instance);
                if (instance == null || stop == null)
                {
                    report.AppendLine("Relay stop: RelayService not available.");
                    return;
                }

                if (stop.Invoke(instance, null) is Task task)
                    await task;

                report.AppendLine("Relay stopped.");
            }
            catch (Exception ex)
            {
                report.AppendLine($"Relay stop warning: {ex.Message}");
            }
        }

        static async Task StartUnityAiRelay(StringBuilder report)
        {
            try
            {
                var relayType = Type.GetType("Unity.Relay.Editor.RelayService, Unity.AI.Assistant.Editor");
                var instance = relayType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var start = relayType?.GetMethod("StartAsync", BindingFlags.Public | BindingFlags.Instance);
                if (instance == null || start == null)
                {
                    report.AppendLine("Relay start: RelayService not available.");
                    return;
                }

                if (start.Invoke(instance, null) is Task task)
                    await task;

                report.AppendLine($"Relay started. {GetRelayServiceStatus()}");
            }
            catch (Exception ex)
            {
                report.AppendLine($"Relay start warning: {ex.Message}");
            }
        }

        static async Task RestartUnityAiRelay(StringBuilder report)
        {
            await StopUnityAiRelay(report);
            await StartUnityAiRelay(report);
        }

        static void RefreshGatewayPreferences(StringBuilder report)
        {
            try
            {
                var serviceType = Type.GetType("Unity.AI.Assistant.Editor.Settings.GatewayPreferenceService, Unity.AI.Assistant.Editor");
                var instance = serviceType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var preferences = serviceType?.GetProperty("Preferences", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                preferences?.GetType().GetMethod("Refresh", BindingFlags.Public | BindingFlags.Instance)?.Invoke(preferences, null);
                report?.AppendLine("Gateway prefs refresh requested.");
            }
            catch (Exception ex)
            {
                report?.AppendLine($"Gateway prefs refresh warning: {ex.Message}");
            }
        }

        static PackageInfo ResolveAssistantPackage(StringBuilder report)
        {
            var packageInfo = PackageInfo.FindForPackageName(AssistantPackageName);
            if (packageInfo == null)
            {
                report.AppendLine($"ERROR: {AssistantPackageName} is not installed.");
                return null;
            }

            return packageInfo;
        }

        static string ResolveRelayPath(PackageInfo packageInfo)
        {
            var basePath = packageInfo.resolvedPath;
            if (!Path.IsPathRooted(basePath))
                basePath = Path.GetFullPath(basePath);

            return Path.Combine(basePath, "RelayApp~", RelayExecutableName);
        }

        static string GetGatewayPreferencesStatus()
        {
            var rawPrefs = EditorPrefs.GetString(GatewayPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(rawPrefs))
                return "not initialized";

            try
            {
                var prefs = JObject.Parse(rawPrefs);
                var codex = FindProvider(prefs["ProviderInfoList"] as JArray, ProviderId);
                if (codex == null)
                    return "Codex provider not found";

                var required = codex["RequiredEnvVarNames"] as JArray;
                var count = required?.Count ?? 0;
                return count == 0 ? "Codex does not require env vars" : $"Codex requires {required}";
            }
            catch (Exception ex)
            {
                return $"invalid JSON: {ex.Message}";
            }
        }

        static string GetRelayServiceStatus()
        {
            try
            {
                var relayType = Type.GetType("Unity.Relay.Editor.RelayService, Unity.AI.Assistant.Editor");
                var instance = relayType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null)
                    return "RelayService not available";

                var isConnected = relayType.GetProperty("IsConnected", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                var port = relayType.GetProperty("Port", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                var processPath = relayType.GetProperty("ProcessExecutablePath", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                var state = relayType.GetProperty("State", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                return $"IsConnected={isConnected}, Port={port}, State={state}, Path={processPath}";
            }
            catch (Exception ex)
            {
                return $"RelayService status failed: {ex.Message}";
            }
        }

        static JObject FindProvider(JArray providers, string providerId)
        {
            if (providers == null)
                return null;

            foreach (var provider in providers)
            {
                if (provider is JObject providerObject &&
                    string.Equals(providerObject["ProviderType"]?.ToString(), providerId, StringComparison.OrdinalIgnoreCase))
                    return providerObject;
            }

            return null;
        }

        static void SetProperty(object target, string propertyName, object value)
        {
            target?.GetType()
                .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(target, value);
        }

        static object CreateEmptyListForProperty(object target, string propertyName)
        {
            var property = target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property == null ? null : Activator.CreateInstance(property.PropertyType);
        }

        static void InvokeAssistantPreferenceMethod(string methodName, StringBuilder report, params object[] args)
        {
            try
            {
                var type = Type.GetType("Unity.AI.Assistant.Editor.AssistantEditorPreferences, Unity.AI.Assistant.Editor");
                var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                method?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                report?.AppendLine($"Assistant preference warning ({methodName}): {ex.Message}");
            }
        }

        static string GetPreferredCodexModelId()
        {
            var cached = EditorPrefs.GetString(CodexModelPrefsKey, null);
            if (!string.IsNullOrEmpty(cached))
                return cached;

            var configured = ReadCodexConfiguredModel();
            return string.IsNullOrEmpty(configured) ? DefaultCodexModelId : configured;
        }

        static string ReadCodexConfiguredModel()
        {
            try
            {
                var config = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex",
                    "config.toml");
                if (!File.Exists(config))
                    return null;

                foreach (var line in File.ReadLines(config))
                {
                    var trimmed = line.Trim();
                    var equals = trimmed.IndexOf("=", StringComparison.Ordinal);
                    if (equals <= 0)
                        continue;

                    var key = trimmed.Substring(0, equals).Trim();
                    if (!string.Equals(key, "model", StringComparison.Ordinal))
                        continue;

                    var value = trimmed.Substring(equals + 1).Trim().Trim('"', '\'');
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        static string FindCodexExecutable()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                var cleanDir = dir.Trim('"');
                var candidate = Path.Combine(cleanDir, "codex.exe");
                if (File.Exists(candidate))
                    return candidate;

                candidate = Path.Combine(cleanDir, "codex.cmd");
                if (File.Exists(candidate))
                    return candidate;

                candidate = Path.Combine(cleanDir, "codex");
                if (File.Exists(candidate))
                    return candidate;
            }

            var windowsAppsShim = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "codex.exe");
            if (File.Exists(windowsAppsShim))
                return windowsAppsShim;

            return FindInstalledCodexExecutable();
        }

        static string FindInstalledCodexExecutable()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var windowsApps = Path.Combine(programFiles, "WindowsApps");

            try
            {
                if (!Directory.Exists(windowsApps))
                    return null;

                foreach (var dir in Directory.GetDirectories(windowsApps, "OpenAI.Codex_*"))
                {
                    var exe = Path.Combine(dir, "app", "resources", "codex.exe");
                    if (File.Exists(exe))
                        return exe;

                    var extensionless = Path.Combine(dir, "app", "resources", "codex");
                    if (File.Exists(extensionless))
                        return extensionless;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        static bool IsCodexLoggedIn(string codex)
        {
            if (string.IsNullOrEmpty(codex))
                return false;

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = codex,
                        Arguments = "login status",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    process.Start();
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        return false;
                    }

                    var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    return process.ExitCode == 0 &&
                        output.IndexOf("Logged in", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        static void StartCodexBrowserLogin(string codex)
        {
            var powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            if (!File.Exists(powershell))
                throw new FileNotFoundException("PowerShell was not found.", powershell);

            var command =
                $"& '{EscapePowerShellSingleQuotedPath(codex)}' login; " +
                "Write-Host ''; " +
                "Write-Host 'Codex OAuth login finished. You can close this window.'; " +
                "Write-Host 'If the browser did not open, copy the login URL printed above.'";

            Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoExit -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = Application.dataPath
            });
        }

        static void StartLoginStatusPolling()
        {
            s_LoginPollStartedAt = EditorApplication.timeSinceStartup;
            s_NextLoginPollAt = 0;
            EditorApplication.update -= PollLoginStatus;
            EditorApplication.update += PollLoginStatus;
        }

        static void PollLoginStatus()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < s_NextLoginPollAt)
                return;

            s_NextLoginPollAt = now + 3;
            var report = new StringBuilder();
            if (SyncAuthAndEnableCodex(showPreferences: false, report: report))
            {
                EditorApplication.update -= PollLoginStatus;
                Debug.Log($"{LogPrefix} Codex CLI login verified. Codex is enabled in Unity AI Assistant.");
                return;
            }

            if (now - s_LoginPollStartedAt > 120)
            {
                EditorApplication.update -= PollLoginStatus;
                UpdateCachedAuthStatus(false, FindCodexExecutable(), EditorPrefs.GetString(RelayPathPrefsKey, string.Empty));
                Debug.LogWarning($"{LogPrefix} Login polling timed out. If browser login completed, run Setup Codex OAuth again.");
            }
        }

        static void UpdateCachedAuthStatus(bool loggedIn, string codexPath, string relayPath)
        {
            EditorPrefs.SetString(AuthStatusPrefsKey, loggedIn ? "logged-in" : "logged-out");
            EditorPrefs.SetString(AuthStatusCheckedUtcPrefsKey, DateTime.UtcNow.ToString("O"));

            if (!string.IsNullOrEmpty(codexPath))
                EditorPrefs.SetString(CodexExecutablePrefsKey, codexPath);

            if (!string.IsNullOrEmpty(relayPath))
                EditorPrefs.SetString(RelayPathPrefsKey, relayPath);
        }

        static void ClearBridgeEditorPrefs()
        {
            EditorPrefs.DeleteKey(AuthStatusPrefsKey);
            EditorPrefs.DeleteKey(AuthStatusCheckedUtcPrefsKey);
            EditorPrefs.DeleteKey(CodexExecutablePrefsKey);
            EditorPrefs.DeleteKey(CodexModelPrefsKey);
            EditorPrefs.DeleteKey(RelayPathPrefsKey);
            EditorPrefs.DeleteKey(RelayPatchedUtcPrefsKey);
        }

        static bool IsWindowsEditor()
        {
            return Application.platform == RuntimePlatform.WindowsEditor;
        }

        static string EscapePowerShellSingleQuotedPath(string path)
        {
            return path.Replace("'", "''");
        }

        static int IndexOf(byte[] haystack, byte[] needle)
        {
            if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length)
                return -1;

            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var found = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] == needle[j])
                        continue;

                    found = false;
                    break;
                }

                if (found)
                    return i;
            }

            return -1;
        }

        static void ShowReport(string title, string report)
        {
            var output = string.IsNullOrWhiteSpace(report) ? "No report." : report.Trim();
            Debug.Log($"{LogPrefix}\n{output}");

            var dialogText = output.Length > 3500
                ? output.Substring(0, 3500) + "\n\nReport truncated in dialog. See the Unity Console for the full output."
                : output;

            EditorUtility.DisplayDialog(title, dialogText, "OK");
        }
    }
}
