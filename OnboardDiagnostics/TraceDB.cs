using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using MathNet.Numerics.Statistics;
using SpatialNetwork;

namespace OnboardDiagnostics
{
    public class TraceDB : Dictionary<string, Trace>
    {
        public Dictionary<uint, HashSet<string>> EdgeTraceMapping { get; } = new Dictionary<uint, HashSet<string>>();

        public TraceDB(string tracePointsFilepath, string traceEdgesFilepath,
            string tracePtLatFieldName = "Latitude", string tracePtLonFieldName = "Longitude",
            string tracePtEdgeIdFieldName = "EdgeId", string tracePtEdgeOffsetFieldName = "EdgeOffset",
            string tracePtEdgeDirectionFieldName = "EdgeDir",
            string traceEdgeIdFieldName = "EdgeId")
        {
            if (File.Exists(tracePointsFilepath))
            {
                ImportTracePoint(tracePointsFilepath,
                    tracePtLatFieldName, tracePtLonFieldName,
                    tracePtEdgeIdFieldName, tracePtEdgeOffsetFieldName,
                    tracePtEdgeDirectionFieldName);
            }
            else if (Directory.Exists(tracePointsFilepath))
            {
                foreach (var filepath in Directory.EnumerateFiles(
                    tracePointsFilepath, "*.csv",
                    SearchOption.AllDirectories))
                {
                    ImportTracePoint(filepath,
                        tracePtLatFieldName, tracePtLonFieldName,
                        tracePtEdgeIdFieldName, tracePtEdgeOffsetFieldName,
                        tracePtEdgeDirectionFieldName);
                }
            }
            else
            {
                throw new FileNotFoundException("Point file does not exist.");
            }

            Console.WriteLine("Finish reading points.");

            if (File.Exists(traceEdgesFilepath))
            {
                ImportTraceEdge(traceEdgesFilepath, traceEdgeIdFieldName);
            }
            else if (Directory.Exists(traceEdgesFilepath))
            {
                foreach (var filepath in Directory.EnumerateFiles(
                    traceEdgesFilepath, "*.csv",
                    SearchOption.AllDirectories))
                {
                    ImportTraceEdge(filepath, traceEdgeIdFieldName);
                }
            }
            else
            {
                throw new FileNotFoundException("Edge file does not exist.");
            }

            Console.WriteLine($"Finish reading edges.\n{Count} traces in total.");
        }

        private void ImportTracePoint(string traceFilepath,
            string tracePtLatFieldName, string tracePtLonFieldName,
            string tracePtEdgeIdFieldName, string tracePtEdgeOffsetFieldName,
            string tracePtEdgeDirectionFieldName)
        {
            string traceName = Path.GetFileNameWithoutExtension(traceFilepath);
            if (traceName == null) return;

            Trace trace = new Trace(traceName);
            using (var fileReader = File.OpenText(traceFilepath))
            using (var csvReader = new CsvReader(fileReader, CultureInfo.InvariantCulture))
            {
                csvReader.Read();
                csvReader.ReadHeader();
                while (csvReader.Read())
                {
                    if (!csvReader.TryGetField(tracePtLatFieldName, out float lat) ||
                        !csvReader.TryGetField(tracePtLonFieldName, out float lon) ||
                        !csvReader.TryGetField(tracePtEdgeIdFieldName, out uint edgeId) ||
                        !csvReader.TryGetField(tracePtEdgeOffsetFieldName, out double offset) ||
                        !csvReader.TryGetField(tracePtEdgeDirectionFieldName, out string directionIndicator)) continue;

                    bool direction = directionIndicator.Equals("1");
                    var pt = new TracePoint(trace.Count, trace.TraceName,
                        lat, lon, edgeId, offset, direction);
                    foreach (var columnName in csvReader.Context.HeaderRecord.Where(columnName =>
                        !(columnName.Equals(tracePtLatFieldName) || columnName.Equals(tracePtLonFieldName) ||
                          columnName.Equals(tracePtEdgeIdFieldName) ||
                          columnName.Equals(tracePtEdgeOffsetFieldName) ||
                          columnName.Equals(tracePtEdgeDirectionFieldName))))
                    {
                        pt.Attributes[columnName] = csvReader.GetField(columnName);
                    }

                    trace.AddTracePoint(pt);
                }
            }

            this[trace.TraceName] = trace;
        }

