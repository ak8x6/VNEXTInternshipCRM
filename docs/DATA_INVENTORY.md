# VNEXT Internship CRM — Master Data & Record Inventory

**Solution:** `VNEXTInternshipCRM` | **Publisher Prefix:** `vn` | **Base Currency:** `USD` (`$`, Precision `2`; Product Unit Price Precision `4`) | **Time Zone:** `UTC`  
**Last Synchronized & Audited Against Live Dataverse:** `2026-11-27` *(Milestone 3 Complete — Transactional CRM & Live Demo Pack)*  
**Data Standard:** 100% Synthetic B2B Records (`@example.com` / `@example.test`), Zero Real-World PII

---

## 1. Overview & Data Architecture Principles

This document is the **single source of truth for the curated B2B data inventory** currently provisioned in the `VNEXTInternshipCRM` Dataverse environment. Every GUID, lookup relationship, choice value, line total, and BPF state below was audited directly against live Dataverse Web API v9.2 responses.

1. **Legitimate M3 Transactional Provenance (`§8.2`, `§8.4`):** Every single Opportunity (`13` clean deals), Opportunity Line (`12` clean lines), and Opportunity Sales Process BPF instance (`13` strictly `1:1` rows) was created exclusively through synchronous guarded Custom APIs (`EXT10 vn_QualifyLead`, `EXT12 vn_EditOpportunityLine`, and `EXT11 vn_TransitionOpportunity`) with zero bypasses of `PL01`–`PL03`.
2. **M3 / M5 Approval Boundary (`§8.4`):** `vn_approvalrequest` strictly contains **`0` records** until M5 delivers `EXT13` (`vn_ManageDealApproval`) and `WF04` genuine Manager approval integration.
3. **Sibling Business Unit Isolation (`§6.1`):** Operational records span **`Sales A`** (`Rep A1`, `Rep A2`, `Manager A`), **`Sales B`** (`Rep B1`, `Manager B`), and Root BU **`org3b306249`** to verify territory isolation, peer Rep isolation, and organization-wide Account/Contact reuse.
4. **Four-Decimal Currency Precision (`§1.1`, `BR17`):** Active catalog items (`vn_product`) store `Money(4)` unit prices (`PrecisionSource = 0`), verified live on `Cedar Sensor Calibration Rollout` (`3 × $100.0050 @ 10.00% = $270.02` using `MidpointRounding.AwayFromZero`).

---

## 2. Business Units & Default Teams (`businessunit` & `team`)

Hierarchy Security is **Disabled**, and **zero custom cross-BU owner teams** exist. Only the platform-generated default BU owner teams are present:

| Business Unit Name | Role in Solution | `businessunitid` (GUID) | Parent BU | Default Owner Team (`teamid`, `teamtype = 0`) |
| :--- | :--- | :--- | :--- | :--- |
| **`org3b306249`** | Root Business Unit | `7e8d0b73-bdc9-f111-a05a-7ced8d5f8fd9` | *None (Org Root)* | `7f8d0b73-bdc9-f111-a05a-7ced8d5f8fd9` |
| **`Sales A`** | Sibling BU A (Territory A) | `f28280ec-82e3-f111-84ea-002248db522e` | `org3b306249` | `f38280ec-82e3-f111-84ea-002248db522e` |
| **`Sales B`** | Sibling BU B (Territory B) | `f68280ec-82e3-f111-84ea-002248db522e` | `org3b306249` | `f78280ec-82e3-f111-84ea-002248db522e` |

---

## 3. Identities: Platform Admin, Business Personas (`SR01`) & Scoped Internal Writer (`SR03`)

All 6 sales test personas (`Sales Administrator`, `Manager A`, `Rep A1`, `Rep A2`, `Manager B`, `Rep B1`) are **enabled Microsoft Entra ID users** assigned **Office 365 E3 (`ENTERPRISEPACK`)**, **Microsoft Power Apps for Developer (`POWERAPPS_DEV`)**, and **Microsoft Power Automate Free (`FLOW_FREE`)** licenses, bound 1-to-1 to their Dataverse `systemuser` records (`azureactivedirectoryobjectid`), and **verified via live interactive browser sign-in**. All 6 hold **zero administrative roles** (`System Administrator`, `System Customizer`, `Environment Maker`), and their role/BU boundaries were verified both interactively and via Dataverse security APIs (`RetrievePrincipalAccess`, `RetrieveUserPrivileges`, and `MSCRMCallerID`). Meanwhile, the `SR03` **Scoped Internal Writer** (`VNEXT Internal VNEXT-CRM-Internal-Writer`) is a **non-interactive Entra ID App Registration / Dataverse Application User** located under **Power Platform Admin Center → Settings → Users + permissions → `Application users`** (not interactive `Users`; zero email, zero password, zero client secret, zero paid license seat) per §6.1.

