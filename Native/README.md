# qrc_camera_native

Unity Android / Meta Quest 向けのNDK Camera2 native plugin雛形です。

## 方針

- 権限要求は行いません。Unity側で `CAMERA` と `horizonos.permission.HEADSET_CAMERA` を取得済みの前提です。
- JPEG返却、YUV->RGB変換は行いません。
- `AImage` の各planeを `AImage_getPlaneData()` で取得し、現行Kotlin版と同じく plane 0, 1, 2 の順にそのまま連結して `.yuv` 保存します。
- 保存FPSはCameraに要求せず、保存時にフレームを間引きます。

## 生成物

```text
include/qrc_camera_native.h
src/qrc_camera_native.cpp
CMakeLists.txt
```

## C API

```c
QrcCamera_Initialize(width, height, frameDirectory, formatInfoFilePath);
QrcCamera_SetSaveFrameRate(fps);   // 0なら全保存、正の値なら保存フレームを間引く
QrcCamera_Open(position);          // 0=left, 1=right。ただし現状はcameraId順のfallback
QrcCamera_OpenById(cameraId);      // 実運用ではこちらを推奨
QrcCamera_StartRecording();
QrcCamera_StopRecording();
QrcCamera_Close();
QrcCamera_GetStats(&stats);
QrcCamera_GetLastError();
QrcCamera_GetCameraIdListJson();
```

## 保存形式

各フレームは以下の順でそのまま連結されます。

```text
plane[0].buffer
plane[1].buffer
plane[2].buffer
```

I420 / NV12 / NV21 への変換、stride除去、padding除去、plane並び替えは行いません。

ファイル名は現行Kotlin版と同じ考え方です。

```text
<computed_unix_time_ms>.yuv
```

時刻変換:

```text
unix_ms = baseUnixTimeMs + (imageTimestampNs - baseMonoTimeNs) / 1_000_000
```

## formatInfo.json

初回保存フレームで以下を出力します。

```json
{
  "width": 1280,
  "height": 960,
  "format": "YUV_420_888",
  "planes": [
    { "rowStride": 1280, "pixelStride": 1, "bufferSize": 1228800 }
  ],
  "baseTime": {
    "baseMonoTimeNs": 123,
    "baseUnixTimeMs": 456
  }
}
```

## 注意

NDK単体ではMetaのvendor metadata名からleft/right passthrough cameraを安定判定する経路が限定されるため、初期版では `QrcCamera_OpenById()` を推奨します。`QrcCamera_Open(position)` はcameraId順のfallbackです。

Unity側で `QrcCamera_GetCameraIdListJson()` を呼んで実機のIDを確認し、既存Kotlin版と同じIDを `QrcCamera_OpenById()` に渡す運用が安全です。
