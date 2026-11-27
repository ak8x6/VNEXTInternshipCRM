using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;
using VNEXT.CRM.Plugins.Services;

namespace VNEXT.CRM.Plugins.Guards
{
    public sealed class SalesGuardPlugin : IPlugin
    {
        private const string PreImageName = "PreImage";

        private static readonly HashSet<string> PlatformSystemAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "createdon",
            "createdby",
            "createdonbehalfby",
            "modifiedon",
            "modifiedby",
            "modifiedonbehalfby",
            "overriddencreatedon",
            "timezoneruleversionnumber",
            "utcconversiontimezonecode",
            "exchangerate",
            "organizationid",
            "versionnumber",
            "importsequencenumber"
        };

        private static readonly string[] LeadColumns =
        {
            CrmSchema.Lead.StateCode,
            CrmSchema.Lead.StatusCode,
            CrmSchema.Lead.OwnerId,
            CrmSchema.Lead.Name,
            CrmSchema.Lead.LastName,
            CrmSchema.Lead.Source,
            CrmSchema.Lead.Rating,
            CrmSchema.Lead.Email,
            CrmSchema.Lead.Phone,
            CrmSchema.Lead.NextFollowUpDate,
            CrmSchema.Lead.NextStep,
            CrmSchema.Lead.Budget,
            CrmSchema.Lead.TransactionCurrencyId,
            CrmSchema.Lead.DisqualificationReason,
            CrmSchema.Lead.DisqualificationComments,
            CrmSchema.Lead.DuplicateOf
        };

        private static readonly string[] LeadCreateForbiddenFields =
        {
            CrmSchema.Lead.Account,
            CrmSchema.Lead.Contact,
            CrmSchema.Lead.Opportunity,
            CrmSchema.Lead.QualifiedOn,
            CrmSchema.Lead.DisqualifiedOn,
            CrmSchema.Lead.DisqualificationReason,
            CrmSchema.Lead.DisqualificationComments,
            CrmSchema.Lead.DuplicateOf
        };

        private static readonly string[] LeadProtectedResultFields =
        {
            CrmSchema.Lead.Account,
            CrmSchema.Lead.Contact,
            CrmSchema.Lead.Opportunity,
            CrmSchema.Lead.QualifiedOn,
            CrmSchema.Lead.DisqualifiedOn
        };