| Persona / Technical Identity | Business Unit | Assigned Security Role | `systemuserid` (GUID) | Native Manager (`parentsystemuserid`) | Usage / Ownership Scope |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Maker / Platform Admin** | `org3b306249` (Root) | `System Administrator` | `70940b73-bdc9-f111-a05a-7ced8d5f8fd9` | *N/A* | Build, interactive UI demo & diagnostics (never substitutes for role tests) |
| **`Sales Administrator`** | `org3b306249` (Root) | `Sales Administrator` | `892b4868-89e3-f111-84ea-002248db522e` | *None* | Org-wide catalog & admin tests |
| **`Manager A`** | `Sales A` | `Sales Manager` | `59c4affe-88e3-f111-84ea-002248db522e` | *None* | Territory A manager oversight & `BR05` qualification |
| **`Rep A1`** | `Sales A` | `Sales Representative` | `57631bb0-88e3-f111-84ea-002248db522e` | `Manager A` (`59c4affe-...`) | Territory A representative |
| **`Rep A2`** | `Sales A` | `Sales Representative` | `8b0f3227-89e3-f111-84ea-002248db522e` | `Manager A` (`59c4affe-...`) | Territory A peer representative (`SR05` isolation) |
| **`Manager B`** | `Sales B` | `Sales Manager` | `2576ba3f-89e3-f111-84ea-002248db522e` | *None* | Territory B manager oversight |
| **`Rep B1`** | `Sales B` | `Sales Representative` | `7cf4a856-89e3-f111-84ea-002248db522e` | `Manager B` (`2576ba3f-...`) | Territory B representative |
| **`VNEXT Internal VNEXT-CRM-Internal-Writer`** *(SR03 App User)* | `org3b306249` (Root) | `VNEXT Internal CRM Writer` *(28 least-privilege rights; 0 Delete/Share/Admin)* | `f3f85cec-2de7-f111-84ea-002248db522e` | *N/A* | Technical service identity (`vn_InternalWriterUserId`) for scoped BPF & Approval writes inside verified Custom APIs |

*(Login UPNs and client identifiers are maintained locally in gitignored `readme_local.md`.)*

---

## 4. Curated Accounts (`account`) — 4 Active Records

All curated accounts are Active (`statecode = 0`, `statuscode = 1`) with Organization-level Read so reps across `Sales A` and `Sales B` reuse customer accounts without creating duplicates.

| Account Name | `accountid` (GUID) | Account Type (`vn_accounttype`) | Industry (`vn_industry`) | Phone (`telephone1`) | City / Country | Primary Contact (`primarycontactid`) | Owner |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Cedar Manufacturing** | `726bdaff-81e2-f111-84ea-002248db522e` | Prospect (`100000000`) | Manufacturing (`100000000`) | `+1-555-0100` | Zurich, Switzerland | Karim Haddad (`766bdaff-...`) | Platform Admin |
| **Alpine Logistics AG** | `746bdaff-81e2-f111-84ea-002248db522e` | Customer (`100000001`) | Services (`100000001`) | `+1-555-0200` | Geneva, Switzerland | Elena Meyer (`796bdaff-...`) | `Rep B1` |
| **Byblos Automation Systems** | `5f4fe6fa-cee4-f111-84ea-002248db522e` | Prospect (`100000000`) | Manufacturing (`100000000`) | `+961-9-5550200` | Byblos, Lebanon | *(null — preserved by `BR05`)* | `Rep A1` |
| **Phoenicia Robotics Ltd** *(created via `EXT10` Path 3)* | `8fac74dd-11e8-f111-84ea-002248db522e` | Prospect (`100000000`) | *(null)* | *(null)* | *(null)* | Nadim Khoury (`91ac74dd-...`) | Platform Admin |

---

## 5. Curated Contacts (`contact`) — 7 Active Records

All curated contacts are Active (`statecode = 0`, `statuscode = 1`), strictly parented to an immutable Account (`parentcustomerid` per `DM05`/`BR08`).

