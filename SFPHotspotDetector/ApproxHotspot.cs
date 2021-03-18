using System;
using System.Collections.Generic;
using System.Linq;
using HotspotDetector;
using OnboardDiagnostics;
using SpatialNetwork;

namespace SFPHotspotDetector
{
    public class ApproxHotspot : Hotspot
    {
        private readonly List<TracePoint> _pointInStartEdge;
        private readonly List<TracePoint> _pointInEndEdge;
        private readonly Dictionary<string, int> _nonEventCountInStartEdge;
        private readonly Dictionary<string, int> _nonEventCountInEndEdge;

        public ApproxHotspot(SpatialPath path, IEnumerable<string> traceNameOnPath,
            SortedList<double, TracePoint> startPoints, SortedList<double, TracePoint> endPoints,
            Dictionary<string, int> startNonEventCount, Dictionary<string, int> endNonEventCount,
            Dictionary<string, int> pointCountInHotspot, Dictionary<string, int> eventCountInHotspot,
            Dictionary<string, int> pointCountInTotal, Dictionary<string, int> eventCountInTotal) : base(path,
            traceNameOnPath,
            pointCountInHotspot, eventCountInHotspot,
            pointCountInTotal, eventCountInTotal)
        {
            _pointInStartEdge =
                (startPoints.Values[0].EdgeDirection ? startPoints.Values : startPoints.Values.Reverse()).ToList();
            _pointInEndEdge =
                (endPoints.Values[0].EdgeDirection ? endPoints.Values.Reverse() : endPoints.Values).ToList();
            _nonEventCountInStartEdge = startNonEventCount;
            _nonEventCountInEndEdge = endNonEventCount;
        }

        private double _confidenceUpperBound = -1;

        public double ConfidenceUpperBound
        {
            get
            {
                if (_confidenceUpperBound < 0) _confidenceUpperBound = CalculateLLRUpperBound();
                return _confidenceUpperBound;
            }
        }

        private double CalculateLLRUpperBound()
        {
            double llr = 0.0;
            foreach (var traceName in TraceNameInHotspot)
            {
                if (!EventCountInHotspot.TryGetValue(traceName, out int np)) np = 0;

                if (!PointCountInHotspot.TryGetValue(traceName, out int mup)) mup = 0;
                if (!_nonEventCountInStartEdge.TryGetValue(traceName, out int startNonEvtCount)) startNonEvtCount = 0;
                if (!_nonEventCountInEndEdge.TryGetValue(traceName, out int endNonEvtCount)) endNonEvtCount = 0;
                mup = mup - startNonEvtCount - endNonEvtCount;

                if (!EventCountInTotal.TryGetValue(traceName, out int ng)) ng = 0;
                if (!PointCountInTotal.TryGetValue(traceName, out int mug)) mug = 0;

                double traceLLR = CalculateLikelihoodRatioOnTrace(np, mup, ng, mug);
                if (double.IsInfinity(traceLLR) || double.IsNaN(traceLLR)) continue;

                llr += traceLLR;
            }

            return llr;
        }

        public IEnumerable<Hotspot> GetChildHotspots()
        {
            var tmpPointCountInHotspot = new Dictionary<string, int>(PointCountInHotspot);
            var tmpEventCountInHotspot = new Dictionary<string, int>(EventCountInHotspot);

            for (int startPtIdx = 0; startPtIdx < _pointInStartEdge.Count; startPtIdx++)
            {
                var startPt = _pointInStartEdge[startPtIdx];
                if (startPtIdx != 0)
                {
                    if (tmpPointCountInHotspot.TryGetValue(startPt.TraceName, out int ptCount))
                        tmpPointCountInHotspot[startPt.TraceName] = ptCount - 1;

                    if (startPt.IsEvent && tmpEventCountInHotspot.TryGetValue(startPt.TraceName, out int evtCount))
                        tmpEventCountInHotspot[startPt.TraceName] = evtCount - 1;
                }

                if (!startPt.IsEvent) continue;

                var startSegment = startPt.EdgeDirection
                    ? new Tuple<double, double>(startPt.EdgeOffset, 100)
                    : new Tuple<double, double>(0, startPt.EdgeOffset);

                var tmpTmpPointCountInHotspot = new Dictionary<string, int>(tmpPointCountInHotspot);
                var tmpTmpEventCountInHotspot = new Dictionary<string, int>(tmpEventCountInHotspot);

                for (int endPtIdx = 0; endPtIdx < _pointInEndEdge.Count; endPtIdx++)
                {
                    var endPt = _pointInEndEdge[endPtIdx];
                    if (endPtIdx != 0)
                    {
                        if (tmpPointCountInHotspot.TryGetValue(endPt.TraceName, out int ptCount))
                            tmpPointCountInHotspot[endPt.TraceName] = ptCount - 1;

                        if (endPt.IsEvent && tmpEventCountInHotspot.TryGetValue(endPt.TraceName, out int evtCount))
                            tmpEventCountInHotspot[endPt.TraceName] = evtCount - 1;
                    }

                    if (!endPt.IsEvent) continue;

                    var tmpPath = new SpatialPath(SPath, startSegment,
                        endPt.EdgeDirection
                            ? new Tuple<double, double>(0, endPt.EdgeOffset)
                            : new Tuple<double, double>(endPt.EdgeOffset, 100));

                    yield return new Hotspot(tmpPath,
                        TraceNameInHotspot,
                        tmpTmpPointCountInHotspot, tmpTmpEventCountInHotspot,
                        PointCountInTotal, EventCountInTotal
                    );
                }
            }
        }
    }
}