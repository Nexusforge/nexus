//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Logging.Abstractions;
//using Nexus.Buffers;
//using Nexus.Extensibility;
//using Nexus.Extension.Famos;
//using Nexus.Infrastructure;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using Xunit;

//namespace Nexus.Tests
//{
//    public class FamosDataWriterTests
//    {
//        [Fact]
//        public void FamosWriterCreatesDatFile()
//        {
//            // Arrange
//            var services = new ServiceCollection();

//            ConfigureServices(services);

//            var provider = services.BuildServiceProvider();
//            var dataWriter = provider.GetRequiredService<FamosWriter>();

//            var catalogGuid = Guid.NewGuid();
//            var dataDirectoryPath = Path.Combine(Path.GetTempPath(), catalogGuid.ToString());

//            Directory.CreateDirectory(dataDirectoryPath);

//            var catalogDescription = new NexusCatalogDescription(catalogGuid, 1, "a", "b", "c");
//            var customMetadataEntrySet = new List<CustomMetadataEntry>();
//            var dataWriterContext = new DataWriterContext("Nexus", dataDirectoryPath, catalogDescription, customMetadataEntrySet);

//            var resourceDescriptionSet = new List<ResourceDescription>()
//            {
//                this.CreateResourceDescription("Var1", "Group1", NexusDataType.FLOAT64, new SampleRateContainer(8640000), "Unit1"),
//                this.CreateResourceDescription("Var2", "Group2", NexusDataType.FLOAT64, new SampleRateContainer(8640000), "Unit2"),
//                this.CreateResourceDescription("Var3", "Group1", NexusDataType.FLOAT64, new SampleRateContainer(86400), "Unit2"),
//            };

//            var currentDate = new DateTime(2019, 1, 1, 15, 0, 0);
//            var period = TimeSpan.FromMinutes(1);

//            // Act
//            dataWriter.Configure(dataWriterContext, resourceDescriptionSet);

//            for (int i = 0; i < 9; i++)
//            {
//                var buffers = resourceDescriptionSet.Select(current =>
//                {
//                    var length = (int)current.SampleRate.SamplesPerDay / 1440;
//                    var offset = length * i;
//                    var data = Enumerable.Range(offset, length).Select(value => value * 0 + (double)i + 1).ToArray();

//                    return BufferUtilities.CreateSimpleBuffer(data);
//                }).ToList();

//                dataWriter.Write(currentDate, period, buffers.Cast<IBuffer>().ToList());
//                currentDate += period;
//            }

//            dataWriter.Dispose();

//            // Assert
//        }

//        private ResourceDescription CreateResourceDescription(string resourceName, string group, NexusDataType dataType, SampleRateContainer sampleRate, string unit)
//        {
//            var guid = Guid.NewGuid();
//            var datasetName = sampleRate.ToUnitString();

//            return new ResourceDescription(guid, resourceName, datasetName, group, dataType, sampleRate, unit, BufferType.Simple);
//        }

//        private static void ConfigureServices(IServiceCollection services)
//        {
//            services.AddSingleton(current => new FamosWriter(new FamosSettings() { FilePeriod = TimeSpan.FromMinutes(10) }, NullLogger.Instance));
//        }
//    }
//}