| Full Name | `contactid` (GUID) | Parent Account | Job Title | Email (`emailaddress1`) | Mobile (`mobilephone`) | Preferred Method | Do Not Contact | Owner |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| **Karim Haddad** | `766bdaff-81e2-f111-84ea-002248db522e` | Cedar Manufacturing | Procurement Director | `karim.haddad@example.com` | `+1-555-0101` | Email | `false` | Platform Admin |
| **Elena Meyer** | `796bdaff-81e2-f111-84ea-002248db522e` | Alpine Logistics AG | Operations VP | `elena.meyer@example.com` | `+1-555-0201` | Phone | `false` | `Rep B1` |
| **Nadia Haddad** | `1505a2a0-cbe4-f111-84ea-002248db522e` | Cedar Manufacturing | Procurement Officer | *(null)* | `+961-3-5550199` | Phone | **`true`** *(DNC guard)* | Platform Admin |
| **Hadi Nassar** | `c4689a13-cfe4-f111-84ea-002248db522e` | Byblos Automation Systems | Plant Engineering Lead | `hadi.nassar@byblosauto.example.test` | `+961-3-5550202` | Email | `false` | `Rep A1` |
| **Layla Mansour** *(created via `EXT10` Path 2 by `Manager A`)* | `6d0dde12-0fe8-f111-84ea-002248db522e` | Byblos Automation Systems | *(null)* | `layla.mansour@byblosauto.example.test` | *(null)* | Email | `false` | **`Rep A1`** *(`BR05`)* |
| **Nadim Khoury** *(created via `EXT10` Path 3)* | `91ac74dd-11e8-f111-84ea-002248db522e` | Phoenicia Robotics Ltd | *(null)* | `nadim.khoury@phoeniciarobotics.example.test` | *(null)* | Email | `false` | Platform Admin |
| **Rami Khoury** *(created via `EXT10` Path 2 browser test)* | `679666a2-17e8-f111-84ea-002248db522e` | Cedar Manufacturing | *(null)* | `rami.khoury@cedarmfg.example.test` | `+961-71-5550102` | Email | `false` | Platform Admin |

---

## 6. Product Catalog (`vn_product`) — 3 Active + 1 Clean Inactive Record

Organization-owned records (currency = `USD`), protected by Active unique Alternate Key `vn_sku_key` (`vn_sku`). ONLY `PRD-1001`, `PRD-2001`, and `PRD-3001` are Active (`V16`).

| Product Name (`vn_name`) | SKU (`vn_sku`) | `vn_productid` (GUID) | State | Unit Price (`vn_unitprice`) | Description (`vn_description`) |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Precision Sensor Unit** | `PRD-1001` | `7e6bdaff-81e2-f111-84ea-002248db522e` | **Active** | **`$100.0050`** | Industrial telemetry sensor with 4-decimal unit pricing test |
| **Enterprise Gateway Controller** | `PRD-2001` | `8cc2c309-82e2-f111-84ea-002248db522e` | **Active** | **`$12,500.0000`** | Core B2B gateway appliance |
| **Annual Maintenance SLA** | `PRD-3001` | `8ec2c309-82e2-f111-84ea-002248db522e` | **Active** | **`$4,500.5000`** | 24/7 enterprise support package |
| **Legacy Telemetry Module (Discontinued)** | `PRD-9001` | `82c321b8-0ee8-f111-84ea-002248db522e` | **Inactive** | `$500.0000` | Discontinued legacy sensor module kept inactive to verify `BR14` active-only Add Line filtering |

---

## 7. Curated Inbound Leads (`vn_lead`) — 25 Records (`10` Open · `13` Qualified · `2` Disqualified)

### 7.1 Open Leads Ready for Demo & Daily Queues (`10` Active)

Platform Admin's `7` Open Leads cover all 4 `C01` sources (`Website = 2`, `Referral = 2`, `Event = 2`, `Cold Call = 1`) and stage dedicated Contacted Leads for **all 3 `EXT10` customer paths** (`Path 1 Reuse+Reuse`, `Path 2 Reuse+Create`, `Path 3 Create+Create`) plus `[CRM002]` missing-Need and `Duplicate` Disqualify demos.

