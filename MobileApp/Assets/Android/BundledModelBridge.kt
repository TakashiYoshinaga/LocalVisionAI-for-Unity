package com.takashiyoshinaga.localvisionai

import android.content.res.AssetManager
import android.os.StatFs
import android.os.SystemClock
import android.util.Log
import com.google.ai.edge.litertlm.Backend
import com.google.ai.edge.litertlm.Content
import com.google.ai.edge.litertlm.Contents
import com.google.ai.edge.litertlm.ConversationConfig
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.EngineConfig
import com.google.ai.edge.litertlm.Message
import com.unity3d.player.UnityPlayer
import org.json.JSONObject
import java.io.File
import java.io.FileNotFoundException
import java.io.FileOutputStream
import java.io.IOException
import java.security.MessageDigest
import java.util.Locale
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

object BundledModelBridge {
    private const val LOG_TAG = "LocalVisionAI"
    private const val MODELS_DIRECTORY = "Models"
    private const val BUFFER_SIZE = 1024 * 1024
    private const val STORAGE_MARGIN_BYTES = 16L * 1024L * 1024L
    private const val PROGRESS_STEP = 0.01f

    private val executor = Executors.newSingleThreadExecutor()
    private val isPreparing = AtomicBoolean(false)
    private val isInitializing = AtomicBoolean(false)
    private val isAnalyzing = AtomicBoolean(false)
    private val shutdownRequested = AtomicBoolean(false)
    private val engineLock = Any()

    @Volatile
    private var engine: Engine? = null

    @JvmStatic
    fun prepareBundledModel(
        callbackGameObject: String,
        assetPath: String,
        destinationFileName: String
    ) {
        if (!isPreparing.compareAndSet(false, true)) {
            send(
                callbackGameObject,
                "ExtractingModel",
                "AI model setup is already running.",
                null,
                ready = false,
                retryable = false
            )
            return
        }

        executor.execute {
            try {
                prepare(
                    callbackGameObject,
                    assetPath,
                    destinationFileName
                )
            } catch (exception: FileNotFoundException) {
                sendError(
                    callbackGameObject,
                    "Bundled AI model or verification metadata was not found in this APK.",
                    retryable = false
                )
            } catch (exception: InsufficientStorageException) {
                sendError(
                    callbackGameObject,
                    "Not enough free storage to extract the AI model.",
                    retryable = true
                )
            } catch (exception: ModelIntegrityException) {
                sendError(
                    callbackGameObject,
                    "Bundled AI model verification failed. Reinstall the APK and try again.",
                    retryable = true
                )
            } catch (exception: IOException) {
                sendError(
                    callbackGameObject,
                    "Could not extract the bundled AI model: ${safeMessage(exception)}",
                    retryable = true
                )
            } catch (exception: Exception) {
                sendError(
                    callbackGameObject,
                    "Unexpected model setup error: ${safeMessage(exception)}",
                    retryable = true
                )
            } finally {
                isPreparing.set(false)
            }
        }
    }

    @JvmStatic
    fun initialize(
        callbackGameObject: String,
        modelPath: String
    ) {
        if (!isInitializing.compareAndSet(false, true)) {
            sendEngine(
                callbackGameObject,
                "Initializing",
                "AI engine initialization is already running.",
                ready = false
            )
            return
        }

        shutdownRequested.set(false)
        executor.execute {
            try {
                initializeEngine(callbackGameObject, modelPath)
            } catch (throwable: Throwable) {
                Log.e(LOG_TAG, "LiteRT-LM initialization failed.", throwable)
                sendEngine(
                    callbackGameObject,
                    "Error",
                    "Could not initialize the AI engine: ${safeMessage(throwable)}",
                    ready = false
                )
            } finally {
                isInitializing.set(false)
            }
        }
    }

    @JvmStatic
    fun analyze(
        callbackGameObject: String,
        requestId: Int,
        imageData: ByteArray,
        prompt: String
    ) {
        if (!isAnalyzing.compareAndSet(false, true)) {
            sendAnalysis(
                callbackGameObject,
                requestId,
                "Error",
                "Image analysis is already running."
            )
            return
        }

        executor.execute {
            try {
                runAnalysis(callbackGameObject, requestId, imageData, prompt)
            } catch (throwable: Throwable) {
                Log.e(LOG_TAG, "Image analysis failed.", throwable)
                sendAnalysis(
                    callbackGameObject,
                    requestId,
                    "Error",
                    "Could not analyze the image: ${safeMessage(throwable)}"
                )
            } finally {
                isAnalyzing.set(false)
            }
        }
    }

