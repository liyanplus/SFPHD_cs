using System;
using System.Collections.Generic;
using System.Linq;
using HotspotDetector;
using OnboardDiagnostics;
using SpatialNetwork;

namespace SFPHotspotDetector
{
    class AggNode
    {
        public uint EdgeId { get; }
        public List<AggEdge> InEdges { get; } = new List<AggEdge>();
        public List<AggEdge> OutEdges { get; } = new List<AggEdge>();

        // point count, event count, and edge order
        public Dictionary<string, Tuple<int, int, int>> TraceStats = new Dictionary<string, Tuple<int, int, int>>();

        public AggNode(uint id)
        {
            EdgeId = id;
        }
    }

    class AggEdge
    {
        public AggNode StartNode { get; }
        public AggNode EndNode { get; }

        public List<uint> EdgeIds { get; } = new List<uint>();

        public Dictionary<string, int> PointCount { get; } = new Dictionary<string, int>();

        public AggEdge(AggNode startNode, AggNode endNode)
        {
            StartNode = startNode;
            EndNode = endNode;
        }
    }

    public class AggDetector : AbstractDetector
    {
        private readonly Dictionary<uint, int> _aggNodeEdgeIdOrderMapping = new Dictionary<uint, int>();
        private List<AggNode> _aggNodes;
        private int _aggNodeCount = 0;

        public AggDetector(TraceDB traces, int support, double confidence, string confidenceType) :
            base(support, confidence, confidenceType)
        {
            ConstructAggGraph(traces);

            Hotspots = new List<Hotspot>();
            foreach (var rootNode in _aggNodes)
            {
                var traceNameOnPath = new HashSet<string>(rootNode.TraceStats.Keys);

                var nodeOnPath = new List<AggNode> {rootNode};
                var edgeOnPath = new List<AggEdge>();

                var pointInStartEdge = new SortedList<double, TracePoint>(new DuplicateKeyComparer<double>());
                var nonEventCountInStartEdge = new Dictionary<string, int>();

                var pointCountInHotspot = new Dictionary<string, int>();
                var eventCountInHotspot = new Dictionary<string, int>();
                var pointCountTotal = new Dictionary<string, int>();
                var eventCountTotal = new Dictionary<string, int>();

                foreach (var traceName in traceNameOnPath)
                {
                    var currentTrace = traces[traceName];
                    pointCountTotal[traceName] = currentTrace.Count;
                    eventCountTotal[traceName] = currentTrace.EventCount;

                    if (currentTrace.EdgeTracePointMapping.TryGetValue(rootNode.EdgeId, out var ptIds))
                    {
                        pointCountInHotspot[traceName] = ptIds.Count +
                                                         (pointCountInHotspot.TryGetValue(traceName, out var ptCount)
                                                             ? ptCount
                                                             : 0);
                        nonEventCountInStartEdge[traceName] = ptIds.Count;
                        ptIds.ForEach(ptId => pointInStartEdge.Add(
                            currentTrace[ptId].EdgeOffset, currentTrace[ptId]));
                    }
                    else
                    {
                        nonEventCountInStartEdge[traceName] = 0;
                    }

                    if (!currentTrace.EdgeEventMapping.TryGetValue(rootNode.EdgeId, out ptIds)) continue;
                    eventCountInHotspot[traceName] = ptIds.Count +
                                                     (eventCountInHotspot.TryGetValue(traceName, out var evtCount)
                                                         ? evtCount
                                                         : 0);
                    nonEventCountInStartEdge[traceName] -= ptIds.Count;
                }

                ExploreApproxHotspot(
                    nodeOnPath,
                    edgeOnPath,
                    new SpatialPath(new[] {rootNode.EdgeId}),
                    traces,
                    traceNameOnPath,
                    pointInStartEdge,
                    null,
                    nonEventCountInStartEdge,
                    null,
                    pointCountInHotspot,
                    eventCountInHotspot,
                    pointCountTotal,
                    eventCountTotal
                );
            }
        }

