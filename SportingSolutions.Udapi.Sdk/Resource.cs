﻿//Copyright 2012 Spin Services Limited

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//    http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using Newtonsoft.Json.Linq;
using SportingSolutions.Udapi.Sdk.Clients;
using SportingSolutions.Udapi.Sdk.Events;
using SportingSolutions.Udapi.Sdk.Interfaces;
using SportingSolutions.Udapi.Sdk.Model;
using log4net;
using System.Reactive;

namespace SportingSolutions.Udapi.Sdk
{
    public class Resource : Endpoint, IResource, IDisposable, IStreamStatistics
    {
        private readonly ManualResetEvent _pauseStream;

        public IObserver<string> StreamObserver;
        public IObserver<string> EchoObserver;

        public event EventHandler StreamConnected;
        public event EventHandler StreamDisconnected;
        public event EventHandler<StreamEventArgs> StreamEvent;
        public event EventHandler StreamSynchronizationError;

        private readonly StreamController _streamController;

        internal Resource(RestItem restItem, IConnectClient connectClient, StreamController streamController)
            : base(restItem, connectClient)
        {
            Logger = LogManager.GetLogger(typeof(Resource).ToString());
            Logger.DebugFormat("Instantiated fixtureName=\"{0}\"", restItem.Name);
            _streamController = streamController;
            _pauseStream = new ManualResetEvent(true);
        }

        public string Id
        {
            get { return State.Content.Id; }
        }
        public string Name
        {
            get { return State.Name; }
        }
        public string QueueName { get; set; }

        public DateTime LastMessageReceived { get; private set; }
        public DateTime LastStreamDisconnect { get; private set; }
        public bool IsStreamActive { get; set; }

        public double EchoRoundTripInMilliseconds { get; private set; }

        public Summary Content
        {
            get { return State.Content; }
        }

        public string GetSnapshot()
        {
            var loggingStringBuilder = new StringBuilder();
            loggingStringBuilder.AppendFormat("Get Snapshot for fixtureName=\"{0}\" fixtureId={1} \r\n", Name, Id);

            var result = FindRelationAndFollowAsString("http://api.sportingsolutions.com/rels/snapshot", "GetSnapshot Http Error", loggingStringBuilder);
            Logger.Info(loggingStringBuilder);
            return result;
        }

        public void StartStreaming()
        {
            StartStreaming(10000, 3000);
        }

        public void StartStreaming(int echoInterval, int echoMaxDelay)
        {
            IsStreamActive = true;

            StreamObserver = Observer.Create<string>(ProcessMessage);
            EchoObserver = Observer.Create<string>(ProcessEcho);
            Logger.DebugFormat("Stream request started for fixtureId={0} fixtureName=\"{1}\"", Id, Name);
            var streamSubscriber = StreamSubscriber.GetStreamSubscriber();
            streamSubscriber.StartStream(this);
        }

        private void ProcessEcho(string echo)
        {
            if (IsStreamActive)
            {
                Logger.DebugFormat("Thread: {2} - Echo recieved for fixtureId={0} fixtureName=\"{1}\"", Id, Name, Thread.CurrentThread.ManagedThreadId);

                var split = echo.Split(';');
                var timeSent = DateTime.ParseExact(split[1], "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                var roundTripTime = DateTime.Now - timeSent;

                var roundMillis = roundTripTime.TotalMilliseconds;

                EchoRoundTripInMilliseconds = roundMillis;

                LastMessageReceived = DateTime.Now;
            }
        }

        private void ProcessMessage(string message)
        {
            if (IsStreamActive)
            {
                Logger.DebugFormat("Thread: {2} - Stream message arrived for fixtureId={0} fixtureName=\"{1}\"", Id, Name, Thread.CurrentThread.ManagedThreadId);

                if (StreamEvent != null)
                {
                    StreamEvent(this, new StreamEventArgs(message));
                }

                LastMessageReceived = DateTime.Now;
            }
        }

        public static int GetSequenceFromStreamUpdate(string update)
        {
            var jobject = JObject.Parse(update);

            return jobject["Content"]["Sequence"].Value<int>();
        }

        public void PauseStreaming()
        {
            Logger.InfoFormat("Streaming paused for fixtureName=\"{0}\" fixtureId={1}", Name, Id);
            _pauseStream.Reset();
        }

        public void UnPauseStreaming()
        {
            Logger.InfoFormat("Streaming unpaused for fixtureName=\"{0}\" fixtureId={1}", Name, Id);
            _pauseStream.Set();
        }

        public void StopStreaming()
        {
            try
            {
                Logger.InfoFormat("Stopping streaming for fixtureName=\"{0}\" fixtureId={1}", Name, Id);

                if (IsStreamActive)
                {
                    IsStreamActive = false;

                    var streamSubscriber = StreamSubscriber.GetStreamSubscriber();
                    streamSubscriber.StopStream(Id);

                    if (StreamDisconnected != null)
                    {
                        StreamDisconnected(this, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(string.Format("Problem when stopping stream for fixtureId={0} fixtureName=\"{1}\"", Id, Name), ex);
                Dispose();
            }
        }

        internal void RaiseStreamDisconnected()
        {
            if (StreamDisconnected != null)
            {
                IsStreamActive = false;
                StreamDisconnected(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            StopStreaming();
        }

        public QueueDetails GetQueueDetails()
        {
            var loggingStringBuilder = new StringBuilder();
            var restItems = FindRelationAndFollow("http://api.sportingsolutions.com/rels/stream/amqp", "GetAmqpStream Http Error", loggingStringBuilder);
            var amqpLink =
                restItems.SelectMany(restItem => restItem.Links).First(restLink => restLink.Relation == "amqp");

            var amqpUri = new Uri(amqpLink.Href);

            var queueDetails = new QueueDetails() { Host = amqpUri.Host };

            var userInfo = amqpUri.UserInfo;
            userInfo = HttpUtility.UrlDecode(userInfo);
            if (!String.IsNullOrEmpty(userInfo))
            {
                var userPass = userInfo.Split(':');
                if (userPass.Length > 2)
                {
                    throw new ArgumentException(string.Format("Bad user info in AMQP URI: {0}", userInfo));
                }
                queueDetails.UserName = userPass[0];
                if (userPass.Length == 2)
                {
                    queueDetails.Password = userPass[1];
                }
            }

            var path = amqpUri.AbsolutePath;
            if (!String.IsNullOrEmpty(path))
            {
                queueDetails.Name = path.Substring(path.IndexOf('/', 1) + 1);
                var virtualHost = path.Substring(1, path.IndexOf('/', 1) - 1);

                queueDetails.VirtualHost = virtualHost;
            }

            var port = amqpUri.Port;
            if (port != -1)
            {
                queueDetails.Port = port;
            }

            QueueName = queueDetails.Name;

            return queueDetails;
        }
    }
}