| Lead Name / Topic (`vn_name`) | `vn_leadid` (GUID) | Status | Source / Rating | Budget | Next Follow-Up | Company & Contact | Owner (BU) |
| :--- | :--- | :--- | :--- | :---: | :---: | :--- | :--- |
| **Cedar Telemetry Upgrade Enquiry** | `91c2c309-82e2-f111-84ea-002248db522e` | Active / `New` | Referral / Warm | *(null)* | `2026-11-19` | Cedar Manufacturing · Karim Haddad | `Rep A2` (`Sales A`) |
| **Byblos PLC Modernization Inquiry** | `eb7c72d7-cee4-f111-84ea-002248db522e` | Active / `New` | Referral / Warm | *(null)* | `2026-11-20` | Byblos Automation Systems · Fadi Karam | `Rep A1` (`Sales A`) |
| **Industrial Sensor Upgrade Inquiry** | `ae872f56-cfe4-f111-84ea-002248db522e` | Active / `New` | Referral / Warm | *(null)* | `2026-11-20` | Levant Precision Parts · Tariq Mansour | Platform Admin (`org3b306249`) |
| **Beirut Cold-Chain Retrofit Call** | `27d48eda-0ee8-f111-84ea-002248db522e` | Active / `New` | Cold Call / Warm | *(null)* | `2026-11-24` | Levant Cold Storage · Rami Fares | Platform Admin (`org3b306249`) |
| **Alpine Cold-Storage Telemetry Inquiry** | `29d48eda-0ee8-f111-84ea-002248db522e` | Active / `New` | Referral / Hot | *(null)* | `2026-11-24` | Alpine Logistics AG · Lukas Weber | `Rep B1` (`Sales B`) |
| **Cedar Plant 3 Sensor Modernization** | `d88cec0b-1ee8-f111-84ea-002248db522e` | Active / `Contacted` | Website / Hot | `$25,000.00` | `2026-11-24` | Cedar Manufacturing · Samer Darwish *(Demo `EXT10` Path 2 ready)* | Platform Admin (`org3b306249`) |
| **Tripoli Cold-Storage Feasibility Inquiry** | `db8cec0b-1ee8-f111-84ea-002248db522e` | Active / `Contacted` | Event / Cold | *(null)* | `2026-11-26` | Tripoli Cold Storage Co · Lina Chahine *(`vn_need = null`, `[CRM002]` / Disqualify ready)* | Platform Admin (`org3b306249`) |
| **Cedar Line 4 Predictive Maintenance** | `d01a5f5c-4fea-f111-84ea-002248db522e` | Active / `Contacted` | Website / Hot | `$18,000.00` | `2026-11-27` | Cedar Manufacturing · Karim Haddad *(Demo `EXT10` Path 1 Reuse+Reuse ready)* | Platform Admin (`org3b306249`) |
| **Levant Cleanroom Monitoring Rollout** | `d31a5f5c-4fea-f111-84ea-002248db522e` | Active / `Contacted` | Referral / Hot | `$32,000.00` | `2026-11-27` | Levant Pharma Automation · Maya Boulos *(Demo `EXT10` Path 3 Create+Create ready)* | Platform Admin (`org3b306249`) |
| **Cedar Plant 3 Sensor Inquiry (Duplicate Copy)** | `d61a5f5c-4fea-f111-84ea-002248db522e` | Active / `New` | Event / Cold | *(null)* | `2026-11-27` | Cedar Manufacturing · Samer Darwish *(Demo `Disqualify → Duplicate` ready)* | Platform Admin (`org3b306249`) |

### 7.2 Closed Leads (`13` Qualified · `2` Disqualified)

