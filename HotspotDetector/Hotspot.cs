using System;
using System.Collections.Generic;
using System.Linq;
using OnboardDiagnostics;
using SpatialNetwork;

namespace HotspotDetector
{
    public class Hotspot
    {
        public SpatialPath SPath { get; }
        protected List<string> TraceNameInHotspot { get; }

        protected Dictionary<string, int> PointCountInHotspot { get; }
        protected Dictionary<string, int> EventCountInHotspot { get; }

        protected Dictionary<string, int> PointCountInTotal { get; }
        protected Dictionary<string, int> EventCountInTotal { get; }

        public double StatisticP { get; set; } = 1.0;

        public int Support => TraceNameInHotspot.Count;

        private double _confidence = -1.0;

        public double Confidence
        {
            get
            {
                if (_confidence < 0)
                {
                    switch (ConfidenceType)
                    {
                        case "LLR":
                            _confidence = CalculateLikelihoodRatio();
                            break;
                        case "DensityRatio":
                            _confidence = CalculateDensityRatio();
                            break;
                        default:
                            _confidence = CalculateLikelihoodRatio();
                            break;
                    }
                }

                return _confidence;
            }
        }

        public string ConfidenceType { get; set; }

        public Hotspot(SpatialPath path)
        {
            this.SPath = path;
            TraceNameInHotspot = new List<string>();
            PointCountInHotspot = new Dictionary<string, int>();
            EventCountInHotspot = new Dictionary<string, int>();
            PointCountInTotal = new Dictionary<string, int>();
            EventCountInTotal = new Dictionary<string, int>();
        }

        public Hotspot(SpatialPath path,
            IEnumerable<string> traceNameInHotspot,
            Dictionary<string, int> pointCountInHotspot, Dictionary<string, int> eventCountInHotspot,
            Dictionary<string, int> pointCountInTotal, Dictionary<string, int> eventCountInTotal)
        {
            SPath = path;
            TraceNameInHotspot = new List<string>(traceNameInHotspot);
            PointCountInHotspot = new Dictionary<string, int>(pointCountInHotspot);
            EventCountInHotspot = new Dictionary<string, int>(eventCountInHotspot);
            PointCountInTotal = new Dictionary<string, int>(pointCountInTotal);
            EventCountInTotal = new Dictionary<string, int>(eventCountInTotal);
        }

        private double CalculateLikelihoodRatio()
        {
            double llr = 0.0;

            foreach (var traceName in TraceNameInHotspot)
            {
                if (!EventCountInHotspot.TryGetValue(traceName, out int np)) np = 0;
                if (!PointCountInHotspot.TryGetValue(traceName, out int mup)) mup = 0;
                if (!EventCountInTotal.TryGetValue(traceName, out int ng)) ng = 0;
                if (!PointCountInTotal.TryGetValue(traceName, out int mug)) mug = 0;

                double traceLLR = CalculateLikelihoodRatioOnTrace(np, mup, ng, mug);
                if (double.IsInfinity(traceLLR) || double.IsNaN(traceLLR)) continue;

                llr += traceLLR;
            }

            return llr;
        }

        protected double CalculateLikelihoodRatioOnTrace(int np, int mup, int ng, int mug)
        {
            double eventRatioInHotspot = (np == 0 ? double.Epsilon : Convert.ToDouble(np))
                                         / (mup == 0 ? double.Epsilon : Convert.ToDouble(mup));
            double eventRatioOutOfHotspot = (ng - np == 0 ? double.Epsilon : Convert.ToDouble(ng - np))
                                            / (mug - mup == 0 ? double.Epsilon : Convert.ToDouble(mug - mup));
            double eventRatioInTotal = (ng == 0 ? double.Epsilon : Convert.ToDouble(ng))
                                       / (mug == 0 ? double.Epsilon : Convert.ToDouble(mug));

            double traceLLR;
            if (eventRatioInHotspot > eventRatioOutOfHotspot)
            {
                traceLLR = np * Math.Log(eventRatioInHotspot)
                           + (mup - np) * Math.Log(1 - eventRatioInHotspot)
                           + (ng - np) * Math.Log(eventRatioOutOfHotspot)
                           + ((mug - mup) - (ng - np)) * Math.Log(1 - eventRatioOutOfHotspot)
                           - ng * Math.Log(eventRatioInTotal)
                           - (mug - ng) * Math.Log(1 - eventRatioInTotal);
            }
            else
            {
                traceLLR = 0;
            }

            return traceLLR;
        }

        private double CalculateDensityRatio()
        {
            var totalEventsCountInHotspot = EventCountInHotspot.Values.Sum();
            var totalPointsCountInHotspot = PointCountInHotspot.Values.Sum();

            var totalEventsCount = EventCountInTotal.Values.Sum();
            var totalPointsCount = PointCountInTotal.Values.Sum();

            return (Convert.ToDouble(totalEventsCountInHotspot) /
                    (totalPointsCountInHotspot == 0 ? double.MinValue : Convert.ToDouble(totalPointsCountInHotspot))) /
                   ((totalEventsCount - totalEventsCountInHotspot == 0
                        ? double.MinValue
                        : Convert.ToDouble(totalEventsCount - totalEventsCountInHotspot)) /
                    (totalPointsCount - totalPointsCountInHotspot == 0
                        ? double.MinValue
                        : Convert.ToDouble(totalPointsCount - totalPointsCountInHotspot)));
        }


        public void AddTraces(TraceDB traces, HashSet<string> traceNameOnPath,
            List<TracePoint> pointOnInnerEdges, List<TracePoint> pointOnStartSegment,
            List<TracePoint> pointOnEndSegment)
        {
            TraceNameInHotspot.Clear();
            TraceNameInHotspot.AddRange(traceNameOnPath);

            AddTracePoint(pointOnStartSegment);
            AddTracePoint(pointOnInnerEdges);
            AddTracePoint(pointOnEndSegment);

            TraceNameInHotspot.ForEach(traceName =>
            {
                if (!traces.TryGetValue(traceName, out var trace)) return;
                PointCountInTotal[traceName] = trace?.Count ?? 0;
                EventCountInTotal[traceName] = trace?.EventCount ?? 0;
            });
        }

        private void AddTracePoint(List<TracePoint> points)
        {
            points.ForEach(pt =>
            {
                if (PointCountInHotspot.TryGetValue(pt.TraceName, out int prevPtCount))
                    PointCountInHotspot[pt.TraceName] = prevPtCount + 1;
                else
                    PointCountInHotspot[pt.TraceName] = 1;

                if (!pt.IsEvent) return;

                if (EventCountInHotspot.TryGetValue(pt.TraceName, out int prevEvtCount))
                {
                    EventCountInHotspot[pt.TraceName] = prevEvtCount + 1;
                }
                else
                {
                    EventCountInHotspot[pt.TraceName] = 1;
                }
            });
        }

        public override string ToString()
        {
            return $"{Support},{Confidence},{StatisticP},{EventCountInHotspot.Sum(count => count.Value)},"
                   + $"{PointCountInHotspot.Sum(count => count.Value)},"
                   + $"{SPath}";
        }
    }
}