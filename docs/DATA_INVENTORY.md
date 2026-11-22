# VNEXT Internship CRM — Master Data & Record Inventory

**Solution:** `VNEXTInternshipCRM` | **Publisher Prefix:** `vn` | **Base Currency:** `USD` (`$`, Precision `2`; Product Unit Price Precision `4`) | **Time Zone:** `UTC`  
**Last Synchronized & Audited Against Live Dataverse:** `2026-11-23`  
**Data Standard:** 100% Synthetic B2B Records (`@example.com` / `@example.test`), Zero Real-World PII

---

## 1. Overview & Data Architecture Principles

This document is the **single source of truth for every data record** currently provisioned in the `VNEXTInternshipCRM` Dataverse environment. Every GUID, lookup relationship, choice value, and field attribute below was audited directly against live Dataverse Web API responses.

1. **Safe Seed Boundary (`§8.2`):** Core transactional entities (`vn_opportunity`, `vn_opportunityline`, `vn_approvalrequest`, `vn_opportunitysalesprocess`) contain **zero raw records**. Deals, lines, BPF instances, and approval requests are created exclusively via synchronous transactional Custom APIs (`EXT10`–`EXT13`) so business rules and guard plug-ins (`PL01`–`PL03`) are never bypassed.
2. **Sibling Business Unit Isolation (`§6.1`):** Operational records (`vn_lead`, `account`, `contact`, activities) span **`Sales A`**, **`Sales B`**, and Root BU **`org3b306249`** to test territory isolation and cross-BU customer reuse.
3. **Four-Decimal Currency Precision (`§1.1`, `BR17`):** Catalog items (`vn_product`) store `Money(4)` unit prices (`PrecisionSource = 0`) to test commercial `MidpointRounding.AwayFromZero` arithmetic.

---

## 2. Business Units & Default Teams (`businessunit` & `team`)

Hierarchy Security is **Disabled**, and **zero custom cross-BU owner teams** exist. Only the platform-generated default BU owner teams are present:

| Business Unit Name | Role in Solution | `businessunitid` (GUID) | Parent BU | Default Owner Team (`teamid`, `teamtype = 0`) |
| :--- | :--- | :--- | :--- | :--- |
| **`org3b306249`** | Root Business Unit | `7e8d0b73-bdc9-f111-a05a-7ced8d5f8fd9` | *None (Org Root)* | `7f8d0b73-bdc9-f111-a05a-7ced8d5f8fd9` |
| **`Sales A`** | Sibling BU A (Territory A) | `f28280ec-82e3-f111-84ea-002248db522e` | `org3b306249` | `f38280ec-82e3-f111-84ea-002248db522e` |
| **`Sales B`** | Sibling BU B (Territory B) | `f68280ec-82e3-f111-84ea-002248db522e` | `org3b306249` | `f78280ec-82e3-f111-84ea-002248db522e` |

---

## 3. Platform Admin & Non-Interactive Test Personas (`systemuser` / `SR01`)

ONLY the Platform Admin holds an interactive Entra ID license seat, while all 6 `# ...` personas are non-interactive stub `systemuser` records verified via Dataverse security APIs (`RetrievePrincipalAccess`, `RetrieveUserPrivileges`, and `MSCRMCallerID`) per §§8.1, 8.3, and 10. All 6 sales test personas (`# ...`) operate under least-privilege custom roles and hold **zero administrative roles** (`System Administrator`, `System Customizer`, `Environment Maker`).

| Persona Label | Business Unit | Assigned Security Role | `systemuserid` (GUID) | Native Manager (`parentsystemuserid`) | Usage / Ownership Scope |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Maker / Platform Admin** | `org3b306249` (Root) | `System Administrator` | `70940b73-bdc9-f111-a05a-7ced8d5f8fd9` | *N/A* | Build / Seed Owner only (never used for role tests) |
| **`# Sales Administrator`** | `org3b306249` (Root) | `Sales Administrator` | `892b4868-89e3-f111-84ea-002248db522e` | *None* | Org-wide catalog & admin tests |
| **`# Manager A`** | `Sales A` | `Sales Manager` | `59c4affe-88e3-f111-84ea-002248db522e` | *None* | Territory A manager oversight |
| **`# Rep A1`** | `Sales A` | `Sales Representative` | `57631bb0-88e3-f111-84ea-002248db522e` | `# Manager A` (`59c4affe-...`) | Territory A representative |
| **`# Rep A2`** | `Sales A` | `Sales Representative` | `8b0f3227-89e3-f111-84ea-002248db522e` | `# Manager A` (`59c4affe-...`) | Territory A representative |
| **`# Manager B`** | `Sales B` | `Sales Manager` | `2576ba3f-89e3-f111-84ea-002248db522e` | *None* | Territory B manager oversight |
| **`# Rep B1`** | `Sales B` | `Sales Representative` | `7cf4a856-89e3-f111-84ea-002248db522e` | `# Manager B` (`2576ba3f-...`) | Territory B representative |

