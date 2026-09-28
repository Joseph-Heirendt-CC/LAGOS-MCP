# LAGOS-MCP Tools Reference

All tools are exposed via the `StoreVisitWorkflow` MCP server (Azure Functions, .NET 8).

Conventions:
- IDs (`doorId`, `contactId`, `recordId`, …) are numeric NetSuite internal IDs passed as strings.
- Boolean parameters accept `true` / `false` (legacy `T` / `F` also accepted) and are written to NetSuite as JSON booleans.
- SuiteQL returns column names in lowercase (e.g. `companyname`, `brandambassador`), regardless of the alias casing in the query.
- Write tools only send the fields you supply; omitted parameters leave the NetSuite value unchanged.

| Tool | File | Type |
|---|---|---|
| [lookup_door](#lookup_door) | `DoorTools.cs` | Read |
| [lookup_door_for_ui_selection](#lookup_door_for_ui_selection) | `DoorTools.cs` | Read |
| [lookup_brand_ambassador](#lookup_brand_ambassador) | `DoorTools.cs` | Read |
| [lookup_project](#lookup_project) | `DoorTools.cs` | Read |
| [get_door_contacts](#get_door_contacts) | `DoorTools.cs` | Read |
| [get_open_tasks_for_door](#get_open_tasks_for_door) | `DoorTools.cs` | Read |
| [get_tasks_for_store_visits](#get_tasks_for_store_visits) | `DoorTools.cs` | Read |
| [get_event_and_training_history](#get_event_and_training_history) | `DoorTools.cs` | Read |
| [get_other_lines_options](#get_other_lines_options) | `DoorTools.cs` | Read |
| [update_customer](#update_customer) | `DoorTools.cs` | Write |
| [get_area_of_responsibility_options](#get_area_of_responsibility_options) | `ContactTools.cs` | Read |
| [create_contact](#create_contact) | `ContactTools.cs` | Write |
| [update_contact](#update_contact) | `ContactTools.cs` | Write |
| [create_store_visit](#create_store_visit) | `StoreVisitTools.cs` | Write |
| [update_store_visit](#update_store_visit) | `StoreVisitTools.cs` | Write |
| [get_recent_store_visits](#get_recent_store_visits) | `StoreVisitTools.cs` | Read |

---

## lookup_door

**Purpose:** Resolve a Door (retail account / company-type Customer) by name, BA assignment, city, or state. Returns up to 100 matches.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `name` | string | At least one required | Partial company name match |
| `baId` | string | At least one required | Brand Ambassador employee internal ID (numeric) |
| `city` | string | At least one required | Partial city match against the Door's **default shipping** address |
| `state` | string | At least one required | Exact two-letter state abbreviation against the Door's **default shipping** address |

### NetSuite Tables
| Table | Alias | Join Condition |
|---|---|---|
| `customer` | `c` | Primary |
| `customlist_cca_door_type` | `dt` | `dt.id = c.custentity_cca_door_type` |
| `customlist_cca_ba_territory` | `t` | `t.id = c.custentity_cca_territory` |
| `addressbookaddress` | `aba` | `EXISTS` subquery on `aba.entity = c.id AND aba.defaultshipping = 'T'` — only when `city` or `state` supplied |

### Filter Fields
| Field | Condition |
|---|---|
| `c.custentity_cca_door` | `= 'T'` (Doors only) |
| `c.isinactive` | `= 'F'` (active only) |
| `c.custentity_cca_brand_ambassador` | `= baId` (when supplied) |
| `c.companyname` | `LIKE '%name%'` (when supplied) |
| `aba.city` / `aba.state` | Default shipping address matches `LIKE '%city%'` and/or `= state` (when supplied) |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `c.id` | `id` |
| `c.entityid` | `entityid` |
| `c.companyname` | `companyname` |
| `c.custentity_cca_brand_ambassador` | `brandambassador` (display value) |
| `c.salesrep` | `wholesalebrandmanager` (display value) |
| `c.custentity_cca_planner` | `planner` (display value) |
| `c.subsidiary` | `subsidiary` (display value) |
| `c.custentity_cca_other_lines_carried` | `other_lines_carried` — array of display names (multi-select, `customlist_cca_other_lines`); `[]` when empty |
| `c.custentity_cca_visit_hours` | `custentity_cca_visit_hours` |
| `c.custentity_cca_door_number` | `custentity_cca_door_number` |
| `c.custentity_cca_sharepoint_url` | `custentity_cca_sharepoint_url` |
| `dt.name` | `door_type` |
| `t.name` | `territory` |

---

## lookup_door_for_ui_selection

**Purpose:** Same search as `lookup_door`, formatted for a Copilot Studio adaptive-card choice set.

### Input Parameters
Same as [lookup_door](#lookup_door) (`name`, `baId`, `city`, `state` — at least one required).

### Returns
```json
{ "choices": [ { "title": "<company name>", "value": "<door id>" } ] }
```

---

## lookup_brand_ambassador

**Purpose:** Resolve a Brand Ambassador (NetSuite employee) by name or email to get their internal ID for use in `create_store_visit`. Returns up to 25 matches.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `name` | string | At least one required | Partial first or last name match |
| `email` | string | At least one required | Partial email address match |

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
| `e.entityid` | `entityid` |
| `e.firstname` | `firstname` |
| `e.lastname` | `lastname` |
| `e.email` | `email` |
| `customlist_cca_ba_territory.name` | `ba_territory` |

---

## lookup_project

**Purpose:** Find Project (Job) records linked to a Brand Ambassador or a Door. Returns up to 50 matches.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `baId` | string | At least one required | Brand Ambassador employee internal ID |
| `doorId` | string | At least one required | Customer internal ID from `lookup_door` |

### Filter Fields
| Field | Condition |
|---|---|
| `j.custentity_cca_brand_ambassador` | `= baId` (when supplied) |
| `j.custentity_cca_project_door` | `= doorId` (when supplied) |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `j.id` | `id` |
| `j.entityid` | `entityid` |
| `j.entitystatus` | `status` (display value) |
| `j.jobname` | `projectname` |
| `j.parent` | `customername` (display value) |
| `employee.entityid` (via `j.projectmanager`) | `project_manager` |
| `employee.entityid` (via `j.custentity_cca_brand_ambassador`) | `brand_ambassador` |
| `j.custentity_cca_special_requests` | `custentity_cca_special_requests` |

---

## get_door_contacts

**Purpose:** Return all active contacts linked to a Door (store managers, sales associates, etc.). Returns up to 100.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |

### NetSuite Tables
| Table | Alias | Join Condition |
|---|---|---|
| `contact` | `con` | Primary |
| `customlist_cca_contact_type_list` | `ct` | `ct.id = con.custentity_cca_contact_type` |

### Filter Fields
| Field | Condition |
|---|---|
| `con.company` | `= doorId` |
| `con.isinactive` | `= 'F'` (active only) |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `con.id` | `id` |
| `con.firstname` | `firstname` |
| `con.lastname` | `lastname` |
| `con.email` | `email` |
| `con.phone` | `phone` |
| `con.title` | `title` |
| `ct.name` | `contact_type` |
| `con.custentity_cca_area_of_resp` | `area_of_responsibility` — array of display names (multi-select, "Area of Responsibility List"); `[]` when empty |

---

## get_open_tasks_for_door

**Purpose:** Return open escalation tasks associated with a Door (excludes completed tasks), ordered by due date.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `limit` | integer | No | Max records (default 25, max 100) |

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
| `t.startdate` | `startdate` |
| `t.duedate` | `duedate` |
| `t.assigned` | `assignedto` (display value) |
| `t.message` | `message` |

---

## get_tasks_for_store_visits

**Purpose:** Return Tasks linked to 1–3 recent Store Visits so the BA can review unresolved escalations from prior visits.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `visitId1` | string | Yes | Most recent Store Visit ID from `get_recent_store_visits` |
| `visitId2` | string | No | 2nd most recent visit |
| `visitId3` | string | No | 3rd most recent visit |

### Filter Fields
| Field | Condition |
|---|---|
| `t.custevent_cca_related_store_visit` | `IN (visitId1, visitId2, visitId3)` |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `t.id` | `id` |
| `t.title` | `title` |
| `employee.entityid` (via `t.assigned`) | `assigned_to` |
| `t.priority` | `priority` |
| `t.status` | `status` (`Completed` / `In Progress` / `Not Started`) |
| `t.startdate` | `startdate` |
| `t.completeddate` | `completeddate` |
| `customrecord_cca_store_visit.name` | `related_store_visit` |
| `customlist_cca_sv_escal_type.name` (via `t.custevent_cca_sv_escal_type`) | `escalation_type` |
| `customer.companyname` (via `t.custevent_cca_door`) | `door` |

---

## get_event_and_training_history

**Purpose:** Retrieve Event (Project/Job) records and Training Recap records for a Door to support pre-visit preparation. Returns `{ events, trainingRecaps, totalEvents, totalTrainingRecaps }`, plus `warning` if the Training Recap query fails.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `startDate` | string | No | Start date filter (MM/DD/YYYY) |
| `endDate` | string | No | End date filter (MM/DD/YYYY) |
| `limit` | integer | No | Max records per type (default 20, max 50) |

### Events — `job` (`j`)
| Filter | Condition |
|---|---|
| `j.customer` | `= doorId` |
| `j.startdate` | `>= startDate` (when supplied) |
| `j.enddate` | `<= endDate` (when supplied) |

Returns `id`, `title` (`j.jobname`), `startdate`, `enddate`, `status` (display value), `memo`.

### Training Recaps — `customrecord_cca_training_recap` (`tr`)
| Filter | Condition |
|---|---|
| `tr.custrecord_cca_tr_door` | `= doorId` |
| `tr.custrecord_cca_tr_date` | `>= startDate` / `<= endDate` (when supplied) |

Returns `id`, `title` (`tr.name`), `trainingdate`, `brandambassador` (display value), `notes`.

---

## get_other_lines_options

**File:** `DoorTools.cs`
**Purpose:** Return the valid Other Lines Carried values so the agent can present a pick list before `update_customer`. No parameters. Queried live on every call.

Returns `id` and `name` for each active value in `customlist_cca_other_lines`, ordered by `id`.

---

## update_customer

**File:** `DoorTools.cs`
**Purpose:** Update a Door (Customer) record's pad counts, linear feet, and other lines carried. Only supplied fields are changed. These fields are tracked independently of the Store Visit pad counts — `update_store_visit` does not write to the Door.

### NetSuite Record Updated
`customer`

| Parameter | Type | Required | NetSuite Field |
|---|---|---|---|
| `doorId` | string | Yes | — (Customer internal ID from `lookup_door`) |
| `numbPadsWomen` | integer | No | `custentity_cca_womens_pads` |
| `goldPads` | integer | No | `custentity_cca_gold_pads` |
| `numbPadsMens` | integer | No | `custentity_cca_lagos_mens_pads` |
| `linearFeet` | decimal (string input) | No | `custentity_cca_linear_feet` |
| `otherLinesCarried` | string | No | `custentity_cca_other_lines_carried` — comma-separated names from `customlist_cca_other_lines`, resolved to internal IDs; **replaces** the full selection (include existing values from `lookup_door` to keep them; empty string clears). An unknown name returns the list of valid values. |

Not writable: `custentity_cca_total_in_case_pads` (calculated).

---

## get_area_of_responsibility_options

**File:** `ContactTools.cs`
**Purpose:** Return the valid Area of Responsibility values so the agent can present a pick list before `create_contact` / `update_contact`. No parameters. Queried live on every call.

Returns `id` and `name` for each active value in the Area of Responsibility custom list, ordered by `id`.

---

## create_contact

**File:** `ContactTools.cs`
**Purpose:** Create a new Contact linked to a Door.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `firstName` | string | Yes | |
| `lastName` | string | Yes | |
| `email` | string | No | |
| `phone` | string | No | Main phone |
| `title` | string | No | Job title |
| `areaOfResponsibility` | string | No | Comma-separated display names from the Area of Responsibility custom list, resolved to internal IDs at call time. Multi-select — written as the complete selection. An unknown name returns the list of valid values. |

### Behavior
- Verifies `doorId` is an active Door (`custentity_cca_door = 'T'`) and reads its subsidiary.
- **Subsidiary is set automatically from the Door** — NetSuite requires a contact's subsidiary to match its company's.
- If `email` is supplied and an active contact on the same Door already has that email, no record is created and the existing contact's id is returned in the error — use `update_contact`.

### NetSuite Record Written
`contact`

| NetSuite Field | Value Source |
|---|---|
| `firstName` | `firstName` |
| `lastName` | `lastName` |
| `company` | `{ id: doorId }` |
| `subsidiary` | `{ id: <Door's subsidiary> }` |
| `email` | `email` |
| `phone` | `phone` |
| `title` | `title` |
| `custentity_cca_area_of_resp` | `{ items: [{ id }, …] }` (resolved areaOfResponsibility) |

---

## update_contact

**File:** `ContactTools.cs`
**Purpose:** Update an existing Contact. Only supplied fields are changed.

### Input Parameters
| Parameter | Type | Required | NetSuite Field |
|---|---|---|---|
| `contactId` | string | Yes | — (record ID from `get_door_contacts` or `create_contact`) |
| `firstName` | string | No | `firstName` |
| `lastName` | string | No | `lastName` |
| `email` | string | No | `email` |
| `phone` | string | No | `phone` |
| `title` | string | No | `title` |
| `contactType` | string | No | `custentity_cca_contact_type` (resolved by name against `customlist_cca_contact_type_list`) |
| `areaOfResponsibility` | string | No | `custentity_cca_area_of_resp` — comma-separated names; **replaces** the full selection (include existing values from `get_door_contacts` to keep them; empty string clears) |
| `isInactive` | boolean | No | `isInactive` — `true` deactivates a contact who has left the store |

---

## create_store_visit

**Purpose:** Create a new Store Visit record as a skeleton at the start of a visit. Returns the new record's `id` for use with `update_store_visit`.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `brandAmbassadorId` | string | Yes | Employee internal ID from `lookup_brand_ambassador` |
| `visitDate` | string | Yes | Any recognizable date format (e.g. `June 2nd 2026`, `6/2/26`) — normalized to `YYYY-MM-DD` server-side |
| `name` | string | Yes | Visit record title — Door name + `-Visit ` + visit date (e.g. `Charlotte-5-Visit 6/30/2026`) |

### NetSuite Record Written
`customrecord_cca_store_visit`

| NetSuite Field | Value Source |
|---|---|
| `name` | `name` |
| `custrecord_cca_sv_door` | `{ id: doorId }` |
| `custrecord_cca_sv_brand_ambassador` | `{ id: brandAmbassadorId }` |
| `custrecord_cca_sv_visit_date` | `visitDate` normalized to `YYYY-MM-DD` |

---

## update_store_visit

**Purpose:** Update an existing Store Visit record with checklist responses, issue flags, notes, corrective actions, and summary fields. Pass `recordId` plus only the fields to change; at least one field is required.

### NetSuite Record Updated
`customrecord_cca_store_visit`

| Parameter | Type | Required | NetSuite Field |
|---|---|---|---|
| `recordId` | string (numeric) | Yes | — (record ID from `create_store_visit` or `get_recent_store_visits`) |

### Audit Booleans
| Parameter | NetSuite Field |
|---|---|
| `backstockInvNotRep` | `custrecord_cca_sv_backstock_inv_not_rep` — **`true` = backstock NOT represented (issue found)**; the reverse of the old `backstockInvAudited` |
| `priceAudited` | `custrecord_cca_sv_price_audited` |
| `padProductAudit` | `custrecord_cca_sv_pad_product_audit` |
| `presElemAudit` | `custrecord_cca_sv_pres_elem_audit` |
| `fixtureLayoutAudit` | `custrecord_cca_sv_fixture_layout_audit` |
| `marketMaterialAudit` | `custrecord_cca_sv_market_material_audit` |
| `caselineFlowReviewed` | `custrecord_cca_sv_caseline_flow_reviewed` |
| `tarnishingCheck` | `custrecord_cca_sv_tarnishing_check` |
| `vitrineAudited` | `custrecord_cca_sv_vitrine_audited` |
| `salesFloorRepVerified` | `custrecord_cca_sv_sales_floor_rep_verif` |

### Issue Flags (booleans)
| Parameter | NetSuite Field |
|---|---|
| `backstockInvIssue` | `custrecord_cca_sv_backstock_inv_issue` |
| `priceIssue` | `custrecord_cca_sv_price_issue_id` |
| `padProdIssue` | `custrecord_cca_sv_pad_prod_issue` |
| `presElemIssue` | `custrecord_cca_sv_pres_elem_issue` |
| `fixtureLayoutIssue` | `custrecord_cca_sv_fixture_layout_issue` |
| `markMaterialIssue` | `custrecord_cca_sv_mark_material_issue` |
| `caselineFlowIssue` | `custrecord_cca_sv_caseline_flow_issue` |
| `tarnishIssue` | `custrecord_cca_sv_tarnish_issue` |
| `vitrineIssue` | `custrecord_cca_sv_vitrine_issue_identi` |
| `dsaUpdOppWin` | `custrecord_cca_sv_dsa_upd_opp_win` |
| `markOppIdentified` | `custrecord_cca_sv_mark_opp_identified` |
| `qualIssue` | `custrecord_cca_sv_qual_iss_id` |
| `prodTagsIssue` | `custrecord_cca_sv_prod_tags_issue` |
| `prodTagTucked` | `custrecord_cca_sv_prod_tag_tucked` |
| `trainingNeedsIdentified` | `custrecord_cca_sv_training_needs_id` |
| `spaceLocationMoved` | `custrecord_cca_sv_space_location_moved` |
| `incentiveRunning` | `custrecord_cca_sv_incentive_running` |
| `storeAwareIncentive` | `custrecord_cca_sv_store_aware_incentive` |
| `updCompLand` | `custrecord_cca_sv_upd_comp_land` |
| `collectionsSoldDown` | `custrecord_cca_sv_collect_sold_down` |
| `piecesReqRtvRefurb` | `custrecord_cca_sv_piece_req_rtv_refurb` |

### Summary Text
| Parameter | NetSuite Field |
|---|---|
| `immediateActions` | `custrecord_cca_sv_immediate_actions` |
| `nextVisitFocus` | `custrecord_cca_sv_next_visit_focus` |
| `visitSummary` | `custrecord_cca_sv_visit_summary` |
| `sharepointUrl` | `custrecord_cca_sv_sharepoint_url` |
| `teamMembersEngaged` | `custrecord_cca_sv_team_mem_engaged` |
| `miscNotes` | `custrecord_cca_sv_misc_notes` |

### Notes (text)
| Parameter | NetSuite Field |
|---|---|
| `backstockNotRepNotes` | `custrecord_cca_sv_backstock_not_rep_note` |
| `caselineFlowNotes` | `custrecord_cca_sv_caseline_flow_notes` |
| `competitorLandscapeNotes` | `custrecord_cca_sv_upd_comp_land_notes` |
| `compVisMerchNotes` | `custrecord_cca_sv_comp_vis_merch_notes` |
| `dsaNotes` | `custrecord_cca_sv_dsa_notes` |
| `fixtureLayoutNotes` | `custrecord_cca_sv_fixture_layout_notes` |
| `invProdRepNotes` | `custrecord_cca_sv_inv_prod_rep_notes` |
| `marketMaterialNotes` | `custrecord_cca_sv_mark_material_notes` |
| `marketingOpportunityNotes` | `custrecord_cca_sv_mark_opp_notes` |
| `padProductNotes` | `custrecord_cca_sv_pad_product_notes` |
| `piecesReqRtvRefurbNotes` | `custrecord_cca_sv_piece_rtv_refurb_notes` |
| `presElemNotes` | `custrecord_cca_pres_elem_notes` |
| `priceNotes` | `custrecord_cca_sv_price_notes` |
| `productTagNotes` | `custrecord_cca_sv_prod_tag_notes` |
| `qualityNotes` | `custrecord_cca_sv_quality_notes` |
| `spaceLocationNotes` | `custrecord_cca_sv_space_location_notes` |
| `tarnishingNotes` | `custrecord_cca_sv_tarnishing_notes` |
| `trainingNeedsNotes` | `custrecord_cca_sv_training_needs_notes` |
| `vitrineNotes` | `custrecord_cca_sv_vitrine_notes` |

### Corrective Actions (text)
| Parameter | NetSuite Field |
|---|---|
| `backstockInvCorrectiveActions` | `custrecord_cca_sv_backstock_inv_corr_act` |
| `caselineFlowCorrectiveActions` | `custrecord_cca_sv_case_flow_correct_act` |
| `collectionsSoldDownCorrAct` | `custrecord_cca_sv_collect_sold_down_corr` |
| `dsaCorrectiveActions` | `custrecord_cca_sv_dsa_correct_act` |
| `fixtureLayoutCorrectiveActions` | `custrecord_cca_sv_fix_layout_correct_act` |
| `marketMaterialCorrectiveActions` | `custrecord_cca_sv_mark_mater_correct_act` |
| `padProductCorrectiveActions` | `custrecord_cca_sv_pad_prod_correct_act` |
| `presElemCorrectiveActions` | `custrecord_cca_sv_pres_elem_correct_act` |
| `priceIssueCorrectiveActions` | `custrecord_cca_sv_price_issue_corr_act` |
| `productTagCorrectiveActions` | `custrecord_cca_sv_prod_tag_correct_act` |
| `qualityCorrectiveActions` | `custrecord_cca_sv_quality_correct_act` |
| `rtvRefurbCorrAct` | `custrecord_cca_sv_rtv_refurb_corr_act` |
| `spaceLocationCorrAct` | `custrecord_cca_sv_space_loc_corr_act` |
| `tarnishCorrectiveActions` | `custrecord_cca_sv_tarnish_correct_act` |
| `updCompLandCorrAct` | `custrecord_cca_sv_upd_comp_land_corr_act` — Free-Form Text, **max 300 characters** (rejected if longer) |
| `vitrineCorrectiveActions` | `custrecord_cca_sv_vitrine_correct_act` |

### Numeric
| Parameter | NetSuite Field |
|---|---|
| `caselineSpace` | `custrecord_cca_sv_caseline_space` |
| `goldPads` | `custrecord_cca_sv_gold_pads` |
| `numbPadsMens` | `custrecord_cca_sv_numb_pads_mens` |
| `numbPadsWomen` | `custrecord_cca_sv_numb_pads_women` |

Not writable: `custrecord_cca_sv_total_pads` (calculated). Removed: `totalGoldPads` (field retired).

Not yet exposed: the five escalation Task-link fields (`rtv_refurb_escal`, `collect(?)_sold_down_escal`, `upd_comp_land_escal`, `space_location_escal`, `dsa_upd_escal`). They are set by a NetSuite workflow and don't exist in Sandbox yet.

---

## get_recent_store_visits

**Purpose:** Retrieve the most recent Store Visit records for a Door, newest first. Used for the Pre-Visit Summary and to get a `recordId` for `update_store_visit`.

### Input Parameters
| Parameter | Type | Required | Notes |
|---|---|---|---|
| `doorId` | string | Yes | Customer internal ID from `lookup_door` |
| `limit` | integer | No | Max records (default 5, max 50) |

### Filter Fields
| Field | Condition |
|---|---|
| `sv.custrecord_cca_sv_door` | `= doorId` |

### Returned Fields
| NetSuite Field | Returned As |
|---|---|
| `sv.id`, `sv.name`, `sv.created`, `sv.lastmodified` | same name |
| `sv.custrecord_cca_sv_visit_type` | `visittype` (display value) |
| `sv.custrecord_cca_sv_brand_ambassador` | `brandambassador` (display value) |
| `sv.custrecord_cca_sv_visit_date`, `_project`, `_visit_summary`, `_immediate_actions`, `_next_visit_focus`, `_sharepoint_url`, `_total_pads`, `_team_mem_engaged`, `_misc_notes` | same name |
| All **Corrective Actions** fields listed under `update_store_visit` | same name |
| All **Notes** fields listed under `update_store_visit` except `dsa_notes` | same name |