| Lead Name (`vn_name`) | `vn_leadid` (GUID) | Terminal Outcome | Resulting Opportunity / Reason | Owner (BU) |
| :--- | :--- | :--- | :--- | :--- |
| **Alpine Fleet Gateway Expansion** | `93c2c309-82e2-f111-84ea-002248db522e` | Inactive / `Qualified` | `Alpine Fleet Gateway Expansion` (`fc1f721f-...`) | `Rep B1` (`Sales B`) |
| **Annual Fleet Analytics Expansion** | `50c9c97b-cfe4-f111-84ea-002248db522e` | Inactive / `Qualified` | `Annual Fleet Analytics Expansion` (`6a9666a2-...`) | Platform Admin |
| **Lead - Byblos Plant Automation Phase 1** | `5942c40c-0fe8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Byblos Plant Automation Phase 1` (`5c42c40c-...`) | `Rep A1` (`Sales A`) |
| **Lead - Cedar Sensor Calibration Rollout** | `5f42c40c-0fe8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Cedar Sensor Calibration Rollout` (`630dde12-...`) | `Rep A1` (`Sales A`) |
| **Lead - Byblos Annual Support Renewal** | `6a0dde12-0fe8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Byblos Annual Support Renewal` (`700dde12-...`) | `Rep A1` (`Sales A`) |
| **Lead - Cedar Enterprise Gateway Upgrade** | `6dd15019-0fe8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Cedar Enterprise Gateway Upgrade` (`70d15019-...`) | `Rep A1` (`Sales A`) |
| **Lead - Cedar Plant 2 Telemetry Expansion** | `7bd15019-0fe8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Cedar Plant 2 Telemetry Expansion` (`7ed15019-...`) | `Rep A2` (`Sales A`) |
| **Lead - Phoenicia Robotics Telemetry Starter** | `a5f8c725-0fe8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Phoenicia Robotics Telemetry Starter` (`93ac74dd-...`) | Platform Admin |
| **Lead - Cedar Quality Control SLA Package** | `95ac74dd-11e8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Cedar Quality Control SLA Package` (`98ac74dd-...`) | Platform Admin |
| **Lead - Byblos Telemetry Pilot** | `e5ac74dd-11e8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Byblos Telemetry Pilot (Closed Won)` (`0bad74dd-...`) | `Rep A1` (`Sales A`) |
| **Lead - Alpine Warehouse Sensor Bid** | `c54e6de3-11e8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Alpine Warehouse Sensor Bid (Closed Lost)` (`c84e6de3-...`) | `Rep B1` (`Sales B`) |
| **Byblos Plant 2 Gateway Package Lead** | `ff10ea11-1ee8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Byblos Plant 2 Gateway Package` (`0211ea11-...`) | `Rep A1` (`Sales A`) |
| **Cedar Warehouse Pilot Evaluation Lead** | `0d11ea11-1ee8-f111-84ea-002248db522e` | Inactive / `Qualified` | `Cedar Warehouse Pilot Evaluation` (`1011ea11-...`) | Platform Admin |
| **Trade Show Cold-Chain Monitoring** | `c1ce9297-cfe4-f111-84ea-002248db522e` | Inactive / `Disqualified` | Reason: `No Fit` (`100000000`) | Platform Admin |
| **Legacy Packaging Retro-Fit (No Budget)** | `ddbd9fba-cfe4-f111-84ea-002248db522e` | Inactive / `Disqualified` | Reason: `No Budget` (`100000001`) | Platform Admin |

---

## 8. Curated Opportunities (`vn_opportunity`), Lines (`vn_opportunityline`) & BPF Instances — 13 Deals · 12 Lines · 13 BPFs

Every Opportunity below has an Active unique `vn_originatinglead` link and strictly **`1` synchronized `vn_opportunitysalesprocess` BPF instance**.