*(Login UPNs are maintained locally in gitignored `readme_local.md`.)*

---

## 4. Accounts (`account`) — 3 Records

All accounts are Active (`statecode = 0`, `statuscode = 1`) with Organization-level Read so reps across `Sales A` and `Sales B` can reuse accounts without creating duplicates.

| Account Name | `accountid` (GUID) | Account Type (`vn_accounttype`) | Industry (`vn_industry`) | Phone (`telephone1`) | City / Country | Primary Contact (`primarycontactid`) | Owner |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Cedar Manufacturing** | `726bdaff-81e2-f111-84ea-002248db522e` | Prospect (`100000000`) | Manufacturing (`100000000`) | `+1-555-0100` | Zurich, Switzerland | Karim Haddad (`766bdaff-...`) | Platform Admin (`70940b73-...`) |
| **Alpine Logistics AG** | `746bdaff-81e2-f111-84ea-002248db522e` | Customer (`100000001`) | Services (`100000001`) | `+1-555-0200` | Geneva, Switzerland | Elena Meyer (`796bdaff-...`) | `# Rep B1` (`7cf4a856-...`) |
| **Byblos Automation Systems** | `5f4fe6fa-cee4-f111-84ea-002248db522e` | Prospect (`100000000`) | Manufacturing (`100000000`) | `+961-9-5550200` | Byblos, Lebanon | *(None)* | `# Rep A1` (`57631bb0-...`) |

---

## 5. Contacts (`contact`) — 4 Records

All contacts are Active (`statecode = 0`, `statuscode = 1`), strictly parented to an Account (`parentcustomerid` required per `DM05`). Note that phone numbers are stored on `mobilephone` (`telephone1` is `null`).

| Full Name | `contactid` (GUID) | Parent Account | Job Title | Email (`emailaddress1`) | Mobile (`mobilephone`) | Preferred Method (`vn_preferredmethod`) | Do Not Contact (`vn_donotcontact`) | Owner |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| **Karim Haddad** | `766bdaff-81e2-f111-84ea-002248db522e` | Cedar Manufacturing (`726bdaff-...`) | Procurement Director | `karim.haddad@example.com` | `+1-555-0101` | Email (`100000000`) | `false` | Platform Admin (`70940b73-...`) |
| **Elena Meyer** | `796bdaff-81e2-f111-84ea-002248db522e` | Alpine Logistics AG (`746bdaff-...`) | Operations VP | `elena.meyer@example.com` | `+1-555-0201` | Phone (`100000001`) | `false` | `# Rep B1` (`7cf4a856-...`) |
| **Nadia Haddad** | `1505a2a0-cbe4-f111-84ea-002248db522e` | Cedar Manufacturing (`726bdaff-...`) | Procurement Officer | *(null)* | `+961-3-5550199` | Phone (`100000001`) | **`true`** *(DNC guard test)* | Platform Admin (`70940b73-...`) |
| **Hadi Nassar** | `c4689a13-cfe4-f111-84ea-002248db522e` | Byblos Automation Systems (`5f4fe6fa-...`) | Plant Engineering Lead | `hadi.nassar@byblosauto.example.test` | `+961-3-5550202` | Email (`100000000`) | `false` | `# Rep A1` (`57631bb0-...`) |

---

## 6. Product Catalog (`vn_product`) — 3 Records

Organization-owned records (`statecode = 0`, `statuscode = 1`, currency = `USD`), protected by unique Alternate Key `vn_sku_key` (`vn_sku`).

| Product Name (`vn_name`) | SKU (`vn_sku`) | `vn_productid` (GUID) | Unit Price (`vn_unitprice`) | Description (`vn_description`) |
| :--- | :--- | :--- | :---: | :--- |
| **Precision Sensor Unit** | `PRD-1001` | `7e6bdaff-81e2-f111-84ea-002248db522e` | **`$100.0050`** | Industrial telemetry sensor with 4-decimal unit pricing test |
| **Enterprise Gateway Controller** | `PRD-2001` | `8cc2c309-82e2-f111-84ea-002248db522e` | **`$12,500.0000`** | Core B2B gateway appliance |
| **Annual Maintenance SLA** | `PRD-3001` | `8ec2c309-82e2-f111-84ea-002248db522e` | **`$4,500.5000`** | 24/7 enterprise support package |

---

## 7. Inbound Leads (`vn_lead`) — 7 Records

Conversion lookups (`vn_account`, `vn_contact`, `vn_opportunity`, `vn_qualifiedon`) are `null` across all 7 leads.

