namespace SignMaker
{
    public class ItemLicense
    {
        public class Payload
        {
            public int __Version { get; set; }
            public List<subLicense> subLicenses { get; set; }
            public List<Provenancetree> provenanceTree { get; set; }
            public List<object> licensePoints { get; set; }
            public string resourceTypeName { get; set; }
            public string resourceId { get; set; }
            public List<object> lockData { get; set; }
            public List<object> missingPoints { get; set; }

            public Payload(Guid creatorId, Guid newLicenseId, string itemName, int itemPrice, string resourceType, string resourceId)
            {
                __Version = 13;
                licensePoints = new List<object>();
                this.resourceTypeName = resourceType;
                this.resourceId = resourceId;
                this.lockData = new List<object>();
                this.missingPoints = new List<object>();
                this.provenanceTree = new List<Provenancetree>()
                    {
                        new Provenancetree(creatorId),
                    };
                this.subLicenses = new List<subLicense>()
                    {
                        new subLicense(creatorId, newLicenseId, itemName, itemPrice),
                    };
            }
        }

        public class subLicense
        {
            public int __Version { get; set; }
            public Guid creatorId { get; set; }
            public Guid platonicId { get; set; }
            public Guid licenseId { get; set; }
            public int price { get; set; }
            public string trackingData { get; set; }
            public bool canExtract { get; set; }
            public bool canModify { get; set; }
            public int nodeVersion { get; set; }
            public string name { get; set; }

            public subLicense(Guid personaId, Guid newLicenseId, string itemName, int itemPrice)
            {
                __Version = 5;
                creatorId = personaId;
                platonicId = Guid.Empty;
                licenseId = newLicenseId;
                price = itemPrice;
                trackingData = "";
                canExtract = true;
                canModify = false;
                nodeVersion = 2;
                name = itemName;
            }
        }

        public class Provenancetree
        {
            public int __Version { get; set; }
            public string name { get; set; }
            public Guid creatorId { get; set; }
            public List<Guid> childIds { get; set; }
            public Guid provenanceId { get; set; }
            public int nodeVersion { get; set; }
            public int licenseIndex { get; set; }

            public Provenancetree(Guid personaId)
            {
                __Version = 4;
                name = "";
                creatorId = personaId;
                childIds = new List<Guid>();
                provenanceId = new Guid("9341e5cc-0cb6-4f73-94e3-e202c5ea29ba");
                nodeVersion = 1;
                licenseIndex = 0;
            }
        }

        public string hash { get; set; }
        public Payload payload { get; set; }


        public ItemLicense(string licenseAssetId, Guid creatorId, Guid newLicenseId, string itemName, int itemPrice, string resourceType, string resourceId)
        {
            hash = licenseAssetId;
            payload = new Payload(creatorId, newLicenseId, itemName, itemPrice, resourceType, resourceId);
        }
    }
}