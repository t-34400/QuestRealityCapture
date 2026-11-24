#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RealityLog.Recording
{
    internal sealed class MrukFrameWriter
    {
        private const int BuffersPerCamera = 4;
        private const int FlushIntervalMs = 1000;
        private const int FileNameColumn = 1;
        private const int ErrorColumn = 25;

        private readonly ConcurrentQueue<PendingFrame> freeLeft = new();
        private readonly ConcurrentQueue<PendingFrame> freeRight = new();
        private readonly BlockingCollection<PendingFrame> pending = new(BuffersPerCamera * 2);
        private readonly Task worker;
        private int outstandingFrames;

        public MrukFrameWriter(RecordingSessionPaths paths, bool leftEnabled, bool rightEnabled)
        {
            for (var i = 0; i < BuffersPerCamera; i++)
            {
                if (leftEnabled)
                {
                    freeLeft.Enqueue(new PendingFrame(isLeft: true));
                }
                if (rightEnabled)
                {
                    freeRight.Enqueue(new PendingFrame(isLeft: false));
                }
            }

            var started = new TaskCompletionSource<bool>();
            worker = Task.Factory.StartNew(() =>
            {
                try
                {
                    WriteSession(paths, leftEnabled, rightEnabled, started);
                }
                catch (Exception ex)
                {
                    started.TrySetException(ex);
                    throw;
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // Report CSV creation failures to the caller before the session starts sampling.
            try
            {
                started.Task.GetAwaiter().GetResult();
            }
            catch
            {
                Complete();
                throw;
            }
        }

        public bool HasFailed => worker.IsFaulted;
        public int LeftFilesWritten { get; private set; }
        public int RightFilesWritten { get; private set; }
        public int FailedWrites { get; private set; }
        public int PeakOutstandingFrames { get; private set; }

        public PendingFrame? TryRent(bool isLeft)
        {
            var pool = isLeft ? freeLeft : freeRight;
            return pool.TryDequeue(out var frame) ? frame : null;
        }

        public void Return(PendingFrame frame)
        {
            frame.Metadata = Array.Empty<string>();
            var pool = frame.IsLeft ? freeLeft : freeRight;
            pool.Enqueue(frame);
        }

        public void Enqueue(PendingFrame frame)
        {
            var count = Interlocked.Increment(ref outstandingFrames);
            PeakOutstandingFrames = Math.Max(PeakOutstandingFrames, count);
            // Each queued frame owns a slot, so the queue has room for every rented slot.
            pending.Add(frame);
        }

        public void Complete()
        {
            pending.CompleteAdding();
            try
            {
                worker.GetAwaiter().GetResult();
            }
            finally
            {
                pending.Dispose();
            }
        }

        private void WriteSession(RecordingSessionPaths paths, bool leftEnabled, bool rightEnabled,
            TaskCompletionSource<bool> started)
        {
            using var leftCsv = leftEnabled
                ? CreateCsv(paths.LeftMrukCamera.FrameMetadataFilePath, FrameMetadataHeader) : null;
            using var rightCsv = rightEnabled
                ? CreateCsv(paths.RightMrukCamera.FrameMetadataFilePath, FrameMetadataHeader) : null;
            using var pairCsv = leftEnabled && rightEnabled
                ? CreateCsv(paths.MrukStereoPairFilePath, PairMetadataHeader) : null;
            started.SetResult(true);

            var recordedFrames = pairCsv != null ? new List<MrukStereoMatcher.Frame>() : null;
            var flushTimer = Stopwatch.StartNew();
            var dirty = false;
            while (!pending.IsCompleted)
            {
                var waitMs = dirty ? (int)Math.Max(0, FlushIntervalMs - flushTimer.ElapsedMilliseconds) : Timeout.Infinite;
                if (pending.TryTake(out var frame, waitMs))
                {
                    var cameraPaths = frame.IsLeft ? paths.LeftMrukCamera : paths.RightMrukCamera;
                    var csv = frame.IsLeft ? leftCsv! : rightCsv!;
                    WriteFrame(frame, cameraPaths.ImageDirectoryPath, csv, recordedFrames);
                    Interlocked.Decrement(ref outstandingFrames);
                    Return(frame);
                    if (!dirty)
                    {
                        flushTimer.Restart();
                        dirty = true;
                    }
                }

                if (dirty && flushTimer.ElapsedMilliseconds >= FlushIntervalMs)
                {
                    leftCsv?.Flush();
                    rightCsv?.Flush();
                    dirty = false;
                }
            }

            // Frame metadata must reach the filesystem before the pair index is emitted.
            leftCsv?.Flush();
            rightCsv?.Flush();
            if (pairCsv != null && recordedFrames != null)
            {
                var pairIndex = 0;
                MrukStereoMatcher.Match(recordedFrames, (left, right) =>
                {
                    pairCsv.WriteLine(string.Join(",",
                        (++pairIndex).ToString(CultureInfo.InvariantCulture),
                        left.FileName,
                        right.FileName,
                        left.TimestampUs.ToString(CultureInfo.InvariantCulture),
                        right.TimestampUs.ToString(CultureInfo.InvariantCulture),
                        (right.TimestampUs - left.TimestampUs).ToString(CultureInfo.InvariantCulture),
                        left.Index.ToString(CultureInfo.InvariantCulture),
                        right.Index.ToString(CultureInfo.InvariantCulture)));
                });
            }
        }

        private void WriteFrame(PendingFrame frame, string directory, StreamWriter csv,
            List<MrukStereoMatcher.Frame>? recordedFrames)
        {
            var row = frame.Metadata;
            if (row[FileNameColumn].Length > 0)
            {
                var path = Path.Combine(directory, row[FileNameColumn]);
                var created = false;
                try
                {
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    created = true;
                    output.Write(frame.Pixels, 0, frame.Pixels.Length);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    row[FileNameColumn] = string.Empty;
                    var message = $"RGBA write failed: {ex.Message}";
                    if (created)
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception cleanupError) when (cleanupError is IOException || cleanupError is UnauthorizedAccessException)
                        {
                            message += $"; could not remove incomplete file: {cleanupError.Message}";
                        }
                    }
                    row[ErrorColumn] = row[ErrorColumn].Length == 0 ? message : row[ErrorColumn] + "; " + message;
                    FailedWrites++;
                }
            }

            row[ErrorColumn] = EscapeCsv(row[ErrorColumn]);
            csv.WriteLine(string.Join(",", row));

            var fileName = row[FileNameColumn];
            if (fileName.Length > 0)
            {
                if (frame.IsLeft)
                {
                    LeftFilesWritten++;
                }
                else
                {
                    RightFilesWritten++;
                }
                recordedFrames?.Add(new MrukStereoMatcher.Frame(frame.Index, fileName, frame.TimestampUs, frame.IsLeft));
            }
        }

        private static StreamWriter CreateCsv(string path, string header)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var writer = new StreamWriter(path, append: false, new UTF8Encoding(false), bufferSize: 16 * 1024);
            try
            {
                writer.WriteLine(header);
                writer.Flush();
                return writer;
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }

        private static string EscapeCsv(string value)
        {
            return value.Length == 0 ? string.Empty : "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private const string FrameMetadataHeader =
            "frame_index,file_name,unix_time_ms,timestamp_us_realtime,width,height,format,camera_position,pose_pos_x,pose_pos_y,pose_pos_z,pose_rot_x,pose_rot_y,pose_rot_z,pose_rot_w,pose_ok,is_playing,is_updated_this_frame,current_resolution_width,current_resolution_height,pixel_count,colors_array_length,rgba_byte_count,get_texture_ok,get_colors_ok,error";

        private const string PairMetadataHeader =
            "pair_index,left_file_name,right_file_name,left_timestamp_us_realtime,right_timestamp_us_realtime,time_delta_us,left_frame_index,right_frame_index";

        internal sealed class PendingFrame
        {
            public PendingFrame(bool isLeft) => IsLeft = isLeft;

            public bool IsLeft { get; }
            public int Index { get; set; }
            public long TimestampUs { get; set; }
            public byte[] Pixels { get; set; } = Array.Empty<byte>();
            public string[] Metadata { get; set; } = Array.Empty<string>();
        }
    }
}
