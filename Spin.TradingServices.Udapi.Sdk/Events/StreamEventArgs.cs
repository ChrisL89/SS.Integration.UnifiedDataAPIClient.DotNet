﻿using System;

namespace Spin.TradingServices.Udapi.Sdk.Events
{
    public class StreamEventArgs : EventArgs
    {
        public string Update { get; private set; }
        
        public StreamEventArgs(string update)
        {
            Update = update;
        }
    }
}
