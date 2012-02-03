﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Spin.TradingServices.Udapi.Sdk.Example.Console.Model
{
    public class Market
    {
        public Market()
        {
            Tags = new Dictionary<string, object>();
            Selections = new List<Selection>();
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public bool Tradable { get; set; }

        public Dictionary<string, object> Tags { get; set; }

        public List<Selection> Selections { get; set; }
    }
}