### 7.1 Summary Table

| Lead Name / Topic (`vn_name`) | `vn_leadid` (GUID) | State / Status (`statuscode`) | Source (`vn_source`) | Rating (`vn_rating`) | Budget (`vn_budget`) | Next Follow-Up | Company (`vn_companyname`) | Owner (BU) |
| :--- | :--- | :--- | :--- | :--- | :---: | :---: | :--- | :--- |
| **Cedar Telemetry Upgrade Enquiry** | `91c2c309-82e2-f111-84ea-002248db522e` | Active / `New` (`100000000`) | Referral (`100000001`) | Warm (`100000001`) | *(null)* | `2026-11-19` | Cedar Manufacturing | `# Rep A2` (`Sales A`) |
| **Alpine Fleet Gateway Expansion** | `93c2c309-82e2-f111-84ea-002248db522e` | Active / `Contacted` (`100000001`) | Website (`100000000`) | Hot (`100000000`) | **`$65,000.00`** | `2026-11-18` | Alpine Logistics AG | `# Rep B1` (`Sales B`) |
| **Byblos PLC Modernization Inquiry** | `eb7c72d7-cee4-f111-84ea-002248db522e` | Active / `New` (`100000000`) | Referral (`100000001`) | Warm (`100000001`) | *(null)* | `2026-11-20` | Byblos Automation Systems | `# Rep A1` (`Sales A`) |
| **Industrial Sensor Upgrade Inquiry** | `ae872f56-cfe4-f111-84ea-002248db522e` | Active / `New` (`100000000`) | Referral (`100000001`) | Warm (`100000001`) | *(null)* | `2026-11-20` | Levant Precision Parts | Platform Admin (`org3b306249`) |
| **Annual Fleet Analytics Expansion** | `50c9c97b-cfe4-f111-84ea-002248db522e` | Active / `Contacted` (`100000001`) | Website (`100000000`) | Hot (`100000000`) | **`$75,000.00`** | `2026-11-19` | Cedar Manufacturing | Platform Admin (`org3b306249`) |
| **Trade Show Cold-Chain Monitoring** | `c1ce9297-cfe4-f111-84ea-002248db522e` | Active / `Contacted` (`100000001`) | Event (`100000003`) | Cold (`100000002`) | *(null)* | `2026-11-27` | Phoenicia Logistics | Platform Admin (`org3b306249`) |
| **Legacy Packaging Retro-Fit (No Budget)** | `ddbd9fba-cfe4-f111-84ea-002248db522e` | Inactive (`1`) / `Disqualified` (`100000003`) | Cold Call (`100000002`) | Cold (`100000002`) | *(null)* | `2026-11-20` | Batroun Packaging Co | Platform Admin (`org3b306249`) |

### 7.2 Detailed Lead Contact, Discovery & Disqualification Fields

| Lead Name | Contact (`vn_firstname` `vn_lastname`) | Email (`vn_email`) | Phone (`vn_phone`) | Need (`vn_need`) / Next Step (`vn_nextstep`) / Disqualification Details |
| :--- | :--- | :--- | :--- | :--- |
| **Cedar Telemetry Upgrade Enquiry** | Karim Haddad | `karim.haddad@example.com` | *(null)* | `vn_need`: *(null)* · `vn_nextstep`: *(null)* |
| **Alpine Fleet Gateway Expansion** | Elena Meyer | `elena.meyer@example.com` | `+1-555-0201` | `vn_need`: *"Needs 5 gateway controllers for Q1 warehouse rollout."*<br>`vn_nextstep`: *"Confirm technical readiness and qualify into opportunity."* |
| **Byblos PLC Modernization Inquiry** | Fadi Karam | `fadi.karam@byblosauto.example.test` | `+961-9-5550201` | `vn_need`: *(null)* · `vn_nextstep`: *"Schedule technical scoping call with Sales A"* |
| **Industrial Sensor Upgrade Inquiry** | Tariq Mansour | `tariq.mansour@example.test` | `+961-70-5550101` | `vn_need`: *(null)* · `vn_nextstep`: *(null)* |
| **Annual Fleet Analytics Expansion** | Rami Khoury | `rami.khoury@cedarmfg.example.test` | `+961-71-5550102` | `vn_need`: *"Needs centralized quality-control hardware and annual support across 2 plants."*<br>`vn_nextstep`: *"Review customer selection and prepare for qualification in M3"* |
| **Trade Show Cold-Chain Monitoring** | Maya Saad | `maya.saad@example.test` | *(null)* | `vn_need`: *(null)* · `vn_nextstep`: *"Send introductory brochure next week"* |
| **Legacy Packaging Retro-Fit (No Budget)** | Sami Farah | `sami.farah@example.test` | *(null)* | **Reason:** `No Budget` (`100000001`) · **Disqualified On:** `2026-11-20T10:47:07Z`<br>**Comments:** *"Prospect confirmed zero capital budget for FY2026."* |

