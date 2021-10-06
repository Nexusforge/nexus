using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility.Tests
{
    public class DataModelFixture
    { 
        public DataModelFixture()
        {
            // catalogs
            this.Catalog0_V0 = new ResourceCatalog()
            {
                Id = "/A/B/C",
                Metadata = new Dictionary<string, string>()
                {
                    ["C_0_A"] = "A_0",
                    ["C_0_B"] = "B_0",
                }
            };

            this.Catalog0_V1 = new ResourceCatalog()
            {
                Id = "/A/B/C",
                Metadata = new Dictionary<string, string>()
                {
                    ["C_0_A"] = "A_1",
                    ["C_0_C"] = "C_0",
                }
            };

            this.Catalog0_V2 = new ResourceCatalog()
            {
                Id = "/A/B/C",
                Metadata = new Dictionary<string, string>()
                {
                    ["C_0_C"] = "C_0",
                }
            };

            this.Catalog0_Vmerged = new ResourceCatalog()
            {
                Id = "/A/B/C",
                Metadata = new Dictionary<string, string>()
                {
                    ["C_0_A"] = "A_1",
                    ["C_0_B"] = "B_0",
                    ["C_0_C"] = "C_0",
                }
            };

            this.Catalog0_Vxor = new ResourceCatalog()
            {
                Id = "/A/B/C",
                Metadata = new Dictionary<string, string>()
                {
                    ["C_0_A"] = "A_0",
                    ["C_0_B"] = "B_0",
                    ["C_0_C"] = "C_0",
                }
            };

            // resources
            this.Resource0_V0 = new Resource()
            {
                Id = "Resource0",
                Unit = "U_0",
                Groups = null,
                Metadata = new Dictionary<string, string>()
                {
                    ["R_0_A"] = "A_0",
                    ["R_0_B"] = "B_0",
                }
            };

            this.Resource0_V1 = new Resource()
            {
                Id = "Resource0",
                Unit = "U_1",
                Groups = new[] { "G_1" },
                Metadata = new Dictionary<string, string>()
                {
                    ["R_0_A"] = "A_1",
                    ["R_0_C"] = "C_0",
                }
            };

            this.Resource0_V2 = new Resource()
            {
                Id = "Resource0",
                Unit = null,
                Groups = new[] { "G_1" },
                Metadata = new Dictionary<string, string>()
                {
                    ["R_0_C"] = "C_0",
                }
            };

            this.Resource0_Vmerged = new Resource()
            {
                Id = "Resource0",
                Unit = "U_1",
                Groups = new[] { "G_1" },
                Metadata = new Dictionary<string, string>()
                {
                    ["R_0_A"] = "A_1",
                    ["R_0_B"] = "B_0",
                    ["R_0_C"] = "C_0",
                }
            };

            this.Resource0_Vxor = new Resource()
            {
                Id = "Resource0",
                Unit = "U_0",
                Groups = new[] { "G_1" },
                Metadata = new Dictionary<string, string>()
                {
                    ["R_0_A"] = "A_0",
                    ["R_0_B"] = "B_0",
                    ["R_0_C"] = "C_0",
                }
            };

            this.Resource1_V0 = new Resource()
            {
                Id = "Resource1",
                Unit = "U_0",
                Groups = new[] { "G_0" },
                Metadata = new Dictionary<string, string>()
                {
                    ["R_1_A"] = "A_0",
                    ["R_1_B"] = "B_0",
                }
            };

            this.Resource2_V0 = new Resource()
            {
                Id = "Resource2",
                Unit = "U_0",
                Groups = new[] { "G_0" },
                Metadata = new Dictionary<string, string>()
                {
                    ["R_2_A"] = "A_0",
                    ["R_2_B"] = "B_0",
                }
            };

            // representations
            this.Representation0_V0 = new Representation()
            {
                SamplePeriod = TimeSpan.FromMinutes(10),
                Detail = "",
                DataType = NexusDataType.FLOAT32,
            };

            this.Representation0_V1 = this.Representation0_V0;

            this.Representation0_Vmerged = this.Representation0_V0;

            this.Representation0_Vxor = this.Representation0_V0;

            this.Representation1_V0 = new Representation()
            {
                SamplePeriod = TimeSpan.FromMinutes(10),
                Detail = "",
                DataType = NexusDataType.FLOAT64,
            };

            this.Representation2_V0 = new Representation()
            {
                SamplePeriod = TimeSpan.FromSeconds(100),
                Detail = "",
                DataType = NexusDataType.UINT16,
            };
        }

        public ResourceCatalog Catalog0_V0 { get; }
        public ResourceCatalog Catalog0_V1 { get; }
        public ResourceCatalog Catalog0_V2 { get; }
        public ResourceCatalog Catalog0_Vmerged { get; }
        public ResourceCatalog Catalog0_Vxor { get; }

        public Resource Resource0_V0 { get; }
        public Resource Resource0_V1 { get; }
        public Resource Resource0_V2 { get; }
        public Resource Resource0_Vmerged { get; }
        public Resource Resource0_Vxor { get; }
        public Resource Resource1_V0 { get; }
        public Resource Resource2_V0 { get; }

        public Representation Representation0_V0 { get; }
        public Representation Representation0_V1 { get; }
        public Representation Representation0_Vmerged { get; }
        public Representation Representation0_Vxor { get; }
        public Representation Representation1_V0 { get; }
        public Representation Representation2_V0 { get; }
    }
}
