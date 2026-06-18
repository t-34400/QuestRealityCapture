#include "qrc_camera_native.h"

#include <android/log.h>
#include <camera/NdkCameraDevice.h>
#include <camera/NdkCameraManager.h>
#include <media/NdkImage.h>
#include <media/NdkImageReader.h>

#include <atomic>
#include <cerrno>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <deque>
#include <dlfcn.h>
#include <fstream>
#include <memory>
#include <mutex>
#include <new>
#include <sstream>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#define QRC_STEREO_LOG_TAG "QrcStereoCameraNative"
#define QRC_STEREO_LOGE(...) __android_log_print(ANDROID_LOG_ERROR, QRC_STEREO_LOG_TAG, __VA_ARGS__)
#define QRC_STEREO_LOGW(...) __android_log_print(ANDROID_LOG_WARN, QRC_STEREO_LOG_TAG, __VA_ARGS__)

namespace {

constexpr int kImageReaderMaxImages = 2;
constexpr size_t kMaxPendingSideFrames = 6;
constexpr size_t kMaxPendingWrites = 24;
constexpr uint32_t kQuestCameraSourceTag = 0x80004d00u;
constexpr uint32_t kQuestCameraPositionTag = 0x80004d01u;
constexpr int64_t kDefaultMaxTimeDeltaNs = 20000000LL;

int64_t nowMonotonicNs() {
    timespec ts{};
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<int64_t>(ts.tv_sec) * 1000000000LL + ts.tv_nsec;
}

int64_t nowUnixMs() {
    timespec ts{};
    clock_gettime(CLOCK_REALTIME, &ts);
    return static_cast<int64_t>(ts.tv_sec) * 1000LL + ts.tv_nsec / 1000000LL;
}

std::string parentDirectoryOf(const std::string& path) {
    const auto pos = path.find_last_of('/');
    if (pos == std::string::npos) return {};
    return pos == 0 ? "/" : path.substr(0, pos);
}

bool makeDirectories(const std::string& path) {
    if (path.empty()) return true;
    std::string current;
    size_t i = 0;
    if (path[0] == '/') {
        current = "/";
        i = 1;
    }
    while (i <= path.size()) {
        const size_t next = path.find('/', i);
        const std::string part = path.substr(i, next == std::string::npos ? std::string::npos : next - i);
        if (!part.empty()) {
            if (current.size() > 1 && current.back() != '/') current += '/';
            current += part;
            if (mkdir(current.c_str(), 0775) != 0 && errno != EEXIST) return false;
        }
        if (next == std::string::npos) break;
        i = next + 1;
    }
    return true;
}

bool writeBinaryFile(const std::string& path, const std::vector<uint8_t>& data) {
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) return false;
    out.write(reinterpret_cast<const char*>(data.data()), static_cast<std::streamsize>(data.size()));
    return out.good();
}

bool writeTextFile(const std::string& path, const std::string& text) {
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) return false;
    out << text;
    return out.good();
}

bool appendTextFile(const std::string& path, const std::string& text) {
    std::ofstream out(path, std::ios::binary | std::ios::app);
    if (!out) return false;
    out << text;
    return out.good();
}

struct PlaneInfo {
    int rowStride = 0;
    int pixelStride = 0;
    int bufferSize = 0;
};

struct FrameJob {
    std::string path;
    std::vector<uint8_t> data;
};

class FrameWriter {
public:
    ~FrameWriter() { stop(); }

    void start() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (running_) return;
        stopping_ = false;
        running_ = true;
        worker_ = std::thread([this] { run(); });
    }

    void stop() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!running_) return;
            stopping_ = true;
        }
        cv_.notify_all();
        if (worker_.joinable()) worker_.join();
        std::lock_guard<std::mutex> lock(mutex_);
        running_ = false;
        queue_.clear();
    }

    bool enqueue(FrameJob&& job) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!running_ || stopping_ || queue_.size() >= kMaxPendingWrites) return false;
        queue_.push_back(std::move(job));
        cv_.notify_one();
        return true;
    }

    int64_t ioErrorCount() const { return ioErrors_.load(); }

private:
    void run() {
        for (;;) {
            FrameJob job;
            {
                std::unique_lock<std::mutex> lock(mutex_);
                cv_.wait(lock, [this] { return stopping_ || !queue_.empty(); });
                if (stopping_ && queue_.empty()) return;
                job = std::move(queue_.front());
                queue_.pop_front();
            }
            if (!writeBinaryFile(job.path, job.data)) {
                QRC_STEREO_LOGE("Failed to write stereo frame: %s", job.path.c_str());
                ++ioErrors_;
            }
        }
    }

    std::mutex mutex_;
    std::condition_variable cv_;
    std::deque<FrameJob> queue_;
    std::thread worker_;
    bool running_ = false;
    bool stopping_ = false;
    std::atomic<int64_t> ioErrors_{0};
};

