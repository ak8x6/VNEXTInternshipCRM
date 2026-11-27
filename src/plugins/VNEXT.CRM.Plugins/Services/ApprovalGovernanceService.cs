using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;

namespace VNEXT.CRM.Plugins.Services
{
    public static class ApprovalGovernanceService
    {
        public static bool InvalidateCurrentApprovalOnLineChange(
            IOrganizationService writerService,
            Guid opportunityId,
            EntityReference currentApprovalRef,
            Guid correlationId)
        {
            if (currentApprovalRef == null || currentApprovalRef.Id == Guid.Empty)
            {
                return false;
            }

            if (writerService == null || opportunityId == Guid.Empty)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Unable to invalidate current Approval Request without a valid Opportunity and internal writer.",
                    correlationId);
            }

            Entity request;
            try
            {
                request = writerService.Retrieve(
                    CrmSchema.Entities.ApprovalRequest,
                    currentApprovalRef.Id,
                    new ColumnSet(
                        CrmSchema.ApprovalRequest.Result,
                        CrmSchema.ApprovalRequest.Opportunity));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approval Request could not be retrieved for invalidation.",
                    correlationId);
                return false;
            }

            if (request == null)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approval Request record was not found.",
                    correlationId);
            }

            var requestOppRef = request.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Opportunity);
            if (requestOppRef == null || requestOppRef.Id != opportunityId)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approval Request does not belong to the target Opportunity.",
                    correlationId);
            }

            var resultOption = request.GetAttributeValue<OptionSetValue>(CrmSchema.ApprovalRequest.Result);
            if (resultOption == null)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approval Request has no valid result value.",
                    correlationId);
            }

            switch (resultOption.Value)
            {
                case CrmSchema.ApprovalRequest.ResultValues.Pending:
                case CrmSchema.ApprovalRequest.ResultValues.Approved:
                    var reqPatch = new Entity(CrmSchema.Entities.ApprovalRequest, currentApprovalRef.Id);
                    reqPatch[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(CrmSchema.ApprovalRequest.ResultValues.Superseded);
                    writerService.Update(reqPatch);
                    break;

                case CrmSchema.ApprovalRequest.ResultValues.Rejected:
                case CrmSchema.ApprovalRequest.ResultValues.Superseded:
                case CrmSchema.ApprovalRequest.ResultValues.Cancelled:
                    // Preserve immutable terminal history (BR19 & R-02); only clear the parent pointer below.
                    break;

                default:
                    CrmError.Throw(
                        CrmError.CRM012,
                        "Current Approval Request has an unsupported result value.",
                        correlationId);
                    break;
            }

            var oppPatch = new Entity(CrmSchema.Entities.Opportunity, opportunityId);
            oppPatch[CrmSchema.Opportunity.CurrentApproval] = null;
            writerService.Update(oppPatch);

            return true;
        }

        public static void ValidateCurrentApprovalForWon(
            IOrganizationService readerService,
            Entity opportunity,
            decimal authoritativeTotal,
            decimal authoritativeMaxDiscount,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (config == null)
            {
                CrmError.Throw(CrmError.CRM008, "CRM policy configuration is unavailable.", correlationId);
            }

            if (!DomainPricing.RequiresApproval(authoritativeTotal, authoritativeMaxDiscount, config))
            {
                return;
            }

            if (readerService == null || opportunity == null || opportunity.Id == Guid.Empty)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approved Request is required before closing this Opportunity as Won.",
                    correlationId);
            }

            var currentApproval = opportunity.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.CurrentApproval);
            if (currentApproval == null || currentApproval.Id == Guid.Empty)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "This Opportunity requires a current Approved Request before closing as Won.",
                    correlationId);
            }

            Entity request;
            try
            {
                request = readerService.Retrieve(
                    CrmSchema.Entities.ApprovalRequest,
                    currentApproval.Id,
                    new ColumnSet(
                        CrmSchema.ApprovalRequest.Result,
                        CrmSchema.ApprovalRequest.Opportunity,
                        CrmSchema.ApprovalRequest.DealOwner,
                        CrmSchema.ApprovalRequest.Approver,
                        CrmSchema.ApprovalRequest.Responder,
                        CrmSchema.ApprovalRequest.RespondedOn,
                        CrmSchema.ApprovalRequest.Total,
                        CrmSchema.ApprovalRequest.MaxDiscount));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approval Request could not be verified.",
                    correlationId);
                return;
            }

            if (request == null)
            {
                CrmError.Throw(CrmError.CRM012, "Current Approval Request was not found.", correlationId);
            }

            var requestOppRef = request.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Opportunity);
            if (requestOppRef == null || requestOppRef.Id != opportunity.Id)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approval Request does not match this Opportunity.",
                    correlationId);
            }

            var resultOption = request.GetAttributeValue<OptionSetValue>(CrmSchema.ApprovalRequest.Result);
            if (resultOption == null || resultOption.Value != CrmSchema.ApprovalRequest.ResultValues.Approved)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Current Approval Request is not in Approved status.",
                    correlationId);
            }

            var oppOwner = opportunity.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OwnerId);
            var dealOwner = request.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.DealOwner);
            if (oppOwner == null
                || oppOwner.Id == Guid.Empty
                || dealOwner == null
                || dealOwner.Id != oppOwner.Id)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Approval Request deal owner snapshot does not match the Opportunity owner.",
                    correlationId);
            }

            var approver = request.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Approver);
            if (approver == null || approver.Id == Guid.Empty || approver.Id == oppOwner.Id)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Approval Request approver must be a distinct authorized Manager.",
                    correlationId);
            }

            var responder = request.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Responder);
            var respondedOn = request.GetAttributeValue<DateTime?>(CrmSchema.ApprovalRequest.RespondedOn);
            if (responder == null || responder.Id != approver.Id || !respondedOn.HasValue)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Approval Request lacks a verified Manager decision responder or timestamp.",
                    correlationId);
            }

            var reqTotal = request.GetAttributeValue<Money>(CrmSchema.ApprovalRequest.Total);
            var reqMaxDiscount = request.GetAttributeValue<decimal?>(CrmSchema.ApprovalRequest.MaxDiscount);
            if (reqTotal == null
                || reqTotal.Value != authoritativeTotal
                || !reqMaxDiscount.HasValue
                || reqMaxDiscount.Value != authoritativeMaxDiscount)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Approval Request financial snapshot does not match the current Opportunity totals.",
                    correlationId);
            }

            Entity approverUser;
            Entity dealOwnerUser;
            try
            {
                approverUser = readerService.Retrieve(
                    CrmSchema.Entities.SystemUser,
                    approver.Id,
                    new ColumnSet(CrmSchema.Platform.IsDisabled, CrmSchema.Platform.BusinessUnitId));

                dealOwnerUser = readerService.Retrieve(
                    CrmSchema.Entities.SystemUser,
                    dealOwner.Id,
                    new ColumnSet(CrmSchema.Platform.BusinessUnitId));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Unable to verify Approval Request Manager and owner business unit integrity.",
                    correlationId);
                return;
            }

            if (approverUser == null
                || approverUser.GetAttributeValue<bool?>(CrmSchema.Platform.IsDisabled) == true
                || dealOwnerUser == null)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Approval Request approver is disabled or unavailable.",
                    correlationId);
            }

            var approverBu = approverUser.GetAttributeValue<EntityReference>(CrmSchema.Platform.BusinessUnitId);
            var dealOwnerBu = dealOwnerUser.GetAttributeValue<EntityReference>(CrmSchema.Platform.BusinessUnitId);
            if (approverBu == null
                || approverBu.Id == Guid.Empty
                || dealOwnerBu == null
                || dealOwnerBu.Id == Guid.Empty
                || approverBu.Id != dealOwnerBu.Id)
            {
                CrmError.Throw(
                    CrmError.CRM012,
                    "Approval Request approver does not belong to the deal owner's business unit.",
                    correlationId);
            }
        }
    }
}
