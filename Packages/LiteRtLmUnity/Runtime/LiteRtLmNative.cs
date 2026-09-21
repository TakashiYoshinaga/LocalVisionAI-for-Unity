// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteRtLmUnity
{
    /// <summary>
    /// The subset of the LiteRT-LM C API that the in-Editor backend needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library is a prebuilt one from the LiteRT-LM releases:
    /// <c>libCLiteRTLM_mac.dylib</c> on macOS and <c>litert-lm.dll</c> on
    /// Windows. Neither is tracked by Git because of its size, so
    /// <c>Tools &gt; LiteRT-LM &gt; Install Editor Native Library</c> downloads it.
    /// Until it is installed every entry point throws
    /// <see cref="DllNotFoundException"/>, which the backend turns into an
    /// on-screen message.
    /// </para>
    /// <para>
    /// The two libraries are built from different releases — see
    /// <see cref="LiteRtLmUnity.EditorNativeLibrarySetup"/> for why — but every
    /// entry point below has the same signature in both, so one set of
    /// declarations covers them.
    /// </para>
    /// <para>
    /// Strings cross the boundary as explicitly UTF-8 encoded bytes rather than
    /// through the default marshaller, because prompts are routinely written in
    /// Japanese and the default is not guaranteed to be UTF-8.
    /// </para>
    /// </remarks>
    internal static class LiteRtLmNative
    {
#if UNITY_EDITOR_WIN
        private const string Library = "litert-lm";
#else
        private const string Library = "CLiteRTLM_mac";
#endif

        /// <summary>Matches <c>LiteRtLmSamplerType</c> in <c>c/engine.h</c>.</summary>
        public const int SamplerTypeTopK = 1;

        /// <summary>Matches <c>LiteRtLmLogSeverity</c> in <c>c/engine.h</c>.</summary>
        public const int LogSeverityWarning = 3;

        // --- Logging ----------------------------------------------------------

        // The error reporter in c/error_reporter.h is deliberately absent: the
        // published macOS library does not export it. A failing call returns null
        // and the library writes the reason to stderr, which is where the Console
        // and the Editor log pick it up.

        [DllImport(Library, EntryPoint = "litert_lm_set_min_log_level")]
        public static extern void SetMinLogLevel(int severity);

        // --- Engine settings --------------------------------------------------

        [DllImport(Library, EntryPoint = "litert_lm_engine_settings_create")]
        private static extern IntPtr EngineSettingsCreateNative(
            byte[] modelPath,
            byte[] backend,
            byte[] visionBackend,
            byte[] audioBackend);

        [DllImport(Library, EntryPoint = "litert_lm_engine_settings_set_max_num_images")]
        public static extern void EngineSettingsSetMaxNumImages(IntPtr settings, int maxNumImages);

        [DllImport(Library, EntryPoint = "litert_lm_engine_settings_set_cache_dir")]
        private static extern void EngineSettingsSetCacheDirNative(IntPtr settings, byte[] cacheDir);

        [DllImport(Library, EntryPoint = "litert_lm_engine_settings_delete")]
        public static extern void EngineSettingsDelete(IntPtr settings);

        // --- Engine -----------------------------------------------------------

        [DllImport(Library, EntryPoint = "litert_lm_engine_create")]
        public static extern IntPtr EngineCreate(IntPtr settings);

        [DllImport(Library, EntryPoint = "litert_lm_engine_delete")]
        public static extern void EngineDelete(IntPtr engine);

        // --- Sampler ----------------------------------------------------------

        [DllImport(Library, EntryPoint = "litert_lm_sampler_params_create")]
        public static extern IntPtr SamplerParamsCreate(int samplerType);

        [DllImport(Library, EntryPoint = "litert_lm_sampler_params_set_top_k")]
        public static extern void SamplerParamsSetTopK(IntPtr parameters, int topK);

        [DllImport(Library, EntryPoint = "litert_lm_sampler_params_set_top_p")]
        public static extern void SamplerParamsSetTopP(IntPtr parameters, float topP);

        [DllImport(Library, EntryPoint = "litert_lm_sampler_params_set_temperature")]
        public static extern void SamplerParamsSetTemperature(IntPtr parameters, float temperature);

        [DllImport(Library, EntryPoint = "litert_lm_sampler_params_set_seed")]
        public static extern void SamplerParamsSetSeed(IntPtr parameters, int seed);

        [DllImport(Library, EntryPoint = "litert_lm_sampler_params_delete")]
        public static extern void SamplerParamsDelete(IntPtr parameters);

        // --- Session config ---------------------------------------------------

        [DllImport(Library, EntryPoint = "litert_lm_session_config_create")]
        public static extern IntPtr SessionConfigCreate();

        [DllImport(Library, EntryPoint = "litert_lm_session_config_set_sampler_params")]
        public static extern void SessionConfigSetSamplerParams(IntPtr config, IntPtr samplerParams);

        [DllImport(Library, EntryPoint = "litert_lm_session_config_set_max_output_tokens")]
        public static extern void SessionConfigSetMaxOutputTokens(IntPtr config, int maxOutputTokens);

        [DllImport(Library, EntryPoint = "litert_lm_session_config_delete")]
        public static extern void SessionConfigDelete(IntPtr config);

        // --- Thinking config --------------------------------------------------

        [DllImport(Library, EntryPoint = "litert_lm_thinking_config_create")]
        public static extern IntPtr ThinkingConfigCreate();

        [DllImport(Library, EntryPoint = "litert_lm_thinking_config_set_enable_thinking")]
        public static extern void ThinkingConfigSetEnableThinking(
            IntPtr config,
            [MarshalAs(UnmanagedType.I1)] bool enableThinking);

        [DllImport(Library, EntryPoint = "litert_lm_thinking_config_set_thinking_token_budget")]
        public static extern void ThinkingConfigSetTokenBudget(IntPtr config, int tokenBudget);

        [DllImport(Library, EntryPoint = "litert_lm_thinking_config_delete")]
        public static extern void ThinkingConfigDelete(IntPtr config);

        // --- Conversation config ----------------------------------------------

        [DllImport(Library, EntryPoint = "litert_lm_conversation_config_create")]
        public static extern IntPtr ConversationConfigCreate();

        [DllImport(Library, EntryPoint = "litert_lm_conversation_config_set_session_config")]
        public static extern void ConversationConfigSetSessionConfig(IntPtr config, IntPtr sessionConfig);

        [DllImport(Library, EntryPoint = "litert_lm_conversation_config_set_system_message")]
        private static extern void ConversationConfigSetSystemMessageNative(IntPtr config, byte[] systemMessage);

        [DllImport(Library, EntryPoint = "litert_lm_conversation_config_set_thinking_config")]
        public static extern void ConversationConfigSetThinkingConfig(IntPtr config, IntPtr thinkingConfig);

        [DllImport(Library, EntryPoint = "litert_lm_conversation_config_delete")]
        public static extern void ConversationConfigDelete(IntPtr config);

        // --- Conversation -----------------------------------------------------

        [DllImport(Library, EntryPoint = "litert_lm_conversation_create")]
        public static extern IntPtr ConversationCreate(IntPtr engine, IntPtr config);

        [DllImport(Library, EntryPoint = "litert_lm_conversation_delete")]
        public static extern void ConversationDelete(IntPtr conversation);

        [DllImport(Library, EntryPoint = "litert_lm_conversation_send_message")]
        private static extern IntPtr ConversationSendMessageNative(
            IntPtr conversation,
            byte[] messageJson,
            byte[] extraContext,
            IntPtr optionalArgs);

        [DllImport(Library, EntryPoint = "litert_lm_json_response_get_string")]
        private static extern IntPtr JsonResponseGetStringNative(IntPtr response);

        [DllImport(Library, EntryPoint = "litert_lm_json_response_delete")]
        public static extern void JsonResponseDelete(IntPtr response);

        // --- Managed wrappers -------------------------------------------------

        public static IntPtr EngineSettingsCreate(string modelPath, string backend)
        {
            // The audio backend stays unset: this sample never sends audio, and
            // asking for one only makes the engine load more than it needs.
            return EngineSettingsCreateNative(
                ToUtf8(modelPath),
                ToUtf8(backend),
                ToUtf8(backend),
                null);
        }

        public static void EngineSettingsSetCacheDir(IntPtr settings, string cacheDir)
        {
            EngineSettingsSetCacheDirNative(settings, ToUtf8(cacheDir));
        }

        public static void ConversationConfigSetSystemMessage(IntPtr config, string systemMessage)
        {
            ConversationConfigSetSystemMessageNative(config, ToUtf8(systemMessage));
        }

        public static IntPtr ConversationSendMessage(IntPtr conversation, string messageJson)
        {
            return ConversationSendMessageNative(conversation, ToUtf8(messageJson), null, IntPtr.Zero);
        }

        public static string JsonResponseGetString(IntPtr response)
        {
            return FromUtf8(JsonResponseGetStringNative(response));
        }

        private static byte[] ToUtf8(string value)
        {
            if (value == null)
            {
                return null;
            }

            // The C API expects a null-terminated string, which the UTF8 encoder
            // does not add on its own.
            byte[] encoded = Encoding.UTF8.GetBytes(value);
            var terminated = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, terminated, 0, encoded.Length);
            terminated[encoded.Length] = 0;
            return terminated;
        }

        private static string FromUtf8(IntPtr pointer)
        {
            return pointer == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
    }
}

#endif
