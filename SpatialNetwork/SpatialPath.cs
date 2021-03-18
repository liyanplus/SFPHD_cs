using System;
using System.Collections.Generic;

namespace SpatialNetwork
{
    public class SpatialPath : List<uint>
    {
        public double Distance { get; private set; }

        public Tuple<double, double> StartSegment { get; }
            = new Tuple<double, double>(0.0, 100.0);

        public Tuple<double, double> EndSegment { get; }
            = new Tuple<double, double>(0.0, 100.0);

        public SpatialPath(IEnumerable<uint> edgeIds,
            Tuple<double, double> start,
            Tuple<double, double> end) : base(edgeIds)
        {
            StartSegment = start;
            EndSegment = end;
        }

        public SpatialPath(IEnumerable<uint> edgeIds,
            Tuple<double, double> start) : base(edgeIds)
        {
            StartSegment = start;
        }

        public SpatialPath(IEnumerable<uint> edgeIds) : base(edgeIds)
        {
        }

        public SpatialPath()
        {
        }

        public void AddEdge(uint edgeId, double distance)
        {
            Add(edgeId);
            Distance += distance;
        }

        public override string ToString()
        {
            return $"{string.Join(":", this)},"
                + $"StartOffset: {StartSegment},EndOffset: {EndSegment}";
        }
    }
}