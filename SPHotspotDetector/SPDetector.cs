using System;
using System.Collections.Generic;
using System.Linq;
using HotspotDetector;
using OnboardDiagnostics;
using SpatialNetwork;

namespace SPHotspotDetector
{
    public class SPDetector : AbstractDetector
    {
        public SPDetector(TraceDB traces,
            int supportThreshold, double confidenceThreshold,
            string confidenceType) : base(supportThreshold, confidenceThreshold, confidenceType)
        {
            foreach (var startTrace in traces.Values)
            {
                foreach (var startPoint in startTrace.Where(pt => pt.IsEvent))
                {
                    foreach (var endTrace in traces.Values)
                    {
                        foreach (var endPoint in endTrace.Where(pt => pt.IsEvent))
                        {
                            var road = RoadSystem.RoadSystemFactory.ConstructRoadSystem();
                            var sp = road.GetShortestPath(
                                startPoint.Longitude, startPoint.Latitude, startPoint.EdgeDirection,
                                endPoint.Longitude, endPoint.Latitude, endPoint.EdgeDirection);

                            Dictionary<string, int> traceNameStringOrderMapping = new Dictionary<string, int>();
                            List<TracePoint> pointOnStartSegment = new List<TracePoint>();
                            List<TracePoint> pointOnInnerEdges = new List<TracePoint>();
                            List<TracePoint> pointOnEndSegment = new List<TracePoint>();
                            var traceNames = traces.GetTraceNameOnPath(sp,
                                traceNameStringOrderMapping, pointOnStartSegment,
                                pointOnInnerEdges, pointOnEndSegment);

                            if (traceNames.Count < SupportThreshold) continue;
                            
                            var hotspot = new Hotspot(sp);
                            hotspot.AddTraces(traces, traceNames,
                                pointOnInnerEdges, pointOnStartSegment, pointOnEndSegment);

                            if (hotspot.Support >= SupportThreshold && hotspot.Confidence >= ConfidenceThreshold)
                            {
                                Hotspots.Add(hotspot);
                            }
                        }
                    }
                }
            }
        }

        public override AbstractDetector CreateDetector(TraceDB traces, int support, double confidence,
            string confidenceType)
        {
            return new SPDetector(traces, support, confidence, confidenceType);
        }
    }
}