        private void ImportTraceEdge(string traceFilepath, string traceEdgeIdFieldName)
        {
            string traceName = Path.GetFileNameWithoutExtension(traceFilepath);
            if (traceName == null) return;
            if (!TryGetValue(traceName, out Trace trace) || trace == null) return;

            using (var fileReader = File.OpenText(traceFilepath))
            using (var csvReader = new CsvReader(fileReader, CultureInfo.InvariantCulture))
            {
                csvReader.Read();
                csvReader.ReadHeader();
                while (csvReader.Read())
                {
                    if (!csvReader.TryGetField<uint>(traceEdgeIdFieldName, out var edgeId)) continue;

                    trace.EdgeIds.Add(edgeId);
                    if (!EdgeTraceMapping.ContainsKey(edgeId)) EdgeTraceMapping[edgeId] = new HashSet<string>();
                    EdgeTraceMapping[edgeId].Add(traceName);
                }
            }
        }

        public void DefineRandomEvent()
        {
            Random rnd = new Random();

            Action<TracePoint> EventDefinition(double eventRatio) =>
                pt => pt.IsEvent = rnd.NextDouble() < eventRatio;

            foreach (var trace in Values)
                trace.UpdateEventStatus(EventDefinition(trace.EventRatio));
        }

        public void DefineGreaterThanThresholdEvent(double threshold, string eventColumn, bool isThresholdRelative)
        {
            Action<TracePoint> EventDefinition(double eventThreshold) =>
                pt => pt.IsEvent = (double.TryParse(pt.Attributes[eventColumn], out double tmp) ? tmp : 0.0) >
                                   eventThreshold;

            foreach (var trace in Values)
            {
                if (isThresholdRelative)
                {
                    double th = trace.Select(
                        pt => double.TryParse(pt.Attributes[eventColumn], out double tmp) ? tmp : 0.0).Percentile(
                        Convert.ToInt32(Math.Floor(threshold)));
                    trace.UpdateEventStatus(EventDefinition(th));
                }
                else
                {
                    trace.UpdateEventStatus(EventDefinition(threshold));
                }

                trace.UpdateEventRatio();
            }
        }

        public HashSet<string> GetTraceNameOnPath(SpatialPath path,
            Dictionary<string, int> traceNameStringOrderMapping = null,
            List<TracePoint> pointOnStartSegment = null,
            List<TracePoint> pointOnInnerEdges = null,
            List<TracePoint> pointOnEndSegment = null)
        {
            if (path == null || path.Count == 0)
            {
                return new HashSet<string>();
            }

            var results = new HashSet<string>(EdgeTraceMapping[path[0]]);
            foreach (var edgeId in path)
            {
                results.IntersectWith(EdgeTraceMapping[edgeId]);
                if (results.Count == 0)
                {
                    break;
                }
            }

            results.RemoveWhere(traceName =>
                !this[traceName].AddToPath(path, traceNameStringOrderMapping,
                    pointOnInnerEdges, pointOnStartSegment, pointOnEndSegment));

            return results;
        }
        
        public IEnumerable<Tuple<SpatialPath, Dictionary<string, int>>> GetExtendedPathWithStartingOrderMapping(
            SpatialPath oldPath,
            Dictionary<string, int> oldStartingOrderMapping,
            int supportThreshold)
        {
            var nextEdgeIdWithStartingOrderMapping = new Dictionary<uint, Dictionary<string, int>>();

            foreach (var (traceName, startingOrder) in oldStartingOrderMapping)
            {
                var trace = this[traceName];
                if (startingOrder + oldPath.Count >= trace.EdgeIds.Count)
                    continue;

                if (!nextEdgeIdWithStartingOrderMapping.ContainsKey(
                    trace.EdgeIds[startingOrder + oldPath.Count]))
                {
                    nextEdgeIdWithStartingOrderMapping.Add(
                        trace.EdgeIds[startingOrder + oldPath.Count],
                        new Dictionary<string, int>());
                }

                nextEdgeIdWithStartingOrderMapping[trace.EdgeIds[startingOrder + oldPath.Count]]
                    .Add(
                        traceName, startingOrder
                    );
            }

            foreach (var (nextEdgeId, startingOrder) in
                nextEdgeIdWithStartingOrderMapping.Where(pair => pair.Value.Count >= supportThreshold))
            {
                var newEdgeIds = new List<uint>(oldPath) {nextEdgeId};
                yield return new Tuple<SpatialPath, Dictionary<string, int>>(
                    new SpatialPath(newEdgeIds, oldPath.StartSegment), startingOrder);
            }
        }
    }
}