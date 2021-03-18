using System;
using System.Collections.Generic;
using System.Linq;
using HotspotDetector;
using OnboardDiagnostics;
using SpatialNetwork;

namespace DBSCANDetector
{
    public class DBDetector: AbstractDetector
    {
        private double _distThreshold;
        private int _minPts;
        
        public DBDetector(TraceDB traces, double distanceThreshold, int minPts)
        {
            _distThreshold = distanceThreshold;
            _minPts = minPts;
            
            var events = traces
                .SelectMany(trace => trace.Value)
                .Where(pt => pt.IsEvent)
                .ToDictionary(pt => pt, pt => 0);

            int clusterCounter = 0; // Cluster counter
            foreach (var (seed, label) in events)
            {
                if (label != 0) continue;
                var neighbors = RangeQuery(events, distanceThreshold, seed);
                neighbors.ExceptWith(new[] {seed});
                if (neighbors.Count < minPts)
                {
                    events[seed] = -1;
                    continue;
                }

                clusterCounter++;
                events[seed] = clusterCounter;
                foreach (var neighbor in neighbors)
                {
                    neighbors.ExceptWith(new[] {neighbor});

                    if (events[neighbor] == -1)
                    {
                        events[neighbor] = clusterCounter;
                        continue;
                    }

                    if (events[neighbor] != 0) continue;
                    events[neighbor] = clusterCounter;

                    var nextNeighbors = RangeQuery(events, distanceThreshold, neighbor);
                    nextNeighbors.ExceptWith(new[] {neighbor});
                    if (neighbors.Count >= minPts)
                    {
                        neighbors.UnionWith(nextNeighbors);
                    }
                }
            }

            for (int clusterIdx = 0; clusterIdx < clusterCounter; clusterIdx++)
            {
                var subGraph = events
                    .Where(pair => pair.Value == clusterIdx)
                    .Select(pair => pair.Key.EdgeId)
                    .ToHashSet().ToList();
                var hotspot = new Hotspot(new SpatialPath(subGraph));
                Hotspots.Add(hotspot);
            }
        }


        private HashSet<TracePoint> RangeQuery(Dictionary<TracePoint, int> events,
            double distanceThreshold, TracePoint seed)
        {
            var road = RoadSystem.RoadSystemFactory.ConstructRoadSystem();
            var results = new HashSet<TracePoint>();
            foreach (var neighbor in from neighbor in events.Keys
                where seed.GetGeographicDistance(neighbor) <= distanceThreshold
                let sp = road.GetShortestPath(seed.Longitude, seed.Latitude, seed.EdgeDirection,
                    neighbor.Longitude, neighbor.Latitude, neighbor.EdgeDirection)
                where sp.Distance < distanceThreshold
                select neighbor)
            {
                results.Add(neighbor);
            }

            return results;
        }

        public override AbstractDetector CreateDetector(TraceDB traces, int support, double confidence, string confidenceType)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return $"Distance threshold: {_distThreshold}; minPts threshold: {_minPts}.\n "
                   + "Support,Confidence,Statistical significance,Event number in hotspot,Record number in hotspot,Path\n"
                   + $"{string.Join("\n", Hotspots)}";
        }
    }
}