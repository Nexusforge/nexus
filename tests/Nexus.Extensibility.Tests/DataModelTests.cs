using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
        [InlineData("1_Hz", true)]
        [InlineData("12_Hz", true)]
        [InlineData("1_min", true)]
        [InlineData("1_s", true)]
        [InlineData("1_ms", true)]
        [InlineData("1_us", true)]
        [InlineData("1_ns", true)]

        [InlineData("1_Hz_mean", true)]
        [InlineData("1_Hz_Mean", true)]
        [InlineData("1_Hz_mean_polar", true)]
        [InlineData("12_Hz_mean", true)]
        [InlineData("1_min_mean", true)]
        [InlineData("1_s_mean", true)]
        [InlineData("1_ms_mean", true)]
        [InlineData("1_us_mean", true)]
        [InlineData("1_ns_mean", true)]

        // invalid
        [InlineData("1 Hz", false)]
        [InlineData("1_h", false)]
        [InlineData("a_Hz", false)]
        [InlineData("a1_Hz", false)]
        [InlineData("1a_Hz", false)]

        [InlineData("1_h_mean", false)]
        [InlineData("1 Hz_mean", false)]
        [InlineData("1_Hz mean", false)]
        [InlineData("1_Hz_meaﬂn", false)]
        [InlineData("1_Hz_mea?n", false)]
        [InlineData("a_Hz_mean", false)]
        [InlineData("1a_Hz_mean", false)]
        [InlineData("a1_Hz_mean", false)]
        public void CanValidateId(string id, bool isValid)
        {
            if (isValid)
                new Representation() { Id = id, DataType = NexusDataType.FLOAT64 };

            else
                Assert.Throws<ArgumentException>(() => new Representation() { Id = id, DataType = NexusDataType.FLOAT64 });
        }

        [Theory]
        [InlineData(NexusDataType.FLOAT32, true)]
        [InlineData((NexusDataType)0, false)]
        [InlineData((NexusDataType)9999, false)]
        public void CanValidateDataType(NexusDataType dataType, bool isValid)
        {
            if (isValid)
                new Representation() { Id = "1_Hz_mean", DataType = dataType };

            else
                Assert.Throws<ArgumentException>(() => new Representation() { Id = "1_Hz_mean", DataType = dataType });
        }

        [Theory]
        [InlineData("1_Hz_mean_polar", "00:00:01")]
        [InlineData("10_Hz", "00:00:00.1")]
        [InlineData("4000_Hz", "00:00:00.00025")]
        [InlineData("1_min", "00:01:00")]
        [InlineData("15_s", "00:00:15")]
        [InlineData("1_s", "00:00:01")]
        [InlineData("15_ms", "00:00:00.015")]
        [InlineData("1_ms", "00:00:00.001")]
        [InlineData("15_us", "00:00:00.000015")]
        [InlineData("1_us", "00:00:00.000001")]
        [InlineData("200_ns", "00:00:00.0000002")]
        [InlineData("15_ns", "00:00:00")]
        public void CanGetSamplePeriodFromId(string representationId, string expectedString)
        {
            var expected = TimeSpan.Parse(expectedString);
            var representation = new Representation() { Id = representationId };
            var actual = representation.GetSamplePeriod();

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