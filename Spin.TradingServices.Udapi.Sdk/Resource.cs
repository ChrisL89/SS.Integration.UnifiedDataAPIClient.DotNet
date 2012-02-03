﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Spin.TradingServices.Udapi.Sdk.Clients;
using Spin.TradingServices.Udapi.Sdk.Events;
using Spin.TradingServices.Udapi.Sdk.Extensions;
using Spin.TradingServices.Udapi.Sdk.Interfaces;
using Spin.TradingServices.Udapi.Sdk.Model;

namespace Spin.TradingServices.Udapi.Sdk
{
    public class Resource : Endpoint, IResource
    {
        private bool _isStreaming;
        private bool _streamingCompleted;

        internal Resource(NameValueCollection headers, RestItem restItem) : base(headers, restItem)
        {
            
        }

        public string Id
        {
            get { return _state.Content.Id; }
        }

        public string Name
        {
            get { return _state.Name; }
        }

        public Summary Content
        {
            get { return _state.Content; }
        }

        public string GetSnapshot()
        {
            if(_state != null)
            {
                foreach (var restLink in _state.Links.Where(restLink => restLink.Relation == "http://api.sportingsolutions.com/lastresponse"))
                {
                    return RestHelper.GetResponse(new Uri(restLink.Href), null, "GET", "application/json", _headers);
                }
            }
            return "";
        }

        public void StartStreaming()
        {
            if (_state != null)
            {
                Task.Factory.StartNew((stateObj) =>
                {
                    foreach (var restLink in _state.Links.Where(restLink => restLink.Relation == "http://api.sportingsolutions.com/stream/amqp"))
                    {
                        _restItems = RestHelper.GetResponse(new Uri(restLink.Href), null, "GET", "application/json", _headers).FromJson<List<RestItem>>();
                        break;
                    }
                    var amqpUri = new Uri(_restItems[0].Links[0].Href);
                    var connectionFactory = new ConnectionFactory();
                    var host = amqpUri.Host;
                    if (!String.IsNullOrEmpty(host))
                    {
                        connectionFactory.HostName = host;
                    }
                    var port = amqpUri.Port;
                    if (port != -1)
                    {
                        connectionFactory.Port = port;
                    }
                    var userInfo = amqpUri.UserInfo;
                    if (!String.IsNullOrEmpty(userInfo))
                    {
                        var userPass = userInfo.Split(':');
                        if (userPass.Length > 2)
                        {
                            throw new ArgumentException(string.Format("Bad user info in AMQP URI: {0}", userInfo));
                        }
                        connectionFactory.UserName = userPass[0];
                        if (userPass.Length == 2)
                        {
                            connectionFactory.Password = userPass[1];
                        }
                    }
                    var queueName = "";
                    var path = amqpUri.AbsolutePath;
                    if (!String.IsNullOrEmpty(path))
                    {
                        queueName = path.Substring(path.IndexOf('/', 1) + 1);
                        var virtualHost = path.Substring(1, path.IndexOf('/', 1) - 1);
                        connectionFactory.VirtualHost = "/" + virtualHost;
                    }

                    var connection = connectionFactory.CreateConnection();
                    StreamConnected(this, new EventArgs());
                    var channel = connection.CreateModel();
                    var consumer = new QueueingBasicConsumer(channel);
                    channel.BasicConsume(queueName, true, consumer);
                    channel.BasicQos(0, 10, false);

                    _isStreaming = true;
                    while (_isStreaming)
                    {
                        var output = consumer.Queue.Dequeue();
                        if (output != null)
                        {
                            var deliveryArgs = (BasicDeliverEventArgs)output;
                            var message = deliveryArgs.Body;
                            StreamEvent(this, new StreamEventArgs(Encoding.UTF8.GetString(message)));
                        }
                    }

                    channel.Close();
                    connection.Close();
                    _streamingCompleted = true;
                }, null);
                
            }
        }

        public void StopStreaming()
        {
            _isStreaming = false;
            while(!_streamingCompleted)
            {
                
            }
            StreamDisconnected(this,new EventArgs());
        }

        public event EventHandler StreamConnected;
        public event EventHandler StreamDisconnected;
        public event EventHandler<StreamEventArgs> StreamEvent;
    }
}
