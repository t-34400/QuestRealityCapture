#include "qrc_camera_native.h"

#include <android/log.h>
#include <camera/NdkCameraDevice.h>
#include <camera/NdkCameraManager.h>
#include <camera/NdkCameraMetadataTags.h>
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

#define QRC_LOG_TAG "QrcCameraNative"
#define QRC_LOGI(...) __android_log_print(ANDROID_LOG_INFO, QRC_LOG_TAG, __VA_ARGS__)
#define QRC_LOGW(...) __android_log_print(ANDROID_LOG_WARN, QRC_LOG_TAG, __VA_ARGS__)
#define QRC_LOGE(...) __android_log_print(ANDROID_LOG_ERROR, QRC_LOG_TAG, __VA_ARGS__)

namespace {

constexpr int kImageReaderMaxImages = 2;
constexpr size_t kMaxPendingFrames = 4;
constexpr uint32_t kQuestCameraSourceTag = 0x80004d00u;
constexpr uint32_t kQuestCameraPositionTag = 0x80004d01u;


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

bool getEntry(ACameraMetadata* metadata, uint32_t tag, ACameraMetadata_const_entry& entry) {
    return metadata != nullptr && ACameraMetadata_getConstEntry(metadata, tag, &entry) == ACAMERA_OK;
}

const char* lensFacingName(uint8_t value) {
    switch (value) {
        case ACAMERA_LENS_FACING_FRONT: return "FRONT";
        case ACAMERA_LENS_FACING_BACK: return "BACK";
        case ACAMERA_LENS_FACING_EXTERNAL: return "EXTERNAL";
        default: return "UNKNOWN";
    }
}

const char* hardwareLevelName(uint8_t value) {
    switch (value) {
        case ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL_LIMITED: return "LIMITED";
        case ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL_FULL: return "FULL";
        case ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL_LEGACY: return "LEGACY";
        case ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL_3: return "LEVEL_3";
        case ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL_EXTERNAL: return "EXTERNAL";
        default: return "UNKNOWN";
    }
}

const char* poseReferenceName(uint8_t value) {
    switch (value) {
        case ACAMERA_LENS_POSE_REFERENCE_PRIMARY_CAMERA: return "PRIMARY_CAMERA";
        case ACAMERA_LENS_POSE_REFERENCE_GYROSCOPE: return "GYROSCOPE";
        default: return "UNKNOWN";
    }
}

const char* timestampSourceName(uint8_t value) {
    switch (value) {
        case ACAMERA_SENSOR_INFO_TIMESTAMP_SOURCE_UNKNOWN: return "UNKNOWN";
        case ACAMERA_SENSOR_INFO_TIMESTAMP_SOURCE_REALTIME: return "REALTIME";
        default: return "UNKNOWN";
    }
}

const char* metadataTypeName(uint8_t value) {
    switch (value) {
        case ACAMERA_TYPE_BYTE: return "BYTE";
        case ACAMERA_TYPE_INT32: return "INT32";
        case ACAMERA_TYPE_FLOAT: return "FLOAT";
        case ACAMERA_TYPE_INT64: return "INT64";
        case ACAMERA_TYPE_DOUBLE: return "DOUBLE";
        case ACAMERA_TYPE_RATIONAL: return "RATIONAL";
        default: return "UNKNOWN";
    }
}

template <typename T>
void writeNumericArray(std::ostringstream& os, const T* values, uint32_t count) {
    os << "[";
    for (uint32_t i = 0; i < count; ++i) {
        if (i > 0) {
            os << ",";
        }
        os << values[i];
    }
    os << "]";
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

    int64_t ioErrorCount() const {
        return ioErrors_.load();
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

        if (!ensureManagerLocked()) {
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
            if (!findPassthroughCameraIdForPositionLocked(position, id)) {
                const int requestedPosition = position == QRC_CAMERA_RIGHT ? 1 : 0;
                std::ostringstream os;
                os << "No Quest passthrough camera found for position " << requestedPosition
                   << ". Required vendor tags were not available or did not identify a matching camera.";
                return setError(QRC_CAMERA_ERROR_CAMERA_NOT_FOUND, os.str());
            }
        }
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
        outStats->ioErrorCount = writer_.ioErrorCount();
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
        if (!ensureManagerLocked()) {
            cameraIdListJsonCache_ = "[]";
            return cameraIdListJsonCache_.c_str();
        }
        cameraIdListJsonCache_ = buildCameraIdListJsonLocked();
        return cameraIdListJsonCache_.c_str();
    }

    const char* cameraMetadataJson(QrcCameraPosition position) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!ensureManagerLocked()) {
            cameraMetadataJsonCache_ = "";
            return cameraMetadataJsonCache_.c_str();
        }
        cameraMetadataJsonCache_ = buildCameraMetadataJsonForPositionLocked(position);
        return cameraMetadataJsonCache_.c_str();
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

