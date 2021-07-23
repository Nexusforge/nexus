using Nexus.DataModel;
using System;
using Xunit;

namespace Nexus.Extensibility.Tests
{
    public class CustomExtensionsTests
    {
        [Theory]
        [InlineData("00:00:00.0000001", true, "100_ns")]
        [InlineData("00:00:00.0000002", true, "200_ns")]
        [InlineData("00:00:00.0000015", true, "1500_ns")]

        [InlineData("00:00:00.0000010", false, "1 us")]
        [InlineData("00:00:00.0000100", false, "10 us")]
        [InlineData("00:00:00.0001000", false, "100 us")]
        [InlineData("00:00:00.0015000", false, "1500 us")]

        [InlineData("00:00:00.0010000", false, "1 ms")]
        [InlineData("00:00:00.0100000", false, "10 ms")]
        [InlineData("00:00:00.1000000", false, "100 ms")]
        [InlineData("00:00:01.5000000", false, "1500 ms")]

        [InlineData("00:00:01.0000000", false, "1 s")]
        [InlineData("00:00:15.0000000", false, "15 s")]
        public void CanCreatUnitStrings(string period, bool underscore, string expected)
        {
            var actual = TimeSpan
                .Parse(period)
                .ToUnitString(underscore);

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
            var catalog1 = new ResourceCatalog() { Id = "/A/B/C" };
            var resource1 = new Resource() { Id = Guid.NewGuid(), Name = "Resource1" };

            var representation1 = new Representation() { Id = "1_s_mean" };

            resource1.Representations.Add(representation1);
            catalog1.Resources.Add(resource1);

            var catalog2 = new ResourceCatalog() { Id = "/D/E/F" };
            var resource2 = new Resource() { Id = Guid.NewGuid(), Name = "Resource2" };
            var representation2 = new Representation() { Id = "1_s_mean" };

            resource2.Representations.Add(representation2);
            catalog2.Resources.Add(resource2);

            var catalogs = new[] { catalog1, catalog2 };

            var catalogItem1 = new CatalogItem(catalog1, resource1, representation1);
            var catalogItem2 = new CatalogItem(catalog2, resource2, representation2);

            var foundCatalogItem1 = catalogs.Find(catalogItem1.GetPath());
            var foundCatalogItem1ByName = catalogs.Find($"{catalogItem1.Catalog.Id}/{catalogItem1.Resource.Name}/{catalogItem1.Representation.Id}");
            var foundCatalogItem2 = catalogs.Find(catalogItem2.GetPath());
            var foundCatalogItem2ByName = catalogs.Find($"{catalogItem2.Catalog.Id}/{catalogItem2.Resource.Name}/{catalogItem2.Representation.Id}");

            Assert.Equal(catalogItem1, foundCatalogItem1);
            Assert.Equal(catalogItem1, foundCatalogItem1ByName);
            Assert.Equal(catalogItem2, foundCatalogItem2);
            Assert.Equal(catalogItem2, foundCatalogItem2ByName);
        }

        [Fact]
        public void CanTryFindCatalogItem()
        {
            var catalog1 = new ResourceCatalog() { Id = "/A/B/C" };
            var resource1 = new Resource() { Id = Guid.NewGuid(), Name = "Resource1" };

            var representation1 = new Representation() { Id = "1_s_mean" };

            resource1.Representations.Add(representation1);
            catalog1.Resources.Add(resource1);

            var catalog2 = new ResourceCatalog() { Id = "/D/E/F" };
            var resource2 = new Resource() { Id = Guid.NewGuid(), Name = "Resource2" };
            var representation2 = new Representation() { Id = "1_s_mean" };

            resource2.Representations.Add(representation2);
            catalog2.Resources.Add(resource2);

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
        [InlineData("/A/B/C", "2fbcfc9b-cb02-45c8-9d35-f8bb0d5ac51c", "1_s_max")]
        [InlineData("/A/B/C", "2fbcfc9b-cb02-45c8-9d35-f8bb0d5ac51d", "1_s_mean")]
        [InlineData("/A/B/D", "2fbcfc9b-cb02-45c8-9d35-f8bb0d5ac51c", "1_s_max")]
        public void ThrowsForInvalidResourcePath(string catalogId, string resourceId, string datasetId)
        {
            var catalog = new ResourceCatalog() { Id = "/A/B/C" };
            var resource = new Resource() { Id = Guid.Parse("2fbcfc9b-cb02-45c8-9d35-f8bb0d5ac51c"), Name = "Resource1" };

            var representation = new Representation() { Id = "1_s_mean" };

            resource.Representations.Add(representation);
            catalog.Resources.Add(resource);

            var catalogs = new[] { catalog };
            var catalogItem = new CatalogItem(catalog, resource, representation);

            Action action = () => catalogs.Find($"/{catalogId}/{resourceId}/{datasetId}");
            Assert.Throws<Exception>(action);
        }
    }
}