| Deal Name & GUID | Owner (BU) | Account / Contact | Stage / Status / Prob | Lines · Total / MaxDisc / ClosedAmt | BPF Instance (`1:1`) | Key M0–M3 Scenario |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Byblos Plant Automation Phase 1**<br>`5c42c40c-0fe8-f111-84ea-002248db522e` | `Rep A1`<br>(`Sales A`) | Byblos Automation / Hadi Nassar | `Qualify` / Open / `10%` | `0` · `$0.00` / `0.00%` / *(null)* | Active / `Qualify`<br>`5e42c40c-...` | At-Risk `V08` (`NextFU = 2026-11-22`); blank Decision-Maker (`[CRM007]`) |
| **Cedar Sensor Calibration Rollout**<br>`630dde12-0fe8-f111-84ea-002248db522e` | `Rep A1`<br>(`Sales A`) | Cedar Mfg / Karim Haddad | `Develop` / Open / `30%` | `1` (`3×PRD-1001 @ 10%`)<br>**`$270.02`** / **`10.00%`** / *(null)* | Active / `Develop`<br>`650dde12-...` | Canonical `BR17` rounding & exact `10.00%` **non-trigger** |
| **Byblos Annual Support Renewal**<br>`700dde12-0fe8-f111-84ea-002248db522e` | `Rep A1`<br>(`Sales A`) | Byblos Automation / Layla Mansour | `Close` / **Won** / `100%` | `1` (`2×PRD-3001 @ 0%`)<br>`$9,001.00` / `0.00%` / **`$9,001.00`** | Inactive / **`Finished`**<br>`710dde12-...` | `Manager A` `EXT10` Path 2 (`BR05`) + live browser `Close as Won` (`2026-11-24`) |
| **Cedar Enterprise Gateway Upgrade**<br>`70d15019-0fe8-f111-84ea-002248db522e` | `Rep A1`<br>(`Sales A`) | Cedar Mfg / Karim Haddad | `Close` / Open / `90%` | `1` (`4×PRD-2001 @ 0%`)<br>**`$50,000.00`** / `0.00%` / *(null)* | Active / `Close`<br>`72d15019-...` | Exact **`$50,000.00`** `BR20` amount trigger (`[CRM012]` in M3; ready for M5 `EXT13`) |
| **Cedar Plant 2 Telemetry Expansion**<br>`7ed15019-0fe8-f111-84ea-002248db522e` | `Rep A2`<br>(`Sales A`) | Cedar Mfg / Karim Haddad | `Close` / Open / `90%` | `2` (`PRD-3001 @ 5%` + `PRD-1001 @ 10.01%`)<br>**`$4,365.47`** / **`10.01%`** / *(null)* | Active / `Close`<br>`80d15019-...` | Discount-only `10.01%` `BR20` trigger; `C04` 2nd Sales A rep; `SR05` peer isolation |
| **Alpine Fleet Gateway Expansion**<br>`fc1f721f-0fe8-f111-84ea-002248db522e` | `Rep B1`<br>(`Sales B`) | Alpine Logistics / Elena Meyer | `Propose` / Open / `60%` | `2` (`PRD-2001 @ 0%` + `PRD-3001 @ 5%`)<br>**`$16,775.47`** / `5.00%` / *(null)* | Active / `Propose`<br>`fe1f721f-...` | `Sales B` territory pipeline isolation + At-Risk (`NextFU = 2026-11-18`) |
| **Phoenicia Robotics Telemetry Starter**<br>`93ac74dd-11e8-f111-84ea-002248db522e` | Platform Admin<br>(`org3b306249`) | Phoenicia Robotics / Nadim Khoury | `Develop` / Open / `30%` | `0` · `$0.00` / `0.00%` / *(null)* | Active / `Develop`<br>`94ac74dd-...` | `EXT10` Path 3 + `F-02` Admin auto-BPF proof + live browser Advance to Develop |
| **Cedar Quality Control SLA Package**<br>`98ac74dd-11e8-f111-84ea-002248db522e` | Platform Admin<br>(`org3b306249`) | Cedar Mfg / Karim Haddad | `Propose` / Open / `60%` | `1` (`1×PRD-3001 @ 0%`)<br>`$4,500.50` / `0.00%` / *(null)* | Active / `Propose`<br>`9aac74dd-...` | Admin `C03` chart bar + At-Risk `V08` (`NextFU = 2026-11-23`) |
| **Byblos Telemetry Pilot (Closed Won)**<br>`0bad74dd-11e8-f111-84ea-002248db522e` | `Rep A1`<br>(`Sales A`) | Byblos Automation / Hadi Nassar | `Close` / **Won** / `100%` | `1` (`2×PRD-3001 @ 0%`)<br>`$9,001.00` / `0.00%` / **`$9,001.00`** | Inactive / **`Finished`**<br>`1cad74dd-...` | `V09`/`C06` Won; frozen `ClosedAmount`; `BR24`/`BR29` post-close Task immutability |
| **Alpine Warehouse Sensor Bid (Closed Lost)**<br>`c84e6de3-11e8-f111-84ea-002248db522e` | `Rep B1`<br>(`Sales B`) | Alpine Logistics / Elena Meyer | `Develop` / **Lost** / `0%` | `1` (`1×PRD-2001 @ 0%`)<br>**`$12,500.00`** / `0.00%` / **`$0.00`** | Inactive / **`Aborted`**<br>`ca4e6de3-...` | `V09`/`C06` Lost (`Competitor`); proposed total preserved (`BR23`) |
| **Annual Fleet Analytics Expansion**<br>`6a9666a2-17e8-f111-84ea-002248db522e` | Platform Admin<br>(`org3b306249`) | Cedar Mfg / Rami Khoury | `Qualify` / Open / `10%` | `0` · `$0.00` / `0.00%` / *(null)* | Active / `Qualify`<br>`6b9666a2-...` | Live browser `EXT10` Path 2 qualification + At-Risk (`NextFU = 2026-11-19`) |
| **Byblos Plant 2 Gateway Package**<br>`0211ea11-1ee8-f111-84ea-002248db522e` | `Rep A1`<br>(`Sales A`) | Byblos Automation / Hadi Nassar | `Close` / Open / `90%` | `1` (`1×PRD-2001 @ 10%`)<br>**`$11,250.00`** / **`10.00%`** / *(null)* | Active / `Close`<br>`0411ea11-...` | Live Demo Pack: last-line `[CRM007]` & exact `10.00%` non-trigger `Close as Won` |
| **Cedar Warehouse Pilot Evaluation**<br>`1011ea11-1ee8-f111-84ea-002248db522e` | Platform Admin<br>(`org3b306249`) | Cedar Mfg / Karim Haddad | `Develop` / Open / `30%` | `1` (`1×PRD-3001 @ 0%`)<br>`$4,500.50` / `0.00%` / *(null)* | Active / `Develop`<br>`1211ea11-...` | Live Demo Pack: `Close as Lost` from `Develop` + Admin `C03` `Develop` bar |