    @JvmStatic
    fun shutdown() {
        shutdownRequested.set(true)
        executor.execute {
            val engineToClose = synchronized(engineLock) {
                val current = engine
                engine = null
                current
            }
            closeQuietly(engineToClose)
        }
    }

    private fun initializeEngine(
        callbackGameObject: String,
        modelPath: String
    ) {
        val model = File(modelPath)
        if (!model.isFile || model.length() <= 0L) {
            throw FileNotFoundException("Prepared AI model was not found.")
        }

        synchronized(engineLock) {
            engine?.let { existing ->
                if (existing.isInitialized()) {
                    sendEngine(
                        callbackGameObject,
                        "Ready",
                        "AI Ready",
                        ready = true
                    )
                    return
                }
            }
        }

        sendEngine(
            callbackGameObject,
            "Initializing",
            "Initializing AI engine...",
            ready = false
        )

        var candidate: Engine? = null
        var selectedBackend = "GPU"

        try {
            candidate = createEngine(model.absolutePath, Backend.GPU())
            candidate.initialize()
        } catch (gpuFailure: Throwable) {
            closeQuietly(candidate)
            candidate = null
            selectedBackend = "CPU fallback"
            Log.w(
                LOG_TAG,
                "GPU model initialization failed; retrying the main model on CPU.",
                gpuFailure
            )
            sendEngine(
                callbackGameObject,
                "Initializing",
                "GPU initialization failed. Trying CPU fallback...",
                ready = false
            )

            candidate = createEngine(model.absolutePath, Backend.CPU())
            candidate.initialize()
        }

        if (shutdownRequested.get()) {
            closeQuietly(candidate)
            return
        }

        synchronized(engineLock) {
            closeQuietly(engine)
            engine = candidate
        }

        Log.i(LOG_TAG, "LiteRT-LM initialized with $selectedBackend backend.")
        sendEngine(
            callbackGameObject,
            "Ready",
            "AI Ready",
            ready = true
        )
    }

    private fun runAnalysis(
        callbackGameObject: String,
        requestId: Int,
        imageData: ByteArray,
        prompt: String
    ) {
        if (imageData.isEmpty()) {
            throw IllegalArgumentException("The captured image was empty.")
        }

        val activeEngine = synchronized(engineLock) { engine }
        if (activeEngine == null || !activeEngine.isInitialized()) {
            throw IllegalStateException("The AI engine is not ready.")
        }

        sendAnalysis(
            callbackGameObject,
            requestId,
            "Inferencing",
            "Analyzing image..."
        )

        val contents = Contents.of(
            Content.ImageBytes(imageData),
            Content.Text(prompt)
        )
        val startedAt = SystemClock.elapsedRealtime()

        // One conversation per request. This PoC describes a single image, so
        // keeping history would only grow the context with unused image tokens.
        val answer = activeEngine.createConversation(ConversationConfig()).use {
            conversation ->
            extractText(conversation.sendMessage(contents))
        }

        if (shutdownRequested.get()) {
            return
        }

        Log.i(
            LOG_TAG,
            String.format(
                Locale.US,
                "Image analysis finished in %.1f s.",
                (SystemClock.elapsedRealtime() - startedAt) / 1000.0
            )
        )

        if (answer.isBlank()) {
            throw IllegalStateException("The AI model returned an empty answer.")
        }

        sendAnalysis(
            callbackGameObject,
            requestId,
            "Ready",
            "AI Ready",
            answer
        )
    }

    private fun extractText(message: Message): String =
        message.contents.contents
            .filterIsInstance<Content.Text>()
            .joinToString(separator = "") { content -> content.text }
            .trim()

    private fun createEngine(modelPath: String, mainBackend: Backend): Engine {
        val activity = UnityPlayer.currentActivity
            ?: throw IllegalStateException("Unity activity is unavailable.")
        val config = EngineConfig(
            modelPath = modelPath,
            backend = mainBackend,
            visionBackend = Backend.GPU(),
            cacheDir = activity.cacheDir.absolutePath
        )
        return Engine(config)
    }

