using System;
using System.Threading;
using System.Collections.Generic;
using CircularBuffer;

namespace DataStreamTools
{
    public abstract class DataStream
    {
        public int RawBufferSize => _rawBufferSize;
        public int DataBufferSize => _dataBufferSize;

        protected int _rawBufferSize;
        protected int _dataBufferSize;
        
        public abstract void ProcessQueue();
        public abstract void AddData();
        public abstract string LatestDataToString();

        public delegate string DatapointToStringHandler();
        protected DatapointToStringHandler _datapointToStringHandler;

        public string Name;
    }
    
    public class DataStream<T> : DataStream
    {
        public Queue<T> RawDataBuffer { get; set; }
        public CircularBuffer<T> DataBuffer { get; private set; }
        public int RawDataCount => RawDataBuffer.Count;

        public delegate T QueueProcessHandler(T data);
        public delegate T AddDataPointHandler();

        private QueueProcessHandler _queueHandler;
        private AddDataPointHandler _addDataPointPointHandler;
        
        private bool _hasProcessHandler = false;

        private T _tempData;
        private T _latestRawData;
        
        public T LatestRawDatapoint => _latestRawData;

        public DataStream(string name, int rawBufferSize, int dataBufferSize,
            AddDataPointHandler addDataPointPointHandler, DatapointToStringHandler datapointToStringHandler,
            QueueProcessHandler queueHandler = null)
        {
            Name = name;
            _rawBufferSize = rawBufferSize;
            _dataBufferSize = dataBufferSize;
            _addDataPointPointHandler = addDataPointPointHandler;
            _datapointToStringHandler = datapointToStringHandler;

            RawDataBuffer = new Queue<T>(_rawBufferSize);
            DataBuffer = new CircularBuffer<T>(_dataBufferSize);

            if (queueHandler != null)
            {
                _hasProcessHandler = true;
                _queueHandler = queueHandler;
            }
        }

        public override void ProcessQueue()
        {
            lock (RawDataBuffer)
            {
                while (RawDataBuffer.Count > 0)
                {
                    _tempData = RawDataBuffer.Dequeue();
                    if (_hasProcessHandler)
                    {
                        _tempData = _queueHandler(_tempData);
                    }

                    DataBuffer.PushBack(_tempData);
                }
            }
        }

        public override void AddData()
        {
            lock (RawDataBuffer)
            {
                if (RawDataBuffer.Count >= _rawBufferSize)
                {
                    RawDataBuffer.Clear();
                }

                _latestRawData = _addDataPointPointHandler();
                
                RawDataBuffer.Enqueue(_latestRawData);
            }
        }

        public override string LatestDataToString()
        {
            return _datapointToStringHandler();
        }
    }
}