using System;
using System.Collections.Generic;
using System.Linq;
using OnboardDiagnostics;

namespace HotspotDetector
{
    public abstract class AbstractDetector
    {
        public int SupportThreshold { get; }
        public double ConfidenceThreshold { get; }

        private double _significanceThreshold = 0.05;

        private readonly string _confidenceType;

        public List<Hotspot> Hotspots { get; protected set; }

        public double LargestConfidence
        {
            get { return Hotspots.Select(h => h.Confidence).DefaultIfEmpty(0).Max(); }
        }

        public abstract AbstractDetector CreateDetector(TraceDB traces, 
            int support, double confidence,
            string confidenceType);

        public AbstractDetector(int support, double confidence, string confidenceType)
        {
            SupportThreshold = support;
            ConfidenceThreshold = confidence;
            _confidenceType = confidenceType;
        }

        public AbstractDetector()
        {
        }

        public void StatisticalSignificanceTest(TraceDB traces, 
            double significance, int simulationTimes)
        {
            Console.WriteLine("Start Monte Carlo simulation:");
            _significanceThreshold = significance;
            var simulationResults = new List<double>();
            for (int simulationIdx = 0; simulationIdx < simulationTimes; simulationIdx++)
            {
                traces.DefineRandomEvent();
                var testDetector = CreateDetector(traces, 
                    SupportThreshold, ConfidenceThreshold, _confidenceType);
                
                simulationResults.Add(testDetector.LargestConfidence);
                Console.WriteLine($"Finish {simulationIdx} / {simulationTimes} simulations. " +
                                  $"The largest LLR is {testDetector.LargestConfidence}.");
            }

            simulationResults.Sort();

            var significanceOrder = Convert.ToInt32(
                Math.Floor(simulationResults.Count - significance * simulationResults.Count - 1));
            significanceOrder = significanceOrder < 0 ? 0 : significanceOrder;

            var significantResults = simulationResults.GetRange(
                significanceOrder, simulationResults.Count - significanceOrder);
            Hotspots.RemoveAll(h => h.Confidence < simulationResults[significanceOrder]);
            Hotspots.ForEach(hotspot =>
            {
                hotspot.StatisticP = significantResults.Count(h => h > hotspot.Confidence) /
                                     Convert.ToDouble(simulationResults.Count);
            });
            Hotspots.RemoveAll(h => h.StatisticP > _significanceThreshold);
        }

        public void RemoveRedundancy()
        {
            List<Hotspot> backup = new List<Hotspot>(Hotspots);
            for (int hotspotId1 = 0; hotspotId1 < backup.Count; hotspotId1++)
            {
                var hotspot1 = backup[hotspotId1];
                if (hotspot1.SPath.Count == 0) continue;

                bool isRedundant = false;
                for (int hotspotId2 = 0; hotspotId2 < backup.Count; hotspotId2++)
                {
                    if (hotspotId1 == hotspotId2) continue;
                    var hotspot2 = backup[hotspotId2];
                    for (int edgeOrder = 0; edgeOrder <= hotspot2.SPath.Count - hotspot1.SPath.Count; edgeOrder++)
                    {
                        if (hotspot1.SPath[0] != hotspot2.SPath[edgeOrder] ||
                            !hotspot2.SPath.GetRange(edgeOrder, hotspot1.SPath.Count).SequenceEqual(hotspot1.SPath))
                            continue;
                        isRedundant = true;
                        break;
                    }

                    if (isRedundant) break;
                }

                if (isRedundant) Hotspots.Remove(hotspot1);
            }
        }

        public override string ToString()
        {
            return $"Support threshold: {SupportThreshold}; Confidence threshold: {ConfidenceThreshold}; "
                   + $"Statistical significance threshold: {_significanceThreshold}.\n"
                   + "Support,Confidence,Statistical significance,Event number in hotspot,Record number in hotspot,Path\n"
                   + $"{string.Join("\n", Hotspots)}";
        }
    }
}