﻿//SCCM Client Center Automation Library (SCCMCliCtr.automation)
//Copyright (c) 2018 by Roger Zander

//This program is free software; you can redistribute it and/or modify it under the terms of the GNU Lesser General Public License as published by the Free Software Foundation; either version 3 of the License, or any later version. 
//This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public License for more details. 
//GNU General Public License: http://www.gnu.org/licenses/lgpl.html

#define CM2012
#define CM2007

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Diagnostics;
using System.Threading;

using System.Collections.ObjectModel;


namespace sccmclictr.automation.functions
{
    /// <summary>
    /// Class monitoring.
    /// </summary>
    public class monitoring : baseInit
    {
        internal Runspace remoteRunspace;
        internal TraceSource pSCode;
        internal ccm baseClient;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /*
        public new void Dispose()
        {
            if (remoteRunspace != null)
                remoteRunspace.Dispose();
            if (baseClient != null)
                baseClient.Dispose();
            AsynchronousScript = null;
        }*/

        //Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="monitoring"/> class.
        /// </summary>
        /// <param name="RemoteRunspace">The remote runspace.</param>
        /// <param name="PSCode">The PowerShell code.</param>
        /// <param name="oClient">A CCM Client object.</param>
        public monitoring(Runspace RemoteRunspace, TraceSource PSCode, ccm oClient)
            : base(RemoteRunspace, PSCode)
        {
            remoteRunspace = RemoteRunspace;
            pSCode = PSCode;
            baseClient = oClient;
            AsynchronousScript = new runScriptAsync(RemoteRunspace, pSCode);

        }

        /// <summary>
        /// The asynchronous script
        /// </summary>
        public runScriptAsync AsynchronousScript;

        /// <summary>
        /// Class runScriptAsync.
        /// </summary>
        public class runScriptAsync : IDisposable
        {
            private AutoResetEvent _autoResetEvent = new AutoResetEvent(false);
            internal Runspace _remoteRunspace;
            internal Pipeline pipeline;
            internal RunspaceConnectionInfo _connectionInfo;
            internal TraceSource pSCode;

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                if (_remoteRunspace != null)
                    _remoteRunspace.Dispose();
                if (pipeline != null)
                    pipeline.Dispose();
                _connectionInfo = null;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="runScriptAsync"/> class.
            /// </summary>
            /// <param name="remoteRunspace">The remote runspace.</param>
            /// <param name="PSCode">TraceSource</param>
            public runScriptAsync(Runspace remoteRunspace, TraceSource PSCode)
            {
                if (_remoteRunspace == null)
                {
                    _connectionInfo = remoteRunspace.ConnectionInfo;
                    _remoteRunspace = RunspaceFactory.CreateRunspace(_connectionInfo);
                }
                else
                {
                    _remoteRunspace = RunspaceFactory.CreateRunspace(remoteRunspace.ConnectionInfo);
                }

                pSCode = PSCode;
            }

            /// <summary>
            /// Connects this instance.
            /// </summary>
            public void Connect()
            {
                if (_remoteRunspace.RunspaceStateInfo.State != RunspaceState.Opened)
                {
                    _remoteRunspace = RunspaceFactory.CreateRunspace(_connectionInfo);
                    _remoteRunspace.Open();
                }

                pipeline = _remoteRunspace.CreatePipeline();

                pipeline.Output.DataReady += new EventHandler(Output_DataReady);
                pipeline.StateChanged += new EventHandler<PipelineStateEventArgs>(pipeline_StateChanged);
            }

            void pipeline_StateChanged(object sender, PipelineStateEventArgs e)
            {
                if (e.PipelineStateInfo.State != PipelineState.Running)
                {
                    pipeline.Output.Close();
                    pipeline.Input.Close();
                }

                if (e.PipelineStateInfo.State == PipelineState.Completed)
                {
                    if (this.Finished != null)
                        this.Finished.Invoke(pipeline.Output, new EventArgs());
                }

                _autoResetEvent.Set();
            }