---

## 9. Curated Native Activities (`task` & `phonecall`) — 14 Records (`8` Tasks · `6` Phone Calls)

### 9.1 Tasks (`task`) — 8 Curated Records

| Subject | `activityid` (GUID) | State / Status | Priority | Scheduled End (UTC) | Regarding Record | `vn_automationkey` | Owner |
| :--- | :--- | :--- | :---: | :---: | :--- | :---: | :--- |
| **Prepare discovery checklist for Cedar Manufacturing** | `ecb2c6e9-cfe4-f111-84ea-002248db522e` | Open / Not Started | High | `2026-11-20T20:30:00Z` | Lead: *Annual Fleet Analytics Expansion* | *(null)* | Platform Admin |
| **Initial prospect research completed** | `241f492c-d0e4-f111-84ea-002248db522e` | **Completed** / Completed | Normal | `2026-11-19T10:00:00Z` | Lead: *Annual Fleet Analytics Expansion* | *(null)* | Platform Admin |
| **Send pilot sensor spec sheet to Phoenicia Robotics** | `d14e6de3-11e8-f111-84ea-002248db522e` | Open / Not Started | Normal | `2026-11-24T21:00:00Z` | Opp: *Phoenicia Robotics Telemetry Starter* | *(null)* | Platform Admin |
| **Schedule post-win onboarding kickoff with Byblos** | `322e68e9-11e8-f111-84ea-002248db522e` | Open / Not Started | Normal | `2026-11-24T21:00:00Z` | Opp: *Byblos Telemetry Pilot (Closed Won)* | *(null)* | `Rep A1` |
| **Follow up on overdue plant telemetry specs** | `342e68e9-11e8-f111-84ea-002248db522e` | Open / Not Started | High | `2026-11-22T14:00:00Z` | Opp: *Byblos Plant Automation Phase 1* | *(null)* | `Rep A1` |
| **Prepare revised Alpine warehouse quote (Canceled after Lost)** | `3a2e68e9-11e8-f111-84ea-002248db522e` | **Canceled** / Canceled | Normal | `2026-11-26T12:00:00Z` | Opp: *Alpine Warehouse Sensor Bid (Closed Lost)* | *(null)* | `Rep B1` |
| **Review Cedar Plant 3 qualification scope before call** | `d81a5f5c-4fea-f111-84ea-002248db522e` | Open / Not Started | High | `2026-11-27T17:00:00Z` | Lead: *Cedar Plant 3 Sensor Modernization* *(`V10` Due Today)* | *(null)* | Platform Admin |
| **Prepare final delivery checklist for Byblos Plant 2** | `ab9e5762-4fea-f111-84ea-002248db522e` | Open / Not Started | High | `2026-11-27T17:30:00Z` | Opp: *Byblos Plant 2 Gateway Package* *(`V10` Due Today)* | *(null)* | `Rep A1` |

### 9.2 Phone Calls (`phonecall`) — 6 Curated Records

