using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Itinero;
using Itinero.IO.Osm;
using Itinero.Osm.Vehicles;
using OsmSharp.Streams;

namespace SpatialNetwork
{
    public class RoadSystem
    {
        #region property

        private Router GraphRouter { get; }

        #endregion

        #region constructor

        public static class RoadSystemFactory
        {
            private static RoadSystem _road;

            public static RoadSystem ConstructRoadSystem(string pbfFilePath = null)
            {
                if (pbfFilePath != null)
                {
                    return _road ??= new RoadSystem(pbfFilePath);
                }

                return _road;
            }
        }

        private RoadSystem(string pbfFilePath)
        {
            if (!File.Exists(pbfFilePath))
            {
                throw new FileNotFoundException("OpenStreetMap PBF file not found.");
            }

            using (var osmFileStream = File.OpenRead(pbfFilePath))
            {
                // create source stream.
                var source = new XmlOsmStreamSource(osmFileStream);

                var filteredSource = from feature in source
                    where feature.Type == OsmSharp.OsmGeoType.Node || (
                              feature.Type == OsmSharp.OsmGeoType.Way && feature.Tags != null && (
                                  feature.Tags.Contains("highway", "motorway") ||
                                  feature.Tags.Contains("highway", "trunk") ||
                                  feature.Tags.Contains("highway", "primary") ||
                                  feature.Tags.Contains("highway", "secondary") ||
                                  feature.Tags.Contains("highway", "tertiary") ||
                                  feature.Tags.Contains("highway", "unclassified") ||
                                  feature.Tags.Contains("highway", "residential") ||
                                  feature.Tags.Contains("highway", "service") ||
                                  feature.Tags.Contains("highway", "motorway_link") ||
                                  feature.Tags.Contains("highway", "trunk_link") ||
                                  feature.Tags.Contains("highway", "primary_link") ||
                                  feature.Tags.Contains("highway", "secondary_link") ||
                                  feature.Tags.Contains("highway", "tertiary_link") ||
                                  feature.Tags.Contains("highway", "living_street")) &&
                              !feature.Tags.Contains("access", "private"))
                    select feature;

                // load routerDb data
                RouterDb rtDb = new RouterDb();
                if (File.Exists(pbfFilePath + ".routerdb"))
                {
                    using (var routerDbStream = File.OpenRead(pbfFilePath + ".routerdb"))
                    {
                        rtDb = RouterDb.Deserialize(routerDbStream);
                    }
                }
                else
                {
                    rtDb.LoadOsmData(filteredSource, Vehicle.Car);
                    using (var routerDbStream = File.OpenWrite(pbfFilePath + ".routerdb"))
                    {
                        rtDb.Serialize(routerDbStream);
                    }
                }

                GraphRouter = new Router(rtDb);
            }
        }

        #endregion

        #region Routing

        public SpatialPath GetShortestPath(double originX, double originY, bool originDirection,
            double destinationX, double destinationY, bool destinationDirection)
        {
            var originRouterPt = GraphRouter.Resolve(
                Vehicle.Car.Fastest(), (float) originY, (float) originX);

            var destinationRouterPt = GraphRouter.Resolve(
                Vehicle.Car.Fastest(), (float) destinationY, (float) destinationX);

            var rt = GraphRouter.TryCalculate(Vehicle.Car.Fastest(),
                originRouterPt, originDirection,
                destinationRouterPt, destinationDirection);

            if (rt.IsError || rt.Value.Shape.Length < 2)
            {
                return null;
            }

            SpatialPath path = new SpatialPath(new List<uint>(),
                (originDirection
                    ? new Tuple<double, double>(originRouterPt.Offset, 100.0)
                    : new Tuple<double, double>(0.0, originRouterPt.Offset)),
                (destinationDirection
                    ? new Tuple<double, double>(0.0, destinationRouterPt.Offset)
                    : new Tuple<double, double>(destinationRouterPt.Offset, 100.0))
            );

            foreach (var curMeta in rt.Value.ShapeMeta)
            {
                if (curMeta.Shape == 0) continue;
                // edge id
                uint curEdgeId = GetEdgeId(rt.Value, curMeta);
                // add edge
                path.AddEdge(curEdgeId, curMeta.Distance);
            }

            return path;
        }

        private uint GetEdgeId(Route rt, Route.Meta curMeta)
        {
            var startCoord = GeographicProjector.PositiveTransform.MathTransform.Transform(
                new double[] {rt.Shape[curMeta.Shape - 1].Longitude, rt.Shape[curMeta.Shape - 1].Latitude});
            var midCoord = GeographicProjector.PositiveTransform.MathTransform.Transform(
                new double[] {rt.Shape[curMeta.Shape].Longitude, rt.Shape[curMeta.Shape].Latitude});

            // edge id
            var tmpCoord = GeographicProjector.NegativeTransform.MathTransform.Transform(new double[]
            {
                (startCoord[0] + midCoord[0]) / 2,
                (startCoord[1] + midCoord[1]) / 2
            });

            var resolvedTmpPoint = GraphRouter.Resolve(Vehicle.Car.Fastest(),
                (float) tmpCoord[1], (float) tmpCoord[0]);
            return resolvedTmpPoint.EdgeId;
        }

        #endregion
    }
}