---

## 8. Native Activities (`task` & `phonecall`) — 3 Records

Total `activitypointer` records = **3** (2 Tasks, 1 Phone Call; **0** Emails, Appointments, Letters, or Faxes).

### 8.1 Tasks (`task`) — 2 Records

| Subject | `activityid` (GUID) | State / Status | Priority | Scheduled End (UTC) | Regarding Record | Description | `vn_automationkey` | Owner |
| :--- | :--- | :--- | :---: | :---: | :--- | :--- | :---: | :--- |
| **Prepare discovery checklist for Cedar Manufacturing** | `ecb2c6e9-cfe4-f111-84ea-002248db522e` | Open (`0`) / Not Started (`2`) | High (`2`) | `2026-11-20T20:30:00Z` | Lead: *Annual Fleet Analytics Expansion* (`50c9c97b-...`) | *"Review plant requirements before qualification."* | *(null)* | Platform Admin (`70940b73-...`) |
| **Initial prospect research completed** | `241f492c-d0e4-f111-84ea-002248db522e` | Completed (`1`) / Completed (`5`) | Normal (`1`) | `2026-11-19T10:00:00Z` | Lead: *Annual Fleet Analytics Expansion* (`50c9c97b-...`) | *"Verified Cedar Manufacturing website and plant locations."* | *(null)* | Platform Admin (`70940b73-...`) |

### 8.2 Phone Calls (`phonecall`) — 1 Record

| Subject | `activityid` (GUID) | State / Status | Direction | Phone (`phonenumber`) | Scheduled End (UTC) | Regarding Record | Description & Activity Parties | Owner |
| :--- | :--- | :--- | :---: | :--- | :---: | :--- | :--- | :--- |
| **Follow-up call on Cedar Manufacturing profile details** | `587afc0f-d0e4-f111-84ea-002248db522e` | Open (`0`) / Open (`1`) | Outgoing (`true`) | `+961-1-5550100` | `2026-11-18T12:00:00Z` | Contact: *Karim Haddad* (`766bdaff-...`) | *"Confirm primary technical contact and phone number."*<br>**Sender (`1`):** Platform Admin (`70940b73-...`) · **To (`2`):** Karim Haddad (`766bdaff-...`) | Platform Admin (`70940b73-...`) |

---

## 9. Empty Transactional & Annotation Tables (`0` Records)

Verified directly against live Dataverse Web API (`Count = 0` on all 5 tables):

| Entity Set | Table Display Name | Current Record Count | Architectural Rule |
| :--- | :--- | :---: | :--- |
| `vn_opportunities` | Opportunity | **`0`** | Created exclusively via Custom API `EXT10 vn_QualifyLead` (`§8.2`) |
| `vn_opportunitylines` | Opportunity Line | **`0`** | Created exclusively via Custom API `EXT12 vn_EditOpportunityLine` (`§8.2`) |
| `vn_approvalrequests` | Approval Request | **`0`** | Created exclusively via Custom API `EXT13 vn_ManageDealApproval` & Scoped Internal Writer |
| `vn_opportunitysalesprocesses` | Opportunity Sales Process (BPF) | **`0`** | Initialized server-side upon Lead qualification (`EXT10`) |
| `annotations` | Notes | **`0`** | Clean baseline; no stray test notes |

---

## 10. Fixed Policy Environment Variables & Base Currency (`§1.1`, `§8.1`)

| Schema Name | Data Type | Default Value | Specification Reference |
| :--- | :---: | :---: | :--- |
| **`vn_ApprovalAmount`** | Decimal | `50000` | Deal `Total >= $50,000` (USD) requires Manager approval (`BR20`) |
| **`vn_ApprovalDiscount`** | Decimal | `10` | Line discount `> 10.00%` requires Manager approval (`BR20`) |
| **`vn_MaxDiscount`** | Decimal | `30` | Maximum allowed line discount `30.00%` (`§1.1` / `DM07` / `EXT12`) |
| **`vn_StaleDays`** | Decimal | `14` | UTC days without progress before Open deal is flagged Stale (`BR30` / `V08`) |
| **`vn_EscalationDays`** | Decimal | `7` | UTC days overdue before open work escalates to Manager (`WF02`) |
| **Base Currency** | Lookup | **`USD`** (`US Dollar`, `c6c8babe-05ca-f111-a05a-7ced8d5f8fd9`) | Precision `2` standard; Precision `4` on both `vn_product.vn_unitprice` and `vn_opportunityline.vn_unitprice` |