    private fun closeQuietly(engineToClose: Engine?) {
        if (engineToClose == null) {
            return
        }

        try {
            engineToClose.close()
        } catch (throwable: Throwable) {
            Log.w(LOG_TAG, "Could not close the LiteRT-LM engine cleanly.", throwable)
        }
    }

    private fun prepare(
        callbackGameObject: String,
        assetPath: String,
        destinationFileName: String
    ) {
        val activity = UnityPlayer.currentActivity
            ?: throw IllegalStateException("Unity activity is unavailable.")
        val metadata = readMetadata(activity.assets, "$assetPath.sha256")
        val modelsDirectory = File(activity.noBackupFilesDir, MODELS_DIRECTORY)

        if (!modelsDirectory.exists() && !modelsDirectory.mkdirs()) {
            throw IOException("Could not create the private model directory.")
        }

        val destination = File(modelsDirectory, destinationFileName)
        val partial = File(modelsDirectory, "$destinationFileName.partial")
        val verification = File(modelsDirectory, "$destinationFileName.verified")

        if (isVerified(destination, verification, metadata)) {
            send(
                callbackGameObject,
                "Initializing",
                "Bundled AI model is ready.",
                null,
                ready = true,
                retryable = false,
                modelPath = destination.absolutePath
            )
            return
        }

        ensureEnoughStorage(modelsDirectory, metadata.size)
        deleteIfPresent(partial)

        send(
            callbackGameObject,
            "ExtractingModel",
            "Extracting bundled AI model...",
            0f,
            ready = false,
            retryable = true
        )

        val digest = MessageDigest.getInstance("SHA-256")
        var copiedBytes = 0L
        var lastReportedProgress = -1f

        try {
            FileOutputStream(partial).use { output ->
                val buffer = ByteArray(BUFFER_SIZE)

                for (partIndex in 0 until metadata.partCount) {
                    val partPath = partAssetPath(assetPath, partIndex)

                    activity.assets.open(partPath, AssetManager.ACCESS_STREAMING)
                        .use { input ->
                            while (true) {
                                val read = input.read(buffer)
                                if (read < 0) {
                                    break
                                }

                                output.write(buffer, 0, read)
                                digest.update(buffer, 0, read)
                                copiedBytes += read

                                val progress = if (metadata.size > 0L) {
                                    (copiedBytes.toDouble() / metadata.size.toDouble())
                                        .coerceIn(0.0, 1.0)
                                        .toFloat()
                                } else {
                                    0f
                                }

                                if (progress - lastReportedProgress >= PROGRESS_STEP) {
                                    lastReportedProgress = progress
                                    send(
                                        callbackGameObject,
                                        "ExtractingModel",
                                        "Extracting bundled AI model...",
                                        progress,
                                        ready = false,
                                        retryable = true
                                    )
                                }
                            }
                        }
                }

                output.fd.sync()
            }

            val copiedHash = digest.digest().toHex()
            if (copiedBytes != metadata.size ||
                !copiedHash.equals(metadata.sha256, ignoreCase = true)) {
                throw ModelIntegrityException()
            }

            replaceFile(partial, destination)
            writeVerification(verification, metadata)

            send(
                callbackGameObject,
                "Initializing",
                "Bundled AI model is ready.",
                null,
                ready = true,
                retryable = false,
                modelPath = destination.absolutePath
            )
        } catch (exception: Exception) {
            deleteQuietly(partial)
            throw exception
        }
    }

    private fun readMetadata(
        assets: AssetManager,
        metadataAssetPath: String
    ): ModelMetadata {
        val values = mutableMapOf<String, String>()

        assets.open(metadataAssetPath, AssetManager.ACCESS_STREAMING)
            .bufferedReader(Charsets.UTF_8)
            .useLines { lines ->
                lines.forEach { line ->
                    val separator = line.indexOf('=')
                    if (separator > 0) {
                        values[line.substring(0, separator)] =
                            line.substring(separator + 1)
                    }
                }
            }

        val hash = values["sha256"]
        val size = values["size"]?.toLongOrNull()
        val partCount = values["parts"]?.toIntOrNull() ?: 1
        if (hash == null || hash.length != 64 || size == null || size <= 0L ||
            partCount <= 0) {
            throw ModelIntegrityException()
        }

        return ModelMetadata(hash.lowercase(), size, partCount)
    }

