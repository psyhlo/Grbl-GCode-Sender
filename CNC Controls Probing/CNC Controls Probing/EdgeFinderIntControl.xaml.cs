﻿/*
 * EdgeFinderIntControl.xaml.cs - part of CNC Probing library
 *
 * v0.27 / 2020-09-26 / Io Engineering (Terje Io)
 *
 */

/*

Copyright (c) 2020, Io Engineering (Terje Io)
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System;
using System.Windows;
using System.Windows.Controls;
using CNC.Core;
using CNC.GCode;

namespace CNC.Controls.Probing
{
    // D |-----| C
    //   |  Z  |
    // A | ----| B

    /// <summary>
    /// Interaction logic for EdgeFinderIntControl.xaml
    /// </summary>
    public partial class EdgeFinderIntControl : UserControl, IProbeTab
    {
        private volatile bool isCancelled = false;
        private AxisFlags axisflags = AxisFlags.None;
        private double[] af = new double[3];

        public EdgeFinderIntControl()
        {
            InitializeComponent();
        }

        public ProbingType ProbingType { get { return ProbingType.EdgeFinderInternal; } }

        public void Activate()
        {
            (DataContext as ProbingViewModel).Instructions = "Click edge, corner or center in image above to select probing action.\nMove the probe to above the position indicated by green dot before start.";
        }

        public void Start(bool preview = false)
        {
            var probing = DataContext as ProbingViewModel;

            if (!probing.ValidateInput())
                return;

            if (probing.ProbeEdge == Edge.None)
            {
                MessageBox.Show("Select edge or corner to probe by clicking on the relevant part of the image above.", "Edge finder", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if (!probing.Program.Init())
                return;

            isCancelled = false;

            if (preview)
                probing.StartPosition.Zero();

            probing.Program.Add(string.Format("G91F{0}", probing.ProbeFeedRate.ToInvariantString()));

            switch (probing.ProbeEdge)
            {
                case Edge.A:
                    if (!AddCorner(probing, true, true))
                        return;
                    break;

                case Edge.B:
                    if (!AddCorner(probing, false, true))
                        return;
                    break;

                case Edge.C:
                    if (!AddCorner(probing, false, false))
                        return;
                    break;

                case Edge.D:
                    if (!AddCorner(probing, true, false))
                        return;
                    break;

                case Edge.Z:
                    axisflags = AxisFlags.Z;
                    af[GrblConstants.Z_AXIS] = 1d;
                    probing.Program.AddProbingAction(AxisFlags.Z, true);
                    break;

                case Edge.AD:
                    AddEdge(probing, 'X', true);
                    break;

                case Edge.AB:
                    AddEdge(probing, 'Y', true);
                    break;

                case Edge.CB:
                    AddEdge(probing, 'X', false);
                    break;

                case Edge.CD:
                    AddEdge(probing, 'Y', false);
                    break;
            }

            if (preview)
                probing.PreviewText = probing.Program.ToString().Replace("G53", string.Empty);
            else
            {
                probing.Program.Execute(true);
                OnCompleted();
            }
        }

        private void AddEdge(ProbingViewModel probing, char axisletter, bool negative)
        {
            int axis = GrblInfo.AxisLetterToIndex(axisletter);

            af[axis] = negative ? -1d : 1d;

            axisflags = GrblInfo.AxisLetterToFlag(axisletter);

            var rapidto = new Position(probing.StartPosition);
            rapidto.Values[axis] -= probing.XYClearance * af[axis];
            rapidto.Z -= probing.Depth;

            probing.Program.AddRapidToMPos(rapidto, axisflags);
            probing.Program.AddRapidToMPos(rapidto, AxisFlags.Z);

            probing.Program.AddProbingAction(axisflags, negative);

            rapidto.Values[axis] = probing.StartPosition.Values[axis] - probing.XYClearance * af[axis];
            probing.Program.AddRapidToMPos(rapidto, axisflags);
            probing.Program.AddRapidToMPos(probing.StartPosition, AxisFlags.Z);
        }

        private bool AddCorner(ProbingViewModel probing, bool negx, bool negy)
        {
            af[GrblConstants.X_AXIS] = negx ? -1d : 1d;
            af[GrblConstants.Y_AXIS] = negy ? -1d : 1d;

            axisflags = AxisFlags.X | AxisFlags.Y;

            var XYClearance = Math.Min(probing.XYClearance, probing.Offset);

            if (XYClearance < probing.XYClearance && MessageBox.Show("XY Clearance is less than Offset, run anyway?", "GCode Sender", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
                return false;

            var rapidto = new Position(probing.StartPosition);
            rapidto.X -= XYClearance * af[GrblConstants.X_AXIS];
            rapidto.Y -= probing.Offset * af[GrblConstants.Y_AXIS];
            rapidto.Z -= probing.Depth;

            probing.Program.AddRapidToMPos(rapidto, AxisFlags.X | AxisFlags.Y);
            probing.Program.AddRapidToMPos(rapidto, AxisFlags.Z);

            probing.Program.AddProbingAction(AxisFlags.X, negx);

            probing.Program.AddRapidToMPos(rapidto, AxisFlags.X);
            rapidto.X = probing.StartPosition.X - probing.Offset * af[GrblConstants.X_AXIS];
            rapidto.Y = probing.StartPosition.Y - XYClearance * af[GrblConstants.Y_AXIS];
            probing.Program.AddRapidToMPos(rapidto, AxisFlags.X | AxisFlags.Y);

            probing.Program.AddProbingAction(AxisFlags.Y, negy);

            probing.Program.AddRapidToMPos(rapidto, AxisFlags.Y);
            probing.Program.AddRapidToMPos(probing.StartPosition, AxisFlags.Z);

            return true;
        }

        public void Stop()
        {
            isCancelled = true;
            (DataContext as ProbingViewModel).Program.Cancel();
        }

        private void OnCompleted()
        {
            bool ok;

            var probing = DataContext as ProbingViewModel;

            if ((ok = probing.IsSuccess && probing.Positions.Count > 0))
            {
                int p = 0;
                Position pos = new Position(probing.StartPosition);

                foreach (int i in axisflags.ToIndices())
                    pos.Values[i] = probing.Positions[p++].Values[i] + (i == GrblConstants.Z_AXIS ? 0d : probing.ProbeDiameter / 2d * af[i]);

                if (double.IsNaN(pos.Z))
                {
                    probing.Grbl.IsJobRunning = false;
                    probing.Program.End("Probing failed, machine position not known");
                    return;
                }

                if (probing.ProbeZ && axisflags != AxisFlags.Z)
                {
                    Position pz = new Position(pos);

                    pz.X += probing.ProbeDiameter / 2d * af[GrblConstants.X_AXIS];
                    pz.Y += probing.ProbeDiameter / 2d * af[GrblConstants.Y_AXIS];
                    if ((ok = !isCancelled && probing.GotoMachinePosition(pz, axisflags)))
                    {
                        ok = probing.WaitForResponse(probing.FastProbe + "Z-" + probing.Depth.ToInvariantString());
                        ok = ok && !isCancelled && probing.WaitForResponse(probing.RapidCommand + "Z" + probing.LatchDistance.ToInvariantString());
                        ok = ok && !isCancelled && probing.RemoveLastPosition();
                        if ((ok = ok && !isCancelled && probing.WaitForResponse(probing.SlowProbe + "Z-" + probing.Depth.ToInvariantString())))
                        {
                            pos.Z = probing.Grbl.ProbePosition.Z;
                            ok = !isCancelled && probing.GotoMachinePosition(probing.StartPosition, AxisFlags.Z);
                        }
                    }
                }

                ok = ok && !isCancelled && probing.GotoMachinePosition(pos, axisflags);

                if (probing.ProbeZ)
                    axisflags |= AxisFlags.Z;

                if (ok)
                {
                    if (probing.CoordinateMode == ProbingViewModel.CoordMode.G92)
                    {
                        if ((ok = !isCancelled && probing.GotoMachinePosition(pos, AxisFlags.Z)))
                        {
                            pos.X = pos.Y = 0d;
                            pos.Z = probing.WorkpieceHeight + probing.TouchPlateHeight;
                            probing.Grbl.ExecuteCommand("G92" + pos.ToString(axisflags));
                            if (!isCancelled && axisflags.HasFlag(AxisFlags.Z))
                                probing.GotoMachinePosition(probing.StartPosition, AxisFlags.Z);
                        }
                    }
                    else
                    {
                        pos.Z -= probing.WorkpieceHeight + probing.TouchPlateHeight + probing.Grbl.ToolOffset.Z;
                        probing.Grbl.ExecuteCommand(string.Format("G10L2P{0}{1}", probing.CoordinateSystem, pos.ToString(axisflags)));
                    }
                }
            }

            if (!probing.Grbl.IsParserStateLive && probing.CoordinateMode == ProbingViewModel.CoordMode.G92)
                probing.Grbl.ExecuteCommand("$G");

            probing.Grbl.IsJobRunning = false;
            probing.Program.End(ok ? "Probing completed" : "Probing failed");
        }

        private void start_Click(object sender, RoutedEventArgs e)
        {
            Start((DataContext as ProbingViewModel).PreviewEnable);
        }

        private void stop_Click(object sender, RoutedEventArgs e)
        {
            Stop();
        }
    }
}
