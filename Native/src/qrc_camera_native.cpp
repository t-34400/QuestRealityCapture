#include "qrc_camera_native.h"

#include <android/log.h>
#include <camera/NdkCameraDevice.h>
#include <camera/NdkCameraManager.h>
#include <media/NdkImage.h>
#include <media/NdkImageReader.h>

#include <atomic>
#include <cerrno>
#include <condition_variable>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <deque>
#include <fstream>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#define QRC_LOG_TAG "QrcCameraNative"
#define QRC_LOGI(...) __android_log_print(ANDROID_LOG_INFO, QRC_LOG_TAG, __VA_ARGS__)
#define QRC_LOGW(...) __android_log_print(ANDROID_LOG_WARN, QRC_LOG_TAG, __VA_ARGS__)
#define QRC_LOGE(...) __android_log_print(ANDROID_LOG_ERROR, QRC_LOG_TAG, __VA_ARGS__)

namespace {

constexpr int kImageReaderMaxImages = 2;
constexpr size_t kMaxPendingFrames = 4;

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
    if (pos == std::string::npos) {
        return {};
    }
    if (pos == 0) {
        return "/";
    }
    return path.substr(0, pos);
}

bool makeDirectories(const std::string& path) {
    if (path.empty()) {
        return true;
    }

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
            if (current.size() > 1 && current.back() != '/') {
                current += '/';
            }
            current += part;
            if (mkdir(current.c_str(), 0775) != 0 && errno != EEXIST) {
                return false;
            }
        }
        if (next == std::string::npos) {
            break;
        }
        i = next + 1;
    }
    return true;
}

bool writeBinaryFile(const std::string& path, const std::vector<uint8_t>& data) {
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) {
        return false;
    }
    out.write(reinterpret_cast<const char*>(data.data()), static_cast<std::streamsize>(data.size()));
    return out.good();
}

bool writeTextFile(const std::string& path, const std::string& text) {
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) {
        return false;
    }
    out << text;
    return out.good();
}

std::string escapeJson(const std::string& value) {
    std::ostringstream os;
    for (char c : value) {
        switch (c) {
            case '"': os << "\\\""; break;
            case '\\': os << "\\\\"; break;
            case '\n': os << "\\n"; break;
            case '\r': os << "\\r"; break;
            case '\t': os << "\\t"; break;
            default: os << c; break;
        }
    }
    return os.str();
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
    FrameWriter() = default;

    ~FrameWriter() {
        stop();
    }

    void start() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (running_) {
            return;
        }
        stopping_ = false;
        running_ = true;
        worker_ = std::thread([this] { run(); });
    }

    void stop() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!running_) {
                return;
            }
            stopping_ = true;
        }
        cv_.notify_all();
        if (worker_.joinable()) {
            worker_.join();
        }
        std::lock_guard<std::mutex> lock(mutex_);
        running_ = false;
        queue_.clear();
    }

    bool enqueue(FrameJob&& job) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!running_ || stopping_ || queue_.size() >= kMaxPendingFrames) {
            return false;
        }
        queue_.push_back(std::move(job));
        cv_.notify_one();
        return true;
    }

