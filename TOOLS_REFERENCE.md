# LAGOS-MCP Tools Reference

All tools are exposed via the `StoreVisitWorkflow` MCP server (Azure Functions, .NET 8).

---

## lookup_door

**File:** `DoorTools.cs`  
**Purpose:** Resolve a Door (retail account / company-type Customer) by name, BA assignment, city, or state.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `name` | string | At least one required | Partial company name match |
| `baId` | string | At least one required | Brand Ambassador employee internal ID (numeric) |
| `city` | string | At least one required | Partial city match |
| `state` | string | At least one required | Exact two-letter state abbreviation |

### NetSuite Tables
| Table | Alias | Join Condition |
|---|---|---|
| `customer` | `c` | Primary |
| `addressbookaddress` | `aba` | `aba.entity = c.id AND aba.defaultbilling = 'T'` — only when `city` or `state` supplied |

### Filter Fields
| Field | Condition |
|---|---|
| `c.custentity_cca_door` | `= 'T'` (Doors only) |
| `c.isinactive` | `= 'F'` (active only) |
| `c.custentity_cca_brand_ambassador` | `= baId` (when supplied) |
| `c.companyname` | `LIKE '%name%'` (when supplied) |
| `aba.city` | `LIKE '%city%'` (when supplied) |
| `aba.state` | `= state` (when supplied) |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `c.id` | `id` |
| `c.entityid` | `entityId` |
| `c.companyname` | `companyName` |
| `c.custentity_cca_brand_ambassador` | `brandAmbassador` (display value) |
| `c.salesrep` | `wholesaleBrandManager` (display value) |
| `c.custentity_cca_planner` | `planner` (display value) |
| `c.subsidiary` | `subsidiary` (display value) |

---

## lookup_brand_ambassador

**File:** `DoorTools.cs`  
**Purpose:** Resolve a Brand Ambassador (NetSuite employee) by name or email to get their internal ID for use in `create_store_visit`.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `name` | string | At least one required | Partial first or last name match |
| `email` | string | At least one required | Partial email address match |

### NetSuite Tables
| Table | Alias |
|---|---|
| `employee` | `e` |

### Filter Fields
| Field | Condition |
|---|---|
| `e.isinactive` | `= 'F'` (active only) |
| `e.custentity_cca_is_brand_ambassador` | `= 'T'` (BAs only) |
| `e.firstname` / `e.lastname` | `LIKE '%name%'` (when `name` supplied) |
| `e.email` | `LIKE '%email%'` (when `email` supplied) |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `e.id` | `id` |
| `e.entityid` | `entityId` |
| `e.firstname` | `firstName` |
| `e.lastname` | `lastName` |
| `e.email` | `email` |

---

## get_door_contacts

**File:** `DoorTools.cs`  
**Purpose:** Return all active contacts linked to a Door (store managers, sales associates, etc.).

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |

### NetSuite Tables
| Table | Alias | Join Condition |
|---|---|---|
| `contact` | `c` | Primary |
| `customercontact` | `cc` | `cc.contact = c.id` |

### Filter Fields
| Field | Condition |
|---|---|
| `cc.entity` | `= doorId` |
| `c.isinactive` | `= 'F'` (active only) |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `c.id` | `id` |
| `c.firstname` | `firstName` |
| `c.lastname` | `lastName` |
| `c.email` | `email` |
| `c.phone` | `phone` |
| `c.title` | `title` |
| `cc.contactrole` | `role` (display value) |

---

## get_open_tasks_for_door

**File:** `DoorTools.cs`  
**Purpose:** Return open escalation tasks associated with a Door (excludes completed tasks).

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `limit` | integer | No | Max records (default 25, max 100) |

### NetSuite Tables
| Table | Alias |
|---|---|
| `task` | `t` |

### Filter Fields
| Field | Condition |
|---|---|
| `t.company` | `= doorId` |
| `t.status` | `!= 'COMPLETE'` |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `t.id` | `id` |
| `t.title` | `title` |
| `t.status` | `status` |
| `t.priority` | `priority` |
| `t.startdate` | `startDate` |
| `t.duedate` | `dueDate` |
| `t.assigned` | `assignedTo` (display value) |
| `t.message` | `message` |

