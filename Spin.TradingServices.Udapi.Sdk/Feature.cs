﻿using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Spin.TradingServices.Udapi.Sdk.Interfaces;
using Spin.TradingServices.Udapi.Sdk.Model;

namespace Spin.TradingServices.Udapi.Sdk
{
    public class Feature : Endpoint, IFeature
    {
        internal Feature(NameValueCollection headers, RestItem restItem):base(headers, restItem)
        {
            
        }

        public string Name
        {
            get { return _state.Name; }
        }

        public List<IResource> GetResources()
        {
            var restItems = GetNext();
            return restItems.Select(restItem => new Resource(_headers, restItem)).Cast<IResource>().ToList();
        }

        public IResource GetResource(string name)
        {
            var restItems = GetNext();
            return (from restItem in restItems where restItem.Name == name select new Resource(_headers, restItem)).FirstOrDefault();
        }
    }
}