private:
    void run() {
        for (;;) {
            FrameJob job;
            {
                std::unique_lock<std::mutex> lock(mutex_);
                cv_.wait(lock, [this] { return stopping_ || !queue_.empty(); });
                if (stopping_ && queue_.empty()) {
                    return;
                }
                job = std::move(queue_.front());
                queue_.pop_front();
            }
            if (!writeBinaryFile(job.path, job.data)) {
                QRC_LOGE("Failed to write frame: %s", job.path.c_str());
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

class CameraNative {
public:
    ~CameraNative() {
        close();
        if (manager_) {
            ACameraManager_delete(manager_);
        }
    }

    QrcCameraResult initialize(int width, int height, const char* frameDirectory, const char* formatInfoFilePath) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (width <= 0 || height <= 0 || frameDirectory == nullptr || formatInfoFilePath == nullptr) {
            return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "Invalid initialize arguments");
        }
        if (state_ != State::Uninitialized) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Already initialized");
        }
        if (!makeDirectories(frameDirectory) || !makeDirectories(parentDirectoryOf(formatInfoFilePath))) {
            return setError(QRC_CAMERA_ERROR_IO, "Failed to create output directories");
        }

        manager_ = ACameraManager_create();
        if (!manager_) {
            return setError(QRC_CAMERA_ERROR_CAMERA_OPEN_FAILED, "ACameraManager_create failed");
        }

        width_ = width;
        height_ = height;
        frameDirectory_ = frameDirectory;
        formatInfoFilePath_ = formatInfoFilePath;
        baseMonoTimeNs_ = nowMonotonicNs();
        baseUnixTimeMs_ = nowUnixMs();
        state_ = State::Initialized;
        lastError_.clear();
        return QRC_CAMERA_OK;
    }

    QrcCameraResult setSaveFrameRate(int fps) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (fps < 0) {
            return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "fps must be >= 0");
        }
        targetSaveFps_ = fps;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult openByPosition(QrcCameraPosition position) {
        std::string id;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!manager_) {
                return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Not initialized");
            }
            auto ids = listCameraIdsLocked();
            if (ids.empty()) {
                return setError(QRC_CAMERA_ERROR_CAMERA_NOT_FOUND, "No camera IDs available");
            }
            const size_t index = position == QRC_CAMERA_RIGHT ? 1 : 0;
            if (index >= ids.size()) {
                std::ostringstream os;
                os << "Requested position index " << index << " but only " << ids.size() << " camera(s) found";
                return setError(QRC_CAMERA_ERROR_CAMERA_NOT_FOUND, os.str());
            }
            id = ids[index];
        }
        QRC_LOGW("QrcCamera_Open uses camera-id order fallback. Prefer QrcCamera_OpenById when exact Quest left/right IDs are known.");
        return openById(id.c_str());
    }

    QrcCameraResult openById(const char* cameraId) {
        std::unique_lock<std::mutex> lock(mutex_);
        if (cameraId == nullptr || cameraId[0] == '\0') {
            return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "cameraId is empty");
        }
        if (state_ != State::Initialized) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Camera must be initialized and closed before open");
        }

        QrcCameraResult readerResult = createImageReaderLocked();
        if (readerResult != QRC_CAMERA_OK) {
            return readerResult;
        }

        ACameraDevice_StateCallbacks callbacks{};
        callbacks.context = this;
        callbacks.onDisconnected = &CameraNative::onDeviceDisconnected;
        callbacks.onError = &CameraNative::onDeviceError;

        const camera_status_t status = ACameraManager_openCamera(manager_, cameraId, &callbacks, &device_);
        if (status != ACAMERA_OK || !device_) {
            destroyImageReaderLocked();
            if (status == ACAMERA_ERROR_PERMISSION_DENIED) {
                return setError(QRC_CAMERA_ERROR_PERMISSION_DENIED, "Camera permission denied");
            }
            return setError(QRC_CAMERA_ERROR_CAMERA_OPEN_FAILED, "ACameraManager_openCamera failed");
        }

        QrcCameraResult sessionResult = createSessionLocked();
        if (sessionResult != QRC_CAMERA_OK) {
            destroyDeviceLocked();
            destroyImageReaderLocked();
            return sessionResult;
        }

        lastOpenedCameraId_ = cameraId;
        state_ = State::Opened;
        lastError_.clear();
        return QRC_CAMERA_OK;
    }

    QrcCameraResult startRecording() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ != State::Opened && state_ != State::Recording) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Camera is not opened");
        }
        writer_.start();
        recording_ = true;
        state_ = State::Recording;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult stopRecording() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ != State::Opened && state_ != State::Recording) {
            return setError(QRC_CAMERA_ERROR_INVALID_STATE, "Camera is not opened");
        }
        recording_ = false;
        writer_.stop();
        state_ = State::Opened;
        return QRC_CAMERA_OK;
    }

    QrcCameraResult close() {
        std::lock_guard<std::mutex> lock(mutex_);
        recording_ = false;
        writer_.stop();
        destroySessionLocked();
        destroyDeviceLocked();
        destroyImageReaderLocked();
        if (state_ != State::Uninitialized) {
            state_ = State::Initialized;
        }
        return QRC_CAMERA_OK;
    }

    QrcCameraResult getStats(QrcCameraStats* outStats) {
        if (!outStats) {
            return setError(QRC_CAMERA_ERROR_INVALID_ARGUMENT, "outStats is null");
        }
        outStats->receivedFrameCount = receivedFrameCount_.load();
        outStats->savedFrameCount = savedFrameCount_.load();
        outStats->droppedFrameCount = droppedFrameCount_.load();
        outStats->ioErrorCount = 0;
        outStats->lastImageTimestampNs = lastImageTimestampNs_.load();
        outStats->lastSavedTimestampNs = lastSavedTimestampNs_.load();
        return QRC_CAMERA_OK;
    }

    const char* lastError() const {
        return lastError_.c_str();
    }

    const char* lastOpenedCameraId() const {
        return lastOpenedCameraId_.c_str();
    }

    const char* cameraIdListJson() {
        std::lock_guard<std::mutex> lock(mutex_);
        cameraIdListJsonCache_ = buildCameraIdListJsonLocked();
        return cameraIdListJsonCache_.c_str();
    }