---

## get_event_and_training_history

**File:** `DoorTools.cs`  
**Purpose:** Retrieve Event (Project/Job) records and Training Recap records for a Door to support pre-visit preparation. Returns two arrays: `events` and `trainingRecaps`.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `startDate` | string | No | Start date filter (MM/DD/YYYY) |
| `endDate` | string | No | End date filter (MM/DD/YYYY) |
| `limit` | integer | No | Max records per type (default 20, max 50) |

### NetSuite Tables — Events
| Table | Alias |
|---|---|
| `job` | `j` |

### Filter Fields — Events
| Field | Condition |
|---|---|
| `j.customer` | `= doorId` |
| `j.startdate` | `>= startDate` (when supplied) |
| `j.enddate` | `<= endDate` (when supplied) |

### Returned Fields — Events
| NetSuite Field | Returned As |
|---|---|
| `j.id` | `id` |
| `j.jobname` | `title` |
| `j.startdate` | `startDate` |
| `j.enddate` | `endDate` |
| `j.status` | `status` (display value) |
| `j.memo` | `memo` |

### NetSuite Tables — Training Recaps
| Table | Alias |
|---|---|
| `customrecord_cca_training_recap` | `tr` |

### Filter Fields — Training Recaps
| Field | Condition |
|---|---|
| `tr.custrecord_cca_tr_door` | `= doorId` |
| `tr.custrecord_cca_tr_date` | `>= startDate` / `<= endDate` (when supplied) |

### Returned Fields — Training Recaps
| NetSuite Field | Returned As |
|---|---|
| `tr.id` | `id` |
| `tr.name` | `title` |
| `tr.custrecord_cca_tr_date` | `trainingDate` |
| `tr.custrecord_cca_tr_ba` | `brandAmbassador` (display value) |
| `tr.custrecord_cca_tr_notes` | `notes` |

---

## create_store_visit

**File:** `StoreVisitTools.cs`  
**Purpose:** Create a new Store Visit record in NetSuite as a skeleton. Call at the start of a visit before capturing checklist data. Returns the new record's `id` for use with `update_store_visit`.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `brandAmbassadorId` | string | Yes | Employee internal ID from `lookup_brand_ambassador` |
| `visitDate` | string | Yes | Any recognizable date format — normalized to `YYYY-MM-DD` server-side |
| `name` | string | Yes | Visit record title — Door name + `-Visit ` + visit date (e.g. `Charlotte-5-Visit 6/30/2026`) |

### NetSuite Record Written
`customrecord_cca_store_visit`

### Fields Written
| NetSuite Field | Value Source |
|---|---|
| `name` | `name` parameter |
| `custrecord_cca_sv_door` | `{ id: doorId }` |
| `custrecord_cca_sv_brand_ambassador` | `{ id: brandAmbassadorId }` |
| `custrecord_cca_sv_visit_date` | `visitDate` normalized to `YYYY-MM-DD` |

### Returned Fields
| Field | Notes |
|---|---|
| `id` | Internal ID of the new store visit record |

---

## update_store_visit

**File:** `StoreVisitTools.cs`  
**Purpose:** Update an existing Store Visit record with checklist responses, issue flags, and summary fields.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `recordId` | string | Yes | Internal ID from `create_store_visit` or `get_recent_store_visits` |
| `fields` | object | Yes | Key-value pairs of fields to update |

### NetSuite Record Updated
`customrecord_cca_store_visit`

### Writable Fields — Audit Booleans (`'T'` / `'F'`)
| NetSuite Field | Description |
|---|---|
| `custrecord_cca_sv_backstock_inv_audited` | Backstock inventory audit completed |
| `custrecord_cca_sv_price_audited` | Price audit completed |
| `custrecord_cca_sv_pad_product_audit` | Pad product audit completed |
| `custrecord_cca_sv_pres_elem_audit` | Presentation elements audit completed |
| `custrecord_cca_sv_fixture_layout_audit` | Fixture layout audit completed |
| `custrecord_cca_sv_market_material_audit` | Marketing material audit completed |
| `custrecord_cca_sv_caseline_flow_reviewed` | Caseline flow reviewed |
| `custrecord_cca_sv_tarnishing_check` | Tarnishing check completed |
| `custrecord_cca_sv_vitrine_audited` | Vitrine audited |
| `custrecord_cca_sv_sales_floor_rep_verif` | Sales floor rep verified |