            /// <summary>
            /// Output data arrived.
            /// </summary>
            /// <param name="sender">Contains the result as List of strings.</param>
            /// <param name="EventArgs"/>The instance containing the event data.</param>
            internal void Output_DataReady(object sender, EventArgs e)
            {
                PipelineReader<PSObject> output = sender as PipelineReader<PSObject>;
                List<string> lStringOutput = new List<string>();
                List<object> lOutput = new List<object>();
                if (output != null)
                {
                    Collection<PSObject> pso = output.NonBlockingRead();
                    if (pso.Count > 0)
                    {
                        //Forward the Raw data...
                        if (this.RawOutput != null)
                            this.RawOutput.Invoke(pso, e);

                        foreach (PSObject PO in pso)
                        {
                            if (PO != null)
                            {
                                lStringOutput.Add(PO.ToString());


                                foreach (string sType in PO.TypeNames)
                                {
                                    ConvertThroughString cts = new ConvertThroughString();
                                    Type objectType = Type.GetType(sType.Replace("Deserialized.", ""));

                                    if (cts.CanConvertFrom(PO, objectType))
                                    {
                                        try
                                        {
                                            lOutput.Add(cts.ConvertFrom(PO, objectType, null, true));
                                            break;
                                        }
                                        catch
                                        {
                                            try
                                            {
                                                System.Collections.Hashtable HT = new System.Collections.Hashtable();
                                                foreach (PSPropertyInfo PI in PO.Properties)
                                                {
                                                    try
                                                    {
                                                        HT.Add(PI.Name, PI.Value.ToString());
                                                    }
                                                    catch { }
                                                }
                                                lOutput.Add(HT);
                                                break;
                                            }
                                            catch { }
                                            //break;
                                        }
                                    }
                                    else
                                    {
                                    }

                                }
                            }
                        }
                    }

                    if (output.EndOfPipeline & output.IsOpen)
                    {
                        output.Close();
                    }
                }
                else
                {
                    PipelineReader<object> error = sender as PipelineReader<object>;

                    if (error != null)
                    {
                        while (error.Count > 0)
                        {
                            lStringOutput.Add(error.Read().ToString());
                            lOutput.Add(error.Read());
                        }

                        if (error.EndOfPipeline)
                        {
                            error.Close();
                        }

                        if (this.ErrorOccured != null)
                            this.ErrorOccured.Invoke(sender, e);

                        _autoResetEvent.Set();
                    }
                }

                //Forward output as ListOfStrings
                if (this.StringOutput != null)
                    this.StringOutput.Invoke(lStringOutput, e);
                if (this.TypedOutput != null)
                    this.TypedOutput.Invoke(lOutput, e);
                _autoResetEvent.Set();
            }

            /// <summary>
            /// Powershell Script to execute
            /// </summary>
            public string Command
            {
                get
                {
                    if (pipeline == null)
                        this.Connect();
                    return pipeline.Commands.ToString();
                }
                set
                {
                    if (pipeline == null)
                        this.Connect();
                    pipeline.Commands.Clear();
                    pipeline.Commands.AddScript(value);

                    //Trace Powershell code
                    pSCode.TraceInformation(value);
                    
                }

            }

            /// <summary>
            /// Runs this instance.
            /// </summary>
            public void Run()
            {
                if (pipeline == null)
                    this.Connect();
                pipeline.InvokeAsync();
                pipeline.Input.Close();
            }

            /// <summary>
            /// Runs and waits.
            /// </summary>
            public void RunWait()
            {
                if (pipeline == null)
                    this.Connect();
                pipeline.InvokeAsync();
                pipeline.Input.Close();
                do
                {
                    _autoResetEvent.WaitOne(500);
                }
                while (pipeline.Output.IsOpen);

            }

            /// <summary>
            /// Stop the pieline reader
            /// </summary>
            public void Stop()
            {
                if (pipeline != null)
                {
                    if (pipeline.Output.IsOpen)
                        pipeline.Output.Close();
                }
            }

            /// <summary>
            /// Check if PS output is open..
            /// </summary>
            public bool isRunning
            {
                get
                {
                    if (pipeline != null)
                        if (pipeline.Output != null)
                            return pipeline.Output.IsOpen;
                    return false;
                }
            }

            /// <summary>
            /// Close the pipeline
            /// </summary>
            public void Close()
            {
                try
                {
                    if (pipeline != null)
                    {
                        if (pipeline.Output.IsOpen)
                            pipeline.Output.Close();

                        pipeline.Output.DataReady -= Output_DataReady;
                        pipeline.StateChanged -= pipeline_StateChanged;

                        _remoteRunspace.Close();
                    }
                }
                catch { }
            }

            #pragma warning disable 1591 // Disable warnings about missing XML comments

            public event EventHandler StringOutput;
            public event EventHandler RawOutput;
            public event EventHandler TypedOutput;
            public event EventHandler Finished;
            public event EventHandler ErrorOccured;

            #pragma warning restore 1591 // Enable warnings about missing XML comments
        }
    }


}