private:
    enum class State {
        Uninitialized,
        Initialized,
        Opened,
        Recording
    };

    QrcCameraResult setError(QrcCameraResult result, const std::string& message) {
        lastError_ = message;
        QRC_LOGE("%s", message.c_str());
        return result;
    }

    std::vector<std::string> listCameraIdsLocked() {
        std::vector<std::string> ids;
        ACameraIdList* idList = nullptr;
        if (ACameraManager_getCameraIdList(manager_, &idList) != ACAMERA_OK || idList == nullptr) {
            return ids;
        }
        for (int i = 0; i < idList->numCameras; ++i) {
            ids.emplace_back(idList->cameraIds[i]);
        }
        ACameraManager_deleteCameraIdList(idList);
        return ids;
    }

    std::string buildCameraIdListJsonLocked() {
        std::ostringstream os;
        os << "[";
        auto ids = listCameraIdsLocked();
        for (size_t i = 0; i < ids.size(); ++i) {
            if (i > 0) os << ",";
            os << "{\"cameraId\":\"" << escapeJson(ids[i]) << "\"";

            ACameraMetadata* metadata = nullptr;
            if (ACameraManager_getCameraCharacteristics(manager_, ids[i].c_str(), &metadata) == ACAMERA_OK && metadata) {
                ACameraMetadata_const_entry entry{};
                if (ACameraMetadata_getConstEntry(metadata, ACAMERA_LENS_FACING, &entry) == ACAMERA_OK && entry.count > 0) {
                    os << ",\"lensFacing\":" << static_cast<int>(entry.data.u8[0]);
                }
                if (ACameraMetadata_getConstEntry(metadata, ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL, &entry) == ACAMERA_OK && entry.count > 0) {
                    os << ",\"hardwareLevel\":" << static_cast<int>(entry.data.u8[0]);
                }
                ACameraMetadata_free(metadata);
            }
            os << "}";
        }
        os << "]";
        return os.str();
    }

    QrcCameraResult createImageReaderLocked() {
        media_status_t status = AImageReader_new(width_, height_, AIMAGE_FORMAT_YUV_420_888, kImageReaderMaxImages, &reader_);
        if (status != AMEDIA_OK || !reader_) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "AImageReader_new failed");
        }
        AImageReader_ImageListener listener{};
        listener.context = this;
        listener.onImageAvailable = &CameraNative::onImageAvailable;
        status = AImageReader_setImageListener(reader_, &listener);
        if (status != AMEDIA_OK) {
            destroyImageReaderLocked();
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "AImageReader_setImageListener failed");
        }
        status = AImageReader_getWindow(reader_, &readerWindow_);
        if (status != AMEDIA_OK || !readerWindow_) {
            destroyImageReaderLocked();
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "AImageReader_getWindow failed");
        }
        return QRC_CAMERA_OK;
    }

    QrcCameraResult createSessionLocked() {
        camera_status_t status = ACameraDevice_createCaptureRequest(device_, TEMPLATE_PREVIEW, &request_);
        if (status != ACAMERA_OK || !request_) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraDevice_createCaptureRequest failed");
        }

        status = ACaptureSessionOutputContainer_create(&outputContainer_);
        if (status != ACAMERA_OK || !outputContainer_) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureSessionOutputContainer_create failed");
        }
        status = ACaptureSessionOutput_create(readerWindow_, &sessionOutput_);
        if (status != ACAMERA_OK || !sessionOutput_) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureSessionOutput_create failed");
        }
        status = ACaptureSessionOutputContainer_add(outputContainer_, sessionOutput_);
        if (status != ACAMERA_OK) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureSessionOutputContainer_add failed");
        }
        status = ACameraOutputTarget_create(readerWindow_, &outputTarget_);
        if (status != ACAMERA_OK || !outputTarget_) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraOutputTarget_create failed");
        }
        status = ACaptureRequest_addTarget(request_, outputTarget_);
        if (status != ACAMERA_OK) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACaptureRequest_addTarget failed");
        }

        sessionReady_ = false;
        ACameraCaptureSession_stateCallbacks callbacks{};
        callbacks.context = this;
        callbacks.onReady = &CameraNative::onSessionReady;
        callbacks.onActive = &CameraNative::onSessionActive;
        callbacks.onClosed = &CameraNative::onSessionClosed;
        status = ACameraDevice_createCaptureSession(device_, outputContainer_, &callbacks, &session_);
        if (status != ACAMERA_OK || !session_) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraDevice_createCaptureSession failed");
        }

        ACaptureRequest* requests[] = { request_ };
        status = ACameraCaptureSession_setRepeatingRequest(session_, nullptr, 1, requests, nullptr);
        if (status != ACAMERA_OK) {
            return setError(QRC_CAMERA_ERROR_SESSION_FAILED, "ACameraCaptureSession_setRepeatingRequest failed");
        }
        return QRC_CAMERA_OK;
    }

    void destroySessionLocked() {
        if (session_) {
            ACameraCaptureSession_stopRepeating(session_);
            ACameraCaptureSession_close(session_);
            session_ = nullptr;
        }
        if (request_ && outputTarget_) {
            ACaptureRequest_removeTarget(request_, outputTarget_);
        }
        if (outputTarget_) {
            ACameraOutputTarget_free(outputTarget_);
            outputTarget_ = nullptr;
        }
        if (sessionOutput_) {
            ACaptureSessionOutput_free(sessionOutput_);
            sessionOutput_ = nullptr;
        }
        if (outputContainer_) {
            ACaptureSessionOutputContainer_free(outputContainer_);
            outputContainer_ = nullptr;
        }
        if (request_) {
            ACaptureRequest_free(request_);
            request_ = nullptr;
        }
    }

    void destroyDeviceLocked() {
        if (device_) {
            ACameraDevice_close(device_);
            device_ = nullptr;
        }
    }

    void destroyImageReaderLocked() {
        if (reader_) {
            AImageReader_delete(reader_);
            reader_ = nullptr;
            readerWindow_ = nullptr;
        }
        formatInfoWritten_ = false;
    }

    static void onDeviceDisconnected(void* context, ACameraDevice*) {
        auto* self = static_cast<CameraNative*>(context);
        QRC_LOGW("Camera disconnected");
        std::lock_guard<std::mutex> lock(self->mutex_);
        self->lastError_ = "Camera disconnected";
    }

    static void onDeviceError(void* context, ACameraDevice*, int error) {
        auto* self = static_cast<CameraNative*>(context);
        QRC_LOGE("Camera device error: %d", error);
        std::lock_guard<std::mutex> lock(self->mutex_);
        self->lastError_ = "Camera device error: " + std::to_string(error);
    }

    static void onSessionReady(void* context, ACameraCaptureSession*) {
        auto* self = static_cast<CameraNative*>(context);
        std::lock_guard<std::mutex> lock(self->mutex_);
        self->sessionReady_ = true;
    }

    static void onSessionActive(void*, ACameraCaptureSession*) {}
    static void onSessionClosed(void*, ACameraCaptureSession*) {}

    static void onImageAvailable(void* context, AImageReader* reader) {
        auto* self = static_cast<CameraNative*>(context);
        self->handleImageAvailable(reader);
    }

    void handleImageAvailable(AImageReader* reader) {
        AImage* image = nullptr;
        if (AImageReader_acquireNextImage(reader, &image) != AMEDIA_OK || image == nullptr) {
            return;
        }

        ++receivedFrameCount_;

        int64_t imageTimestampNs = 0;
        AImage_getTimestamp(image, &imageTimestampNs);
        lastImageTimestampNs_ = imageTimestampNs;

        const bool shouldRecord = recording_.load();
        if (!shouldRecord || !shouldSaveTimestamp(imageTimestampNs)) {
            ++droppedFrameCount_;
            AImage_delete(image);
            return;
        }

        std::vector<PlaneInfo> planeInfos;
        std::vector<uint8_t> data;
        if (!dumpImageAsCurrentFormat(image, planeInfos, data)) {
            ++droppedFrameCount_;
            AImage_delete(image);
            return;
        }

        if (!formatInfoWritten_.exchange(true)) {
            const std::string json = buildFormatInfoJson(planeInfos);
            if (!writeTextFile(formatInfoFilePath_, json)) {
                QRC_LOGE("Failed to write format info: %s", formatInfoFilePath_.c_str());
            }
        }

        const int64_t unixMs = baseUnixTimeMs_ + (imageTimestampNs - baseMonoTimeNs_) / 1000000LL;
        std::ostringstream path;
        path << frameDirectory_ << "/" << unixMs << ".yuv";

        FrameJob job{path.str(), std::move(data)};
        if (writer_.enqueue(std::move(job))) {
            lastSavedTimestampNs_ = imageTimestampNs;
            ++savedFrameCount_;
        } else {
            ++droppedFrameCount_;
        }

        AImage_delete(image);
    }

    bool shouldSaveTimestamp(int64_t imageTimestampNs) {
        const int fps = targetSaveFps_.load();
        if (fps <= 0) {
            return true;
        }
        const int64_t previous = lastAcceptedTimestampNs_.load();
        const int64_t minIntervalNs = 1000000000LL / fps;
        if (previous > 0 && imageTimestampNs - previous < minIntervalNs) {
            return false;
        }
        lastAcceptedTimestampNs_ = imageTimestampNs;
        return true;
    }

    bool dumpImageAsCurrentFormat(AImage* image, std::vector<PlaneInfo>& planeInfos, std::vector<uint8_t>& data) {
        int planeCount = 0;
        if (AImage_getNumberOfPlanes(image, &planeCount) != AMEDIA_OK || planeCount <= 0) {
            return false;
        }

        planeInfos.reserve(static_cast<size_t>(planeCount));
        size_t totalSize = 0;
        for (int i = 0; i < planeCount; ++i) {
            PlaneInfo info{};
            AImage_getPlaneRowStride(image, i, &info.rowStride);
            AImage_getPlanePixelStride(image, i, &info.pixelStride);
            uint8_t* planeData = nullptr;
            int planeLength = 0;
            if (AImage_getPlaneData(image, i, &planeData, &planeLength) != AMEDIA_OK || planeData == nullptr || planeLength < 0) {
                return false;
            }
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
        os << "}";
        os << "}";
        return os.str();
    }

    mutable std::mutex mutex_;
    State state_ = State::Uninitialized;
    ACameraManager* manager_ = nullptr;
    ACameraDevice* device_ = nullptr;
    ACameraCaptureSession* session_ = nullptr;
    ACaptureRequest* request_ = nullptr;
    ACaptureSessionOutputContainer* outputContainer_ = nullptr;
    ACaptureSessionOutput* sessionOutput_ = nullptr;
    ACameraOutputTarget* outputTarget_ = nullptr;
    AImageReader* reader_ = nullptr;
    ANativeWindow* readerWindow_ = nullptr;

    int width_ = 0;
    int height_ = 0;
    std::string frameDirectory_;
    std::string formatInfoFilePath_;
    std::string lastError_;
    std::string lastOpenedCameraId_;
    std::string cameraIdListJsonCache_;

    int64_t baseMonoTimeNs_ = 0;
    int64_t baseUnixTimeMs_ = 0;
    bool sessionReady_ = false;

    std::atomic<bool> recording_{false};
    std::atomic<bool> formatInfoWritten_{false};
    std::atomic<int> targetSaveFps_{0};
    std::atomic<int64_t> receivedFrameCount_{0};
    std::atomic<int64_t> savedFrameCount_{0};
    std::atomic<int64_t> droppedFrameCount_{0};
    std::atomic<int64_t> lastImageTimestampNs_{0};
    std::atomic<int64_t> lastSavedTimestampNs_{0};
    std::atomic<int64_t> lastAcceptedTimestampNs_{0};
    FrameWriter writer_;
};

