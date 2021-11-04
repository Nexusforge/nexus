using Nexus.DataModel;
using System;
using System.Collections.Generic;
using Xunit;

namespace Nexus.Extensibility.Tests
{
    public class DateModelExtensionsTests
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
    }
}