### Writable Fields — Issue Flags (`'T'` / `'F'`)
| NetSuite Field | Description |
|---|---|
| `custrecord_cca_sv_backstock_inv_issue` | Backstock inventory issue identified |
| `custrecord_cca_sv_price_issue_id` | Price issue identified |
| `custrecord_cca_sv_pad_prod_issue` | Pad product issue identified |
| `custrecord_cca_sv_pres_elem_issue` | Presentation elements issue identified |
| `custrecord_cca_sv_fixture_layout_issue` | Fixture layout issue identified |
| `custrecord_cca_sv_mark_material_issue` | Marketing material issue identified |
| `custrecord_cca_sv_caseline_flow_issue` | Caseline flow issue identified |
| `custrecord_cca_sv_tarnish_issue` | Tarnishing issue identified |
| `custrecord_cca_sv_vitrine_issue_identi` | Vitrine issue identified |
| `custrecord_cca_sv_dsa_issue_idenified` | DSA issue identified |
| `custrecord_cca_sv_mark_opp_identified` | Marketing opportunity identified |
| `custrecord_cca_sv_qual_iss_id` | Quality issue identified |
| `custrecord_cca_sv_prod_tags_issue` | Product tags issue identified |
| `custrecord_cca_sv_prod_tag_tucked` | Product tag tucked issue |
| `custrecord_cca_sv_training_needs_id` | Training needs identified |
| `custrecord_cca_sv_space_location_moved` | Space/location moved issue |
| `custrecord_cca_sv_incentive_running` | Incentive currently running |
| `custrecord_cca_sv_store_aware_incentive` | Store aware of incentive |
| `custrecord_cca_sv_competitor_incentives` | Competitor incentives present |

### Writable Fields — Text / Numeric
| NetSuite Field | Type | Description |
|---|---|---|
| `custrecord_cca_sv_immediate_actions` | string | Immediate actions taken during visit |
| `custrecord_cca_sv_next_visit_focus` | string | Focus areas for next visit |
| `custrecord_cca_sv_visit_summary` | string | Overall visit summary |
| `custrecord_cca_sv_dsa_notes` | string | DSA notes |
| `custrecord_cca_sv_caseline_space` | number | Caseline space count |
| `custrecord_cca_sv_gold_pads` | number | Gold pad count |
| `custrecord_cca_sv_numb_pads_mens` | number | Number of men's pads |
| `custrecord_cca_sv_numb_pads_women` | number | Number of women's pads |
| `custrecord_cca_sv_total_gold_pads` | number | Total gold pads |
| `custrecord_cca_sv_total_pads` | number | Total pads |

---

## get_recent_store_visits

**File:** `StoreVisitTools.cs`  
**Purpose:** Retrieve the most recent Store Visit records for a Door. Used for Pre-Visit Summary and to get a `recordId` for `update_store_visit`.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `limit` | integer | No | Max records (default 5, max 50) |

### NetSuite Tables
| Table | Alias |
|---|---|
| `customrecord_cca_store_visit` | `sv` |

### Filter Fields
| Field | Condition |
|---|---|
| `sv.custrecord_cca_sv_door` | `= doorId` |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `sv.id` | `id` |
| `sv.name` | `name` |
| `sv.custrecord_cca_sv_visit_date` | `custrecord_cca_sv_visit_date` |
| `sv.custrecord_cca_sv_visit_type` | `visitType` (display value) |
| `sv.custrecord_cca_sv_brand_ambassador` | `brandAmbassador` (display value) |
| `sv.custrecord_cca_sv_immediate_actions` | `custrecord_cca_sv_immediate_actions` |
| `sv.custrecord_cca_sv_next_visit_focus` | `custrecord_cca_sv_next_visit_focus` |
| `sv.custrecord_cca_sv_total_pads` | `custrecord_cca_sv_total_pads` |
| `sv.custrecord_cca_sv_total_gold_pads` | `custrecord_cca_sv_total_gold_pads` |