| Subject | `activityid` (GUID) | State / Status | Direction | Phone | Scheduled End (UTC) | Regarding Record | Owner |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- |
| **Follow-up call on Cedar Manufacturing profile details** | `587afc0f-d0e4-f111-84ea-002248db522e` | Open / Open | Outgoing | `+961-1-5550100` | `2026-11-18T12:00:00Z` | Contact: *Karim Haddad* | Platform Admin |
| **Discovery call with Rami Khoury (Unresolved Lead Phone)** | `d34e6de3-11e8-f111-84ea-002248db522e` | Open / Open | Outgoing | `+961-71-5550102` | `2026-11-24T20:00:00Z` | Lead: *Annual Fleet Analytics Expansion* *(unresolved party)* | Platform Admin |
| **Confirm calibration schedule with Karim Haddad** | `362e68e9-11e8-f111-84ea-002248db522e` | Open / Open | Outgoing | `+1-555-0101` | `2026-11-25T14:00:00Z` | Opp: *Cedar Sensor Calibration Rollout* | `Rep A1` |
| **Loss debrief call with Elena Meyer** | `3d2e68e9-11e8-f111-84ea-002248db522e` | **Completed** / Made | Outgoing | `+1-555-0201` | `2026-11-24T12:00:00Z` | Opp: *Alpine Warehouse Sensor Bid (Closed Lost)* | `Rep B1` |
| **Confirm SLA timeline with Karim Haddad** | `da1a5f5c-4fea-f111-84ea-002248db522e` | Open / Open | Outgoing | `+1-555-0101` | `2026-11-27T16:30:00Z` | Opp: *Cedar Quality Control SLA Package* *(`V10` Due Today)* | Platform Admin |
| **Call Hadi Nassar to confirm gateway installation window** | `ad9e5762-4fea-f111-84ea-002248db522e` | Open / Open | Outgoing | `+961-3-5550202` | `2026-11-27T18:00:00Z` | Opp: *Byblos Plant 2 Gateway Package* *(`V10` Due Today)* | `Rep A1` |

---

## 10. Empty M5-Locked Tables & Automated M3 Test Evidence Archive

### 10.1 Empty Tables Verified Live (`Count = 0`)

| Entity Set | Table Display Name | Live Count | Architectural Rule |
| :--- | :--- | :---: | :--- |
| `vn_approvalrequests` | Approval Request | **`0`** | Created exclusively via `EXT13 vn_ManageDealApproval` & `WF04` starting in **M5** (`§8.4`) |
| `annotations` | Notes | **`0`** | Clean baseline; no stray test notes |

### 10.2 Automated M3 Verification Archive (`[M3-E2E*]`, `[M3-AUDIT*]`, `[M3-STEP17*]`)

Because `PL01`–`PL03` intentionally prohibit `DELETE` on Leads, Opportunities, referenced customers, and completed/keyed activities (never disabled or bypassed), automated M3 test records from Steps 16–17 remain transparently archived in the dev environment (`M8` §8.9 executes a clean target import & fresh re-seed):
- **Products:** `13` `M3E2E-PRD-R1..R13` records — all `13/13` **Inactive** (hidden from `V16` & `vn_dlg_line.html`).
- **Accounts / Contacts:** `25` test Accounts & `13` test Contacts **Inactive**; `4` Accounts & `4` Contacts lawfully retained Active by `PL01` `[CRM017]` because they back archived open test deals.
- **Transactional Test Evidence:** `33` `[M3-*]` Leads, `11` `[M3-*]` Opportunities, `8` Lines, `11` BPFs, `3` Tasks, `1` Phone Call — all clearly prefixed `[M3-*]` and owned exclusively by test runs.

---

## 11. Solution Environment Variables & Base Currency (`§1.1`, `§6.1`, `§8.1`)

| Schema Name | Data Type | Default / Current Value | Specification Reference |
| :--- | :---: | :---: | :--- |
| **`vn_ApprovalAmount`** | Decimal | Default `50000` | Deal `Total >= $50,000` (USD) requires Manager approval (`BR20`) |
| **`vn_ApprovalDiscount`** | Decimal | Default `10` | Line discount `> 10.00%` requires Manager approval (`BR20`) |
| **`vn_MaxDiscount`** | Decimal | Default `30` | Maximum allowed line discount `30.00%` (`§1.1` / `DM07` / `EXT12`) |
| **`vn_StaleDays`** | Decimal | Default `14` | UTC days without progress before Open deal is flagged Stale (`BR30` / `V08`) |
| **`vn_EscalationDays`** | Decimal | Default `7` | UTC days overdue before open work escalates to Manager (`WF02`) |
| **`vn_InternalWriterUserId`** | String | Current `f3f85cec-2de7-f111-84ea-002248db522e` | `SR03` Scoped Internal Writer Dataverse `systemuserid` (`§6.1`/`§6.2`; per-environment value) |
| **Base Currency** | Lookup | **`USD`** (`US Dollar`, `c6c8babe-05ca-f111-a05a-7ced8d5f8fd9`) | Precision `2` standard; Precision `4` on `vn_product.vn_unitprice` and `vn_opportunityline.vn_unitprice` |