        private static readonly HashSet<string> LeadExt10Allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Lead.Id,
            CrmSchema.Lead.StateCode,
            CrmSchema.Lead.StatusCode,
            CrmSchema.Lead.Account,
            CrmSchema.Lead.Contact,
            CrmSchema.Lead.Opportunity,
            CrmSchema.Lead.QualifiedOn
        };

        private static readonly string[] OpportunityColumns =
        {
            CrmSchema.Opportunity.StateCode,
            CrmSchema.Opportunity.StatusCode,
            CrmSchema.Opportunity.Stage,
            CrmSchema.Opportunity.Name,
            CrmSchema.Opportunity.Need,
            CrmSchema.Opportunity.DecisionMaker,
            CrmSchema.Opportunity.ProposalSummary,
            CrmSchema.Opportunity.NextStep,
            CrmSchema.Opportunity.ExpectedCloseDate,
            CrmSchema.Opportunity.NextFollowUpDate,
            CrmSchema.Opportunity.Budget,
            CrmSchema.Opportunity.BuyingTargetDate,
            CrmSchema.Opportunity.TransactionCurrencyId,
            CrmSchema.Opportunity.OwnerId
        };

        private static readonly string[] OpportunityProgressFields =
        {
            CrmSchema.Opportunity.Name,
            CrmSchema.Opportunity.Need,
            CrmSchema.Opportunity.DecisionMaker,
            CrmSchema.Opportunity.ProposalSummary,
            CrmSchema.Opportunity.NextStep,
            CrmSchema.Opportunity.ExpectedCloseDate,
            CrmSchema.Opportunity.Budget,
            CrmSchema.Opportunity.BuyingTargetDate
        };

        private static readonly string[] OpportunityLifecycleProtectedFields =
        {
            CrmSchema.Opportunity.Stage,
            CrmSchema.Opportunity.Probability,
            CrmSchema.Opportunity.StageEnteredOn,
            CrmSchema.Opportunity.LastProgressOn,
            CrmSchema.Opportunity.StateCode,
            CrmSchema.Opportunity.StatusCode,
            CrmSchema.Opportunity.ActualCloseDate,
            CrmSchema.Opportunity.ClosedAmount,
            CrmSchema.Opportunity.CloseComments,
            CrmSchema.Opportunity.LossReason,
            CrmSchema.Opportunity.CommandSequence
        };

        private static readonly HashSet<string> OpportunityExt10OwnerHandoffAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Opportunity.Id,
            CrmSchema.Opportunity.OwnerId,
            "owninguser",
            "owningteam",
            "owningbusinessunit"
        };

        private static readonly HashSet<string> OpportunityExt11Allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Opportunity.Id,
            CrmSchema.Opportunity.CommandSequence,
            CrmSchema.Opportunity.Stage,
            CrmSchema.Opportunity.Probability,
            CrmSchema.Opportunity.StageEnteredOn,
            CrmSchema.Opportunity.LastProgressOn,
            CrmSchema.Opportunity.StateCode,
            CrmSchema.Opportunity.StatusCode,
            CrmSchema.Opportunity.ActualCloseDate,
            CrmSchema.Opportunity.ClosedAmount,
            "vn_closedamount_base",
            CrmSchema.Opportunity.CloseComments,
            CrmSchema.Opportunity.LossReason
        };

        private static readonly HashSet<string> OpportunityExt12TotalsAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Opportunity.Id,
            CrmSchema.Opportunity.CommandSequence,
            CrmSchema.Opportunity.Total,
            "vn_total_base",
            CrmSchema.Opportunity.MaxDiscount,
            CrmSchema.Opportunity.LastProgressOn
        };

        private static readonly HashSet<string> OpportunityExt12PointerAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Opportunity.Id,
            CrmSchema.Opportunity.CurrentApproval
        };

        private static readonly HashSet<string> OpportunityExt13Allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Opportunity.Id,
            CrmSchema.Opportunity.CommandSequence,
            CrmSchema.Opportunity.CurrentApproval
        };

        private static readonly HashSet<string> OpportunityLineExt12OwnerHandoffAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.OpportunityLine.Id,
            CrmSchema.OpportunityLine.OwnerId,
            "owninguser",
            "owningteam",
            "owningbusinessunit"
        };

        private static readonly HashSet<string> ApprovalExt12UpdateAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.ApprovalRequest.Id,
            CrmSchema.ApprovalRequest.Result
        };

        private static readonly HashSet<string> ApprovalExt13UpdateAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.ApprovalRequest.Id,
            CrmSchema.ApprovalRequest.Result,
            CrmSchema.ApprovalRequest.Responder,
            CrmSchema.ApprovalRequest.RespondedOn,
            CrmSchema.ApprovalRequest.ResponseComments
        };

        private static readonly HashSet<string> BpfExt10CreateAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Bpf.Id,
            CrmSchema.Bpf.Name,
            CrmSchema.Bpf.Opportunity,
            CrmSchema.Bpf.ProcessId,
            CrmSchema.Bpf.ActiveStageId,
            CrmSchema.Bpf.TraversedPath,
            CrmSchema.Bpf.ActiveStageStartedOn,
            CrmSchema.Bpf.StateCode,
            CrmSchema.Bpf.StatusCode,
            CrmSchema.Bpf.CompletedOn,
            "bpf_duration"
        };

        private static readonly HashSet<string> BpfExt10UpdateAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Bpf.Id,
            CrmSchema.Bpf.ActiveStageId,
            CrmSchema.Bpf.TraversedPath,
            CrmSchema.Bpf.ActiveStageStartedOn,
            CrmSchema.Bpf.StateCode,
            CrmSchema.Bpf.StatusCode,
            "bpf_duration"
        };

        private static readonly HashSet<string> BpfExt11UpdateAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Bpf.Id,
            CrmSchema.Bpf.ActiveStageId,
            CrmSchema.Bpf.TraversedPath,
            CrmSchema.Bpf.ActiveStageStartedOn,
            CrmSchema.Bpf.StateCode,
            CrmSchema.Bpf.StatusCode,
            CrmSchema.Bpf.CompletedOn,
            "bpf_duration"
        };

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                CrmError.Throw(CrmError.CRM008, "Plugin service provider is unavailable.", Guid.Empty);
            }

            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context == null || factory == null)
            {
                CrmError.Throw(CrmError.CRM008, "Plugin execution context or service factory is unavailable.", Guid.Empty);
            }

            var correlationId = context.CorrelationId;
            var entityName = context.PrimaryEntityName ?? string.Empty;
            var message = context.MessageName ?? string.Empty;

            if (tracing != null)
            {
                tracing.Trace(
                    "PL02 SalesGuardPlugin: Message={0}, Entity={1}, Stage={2}, Depth={3}, CorrelationId={4:D}",
                    message,
                    entityName,
                    context.Stage,
                    context.Depth,
                    correlationId);
            }

            var callerService = AuthorizationService.CreateCallerService(factory, context);
            var config = CrmPolicyConfigLoader.Load(callerService, correlationId);

            if (string.Equals(entityName, CrmSchema.Entities.Lead, StringComparison.OrdinalIgnoreCase))
            {
                HandleLead(context, message, callerService, config, correlationId);
            }
            else if (string.Equals(entityName, CrmSchema.Entities.Opportunity, StringComparison.OrdinalIgnoreCase))
            {
                HandleOpportunity(context, message, callerService, config, correlationId);
            }
            else if (string.Equals(entityName, CrmSchema.Entities.OpportunityLine, StringComparison.OrdinalIgnoreCase))
            {
                HandleOpportunityLine(context, message, callerService, correlationId);
            }
            else if (string.Equals(entityName, CrmSchema.Entities.ApprovalRequest, StringComparison.OrdinalIgnoreCase))
            {
                HandleApprovalRequest(context, message, factory, config, correlationId);
            }
            else if (string.Equals(entityName, CrmSchema.Entities.OpportunitySalesProcess, StringComparison.OrdinalIgnoreCase))
            {
                HandleOpportunitySalesProcess(context, message, callerService, factory, config, correlationId);
            }
        }

        private static void HandleLead(
            IPluginExecutionContext context,
            string message,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "DeleteMultiple", StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(CrmError.CRM017, "Leads cannot be deleted; historical lead records must be preserved.", correlationId);
            }

            if (string.Equals(message, "Assign", StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(CrmError.CRM018, "Direct Lead reassignment is not permitted.", correlationId);
            }

            if (IsSetStateMessage(message))
            {
                var setStateLeadId = GetSetStateRecordId(context);
                var requestedLeadState = GetSetStateValue(context);
                TrustedApiScope setStateLeadScope = null;
                if (setStateLeadId != Guid.Empty
                    && requestedLeadState == CrmSchema.Lead.State.Inactive
                    && (IsPlatformStateSplitFromValidatedUpdate(context, CrmSchema.Entities.Lead, setStateLeadId)
                        || (TrustedOperationScope.TryResolveFromParentChain(context, out setStateLeadScope)
                            && string.Equals(setStateLeadScope.ApiMessage, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal)
                            && setStateLeadScope.BoundEntityId == setStateLeadId)))
                {
                    return;
                }

                CrmError.Throw(CrmError.CRM005, "Direct Lead state transitions via SetState are not permitted.", correlationId);
            }

            if (string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateLeadCreate(target, config, correlationId);
                }
            }
            else if (string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateLeadCreate(target, config, correlationId);
                    }
                }
            }
            else if (string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateLeadUpdate(context, target, allowTrustedScope: true, allowPreImage: true, allowDelegatedServerStamp: false, callerService, config, correlationId);
                }
            }
            else if (string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null && targets.Entities != null)
                {
                    bool isSingleItem = targets.Entities.Count == 1;
                    foreach (var target in targets.Entities)
                    {
                        ValidateLeadUpdate(
                            context,
                            target,
                            allowTrustedScope: isSingleItem,
                            allowPreImage: false,
                            allowDelegatedServerStamp: isSingleItem,
                            callerService,
                            config,
                            correlationId);
                    }
                }
            }
        }

        private static void ValidateLeadCreate(Entity target, CrmPolicyConfig config, Guid correlationId)
        {
            var state = GetOptionSetInt(target, CrmSchema.Lead.StateCode);
            if (state.HasValue && state.Value != CrmSchema.Lead.State.Active)
            {
                CrmError.Throw(CrmError.CRM005, "A new Lead cannot be created directly in an Inactive state.", correlationId);
            }

            var status = GetOptionSetInt(target, CrmSchema.Lead.StatusCode);
            if (status.HasValue
                && (status.Value == CrmSchema.Lead.Status.Qualified || status.Value == CrmSchema.Lead.Status.Disqualified))
            {
                CrmError.Throw(CrmError.CRM005, "A new Lead cannot be created directly as Qualified or Disqualified.", correlationId);
            }

            for (int i = 0; i < LeadCreateForbiddenFields.Length; i++)
            {
                var attr = LeadCreateForbiddenFields[i];
                if (target.Contains(attr) && target[attr] != null)
                {
                    CrmError.Throw(CrmError.CRM005, "Protected Lead conversion or disqualification fields cannot be set on Create.", correlationId);
                }
            }

            if (status.HasValue && status.Value != CrmSchema.Lead.Status.New)
            {
                CrmError.Throw(CrmError.CRM001, "A new Lead must start in New status.", correlationId);
            }

            if (!target.Contains(CrmSchema.Lead.Source) || target[CrmSchema.Lead.Source] == null)
            {
                target[CrmSchema.Lead.Source] = new OptionSetValue(CrmSchema.Lead.SourceValues.Referral);
            }

            if (!target.Contains(CrmSchema.Lead.Rating) || target[CrmSchema.Lead.Rating] == null)
            {
                target[CrmSchema.Lead.Rating] = new OptionSetValue(CrmSchema.Lead.RatingValues.Warm);
            }

            if (!target.Contains(CrmSchema.Lead.TransactionCurrencyId) || target[CrmSchema.Lead.TransactionCurrencyId] == null)
            {
                target[CrmSchema.Lead.TransactionCurrencyId] = new EntityReference(
                    CrmSchema.Entities.TransactionCurrency,
                    config.BaseCurrencyId);
            }

            var budgetMoney = target.GetAttributeValue<Money>(CrmSchema.Lead.Budget);
            var currencyRef = target.GetAttributeValue<EntityReference>(CrmSchema.Lead.TransactionCurrencyId);

            DomainValidators.ValidateOpenLead(
                target.GetAttributeValue<string>(CrmSchema.Lead.Name),
                target.GetAttributeValue<string>(CrmSchema.Lead.LastName),
                GetOptionSetInt(target, CrmSchema.Lead.Source),
                GetOptionSetInt(target, CrmSchema.Lead.Rating),
                target.GetAttributeValue<string>(CrmSchema.Lead.Email),
                target.GetAttributeValue<string>(CrmSchema.Lead.Phone),
                target.GetAttributeValue<DateTime?>(CrmSchema.Lead.NextFollowUpDate),
                budgetMoney != null ? (decimal?)budgetMoney.Value : null,
                currencyRef != null ? (Guid?)currencyRef.Id : null,
                CrmSchema.Lead.Status.New,
                target.GetAttributeValue<string>(CrmSchema.Lead.NextStep),
                config,
                correlationId);
        }

        private static void ValidateLeadUpdate(
            IPluginExecutionContext context,
            Entity target,
            bool allowTrustedScope,
            bool allowPreImage,
            bool allowDelegatedServerStamp,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            var leadId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;

            TrustedApiScope scope;
            if (allowTrustedScope
                && TrustedOperationScope.TryResolveFromParentChain(context, out scope)
                && string.Equals(scope.ApiMessage, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal)
                && scope.BoundEntityId == leadId)
            {
                if (!AreAllAttributesAllowedWithPlatformSafe(target, LeadExt10Allowlist))
                {
                    CrmError.Throw(CrmError.CRM005, "Nested Lead qualification update contains unauthorized attributes.", correlationId);
                }

                if (IsPlatformStateSplitFromValidatedUpdate(context, CrmSchema.Entities.Lead, leadId))
                {
                    return;
                }

                var stateVal = GetOptionSetInt(target, CrmSchema.Lead.StateCode);
                var statusVal = GetOptionSetInt(target, CrmSchema.Lead.StatusCode);
                var accountRef = target.GetAttributeValue<EntityReference>(CrmSchema.Lead.Account);
                var contactRef = target.GetAttributeValue<EntityReference>(CrmSchema.Lead.Contact);
                var oppRef = target.GetAttributeValue<EntityReference>(CrmSchema.Lead.Opportunity);
                var qualifiedOn = target.GetAttributeValue<DateTime?>(CrmSchema.Lead.QualifiedOn);

                if (stateVal != CrmSchema.Lead.State.Inactive
                    || statusVal != CrmSchema.Lead.Status.Qualified
                    || accountRef == null || accountRef.Id == Guid.Empty
                    || contactRef == null || contactRef.Id == Guid.Empty
                    || oppRef == null || oppRef.Id == Guid.Empty
                    || !qualifiedOn.HasValue)
                {
                    CrmError.Throw(CrmError.CRM005, "Nested Lead qualification closure is missing required conversion links or timestamps.", correlationId);
                }

                return;
            }

            if (allowTrustedScope
                && IsPlatformStateSplitFromValidatedUpdate(context, CrmSchema.Entities.Lead, leadId))
            {
                if (target.Contains(CrmSchema.Lead.Account)
                    || target.Contains(CrmSchema.Lead.Contact)
                    || target.Contains(CrmSchema.Lead.Opportunity)
                    || target.Contains(CrmSchema.Lead.QualifiedOn))
                {
                    CrmError.Throw(CrmError.CRM005, "Protected Lead conversion fields cannot be edited outside vn_QualifyLead.", correlationId);
                }

                return;
            }

            var existing = LoadExistingEntity(context, CrmSchema.Entities.Lead, leadId, LeadColumns, allowPreImage, callerService, correlationId);
            var existingState = GetOptionSetInt(existing, CrmSchema.Lead.StateCode);
            if (existingState == CrmSchema.Lead.State.Inactive)
            {
                CrmError.Throw(CrmError.CRM005, "Closed Leads are immutable and cannot be modified or reopened.", correlationId);
            }

            if (target.Contains(CrmSchema.Lead.OwnerId))
            {
                var existingOwner = existing.GetAttributeValue<EntityReference>(CrmSchema.Lead.OwnerId);
                var newOwner = target.GetAttributeValue<EntityReference>(CrmSchema.Lead.OwnerId);
                if (newOwner == null || existingOwner == null || newOwner.Id != existingOwner.Id)
                {
                    CrmError.Throw(CrmError.CRM018, "Lead ownership cannot be changed via direct Update.", correlationId);
                }
            }

            for (int i = 0; i < LeadProtectedResultFields.Length; i++)
            {
                var protectedField = LeadProtectedResultFields[i];
                if (!target.Contains(protectedField))
                {
                    continue;
                }

                if (allowDelegatedServerStamp
                    && string.Equals(protectedField, CrmSchema.Lead.DisqualifiedOn, StringComparison.OrdinalIgnoreCase)
                    && GetOptionSetInt(target, CrmSchema.Lead.StatusCode) == CrmSchema.Lead.Status.Disqualified
                    && IsFreshServerUtcStamp(target.GetAttributeValue<DateTime?>(CrmSchema.Lead.DisqualifiedOn)))
                {
                    continue;
                }

                CrmError.Throw(CrmError.CRM005, "Protected Lead conversion and terminal outcome fields cannot be edited directly.", correlationId);
            }

            var targetState = GetOptionSetInt(target, CrmSchema.Lead.StateCode);
            var targetStatus = GetOptionSetInt(target, CrmSchema.Lead.StatusCode);

            if (targetStatus == CrmSchema.Lead.Status.Qualified)
            {
                CrmError.Throw(CrmError.CRM005, "Leads can only be qualified through the vn_QualifyLead API.", correlationId);
            }

            if (targetState == CrmSchema.Lead.State.Inactive && targetStatus != CrmSchema.Lead.Status.Disqualified)
            {
                CrmError.Throw(CrmError.CRM005, "Deactivating a Lead requires explicit Disqualified status.", correlationId);
            }

            var merged = MergeOverExisting(CrmSchema.Entities.Lead, leadId, existing, target);

            if (targetStatus == CrmSchema.Lead.Status.Disqualified)
            {
                target[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);

                var reason = GetOptionSetInt(merged, CrmSchema.Lead.DisqualificationReason);
                var comments = merged.GetAttributeValue<string>(CrmSchema.Lead.DisqualificationComments);
                var duplicateOfRef = merged.GetAttributeValue<EntityReference>(CrmSchema.Lead.DuplicateOf);

                DomainValidators.ValidateLeadDisqualification(
                    leadId,
                    reason,
                    comments,
                    duplicateOfRef != null ? (Guid?)duplicateOfRef.Id : null,
                    correlationId);

                if (reason == CrmSchema.Lead.DisqualificationReasonValues.Duplicate)
                {
                    try
                    {
                        var dupLead = callerService.Retrieve(
                            CrmSchema.Entities.Lead,
                            duplicateOfRef.Id,
                            new ColumnSet(CrmSchema.Lead.Id));
                        if (dupLead == null)
                        {
                            CrmError.Throw(CrmError.CRM001, "Referenced Duplicate Of Lead could not be found.", correlationId);
                        }
                    }
                    catch (FaultException<OrganizationServiceFault>)
                    {
                        CrmError.Throw(CrmError.CRM001, "Referenced Duplicate Of Lead is inaccessible or does not exist.", correlationId);
                    }
                }

                target[CrmSchema.Lead.DisqualifiedOn] = UtcClock.EnsureUtc(DateTime.UtcNow);
                return;
            }

            if ((target.Contains(CrmSchema.Lead.DisqualificationReason) && target[CrmSchema.Lead.DisqualificationReason] != null)
                || (target.Contains(CrmSchema.Lead.DisqualificationComments) && target[CrmSchema.Lead.DisqualificationComments] != null)
                || (target.Contains(CrmSchema.Lead.DuplicateOf) && target[CrmSchema.Lead.DuplicateOf] != null))
            {
                CrmError.Throw(CrmError.CRM001, "Disqualification details can only be populated when disqualifying a Lead.", correlationId);
            }

            var existingStatus = GetOptionSetInt(existing, CrmSchema.Lead.StatusCode) ?? CrmSchema.Lead.Status.New;
            var mergedStatus = GetOptionSetInt(merged, CrmSchema.Lead.StatusCode) ?? existingStatus;
            if (existingStatus == CrmSchema.Lead.Status.Contacted && mergedStatus == CrmSchema.Lead.Status.New)
            {
                CrmError.Throw(CrmError.CRM005, "A Contacted Lead cannot revert to New status.", correlationId);
            }

            var budgetMoney = merged.GetAttributeValue<Money>(CrmSchema.Lead.Budget);
            var currencyRef = merged.GetAttributeValue<EntityReference>(CrmSchema.Lead.TransactionCurrencyId);

            DomainValidators.ValidateOpenLead(
                merged.GetAttributeValue<string>(CrmSchema.Lead.Name),
                merged.GetAttributeValue<string>(CrmSchema.Lead.LastName),
                GetOptionSetInt(merged, CrmSchema.Lead.Source),
                GetOptionSetInt(merged, CrmSchema.Lead.Rating),
                merged.GetAttributeValue<string>(CrmSchema.Lead.Email),
                merged.GetAttributeValue<string>(CrmSchema.Lead.Phone),
                merged.GetAttributeValue<DateTime?>(CrmSchema.Lead.NextFollowUpDate),
                budgetMoney != null ? (decimal?)budgetMoney.Value : null,
                currencyRef != null ? (Guid?)currencyRef.Id : null,
                mergedStatus,
                merged.GetAttributeValue<string>(CrmSchema.Lead.NextStep),
                config,
                correlationId);
        }

        private static void HandleOpportunity(
            IPluginExecutionContext context,
            string message,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "DeleteMultiple", StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(CrmError.CRM017, "Opportunities cannot be deleted; historical sales records must be preserved.", correlationId);
            }

            if (string.Equals(message, "Assign", StringComparison.OrdinalIgnoreCase))
            {
                ValidateOpportunityAssign(context, callerService, correlationId);
                return;
            }

            if (IsSetStateMessage(message))
            {
                var setStateOppId = GetSetStateRecordId(context);
                var requestedOppState = GetSetStateValue(context);
                TrustedApiScope setStateScope = null;
                if (setStateOppId != Guid.Empty
                    && requestedOppState == CrmSchema.Opportunity.State.Inactive
                    && TrustedOperationScope.TryResolveFromParentChain(context, out setStateScope)
                    && string.Equals(setStateScope.ApiMessage, CrmSchema.Apis.TransitionOpportunity, StringComparison.Ordinal)
                    && setStateScope.BoundEntityId == setStateOppId
                    && (IsPlatformStateSplitFromValidatedUpdate(context, CrmSchema.Entities.Opportunity, setStateOppId)
                        || string.Equals(setStateScope.SubOperation, "Won", StringComparison.Ordinal)
                        || string.Equals(setStateScope.SubOperation, "Lost", StringComparison.Ordinal)))
                {
                    return;
                }

                CrmError.Throw(CrmError.CRM006, "Direct Opportunity state transitions via SetState are not permitted.", correlationId);
            }

            if (string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var singleCreateTarget = GetSingleTargetEntityOrNull(context);
                if (singleCreateTarget != null)
                {
                    ValidateOpportunityCreate(context, singleCreateTarget, config, correlationId);
                    return;
                }

                CrmError.Throw(CrmError.CRM005, "Opportunities can only be created through vn_QualifyLead.", correlationId);
            }

            if (string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                ValidateOpportunityCreate(context, target, config, correlationId);
                return;
            }

            if (string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateOpportunityUpdate(context, target, allowTrustedScope: true, allowPreImage: true, allowDelegatedServerStamp: false, callerService, config, correlationId);
                }
            }
            else if (string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null && targets.Entities != null)
                {
                    bool isSingleItem = targets.Entities.Count == 1;
                    foreach (var target in targets.Entities)
                    {
                        ValidateOpportunityUpdate(
                            context,
                            target,
                            allowTrustedScope: isSingleItem,
                            allowPreImage: false,
                            allowDelegatedServerStamp: isSingleItem,
                            callerService,
                            config,
                            correlationId);
                    }
                }
            }
        }

        private static void ValidateOpportunityCreate(
            IPluginExecutionContext context,
            Entity target,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            TrustedApiScope scope = null;
            if (target == null
                || !TrustedOperationScope.TryResolveFromParentChain(context, out scope)
                || !string.Equals(scope.ApiMessage, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal))
            {
                CrmError.Throw(CrmError.CRM005, "Opportunities can only be created through vn_QualifyLead.", correlationId);
            }

            var originatingLead = target.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OriginatingLead);
            var stage = GetOptionSetInt(target, CrmSchema.Opportunity.Stage);
            var probability = target.GetAttributeValue<int?>(CrmSchema.Opportunity.Probability);
            var total = target.GetAttributeValue<Money>(CrmSchema.Opportunity.Total);
            var maxDiscount = target.GetAttributeValue<decimal?>(CrmSchema.Opportunity.MaxDiscount);
            var commandSeq = target.GetAttributeValue<int?>(CrmSchema.Opportunity.CommandSequence);
            var currentApproval = target.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.CurrentApproval);
            var currencyRef = target.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.TransactionCurrencyId);

            if (originatingLead == null
                || originatingLead.Id != scope.BoundEntityId
                || stage != CrmSchema.Opportunity.StageValues.Qualify
                || probability != CrmSchema.Opportunity.Probabilities.Qualify
                || total == null || total.Value != 0m
                || !maxDiscount.HasValue || maxDiscount.Value != 0m
                || !commandSeq.HasValue || commandSeq.Value != 0
                || currentApproval != null)
            {
                CrmError.Throw(CrmError.CRM005, "New Opportunity does not satisfy mandatory initial server invariants.", correlationId);
            }

            DomainPricing.ValidateBaseCurrency(currencyRef != null ? (Guid?)currencyRef.Id : null, config, correlationId);
        }

        private static void ValidateOpportunityAssign(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            Guid correlationId)
        {
            TrustedApiScope scope = null;
            if (!TrustedOperationScope.TryResolveFromParentChain(context, out scope)
                || !string.Equals(scope.ApiMessage, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal))
            {
                CrmError.Throw(CrmError.CRM018, "Direct Opportunity reassignment is not permitted.", correlationId);
            }

            var targetRef = GetTargetReference(context);
            var assigneeRef = context.InputParameters != null && context.InputParameters.Contains("Assignee")
                ? context.InputParameters["Assignee"] as EntityReference
                : null;

            VerifyExt10OpportunityOwnerTransfer(
                context,
                targetRef != null ? targetRef.Id : Guid.Empty,
                assigneeRef,
                scope,
                callerService,
                correlationId);
        }

        private static void VerifyExt10OpportunityOwnerTransfer(
            IPluginExecutionContext context,
            Guid oppId,
            EntityReference newOwnerRef,
            TrustedApiScope scope,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (oppId == Guid.Empty || newOwnerRef == null || newOwnerRef.Id == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM018, "Opportunity ownership transfer target or assignee is invalid.", correlationId);
            }

            var existingOpp = LoadExistingEntity(
                context,
                CrmSchema.Entities.Opportunity,
                oppId,
                new[] { CrmSchema.Opportunity.OriginatingLead },
                allowPreImage: true,
                callerService,
                correlationId,
                CrmError.CRM018);

            var originatingLead = existingOpp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OriginatingLead);
            if (originatingLead == null || originatingLead.Id != scope.BoundEntityId)
            {
                CrmError.Throw(CrmError.CRM018, "Opportunity does not match the Lead being qualified.", correlationId);
            }

            var boundLead = LoadExistingEntity(
                context,
                CrmSchema.Entities.Lead,
                scope.BoundEntityId,
                new[] { CrmSchema.Lead.OwnerId },
                allowPreImage: false,
                callerService,
                correlationId,
                CrmError.CRM018);

            var leadOwner = boundLead.GetAttributeValue<EntityReference>(CrmSchema.Lead.OwnerId);
            if (leadOwner == null
                || leadOwner.Id == Guid.Empty
                || newOwnerRef.Id != leadOwner.Id
                || !string.Equals(newOwnerRef.LogicalName, leadOwner.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(CrmError.CRM018, "EXT10 may only transfer Opportunity ownership to the originating Lead owner.", correlationId);
            }
        }

        private static void ValidateOpportunityUpdate(
            IPluginExecutionContext context,
            Entity target,
            bool allowTrustedScope,
            bool allowPreImage,
            bool allowDelegatedServerStamp,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            var oppId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;

            TrustedApiScope scope;
            if (allowTrustedScope && TrustedOperationScope.TryResolveFromParentChain(context, out scope))
            {
                if (string.Equals(scope.ApiMessage, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal))
                {
                    if (!AreAllAttributesAllowedWithPlatformSafe(target, OpportunityExt10OwnerHandoffAllowlist)
                        || !target.Contains(CrmSchema.Opportunity.OwnerId))
                    {
                        CrmError.Throw(CrmError.CRM018, "Nested EXT10 Opportunity update may only transfer ownership to the Lead owner.", correlationId);
                    }

                    var newOwnerRef = target.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OwnerId);
                    VerifyExt10OpportunityOwnerTransfer(context, oppId, newOwnerRef, scope, callerService, correlationId);
                    return;
                }

                if (scope.BoundEntityId == oppId)
                {
                    if (string.Equals(scope.ApiMessage, CrmSchema.Apis.TransitionOpportunity, StringComparison.Ordinal))
                    {
                        if (!AreAllAttributesAllowedWithPlatformSafe(target, OpportunityExt11Allowlist))
                        {
                            CrmError.Throw(CrmError.CRM006, "Nested Opportunity transition update contains unauthorized attributes.", correlationId);
                        }

                        return;
                    }

                    if (string.Equals(scope.ApiMessage, CrmSchema.Apis.EditOpportunityLine, StringComparison.Ordinal))
                    {
                        if (target.Contains(CrmSchema.Opportunity.CurrentApproval))
                        {
                            if (!TrustedOperationScope.IsExecutedByConfiguredInternalWriter(context, config)
                                || !AreAllAttributesAllowedWithPlatformSafe(target, OpportunityExt12PointerAllowlist)
                                || target[CrmSchema.Opportunity.CurrentApproval] != null)
                            {
                                CrmError.Throw(CrmError.CRM012, "Clearing vn_currentapproval under EXT12 requires the scoped internal writer and a null pointer.", correlationId);
                            }

                            return;
                        }

                        if (!AreAllAttributesAllowedWithPlatformSafe(target, OpportunityExt12TotalsAllowlist))
                        {
                            CrmError.Throw(CrmError.CRM006, "Nested Opportunity line-totals update contains unauthorized attributes.", correlationId);
                        }

                        return;
                    }

                    if (string.Equals(scope.ApiMessage, CrmSchema.Apis.ManageDealApproval, StringComparison.Ordinal))
                    {
                        if (target.Contains(CrmSchema.Opportunity.CurrentApproval)
                            && !TrustedOperationScope.IsExecutedByConfiguredInternalWriter(context, config))
                        {
                            CrmError.Throw(CrmError.CRM013, "Updating vn_currentapproval requires the scoped internal writer.", correlationId);
                        }

                        if (!AreAllAttributesAllowedWithPlatformSafe(target, OpportunityExt13Allowlist))
                        {
                            CrmError.Throw(CrmError.CRM013, "Nested approval coordination update contains unauthorized attributes.", correlationId);
                        }

                        return;
                    }
                }
            }

            var existing = LoadExistingEntity(context, CrmSchema.Entities.Opportunity, oppId, OpportunityColumns, allowPreImage, callerService, correlationId);
            var existingState = GetOptionSetInt(existing, CrmSchema.Opportunity.StateCode);
            if (existingState == CrmSchema.Opportunity.State.Inactive)
            {
                CrmError.Throw(CrmError.CRM006, "Closed Opportunities are immutable and cannot be modified.", correlationId);
            }

            if (target.Contains(CrmSchema.Opportunity.OwnerId))
            {
                CrmError.Throw(CrmError.CRM018, "Opportunity ownership cannot be modified directly.", correlationId);
            }

            if (target.Contains(CrmSchema.Opportunity.TransactionCurrencyId))
            {
                CrmError.Throw(CrmError.CRM008, "Opportunity currency cannot be modified directly.", correlationId);
            }

            if (target.Contains(CrmSchema.Opportunity.OriginatingLead)
                || target.Contains(CrmSchema.Opportunity.Account)
                || target.Contains(CrmSchema.Opportunity.PrimaryContact))
            {
                CrmError.Throw(CrmError.CRM005, "Opportunity originating Lead and customer links are immutable after qualification.", correlationId);
            }

            if (target.Contains(CrmSchema.Opportunity.Total) || target.Contains(CrmSchema.Opportunity.MaxDiscount))
            {
                CrmError.Throw(CrmError.CRM009, "Opportunity Total and Max Discount are server-computed and cannot be edited directly.", correlationId);
            }

            if (target.Contains(CrmSchema.Opportunity.CurrentApproval))
            {
                CrmError.Throw(CrmError.CRM012, "Opportunity current Approval Request pointer cannot be edited directly.", correlationId);
            }

            bool progressChanged = HasAnyProgressFieldChanged(existing, target);
            for (int i = 0; i < OpportunityLifecycleProtectedFields.Length; i++)
            {
                var protectedField = OpportunityLifecycleProtectedFields[i];
                if (!target.Contains(protectedField))
                {
                    continue;
                }

                if (allowDelegatedServerStamp
                    && progressChanged
                    && string.Equals(protectedField, CrmSchema.Opportunity.LastProgressOn, StringComparison.OrdinalIgnoreCase)
                    && IsFreshServerUtcStamp(target.GetAttributeValue<DateTime?>(CrmSchema.Opportunity.LastProgressOn)))
                {
                    continue;
                }

                CrmError.Throw(CrmError.CRM006, "Protected Opportunity stage, probability, lifecycle, or close fields cannot be edited directly.", correlationId);
            }

            var merged = MergeOverExisting(CrmSchema.Entities.Opportunity, oppId, existing, target);

            var budgetMoney = merged.GetAttributeValue<Money>(CrmSchema.Opportunity.Budget);
            var currencyRef = merged.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.TransactionCurrencyId);
            DomainPricing.ValidateOptionalBudget(
                budgetMoney != null ? (decimal?)budgetMoney.Value : null,
                currencyRef != null ? (Guid?)currencyRef.Id : null,
                config,
                correlationId);

            var currentStage = GetOptionSetInt(merged, CrmSchema.Opportunity.Stage) ?? CrmSchema.Opportunity.StageValues.Qualify;
            OpportunityLifecycleRules.ValidatePersistedOpenStageFields(
                currentStage,
                merged.GetAttributeValue<string>(CrmSchema.Opportunity.Name),
                merged.GetAttributeValue<string>(CrmSchema.Opportunity.Need),
                merged.GetAttributeValue<string>(CrmSchema.Opportunity.DecisionMaker),
                merged.GetAttributeValue<string>(CrmSchema.Opportunity.ProposalSummary),
                merged.GetAttributeValue<string>(CrmSchema.Opportunity.NextStep),
                merged.GetAttributeValue<DateTime?>(CrmSchema.Opportunity.ExpectedCloseDate),
                merged.GetAttributeValue<DateTime?>(CrmSchema.Opportunity.NextFollowUpDate),
                correlationId);

            if (progressChanged)
            {
                target[CrmSchema.Opportunity.LastProgressOn] = UtcClock.EnsureUtc(DateTime.UtcNow);
            }
        }

        private static void HandleOpportunityLine(
            IPluginExecutionContext context,
            string message,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var target = string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase)
                    ? GetTargetEntity(context)
                    : GetSingleTargetEntityOrNull(context);
                TrustedApiScope scope = null;
                if (target == null
                    || !TrustedOperationScope.TryResolveFromParentChain(context, out scope)
                    || !string.Equals(scope.ApiMessage, CrmSchema.Apis.EditOpportunityLine, StringComparison.Ordinal)
                    || !string.Equals(scope.SubOperation, "Add", StringComparison.OrdinalIgnoreCase))
                {
                    CrmError.Throw(CrmError.CRM010, "Opportunity Lines can only be added through vn_EditOpportunityLine.", correlationId);
                }

                var parentRef = target.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.Opportunity);
                if (parentRef == null || parentRef.Id != scope.BoundEntityId)
                {
                    CrmError.Throw(CrmError.CRM010, "Opportunity Line parent does not match the bound Opportunity.", correlationId);
                }

                return;
            }

            if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "DeleteMultiple", StringComparison.OrdinalIgnoreCase))
            {
                bool isSingleDelete = string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase);
                var targetRef = isSingleDelete
                    ? GetTargetReference(context)
                    : GetSingleTargetReferenceOrNull(context);
                TrustedApiScope scope = null;
                if (targetRef == null
                    || !TrustedOperationScope.TryResolveFromParentChain(context, out scope)
                    || !string.Equals(scope.ApiMessage, CrmSchema.Apis.EditOpportunityLine, StringComparison.Ordinal)
                    || !string.Equals(scope.SubOperation, "Remove", StringComparison.OrdinalIgnoreCase))
                {
                    CrmError.Throw(CrmError.CRM010, "Opportunity Lines can only be removed through vn_EditOpportunityLine.", correlationId);
                }

                var existingLine = LoadExistingEntity(
                    context,
                    CrmSchema.Entities.OpportunityLine,
                    targetRef.Id,
                    new[] { CrmSchema.OpportunityLine.Opportunity },
                    allowPreImage: isSingleDelete,
                    callerService,
                    correlationId,
                    CrmError.CRM010);

                var parentRef = existingLine.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.Opportunity);
                if (parentRef == null || parentRef.Id != scope.BoundEntityId)
                {
                    CrmError.Throw(CrmError.CRM010, "Opportunity Line does not belong to the bound Opportunity.", correlationId);
                }

                return;
            }

            if (string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                bool isSingleUpdate = string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase);
                var target = isSingleUpdate
                    ? GetTargetEntity(context)
                    : GetSingleTargetEntityOrNull(context);
                TrustedApiScope scope = null;
                if (target == null
                    || !TrustedOperationScope.TryResolveFromParentChain(context, out scope)
                    || !string.Equals(scope.ApiMessage, CrmSchema.Apis.EditOpportunityLine, StringComparison.Ordinal)
                    || !string.Equals(scope.SubOperation, "Add", StringComparison.OrdinalIgnoreCase)
                    || !AreAllAttributesAllowedWithPlatformSafe(target, OpportunityLineExt12OwnerHandoffAllowlist)
                    || !target.Contains(CrmSchema.OpportunityLine.OwnerId))
                {
                    CrmError.Throw(CrmError.CRM010, "Direct Opportunity Line mutations are not permitted; use vn_EditOpportunityLine.", correlationId);
                }

                var lineId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
                var newOwnerRef = target.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.OwnerId);
                VerifyExt12LineOwnerTransfer(context, lineId, newOwnerRef, scope, callerService, correlationId);
                return;
            }

            if (string.Equals(message, "Assign", StringComparison.OrdinalIgnoreCase))
            {
                TrustedApiScope scope = null;
                if (!TrustedOperationScope.TryResolveFromParentChain(context, out scope)
                    || !string.Equals(scope.ApiMessage, CrmSchema.Apis.EditOpportunityLine, StringComparison.Ordinal)
                    || !string.Equals(scope.SubOperation, "Add", StringComparison.OrdinalIgnoreCase))
                {
                    CrmError.Throw(CrmError.CRM010, "Direct Opportunity Line assignment is not permitted; use vn_EditOpportunityLine.", correlationId);
                }

                var targetRef = GetTargetReference(context);
                var assigneeRef = context.InputParameters != null && context.InputParameters.Contains("Assignee")
                    ? context.InputParameters["Assignee"] as EntityReference
                    : null;

                VerifyExt12LineOwnerTransfer(
                    context,
                    targetRef != null ? targetRef.Id : Guid.Empty,
                    assigneeRef,
                    scope,
                    callerService,
                    correlationId);
                return;
            }

            CrmError.Throw(CrmError.CRM010, "Direct Opportunity Line mutations are not permitted; use vn_EditOpportunityLine.", correlationId);
        }

        private static void VerifyExt12LineOwnerTransfer(
            IPluginExecutionContext context,
            Guid lineId,
            EntityReference newOwnerRef,
            TrustedApiScope scope,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (lineId == Guid.Empty || newOwnerRef == null || newOwnerRef.Id == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM010, "Opportunity Line ownership transfer target or owner is invalid.", correlationId);
            }

            var existingLine = LoadExistingEntity(
                context,
                CrmSchema.Entities.OpportunityLine,
                lineId,
                new[] { CrmSchema.OpportunityLine.Opportunity },
                allowPreImage: true,
                callerService,
                correlationId,
                CrmError.CRM010);

            var lineOppRef = existingLine.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.Opportunity);
            if (lineOppRef == null || lineOppRef.Id != scope.BoundEntityId)
            {
                CrmError.Throw(CrmError.CRM010, "Opportunity Line does not belong to the bound Opportunity.", correlationId);
            }

            var boundOpp = LoadExistingEntity(
                context,
                CrmSchema.Entities.Opportunity,
                scope.BoundEntityId,
                new[] { CrmSchema.Opportunity.OwnerId },
                allowPreImage: false,
                callerService,
                correlationId,
                CrmError.CRM010);

            var oppOwnerRef = boundOpp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OwnerId);
            if (oppOwnerRef == null
                || oppOwnerRef.Id == Guid.Empty
                || newOwnerRef.Id != oppOwnerRef.Id
                || !string.Equals(newOwnerRef.LogicalName, oppOwnerRef.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(CrmError.CRM010, "EXT12 Add may only transfer Line ownership to the parent Opportunity owner.", correlationId);
            }
        }

        private static void HandleApprovalRequest(
            IPluginExecutionContext context,
            string message,
            IOrganizationServiceFactory factory,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "DeleteMultiple", StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(CrmError.CRM017, "Approval Request history cannot be deleted.", correlationId);
            }

            if (IsSetStateMessage(message))
            {
                CrmError.Throw(CrmError.CRM013, "Direct or bulk Approval Request mutations are not permitted.", correlationId);
            }

            bool isCreateMsg = string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase);
            bool isCreateMultipleMsg = string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase);
            bool isUpdateMsg = string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase);
            bool isUpdateMultipleMsg = string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase);

            TrustedApiScope scope = null;
            if (!TrustedOperationScope.IsExecutedByConfiguredInternalWriter(context, config)
                || !TrustedOperationScope.TryResolveFromParentChain(context, out scope))
            {
                CrmError.Throw(CrmError.CRM013, "Approval Request mutations require a verified Custom API scope executed by the scoped internal writer.", correlationId);
            }

            var writerReadService = factory.CreateOrganizationService(context.UserId);

            if (string.Equals(scope.ApiMessage, CrmSchema.Apis.EditOpportunityLine, StringComparison.Ordinal))
            {
                if (!isUpdateMsg && !isUpdateMultipleMsg)
                {
                    CrmError.Throw(CrmError.CRM013, "Only Approval Request supersession updates are permitted under vn_EditOpportunityLine.", correlationId);
                }

                var target = isUpdateMsg ? GetTargetEntity(context) : GetSingleTargetEntityOrNull(context);
                if (target == null || !AreAllAttributesAllowedWithPlatformSafe(target, ApprovalExt12UpdateAllowlist))
                {
                    CrmError.Throw(CrmError.CRM013, "Approval Request update under EXT12 contains unauthorized attributes.", correlationId);
                }

                var reqId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
                var existing = LoadExistingEntity(
                    context,
                    CrmSchema.Entities.ApprovalRequest,
                    reqId,
                    new[] { CrmSchema.ApprovalRequest.Opportunity, CrmSchema.ApprovalRequest.Result },
                    allowPreImage: isUpdateMsg,
                    writerReadService,
                    correlationId,
                    CrmError.CRM013);

                var existingOpp = existing.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Opportunity);
                var existingResult = GetOptionSetInt(existing, CrmSchema.ApprovalRequest.Result);
                var newResult = GetOptionSetInt(target, CrmSchema.ApprovalRequest.Result);

                if (existingOpp == null
                    || existingOpp.Id != scope.BoundEntityId
                    || (existingResult != CrmSchema.ApprovalRequest.ResultValues.Pending
                        && existingResult != CrmSchema.ApprovalRequest.ResultValues.Approved)
                    || newResult != CrmSchema.ApprovalRequest.ResultValues.Superseded)
                {
                    CrmError.Throw(CrmError.CRM013, "EXT12 may only supersede a current Pending or Approved Request for the bound Opportunity.", correlationId);
                }

                return;
            }

            if (string.Equals(scope.ApiMessage, CrmSchema.Apis.ManageDealApproval, StringComparison.Ordinal))
            {
                if (isCreateMsg || isCreateMultipleMsg)
                {
                    var target = isCreateMsg ? GetTargetEntity(context) : GetSingleTargetEntityOrNull(context);
                    var targetOpp = target != null ? target.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Opportunity) : null;
                    if (!string.Equals(scope.SubOperation, "Submit", StringComparison.OrdinalIgnoreCase)
                        || targetOpp == null
                        || targetOpp.Id != scope.BoundEntityId)
                    {
                        CrmError.Throw(CrmError.CRM013, "Approval Request creation is only permitted during EXT13 Submit for the bound Opportunity.", correlationId);
                    }

                    return;
                }

                if (string.Equals(message, "Assign", StringComparison.OrdinalIgnoreCase))
                {
                    var targetRef = GetTargetReference(context);
                    if (!string.Equals(scope.SubOperation, "Submit", StringComparison.OrdinalIgnoreCase) || targetRef == null)
                    {
                        CrmError.Throw(CrmError.CRM013, "Approval Request assignment is only permitted during EXT13 Submit.", correlationId);
                    }

                    var existing = LoadExistingEntity(
                        context,
                        CrmSchema.Entities.ApprovalRequest,
                        targetRef.Id,
                        new[] { CrmSchema.ApprovalRequest.Opportunity },
                        allowPreImage: true,
                        writerReadService,
                        correlationId,
                        CrmError.CRM013);

                    var existingOpp = existing.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Opportunity);
                    if (existingOpp == null || existingOpp.Id != scope.BoundEntityId)
                    {
                        CrmError.Throw(CrmError.CRM013, "Approval Request does not belong to the bound Opportunity.", correlationId);
                    }

                    return;
                }

                if (isUpdateMsg || isUpdateMultipleMsg)
                {
                    var target = isUpdateMsg ? GetTargetEntity(context) : GetSingleTargetEntityOrNull(context);
                    bool isDecisionOrCancel = string.Equals(scope.SubOperation, "RecordDecision", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(scope.SubOperation, "Cancel", StringComparison.OrdinalIgnoreCase);

                    if (!isDecisionOrCancel
                        || target == null
                        || !AreAllAttributesAllowedWithPlatformSafe(target, ApprovalExt13UpdateAllowlist))
                    {
                        CrmError.Throw(CrmError.CRM013, "Approval Request update under EXT13 is not authorized for this operation or attribute set.", correlationId);
                    }

                    var reqId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
                    var existing = LoadExistingEntity(
                        context,
                        CrmSchema.Entities.ApprovalRequest,
                        reqId,
                        new[] { CrmSchema.ApprovalRequest.Opportunity },
                        allowPreImage: isUpdateMsg,
                        writerReadService,
                        correlationId,
                        CrmError.CRM013);

                    var existingOpp = existing.GetAttributeValue<EntityReference>(CrmSchema.ApprovalRequest.Opportunity);
                    if (existingOpp == null || existingOpp.Id != scope.BoundEntityId)
                    {
                        CrmError.Throw(CrmError.CRM013, "Approval Request does not belong to the bound Opportunity.", correlationId);
                    }

                    return;
                }
            }

            CrmError.Throw(CrmError.CRM013, "Approval Request mutation is not authorized under this execution scope.", correlationId);
        }

        private static void HandleOpportunitySalesProcess(
            IPluginExecutionContext context,
            string message,
            IOrganizationService callerService,
            IOrganizationServiceFactory factory,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (IsSetStateMessage(message))
            {
                var setStateBpfId = GetSetStateRecordId(context);
                var requestedBpfState = GetSetStateValue(context);
                TrustedApiScope setStateBpfScope = null;
                if (setStateBpfId != Guid.Empty
                    && requestedBpfState == CrmSchema.Bpf.StateInactive
                    && TrustedOperationScope.IsExecutedByConfiguredInternalWriter(context, config)
                    && TrustedOperationScope.TryResolveFromParentChain(context, out setStateBpfScope)
                    && string.Equals(setStateBpfScope.ApiMessage, CrmSchema.Apis.TransitionOpportunity, StringComparison.Ordinal))
                {
                    if (IsPlatformStateSplitFromValidatedUpdate(context, CrmSchema.Entities.OpportunitySalesProcess, setStateBpfId))
                    {
                        return;
                    }

                    if (string.Equals(setStateBpfScope.SubOperation, "Won", StringComparison.Ordinal)
                        || string.Equals(setStateBpfScope.SubOperation, "Lost", StringComparison.Ordinal))
                    {
                        var bpfVerifyService = factory.CreateOrganizationService(context.UserId);
                        var existingSetStateBpf = LoadExistingEntity(
                            context,
                            CrmSchema.Entities.OpportunitySalesProcess,
                            setStateBpfId,
                            new[] { CrmSchema.Bpf.Opportunity },
                            allowPreImage: false,
                            bpfVerifyService,
                            correlationId,
                            CrmError.CRM006);
                        var setStateBpfOppRef = existingSetStateBpf.GetAttributeValue<EntityReference>(CrmSchema.Bpf.Opportunity);
                        if (setStateBpfOppRef != null && setStateBpfOppRef.Id == setStateBpfScope.BoundEntityId)
                        {
                            return;
                        }
                    }
                }

                CrmError.Throw(CrmError.CRM006, "Direct Opportunity BPF deletion, bulk writes, or state changes are not permitted.", correlationId);
            }

            bool isSingleCreate = string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase);
            bool isMultiCreate = string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase);
            bool isSingleUpdate = string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase);
            bool isMultiUpdate = string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase);
            bool isCreate = isSingleCreate || isMultiCreate;
            bool isUpdate = isSingleUpdate || isMultiUpdate;
            if (!isCreate && !isUpdate)
            {
                CrmError.Throw(CrmError.CRM006, "Direct Opportunity BPF deletion, bulk writes, or state changes are not permitted.", correlationId);
            }

            TrustedApiScope scope = null;
            if (!TrustedOperationScope.TryResolveFromParentChain(context, out scope))
            {
                CrmError.Throw(CrmError.CRM006, "Opportunity BPF transitions are server-owned and cannot be modified directly.", correlationId);
            }

            bool isInternalWriter = TrustedOperationScope.IsExecutedByConfiguredInternalWriter(context, config);
            bool isPlatformAutoCreateUnderExt10 = !isInternalWriter
                && isCreate
                && string.Equals(scope.ApiMessage, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal)
                && HasEnclosingOpportunityCreateUnderExt10(context);

            if (!isInternalWriter && !isPlatformAutoCreateUnderExt10)
            {
                CrmError.Throw(CrmError.CRM006, "Opportunity BPF transitions are server-owned and cannot be modified directly.", correlationId);
            }

            var target = (isSingleCreate || isSingleUpdate)
                ? GetTargetEntity(context)
                : GetSingleTargetEntityOrNull(context);
            if (target == null)
            {
                CrmError.Throw(CrmError.CRM006, "Opportunity BPF target entity is missing or bulk writes are not permitted.", correlationId);
            }

            var verifyService = isInternalWriter
                ? factory.CreateOrganizationService(context.UserId)
                : callerService;

            if (string.Equals(scope.ApiMessage, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal))
            {
                EntityReference oppRef;
                if (isCreate)
                {
                    if (!AreAllAttributesAllowedWithPlatformSafe(target, BpfExt10CreateAllowlist))
                    {
                        CrmError.Throw(CrmError.CRM006, "BPF Create under EXT10 contains unauthorized attributes.", correlationId);
                    }

                    if (isPlatformAutoCreateUnderExt10)
                    {
                        ValidatePlatformAutoBpfCreateInvariants(target, correlationId);
                    }

                    oppRef = target.GetAttributeValue<EntityReference>(CrmSchema.Bpf.Opportunity);
                }
                else
                {
                    if (!AreAllAttributesAllowedWithPlatformSafe(target, BpfExt10UpdateAllowlist))
                    {
                        CrmError.Throw(CrmError.CRM006, "BPF Update under EXT10 contains unauthorized attributes.", correlationId);
                    }

                    var bpfId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
                    var existingBpf = LoadExistingEntity(
                        context,
                        CrmSchema.Entities.OpportunitySalesProcess,
                        bpfId,
                        new[] { CrmSchema.Bpf.Opportunity },
                        allowPreImage: isSingleUpdate,
                        verifyService,
                        correlationId,
                        CrmError.CRM006);
                    oppRef = existingBpf.GetAttributeValue<EntityReference>(CrmSchema.Bpf.Opportunity);
                }

                if (oppRef == null || oppRef.Id == Guid.Empty)
                {
                    CrmError.Throw(CrmError.CRM006, "BPF instance must reference a valid Opportunity.", correlationId);
                }

                var opp = LoadExistingEntity(
                    context,
                    CrmSchema.Entities.Opportunity,
                    oppRef.Id,
                    new[] { CrmSchema.Opportunity.OriginatingLead },
                    allowPreImage: false,
                    verifyService,
                    correlationId,
                    CrmError.CRM006);

                var originatingLead = opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OriginatingLead);
                if (originatingLead == null || originatingLead.Id != scope.BoundEntityId)
                {
                    CrmError.Throw(CrmError.CRM006, "BPF Opportunity does not match the Lead being qualified.", correlationId);
                }

                return;
            }

            if (string.Equals(scope.ApiMessage, CrmSchema.Apis.TransitionOpportunity, StringComparison.Ordinal))
            {
                if (!isUpdate || !AreAllAttributesAllowedWithPlatformSafe(target, BpfExt11UpdateAllowlist))
                {
                    CrmError.Throw(CrmError.CRM006, "Only BPF stage or close mirror updates are permitted under EXT11.", correlationId);
                }

                var bpfId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
                var existingBpf = LoadExistingEntity(
                    context,
                    CrmSchema.Entities.OpportunitySalesProcess,
                    bpfId,
                    new[] { CrmSchema.Bpf.Opportunity },
                    allowPreImage: isSingleUpdate,
                    verifyService,
                    correlationId,
                    CrmError.CRM006);

                var oppRef = existingBpf.GetAttributeValue<EntityReference>(CrmSchema.Bpf.Opportunity);
                if (oppRef == null || oppRef.Id != scope.BoundEntityId)
                {
                    CrmError.Throw(CrmError.CRM006, "BPF instance does not belong to the Opportunity being transitioned.", correlationId);
                }

                return;
            }

            CrmError.Throw(CrmError.CRM006, "Opportunity BPF mutation is not authorized under this execution scope.", correlationId);
        }

        private static bool HasEnclosingOpportunityCreateUnderExt10(IPluginExecutionContext context)
        {
            if (context == null
                || context.Mode != 0
                || !context.IsInTransaction
                || context.InitiatingUserId == Guid.Empty)
            {
                return false;
            }

            bool foundOpportunityCreate = false;
            var ancestor = context.ParentContext;
            var hops = 0;

            while (ancestor != null && hops < 32)
            {
                hops++;

                if (ancestor.Mode != 0
                    || !ancestor.IsInTransaction
                    || ancestor.CorrelationId != context.CorrelationId
                    || ancestor.InitiatingUserId != context.InitiatingUserId)
                {
                    return false;
                }

                if (string.Equals(ancestor.MessageName, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal))
                {
                    return foundOpportunityCreate;
                }

                if (string.Equals(ancestor.PrimaryEntityName, CrmSchema.Entities.Opportunity, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(ancestor.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ancestor.MessageName, "CreateMultiple", StringComparison.OrdinalIgnoreCase)))
                {
                    foundOpportunityCreate = true;
                }

                ancestor = ancestor.ParentContext;
            }

            return false;
        }

        private static void ValidatePlatformAutoBpfCreateInvariants(Entity target, Guid correlationId)
        {
            if (target.Contains(CrmSchema.Bpf.ActiveStageId) && target[CrmSchema.Bpf.ActiveStageId] != null)
            {
                var stageRef = target.GetAttributeValue<EntityReference>(CrmSchema.Bpf.ActiveStageId);
                if (stageRef == null || stageRef.Id != CrmSchema.Bpf.StageQualifyId)
                {
                    CrmError.Throw(CrmError.CRM006, "Platform auto-created Opportunity BPF must start at the Qualify stage.", correlationId);
                }
            }

            if (target.Contains(CrmSchema.Bpf.ProcessId) && target[CrmSchema.Bpf.ProcessId] != null)
            {
                var processRef = target.GetAttributeValue<EntityReference>(CrmSchema.Bpf.ProcessId);
                if (processRef == null || processRef.Id != CrmSchema.Bpf.WorkflowId)
                {
                    CrmError.Throw(CrmError.CRM006, "Platform auto-created Opportunity BPF must reference Opportunity Sales Process.", correlationId);
                }
            }

            if (target.Contains(CrmSchema.Bpf.StateCode) && target[CrmSchema.Bpf.StateCode] != null)
            {
                var state = GetOptionSetInt(target, CrmSchema.Bpf.StateCode);
                if (!state.HasValue || state.Value != 0)
                {
                    CrmError.Throw(CrmError.CRM006, "Platform auto-created Opportunity BPF must start in Active state.", correlationId);
                }
            }
        }

        private static bool AreAllAttributesAllowedWithPlatformSafe(Entity target, HashSet<string> allowlist)
        {
            if (target == null || allowlist == null)
            {
                return false;
            }

            foreach (var key in target.Attributes.Keys)
            {
                if (allowlist.Contains(key) || PlatformSystemAttributes.Contains(key))
                {
                    continue;
                }

                if (key.Length > 5 && key.EndsWith("_base", StringComparison.OrdinalIgnoreCase))
                {
                    var moneyAttr = key.Substring(0, key.Length - 5);
                    if (allowlist.Contains(moneyAttr))
                    {
                        continue;
                    }
                }

                return false;
            }

            return true;
        }

        private static bool HasAnyProgressFieldChanged(Entity existing, Entity target)
        {
            for (int i = 0; i < OpportunityProgressFields.Length; i++)
            {
                var field = OpportunityProgressFields[i];
                if (!target.Contains(field))
                {
                    continue;
                }

                var oldVal = existing != null && existing.Contains(field) ? existing[field] : null;
                var newVal = target[field];

                if (!AreFieldValuesEqual(field, oldVal, newVal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreFieldValuesEqual(string fieldName, object oldVal, object newVal)
        {
            if (oldVal == null && newVal == null)
            {
                return true;
            }

            if (oldVal is string || newVal is string)
            {
                var s1 = oldVal as string ?? string.Empty;
                var s2 = newVal as string ?? string.Empty;
                return string.Equals(s1, s2, StringComparison.Ordinal);
            }

            if (oldVal is Money || newVal is Money)
            {
                var m1 = oldVal as Money;
                var m2 = newVal as Money;
                if (m1 == null && m2 == null) return true;
                if (m1 == null || m2 == null) return false;
                return m1.Value == m2.Value;
            }

            if (oldVal is DateTime || newVal is DateTime)
            {
                if (!(oldVal is DateTime) || !(newVal is DateTime))
                {
                    return false;
                }

                var d1 = UtcClock.NormalizeDateOnly((DateTime)oldVal);
                var d2 = UtcClock.NormalizeDateOnly((DateTime)newVal);
                return d1 == d2;
            }

            return Equals(oldVal, newVal);
        }

        private static Entity LoadExistingEntity(
            IPluginExecutionContext context,
            string entityName,
            Guid id,
            string[] columns,
            bool allowPreImage,
            IOrganizationService service,
            Guid correlationId,
            string errorCode = CrmError.CRM008)
        {
            if (allowPreImage
                && context.PreEntityImages != null
                && context.PreEntityImages.Contains(PreImageName)
                && context.PreEntityImages[PreImageName] != null)
            {
                return context.PreEntityImages[PreImageName];
            }

            if (service == null || id == Guid.Empty)
            {
                CrmError.Throw(errorCode, "Existing record could not be loaded for guard verification.", correlationId);
            }

            try
            {
                var entity = service.Retrieve(entityName, id, new ColumnSet(columns));
                if (entity == null)
                {
                    CrmError.Throw(errorCode, "Existing record was not found.", correlationId);
                }

                return entity;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(errorCode, "Existing record could not be accessed for guard verification.", correlationId);
                return null;
            }
        }

        private static Entity MergeOverExisting(string entityName, Guid id, Entity existing, Entity target)
        {
            var merged = new Entity(entityName, id);
            if (existing != null)
            {
                foreach (var kvp in existing.Attributes)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            if (target != null)
            {
                foreach (var kvp in target.Attributes)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        private static Entity GetTargetEntity(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Target"))
            {
                return context.InputParameters["Target"] as Entity;
            }

            return null;
        }

        private static EntityCollection GetTargetEntities(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Targets"))
            {
                return context.InputParameters["Targets"] as EntityCollection;
            }

            return null;
        }

        private static Entity GetSingleTargetEntityOrNull(IPluginExecutionContext context)
        {
            var targets = GetTargetEntities(context);
            if (targets != null && targets.Entities != null && targets.Entities.Count == 1)
            {
                return targets.Entities[0];
            }

            return null;
        }

        private static EntityReference GetSingleTargetReferenceOrNull(IPluginExecutionContext context)
        {
            if (context != null && context.InputParameters != null && context.InputParameters.Contains("Targets"))
            {
                var refs = context.InputParameters["Targets"] as EntityReferenceCollection;
                if (refs != null && refs.Count == 1)
                {
                    return refs[0];
                }
            }

            return null;
        }

        private static bool IsFreshServerUtcStamp(DateTime? timestamp)
        {
            if (!timestamp.HasValue)
            {
                return false;
            }

            var utcStamp = UtcClock.EnsureUtc(timestamp.Value);
            var deltaSeconds = Math.Abs((DateTime.UtcNow - utcStamp).TotalSeconds);
            return deltaSeconds <= 60.0;
        }

        private static EntityReference GetTargetReference(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Target"))
            {
                var targetRef = context.InputParameters["Target"] as EntityReference;
                if (targetRef != null)
                {
                    return targetRef;
                }
            }

            if (context.PrimaryEntityId != Guid.Empty && !string.IsNullOrWhiteSpace(context.PrimaryEntityName))
            {
                return new EntityReference(context.PrimaryEntityName, context.PrimaryEntityId);
            }

            return null;
        }

        private static bool IsSetStateMessage(string message)
        {
            return string.Equals(message, "SetState", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "SetStateDynamicEntity", StringComparison.OrdinalIgnoreCase);
        }

        private static Guid GetSetStateRecordId(IPluginExecutionContext context)
        {
            if (context != null && context.InputParameters != null && context.InputParameters.Contains("EntityMoniker"))
            {
                var moniker = context.InputParameters["EntityMoniker"] as EntityReference;
                if (moniker != null && moniker.Id != Guid.Empty)
                {
                    return moniker.Id;
                }
            }

            return context != null ? context.PrimaryEntityId : Guid.Empty;
        }

        private static int? GetSetStateValue(IPluginExecutionContext context)
        {
            if (context != null && context.InputParameters != null && context.InputParameters.Contains("State"))
            {
                var raw = context.InputParameters["State"];
                var opt = raw as OptionSetValue;
                if (opt != null)
                {
                    return opt.Value;
                }

                if (raw is int)
                {
                    return (int)raw;
                }

                var str = raw as string;
                if (!string.IsNullOrWhiteSpace(str))
                {
                    var trimmed = str.Trim();
                    int parsed;
                    if (int.TryParse(trimmed, out parsed))
                    {
                        return parsed;
                    }

                    if (string.Equals(trimmed, "Active", StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }

                    if (string.Equals(trimmed, "Inactive", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(trimmed, "Finished", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(trimmed, "Aborted", StringComparison.OrdinalIgnoreCase))
                    {
                        return 1;
                    }
                }
            }

            return null;
        }

        private static bool IsPlatformStateSplitFromValidatedUpdate(
            IPluginExecutionContext context,
            string entityLogicalName,
            Guid recordId)
        {
            if (context == null
                || recordId == Guid.Empty
                || context.Depth < 2
                || context.Mode != 0
                || !context.IsInTransaction
                || context.InitiatingUserId == Guid.Empty)
            {
                return false;
            }

            var ancestor = context.ParentContext;
            var hops = 0;
            while (ancestor != null && hops < 8)
            {
                hops++;

                var ancestorEntity = GetContextEntityLogicalName(ancestor);
                if (ancestor.Mode != 0
                    || (ancestor.Stage != 10 && ancestor.Stage != 20 && ancestor.Stage != 30 && ancestor.Stage != 40)
                    || ancestor.CorrelationId != context.CorrelationId
                    || ancestor.InitiatingUserId == Guid.Empty
                    || (ancestor.InitiatingUserId != context.InitiatingUserId && ancestor.UserId != context.InitiatingUserId)
                    || !string.Equals(ancestorEntity, entityLogicalName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var ancestorRecordId = GetContextRecordId(ancestor);
                if (ancestorRecordId != Guid.Empty && ancestorRecordId != recordId)
                {
                    return false;
                }

                if (string.Equals(ancestor.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ancestor.MessageName, "UpdateMultiple", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ancestor.MessageName, "Upsert", StringComparison.OrdinalIgnoreCase))
                {
                    if (ancestorRecordId == recordId
                        || string.Equals(ancestor.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ancestor.MessageName, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    ancestor = ancestor.ParentContext;
                    continue;
                }

                if (IsSetStateMessage(ancestor.MessageName))
                {
                    ancestor = ancestor.ParentContext;
                    continue;
                }

                return false;
            }

            return false;
        }

        private static string GetContextEntityLogicalName(IPluginExecutionContext ctx)
        {
            if (ctx == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(ctx.PrimaryEntityName)
                && !string.Equals(ctx.PrimaryEntityName, "none", StringComparison.OrdinalIgnoreCase))
            {
                return ctx.PrimaryEntityName;
            }

            if (ctx.InputParameters != null)
            {
                if (ctx.InputParameters.Contains("Target"))
                {
                    var entity = ctx.InputParameters["Target"] as Entity;
                    if (entity != null && !string.IsNullOrWhiteSpace(entity.LogicalName))
                    {
                        return entity.LogicalName;
                    }

                    var entityRef = ctx.InputParameters["Target"] as EntityReference;
                    if (entityRef != null && !string.IsNullOrWhiteSpace(entityRef.LogicalName))
                    {
                        return entityRef.LogicalName;
                    }
                }

                var singleTarget = GetSingleTargetEntityOrNull(ctx);
                if (singleTarget != null && !string.IsNullOrWhiteSpace(singleTarget.LogicalName))
                {
                    return singleTarget.LogicalName;
                }

                if (ctx.InputParameters.Contains("EntityMoniker"))
                {
                    var moniker = ctx.InputParameters["EntityMoniker"] as EntityReference;
                    if (moniker != null && !string.IsNullOrWhiteSpace(moniker.LogicalName))
                    {
                        return moniker.LogicalName;
                    }
                }
            }

            return null;
        }

        private static Guid GetContextRecordId(IPluginExecutionContext ctx)
        {
            if (ctx == null)
            {
                return Guid.Empty;
            }

            if (ctx.PrimaryEntityId != Guid.Empty)
            {
                return ctx.PrimaryEntityId;
            }

            if (ctx.InputParameters != null)
            {
                if (ctx.InputParameters.Contains("Target"))
                {
                    var entity = ctx.InputParameters["Target"] as Entity;
                    if (entity != null && entity.Id != Guid.Empty)
                    {
                        return entity.Id;
                    }

                    var entityRef = ctx.InputParameters["Target"] as EntityReference;
                    if (entityRef != null && entityRef.Id != Guid.Empty)
                    {
                        return entityRef.Id;
                    }
                }

                var singleTarget = GetSingleTargetEntityOrNull(ctx);
                if (singleTarget != null && singleTarget.Id != Guid.Empty)
                {
                    return singleTarget.Id;
                }

                if (ctx.InputParameters.Contains("EntityMoniker"))
                {
                    var moniker = ctx.InputParameters["EntityMoniker"] as EntityReference;
                    if (moniker != null && moniker.Id != Guid.Empty)
                    {
                        return moniker.Id;
                    }
                }
            }

            return Guid.Empty;
        }

        private static int? GetOptionSetInt(Entity entity, string attributeName)
        {
            var opt = entity.GetAttributeValue<OptionSetValue>(attributeName);
            return opt != null ? (int?)opt.Value : null;
        }
    }
}
