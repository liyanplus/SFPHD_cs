using System;
using System.Collections.Generic;
using System.Linq;
using SpatialNetwork;

namespace OnboardDiagnostics
{
    public class Trace : List<TracePoint>
    {
        public string TraceName { get; }
        public List<uint> EdgeIds { get; } = new List<uint>();

        public int EventCount { get; private set; }
        public double EventRatio { get; private set; }

        public Dictionary<uint, List<int>> EdgeEventMapping { get; } =
            new Dictionary<uint, List<int>>();

        public Dictionary<uint, List<int>> EdgeTracePointMapping { get; } =
            new Dictionary<uint, List<int>>();

        public Trace(string name)
        {
            TraceName = name;
        }

        public void AddTracePoint(TracePoint pt)
        {
            Add(pt);

            if (!EdgeTracePointMapping.ContainsKey(pt.EdgeId))
            {
                EdgeTracePointMapping.Add(pt.EdgeId, new List<int>());
            }

            EdgeTracePointMapping[pt.EdgeId].Add(pt.Id);
        }

        public void UpdateEventStatus(Action<TracePoint> updateProc)
        {
            EdgeEventMapping.Clear();
            EventCount = 0;
            
            foreach (var pt in this)
            {
                updateProc(pt);

                if (!pt.IsEvent) continue;

                if (!EdgeEventMapping.ContainsKey(pt.EdgeId))
                {
                    EdgeEventMapping.Add(pt.EdgeId, new List<int>());
                }

                EdgeEventMapping[pt.EdgeId].Add(pt.Id);
                EventCount++;
            }
        }

        public void UpdateEventRatio()
        {
            EventRatio = Convert.ToDouble(EventCount) / (this.Count + 1e-7);
        }

        /// <summary>
        /// Add this <see cref="Trace"/> to certain path.
        /// </summary>
        /// <param name="spatialPath">The path where the trace is added.</param>
        /// <param name="traceNameStartingOrderMapping">The order of the starting edge in the trace.</param>
        /// <param name="pointsOnInnerEdges">The points of the trace on the inner edges of the path.</param>
        /// <param name="pointOnStartSegment">The points of the trace on the starting edge.</param>
        /// <param name="pointOnEndSegment">The points of the trace on the ending edge.</param>
        /// <returns>Whether the trace is on the path</returns>
        public bool AddToPath(SpatialPath spatialPath,
            Dictionary<string, int> traceNameStartingOrderMapping = null,
            List<TracePoint> pointsOnInnerEdges = null,
            List<TracePoint> pointOnStartSegment = null,
            List<TracePoint> pointOnEndSegment = null)
        {
            if (spatialPath == null || spatialPath.Count == 0)
            {
                // if the path is not meaningful,
                // the trace is not on the path.
                return false;
            }

            for (var edgeIdOrder = 0;
                edgeIdOrder <= EdgeIds.Count - spatialPath.Count;
                edgeIdOrder++)
            {
                if (EdgeIds[edgeIdOrder] != spatialPath[0] ||
                    !spatialPath.SequenceEqual(EdgeIds.GetRange(edgeIdOrder, spatialPath.Count)))
                    continue;

                int numPointOnPath = 0;

                if (EdgeTracePointMapping.ContainsKey(spatialPath[0]))
                {
                    foreach (var ptId in EdgeTracePointMapping[spatialPath[0]].Where(
                        ptId => spatialPath.StartSegment.Item1 <= this[Convert.ToInt32(ptId)].EdgeOffset &&
                                this[Convert.ToInt32(ptId)].EdgeOffset <= spatialPath.StartSegment.Item2))
                    {
                        traceNameStartingOrderMapping?.Add(TraceName, edgeIdOrder);
                        pointOnStartSegment?.Add(this[Convert.ToInt32(ptId)]);
                        numPointOnPath++;
                    }
                }

                for (var pathEdgeIdOrder = 1; pathEdgeIdOrder < spatialPath.Count - 1; pathEdgeIdOrder++)
                {
                    if (!EdgeTracePointMapping.ContainsKey(spatialPath[pathEdgeIdOrder])) continue;

                    pointsOnInnerEdges?.AddRange(
                        EdgeTracePointMapping[spatialPath[pathEdgeIdOrder]].Select(
                            ptId => this[Convert.ToInt32(ptId)]));
                    numPointOnPath += EdgeTracePointMapping[spatialPath[pathEdgeIdOrder]].Count;
                }

                if (spatialPath.Count > 1 && EdgeTracePointMapping.ContainsKey(spatialPath[^1]))
                {
                    foreach (var ptId in EdgeTracePointMapping[spatialPath[^1]].Where(ptId =>
                        spatialPath.EndSegment.Item1 <= this[Convert.ToInt32(ptId)].EdgeOffset &&
                        this[Convert.ToInt32(ptId)].EdgeOffset <= spatialPath.EndSegment.Item2))
                    {
                        pointOnEndSegment?.Add(this[Convert.ToInt32(ptId)]);
                        numPointOnPath++;
                    }
                }

                return numPointOnPath > 0;
            }

            return false;
        }
    }
}