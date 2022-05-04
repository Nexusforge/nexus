using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace Other
{
    public class UtilitiesTests
    {
        [Theory]

        [InlineData("Basic", "true", new string[0], new string[0], true)]
        [InlineData("Basic", "false", new string[] { "/D/E/F", "/A/B/C", "/G/H/I" }, new string[0], true)]
        [InlineData("Basic", "false", new string[] { "^/A/B/.*" }, new string[0], true)]
        [InlineData("Basic", "false", new string[0], new string[] { "A" }, true)]

        [InlineData("Basic", "false", new string[0], new string[0], false)]
        [InlineData("Basic", "false", new string[] { "/D/E/F", "/A/B/C2", "/G/H/I" }, new string[0], false)]
        [InlineData("Basic", "false", new string[0], new string[] { "A2" }, false)]
        [InlineData(null, "true", new string[0], new string[0], false)]
        public void CanDetermineCatalogAccessibility(
            string authenticationType, 
            string isAdmin, 
            string[] canAccessCatalog,
            string[] canAccessGroup,
            bool expected)
        {
            // Arrange
            var catalogId = "/A/B/C";
            var catalogMetadata = new CatalogMetadata(default, GroupMemberships: new[] { "A" }, default);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new Claim[] { new Claim(NexusClaims.IS_ADMIN, isAdmin) }
                .Concat(canAccessCatalog.Select(value => new Claim(NexusClaims.CAN_ACCESS_CATALOG, value)))
                .Concat(canAccessGroup.Select(value => new Claim(NexusClaims.CAN_ACCESS_GROUP, value))), authenticationType));

            // Act
            var actual = AuthorizationUtilities.IsCatalogAccessible(catalogId, catalogMetadata, principal);

            // Assert
            Assert.Equal(expected, actual);
        }

        [Theory]

        [InlineData("Basic", "true", new string[0], true)]
        [InlineData("Basic", "false", new string[] { "/D/E/F", "/A/B/C", "/G/H/I" }, true)]
        [InlineData("Basic", "false", new string[] { "^/A/B/.*" }, true)]

        [InlineData("Basic", "false", new string[0], false)]
        [InlineData(null, "true", new string[0], false)]
        public void CanDetermineCatalogEditability(
            string authenticationType,
            string isAdmin,
            string[] canEditCatalog,
            bool expected)
        {
            // Arrange
            var catalogId = "/A/B/C";

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
               new Claim[] { new Claim(NexusClaims.IS_ADMIN, isAdmin) }
               .Concat(canEditCatalog.Select(value => new Claim(NexusClaims.CAN_EDIT_CATALOG, value))), authenticationType));

            // Act
            var actual = AuthorizationUtilities.IsCatalogEditable(catalogId, principal);

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CanApplyRepresentationStatus()
        {
            // Arrange
            var data = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
            var actual = new double[status.Length];
            var expected = new double[] { 1, double.NaN, 3, double.NaN, 5, double.NaN, 7, double.NaN };

            // Act
            BufferUtilities.ApplyRepresentationStatus<int>(data, status, actual);

            // Assert
            Assert.True(expected.SequenceEqual(actual.ToArray()));
        }

        [Fact]
        public void CanApplyRepresentationStatusByType()
        {
            // Arrange
            var data = new CastMemoryManager<int, byte>(new int[] { 1, 2, 3, 4, 5, 6, 7, 8 }).Memory;
            var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
            var actual = new double[status.Length];
            var expected = new double[] { 1, double.NaN, 3, double.NaN, 5, double.NaN, 7, double.NaN };

            // Act
            BufferUtilities.ApplyRepresentationStatusByDataType(NexusDataType.INT32, data, status, actual);

            // Assert
            Assert.True(expected.SequenceEqual(actual.ToArray()));
        }

        public static IList<object[]> ToDoubleData = new List<object[]>
        {
            new object[]{ (byte)99, (double)99 },
            new object[]{ (sbyte)-99, (double)-99 },
            new object[]{ (ushort)99, (double)99 },
            new object[]{ (short)-99, (double)-99 },
            new object[]{ (uint)99, (double)99 },
            new object[]{ (int)-99, (double)-99 },
            new object[]{ (ulong)99, (double)99 },
            new object[]{ (long)-99, (double)-99 },
            new object[]{ (float)-99.123, (double)-99.123 },
            new object[]{ (double)-99.123, (double)-99.123 },
        };

        [Theory]
        [MemberData(nameof(UtilitiesTests.ToDoubleData))]
        public void CanGenericConvertToDouble<T>(T value, double expected)
            where T : unmanaged //, IEqualityComparer<T> (does not compile correctly)
        {
            // Arrange

            // Act
            var actual = GenericToDouble<T>.ToDouble(value);

            // Assert
            Assert.Equal(expected, actual, precision: 3);
        }

        public static IList<object[]> BitOrData = new List<object[]>
        {
            new object[]{ (byte)3, (byte)4, (byte)7 },
            new object[]{ (sbyte)-2, (sbyte)-3, (sbyte)-1 },
            new object[]{ (ushort)3, (ushort)4, (ushort)7 },
            new object[]{ (short)-2, (short)-3, (short)-1 },
            new object[]{ (uint)3, (uint)4, (uint)7 },
            new object[]{ (int)-2, (int)-3, (int)-1 },
            new object[]{ (ulong)3, (ulong)4, (ulong)7 },
            new object[]{ (long)-2, (long)-3, (long)-1 },
        };

        [Theory]
        [MemberData(nameof(UtilitiesTests.BitOrData))]
        public void CanGenericBitOr<T>(T a, T b, T expected)
           where T : unmanaged //, IEqualityComparer<T> (does not compile correctly)
        {
            // Arrange


            // Act
            var actual = GenericBitOr<T>.BitOr(a, b);

            // Assert
            Assert.Equal(expected, actual);
        }

        public static IList<object[]> BitAndData = new List<object[]>
        {
            new object[]{ (byte)168, (byte)44, (byte)40 },
            new object[]{ (sbyte)-88, (sbyte)44, (sbyte)40 },
            new object[]{ (ushort)168, (ushort)44, (ushort)40 },
            new object[]{ (short)-88, (short)44, (short)40 },
            new object[]{ (uint)168, (uint)44, (uint)40 },
            new object[]{ (int)-88, (int)44, (int)40 },
            new object[]{ (ulong)168, (ulong)44, (ulong)40 },
            new object[]{ (long)-88, (long)44, (long)40 },
        };

        [Theory]
        [MemberData(nameof(UtilitiesTests.BitAndData))]
        public void CanGenericBitAnd<T>(T a, T b, T expected)
           where T : unmanaged //, IEqualityComparer<T> (does not compile correctly)
        {
            // Arrange


            // Act
            var actual = GenericBitAnd<T>.BitAnd(a, b);

            // Assert
            Assert.Equal(expected, actual);
        }

        record MyType(int A, string B, TimeSpan C);

        [Fact]
        public void CanSerializeAndDeserializeTimeSpan()
        {
            // Arrange
            var expected = new MyType(A: 1, B: "Two", C: TimeSpan.FromSeconds(1));

            // Act
            var jsonString = JsonSerializerHelper.SerializeIntended(expected);
            var actual = JsonSerializer.Deserialize<MyType>(jsonString);

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CanCastMemory()
        {
            // Arrange
            var values = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var expected = new int[] { 67305985, 134678021 };

            // Act
            var actual = new CastMemoryManager<byte, int>(values).Memory;
            var actualReadonly = new ReadonlyCastMemoryManager<byte, int>(new ReadOnlyMemory<byte>(values)).Memory;

            // Assert
            Assert.True(expected.SequenceEqual(actual.ToArray()));
            Assert.True(expected.SequenceEqual(actualReadonly.ToArray()));
        }


        [Fact]
        public void CanDetermineSizeOfNexusDataType()
        {
            // Arrange
            var values = NexusUtilities.GetEnumValues<NexusDataType>();
            var expected = new[] { 1, 2, 4, 8, 1, 2, 4, 8, 4, 8 };

            // Act
            var actual = values.Select(value => NexusUtilities.SizeOf(value));

            // Assert
            Assert.Equal(expected, actual);
        }
    }
}