    bool ensureManagerLocked() {
        if (manager_) {
            return true;
        }
        manager_ = ACameraManager_create();
        return manager_ != nullptr;
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
            os << buildCameraMetadataJsonLocked(ids[i], static_cast<int>(i));
        }
        os << "]";
        return os.str();
    }

    std::string buildCameraMetadataJsonForPositionLocked(QrcCameraPosition position) {
        std::string id;
        int cameraListIndex = -1;
        if (!findPassthroughCameraIdForPositionLocked(position, id, &cameraListIndex)) {
            QRC_LOGE("No Quest passthrough camera metadata found for requested position %d", position == QRC_CAMERA_RIGHT ? 1 : 0);
            return {};
        }
        return buildCameraMetadataJsonLocked(id, cameraListIndex);
    }

    std::string buildCameraMetadataJsonLocked(
        const std::string& cameraId,
        int cameraListIndex,
        int forcedPosition = -1,
        int forcedSource = -1) {
        ACameraMetadata* metadata = nullptr;
        if (ACameraManager_getCameraCharacteristics(manager_, cameraId.c_str(), &metadata) != ACAMERA_OK || !metadata) {
            return "{}";
        }

        int cameraPositionId = forcedPosition;
        int cameraSource = forcedSource;
        if (cameraPositionId < 0 || cameraSource < 0) {
            (void)tryReadQuestVendorHints(metadata, cameraPositionId, cameraSource);
        }
        ACameraMetadata_const_entry entry{};
        std::string lensFacing = "UNKNOWN";
        if (getEntry(metadata, ACAMERA_LENS_FACING, entry)) {
            lensFacing = lensFacingName(entry.data.u8[0]);
        }

        std::string hardwareLevel = "UNKNOWN";
        if (getEntry(metadata, ACAMERA_INFO_SUPPORTED_HARDWARE_LEVEL, entry)) {
            hardwareLevel = hardwareLevelName(entry.data.u8[0]);
        }

        std::ostringstream os;
        os << "{";
        os << "\"cameraId\":\"" << escapeJson(cameraId) << "\",";
        os << "\"cameraSource\":" << cameraSource << ",";
        os << "\"cameraPositionId\":" << cameraPositionId << ",";
        os << "\"lensFacing\":\"" << lensFacing << "\",";
        os << "\"hardwareLevel\":\"" << hardwareLevel << "\",";
        appendPoseJson(os, metadata);
        os << ",";
        appendIntrinsicsJson(os, metadata);
        os << ",";
        appendDistortionJson(os, metadata);
        os << ",";
        appendSensorJson(os, metadata);
        os << ",";
        appendNativeMetadataJson(os, metadata);
        os << "}";

        ACameraMetadata_free(metadata);
        return os.str();
    }

    using GetTagFromNameFn = camera_status_t (*)(const ACameraMetadata*, const char*, uint32_t*);

    GetTagFromNameFn getTagFromNameFunction() const {
        static GetTagFromNameFn function = reinterpret_cast<GetTagFromNameFn>(
            dlsym(RTLD_DEFAULT, "ACameraMetadata_getTagFromName"));
        return function;
    }

    bool readIntegralMetadataByTag(ACameraMetadata* metadata, uint32_t tag, int& value) const {
        ACameraMetadata_const_entry entry{};
        if (ACameraMetadata_getConstEntry(metadata, tag, &entry) != ACAMERA_OK || entry.count <= 0) {
            return false;
        }

        switch (entry.type) {
            case ACAMERA_TYPE_BYTE:
                value = static_cast<int>(entry.data.u8[0]);
                return true;
            case ACAMERA_TYPE_INT32:
                value = static_cast<int>(entry.data.i32[0]);
                return true;
            case ACAMERA_TYPE_INT64:
                value = static_cast<int>(entry.data.i64[0]);
                return true;
            default:
                return false;
        }
    }

    bool readIntegralMetadataByName(ACameraMetadata* metadata, const char* name, int& value) const {
        auto* getTagFromName = getTagFromNameFunction();
        if (!getTagFromName) {
            return false;
        }

        uint32_t tag = 0;
        if (getTagFromName(metadata, name, &tag) != ACAMERA_OK) {
            return false;
        }
        return readIntegralMetadataByTag(metadata, tag, value);
    }

    bool tryReadQuestVendorHints(ACameraMetadata* metadata, int& cameraPositionId, int& cameraSource) const {
        int source = -1;
        int position = -1;
        bool sourceResolved = readIntegralMetadataByTag(metadata, kQuestCameraSourceTag, source);
        bool positionResolved = readIntegralMetadataByTag(metadata, kQuestCameraPositionTag, position);

        if (!sourceResolved) {
            sourceResolved = readIntegralMetadataByName(metadata, "com.meta.extra_metadata.camera_source", source);
        }
        if (!positionResolved) {
            positionResolved = readIntegralMetadataByName(metadata, "com.meta.extra_metadata.position", position);
        }

        if (sourceResolved) {
            cameraSource = source;
        }
        if (positionResolved) {
            cameraPositionId = position;
        }
        return sourceResolved && positionResolved;
    }

    std::string describeQuestVendorLookup(ACameraMetadata* metadata) const {
        std::ostringstream os;
        int32_t tagCount = 0;
        const uint32_t* tags = nullptr;
        const bool hasAllTags = ACameraMetadata_getAllTags(metadata, &tagCount, &tags) == ACAMERA_OK && tags != nullptr;
        os << "availableTagCount=" << (hasAllTags ? tagCount : -1);

        appendQuestVendorTagDiagnostic(os, metadata, "camera_source_descriptor", kQuestCameraSourceTag);
        appendQuestVendorTagDiagnostic(os, metadata, "position_descriptor", kQuestCameraPositionTag);

        auto* getTagFromName = getTagFromNameFunction();
        if (!getTagFromName) {
            os << ", nameLookup=unavailable";
            return os.str();
        }

        appendQuestVendorNameDiagnostic(os, metadata, getTagFromName, "camera_source_name", "com.meta.extra_metadata.camera_source");
        appendQuestVendorNameDiagnostic(os, metadata, getTagFromName, "position_name", "com.meta.extra_metadata.position");
        return os.str();
    }

    void appendQuestVendorNameDiagnostic(
        std::ostringstream& os,
        ACameraMetadata* metadata,
        GetTagFromNameFn getTagFromName,
        const char* label,
        const char* name) const {
        uint32_t resolvedTag = 0;
        const camera_status_t status = getTagFromName(metadata, name, &resolvedTag);
        os << ", " << label << "={lookupStatus:" << status;
        if (status == ACAMERA_OK) {
            os << ", resolvedTag:" << resolvedTag;
            appendQuestVendorEntryDiagnostic(os, metadata, resolvedTag);
        }
        os << "}";
    }

    void appendQuestVendorTagDiagnostic(
        std::ostringstream& os,
        ACameraMetadata* metadata,
        const char* label,
        uint32_t tag) const {
        os << ", " << label << "={tag:" << tag;
        appendQuestVendorEntryDiagnostic(os, metadata, tag);
        os << "}";
    }

    void appendQuestVendorEntryDiagnostic(std::ostringstream& os, ACameraMetadata* metadata, uint32_t tag) const {
        ACameraMetadata_const_entry entry{};
        const camera_status_t status = ACameraMetadata_getConstEntry(metadata, tag, &entry);
        os << ", entryStatus:" << status;
        if (status != ACAMERA_OK) {
            return;
        }

        os << ", type:" << metadataTypeName(entry.type) << ", count:" << entry.count;
        if (entry.count <= 0) {
            return;
        }

        os << ", firstValue:";
        switch (entry.type) {
            case ACAMERA_TYPE_BYTE:
                os << static_cast<int>(entry.data.u8[0]);
                break;
            case ACAMERA_TYPE_INT32:
                os << entry.data.i32[0];
                break;
            case ACAMERA_TYPE_INT64:
                os << entry.data.i64[0];
                break;
            default:
                os << "non-integral";
                break;
        }
    }

    bool findPassthroughCameraIdForPositionLocked(
        QrcCameraPosition position,
        std::string& cameraId,
        int* cameraListIndex = nullptr) {
        auto ids = listCameraIdsLocked();
        const int requestedPosition = position == QRC_CAMERA_RIGHT ? 1 : 0;
        for (size_t i = 0; i < ids.size(); ++i) {
            ACameraMetadata* metadata = nullptr;
            if (ACameraManager_getCameraCharacteristics(manager_, ids[i].c_str(), &metadata) != ACAMERA_OK || !metadata) {
                continue;
            }

            int cameraPositionId = -1;
            int cameraSource = -1;
            const bool resolved = tryReadQuestVendorHints(metadata, cameraPositionId, cameraSource);
            if (!resolved) {
                QRC_LOGW(
                    "Quest passthrough vendor metadata unresolved for cameraId=%s: %s",
                    ids[i].c_str(),
                    describeQuestVendorLookup(metadata).c_str());
            } else {
                QRC_LOGI(
                    "Quest passthrough vendor metadata for cameraId=%s: cameraSource=%d, position=%d",
                    ids[i].c_str(),
                    cameraSource,
                    cameraPositionId);
            }
            ACameraMetadata_free(metadata);

            if (resolved && cameraSource == 0 && cameraPositionId == requestedPosition) {
                cameraId = ids[i];
                if (cameraListIndex != nullptr) {
                    *cameraListIndex = static_cast<int>(i);
                }
                return true;
            }
        }
        QRC_LOGE(
            "No Quest passthrough camera matched requested position %d after scanning %zu camera(s)",
            requestedPosition,
            ids.size());
        return false;
    }

    void appendPoseJson(std::ostringstream& os, ACameraMetadata* metadata) const {
        ACameraMetadata_const_entry entry{};
        os << "\"pose\":{";
        os << "\"translation\":";
        if (getEntry(metadata, ACAMERA_LENS_POSE_TRANSLATION, entry)) {
            writeNumericArray(os, entry.data.f, entry.count);
        } else {
            os << "[]";
        }
        os << ",\"rotation\":";
        if (getEntry(metadata, ACAMERA_LENS_POSE_ROTATION, entry)) {
            writeNumericArray(os, entry.data.f, entry.count);
        } else {
            os << "[]";
        }
        os << ",\"reference\":\"";
        if (getEntry(metadata, ACAMERA_LENS_POSE_REFERENCE, entry)) {
            os << poseReferenceName(entry.data.u8[0]);
        }
        os << "\"}";
    }

    void appendIntrinsicsJson(std::ostringstream& os, ACameraMetadata* metadata) const {
        ACameraMetadata_const_entry entry{};
        float values[5] = {0, 0, 0, 0, 0};
        if (getEntry(metadata, ACAMERA_LENS_INTRINSIC_CALIBRATION, entry)) {
            const uint32_t count = entry.count < 5 ? entry.count : 5;
            for (uint32_t i = 0; i < count; ++i) {
                values[i] = entry.data.f[i];
            }
        }
        os << "\"intrinsics\":{";
        os << "\"fx\":" << values[0] << ",";
        os << "\"fy\":" << values[1] << ",";
        os << "\"cx\":" << values[2] << ",";
        os << "\"cy\":" << values[3] << ",";
        os << "\"skew\":" << values[4];
        os << "}";
    }

    void appendDistortionJson(std::ostringstream& os, ACameraMetadata* metadata) const {
        ACameraMetadata_const_entry entry{};
        os << "\"distortion\":";
        if (getEntry(metadata, ACAMERA_LENS_DISTORTION, entry)) {
            writeNumericArray(os, entry.data.f, entry.count);
        } else {
            os << "[]";
        }
    }

    void appendIntSize(std::ostringstream& os, int width, int height) const {
        os << "{\"width\":" << width << ",\"height\":" << height << "}";
    }

    void appendIntRect(std::ostringstream& os, const int32_t* values, uint32_t count) const {
        const int32_t left = count > 0 ? values[0] : 0;
        const int32_t top = count > 1 ? values[1] : 0;
        const int32_t right = count > 2 ? values[2] : 0;
        const int32_t bottom = count > 3 ? values[3] : 0;
        os << "{\"left\":" << left << ",\"top\":" << top << ",\"right\":" << right << ",\"bottom\":" << bottom << "}";
    }

    void appendSensorJson(std::ostringstream& os, ACameraMetadata* metadata) const {
        ACameraMetadata_const_entry entry{};
        os << "\"sensor\":{";
        os << "\"availableFocalLengths\":";
        if (getEntry(metadata, ACAMERA_LENS_INFO_AVAILABLE_FOCAL_LENGTHS, entry)) {
            writeNumericArray(os, entry.data.f, entry.count);
        } else {
            os << "[]";
        }

        os << ",\"physicalSize\":";
        if (getEntry(metadata, ACAMERA_SENSOR_INFO_PHYSICAL_SIZE, entry) && entry.count >= 2) {
            os << "{\"width\":" << entry.data.f[0] << ",\"height\":" << entry.data.f[1] << "}";
        } else {
            os << "{\"width\":0,\"height\":0}";
        }

        os << ",\"pixelArraySize\":";
        if (getEntry(metadata, ACAMERA_SENSOR_INFO_PIXEL_ARRAY_SIZE, entry) && entry.count >= 2) {
            appendIntSize(os, entry.data.i32[0], entry.data.i32[1]);
        } else {
            appendIntSize(os, 0, 0);
        }

        os << ",\"preCorrectionActiveArraySize\":";
        if (getEntry(metadata, ACAMERA_SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE, entry)) {
            appendIntRect(os, entry.data.i32, entry.count);
        } else {
            const int32_t zeros[4] = {0, 0, 0, 0};
            appendIntRect(os, zeros, 4);
        }

        os << ",\"activeArraySize\":";
        if (getEntry(metadata, ACAMERA_SENSOR_INFO_ACTIVE_ARRAY_SIZE, entry)) {
            appendIntRect(os, entry.data.i32, entry.count);
        } else {
            const int32_t zeros[4] = {0, 0, 0, 0};
            appendIntRect(os, zeros, 4);
        }

        os << ",\"timestampSource\":\"";
        if (getEntry(metadata, ACAMERA_SENSOR_INFO_TIMESTAMP_SOURCE, entry)) {
            os << timestampSourceName(entry.data.u8[0]);
        }
        os << "\"}";
    }

    void appendNativeMetadataJson(std::ostringstream& os, ACameraMetadata* metadata) const {
        int32_t tagCount = 0;
        const uint32_t* tags = nullptr;
        int questSource = -1;
        int questPosition = -1;
        const bool questVendorKeysResolved = tryReadQuestVendorHints(metadata, questPosition, questSource);
        os << "\"nativeMetadata\":{";
        os << "\"questVendorKeysResolved\":" << (questVendorKeysResolved ? "true" : "false") << ",";
        os << "\"questVendorKeyNames\":[\"com.meta.extra_metadata.position\",\"com.meta.extra_metadata.camera_source\"],";
        os << "\"questVendorKeyDescriptors\":[" << kQuestCameraPositionTag << "," << kQuestCameraSourceTag << "],";
        os << "\"questVendorPosition\":" << questPosition << ",";
        os << "\"questVendorCameraSource\":" << questSource << ",";
        os << "\"vendorTags\":[";
        if (ACameraMetadata_getAllTags(metadata, &tagCount, &tags) == ACAMERA_OK && tags) {
            bool first = true;
            for (int32_t i = 0; i < tagCount; ++i) {
                if (tags[i] < 0x80000000u) {
                    continue;
                }
                ACameraMetadata_const_entry entry{};
                if (ACameraMetadata_getConstEntry(metadata, tags[i], &entry) != ACAMERA_OK) {
                    continue;
                }
                if (!first) os << ",";
                first = false;
                os << "{\"tag\":" << tags[i] << ",\"type\":" << static_cast<int>(entry.type) << ",\"count\":" << entry.count;
                if (entry.count > 0 && entry.count <= 8) {
                    os << ",\"values\":";
                    appendEntryValues(os, entry);
                }
                os << "}";
            }
        }
        os << "]}";
    }

    void appendEntryValues(std::ostringstream& os, const ACameraMetadata_const_entry& entry) const {
        os << "[";
        for (uint32_t i = 0; i < entry.count; ++i) {
            if (i > 0) os << ",";
            switch (entry.type) {
                case ACAMERA_TYPE_BYTE: os << static_cast<int>(entry.data.u8[i]); break;
                case ACAMERA_TYPE_INT32: os << entry.data.i32[i]; break;
                case ACAMERA_TYPE_FLOAT: os << entry.data.f[i]; break;
                case ACAMERA_TYPE_INT64: os << entry.data.i64[i]; break;
                case ACAMERA_TYPE_DOUBLE: os << entry.data.d[i]; break;
                case ACAMERA_TYPE_RATIONAL:
                    os << "{\"numerator\":" << entry.data.r[i].numerator << ",\"denominator\":" << entry.data.r[i].denominator << "}";
                    break;
                default: os << 0; break;
            }
        }
        os << "]";
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
    std::string cameraMetadataJsonCache_;

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

const char* kInvalidSessionHandleError = "Invalid camera session handle";
std::mutex gMetadataMutex;
std::string gCameraIdListJsonCache;
std::string gCameraMetadataJsonCache;

CameraNative* sessionFromHandle(QrcCameraSessionHandle handle) {
    return static_cast<CameraNative*>(handle);
}

QrcCameraResult requireSession(QrcCameraSessionHandle handle, CameraNative** outSession) {
    if (handle == nullptr || outSession == nullptr) {
        return QRC_CAMERA_ERROR_INVALID_ARGUMENT;
    }
    *outSession = sessionFromHandle(handle);
    return *outSession == nullptr ? QRC_CAMERA_ERROR_INVALID_ARGUMENT : QRC_CAMERA_OK;
}

} // namespace

