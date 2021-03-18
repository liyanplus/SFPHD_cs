using System;
using System.Collections.Generic;

namespace OnboardDiagnostics
{
    /// <summary>
    /// Comparer for comparing two keys, handling equality as being greater
    /// Use this Comparer e.g. with SortedLists or SortedDictionaries, that don't allow duplicate keys
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    public class DuplicateKeyComparer<TKey>
        : IComparer<TKey> where TKey : IComparable
    {
        #region IComparer<TKey> Members

        public int Compare(TKey x, TKey y)
        {
            int result = x.CompareTo(y);
            return result == 0 ? 1 : result;
        }

        #endregion
    }

    public class TracePoint
    {
        public int Id { get; }
        public string TraceName { get; }
        public float Latitude { get; }
        public float Longitude { get; }
        public uint EdgeId { get; }
        public double EdgeOffset { get; }
        public bool EdgeDirection { get; }

        public Dictionary<string, string> Attributes { get; } =
            new Dictionary<string, string>();

        public bool IsEvent { get; set; } = false;

        public TracePoint(int id, string traceName,
            float latitude, float longitude,
            uint edgeId, double edgeOffset, bool edgeDirection)
        {
            Id = id;
            TraceName = traceName;
            Latitude = latitude;
            Longitude = longitude;
            EdgeId = edgeId;
            EdgeOffset = edgeOffset;
            EdgeDirection = edgeDirection;
        }

        public double GetGeographicDistance(TracePoint another)
        {
            var radius = 6.373e6; // unit meter

            Func<double, double> radians = degree => degree / 180.0 * Math.PI;

            var lat1 = radians(Latitude);
            var lon1 = radians(Longitude);
            var lat2 = radians(another.Latitude);
            var lon2 = radians(another.Longitude);

            var dlon = lon2 - lon1;
            var dlat = lat2 - lat1;

            var a = Math.Pow(Math.Sin(dlat / 2), 2) + 
                    Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dlon / 2),2);
            return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public override int GetHashCode()
        {
            return ($"{TraceName},{Id},{EdgeId}").GetHashCode();
        }

        public override string ToString()
        {
            return $"{Id}, {Latitude}, {Longitude}, {EdgeId}," +
                   $" {EdgeOffset}, {EdgeDirection}, {IsEvent}";
        }
    }
}