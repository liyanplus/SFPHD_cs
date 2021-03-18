using GeoAPI.CoordinateSystems;
using GeoAPI.CoordinateSystems.Transformations;
using GeoAPI.Geometries;
using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace SpatialNetwork
{
    static class GeographicProjector
    {
        public static IGeometryFactory GeoFactory { get; } = new GeometryFactory(
            new PrecisionModel(PrecisionModels.Floating), 32614);

        private static readonly IGeographicCoordinateSystem DataCoordinateSystem = GeographicCoordinateSystem.WGS84;
        private static readonly IProjectedCoordinateSystem DistanceCoordinateSystem = 
            ProjectedCoordinateSystem.WGS84_UTM(14, true);

        private static readonly CoordinateTransformationFactory
            TransformationFactory = new CoordinateTransformationFactory();

        public static ICoordinateTransformation PositiveTransform { get; }
            = TransformationFactory.CreateFromCoordinateSystems(DataCoordinateSystem, 
                DistanceCoordinateSystem);

        public static ICoordinateTransformation NegativeTransform { get; }
            = TransformationFactory.CreateFromCoordinateSystems(DistanceCoordinateSystem, 
                DataCoordinateSystem);
    }
}