struct CapturedFrame {
    int64_t imageTimestampNs = 0;
    int64_t unixMs = 0;
    std::vector<PlaneInfo> planes;
    std::vector<uint8_t> data;
};

class StereoCameraNative {
public:
    ~StereoCameraNative() {
        close();
        if (manager_) ACameraManager_delete(manager_);
    }

    QrcCameraResult initialize(
        int width,
        int height,
        const char* leftFrameDirectory,
        const char* rightFrameDirectory,
        const char* leftFormatInfoFilePath,
        const char* rightFormatInfoFilePath,
        const char* pairCsvFilePath,
        int64_t maxTimeDeltaNs) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (width <= 0 || height <= 0 || !leftFrameDirectory || !rightFrameDirectory
            || !leftFormatInfoFilePath || !rightFormatInfoFilePath || !pairCsvFilePath) {
            return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "Invalid stereo initialize arguments");
        }
        if (state_ != State::Uninitialized) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Stereo session is already initialized");
        }
        if (!makeDirectories(leftFrameDirectory) || !makeDirectories(rightFrameDirectory)
            || !makeDirectories(parentDirectoryOf(leftFormatInfoFilePath))
            || !makeDirectories(parentDirectoryOf(rightFormatInfoFilePath))
            || !makeDirectories(parentDirectoryOf(pairCsvFilePath))) {
            return setError(QRC_CAMERA_ERROR_IO, "Failed to create stereo output directories");
        }
        if (!writeTextFile(pairCsvFilePath,
                "pair_index,left_timestamp_ns,right_timestamp_ns,delta_ns,left_unix_ms,right_unix_ms,left_file,right_file\n")) {
            return setError(QRC_CAMERA_ERROR_IO, "Failed to initialize stereo pair CSV");
        }
        if (!ensureManagerLocked()) {
            return setError(QRC_CAMERA_ERROR_CAMERA_OPEN_FAILED, "ACameraManager_create failed");
        }
        width_ = width;
        height_ = height;
        left_.frameDirectory = leftFrameDirectory;
        right_.frameDirectory = rightFrameDirectory;
        left_.formatInfoFilePath = leftFormatInfoFilePath;
        right_.formatInfoFilePath = rightFormatInfoFilePath;
        pairCsvFilePath_ = pairCsvFilePath;
        maxTimeDeltaNs_ = maxTimeDeltaNs > 0 ? maxTimeDeltaNs : kDefaultMaxTimeDeltaNs;
        baseMonoTimeNs_ = nowMonotonicNs();
        baseUnixTimeMs_ = nowUnixMs();
        state_ = State::Initialized;
        lastError_.clear();
        return QRC_CAMERA_OK;
    }

    QrcCameraResult setSaveFrameRate(int fps) {
        if (fps < 0) return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "fps must be >= 0");
        targetSaveFps_ = fps;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult openByPositions() {
        std::string leftId;
        std::string rightId;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (state_ != State::Initialized) {
                return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Stereo session must be initialized and closed before open");
            }
            if (!findPassthroughCameraIdForPositionLocked(QRC_CAMERA_LEFT, leftId)
                || !findPassthroughCameraIdForPositionLocked(QRC_CAMERA_RIGHT, rightId)) {
                return setError(QRC_CAMERA_ERROR_CAMERA_NOT_FOUND, "Failed to resolve Quest left/right passthrough camera IDs");
            }
        }
        return openByIds(leftId.c_str(), rightId.c_str());
    }

    QrcCameraResult openByIds(const char* leftCameraId, const char* rightCameraId) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!leftCameraId || !rightCameraId || leftCameraId[0] == '\0' || rightCameraId[0] == '\0') {
            return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "Stereo camera IDs must be non-empty");
        }
        if (state_ != State::Initialized) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Stereo session must be initialized and closed before open");
        }
        if (std::string(leftCameraId) == std::string(rightCameraId)) {
            return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "Stereo left and right camera IDs must differ");
        }
        QrcCameraResult result = openSideLocked(left_, leftCameraId, true);
        if (result != QRC_CAMERA_OK) {
            destroySideLocked(left_);
            return result;
        }
        result = openSideLocked(right_, rightCameraId, false);
        if (result != QRC_CAMERA_OK) {
            destroySideLocked(right_);
            destroySideLocked(left_);
            return result;
        }
        state_ = State::Opened;
        lastError_.clear();
        return QRC_CAMERA_OK;
    }

    QrcCameraResult startRecording() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ != State::Opened && state_ != State::Recording) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Stereo cameras are not opened");
        }
        leftPending_.clear();
        rightPending_.clear();
        writer_.start();
        recording_ = true;
        state_ = State::Recording;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult stopRecording() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ != State::Opened && state_ != State::Recording) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Stereo cameras are not opened");
        }
        recording_ = false;
        leftPending_.clear();
        rightPending_.clear();
        writer_.stop();
        state_ = State::Opened;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult close() {
        std::lock_guard<std::mutex> lock(mutex_);
        recording_ = false;
        leftPending_.clear();
        rightPending_.clear();
        writer_.stop();
        destroySideLocked(right_);
        destroySideLocked(left_);
        if (state_ != State::Uninitialized) state_ = State::Initialized;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult getStats(QrcStereoCameraStats* outStats) {
        if (!outStats) return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "outStats is null");
        outStats->leftReceivedFrameCount = leftReceivedFrameCount_.load();
        outStats->rightReceivedFrameCount = rightReceivedFrameCount_.load();
        outStats->savedPairCount = savedPairCount_.load();
        outStats->droppedFrameCount = droppedFrameCount_.load();
        outStats->ioErrorCount = ioErrorCount_.load() + writer_.ioErrorCount();
        outStats->lastLeftImageTimestampNs = lastLeftImageTimestampNs_.load();
        outStats->lastRightImageTimestampNs = lastRightImageTimestampNs_.load();
        outStats->lastSavedLeftTimestampNs = lastSavedLeftTimestampNs_.load();
        outStats->lastSavedRightTimestampNs = lastSavedRightTimestampNs_.load();
        outStats->lastSavedDeltaNs = lastSavedDeltaNs_.load();
        return QRC_CAMERA_OK;
    }

    const char* lastError() const { return lastError_.c_str(); }
    const char* lastOpenedLeftCameraId() const { return left_.cameraId.c_str(); }
    const char* lastOpenedRightCameraId() const { return right_.cameraId.c_str(); }

