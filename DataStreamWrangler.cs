using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DataStreamTools
{
    public class DataStreamWrangler
    {
        public List<DataStream> Streams { get; private set; }
        public DataStream<double> Timestamps;
        public bool IsRecording => _isRecording;
        public bool IsStreaming => _isStreaming;
        
        public string CurrentFile { get; private set; }
        
        private System.DateTime _initialTime;
        private bool _useAbsoluteTimestamp;
        private double _tickInterval;

        public delegate bool FetchNewDataHandler();

        private FetchNewDataHandler _fetchNewDataHandler;

        private string _header = "";
        private string _recordPrepend = "";

        public DataStreamWrangler(double tickInterval, bool useAbsoluteTimestamp, FetchNewDataHandler fetchNewDataHandler,
            string recordPrepend = "")
        {
            Streams = new List<DataStream>();
            _fetchNewDataHandler = fetchNewDataHandler;
            _useAbsoluteTimestamp = useAbsoluteTimestamp;

            if (tickInterval <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickInterval) + " must be >= 0 seconds");
            }

            _tickInterval = tickInterval;
            _recordPrepend = recordPrepend;

            CurrentFile = "inactive";
        }

        public void AddDataStream(DataStream stream)
        {
            if (Timestamps == null)
            {
                if (_useAbsoluteTimestamp)
                {
                    Timestamps = new DataStream<double>(
                        _useAbsoluteTimestamp ? "systemTimestamp" : "localTimestamp",
                        stream.RawBufferSize,
                        stream.DataBufferSize,
                        () => (System.DateTime.Now - System.DateTime.MinValue).TotalSeconds,
                        () => Timestamps.LatestRawDatapoint.ToString("F4")
                    );
                }
                else
                {
                    Timestamps = new DataStream<double>(
                        _useAbsoluteTimestamp ? "systemTimestamp" : "localTimestamp",
                        stream.RawBufferSize,
                        stream.DataBufferSize, 
                        () => (System.DateTime.Now - _initialTime).TotalSeconds,
                        () => Timestamps.LatestRawDatapoint.ToString("E6")
                    );
                }
            }
            else
            {
                if (stream.RawBufferSize != Timestamps.RawBufferSize ||
                    stream.DataBufferSize != Timestamps.DataBufferSize)
                {
                    throw new ArgumentOutOfRangeException(nameof(stream) +
                                                          " does not have same buffer size as previous streams!");
                }
            }

            Streams.Add(stream);
        }

        public void SetHeader(string newHeader)
        {
            _header = newHeader;
        }

        public void SetRecordPrepend(string newPrepend)
        {
            _recordPrepend = newPrepend;
        }

        private bool _isStreaming;
        private bool _isRecording;
        private Thread _streamingThread;

        private StringBuilder _tempLine = new StringBuilder(1024);

        public void StartStreaming()
        {
            if (_streamingThread != null)
            {
                StopStreaming();
            }

            _streamingThread = new Thread(() => Stream());
            _isStreaming = true;

            _initialTime = DateTime.Now;
            _streamingThread.Start();
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
            {
                return;
            }

            if (_isRecording)
            {
                _isRecording = false;
            }

            _isStreaming = false;
            _streamingThread.Join();
            _streamingThread = null;
        }

        public void ProcessStreamQueues()
        {
            if (Timestamps.RawDataBuffer.Count <= 0)
            {
                return;
            }
            
            foreach (var stream in Streams)
            {
                stream.ProcessQueue();
            }
            Timestamps.ProcessQueue();
        }

        public void StartRecording()
        {
            _initialTime = DateTime.Now;
            _isRecording = true;
        }

        public string StopRecording()
        {
            _isRecording = false;
            return CurrentFile;
        }

        public void Stream()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJNI.AttachCurrentThread();
#endif
            FileStream fileStream = null;
            TextWriter textWriter = null;

            var previouslyRecording = false;
            
            var previousTick = 0L;
            var currentTick = 0L;
            var deltaSeconds = 0d;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (_isStreaming)
            {
                currentTick = sw.ElapsedTicks;
                deltaSeconds = (currentTick - previousTick) / (double)Stopwatch.Frequency;

                if (deltaSeconds < _tickInterval)
                {
                    continue;
                }

                previousTick = currentTick;

                // Start recording
                if (_isRecording && !previouslyRecording)
                { 
                    CurrentFile = (_recordPrepend != "" ? _recordPrepend + "-datastream-" : "dataStream-") +
                                System.DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + ".csv";
                    fileStream = new FileStream(CurrentFile, FileMode.OpenOrCreate);
                    textWriter = new StreamWriter(fileStream, System.Text.Encoding.UTF8, 1024, true);
                    if (_header == "")
                    {
                        _header += Timestamps.Name + ",";
                        foreach (var stream in Streams)
                        {
                            _header += stream.Name + ",";
                        }

                        _header = _header.Remove(_header.Length - 1, 1);
                    }
                    textWriter.WriteLine(_header);
                    textWriter.Flush();
                    previouslyRecording = true;
                }
                // Stop recording
                else if (!_isRecording && previouslyRecording)
                {
                    textWriter?.Close();
                    fileStream?.Close();

                    previouslyRecording = false;
                }

                if (!_fetchNewDataHandler())
                {
                    continue;
                }
                
                Timestamps.AddData();
                foreach (var stream in Streams)
                {
                    stream.AddData();
                }

                // During recording
                if (_isRecording && previouslyRecording)
                {
                    _tempLine.Clear();
                    _tempLine.Append(Timestamps.LatestDataToString());
                    _tempLine.Append(',');
                    foreach (var stream in Streams)
                    {
                        _tempLine.Append(stream.LatestDataToString());
                        _tempLine.Append(',');
                    }

                    _tempLine.Remove(_tempLine.Length - 1, 1);

                    textWriter?.WriteLine(_tempLine);
                    textWriter?.Flush();
                }
            }

            if (_isRecording && previouslyRecording)
            {
                textWriter?.Close();
                fileStream?.Close();
            }
            
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJNI.DetachCurrentThread();
#endif
        }
    }
}