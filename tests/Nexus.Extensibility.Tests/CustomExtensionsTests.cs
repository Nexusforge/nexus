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
    }
}