private:
    enum class State { Uninitialized, Initialized, Opened, Recording };

    struct SideState {
        ACameraDevice* device = nullptr;
        ACameraCaptureSession* session = nullptr;
        ACaptureRequest* request = nullptr;
        ACaptureSessionOutputContainer* outputContainer = nullptr;
        ACaptureSessionOutput* sessionOutput = nullptr;
        ACameraOutputTarget* outputTarget = nullptr;
        AImageReader* reader = nullptr;
        ANativeWindow* readerWindow = nullptr;
        std::string frameDirectory;
        std::string formatInfoFilePath;
        std::string cameraId;
        std::atomic<bool> formatInfoWritten{false};
    };

    struct ReaderContext {
        StereoCameraNative* owner = nullptr;
        bool isLeft = true;
    };

    QrcCameraResult setError(QrcCameraResult result, const std::string& message) {
        lastError_ = message;
        QRC_STEREO_LOGE("%s", message.c_str());
        return result;
    }

    bool ensureManagerLocked() {
        if (manager_) return true;
        manager_ = ACameraManager_create();
        return manager_ != nullptr;
    }

    bool readIntegralMetadataByTag(ACameraMetadata* metadata, uint32_t tag, int& value) const {
        ACameraMetadata_const_entry entry{};
        if (ACameraMetadata_getConstEntry(metadata, tag, &entry) != ACAMERA_OK || entry.count <= 0) return false;
        switch (entry.type) {
            case ACAMERA_TYPE_BYTE: value = static_cast<int>(entry.data.u8[0]); return true;
            case ACAMERA_TYPE_INT32: value = static_cast<int>(entry.data.i32[0]); return true;
            case ACAMERA_TYPE_INT64: value = static_cast<int>(entry.data.i64[0]); return true;
            default: return false;
        }
    }

    using GetTagFromNameFn = camera_status_t (*)(const ACameraMetadata*, const char*, uint32_t*);

    GetTagFromNameFn getTagFromNameFunction() const {
        static GetTagFromNameFn function = reinterpret_cast<GetTagFromNameFn>(
            dlsym(RTLD_DEFAULT, "ACameraMetadata_getTagFromName"));
        return function;
    }

    bool readIntegralMetadataByName(ACameraMetadata* metadata, const char* name, int& value) const {
        auto* getTagFromName = getTagFromNameFunction();
        if (!getTagFromName) return false;
        uint32_t tag = 0;
        if (getTagFromName(metadata, name, &tag) != ACAMERA_OK) return false;
        return readIntegralMetadataByTag(metadata, tag, value);
    }

    bool tryReadQuestVendorHints(ACameraMetadata* metadata, int& cameraPositionId, int& cameraSource) const {
        int source = -1;
        int position = -1;
        bool sourceResolved = readIntegralMetadataByTag(metadata, kQuestCameraSourceTag, source);
        bool positionResolved = readIntegralMetadataByTag(metadata, kQuestCameraPositionTag, position);
        if (!sourceResolved) sourceResolved = readIntegralMetadataByName(metadata, "com.meta.extra_metadata.camera_source", source);
        if (!positionResolved) positionResolved = readIntegralMetadataByName(metadata, "com.meta.extra_metadata.position", position);
        cameraSource = sourceResolved ? source : -1;
        cameraPositionId = positionResolved ? position : -1;
        return sourceResolved && positionResolved;
    }

    bool findPassthroughCameraIdForPositionLocked(QrcCameraPosition position, std::string& outId) {
        ACameraIdList* idList = nullptr;
        if (ACameraManager_getCameraIdList(manager_, &idList) != ACAMERA_OK || !idList) return false;
        const int requestedPosition = position == QRC_CAMERA_RIGHT ? 1 : 0;
        bool found = false;
        for (int i = 0; i < idList->numCameras && !found; ++i) {
            ACameraMetadata* metadata = nullptr;
            if (ACameraManager_getCameraCharacteristics(manager_, idList->cameraIds[i], &metadata) != ACAMERA_OK || !metadata) continue;
            int cameraPositionId = -1;
            int cameraSource = -1;
            if (tryReadQuestVendorHints(metadata, cameraPositionId, cameraSource)
                && cameraSource == 0 && cameraPositionId == requestedPosition) {
                outId = idList->cameraIds[i];
                found = true;
            }
            ACameraMetadata_free(metadata);
        }
        ACameraManager_deleteCameraIdList(idList);
        return found;
    }

    QrcCameraResult createImageReaderLocked(SideState& side, bool isLeft) {
        media_status_t status = AImageReader_new(width_, height_, AIMAGE_FORMAT_YUV_420_888, kImageReaderMaxImages, &side.reader);
        if (status != AMEDIA_OK || !side.reader) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "AImageReader_new failed");
        AImageReader_ImageListener listener{};
        listener.context = isLeft ? static_cast<void*>(&leftReaderContext_) : static_cast<void*>(&rightReaderContext_);
        listener.onImageAvailable = &StereoCameraNative::onImageAvailable;
        status = AImageReader_setImageListener(side.reader, &listener);
        if (status != AMEDIA_OK) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "AImageReader_setImageListener failed");
        status = AImageReader_getWindow(side.reader, &side.readerWindow);
        if (status != AMEDIA_OK || !side.readerWindow) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "AImageReader_getWindow failed");
        return QRC_CAMERA_OK;
    }

    QrcCameraResult openSideLocked(SideState& side, const char* cameraId, bool isLeft) {
        QrcCameraResult result = createImageReaderLocked(side, isLeft);
        if (result != QRC_CAMERA_OK) return result;
        ACameraDevice_StateCallbacks deviceCallbacks{};
        deviceCallbacks.context = this;
        deviceCallbacks.onDisconnected = &StereoCameraNative::onDeviceDisconnected;
        deviceCallbacks.onError = &StereoCameraNative::onDeviceError;
        const camera_status_t status = ACameraManager_openCamera(manager_, cameraId, &deviceCallbacks, &side.device);
        if (status != ACAMERA_OK || !side.device) {
            if (status == ACAMERA_ERROR_PERMISSION_DENIED) return setError(QRC_CAMERA_ERROR_PERMISSION_DENIED, "Camera permission denied");
            return setError(QRC_CAMERA_ERROR_CAMERA_OPEN_FAILED, "ACameraManager_openCamera failed");
        }
        result = createCaptureSessionLocked(side);
        if (result != QRC_CAMERA_OK) return result;
        side.cameraId = cameraId;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult createCaptureSessionLocked(SideState& side) {
        camera_status_t status = ACameraDevice_createCaptureRequest(side.device, TEMPLATE_PREVIEW, &side.request);
        if (status != ACAMERA_OK || !side.request) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraDevice_createCaptureRequest failed");
        status = ACaptureSessionOutputContainer_create(&side.outputContainer);
        if (status != ACAMERA_OK || !side.outputContainer) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureSessionOutputContainer_create failed");
        status = ACaptureSessionOutput_create(side.readerWindow, &side.sessionOutput);
        if (status != ACAMERA_OK || !side.sessionOutput) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureSessionOutput_create failed");
        status = ACaptureSessionOutputContainer_add(side.outputContainer, side.sessionOutput);
        if (status != ACAMERA_OK) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureSessionOutputContainer_add failed");
        status = ACameraOutputTarget_create(side.readerWindow, &side.outputTarget);
        if (status != ACAMERA_OK || !side.outputTarget) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraOutputTarget_create failed");
        status = ACaptureRequest_addTarget(side.request, side.outputTarget);
        if (status != ACAMERA_OK) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureRequest_addTarget failed");
        ACameraCaptureSession_stateCallbacks sessionCallbacks{};
        sessionCallbacks.context = this;
        sessionCallbacks.onReady = &StereoCameraNative::onSessionReady;
        sessionCallbacks.onActive = &StereoCameraNative::onSessionActive;
        sessionCallbacks.onClosed = &StereoCameraNative::onSessionClosed;
        status = ACameraDevice_createCaptureSession(side.device, side.outputContainer, &sessionCallbacks, &side.session);
        if (status != ACAMERA_OK || !side.session) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraDevice_createCaptureSession failed");
        ACaptureRequest* requests[] = { side.request };
        status = ACameraCaptureSession_setRepeatingRequest(side.session, nullptr, 1, requests, nullptr);
        if (status != ACAMERA_OK) return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraCaptureSession_setRepeatingRequest failed");
        return QRC_CAMERA_OK;
    }

    void destroySideLocked(SideState& side) {
        if (side.session) {
            ACameraCaptureSession_stopRepeating(side.session);
            ACameraCaptureSession_close(side.session);
            side.session = nullptr;
        }
        if (side.request && side.outputTarget) ACaptureRequest_removeTarget(side.request, side.outputTarget);
        if (side.outputTarget) { ACameraOutputTarget_free(side.outputTarget); side.outputTarget = nullptr; }
        if (side.sessionOutput) { ACaptureSessionOutput_free(side.sessionOutput); side.sessionOutput = nullptr; }
        if (side.outputContainer) { ACaptureSessionOutputContainer_free(side.outputContainer); side.outputContainer = nullptr; }
        if (side.request) { ACaptureRequest_free(side.request); side.request = nullptr; }
        if (side.device) { ACameraDevice_close(side.device); side.device = nullptr; }
        if (side.reader) { AImageReader_delete(side.reader); side.reader = nullptr; side.readerWindow = nullptr; }
        side.cameraId.clear();
        side.formatInfoWritten = false;
    }

    static void onDeviceDisconnected(void* context, ACameraDevice*) {
        auto* self = static_cast<StereoCameraNative*>(context);
        std::lock_guard<std::mutex> lock(self->mutex_);
        self->lastError_ = "Stereo camera disconnected";
        QRC_STEREO_LOGW("Stereo camera disconnected");
    }

    static void onDeviceError(void* context, ACameraDevice*, int error) {
        auto* self = static_cast<StereoCameraNative*>(context);
        std::lock_guard<std::mutex> lock(self->mutex_);
        self->lastError_ = "Stereo camera device error: " + std::to_string(error);
        QRC_STEREO_LOGE("Stereo camera device error: %d", error);
    }

    static void onSessionReady(void*, ACameraCaptureSession*) {}
    static void onSessionActive(void*, ACameraCaptureSession*) {}
    static void onSessionClosed(void*, ACameraCaptureSession*) {}

    static void onImageAvailable(void* context, AImageReader* reader) {
        auto* readerContext = static_cast<ReaderContext*>(context);
        if (!readerContext || !readerContext->owner) return;
        readerContext->owner->handleImageAvailable(reader, readerContext->isLeft);
    }

    void handleImageAvailable(AImageReader* reader, bool isLeft) {
        AImage* image = nullptr;
        if (AImageReader_acquireNextImage(reader, &image) != AMEDIA_OK || !image) return;
        if (isLeft) ++leftReceivedFrameCount_; else ++rightReceivedFrameCount_;
        int64_t timestampNs = 0;
        AImage_getTimestamp(image, &timestampNs);
        if (isLeft) lastLeftImageTimestampNs_ = timestampNs; else lastRightImageTimestampNs_ = timestampNs;

        if (!recording_.load()) {
            ++droppedFrameCount_;
            AImage_delete(image);
            return;
        }

        CapturedFrame frame;
        frame.imageTimestampNs = timestampNs;
        frame.unixMs = baseUnixTimeMs_ + (timestampNs - baseMonoTimeNs_) / 1000000LL;
        if (!dumpImageAsCurrentFormat(image, frame.planes, frame.data)) {
            ++droppedFrameCount_;
            AImage_delete(image);
            return;
        }
        AImage_delete(image);

        std::lock_guard<std::mutex> lock(mutex_);
        matchOrQueueLocked(std::move(frame), isLeft);
    }

    bool dumpImageAsCurrentFormat(AImage* image, std::vector<PlaneInfo>& planeInfos, std::vector<uint8_t>& data) const {
        int planeCount = 0;
        if (AImage_getNumberOfPlanes(image, &planeCount) != AMEDIA_OK || planeCount <= 0) return false;
        planeInfos.reserve(static_cast<size_t>(planeCount));
        size_t totalSize = 0;
        for (int i = 0; i < planeCount; ++i) {
            PlaneInfo info{};
            AImage_getPlaneRowStride(image, i, &info.rowStride);
            AImage_getPlanePixelStride(image, i, &info.pixelStride);
            uint8_t* planeData = nullptr;
            int planeLength = 0;
            if (AImage_getPlaneData(image, i, &planeData, &planeLength) != AMEDIA_OK || !planeData || planeLength < 0) return false;
            info.bufferSize = planeLength;
            planeInfos.push_back(info);
            totalSize += static_cast<size_t>(planeLength);
        }
        data.resize(totalSize);
        size_t offset = 0;
        for (int i = 0; i < planeCount; ++i) {
            uint8_t* planeData = nullptr;
            int planeLength = 0;
            AImage_getPlaneData(image, i, &planeData, &planeLength);
            std::memcpy(data.data() + offset, planeData, static_cast<size_t>(planeLength));
            offset += static_cast<size_t>(planeLength);
        }
        return true;
    }

    void matchOrQueueLocked(CapturedFrame&& frame, bool isLeft) {
        auto& same = isLeft ? leftPending_ : rightPending_;
        auto& other = isLeft ? rightPending_ : leftPending_;
        if (other.empty()) {
            queuePendingLocked(same, std::move(frame));
            return;
        }

        size_t bestIndex = 0;
        int64_t bestDelta = llabs(frame.imageTimestampNs - other[0].imageTimestampNs);
        for (size_t i = 1; i < other.size(); ++i) {
            const int64_t delta = llabs(frame.imageTimestampNs - other[i].imageTimestampNs);
            if (delta < bestDelta) {
                bestDelta = delta;
                bestIndex = i;
            }
        }

        if (bestDelta > maxTimeDeltaNs_) {
            queuePendingLocked(same, std::move(frame));
            pruneOldFramesLocked(leftPending_);
            pruneOldFramesLocked(rightPending_);
            return;
        }

        CapturedFrame match = std::move(other[bestIndex]);
        other.erase(other.begin() + static_cast<std::ptrdiff_t>(bestIndex));
        CapturedFrame leftFrame = isLeft ? std::move(frame) : std::move(match);
        CapturedFrame rightFrame = isLeft ? std::move(match) : std::move(frame);
        savePairLocked(std::move(leftFrame), std::move(rightFrame), bestDelta);
    }

    void queuePendingLocked(std::deque<CapturedFrame>& queue, CapturedFrame&& frame) {
        queue.push_back(std::move(frame));
        while (queue.size() > kMaxPendingSideFrames) {
            queue.pop_front();
            ++droppedFrameCount_;
        }
    }

    void pruneOldFramesLocked(std::deque<CapturedFrame>& queue) {
        while (queue.size() > 1) {
            const int64_t newest = queue.back().imageTimestampNs;
            if (newest - queue.front().imageTimestampNs <= maxTimeDeltaNs_ * 2) return;
            queue.pop_front();
            ++droppedFrameCount_;
        }
    }

    bool shouldSavePair(int64_t pairTimestampNs) {
        const int fps = targetSaveFps_.load();
        if (fps <= 0) return true;
        const int64_t previous = lastAcceptedPairTimestampNs_.load();
        const int64_t minIntervalNs = 1000000000LL / fps;
        if (previous > 0 && pairTimestampNs - previous < minIntervalNs) return false;
        lastAcceptedPairTimestampNs_ = pairTimestampNs;
        return true;
    }

    void savePairLocked(CapturedFrame&& leftFrame, CapturedFrame&& rightFrame, int64_t deltaNs) {
        const int64_t pairTimestampNs = (leftFrame.imageTimestampNs + rightFrame.imageTimestampNs) / 2;
        if (!shouldSavePair(pairTimestampNs)) {
            ++droppedFrameCount_;
            return;
        }

        writeFormatInfoIfNeededLocked(left_, leftFrame.planes);
        writeFormatInfoIfNeededLocked(right_, rightFrame.planes);

        const int64_t pairIndex = savedPairCount_.load();
        std::ostringstream leftPath;
        std::ostringstream rightPath;
        leftPath << left_.frameDirectory << "/" << leftFrame.unixMs << ".yuv";
        rightPath << right_.frameDirectory << "/" << rightFrame.unixMs << ".yuv";
        const std::string leftPathString = leftPath.str();
        const std::string rightPathString = rightPath.str();

        if (!writer_.enqueue(FrameJob{leftPathString, std::move(leftFrame.data)})
            || !writer_.enqueue(FrameJob{rightPathString, std::move(rightFrame.data)})) {
            ++droppedFrameCount_;
            return;
        }

        std::ostringstream row;
        row << pairIndex << "," << leftFrame.imageTimestampNs << "," << rightFrame.imageTimestampNs << ","
            << deltaNs << "," << leftFrame.unixMs << "," << rightFrame.unixMs << ","
            << leftPathString << "," << rightPathString << "\n";
        if (!appendTextFile(pairCsvFilePath_, row.str())) {
            ++ioErrorCount_;
        }

        lastSavedLeftTimestampNs_ = leftFrame.imageTimestampNs;
        lastSavedRightTimestampNs_ = rightFrame.imageTimestampNs;
        lastSavedDeltaNs_ = deltaNs;
        ++savedPairCount_;
    }

    void writeFormatInfoIfNeededLocked(SideState& side, const std::vector<PlaneInfo>& planes) {
        if (side.formatInfoWritten.exchange(true)) return;
        if (!writeTextFile(side.formatInfoFilePath, buildFormatInfoJson(planes))) {
            QRC_STEREO_LOGE("Failed to write stereo format info: %s", side.formatInfoFilePath.c_str());
            ++ioErrorCount_;
        }
    }

    std::string buildFormatInfoJson(const std::vector<PlaneInfo>& planes) const {
        std::ostringstream os;
        os << "{";
        os << "\"width\":" << width_ << ",";
        os << "\"height\":" << height_ << ",";
        os << "\"format\":\"YUV_420_888\",";
        os << "\"planes\":[";
        for (size_t i = 0; i < planes.size(); ++i) {
            if (i > 0) os << ",";
            os << "{";
            os << "\"rowStride\":" << planes[i].rowStride << ",";
            os << "\"pixelStride\":" << planes[i].pixelStride << ",";
            os << "\"bufferSize\":" << planes[i].bufferSize;
            os << "}";
        }
        os << "],";
        os << "\"baseTime\":{";
        os << "\"baseMonoTimeNs\":" << baseMonoTimeNs_ << ",";
        os << "\"baseUnixTimeMs\":" << baseUnixTimeMs_;
        os << "},";
        os << "\"stereoMaxTimeDeltaNs\":" << maxTimeDeltaNs_;
        os << "}";
        return os.str();
    }

    mutable std::mutex mutex_;
    State state_ = State::Uninitialized;
    ACameraManager* manager_ = nullptr;
    SideState left_;
    SideState right_;
    ReaderContext leftReaderContext_{this, true};
    ReaderContext rightReaderContext_{this, false};
    std::deque<CapturedFrame> leftPending_;
    std::deque<CapturedFrame> rightPending_;
    FrameWriter writer_;

    int width_ = 0;
    int height_ = 0;
    int64_t baseMonoTimeNs_ = 0;
    int64_t baseUnixTimeMs_ = 0;
    int64_t maxTimeDeltaNs_ = kDefaultMaxTimeDeltaNs;
    std::string pairCsvFilePath_;
    std::string lastError_;

    std::atomic<bool> recording_{false};
    std::atomic<int> targetSaveFps_{0};
    std::atomic<int64_t> leftReceivedFrameCount_{0};
    std::atomic<int64_t> rightReceivedFrameCount_{0};
    std::atomic<int64_t> savedPairCount_{0};
    std::atomic<int64_t> droppedFrameCount_{0};
    std::atomic<int64_t> ioErrorCount_{0};
    std::atomic<int64_t> lastLeftImageTimestampNs_{0};
    std::atomic<int64_t> lastRightImageTimestampNs_{0};
    std::atomic<int64_t> lastSavedLeftTimestampNs_{0};
    std::atomic<int64_t> lastSavedRightTimestampNs_{0};
    std::atomic<int64_t> lastSavedDeltaNs_{0};
    std::atomic<int64_t> lastAcceptedPairTimestampNs_{0};
};

