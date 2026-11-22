/**
 * @file vn_crm_common.js
 * @description VNEXT CRM Common & Form Handlers
 * Clean namespace implementation using strictly supported client SDK APIs.
 * Strictly NO DOM manipulation, NO window.parent hacks, NO app-global interception.
 */

var VNEXTCRM = VNEXTCRM || {};

(function () {
    "use strict";

    /**
     * Internal helper to resolve formContext from executionContext or primaryControl.
     * Supports form events, ribbon command bars, and subgrids.
     * @param {Object} context
     * @returns {Object|null} formContext
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
     * Reads rowversion / @odata.etag for concurrency control and normalizes W/"123" to "123".
     * @param {string} entityLogicalName
     * @param {string} recordId
     * @returns {Promise<string>} Normalized row version string
     */
    VNEXTCRM.readRowVersion = function (entityLogicalName, recordId) {
        if (!recordId) {
            return Promise.reject(new Error("Record ID is required to read row version."));
        }
        var cleanId = String(recordId).replace(/[{}]/g, "");
        return Xrm.WebApi.retrieveRecord(entityLogicalName, cleanId, "?$select=versionnumber").then(
            function (result) {
                var etag = result["@odata.etag"] || "";
                var match = etag.match(/W\/"'?([^"']+)'?"?/);
                if (match && match[1]) {
                    return match[1];
                }
                var cleaned = etag.replace(/^W\/"?'?/, "").replace(/'?"?$/, "");
                if (cleaned) {
                    return cleaned;
                }
                if (result.versionnumber != null) {
                    return String(result.versionnumber);
                }
                return "";
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
    // 2. Form Event Handlers
    // =========================================================================

    /**
     * Form OnLoad handler for vn_lead.
     * If lead is closed (statecode !== 0), lock all controls and display read-only guidance.
     * @param {Object} executionContext
     */
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

    /**
     * Form OnLoad handler for contact.
     * If existing record (getFormType() !== 1), disable parentcustomerid.
     * Evaluates vn_donotcontact opt-out banner.
     * @param {Object} executionContext
     */
    VNEXTCRM.onContactLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var formType = (formContext.ui && typeof formContext.ui.getFormType === "function")
            ? formContext.ui.getFormType()
            : 0;

        // FormType 1 = Create. If not Create, parentcustomerid is immutable
        if (formType !== 1) {
            var parentCustControl = formContext.getControl("parentcustomerid");
            if (parentCustControl && typeof parentCustControl.setDisabled === "function") {
                parentCustControl.setDisabled(true);
            }
        }

        VNEXTCRM.onContactDncChange(executionContext);
    };

    /**
     * OnChange handler for vn_donotcontact on contact.
     * Displays or clears warning banner when customer opts out of addressed communications.
     * @param {Object} executionContext
     */
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

    /**
     * Form OnLoad handler for vn_opportunity.
     * Always disables server-owned fields.
     * If closed (statecode !== 0), locks all controls and shows closed deal banner.
     * On BPF load, prevents client-side stage skipping via addOnPreStageChange.
     * @param {Object} executionContext
     */
    VNEXTCRM.onOpportunityLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        // 1. Always disable server-owned fields
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

        // 2. If statecode !== 0, lock all controls (including decision maker) and show banner
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

        // 3. On BPF load, prevent client-side stage advance/back via supported addOnPreStageChange
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

    /**
     * Form OnLoad handler for vn_product.
     * If existing product (getFormType() !== 1), shows guidance note that SKU is protected.
     * @param {Object} executionContext
     */
    VNEXTCRM.onProductLoad = function (executionContext) {
        var formContext = resolveFormContext(executionContext);
        if (!formContext) return;

        var formType = (formContext.ui && typeof formContext.ui.getFormType === "function")
            ? formContext.ui.getFormType()
            : 0;

        if (formType !== 1) { // Existing record
            VNEXTCRM.showNotification(
                formContext,
                "Product SKU is protected once referenced by Opportunity Lines.",
                "INFO",
                "VN_PRODUCT_SKU_INFO"
            );
        }
    };

    /**
     * Form OnLoad handler for native activities (task, phonecall).
     * If activity is completed or cancelled (statecode !== 0), locks controls for terminal history.
     * @param {Object} executionContext
     */
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

    /**
     * Generic Read-Only Form OnLoad handler for UX07 (vn_opportunityproduct) and UX09 (vn_approvalrequest).
     * Locks all controls and displays informative guidance.
     * @param {Object} executionContext
     */
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
    // 3. Command Launchers
    // =========================================================================

    /**
     * Command: Mark Lead as Contacted.
     * Checks Open Lead + nonblank Next Step & Follow-up date.
     * Updates statuscode to Contacted (100000001) or alerts missing fields.
     * Enforces Business Rule BR02: Next Step cannot be blank when transitioning to Contacted.
     * @param {Object} primaryControl - formContext
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
            statusAttr.setValue(100000001); // Contacted
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
     * Command: Open Qualify Lead dialog (vn_dlg_qualify.html).
     * Passes Lead ID & RowVersion for optimistic concurrency qualification.
     * @param {Object} primaryControl - formContext
     */
    VNEXTCRM.openQualifyDialog = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var entityId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        if (!entityId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the lead before qualifying.",
                title: "Unsaved Record"
            });
            return;
        }
        entityId = entityId.replace(/[{}]/g, "");

        var stateAttr = formContext.getAttribute("statecode");
        if (stateAttr && stateAttr.getValue() !== 0) {
            Xrm.Navigation.openAlertDialog({
                text: "Only active leads can be qualified.",
                title: "Lead Not Active"
            });
            return;
        }

        function launchDialog(rowVersion) {
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_qualify.html",
                data: JSON.stringify({
                    recordId: entityId,
                    rowVersion: rowVersion || ""
                })
            };
            var navigationOptions = {
                target: 2, // inline modal dialog
                width: { value: 650, unit: "px" },
                height: { value: 550, unit: "px" },
                position: 1, // center
                title: "Qualify Lead"
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openAlertDialog({
                        text: "Lead Qualify dialog invoked for Lead ID: " + entityId + " (RowVersion: " + rowVersion + ").\n\nPrerequisite: Requires HTML Web Resource vn_dlg_qualify.html and Milestone 3 server validation (EXT10).",
                        title: "Qualify Lead"
                    });
                }
            );
        }

        VNEXTCRM.readRowVersion("vn_lead", entityId).then(
            function (rowVersion) {
                launchDialog(rowVersion);
            },
            function () {
                launchDialog("");
            }
        );
    };

    /**
     * Command: Open Disqualify Lead dialog (vn_dlg_disqualify.html).
     * Checks reason, nonblank comments, and Duplicate lookup if Duplicate.
     * Shows M3 server guard readiness note.
     * @param {Object} primaryControl - formContext
     */
    VNEXTCRM.openDisqualifyDialog = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var entityId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        if (!entityId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the lead before disqualifying.",
                title: "Unsaved Record"
            });
            return;
        }
        entityId = entityId.replace(/[{}]/g, "");

        var stateAttr = formContext.getAttribute("statecode");
        if (stateAttr && stateAttr.getValue() !== 0) {
            Xrm.Navigation.openAlertDialog({
                text: "Only active leads can be disqualified.",
                title: "Lead Not Active"
            });
            return;
        }

        Xrm.Navigation.openAlertDialog({
            text: "Prepared for Milestone 3: Lead disqualification (BR07) requires server validation (PL02) for reason, nonblank comments, and non-self Duplicate Lead reference.",
            title: "Disqualify Lead"
        });
    };

    /**
     * Command: Open Transition dialog (vn_dlg_transition.html).
     * Supports commandMode: "Advance" | "Won" | "Lost".
     * Passes Opportunity ID, RowVersion, current stage, and commandMode.
     * @param {Object} primaryControl - formContext
     * @param {string} commandMode - "Advance" | "Won" | "Lost"
     */
    VNEXTCRM.openTransitionDialog = function (primaryControl, commandMode) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var entityId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        if (!entityId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Opportunity before transitioning.",
                title: "Unsaved Record"
            });
            return;
        }
        entityId = entityId.replace(/[{}]/g, "");

        var stateAttr = formContext.getAttribute("statecode");
        if (stateAttr && stateAttr.getValue() !== 0) {
            Xrm.Navigation.openAlertDialog({
                text: "Cannot transition a closed Opportunity.",
                title: "Opportunity Closed"
            });
            return;
        }

        var mode = commandMode || "Advance";
        var currentStage = formContext.getAttribute("vn_stage") ? formContext.getAttribute("vn_stage").getValue() : null;

        function launchDialog(rowVersion) {
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_transition.html",
                data: JSON.stringify({
                    recordId: entityId,
                    rowVersion: rowVersion || "",
                    commandMode: mode,
                    currentStage: currentStage
                })
            };
            var navigationOptions = {
                target: 2,
                width: { value: 700, unit: "px" },
                height: { value: 600, unit: "px" },
                position: 1,
                title: "Deal Transition: " + mode
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openAlertDialog({
                        text: "Opportunity Transition (" + mode + ") invoked for Opportunity ID: " + entityId + " (Stage: " + currentStage + ", RowVersion: " + rowVersion + ").\n\nPrerequisite: Requires HTML Web Resource vn_dlg_transition.html and Milestone 3 server validation (EXT11).",
                        title: "Deal Transition: " + mode
                    });
                }
            );
        }

        VNEXTCRM.readRowVersion("vn_opportunity", entityId).then(
            function (rowVersion) {
                launchDialog(rowVersion);
            },
            function () {
                launchDialog("");
            }
        );
    };

    /**
     * Command: Open Line Item dialog (vn_dlg_line.html).
     * Supports operationMode: "Add" | "Remove".
     * Passes Opportunity ID, RowVersion, operationMode, and optional lineId.
     * @param {Object} primaryControl - formContext
     * @param {string} operationMode - "Add" | "Remove"
     * @param {Array|string} [selectedIds] - selected row IDs from subgrid
     */
    VNEXTCRM.openLineDialog = function (primaryControl, operationMode, selectedIds) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var oppId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        if (!oppId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Opportunity before managing product lines.",
                title: "Unsaved Record"
            });
            return;
        }
        oppId = oppId.replace(/[{}]/g, "");
        var mode = operationMode || "Add";
        var lineId = null;
        if (Array.isArray(selectedIds) && selectedIds.length > 0) {
            lineId = selectedIds[0].replace(/[{}]/g, "");
        } else if (typeof selectedIds === "string" && selectedIds.length > 0) {
            lineId = selectedIds.replace(/[{}]/g, "");
        }

        var stateAttr = formContext.getAttribute("statecode");
        if (stateAttr && stateAttr.getValue() !== 0) {
            Xrm.Navigation.openAlertDialog({
                text: "Cannot modify product lines on a closed Opportunity.",
                title: "Opportunity Closed"
            });
            return;
        }

        function launchDialog(rowVersion) {
            var dialogData = {
                opportunityId: oppId,
                rowVersion: rowVersion || "",
                operationMode: mode
            };
            if (lineId) {
                dialogData.lineId = lineId;
            }
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_line.html",
                data: JSON.stringify(dialogData)
            };
            var navigationOptions = {
                target: 2,
                width: { value: 700, unit: "px" },
                height: { value: 550, unit: "px" },
                position: 1,
                title: (mode === "Remove" ? "Remove" : "Add") + " Opportunity Product Line"
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openAlertDialog({
                        text: "Line operation (" + mode + ") invoked for Opportunity ID: " + oppId + " (RowVersion: " + rowVersion + ").\n\nPrerequisite: Requires HTML Web Resource vn_dlg_line.html and Milestone 3 server validation (EXT12).",
                        title: "Opportunity Line Item Action"
                    });
                }
            );
        }

        VNEXTCRM.readRowVersion("vn_opportunity", oppId).then(
            function (rowVersion) {
                launchDialog(rowVersion);
            },
            function () {
                launchDialog("");
            }
        );
    };

    /**
     * Command: Open Approval dialog (vn_dlg_approval.html).
     * Supports operationMode: "Submit" | "Cancel".
     * Checks current approval state on opportunity and passes context to dialog.
     * @param {Object} primaryControl - formContext
     * @param {string} operationMode - "Submit" | "Cancel"
     */
    VNEXTCRM.openApprovalDialog = function (primaryControl, operationMode) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var oppId = (formContext.data && formContext.data.entity) ? formContext.data.entity.getId() : null;
        if (!oppId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please save the Opportunity before managing approvals.",
                title: "Unsaved Record"
            });
            return;
        }
        oppId = oppId.replace(/[{}]/g, "");
        var mode = operationMode || "Submit";

        var stateAttr = formContext.getAttribute("statecode");
        if (stateAttr && stateAttr.getValue() !== 0) {
            Xrm.Navigation.openAlertDialog({
                text: "Cannot submit or cancel approvals on a closed Opportunity.",
                title: "Opportunity Closed"
            });
            return;
        }

        var approvalAttr = formContext.getAttribute("vn_currentapproval");
        var currentApproval = approvalAttr ? approvalAttr.getValue() : null;
        var currentApprovalId = (currentApproval && currentApproval.length > 0)
            ? currentApproval[0].id.replace(/[{}]/g, "")
            : null;

        function launchDialog(rowVersion) {
            var pageInput = {
                pageType: "webresource",
                webresourceName: "vn_dlg_approval.html",
                data: JSON.stringify({
                    opportunityId: oppId,
                    rowVersion: rowVersion || "",
                    operationMode: mode,
                    currentApprovalId: currentApprovalId
                })
            };
            var navigationOptions = {
                target: 2,
                width: { value: 650, unit: "px" },
                height: { value: 500, unit: "px" },
                position: 1,
                title: (mode === "Cancel" ? "Cancel Approval Request" : "Submit Deal for Approval")
            };
            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function () {
                    VNEXTCRM.refreshForm(formContext);
                },
                function (navError) {
                    Xrm.Navigation.openAlertDialog({
                        text: "Approval action (" + mode + ") invoked for Opportunity ID: " + oppId + " (RowVersion: " + rowVersion + ").\n\nPrerequisite: Requires HTML Web Resource vn_dlg_approval.html and Milestone 5 server validation (EXT13 vn_ManageDealApproval).",
                        title: "Deal Approval Action"
                    });
                }
            );
        }

        VNEXTCRM.readRowVersion("vn_opportunity", oppId).then(
            function (rowVersion) {
                launchDialog(rowVersion);
            },
            function () {
                launchDialog("");
            }
        );
    };

})();