extern "C" QrcCameraResult QrcCamera_CreateSession(QrcCameraSessionHandle* outHandle) {
    if (outHandle == nullptr) {
        return QRC_CAMERA_ERROR_INVALID_ARGUMENT;
    }
    auto* session = new (std::nothrow) CameraNative();
    if (session == nullptr) {
        *outHandle = nullptr;
        return QRC_CAMERA_ERROR_IO;
    }
    *outHandle = static_cast<QrcCameraSessionHandle>(session);
    return QRC_CAMERA_OK;
}

extern "C" QrcCameraResult QrcCamera_DestroySession(QrcCameraSessionHandle handle) {
    if (handle == nullptr) {
        return QRC_CAMERA_ERROR_INVALID_ARGUMENT;
    }
    delete sessionFromHandle(handle);
    return QRC_CAMERA_OK;
}

extern "C" QrcCameraResult QrcCamera_InitializeSession(
    QrcCameraSessionHandle handle,
    int width,
    int height,
    const char* frameDirectory,
    const char* formatInfoFilePath) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK
        ? session->initialize(width, height, frameDirectory, formatInfoFilePath)
        : result;
}

extern "C" QrcCameraResult QrcCamera_SetSessionSaveFrameRate(QrcCameraSessionHandle handle, int fps) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->setSaveFrameRate(fps) : result;
}

