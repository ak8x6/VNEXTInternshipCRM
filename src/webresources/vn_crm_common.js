/**
 * @file vn_crm_common.js
 * @description VNEXT CRM Common, Form Handlers & Supported Command Launchers (EXT05-EXT09).
 * Strictly uses supported client SDK APIs (formContext, Xrm.Navigation, Xrm.Utility, Web API v9.2).
 * Strictly NO internal UCI DOM manipulation, NO parent-frame navigation hacks, NO app-global interception.
 */

var VNEXTCRM = VNEXTCRM || {};

(function () {
    "use strict";

    var cachedBaseCurrencyCode = null;

    var ENTITY_SET_MAP = {
        "vn_lead": { set: "vn_leads", idCol: "vn_leadid" },
        "vn_opportunity": { set: "vn_opportunities", idCol: "vn_opportunityid" },
        "vn_opportunityline": { set: "vn_opportunitylines", idCol: "vn_opportunitylineid" },
        "vn_approvalrequest": { set: "vn_approvalrequests", idCol: "vn_approvalrequestid" }
    };

    /**
     * Resolves formContext from executionContext or Ribbon PrimaryControl.
     * @param {Object} context
     * @returns {Object|null}
     */
    function resolveFormContext(context) {
        if (!context) {
            return (typeof Xrm !== "undefined" && Xrm.Page) ? Xrm.Page : null;
        }
        if (typeof context.getFormContext === "function") {
            return context.getFormContext();
        }
        if (context.data && context.ui) {
            return context;
        }
        if (typeof Xrm !== "undefined" && Xrm.Page) {
            return Xrm.Page;
        }
        return null;
    }

    /**
     * Resolves client base URL via supported GlobalContext.
     * @returns {string}
     */
    function getClientUrl() {
        if (typeof Xrm !== "undefined" && Xrm.Utility && typeof Xrm.Utility.getGlobalContext === "function") {
            return Xrm.Utility.getGlobalContext().getClientUrl().replace(/\/$/, "");
        }
        if (typeof GetGlobalContext === "function") {
            var ctx = GetGlobalContext();
            if (ctx && typeof ctx.getClientUrl === "function") {
                return ctx.getClientUrl().replace(/\/$/, "");
            }
        }
        return "";
    }

    /**
     * Strips curly braces and lowercases a GUID string.
     * @param {string} rawId
     * @returns {string}
     */
    function cleanGuid(rawId) {
        if (!rawId) return "";
        return String(rawId).replace(/[{}]/g, "").trim().toLowerCase();
    }

    /**
     * Normalizes Dataverse weak ETag W/"123" to raw rowversion string "123".
     * @param {string} etag
     * @returns {string}
     */
    function normalizeEtag(etag) {
        if (!etag) return "";
        var str = String(etag).trim();
        var match = str.match(/^W\/"'?([^"']+)'?"$/i);
        if (match && match[1]) {
            return match[1].trim();
        }
        return str.replace(/^W\/"?'?/i, "").replace(/'?"?$/, "").trim();
    }

    /**
     * Performs a fresh no-cache Web API GET request.
     * @param {string} relativePath
     * @returns {Promise<Object>}
     */
    function fetchNoCache(relativePath) {
        var baseUrl = getClientUrl();
        var url = (baseUrl ? baseUrl : "") + "/api/data/v9.2/" + relativePath.replace(/^\//, "");
        return fetch(url, {
            method: "GET",
            cache: "no-store",
            headers: {
                "Accept": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
                "Cache-Control": "no-cache",
                "Pragma": "no-cache"
            }
        }).then(function (response) {
            if (!response.ok) {
                return response.text().then(function (txt) {
                    throw new Error("HTTP " + response.status + ": " + txt);
                });
            }
            return response.json();
        });
    }

    /**
     * BR12 Dirty-Form Guard: blocks launching modal dialogs when the form has unsaved edits.
     * @param {Object} formContext
     * @param {string} actionLabel
     * @returns {boolean} true if form is dirty and caller should abort launch
     */
    function guardDirtyForm(formContext, actionLabel) {
        if (formContext && formContext.data && formContext.data.entity
            && typeof formContext.data.entity.getIsDirty === "function"
            && formContext.data.entity.getIsDirty()) {
            Xrm.Navigation.openAlertDialog({
                text: "You have unsaved changes on this form. Please save your changes first before running " + actionLabel + " so the server validates your latest saved fields.",
                title: "Save Changes First"
            });
            return true;
        }
        return false;
    }

    /**
     * Safely reads and removes a one-time handoff value from sessionStorage.
     * @param {string} key
     * @returns {string|null}
     */
    function consumeSessionHandoff(key) {
        try {
            if (typeof window !== "undefined" && window.sessionStorage) {
                var val = window.sessionStorage.getItem(key);
                if (val) {
                    window.sessionStorage.removeItem(key);
                    return val;
                }
            }
        } catch (e) {
            console.warn("sessionStorage read skipped:", e);
        }
        return null;
    }

    // =========================================================================
    // 1. Shared Helper Utilities
    // =========================================================================

    /**
     * Displays a notification banner on the form header.
     * @param {Object} formContext
     * @param {string} message
     * @param {string} level - "INFO" | "WARNING" | "ERROR"
     * @param {string} uniqueId
     */
    VNEXTCRM.showNotification = function (formContext, message, level, uniqueId) {
        if (!formContext || !formContext.ui || typeof formContext.ui.setFormNotification !== "function") {
            return;
        }
        var validLevels = ["INFO", "WARNING", "ERROR"];
        var lvl = (level && typeof level === "string") ? level.toUpperCase() : "INFO";
        if (validLevels.indexOf(lvl) === -1) {
            lvl = "INFO";
        }
        var id = uniqueId || ("vn_note_" + Math.random().toString(36).substring(2, 9));
        formContext.ui.setFormNotification(message, lvl, id);
    };

    /**
     * Clears a notification banner from the form header.
     * @param {Object} formContext
     * @param {string} uniqueId
     */
    VNEXTCRM.clearNotification = function (formContext, uniqueId) {
        if (!formContext || !formContext.ui || typeof formContext.ui.clearFormNotification !== "function") {
            return;
        }
        if (uniqueId) {
            formContext.ui.clearFormNotification(uniqueId);
        }
    };

    /**
     * Fetches fresh live @odata.etag with Cache-Control: no-cache and normalizes W/"123" to "123".
     * @param {string} entityLogicalName
     * @param {string} recordId
     * @returns {Promise<string>}
     */
    VNEXTCRM.readRowVersion = function (entityLogicalName, recordId) {
        var cleanId = cleanGuid(recordId);
        if (!cleanId) {
            return Promise.reject(new Error("Record ID is required to read row version."));
        }
        var meta = ENTITY_SET_MAP[entityLogicalName];
        if (meta) {
            var path = meta.set + "(" + cleanId + ")?$select=" + meta.idCol + ",versionnumber";
            return fetchNoCache(path).then(function (result) {
                var norm = normalizeEtag(result["@odata.etag"]);
                if (norm) return norm;
                if (result.versionnumber !== undefined && result.versionnumber !== null) {
                    return String(result.versionnumber);
                }
                return "";
            });
        }
        return Xrm.WebApi.retrieveRecord(entityLogicalName, cleanId, "?$select=versionnumber").then(function (result) {
            var norm = normalizeEtag(result["@odata.etag"]);
            if (norm) return norm;
            if (result.versionnumber !== undefined && result.versionnumber !== null) {
                return String(result.versionnumber);
            }
            return "";
        });
    };

    /**
     * Dynamically resolves the environment base currency ISO code via organizations -> transactioncurrency.
     * Zero hardcoded currency codes or GUIDs.
     * @returns {Promise<string>}
     */
    VNEXTCRM.getBaseCurrencyCode = function () {
        if (cachedBaseCurrencyCode) {
            return Promise.resolve(cachedBaseCurrencyCode);
        }
        return fetchNoCache("organizations?$select=_basecurrencyid_value&$top=1").then(
            function (orgRes) {
                var org = (orgRes && orgRes.value && orgRes.value.length > 0) ? orgRes.value[0] : null;
                var baseId = org && org._basecurrencyid_value ? cleanGuid(org._basecurrencyid_value) : "";
                if (!baseId) {
                    return "";
                }
                return fetchNoCache("transactioncurrencies(" + baseId + ")?$select=isocurrencycode").then(
                    function (curr) {
                        if (curr && curr.isocurrencycode) {
                            cachedBaseCurrencyCode = curr.isocurrencycode;
                            return cachedBaseCurrencyCode;
                        }
                        return "";
                    },
                    function () { return ""; }
                );
            },
            function () {
                return fetchNoCache("transactioncurrencies?$select=isocurrencycode&$filter=statecode eq 0&$top=1").then(
                    function (res) {
                        if (res && res.value && res.value.length > 0 && res.value[0].isocurrencycode) {
                            cachedBaseCurrencyCode = res.value[0].isocurrencycode;
                            return cachedBaseCurrencyCode;
                        }
                        return "";
                    },
                    function () { return ""; }
                );
            }
        );
    };

    /**
     * Asynchronously refreshes form data without saving.
     * @param {Object} formContext
     * @returns {Promise}
     */
    VNEXTCRM.refreshForm = function (formContext) {
        if (formContext && formContext.data && typeof formContext.data.refresh === "function") {
            return formContext.data.refresh(false);
        }
        return Promise.resolve();
    };

    /**
     * Locks all editable controls across the form.
     * @param {Object} formContext
     */
    VNEXTCRM.lockAllControls = function (formContext) {
        if (!formContext || !formContext.ui || !formContext.ui.controls) {
            return;
        }
        formContext.ui.controls.forEach(function (control) {
            if (control && typeof control.setDisabled === "function") {
                control.setDisabled(true);
            }
        });
    };

    // =========================================================================
    // 2. Form Event Handlers (M2 Preserved)
    // =========================================================================

    VNEXTCRM.onLeadLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var stateAttr = formContext.getAttribute("statecode");
        var statecode = stateAttr ? stateAttr.getValue() : 0;
        if (statecode !== 0) {
            VNEXTCRM.lockAllControls(formContext);
            VNEXTCRM.showNotification(
                formContext,
                "This Lead is closed and its history is read-only.",
                "INFO",
                "VN_LEAD_CLOSED_READONLY"
            );
        }
    };

    VNEXTCRM.onContactLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var formType = (formContext.ui && typeof formContext.ui.getFormType === "function")
            ? formContext.ui.getFormType()
            : 0;

        if (formType !== 1) {
            var parentCustControl = formContext.getControl("parentcustomerid");
            if (parentCustControl && typeof parentCustControl.setDisabled === "function") {
                parentCustControl.setDisabled(true);
            }
        }

        VNEXTCRM.onContactDncChange(executionContext);
    };

    VNEXTCRM.onContactDncChange = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var dncNotificationId = "VN_CONTACT_DNC_OPT_OUT_WARNING";
        var dncAttr = formContext.getAttribute("vn_donotcontact");
        var isDnc = dncAttr ? (dncAttr.getValue() === true) : false;

        if (isDnc) {
            VNEXTCRM.showNotification(
                formContext,
                "DO NOT CONTACT: Customer has opted out of addressed outbound communications",
                "WARNING",
                dncNotificationId
            );
        } else {
            VNEXTCRM.clearNotification(formContext, dncNotificationId);
        }
    };

    VNEXTCRM.onOpportunityLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var serverOwnedFields = [
            "vn_stage",
            "vn_probability",
            "vn_total",
            "vn_maxdiscount",
            "vn_currentapproval",
            "vn_originatinglead",
            "vn_account",
            "vn_primarycontact",
            "vn_actualclosedate",
            "vn_closedamount",
            "vn_lossreason",
            "vn_closecomments",
            "vn_lastprogresson",
            "vn_stageenteredon",
            "ownerid"
        ];

        serverOwnedFields.forEach(function (fieldName) {
            var attr = (typeof formContext.getAttribute === "function") ? formContext.getAttribute(fieldName) : null;
            if (attr && attr.controls && typeof attr.controls.forEach === "function") {
                attr.controls.forEach(function (c) {
                    if (c && typeof c.setDisabled === "function") {
                        c.setDisabled(true);
                    }
                });
            }
            var ctrl = (typeof formContext.getControl === "function") ? formContext.getControl(fieldName) : null;
            if (ctrl && typeof ctrl.setDisabled === "function") {
                ctrl.setDisabled(true);
            }
        });

        var stateAttr = formContext.getAttribute("statecode");
        var statecode = stateAttr ? stateAttr.getValue() : 0;
        if (statecode !== 0) {
            VNEXTCRM.lockAllControls(formContext);
            var dmCtrl = formContext.getControl("vn_decisionmaker");
            if (dmCtrl && typeof dmCtrl.setDisabled === "function") {
                dmCtrl.setDisabled(true);
            }
            VNEXTCRM.showNotification(
                formContext,
                "This Opportunity is closed and read-only. Associated independent activities remain allowed.",
                "INFO",
                "VN_OPPORTUNITY_CLOSED_READONLY"
            );
        }

        if (formContext.data && formContext.data.process && typeof formContext.data.process.addOnPreStageChange === "function") {
            formContext.data.process.addOnPreStageChange(function (stageChangeArgs) {
                if (stageChangeArgs && typeof stageChangeArgs.getEventArgs === "function") {
                    var eventArgs = stageChangeArgs.getEventArgs();
                    if (eventArgs && typeof eventArgs.preventDefault === "function") {
                        eventArgs.preventDefault();
                    }
                }
                VNEXTCRM.showNotification(
                    formContext,
                    "Stage transitions are server-governed via the Advance Stage command.",
                    "INFO",
                    "VN_BPF_STAGE_GOVERNED"
                );
            });
        }
    };

    VNEXTCRM.onProductLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var formType = (formContext.ui && typeof formContext.ui.getFormType === "function")
            ? formContext.ui.getFormType()
            : 0;

        if (formType !== 1) {
            VNEXTCRM.showNotification(
                formContext,
                "Product SKU is protected once referenced by Opportunity Lines.",
                "INFO",
                "VN_PRODUCT_SKU_INFO"
            );
        }
    };

    VNEXTCRM.onActivityLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var stateAttr = formContext.getAttribute("statecode");
        var statecode = stateAttr ? stateAttr.getValue() : 0;
        if (statecode !== 0) {
            VNEXTCRM.lockAllControls(formContext);
            VNEXTCRM.showNotification(
                formContext,
                "This activity is closed and its history is read-only.",
                "INFO",
                "VN_ACTIVITY_CLOSED_READONLY"
            );
        }
    };

    VNEXTCRM.onReadOnlyLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        VNEXTCRM.lockAllControls(formContext);

        var entityName = (formContext.data && formContext.data.entity && typeof formContext.data.entity.getEntityName === "function")
            ? formContext.data.entity.getEntityName()
            : "";

        var message = "This record is read-only.";
        if (entityName === "vn_opportunityproduct" || entityName === "vn_opportunityline") {
            message = "Opportunity line items are managed through dedicated deal actions and are read-only on this form.";
        } else if (entityName === "vn_approvalrequest") {
            message = "Approval requests are governed by system approval workflows and are read-only on this form.";
        }

        VNEXTCRM.showNotification(
            formContext,
            message,
            "INFO",
            "VN_RECORD_READONLY_INFO"
        );
    };

    // =========================================================================
    // 3. Command Launchers (EXT05-EXT09 Wired to Live M3 APIs & PL02)
    // =========================================================================

    /**
     * Command: Mark Lead as Contacted (BR02).
     * @param {Object} primaryControl
     */
    VNEXTCRM.markLeadContacted = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var entityId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        if (!entityId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Lead before marking it as Contacted.",
                title: "Unsaved Record"
            });
            return;
        }

        var stateAttr = formContext.getAttribute("statecode");
        var statecode = stateAttr ? stateAttr.getValue() : 0;
        if (statecode !== 0) {
            Xrm.Navigation.openAlertDialog({
                text: "Only active leads can be marked as Contacted.",
                title: "Lead Not Active"
            });
            return;
        }

        var nextStepAttr = formContext.getAttribute("vn_nextstep");
        var followUpAttr = formContext.getAttribute("vn_nextfollowupdate");
        var nextStepVal = nextStepAttr ? nextStepAttr.getValue() : null;
        var followUpVal = followUpAttr ? followUpAttr.getValue() : null;

        var isNextStepBlank = !nextStepVal || (typeof nextStepVal === "string" && nextStepVal.trim() === "");
        var isFollowUpBlank = !followUpVal;

        if (isNextStepBlank) {
            Xrm.Navigation.openAlertDialog({
                text: "Cannot mark Lead as Contacted: Business Rule BR02 requires a nonblank Next Step before transitioning to Contacted status. Please enter a Next Step.",
                title: "BR02: Next Step Required"
            });
            return;
        }

        if (isFollowUpBlank) {
            Xrm.Navigation.openAlertDialog({
                text: "Cannot mark Lead as Contacted: A Next Follow-up Date is required. Please provide a Next Follow-up Date.",
                title: "Next Follow-up Date Required"
            });
            return;
        }

        var statusAttr = formContext.getAttribute("statuscode");
        if (statusAttr) {
            statusAttr.setValue(100000001);
            statusAttr.setSubmitMode("always");
        }

        formContext.data.save().then(
            function () {
                VNEXTCRM.showNotification(
                    formContext,
                    "Lead status successfully updated to Contacted.",
                    "INFO",
                    "VN_LEAD_MARKED_CONTACTED"
                );
            },
            function (error) {
                Xrm.Navigation.openErrorDialog({
                    message: (error && error.message) ? error.message : "Failed to update Lead status."
                });
            }
        );
    };

    /**
     * Command: Open Qualify Lead dialog (vn_dlg_qualify.html -> EXT10 vn_QualifyLead).
     * @param {Object} primaryControl
     */
    VNEXTCRM.openQualifyDialog = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var rawId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        var entityId = cleanGuid(rawId);
        if (!entityId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Lead before qualifying.",
                title: "Unsaved Record"
            });
            return;
        }

        if (guardDirtyForm(formContext, "Qualify Lead")) {
            return;
        }

        Promise.all([
            VNEXTCRM.readRowVersion("vn_lead", entityId).catch(function () { return ""; }),
            VNEXTCRM.getBaseCurrencyCode().catch(function () { return ""; })
        ]).then(function (meta) {
            var rowVersion = meta[0];
            var currencyCode = meta[1];
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_qualify.html",
                data: JSON.stringify({
                    recordId: entityId,
                    rowVersion: rowVersion || "",
                    currencyCode: currencyCode || ""
                })
            };
            var navigationOptions = {
                target: 2,
                width: { value: 720, unit: "px" },
                height: { value: 680, unit: "px" },
                position: 1,
                title: "Qualify Lead"
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    var openOppId = consumeSessionHandoff("vn_open_opp_" + entityId);
                    if (openOppId) {
                        Xrm.Navigation.openForm({
                            entityName: "vn_opportunity",
                            entityId: cleanGuid(openOppId)
                        });
                    } else {
                        VNEXTCRM.refreshForm(formContext);
                    }
                },
                function (navError) {
                    Xrm.Navigation.openErrorDialog({
                        message: (navError && navError.message) ? navError.message : "Could not open Qualify Lead dialog."
                    });
                }
            );
        });
    };

    /**
     * Command: Open Disqualify Lead dialog (vn_dlg_disqualify.html -> PL02 PATCH).
     * @param {Object} primaryControl
     */
    VNEXTCRM.openDisqualifyDialog = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var rawId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        var entityId = cleanGuid(rawId);
        if (!entityId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Lead before disqualifying.",
                title: "Unsaved Record"
            });
            return;
        }

        if (guardDirtyForm(formContext, "Disqualify Lead")) {
            return;
        }

        VNEXTCRM.readRowVersion("vn_lead", entityId).catch(function () { return ""; }).then(function (rowVersion) {
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_disqualify.html",
                data: JSON.stringify({
                    recordId: entityId,
                    rowVersion: rowVersion || ""
                })
            };
            var navigationOptions = {
                target: 2,
                width: { value: 640, unit: "px" },
                height: { value: 560, unit: "px" },
                position: 1,
                title: "Disqualify Lead"
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openErrorDialog({
                        message: (navError && navError.message) ? navError.message : "Could not open Disqualify Lead dialog."
                    });
                }
            );
        });
    };

    /**
     * Command: Open Transition Opportunity dialog (vn_dlg_transition.html -> EXT11 vn_TransitionOpportunity).
     * @param {Object} primaryControl
     * @param {string} commandMode - "Advance" | "Won" | "Lost"
     */
    VNEXTCRM.openTransitionDialog = function (primaryControl, commandMode) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var rawId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        var entityId = cleanGuid(rawId);
        if (!entityId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Opportunity before transitioning.",
                title: "Unsaved Record"
            });
            return;
        }

        if (guardDirtyForm(formContext, "Opportunity Transition")) {
            return;
        }

        var mode = (commandMode === "Won" || commandMode === "Lost") ? commandMode : "Advance";
        var currentStage = formContext.getAttribute("vn_stage") ? formContext.getAttribute("vn_stage").getValue() : null;

        Promise.all([
            VNEXTCRM.readRowVersion("vn_opportunity", entityId).catch(function () { return ""; }),
            VNEXTCRM.getBaseCurrencyCode().catch(function () { return ""; })
        ]).then(function (meta) {
            var rowVersion = meta[0];
            var currencyCode = meta[1];
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_transition.html",
                data: JSON.stringify({
                    recordId: entityId,
                    rowVersion: rowVersion || "",
                    commandMode: mode,
                    currentStage: currentStage,
                    currencyCode: currencyCode || ""
                })
            };
            var navigationOptions = {
                target: 2,
                width: { value: 700, unit: "px" },
                height: { value: 620, unit: "px" },
                position: 1,
                title: "Opportunity Transition: " + mode
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openErrorDialog({
                        message: (navError && navError.message) ? navError.message : "Could not open Transition Opportunity dialog."
                    });
                }
            );
        });
    };

    /**
     * Command: Open Opportunity Line dialog (vn_dlg_line.html -> EXT12 vn_EditOpportunityLine).
     * Supports calls from:
     *  - vn_opportunity Main Form / Opportunity Line SubGrid (PrimaryControl = Opportunity formContext)
     *  - vn_opportunityline Main Form (PrimaryControl = Line formContext; resolves parent vn_opportunity)
     * @param {Object} primaryControl
     * @param {string} operationMode - "Add" | "Remove"
     * @param {Array|string} [selectedIds] - SelectedControlSelectedItemIds from subgrid
     */
    VNEXTCRM.openLineDialog = function (primaryControl, operationMode, selectedIds) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var mode = (operationMode === "Remove") ? "Remove" : "Add";
        var entityName = (formContext.data && formContext.data.entity && typeof formContext.data.entity.getEntityName === "function")
            ? formContext.data.entity.getEntityName()
            : "";

        var oppId = "";
        var selectedLineId = "";
        var isLineForm = (entityName === "vn_opportunityline");

        if (isLineForm) {
            var parentAttr = formContext.getAttribute("vn_opportunity");
            var parentVal = parentAttr ? parentAttr.getValue() : null;
            if (parentVal && parentVal.length > 0 && parentVal[0].id) {
                oppId = cleanGuid(parentVal[0].id);
            }
            if (mode === "Remove" && formContext.data && formContext.data.entity) {
                selectedLineId = cleanGuid(formContext.data.entity.getId());
            }
        } else {
            if (guardDirtyForm(formContext, mode + " Opportunity Line")) {
                return;
            }
            var rawOppId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
            oppId = cleanGuid(rawOppId);

            if (Array.isArray(selectedIds) && selectedIds.length > 0) {
                selectedLineId = cleanGuid(selectedIds[0]);
            } else if (typeof selectedIds === "string" && selectedIds.trim().length > 0) {
                selectedLineId = cleanGuid(selectedIds);
            }
        }

        if (!oppId) {
            Xrm.Navigation.openAlertDialog({
                text: "Could not resolve the parent Opportunity. Please open an existing saved Opportunity first.",
                title: "Parent Opportunity Required"
            });
            return;
        }

        Promise.all([
            VNEXTCRM.readRowVersion("vn_opportunity", oppId).catch(function () { return ""; }),
            VNEXTCRM.getBaseCurrencyCode().catch(function () { return ""; })
        ]).then(function (meta) {
            var rowVersion = meta[0];
            var currencyCode = meta[1];
            var dialogData = {
                opportunityId: oppId,
                rowVersion: rowVersion || "",
                operationMode: mode,
                currencyCode: currencyCode || ""
            };
            if (selectedLineId) {
                dialogData.lineId = selectedLineId;
            }
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_line.html",
                data: JSON.stringify(dialogData)
            };
            var navigationOptions = {
                target: 2,
                width: { value: 740, unit: "px" },
                height: { value: 650, unit: "px" },
                position: 1,
                title: (mode === "Remove" ? "Remove" : "Add") + " Opportunity Product Line"
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    if (isLineForm && selectedLineId) {
                        var removed = consumeSessionHandoff("vn_line_removed_" + selectedLineId);
                        if (removed) {
                            Xrm.Navigation.openForm({
                                entityName: "vn_opportunity",
                                entityId: oppId
                            });
                            return;
                        }
                    }
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openErrorDialog({
                        message: (navError && navError.message) ? navError.message : "Could not open Opportunity Line dialog."
                    });
                }
            );
        });
    };

    /**
     * Command: Open Approval dialog (vn_dlg_approval.html — M5 EXT13 / WF04 prerequisite notice).
     * @param {Object} primaryControl
     * @param {string} operationMode - "Submit" | "Cancel"
     */
    VNEXTCRM.openApprovalDialog = function (primaryControl, operationMode) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var rawOppId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        var oppId = cleanGuid(rawOppId);
        if (!oppId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Opportunity before viewing approval details.",
                title: "Unsaved Record"
            });
            return;
        }
        var mode = (operationMode === "Cancel") ? "Cancel" : "Submit";

        var approvalAttr = formContext.getAttribute("vn_currentapproval");
        var currentApproval = approvalAttr ? approvalAttr.getValue() : null;
        var currentApprovalId = (currentApproval && currentApproval.length > 0)
            ? cleanGuid(currentApproval[0].id)
            : null;

        Promise.all([
            VNEXTCRM.readRowVersion("vn_opportunity", oppId).catch(function () { return ""; }),
            VNEXTCRM.getBaseCurrencyCode().catch(function () { return ""; })
        ]).then(function (meta) {
            var rowVersion = meta[0];
            var currencyCode = meta[1];
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_approval.html",
                data: JSON.stringify({
                    opportunityId: oppId,
                    rowVersion: rowVersion || "",
                    operationMode: mode,
                    currentApprovalId: currentApprovalId,
                    currencyCode: currencyCode || ""
                })
            };
            var navigationOptions = {
                target: 2,
                width: { value: 680, unit: "px" },
                height: { value: 560, unit: "px" },
                position: 1,
                title: (mode === "Cancel" ? "Cancel Approval Request" : "Submit Deal for Approval")
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openErrorDialog({
                        message: (navError && navError.message) ? navError.message : "Could not open Deal Approval dialog."
                    });
                }
            );
        });
    };

})();
