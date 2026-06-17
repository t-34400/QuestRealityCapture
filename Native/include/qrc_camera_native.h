#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef enum QrcCameraResult {
    QRC_CAMERA_OK = 0,
    QRC_CAMERA_ERROR_INVALID_STATE = -1,
    QRC_CAMERA_ERROR_INVALID_ARGUMENT = -2,
    QRC_CAMERA_ERROR_CAMERA_NOT_FOUND = -3,
    QRC_CAMERA_ERROR_CAMERA_OPEN_FAILED = -4,
    QRC_CAMERA_ERROR_SESSION_FAILED = -5,
    QRC_CAMERA_ERROR_IO = -6,
    QRC_CAMERA_ERROR_PERMISSION_DENIED = -7,
    QRC_CAMERA_ERROR_NOT_SUPPORTED = -8
} QrcCameraResult;

typedef enum QrcCameraPosition {
    QRC_CAMERA_LEFT = 0,
    QRC_CAMERA_RIGHT = 1
} QrcCameraPosition;

typedef struct QrcCameraStats {
    int64_t receivedFrameCount;
    int64_t savedFrameCount;
    int64_t droppedFrameCount;
    int64_t ioErrorCount;
    int64_t lastImageTimestampNs;
    int64_t lastSavedTimestampNs;
} QrcCameraStats;

typedef void* QrcCameraSessionHandle;

QrcCameraResult QrcCamera_CreateSession(QrcCameraSessionHandle* outHandle);
QrcCameraResult QrcCamera_DestroySession(QrcCameraSessionHandle handle);
QrcCameraResult QrcCamera_InitializeSession(
    QrcCameraSessionHandle handle,
    int width,
    int height,
    const char* frameDirectory,
    const char* formatInfoFilePath);
QrcCameraResult QrcCamera_SetSessionSaveFrameRate(QrcCameraSessionHandle handle, int fps);
QrcCameraResult QrcCamera_OpenSession(QrcCameraSessionHandle handle, QrcCameraPosition position);
QrcCameraResult QrcCamera_OpenSessionById(QrcCameraSessionHandle handle, const char* cameraId);
QrcCameraResult QrcCamera_StartSessionRecording(QrcCameraSessionHandle handle);
QrcCameraResult QrcCamera_StopSessionRecording(QrcCameraSessionHandle handle);
QrcCameraResult QrcCamera_CloseSession(QrcCameraSessionHandle handle);
QrcCameraResult QrcCamera_GetSessionStats(QrcCameraSessionHandle handle, QrcCameraStats* outStats);
const char* QrcCamera_GetSessionLastError(QrcCameraSessionHandle handle);
const char* QrcCamera_GetSessionLastOpenedCameraId(QrcCameraSessionHandle handle);
const char* QrcCamera_GetCameraIdListJson(void);
const char* QrcCamera_GetCameraMetadataJson(QrcCameraPosition position);

#ifdef __cplusplus
}
#endif
