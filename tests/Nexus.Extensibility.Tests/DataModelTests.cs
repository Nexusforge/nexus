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
        [InlineData("00:01:00", true)]
        [InlineData("00:00:00", false)]
        public void CanValidateSamplePeriod(string samplePeriodString, bool isValid)
        {
            var samplePeriod = TimeSpan.Parse(samplePeriodString);

            if (isValid)
                new Representation()
                {
                    SamplePeriod = samplePeriod,
                    Detail = "mean",
                    DataType = NexusDataType.FLOAT64
                };

            else
                Assert.Throws<ArgumentException>(() => new Representation()
                {
                    SamplePeriod = samplePeriod,
                    Detail = "mean",
                    DataType = NexusDataType.FLOAT64
                });
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
        public void CanValidateDetail(string detail, bool isValid)
        {
            if (isValid)
                new Representation() 
                {
                    SamplePeriod = TimeSpan.FromSeconds(1),
                    Detail = detail, 
                    DataType = NexusDataType.FLOAT64 
                };

            else
                Assert.Throws<ArgumentException>(() => new Representation()
                {
                    SamplePeriod = TimeSpan.FromSeconds(1),
                    Detail = detail,
                    DataType = NexusDataType.FLOAT64
                });
        }

        [Theory]
        [InlineData(NexusDataType.FLOAT32, true)]
        [InlineData((NexusDataType)0, false)]
        [InlineData((NexusDataType)9999, false)]
        public void CanValidateDataType(NexusDataType dataType, bool isValid)
        {
            if (isValid)
                new Representation() 
                { 
                    SamplePeriod = TimeSpan.FromSeconds(1),
                    Detail = "mean", 
                    DataType = dataType
                };

            else
                Assert.Throws<ArgumentException>(() => new Representation() 
                { 
                    SamplePeriod = TimeSpan.FromSeconds(1),
                    Detail = "mean",
                    DataType = dataType 
                });
        }

        [Theory]
        [InlineData("00:00:01", "mean", "1_s_mean")]
        [InlineData("00:00:01", "", "1_s")]
        public void CanInferId(string smaplePeriodString, string name, string expected)
        {
            var samplePeriod = TimeSpan.Parse(smaplePeriodString);
            var representation = new Representation() { SamplePeriod = samplePeriod, Detail = name, DataType = NexusDataType.FLOAT32 };
            var actual = representation.Id;

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CanMergeCatalogs_NewWins()
        {
            // arrange

            // prepare catalog 0
            var catalog0_V0 = _fixture.Catalog0_V0 with { Resources = new List<Resource>() };
            var resource0_V0 = _fixture.Resource0_V0 with { Representations = new List<Representation>() };
            var resource1_V0 = _fixture.Resource1_V0 with { Representations = new List<Representation>() };
            var representation0_V0 = _fixture.Representation0_V0;
            var representation1_V0 = _fixture.Representation1_V0;

            resource0_V0.Representations.Add(representation0_V0);
            resource0_V0.Representations.Add(representation1_V0);

            catalog0_V0.Resources.Add(resource0_V0);
            catalog0_V0.Resources.Add(resource1_V0);

            // prepare catalog 1
            var catalog0_V1 = _fixture.Catalog0_V1 with { Resources = new List<Resource>() };
            var resource0_V1 = _fixture.Resource0_V1 with { Representations = new List<Representation>() };
            var resource2_V0 = _fixture.Resource2_V0 with { Representations = new List<Representation>() };
            var representation0_V1 = _fixture.Representation0_V1;
            var representation2_V0 = _fixture.Representation2_V0;

            resource0_V1.Representations.Add(representation0_V1);
            resource0_V1.Representations.Add(representation2_V0);

            catalog0_V1.Resources.Add(resource0_V1);
            catalog0_V1.Resources.Add(resource2_V0);

            // prepare merged
            var catalog0_Vnew = _fixture.Catalog0_Vmerged with { Resources = new List<Resource>() };
            var resource0_Vnew = _fixture.Resource0_Vmerged with { Representations = new List<Representation>() };
            var representation0_Vnew = _fixture.Representation0_Vmerged;

            resource0_Vnew.Representations.Add(representation0_Vnew);
            resource0_Vnew.Representations.Add(representation1_V0);
            resource0_Vnew.Representations.Add(representation2_V0);

            catalog0_Vnew.Resources.Add(resource0_Vnew);
            catalog0_Vnew.Resources.Add(resource1_V0);
            catalog0_Vnew.Resources.Add(resource2_V0);

            // act
            var catalog0_actual = catalog0_V0.Merge(catalog0_V1, MergeMode.NewWins);

            // assert
            var expected = JsonSerializer.Serialize(catalog0_Vnew);
            var actual = JsonSerializer.Serialize(catalog0_actual);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CanMergeCatalogs_ExclusiveOr()
        {
            // arrange

            // prepare catalog 0
            var catalog0_V0 = _fixture.Catalog0_V0 with { Resources = new List<Resource>() };
            var resource0_V0 = _fixture.Resource0_V0 with { Representations = new List<Representation>() };
            var resource1_V0 = _fixture.Resource1_V0 with { Representations = new List<Representation>() };
            var representation0_V0 = _fixture.Representation0_V0;
            var representation1_V0 = _fixture.Representation1_V0;

            resource0_V0.Representations.Add(representation0_V0);
            resource0_V0.Representations.Add(representation1_V0);

            catalog0_V0.Resources.Add(resource0_V0);
            catalog0_V0.Resources.Add(resource1_V0);

            // prepare catalog 1
            var catalog0_V2 = _fixture.Catalog0_V2 with { Resources = new List<Resource>() };
            var resource0_V2 = _fixture.Resource0_V2 with { Representations = new List<Representation>() };
            var resource2_V0 = _fixture.Resource2_V0 with { Representations = new List<Representation>() };
            var representation2_V0 = _fixture.Representation2_V0;

            resource0_V2.Representations.Add(representation2_V0);

            catalog0_V2.Resources.Add(resource0_V2);
            catalog0_V2.Resources.Add(resource2_V0);

            // prepare merged
            var catalog0_Vxor = _fixture.Catalog0_Vxor with { Resources = new List<Resource>() };
            var resource0_Vxor = _fixture.Resource0_Vxor with { Representations = new List<Representation>() };
            var representation0_Vxor = _fixture.Representation0_Vxor;

            resource0_Vxor.Representations.Add(representation0_Vxor);
            resource0_Vxor.Representations.Add(representation1_V0);
            resource0_Vxor.Representations.Add(representation2_V0);

            catalog0_Vxor.Resources.Add(resource0_Vxor);
            catalog0_Vxor.Resources.Add(resource1_V0);
            catalog0_Vxor.Resources.Add(resource2_V0);

            // act
            var catalog0_actual = catalog0_V0.Merge(catalog0_V2, MergeMode.ExclusiveOr);

            // assert
            var expected = JsonSerializer.Serialize(catalog0_Vxor);
            var actual = JsonSerializer.Serialize(catalog0_actual);

            Assert.Equal(expected, actual);
        }
    }
}