    private fun isVerified(
        destination: File,
        verification: File,
        metadata: ModelMetadata
    ): Boolean {
        if (!destination.isFile ||
            destination.length() != metadata.size ||
            !verification.isFile) {
            return false
        }

        return try {
            val values = verification.readLines(Charsets.UTF_8)
                .mapNotNull { line ->
                    val separator = line.indexOf('=')
                    if (separator > 0) {
                        line.substring(0, separator) to
                            line.substring(separator + 1)
                    } else {
                        null
                    }
                }
                .toMap()

            values["sha256"].equals(metadata.sha256, ignoreCase = true) &&
                values["size"]?.toLongOrNull() == metadata.size
        } catch (_: IOException) {
            false
        }
    }

    private fun ensureEnoughStorage(directory: File, requiredBytes: Long) {
        val availableBytes = StatFs(directory.absolutePath).availableBytes
        if (availableBytes < requiredBytes + STORAGE_MARGIN_BYTES) {
            throw InsufficientStorageException()
        }
    }

    private fun replaceFile(source: File, destination: File) {
        deleteIfPresent(destination)
        if (!source.renameTo(destination)) {
            throw IOException("Could not finalize the extracted model.")
        }
    }

    private fun writeVerification(
        verification: File,
        metadata: ModelMetadata
    ) {
        val temporary = File(verification.parentFile, "${verification.name}.partial")
        deleteIfPresent(temporary)

        FileOutputStream(temporary).use { output ->
            output.write(
                "sha256=${metadata.sha256}\nsize=${metadata.size}\n"
                    .toByteArray(Charsets.UTF_8)
            )
            output.fd.sync()
        }

        replaceFile(temporary, verification)
    }

    private fun deleteQuietly(file: File) {
        try {
            deleteIfPresent(file)
        } catch (_: IOException) {
        }
    }

    private fun deleteIfPresent(file: File) {
        if (file.exists() && !file.delete()) {
            throw IOException("Could not replace stale model data.")
        }
    }

    private fun sendError(
        callbackGameObject: String,
        message: String,
        retryable: Boolean
    ) {
        send(
            callbackGameObject,
            "Error",
            message,
            null,
            ready = false,
            retryable = retryable
        )
    }

    private fun send(
        callbackGameObject: String,
        phase: String,
        message: String,
        progress01: Float?,
        ready: Boolean,
        retryable: Boolean,
        modelPath: String = ""
    ) {
        val payload = JSONObject()
            .put("phase", phase)
            .put("message", message)
            .put("hasProgress", progress01 != null)
            .put("progress01", progress01 ?: 0f)
            .put("ready", ready)
            .put("retryable", retryable)
            .put("modelPath", modelPath)
            .toString()

        UnityPlayer.UnitySendMessage(
            callbackGameObject,
            "OnBundledModelProgress",
            payload
        )
    }

    private fun sendEngine(
        callbackGameObject: String,
        phase: String,
        message: String,
        ready: Boolean
    ) {
        val payload = JSONObject()
            .put("phase", phase)
            .put("message", message)
            .put("hasProgress", false)
            .put("progress01", 0f)
            .put("ready", ready)
            .put("retryable", phase == "Error")
            .put("modelPath", "")
            .toString()

        UnityPlayer.UnitySendMessage(
            callbackGameObject,
            "OnEngineProgress",
            payload
        )
    }

    private fun sendAnalysis(
        callbackGameObject: String,
        requestId: Int,
        phase: String,
        message: String,
        resultText: String = ""
    ) {
        val payload = JSONObject()
            .put("phase", phase)
            .put("message", message)
            .put("requestId", requestId)
            .put("resultText", resultText)
            .toString()

        UnityPlayer.UnitySendMessage(
            callbackGameObject,
            "OnAnalysisProgress",
            payload
        )
    }

    private fun partAssetPath(assetPath: String, partIndex: Int): String =
        String.format(Locale.US, "%s.part%03d", assetPath, partIndex)

    private fun ByteArray.toHex(): String =
        joinToString(separator = "") { byte -> "%02x".format(byte) }

    private fun safeMessage(throwable: Throwable): String =
        throwable.message?.take(160) ?: throwable.javaClass.simpleName

    private data class ModelMetadata(
        val sha256: String,
        val size: Long,
        val partCount: Int
    )

    private class InsufficientStorageException : IOException()
    private class ModelIntegrityException : IOException()
}
