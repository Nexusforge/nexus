using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Nexus.Extensibility.Tests
{
    public class DataModelTests : IClassFixture<DataModelFixture>
    {
        private DataModelFixture _fixture;

        public DataModelTests(DataModelFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]

        // valid
        [InlineData("/a", true)]
        [InlineData("/ab_c", true)]
        [InlineData("/a9_b/c__99", true)]

        // invalid
        [InlineData("", false)]
        [InlineData("/", false)]
        [InlineData("/a/", false)]
        [InlineData("/9", false)]
        [InlineData("a", false)]
        public void CanValidateCatalogId(string id, bool isValid)
        {
            if (isValid)
                new ResourceCatalog(id: id);

            else
                Assert.Throws<ArgumentException>(() => new ResourceCatalog(id: id));
        }

        [Theory]

        // valid
        [InlineData("temp", true)]
        [InlineData("Temp", true)]
        [InlineData("Temp_1", true)]

        // invalid
        [InlineData("", false)]
        [InlineData("_temp", false)]
        [InlineData("1temp", false)]
        [InlineData("teßp", false)]
        [InlineData("ª♫", false)]
        [InlineData("tem p", false)]
        [InlineData("tem-p", false)]
        [InlineData("tem*p", false)]
        public void CanValidateResourceId(string id, bool isValid)
        {
            if (isValid)
                new Resource(id: id);

            else
                Assert.Throws<ArgumentException>(() => new Resource(id: id));
        }

        [Theory]
        [InlineData("00:01:00", true)]
        [InlineData("00:00:00", false)]
        public void CanValidateRepresentationSamplePeriod(string samplePeriodString, bool isValid)
        {
            var samplePeriod = TimeSpan.Parse(samplePeriodString);

            if (isValid)
                new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: samplePeriod,
                    detail: "mean");

            else
                Assert.Throws<ArgumentException>(() => new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: samplePeriod,
                    detail: "mean"));
        }

        [Theory]

        // valid
        [InlineData("", true)]
        [InlineData("mean", true)]
        [InlineData("Mean", true)]
        [InlineData("mean_polar", true)]

        // invalid
        [InlineData("_mean", false)]
        [InlineData("1mean", false)]
        [InlineData("meaßn", false)]
        [InlineData("ª♫", false)]
        [InlineData("mea n", false)]
        [InlineData("mea-n", false)]
        [InlineData("mea*n", false)]
        public void CanValidateRepresentationDetail(string detail, bool isValid)
        {
            if (isValid)
                new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: TimeSpan.FromSeconds(1),
                    detail: detail);

            else
                Assert.Throws<ArgumentException>(() => new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: TimeSpan.FromSeconds(1),
                    detail: detail));
        }

        [Theory]
        [InlineData(NexusDataType.FLOAT32, true)]
        [InlineData((NexusDataType)0, false)]
        [InlineData((NexusDataType)9999, false)]
        public void CanValidateRepresentationDataType(NexusDataType dataType, bool isValid)
        {
            if (isValid)
                new Representation(
                     dataType: dataType,
                     samplePeriod: TimeSpan.FromSeconds(1),
                     detail: "mean");

            else
                Assert.Throws<ArgumentException>(() => new Representation(
                     dataType: dataType,
                     samplePeriod: TimeSpan.FromSeconds(1),
                     detail: "mean"));
        }

        [Theory]
        [InlineData("00:00:01", "mean", "1_s_mean")]
        [InlineData("00:00:01", "", "1_s")]
        public void CanInferRepresentationId(string smaplePeriodString, string name, string expected)
        {
            var samplePeriod = TimeSpan.Parse(smaplePeriodString);

            var representation = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: samplePeriod,
                detail: name);

            var actual = representation.Id;

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CanMergeCatalogs_NewWins()
        {
            // arrange

            // prepare catalog 0
            var representation0_V0 = _fixture.Representation0_V0;
            var representation1_V0 = _fixture.Representation1_V0;
            var resource0_V0 = _fixture.Resource0_V0 with { Representations = new List<Representation>() { representation0_V0, representation1_V0 } };
            var resource1_V0 = _fixture.Resource1_V0 with { Representations = null };
            var resource3_V0 = _fixture.Resource3_V0 with { Representations = null };
            var resource4_V0 = _fixture.Resource4_V0 with { Representations = new List<Representation>() { representation0_V0, representation1_V0 } };
            var catalog0_V0 = _fixture.Catalog0_V0 with { Resources = new List<Resource>() { resource0_V0, resource1_V0, resource3_V0, resource4_V0 } };

            // prepare catalog 1
            var representation0_V1 = _fixture.Representation0_V1;
            var representation2_V0 = _fixture.Representation2_V0;
            var resource0_V1 = _fixture.Resource0_V1 with { Representations = new List<Representation>() { representation0_V1, representation2_V0 } };
            var resource2_V0 = _fixture.Resource2_V0 with { Representations = null };
            var resource3_V1 = _fixture.Resource3_V1 with { Representations = new List<Representation>() { representation0_V1, representation1_V0 } };
            var resource4_V1 = _fixture.Resource4_V1 with { Representations = null };
            var catalog0_V1 = _fixture.Catalog0_V1 with { Resources = new List<Resource>() { resource0_V1, resource2_V0, resource3_V1, resource4_V1 } };

            // prepare merged
            var representation0_Vnew = _fixture.Representation0_Vmerged;
            var resource0_Vnew = _fixture.Resource0_Vmerged with { Representations = new List<Representation>() { representation0_Vnew, representation1_V0, representation2_V0 } };
            var resource3_Vnew = _fixture.Resource3_Vmerged with { Representations = new List<Representation>() { representation0_V1, representation1_V0 } };
            var resource4_Vnew = _fixture.Resource4_Vmerged with { Representations = new List<Representation>() { representation0_V0, representation1_V0 } };
            var catalog0_Vnew = _fixture.Catalog0_Vmerged with { Resources = new List<Resource>() { resource0_Vnew, resource1_V0, resource3_Vnew, resource4_Vnew, resource2_V0 } };

            // act
            var catalog0_actual = catalog0_V0.Merge(catalog0_V1, MergeMode.NewWins);

            // assert
            var options = new JsonSerializerOptions();
            options.Converters.Add(new TimeSpanConverter());

            var expected = JsonSerializer.Serialize(catalog0_Vnew, options);
            var actual = JsonSerializer.Serialize(catalog0_actual, options);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CanMergeCatalogs_ExclusiveOr()
        {
            // arrange

            // prepare catalog 0
            var representation0_V0 = _fixture.Representation0_V0;
            var representation1_V0 = _fixture.Representation1_V0;
            var resource0_V0 = _fixture.Resource0_V0 with { Representations = new List<Representation>() { representation0_V0, representation1_V0 } };
            var resource1_V0 = _fixture.Resource1_V0 with { Representations = new List<Representation>() };
            var catalog0_V0 = _fixture.Catalog0_V0 with { Resources = new List<Resource>() { resource0_V0, resource1_V0 } };

            // prepare catalog 1
            var representation2_V0 = _fixture.Representation2_V0;
            var resource0_V2 = _fixture.Resource0_V2 with { Representations = new List<Representation>() { representation2_V0 } };
            var resource2_V0 = _fixture.Resource2_V0 with { Representations = new List<Representation>() };
            var catalog0_V2 = _fixture.Catalog0_V2 with { Resources = new List<Resource>() { resource0_V2, resource2_V0 } };

            // prepare merged
            var representation0_Vxor = _fixture.Representation0_Vxor;
            var resource0_Vxor = _fixture.Resource0_Vxor with { Representations = new List<Representation>() { representation0_Vxor, representation1_V0, representation2_V0 } };
            var catalog0_Vxor = _fixture.Catalog0_Vxor with { Resources = new List<Resource>() { resource0_Vxor, resource1_V0, resource2_V0 } };

            // act
            var catalog0_actual = catalog0_V0.Merge(catalog0_V2, MergeMode.ExclusiveOr);

            // assert
            var expected = JsonSerializer.Serialize(catalog0_Vxor);
            var actual = JsonSerializer.Serialize(catalog0_actual);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CatalogMergeThrowsForNonMatchingIdentifiers()
        {
            // Arrange
            var catalog1 = new ResourceCatalog(id: "/C1");
            var catalog2 = new ResourceCatalog(id: "/C2");

            // Act
            Action action = () => catalog1.Merge(catalog2, MergeMode.ExclusiveOr);

            // Assert
            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void CatalogConstructorThrowsForNonUniqueResource()
        {
            // Act
            Action action = () =>
            {
                var catalog = new ResourceCatalog(
                    id: "/C",
                    resources: new List<Resource>()
                    {
                        new Resource(id: "R1"),
                        new Resource(id: "R2"),
                        new Resource(id: "R2")
                    });
            };

            // Assert
            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void ResourceMergeThrowsForNonUniqueRepresentation()
        {
            // Arrange
            var resource1 = new Resource(
                id: "R1",
                representations: new List<Representation>() 
                { 
                    new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1), detail: "RP1") 
                });

            var resource2 = new Resource(
                id: "R2",
                representations: new List<Representation>()
                {
                    new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1), detail: "RP1"),
                    new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1), detail: "RP2"),
                    new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1), detail: "RP3")
                });

            // Act
            Action action = () => resource1.Merge(resource2, MergeMode.ExclusiveOr);

            // Assert
            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void ResourceMergeThrowsForNonMatchingIdentifiers()
        {
            // Arrange
            var resource1 = new Resource(id: "R1");
            var resource2 = new Resource(id: "R2");

            // Act
            Action action = () => resource1.Merge(resource2, MergeMode.ExclusiveOr);

            // Assert
            Assert.Throws<ArgumentException>(action);
        }

           [Fact]
        public void CanFindCatalogItem()
        {
            var representation = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromSeconds(1),
                detail: "mean");

            var resource = new Resource(id: "Resource1", representations: new List<Representation>() { representation });
            var catalog = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource });
            var catalogItem = new CatalogItem(catalog, resource, representation);
            var foundCatalogItem = catalog.Find(catalogItem.GetPath());
            var foundCatalogItemByName = catalog.Find($"{catalogItem.Catalog.Id}/{catalogItem.Resource.Id}/{catalogItem.Representation.Id}");

            Assert.Equal(catalogItem, foundCatalogItem);
            Assert.Equal(catalogItem, foundCatalogItemByName);
        }

        [Fact]
        public void CanTryFindCatalogItem()
        {
            var representation = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromSeconds(1),
                detail: "mean");

            var resource = new Resource(id: "Resource1", representations: new List<Representation>() { representation });
            var catalog = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource });
            var catalogItem = new CatalogItem(catalog, resource, representation);
            var success = catalog.TryFind(catalogItem.GetPath(), out var foundCatalogItem1);

            Assert.Equal(catalogItem, foundCatalogItem1);
            Assert.True(success);
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
            var catalogItem = new CatalogItem(catalog, resource, representation);

            Action action = () => catalog.Find($"/{catalogId}/{resourceId}/{datasetId}");
            Assert.Throws<Exception>(action);
        }
    }
}