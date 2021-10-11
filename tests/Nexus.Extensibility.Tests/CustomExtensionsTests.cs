using Nexus.DataModel;
using System;
using System.Collections.Generic;
using Xunit;

namespace Nexus.Extensibility.Tests
{
    public class CustomExtensionsTests
    {
        [Theory]
        [InlineData("00:00:00.0000001", "100_ns")]
        [InlineData("00:00:00.0000002", "200_ns")]
        [InlineData("00:00:00.0000015", "1500_ns")]

        [InlineData("00:00:00.0000010", "1_us")]
        [InlineData("00:00:00.0000100", "10_us")]
        [InlineData("00:00:00.0001000", "100_us")]
        [InlineData("00:00:00.0015000", "1500_us")]

        [InlineData("00:00:00.0010000", "1_ms")]
        [InlineData("00:00:00.0100000", "10_ms")]
        [InlineData("00:00:00.1000000", "100_ms")]
        [InlineData("00:00:01.5000000", "1500_ms")]

        [InlineData("00:00:01.0000000", "1_s")]
        [InlineData("00:00:15.0000000", "15_s")]

        [InlineData("00:01:00.0000000", "1_min")]
        [InlineData("00:15:00.0000000", "15_min")]
        public void CanCreateUnitStrings(string period, string expected)
        {
            var actual = TimeSpan
                .Parse(period)
                .ToUnitString();

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("A and B/C/D", UriKind.Relative, "A and B/C/D")]
        [InlineData("A and B/C/D.ext", UriKind.Relative, "A and B/C/D.ext")]
        [InlineData(@"file:///C:/A and B", UriKind.Absolute, @"C:/A and B")]
        [InlineData(@"file:///C:/A and B/C.ext", UriKind.Absolute, @"C:/A and B/C.ext")]
        [InlineData(@"file:///root/A and B", UriKind.Absolute, @"/root/A and B")]
        [InlineData(@"file:///root/A and B/C.ext", UriKind.Absolute, @"/root/A and B/C.ext")]
        public void CanConvertUriToPath(string uriString, UriKind uriKind, string expected)
        {
            var uri = new Uri(uriString, uriKind);
            var actual = uri.ToPath();

            Assert.Equal(actual, expected);
        }

        [Fact]
        public void CanFindCatalogItem()
        {
            var representation1 = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromSeconds(1),
                detail: "mean");

            var resource1 = new Resource(id: "Resource1", representations: new List<Representation>() { representation1 });
            var catalog1 = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource1 });

            var representation2 = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromSeconds(1),
                detail: "mean");

            var resource2 = new Resource(id: "Resource2", representations: new List<Representation>() { representation2 });
            var catalog2 = new ResourceCatalog(id: "/D/E/F", resources: new List<Resource>() { resource2 });

            var catalogs = new[] { catalog1, catalog2 };

            var catalogItem1 = new CatalogItem(catalog1, resource1, representation1);
            var catalogItem2 = new CatalogItem(catalog2, resource2, representation2);

            var foundCatalogItem1 = catalogs.Find(catalogItem1.GetPath());
            var foundCatalogItem1ByName = catalogs.Find($"{catalogItem1.Catalog.Id}/{catalogItem1.Resource.Id}/{catalogItem1.Representation.Id}");
            var foundCatalogItem2 = catalogs.Find(catalogItem2.GetPath());
            var foundCatalogItem2ByName = catalogs.Find($"{catalogItem2.Catalog.Id}/{catalogItem2.Resource.Id}/{catalogItem2.Representation.Id}");

            Assert.Equal(catalogItem1, foundCatalogItem1);
            Assert.Equal(catalogItem1, foundCatalogItem1ByName);
            Assert.Equal(catalogItem2, foundCatalogItem2);
            Assert.Equal(catalogItem2, foundCatalogItem2ByName);
        }

        [Fact]
        public void CanTryFindCatalogItem()
        {
            var representation1 = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromSeconds(1),
                detail: "mean");

            var resource1 = new Resource(id: "Resource1", representations: new List<Representation>() { representation1 });
            var catalog1 = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource1 });

            var representation2 = new Representation(
               dataType: NexusDataType.FLOAT32,
               samplePeriod: TimeSpan.FromSeconds(1),
               detail: "mean");

            var resource2 = new Resource(id: "Resource2", representations: new List<Representation>() { representation2 });
            var catalog2 = new ResourceCatalog(id: "/D/E/F", resources: new List<Resource>() { resource2 });

            var catalogs = new[] { catalog1, catalog2 };

            var catalogItem1 = new CatalogItem(catalog1, resource1, representation1);
            var catalogItem2 = new CatalogItem(catalog2, resource2, representation2);

            var success1 = catalogs.TryFind(catalogItem1.GetPath(), out var foundCatalogItem1);
            var success2 = catalogs.TryFind(catalogItem2.GetPath(), out var foundCatalogItem2);

            Assert.Equal(catalogItem1, foundCatalogItem1);
            Assert.True(success1);

            Assert.Equal(catalogItem2, foundCatalogItem2);
            Assert.True(success2);
        }

        [Theory]
        [InlineData("/A/B/C", "Resource1", "1_s_max")]
        [InlineData("/A/B/C", "Resource2", "1_s_mean")]
        [InlineData("/A/B/D", "Resource1", "1_s_max")]
        public void ThrowsForInvalidResourcePath(string catalogId, string resourceId, string datasetId)
        {
            var representation = new Representation(
               dataType: NexusDataType.FLOAT32,
               samplePeriod: TimeSpan.FromSeconds(1),
               detail: "mean");

            var resource = new Resource(id: "Resource1", representations: new List<Representation>() { representation });
            var catalog = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource });

            var catalogs = new[] { catalog };
            var catalogItem = new CatalogItem(catalog, resource, representation);

            Action action = () => catalogs.Find($"/{catalogId}/{resourceId}/{datasetId}");
            Assert.Throws<Exception>(action);
        }
    }
}