const char* kInvalidStereoSessionHandleError = "Invalid stereo camera session handle";

StereoCameraNative* stereoSessionFromHandle(QrcStereoCameraSessionHandle handle) {
    return static_cast<StereoCameraNative*>(handle);
}

QrcCameraResult requireStereoSession(QrcStereoCameraSessionHandle handle, StereoCameraNative** outSession) {
    if (!handle || !outSession) return QRC_CAMERA_ERROR_INVALID_ARGUMENT;
    *outSession = stereoSessionFromHandle(handle);
    return *outSession ? QRC_CAMERA_OK : QRC_CAMERA_ERROR_INVALID_ARGUMENT;
}

} // namespace

extern "C" QrcCameraResult QrcStereoCamera_CreateSession(QrcStereoCameraSessionHandle* outHandle) {
    if (!outHandle) return QRC_CAMERA_ERROR_INVALID_ARGUMENT;
    auto* session = new (std::nothrow) StereoCameraNative();
    if (!session) {
        *outHandle = nullptr;
        return QRC_CAMERA_ERROR_IO;
    }
    *outHandle = static_cast<QrcStereoCameraSessionHandle>(session);
    return QRC_CAMERA_OK;
}

extern "C" QrcCameraResult QrcStereoCamera_DestroySession(QrcStereoCameraSessionHandle handle) {
    if (!handle) return QRC_CAMERA_ERROR_INVALID_ARGUMENT;
    delete stereoSessionFromHandle(handle);
    return QRC_CAMERA_OK;
}

