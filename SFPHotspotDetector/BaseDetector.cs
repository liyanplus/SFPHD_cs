using System;
using System.Collections.Generic;
using System.Linq;
using HotspotDetector;
using OnboardDiagnostics;
using SpatialNetwork;

namespace SFPHotspotDetector
{
    public class BaseDetector : AbstractDetector
    {
        public BaseDetector(TraceDB traces,
            int supportThreshold, double confidenceThreshold,
            string confidenceType) : base(supportThreshold, confidenceThreshold, confidenceType)
        {
            foreach (var trace in traces.Values)
            {
                foreach (var startPoint in trace.Where(pt => pt.IsEvent))
                {
                    var startSegment = startPoint.EdgeDirection
                        ? new Tuple<double, double>(startPoint.EdgeOffset, 100.0)
                        : new Tuple<double, double>(0.0, startPoint.EdgeOffset);
                    var startEdgeOrderInTrace = trace.EdgeIds.IndexOf(startPoint.EdgeId);

                    if (startEdgeOrderInTrace < 0 || startEdgeOrderInTrace >= trace.EdgeIds.Count - 1) continue;

                    var traceNameStartingOrderMapping = new Dictionary<string, int>();
                    var pointOnStartSegment = new List<TracePoint>();
                    var pointOnInnerEdges = new List<TracePoint>();
                    var pointOnEndSegment = new List<TracePoint>();

                    var path = new SpatialPath(
                        new[] {startPoint.EdgeId, trace.EdgeIds[startEdgeOrderInTrace + 1]},
                        startSegment);

                    traces.GetTraceNameOnPath(
                        path,
                        traceNameStartingOrderMapping,
                        pointOnStartSegment, pointOnInnerEdges, pointOnEndSegment);

                    ExplorePathProc(
                        path,
                        traces,
                        traceNameStartingOrderMapping,
                        pointOnStartSegment,
                        pointOnInnerEdges,
                        pointOnEndSegment);
                }
            }
        }

        private void ExplorePathProc(SpatialPath basePath, TraceDB traces,
            Dictionary<string, int> traceNameStartingOrderMapping,
            List<TracePoint> startPoints,
            List<TracePoint> innerPoints,
            List<TracePoint> endPoints)
        {
            // check subpaths ending at the final edge
            foreach (var (pId, subPath) in GetChildPath(traces, basePath, endPoints))
            {
                var hotspot = new Hotspot(subPath);
                var tmpEndPoints =
                    endPoints[pId].EdgeDirection
                        ? endPoints.GetRange(0, pId + 1)
                        : endPoints.GetRange(pId, endPoints.Count);
                hotspot.AddTraces(traces, new HashSet<string>(traceNameStartingOrderMapping.Keys),
                    innerPoints,
                    startPoints,
                    tmpEndPoints);

                if (hotspot.Support >= SupportThreshold && hotspot.Confidence >= ConfidenceThreshold)
                {
                    Hotspots.Add(hotspot);
                }
            }

            // explore extended paths
            foreach (var (extendedPath, startingOrderMapping) in
                traces.GetExtendedPathWithStartingOrderMapping(basePath,
                    traceNameStartingOrderMapping, SupportThreshold))
            {
                var tmpStartPoints =
                    startPoints.Where(pt => startingOrderMapping.ContainsKey(pt.TraceName)).ToList();

                var tmpEndPoints = new List<TracePoint>();
                foreach (var (traceName, startingOrder) in startingOrderMapping)
                {
                    var trace = traces[traceName];
                    if (trace.EdgeTracePointMapping.TryGetValue(extendedPath[^2], out var pointIds))
                    {
                        innerPoints.AddRange(pointIds.Select(id => trace[id]));
                    }

                    if (trace.EdgeTracePointMapping.TryGetValue(extendedPath[^1], out pointIds))
                    {
                        tmpEndPoints.AddRange(
                            pointIds.Select(id => trace[id])
                                .Where(pt =>
                                    extendedPath.EndSegment.Item1 <= pt.EdgeOffset &&
                                    pt.EdgeOffset <= extendedPath.EndSegment.Item2));
                    }
                }

                ExplorePathProc(extendedPath, traces, startingOrderMapping,
                    tmpStartPoints, innerPoints, tmpEndPoints);
            }
        }


        private static IEnumerable<Tuple<int, SpatialPath>> GetChildPath(
            TraceDB traces,
            SpatialPath path,
            IEnumerable<TracePoint> pointOnEndSegment)
        {
            int ptOrder = -1;
            foreach (var endPoint in pointOnEndSegment)
            {
                ptOrder++;
                if (!endPoint.IsEvent) continue;
                yield return new Tuple<int, SpatialPath>(
                    ptOrder,
                    new SpatialPath(path,
                        path.StartSegment,
                        endPoint.EdgeDirection
                            ? new Tuple<double, double>(0.0, endPoint.EdgeOffset)
                            : new Tuple<double, double>(endPoint.EdgeOffset, 100.0)));
            }
        }

        public override AbstractDetector CreateDetector(TraceDB traces, int support, double confidence,
            string confidenceType)
        {
            return new BaseDetector(traces, support, confidence, confidenceType);
        }
    }
}