        private bool ExploreApproxHotspot(
            List<AggNode> nodeOnPath, List<AggEdge> edgeOnPath,
            SpatialPath path, TraceDB traces,
            HashSet<string> traceNameOnPath,
            SortedList<double, TracePoint> pointInStartEdge, SortedList<double, TracePoint> pointInEndEdge,
            Dictionary<string, int> nonEventCountInStartEdge, Dictionary<string, int> nonEventCountInEndEdge,
            Dictionary<string, int> pointCountInHotspot, Dictionary<string, int> eventCountInHotspot,
            Dictionary<string, int> pointCountTotal, Dictionary<string, int> eventCountTotal)
        {
            if (traceNameOnPath.Count < SupportThreshold || path.Count > 50 ||
                !pointInStartEdge.Any(pt => pt.Value.IsEvent))
                return false;

            var lastNode = nodeOnPath[^1];
            bool found = false;
            foreach (var outEdge in lastNode.OutEdges)
            {
                var nextNode = outEdge.EndNode;
                var nextTraceNameOnPath = new HashSet<string>(traceNameOnPath);
                nextTraceNameOnPath.IntersectWith(outEdge.PointCount.Keys);
                nextTraceNameOnPath.IntersectWith(nextNode.TraceStats.Keys);
                if (nextTraceNameOnPath.Count < SupportThreshold) continue;

                var nextPointInStartEdge = new SortedList<double, TracePoint>(new DuplicateKeyComparer<double>());
                foreach (var pair in pointInStartEdge.Where(pt => nextTraceNameOnPath.Contains(pt.Value.TraceName)))
                {
                    nextPointInStartEdge.Add(pair.Key, pair.Value);
                }

                var nextNonEventCountInStartEdge =
                    nonEventCountInStartEdge.Where(pair => nextTraceNameOnPath.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value);

                var nextPointInEndEdge = new SortedList<double, TracePoint>(new DuplicateKeyComparer<double>());
                var nextNonEventCountInEndEdge = new Dictionary<string, int>();

                var nextPointCountInHotspot = new Dictionary<string, int>(
                    pointCountInHotspot.Where(pair => nextTraceNameOnPath.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value));
                var nextEventCountInHotspot = new Dictionary<string, int>(
                    eventCountInHotspot.Where(pair => nextTraceNameOnPath.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value));

                foreach (var traceName in nextTraceNameOnPath)
                {
                    var trace = traces[traceName];

                    if (trace.EdgeTracePointMapping.TryGetValue(nextNode.EdgeId, out var ptIds))
                    {
                        foreach (var ptId in ptIds)
                        {
                            nextPointInEndEdge.Add(trace[ptId].EdgeOffset, trace[ptId]);
                        }
                    }

                    nextNonEventCountInEndEdge[traceName] =
                        nextNode.TraceStats[traceName].Item1 - nextNode.TraceStats[traceName].Item2;

                    nextPointCountInHotspot[traceName] =
                        (nextPointCountInHotspot.TryGetValue(traceName, out int oldCount) ? oldCount : 0) +
                        outEdge.PointCount[traceName] +
                        nextNode.TraceStats[traceName].Item1;

                    nextEventCountInHotspot[traceName] =
                        (nextEventCountInHotspot.TryGetValue(traceName, out oldCount) ? oldCount : 0) +
                        nextNode.TraceStats[traceName].Item2;
                }

                nodeOnPath.Add(nextNode);
                edgeOnPath.Add(outEdge);
                path.AddRange(outEdge.EdgeIds);
                path.Add(nextNode.EdgeId);

                if (ExploreApproxHotspot(nodeOnPath, edgeOnPath,
                    path, traces,
                    nextTraceNameOnPath,
                    nextPointInStartEdge, nextPointInEndEdge,
                    nextNonEventCountInStartEdge, nextNonEventCountInEndEdge,
                    nextPointCountInHotspot, nextEventCountInHotspot,
                    pointCountTotal.Where(pair => nextTraceNameOnPath.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                    eventCountTotal.Where(pair => nextTraceNameOnPath.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)))
                {
                    found = true;
                }

                path.RemoveRange(path.Count - outEdge.EdgeIds.Count - 1, outEdge.EdgeIds.Count + 1);
                nodeOnPath.RemoveAt(nodeOnPath.Count - 1);
                edgeOnPath.RemoveAt(edgeOnPath.Count - 1);
            }

            if (!found && edgeOnPath.Count > 0
                       && pointInEndEdge != null && nonEventCountInEndEdge != null
                       && pointInEndEdge.Any(pt => pt.Value.IsEvent))
            {
                // get hotspots
                var motherHotspot = new ApproxHotspot(path,
                    traceNameOnPath,
                    pointInStartEdge, pointInEndEdge,
                    nonEventCountInStartEdge, nonEventCountInEndEdge,
                    pointCountInHotspot, eventCountInHotspot,
                    pointCountTotal, eventCountTotal);

                if (motherHotspot.ConfidenceUpperBound >= ConfidenceThreshold)
                {
                    foreach (var childHotspot in motherHotspot.GetChildHotspots()
                        .Where(h => h.Confidence >= ConfidenceThreshold))
                    {
                        Hotspots.Add(childHotspot);
                        return true;
                    }
                }
            }

            return found;
        }


        #region AggGraph Construction

        private void ConstructAggGraph(TraceDB traces)
        {
            _aggNodes = ConstructAggNodes(traces).ToList();
            ConstructAggEdges(traces);
        }

        private IEnumerable<AggNode> ConstructAggNodes(TraceDB traces)
        {
            // find all _aggNodes (edges with events)
            foreach (var (edgeId, traceNames) in traces.EdgeTraceMapping)
            {
                // filter out non-frequented edges
                if (traceNames.Count < SupportThreshold) continue;

                if (!traceNames.Select(name => traces[name])
                    .Any(trace =>
                        trace.EdgeEventMapping.TryGetValue(edgeId, out var eventIds)
                        && eventIds.Count > 0)) continue;

                var node = new AggNode(edgeId);

                foreach (var trace in traceNames.Select(name => traces[name]))
                {
                    node.TraceStats[trace.TraceName] = new Tuple<int, int, int>(
                        trace.EdgeTracePointMapping.TryGetValue(edgeId, out var ptIds) ? ptIds.Count : 0,
                        trace.EdgeEventMapping.TryGetValue(edgeId, out var evtIds) ? evtIds.Count : 0,
                        trace.EdgeIds.IndexOf(edgeId));
                }

                _aggNodeEdgeIdOrderMapping[node.EdgeId] = _aggNodeCount;
                _aggNodeCount++;
                yield return node;
            }
        }

        private void ConstructAggEdges(TraceDB traces)
        {
            // find all _aggEdges (list of edges linking _aggNodes)
            foreach (AggNode rootNode in _aggNodes)
            {
                if (!traces.EdgeTraceMapping.ContainsKey(rootNode.EdgeId)) continue;

                var rootPath = new SpatialPath(new List<uint>() {rootNode.EdgeId});
                var startingOrderMapping = new Dictionary<string, int>();
                var pointCount = new Dictionary<string, int>();

                foreach (var traceName in traces.EdgeTraceMapping[rootNode.EdgeId])
                {
                    startingOrderMapping[traceName] = traces[traceName].EdgeIds.IndexOf(rootNode.EdgeId);
                    pointCount[traceName] = 0;
                }

                ConstructAggEdgesFromRoot(rootPath, traces,
                    startingOrderMapping, rootNode, pointCount);
            }
        }

        private void ConstructAggEdgesFromRoot(SpatialPath currentPath, TraceDB traces,
            Dictionary<string, int> startingOrderMapping, AggNode rootNode, Dictionary<string, int> pointCount)
        {
            foreach (var (extendedPath, extendedStartingOrderMapping) in
                traces.GetExtendedPathWithStartingOrderMapping(currentPath, startingOrderMapping, SupportThreshold))
            {
                if (_aggNodeEdgeIdOrderMapping.ContainsKey(extendedPath[^1]))
                {
                    // the last edge contains events
                    if (!_aggNodeEdgeIdOrderMapping.TryGetValue(extendedPath[^1],
                        out int lastNodeOrder)) continue;

                    var endNode = _aggNodes[lastNodeOrder];
                    var edge = new AggEdge(rootNode, endNode);
                    edge.EdgeIds.AddRange(extendedPath.GetRange(1, extendedPath.Count - 2));
                    foreach (var ptCount in pointCount.Where(c => extendedStartingOrderMapping.ContainsKey(c.Key)))
                    {
                        edge.PointCount.Add(ptCount.Key, ptCount.Value);
                    }

                    rootNode.OutEdges.Add(edge);
                    endNode.InEdges.Add(edge);
                }
                else
                {
                    // the last edge does not contain events
                    var tmpPointCount = new Dictionary<string, int>();
                    foreach (var traceName in extendedStartingOrderMapping.Keys)
                    {
                        var trace = traces[traceName];
                        tmpPointCount[traceName] =
                            (trace.EdgeTracePointMapping.TryGetValue(extendedPath[^1], out var newPts)
                                ? newPts.Count
                                : 0) +
                            (pointCount.TryGetValue(traceName, out var oldCount) ? oldCount : 0);
                    }

                    ConstructAggEdgesFromRoot(extendedPath, traces,
                        extendedStartingOrderMapping, rootNode,
                        tmpPointCount);
                }
            }
        }

        #endregion

        public override AbstractDetector CreateDetector(TraceDB traces, int support, double confidence,
            string confidenceType)
        {
            return new AggDetector(traces, support, confidence, confidenceType);
        }
    }
}