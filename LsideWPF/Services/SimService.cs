﻿namespace LsideWPF.Services
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows.Threading;
    using CommunityToolkit.Mvvm.Messaging;
    using CTrue.FsConnect;
    using LsideWPF.Common;
    using LsideWPF.Models;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.FlightSimulator.SimConnect;
    using Serilog;
    using static LsideWPF.Models.Events;

    /// <summary>
    /// Publishes following events:
    ///
    ///   TouchAndGoEventMessage
    ///   LandingEventMessage, on Landing
    ///
    /// Exposes:
    ///   Connected, true if connected to MSFS.
    ///
    ///  Velocity Body Z = Relative Wind Velocity Z + Aircraft Wind Z
    ///  ie Ground Speed = Airspeed + Wind Speed
    ///
    ///  where Z is GPS Ground True Heading
    ///  where Track is GPS Ground True Track
    ///    GPS Ground Speed looks to be a track speed.
    ///
    ///
    /// </summary>
    public class SimService : BindableBase, ISimService, INotifyPropertyChanged
    {
        // ms
        private const int SampleRate = 20;

        // flag to ensure only one SimConnect data packet being processed at a time
        private static bool safeToRead = true;

        private static bool simulationIsPaused = false;

        private static StateMachine stateMachine;

        private readonly ILandingLoggerService landingLogger = App.Current.Services.GetService<ILandingLoggerService>();

        // timer, task reads data from a SimConnection
        private readonly DispatcherTimer dataReadDispatchTimer = new DispatcherTimer();

        // timer, task establishes connection if disconnected
        private readonly DispatcherTimer connectionDispatchTimer = new DispatcherTimer();

        // Establishes simConnect connection & Update scaneris
        private readonly BackgroundWorker backgroundWorkerConnection = new BackgroundWorker();

        private readonly FsConnect fsConnect = new FsConnect();
        private readonly List<SimVar> definition = new List<SimVar>();

        private int planeInfoDefinitionId;

        private bool running = false;

        private bool connected = false;

        private bool crashed = false;

        public SimService()
        {
            // do a 'Connection check' every 1 sec
            this.connectionDispatchTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000);
            this.connectionDispatchTimer.Tick += new EventHandler(this.ConnectionCheckEventHandler_OnTick);

            // establishes simConnect connection, when required
            this.backgroundWorkerConnection.DoWork += this.BackgroundWorkerConnection_DoWork;
            this.connectionDispatchTimer.Start();

            // Read SimConnect Data every 20 msec
            this.dataReadDispatchTimer.Interval = new TimeSpan(0, 0, 0, 0, SampleRate);
            this.dataReadDispatchTimer.Tick += new EventHandler(this.DataReadEventHandler_OnTick);

            // register the read SimConnect data callback procedure
            this.fsConnect.FsDataReceived += HandleReceivedFsData;
            this.fsConnect.PauseStateChanged += FsConnect_PauseStateChanged;

            this.fsConnect.Crashed += this.FsConnect_Crashed;
            this.fsConnect.FlightLoaded += this.FsConnect_Loaded;

            // properties to be read from SimConnect
            // list additions need to track 1:1 with PlaneInfoResponse structure
            this.definition.Add(new SimVar(FsSimVar.Title, null, SIMCONNECT_DATATYPE.STRING256));
            this.definition.Add(new SimVar(FsSimVar.SimOnGround, FsUnit.Bool, SIMCONNECT_DATATYPE.INT32));

            // Relative Wind component in aircraft lateral (X) axis.
            this.definition.Add(new SimVar(FsSimVar.RelativeWindVelocityBodyX, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));

            // Relative Wind component in aircraft longitudinal(Z) axis.
            this.definition.Add(new SimVar(FsSimVar.RelativeWindVelocityBodyZ, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));

            this.definition.Add(new SimVar(FsSimVar.AirspeedIndicated, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));

            // Speed relative to the earths surface.
            this.definition.Add(new SimVar(FsSimVar.GroundVelocity, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));

            // lateral speed + to the right
            this.definition.Add(new SimVar(FsSimVar.VelocityBodyX, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));

            // speed along airplane axis
            this.definition.Add(new SimVar(FsSimVar.VelocityBodyZ, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.GForce, FsUnit.GForce, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.PlaneTouchdownNormalVelocity, FsUnit.FeetPerSecond, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.PlaneAltitudeAboveGround, FsUnit.Feet, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.PlaneLatitude, FsUnit.Degree, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.PlaneLongitude, FsUnit.Degree, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.PlaneBankDegrees, FsUnit.Degree, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.OnAnyRunway, FsUnit.Bool, SIMCONNECT_DATATYPE.INT32));
            this.definition.Add(new SimVar(FsSimVar.AtcRunwayAirportName, null, SIMCONNECT_DATATYPE.STRING256));
            this.definition.Add(new SimVar(FsSimVar.AtcRunwaySelected, FsUnit.Bool, SIMCONNECT_DATATYPE.INT32));
            this.definition.Add(new SimVar(FsSimVar.AtcRunwayTdpointRelativePositionX, FsUnit.Feet, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.AtcRunwayTdpointRelativePositionZ, FsUnit.Feet, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.VerticalSpeed, FsUnit.FeetPerMinute, SIMCONNECT_DATATYPE.FLOAT64));

            this.definition.Add(new SimVar(FsSimVar.GearPosition, FsUnit.Enum, SIMCONNECT_DATATYPE.INT32));
            this.definition.Add(new SimVar(FsSimVar.LightLandingOn, FsUnit.Bool, SIMCONNECT_DATATYPE.INT32));

            this.definition.Add(new SimVar(FsSimVar.GpsGroundTrueHeading, FsUnit.Degree, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.AtcRunwayHeadingDegreesTrue, FsUnit.Degree, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.AircraftWindZ, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.AircraftWindX, FsUnit.Knots, SIMCONNECT_DATATYPE.FLOAT64));
            this.definition.Add(new SimVar(FsSimVar.AtcRunwayRelativePositionZ, FsUnit.Feet, SIMCONNECT_DATATYPE.FLOAT64));
        }

        private event EventHandler<FlightEventArgs> EventHandler;

        private enum Requests
        {
            PlaneInfoRequest = 0,
        }

        public bool Crashed
        {
            get { return this.crashed; }
            private set { this.crashed = value; }
        }

        public bool Connected
        {
            get { return this.connected; }
            private set { this.connected = value; }
        }

        /// <summary>
        /// Publishes Messages driven by the Event Type and flightParameters.
        ///
        /// </summary>
        /// <param name="sender">not used.</param>
        /// <param name="e">Provides the EventType and flightParameters.</param>
        protected virtual void FlightEventHandler(object sender, FlightEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            switch (e.EventType())
            {
                case EventType.TakeOffEvent:
                    Log.Debug("Take Off Event");

                    // do nothing
                    break;

                case EventType.TouchAndGoEvent:
                    Log.Debug("Touch And Go Event");

                    // update & reveil viewModel
                    if (Properties.Settings.Default.enableTouchAndGo)
                    {
                        var logEntry = this.landingLogger.GetLastLanding();
                        WeakReferenceMessenger.Default.Send(new TouchAndGoEventMessage(logEntry));
                    }

                    break;

                case EventType.LandingEvent:
                    Log.Debug("Landing Event");

                    // update & reveil viewModels
                    {
                        var logEntry = this.landingLogger.GetLastLanding();
                        WeakReferenceMessenger.Default.Send(new LandingEventMessage(logEntry));
                    }

                    break;

                case EventType.SlipLoggingEvent:
                    Log.Debug("Slip Logging Event");

                    // TODO - publish the logging data
                    break;

                default:
                    break;
            }
        }

        private static void HandleReceivedFsData(object sender, FsDataReceivedEventArgs e)
        {
            if (simulationIsPaused)
            {
                return;
            }

            if (!safeToRead)
            {
                // already processing a packet, skip this one
                Log.Debug("lost one");
                return;
            }

            safeToRead = false;
            try
            {
                if (e.RequestId == (uint)Requests.PlaneInfoRequest)
                {
                    var planeInfoResponse = (PlaneInfoResponse)e.Data.FirstOrDefault();
                    planeInfoResponse.Id = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    stateMachine.Handle(planeInfoResponse);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex.Message);
            }

            safeToRead = true;
        }

        private static void FsConnect_PauseStateChanged(object sender, PauseStateChangedEventArgs e)
        {
            simulationIsPaused = e.Paused;
        }

        private void FsConnect_Crashed(object sender, EventArgs e)
        {
            this.crashed = true;
        }

        private void FsConnect_Loaded(object sender, EventArgs e)
        {
            this.crashed = false;
        }

        private void DataReadEventHandler_OnTick(object sender, EventArgs e)
        {
            try
            {
                this.fsConnect.RequestData((int)Requests.PlaneInfoRequest, this.planeInfoDefinitionId);
            }
            catch
            {
            }
        }

        private void ConnectionCheckEventHandler_OnTick(object sender, EventArgs e)
        {
            if (!this.backgroundWorkerConnection.IsBusy)
            {
                this.backgroundWorkerConnection.RunWorkerAsync();
            }

            bool oldConnected = this.Connected;

            if (this.fsConnect.Connected)
            {
                if (!this.running)
                {
                    this.EventHandler += this.FlightEventHandler;

                    stateMachine = new StateMachine(new ConnectedState(), this.EventHandler);
                    this.running = true;
                    this.dataReadDispatchTimer.Start();
                }

                this.Connected = true;
            }
            else
            {
                this.Connected = false;
            }

            if (this.Connected != oldConnected)
            {
                this.OnPropertyChanged(nameof(this.Connected));
            }
        }

        // If not connected , Connect
        private void BackgroundWorkerConnection_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            if (!this.fsConnect.Connected)
            {
                try
                {
                    // connect & register data of interest
                    this.fsConnect.Connect("TestApp", "localhost", 500, SimConnectProtocol.Ipv4);
                    this.planeInfoDefinitionId = this.fsConnect.RegisterDataDefinition<PlaneInfoResponse>(Requests.PlaneInfoRequest, this.definition);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