std::unique_ptr<CameraNative> gCamera;
std::mutex gMutex;

CameraNative* camera() {
    std::lock_guard<std::mutex> lock(gMutex);
    if (!gCamera) {
        gCamera = std::make_unique<CameraNative>();
    }
    return gCamera.get();
}

} // namespace

extern "C" QrcCameraResult QrcCamera_Initialize(
    int width,
    int height,
    const char* frameDirectory,
    const char* formatInfoFilePath) {
    return camera()->initialize(width, height, frameDirectory, formatInfoFilePath);
}

extern "C" QrcCameraResult QrcCamera_SetSaveFrameRate(int fps) {
    return camera()->setSaveFrameRate(fps);
}

extern "C" QrcCameraResult QrcCamera_Open(QrcCameraPosition position) {
    return camera()->openByPosition(position);
}

extern "C" QrcCameraResult QrcCamera_OpenById(const char* cameraId) {
    return camera()->openById(cameraId);
}

extern "C" QrcCameraResult QrcCamera_StartRecording(void) {
    return camera()->startRecording();
}

extern "C" QrcCameraResult QrcCamera_StopRecording(void) {
    return camera()->stopRecording();
}

extern "C" QrcCameraResult QrcCamera_Close(void) {
    return camera()->close();
}

extern "C" QrcCameraResult QrcCamera_GetStats(QrcCameraStats* outStats) {
    return camera()->getStats(outStats);
}

extern "C" const char* QrcCamera_GetLastError(void) {
    return camera()->lastError();
}

extern "C" const char* QrcCamera_GetLastOpenedCameraId(void) {
    return camera()->lastOpenedCameraId();
}

extern "C" const char* QrcCamera_GetCameraIdListJson(void) {
    return camera()->cameraIdListJson();
}
