using System;
using System.IO;
using System.Net;

namespace Nexus.Core.Tests
{
    public class AggregationDataSourceFixture : IDisposable
    {
        public AggregationDataSourceFixture()
        {
            var rootPath = this.InitializeDatabase();
            this.ResourceLocator = new Uri(rootPath);
        }

        public Uri ResourceLocator { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.ResourceLocator.AbsolutePath, true);
            }
            catch
            {
                //
            }
        }

        private string InitializeDatabase()
        {
            // create dirs
            var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            Directory.CreateDirectory(root);

            var catalog1 = "/A/B/C";
            var catalog2 = "/A2/B/C";

            var monthFolderCatalog1 = Path.Combine(root, "DATA", WebUtility.UrlEncode(catalog1));
            var dataFolderPathEmpty1 = Path.Combine(monthFolderCatalog1, "2020-06");
            Directory.CreateDirectory(dataFolderPathEmpty1);

            var monthFolderCatalog2 = Path.Combine(root, "DATA", WebUtility.UrlEncode(catalog2));
            var dataFolderPathEmpty2 = Path.Combine(monthFolderCatalog2, "2020-06");
            Directory.CreateDirectory(dataFolderPathEmpty2);

            // create files
            var dayOffset = 86400 * 100;
            var hourOffset = 360000;
            var halfHourOffset = hourOffset / 2;

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            // day 1
            var dateTime1 = new DateTime(2020, 07, 08);
            var folderPath1 = Path.Combine(monthFolderCatalog1, dateTime1.ToString("yyyy-MM"), dateTime1.ToString("dd"));
            var filePath1 = Path.Combine(folderPath1, $"{id1}_100_Hz_mean.nex");

            Directory.CreateDirectory(folderPath1);

            var buffer1 = new double[86400 * 100];
            buffer1.AsSpan().Fill(double.NaN);
            buffer1[0] = 99.27636;
            buffer1[2] = 99.27626;
            buffer1[dayOffset - 1] = 2323e-3;

            AggregationFile.Create<double>(filePath1, buffer1);

            // day 2
            var dateTime2 = new DateTime(2020, 07, 09);
            var folderPath2 = Path.Combine(monthFolderCatalog1, dateTime2.ToString("yyyy-MM"), dateTime2.ToString("dd"));
            var filePath2 = Path.Combine(folderPath2, $"{id1}_100_Hz_mean.nex");

            Directory.CreateDirectory(folderPath2);

            var buffer2 = new double[86400 * 100];
            buffer2.AsSpan().Fill(double.NaN);
            buffer2[0] = 98.27636;
            buffer2[2] = 97.27626;
            buffer2[dayOffset - hourOffset - 1] = 2323e-6;
            buffer2[dayOffset - halfHourOffset + 0] = 90.27636;
            buffer2[dayOffset - halfHourOffset + 2] = 90.27626;
            buffer2[dayOffset - 1] = 2323e-9;

            AggregationFile.Create<double>(filePath2, buffer2);

            // second catalog
            var folderPath3 = Path.Combine(monthFolderCatalog2, dateTime1.ToString("yyyy-MM"), dateTime1.ToString("dd"));
            var filePath3 = Path.Combine(folderPath3, $"{id2}_100_Hz_mean.nex");

            Directory.CreateDirectory(folderPath3);

            AggregationFile.Create<double>(filePath3, buffer1);

            //
            return root;
        }
    }
}