extern "C" QrcCameraResult QrcCamera_OpenSession(QrcCameraSessionHandle handle, QrcCameraPosition position) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->openByPosition(position) : result;
}

extern "C" QrcCameraResult QrcCamera_OpenSessionById(QrcCameraSessionHandle handle, const char* cameraId) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->openById(cameraId) : result;
}

extern "C" QrcCameraResult QrcCamera_StartSessionRecording(QrcCameraSessionHandle handle) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->startRecording() : result;
}

extern "C" QrcCameraResult QrcCamera_StopSessionRecording(QrcCameraSessionHandle handle) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->stopRecording() : result;
}

extern "C" QrcCameraResult QrcCamera_CloseSession(QrcCameraSessionHandle handle) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->close() : result;
}

extern "C" QrcCameraResult QrcCamera_GetSessionStats(QrcCameraSessionHandle handle, QrcCameraStats* outStats) {
    CameraNative* session = nullptr;
    QrcCameraResult result = requireSession(handle, &session);
    return result == QRC_CAMERA_OK ? session->getStats(outStats) : result;
}

extern "C" const char* QrcCamera_GetSessionLastError(QrcCameraSessionHandle handle) {
    CameraNative* session = sessionFromHandle(handle);
    return session == nullptr ? kInvalidSessionHandleError : session->lastError();
}

extern "C" const char* QrcCamera_GetSessionLastOpenedCameraId(QrcCameraSessionHandle handle) {
    CameraNative* session = sessionFromHandle(handle);
    return session == nullptr ? "" : session->lastOpenedCameraId();
}

extern "C" const char* QrcCamera_GetCameraIdListJson(void) {
    std::lock_guard<std::mutex> lock(gMetadataMutex);
    CameraNative metadataReader;
    gCameraIdListJsonCache = metadataReader.cameraIdListJson();
    return gCameraIdListJsonCache.c_str();
}

extern "C" const char* QrcCamera_GetCameraMetadataJson(QrcCameraPosition position) {
    std::lock_guard<std::mutex> lock(gMetadataMutex);
    CameraNative metadataReader;
    gCameraMetadataJsonCache = metadataReader.cameraMetadataJson(position);
    return gCameraMetadataJsonCache.c_str();
}