extern "C" QrcCameraResult QrcStereoCamera_InitializeSession(
    QrcStereoCameraSessionHandle handle,
    int width,
    int height,
    const char* leftFrameDirectory,
    const char* rightFrameDirectory,
    const char* leftFormatInfoFilePath,
    const char* rightFormatInfoFilePath,
    const char* pairCsvFilePath,
    int64_t maxTimeDeltaNs) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK
        ? session->initialize(width, height, leftFrameDirectory, rightFrameDirectory,
            leftFormatInfoFilePath, rightFormatInfoFilePath, pairCsvFilePath, maxTimeDeltaNs)
        : result;
}

extern "C" QrcCameraResult QrcStereoCamera_SetSessionSaveFrameRate(QrcStereoCameraSessionHandle handle, int fps) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->setSaveFrameRate(fps) : result;
}

extern "C" QrcCameraResult QrcStereoCamera_OpenSession(QrcStereoCameraSessionHandle handle) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->openByPositions() : result;
}

extern "C" QrcCameraResult QrcStereoCamera_OpenSessionByIds(
    QrcStereoCameraSessionHandle handle,
    const char* leftCameraId,
    const char* rightCameraId) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->openByIds(leftCameraId, rightCameraId) : result;
}

extern "C" QrcCameraResult QrcStereoCamera_StartSessionRecording(QrcStereoCameraSessionHandle handle) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->startRecording() : result;
}

