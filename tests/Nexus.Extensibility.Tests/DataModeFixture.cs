﻿using Nexus.DataModel;
using System;

namespace Nexus.Extensibility.Tests
{
    public class DataModelFixture
    { 
        public DataModelFixture()
        {
            // catalogs
            this.Catalog0_V0 = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_0")
                .WithProperty("C_0_B", "B_0")
                .Build(); ;

            this.Catalog0_V1 = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_1")
                .WithProperty("C_0_C", "C_0")
                .Build();

            this.Catalog0_V2 = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_C", "C_0")
                .Build();

            this.Catalog0_Vmerged = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_1")
                .WithProperty("C_0_B", "B_0")
                .WithProperty("C_0_C", "C_0")
                .Build();

            this.Catalog0_Vxor = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_0")
                .WithProperty("C_0_B", "B_0")
                .WithProperty("C_0_C", "C_0")
                .Build();

            // resources
            this.Resource0_V0 = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithProperty("R_0_A", "A_0")
                .WithProperty("R_0_B", "B_0")
                .Build();

            this.Resource0_V1 = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_1")
                .WithDescription("D_1")
                .WithGroups("G_1")
                .WithProperty("R_0_A", "A_1")
                .WithProperty("R_0_C", "C_0")
                .Build();

            this.Resource0_V2 = new ResourceBuilder(id: "Resource0")
               .WithGroups("G_1")
               .WithProperty("R_0_C", "C_0")
               .Build();

            this.Resource0_Vmerged = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_1")
                .WithDescription("D_1")
                .WithProperty("R_0_A", "A_1")
                .WithProperty("R_0_B", "B_0")
                .WithProperty("Nexus:Groups:0", "G_1")
                .WithProperty("R_0_C", "C_0")
                .Build();

            this.Resource0_Vxor = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithProperty("R_0_A", "A_0")
                .WithProperty("R_0_B", "B_0")
                .WithProperty("Nexus:Groups:0", "G_1")
                .WithProperty("R_0_C", "C_0")
                .Build();

            this.Resource1_V0 = new ResourceBuilder(id: "Resource1")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithGroups("G_0")
                .WithProperty("R_1_A", "A_0")
                .WithProperty("R_1_B", "B_0")
                .Build();

            this.Resource2_V0 = new ResourceBuilder(id: "Resource2")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithGroups("G_0")
                .WithProperty("R_2_A", "A_0")
                .WithProperty("R_2_B", "B_0")
                .Build();

            // representations
            this.Representation0_V0 = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromMinutes(10));

            this.Representation0_V1 = this.Representation0_V0;

            this.Representation0_Vmerged = this.Representation0_V0;

            this.Representation0_Vxor = this.Representation0_V0;

            this.Representation1_V0 = new Representation(
               dataType: NexusDataType.FLOAT64,
               samplePeriod: TimeSpan.FromMinutes(10));

            this.Representation2_V0 = new Representation(
               dataType: NexusDataType.UINT16,
               samplePeriod: TimeSpan.FromMinutes(100));
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