extern "C" QrcCameraResult QrcStereoCamera_StopSessionRecording(QrcStereoCameraSessionHandle handle) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->stopRecording() : result;
}

extern "C" QrcCameraResult QrcStereoCamera_CloseSession(QrcStereoCameraSessionHandle handle) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->close() : result;
}

extern "C" QrcCameraResult QrcStereoCamera_GetSessionStats(QrcStereoCameraSessionHandle handle, QrcStereoCameraStats* outStats) {
    StereoCameraNative* session = nullptr;
    QrcCameraResult result = requireStereoSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->getStats(outStats) : result;
}

extern "C" const char* QrcStereoCamera_GetSessionLastError(QrcStereoCameraSessionHandle handle) {
    StereoCameraNative* session = stereoSessionFromHandle(handle);
    return session ? session->lastError() : kInvalidStereoSessionHandleError;
}

extern "C" const char* QrcStereoCamera_GetSessionLastOpenedLeftCameraId(QrcStereoCameraSessionHandle handle) {
    StereoCameraNative* session = stereoSessionFromHandle(handle);
    return session ? session->lastOpenedLeftCameraId() : "";
}

extern "C" const char* QrcStereoCamera_GetSessionLastOpenedRightCameraId(QrcStereoCameraSessionHandle handle) {
    StereoCameraNative* session = stereoSessionFromHandle(handle);
    return session ? session->lastOpenedRightCameraId() : "";
}
