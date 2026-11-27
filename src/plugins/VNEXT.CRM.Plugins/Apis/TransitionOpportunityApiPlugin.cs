using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;
using VNEXT.CRM.Plugins.Services;

namespace VNEXT.CRM.Plugins.Apis
{
    public sealed class TransitionOpportunityApiPlugin : IPlugin
    {
        private const string CommandAdvance = "Advance";
        private const string CommandWon = "Won";
        private const string CommandLost = "Lost";
        private const int LineQueryPageSize = 5000;

        private static readonly string[] OpportunityColumns =
        {
            CrmSchema.Opportunity.StateCode,
            CrmSchema.Opportunity.StatusCode,
            CrmSchema.Opportunity.Stage,
            CrmSchema.Opportunity.Probability,
            CrmSchema.Opportunity.CommandSequence,
            CrmSchema.Opportunity.Account,
            CrmSchema.Opportunity.PrimaryContact,
            CrmSchema.Opportunity.Need,
            CrmSchema.Opportunity.DecisionMaker,
            CrmSchema.Opportunity.ProposalSummary,
            CrmSchema.Opportunity.NextStep,
            CrmSchema.Opportunity.ExpectedCloseDate,
            CrmSchema.Opportunity.NextFollowUpDate,
            CrmSchema.Opportunity.Total,
            CrmSchema.Opportunity.MaxDiscount,
            CrmSchema.Opportunity.CurrentApproval,
            CrmSchema.Opportunity.OwnerId,
            CrmSchema.Common.CreatedOn
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

            ConcurrencyService.EnsureSynchronousTransaction(context);

            var correlationId = context.CorrelationId;
            if (tracing != null)
            {
                tracing.Trace("EXT11 TransitionOpportunityApiPlugin started. CorrelationId={0:D}", correlationId);
            }

            var targetRef = context.InputParameters != null && context.InputParameters.Contains(CrmSchema.Apis.Params.Target)
                ? context.InputParameters[CrmSchema.Apis.Params.Target] as EntityReference
                : null;

            if (targetRef == null
                || !string.Equals(targetRef.LogicalName, CrmSchema.Entities.Opportunity, StringComparison.OrdinalIgnoreCase)
                || targetRef.Id == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM006, "A valid Opportunity Target reference is required for vn_TransitionOpportunity.", correlationId);
            }

            var oppId = targetRef.Id;
            var rawCommand = GetOptionalStringInput(context, CrmSchema.Apis.Params.Command);
            var command = rawCommand != null ? rawCommand.Trim() : string.Empty;
            if (!string.Equals(command, CommandAdvance, StringComparison.Ordinal)
                && !string.Equals(command, CommandWon, StringComparison.Ordinal)
                && !string.Equals(command, CommandLost, StringComparison.Ordinal))
            {
                CrmError.Throw(CrmError.CRM006, "Command must be Advance, Won, or Lost.", correlationId);
            }

            var rawRowVersion = GetOptionalStringInput(context, CrmSchema.Apis.Params.ExpectedRowVersion);
            var normalizedRowVersion = ConcurrencyService.NormalizeRowVersion(rawRowVersion, correlationId);

            var targetStage = GetOptionalIntInput(context, CrmSchema.Apis.Params.TargetStage);
            var comments = GetOptionalStringInput(context, CrmSchema.Apis.Params.Comments);
            var actualCloseDate = GetOptionalDateTimeInput(context, CrmSchema.Apis.Params.ActualCloseDate);
            var lossReason = GetOptionalIntInput(context, CrmSchema.Apis.Params.LossReason);

            var initiatingUserId = AuthorizationService.RequireInitiatingUserId(context);
            var callerService = AuthorizationService.CreateCallerService(factory, context);
            var config = CrmPolicyConfigLoader.Load(callerService, correlationId);
            var utcNow = UtcClock.EnsureUtc(DateTime.UtcNow);

            Entity opp;
            try
            {
                opp = callerService.Retrieve(CrmSchema.Entities.Opportunity, oppId, new ColumnSet(OpportunityColumns));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM013, "Opportunity is inaccessible or does not exist.", correlationId);
                return;
            }

            if (opp == null)
            {
                CrmError.Throw(CrmError.CRM013, "Opportunity is inaccessible or does not exist.", correlationId);
            }

            AuthorizationService.EnsureAccessRights(
                callerService,
                initiatingUserId,
                new EntityReference(CrmSchema.Entities.Opportunity, oppId),
                AccessRights.ReadAccess | AccessRights.WriteAccess,
                CrmError.CRM013,
                "Caller lacks Write permission to transition this Opportunity.",
                correlationId);

            var stateCode = GetOptionSetInt(opp, CrmSchema.Opportunity.StateCode) ?? -1;
            var statusCode = GetOptionSetInt(opp, CrmSchema.Opportunity.StatusCode) ?? -1;
            var currentStage = GetOptionSetInt(opp, CrmSchema.Opportunity.Stage) ?? -1;

            if (stateCode != CrmSchema.Opportunity.State.Active || statusCode != CrmSchema.Opportunity.Status.Open)
            {
                var closedError = string.Equals(command, CommandAdvance, StringComparison.Ordinal)
                    ? CrmError.CRM006
                    : CrmError.CRM014;
                CrmError.Throw(closedError, "Closed Opportunities cannot be transitioned.", correlationId);
            }

            if (!string.IsNullOrWhiteSpace(opp.RowVersion)
                && !string.Equals(opp.RowVersion, normalizedRowVersion, StringComparison.Ordinal))
            {
                CrmError.Throw(
                    CrmError.CRM021,
                    "Opportunity was modified by another operation. Refresh the record and retry.",
                    correlationId);
            }

            int nextStage = currentStage;
            DateTime createdOnUtc = DateTime.MinValue;

            if (string.Equals(command, CommandAdvance, StringComparison.Ordinal))
            {
                nextStage = OpportunityLifecycleRules.GetNextAdjacentStage(currentStage, targetStage, correlationId);
            }
            else
            {
                var createdOn = opp.GetAttributeValue<DateTime?>(CrmSchema.Common.CreatedOn);
                if (!createdOn.HasValue)
                {
                    CrmError.Throw(CrmError.CRM014, "Opportunity creation date is unavailable for close validation.", correlationId);
                }

                createdOnUtc = UtcClock.EnsureUtc(createdOn.Value);

                if (string.Equals(command, CommandWon, StringComparison.Ordinal))
                {
                    if (currentStage != CrmSchema.Opportunity.StageValues.Close)
                    {
                        CrmError.Throw(CrmError.CRM014, "Opportunity must be in the Close stage to close as Won.", correlationId);
                    }

                    OpportunityLifecycleRules.ValidateCloseDateAndComments(actualCloseDate, createdOnUtc, utcNow, comments, correlationId);
                }
                else
                {
                    OpportunityLifecycleRules.ValidateLostEligibility(
                        stateCode,
                        statusCode,
                        lossReason,
                        actualCloseDate,
                        createdOnUtc,
                        utcNow,
                        comments,
                        correlationId);
                }
            }

            // Pre-validate Scoped Internal Writer BEFORE any mutation
            var writerService = AuthorizationService.CreateInternalWriterService(factory, config, correlationId);

            // First synchronous parent coordination latch (EXT02, §6.3)
            var currentCommandSequence = opp.GetAttributeValue<int?>(CrmSchema.Opportunity.CommandSequence) ?? 0;
            var nextSequence = ConcurrencyService.BumpOpportunityCommandSequence(
                callerService,
                oppId,
                normalizedRowVersion,
                currentCommandSequence,
                correlationId);

            if (string.Equals(command, CommandAdvance, StringComparison.Ordinal))
            {
                ExecuteAdvance(context, callerService, writerService, opp, oppId, nextStage, nextSequence, utcNow, correlationId);
            }
            else if (string.Equals(command, CommandWon, StringComparison.Ordinal))
            {
                ExecuteWon(context, callerService, writerService, opp, oppId, stateCode, statusCode, currentStage, actualCloseDate, createdOnUtc, comments, nextSequence, utcNow, config, correlationId);
            }
            else
            {
                ExecuteLost(context, callerService, writerService, oppId, stateCode, statusCode, currentStage, lossReason, actualCloseDate, createdOnUtc, comments, nextSequence, utcNow, correlationId);
            }
        }

        private static void ExecuteAdvance(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            IOrganizationService writerService,
            Entity opp,
            Guid oppId,
            int nextStage,
            int nextSequence,
            DateTime utcNow,
            Guid correlationId)
        {
            var hasActiveMatchingContact = CheckHasActiveMatchingContact(
                callerService,
                opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.Account),
                opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.PrimaryContact));

            int lineCount = 0;
            decimal total = 0m;
            if (nextStage == CrmSchema.Opportunity.StageValues.Propose
                || nextStage == CrmSchema.Opportunity.StageValues.Close)
            {
                var totals = QueryActiveLines(callerService, oppId, correlationId);
                lineCount = totals.LineCount;
                total = totals.Total;
            }

            OpportunityLifecycleRules.ValidateStageAdvanceReadiness(
                nextStage,
                opp.GetAttributeValue<string>(CrmSchema.Opportunity.Need),
                opp.GetAttributeValue<string>(CrmSchema.Opportunity.DecisionMaker),
                hasActiveMatchingContact,
                opp.GetAttributeValue<string>(CrmSchema.Opportunity.NextStep),
                lineCount,
                total,
                opp.GetAttributeValue<string>(CrmSchema.Opportunity.ProposalSummary),
                opp.GetAttributeValue<DateTime?>(CrmSchema.Opportunity.ExpectedCloseDate),
                correlationId);

            var newProbability = OpportunityLifecycleRules.GetProbabilityForOpenStage(nextStage, correlationId);

            var patch = new Entity(CrmSchema.Entities.Opportunity, oppId);
            patch[CrmSchema.Opportunity.Stage] = new OptionSetValue(nextStage);
            patch[CrmSchema.Opportunity.Probability] = newProbability;
            patch[CrmSchema.Opportunity.StageEnteredOn] = utcNow;
            patch[CrmSchema.Opportunity.LastProgressOn] = utcNow;
            callerService.Update(patch);

            BpfMirrorService.MirrorStageAdvance(writerService, oppId, nextStage, correlationId);

            SetOutputs(context, nextStage, CrmSchema.Opportunity.Status.Open, nextSequence, correlationId);
        }

        private static void ExecuteWon(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            IOrganizationService writerService,
            Entity opp,
            Guid oppId,
            int stateCode,
            int statusCode,
            int currentStage,
            DateTime? actualCloseDate,
            DateTime createdOnUtc,
            string comments,
            int nextSequence,
            DateTime utcNow,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            var totals = QueryActiveLines(callerService, oppId, correlationId);

            var storedTotalMoney = opp.GetAttributeValue<Money>(CrmSchema.Opportunity.Total);
            var storedTotal = storedTotalMoney != null ? storedTotalMoney.Value : 0m;
            var storedMaxDiscount = opp.GetAttributeValue<decimal?>(CrmSchema.Opportunity.MaxDiscount) ?? 0m;

            if (storedTotal != totals.Total || storedMaxDiscount != totals.MaxDiscount)
            {
                CrmError.Throw(
                    CrmError.CRM009,
                    "Stored Opportunity totals do not match current active Opportunity Lines.",
                    correlationId);
            }

            OpportunityLifecycleRules.ValidateWonEligibility(
                stateCode,
                statusCode,
                currentStage,
                totals.LineCount,
                totals.Total,
                actualCloseDate,
                createdOnUtc,
                utcNow,
                comments,
                correlationId);

            ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                writerService,
                opp,
                totals.Total,
                totals.MaxDiscount,
                config,
                correlationId);

            var patch = new Entity(CrmSchema.Entities.Opportunity, oppId);
            patch[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Inactive);
            patch[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Won);
            patch[CrmSchema.Opportunity.Probability] = CrmSchema.Opportunity.Probabilities.Won;
            patch[CrmSchema.Opportunity.ActualCloseDate] = UtcClock.NormalizeDateOnly(actualCloseDate.Value);
            patch[CrmSchema.Opportunity.ClosedAmount] = new Money(totals.Total);
            patch[CrmSchema.Opportunity.CloseComments] = comments.Trim();
            patch[CrmSchema.Opportunity.LastProgressOn] = utcNow;
            callerService.Update(patch);

            BpfMirrorService.MirrorClose(writerService, oppId, isWon: true, correlationId: correlationId);

            SetOutputs(context, currentStage, CrmSchema.Opportunity.Status.Won, nextSequence, correlationId);
        }

        private static void ExecuteLost(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            IOrganizationService writerService,
            Guid oppId,
            int stateCode,
            int statusCode,
            int currentStage,
            int? lossReason,
            DateTime? actualCloseDate,
            DateTime createdOnUtc,
            string comments,
            int nextSequence,
            DateTime utcNow,
            Guid correlationId)
        {
            OpportunityLifecycleRules.ValidateLostEligibility(
                stateCode,
                statusCode,
                lossReason,
                actualCloseDate,
                createdOnUtc,
                utcNow,
                comments,
                correlationId);

            var patch = new Entity(CrmSchema.Entities.Opportunity, oppId);
            patch[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Inactive);
            patch[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Lost);
            patch[CrmSchema.Opportunity.Probability] = CrmSchema.Opportunity.Probabilities.Lost;
            patch[CrmSchema.Opportunity.ActualCloseDate] = UtcClock.NormalizeDateOnly(actualCloseDate.Value);
            patch[CrmSchema.Opportunity.ClosedAmount] = new Money(0m);
            patch[CrmSchema.Opportunity.CloseComments] = comments.Trim();
            patch[CrmSchema.Opportunity.LossReason] = new OptionSetValue(lossReason.Value);
            patch[CrmSchema.Opportunity.LastProgressOn] = utcNow;
            callerService.Update(patch);

            BpfMirrorService.MirrorClose(writerService, oppId, isWon: false, correlationId: correlationId);

            SetOutputs(context, currentStage, CrmSchema.Opportunity.Status.Lost, nextSequence, correlationId);
        }

        private static bool CheckHasActiveMatchingContact(
            IOrganizationService callerService,
            EntityReference oppAccountRef,
            EntityReference oppContactRef)
        {
            if (oppAccountRef == null
                || oppAccountRef.Id == Guid.Empty
                || oppContactRef == null
                || oppContactRef.Id == Guid.Empty)
            {
                return false;
            }

            try
            {
                var account = callerService.Retrieve(
                    CrmSchema.Entities.Account,
                    oppAccountRef.Id,
                    new ColumnSet(CrmSchema.Account.StateCode));
                if (account == null || GetOptionSetInt(account, CrmSchema.Account.StateCode) != CrmSchema.Account.State.Active)
                {
                    return false;
                }

                var contact = callerService.Retrieve(
                    CrmSchema.Entities.Contact,
                    oppContactRef.Id,
                    new ColumnSet(CrmSchema.Contact.StateCode, CrmSchema.Contact.ParentCustomerId));
                if (contact == null || GetOptionSetInt(contact, CrmSchema.Contact.StateCode) != CrmSchema.Contact.State.Active)
                {
                    return false;
                }

                var parentRef = contact.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);
                return parentRef != null
                    && string.Equals(parentRef.LogicalName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase)
                    && parentRef.Id == oppAccountRef.Id;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                return false;
            }
        }

        private static OpportunityTotalsResult QueryActiveLines(
            IOrganizationService callerService,
            Guid oppId,
            Guid correlationId)
        {
            var query = new QueryExpression(CrmSchema.Entities.OpportunityLine)
            {
                ColumnSet = new ColumnSet(
                    CrmSchema.OpportunityLine.NetAmount,
                    CrmSchema.OpportunityLine.DiscountPercent),
                PageInfo = new PagingInfo
                {
                    Count = LineQueryPageSize,
                    PageNumber = 1
                }
            };

            query.Criteria.AddCondition(CrmSchema.OpportunityLine.Opportunity, ConditionOperator.Equal, oppId);
            query.Criteria.AddCondition(CrmSchema.OpportunityLine.StateCode, ConditionOperator.Equal, CrmSchema.OpportunityLine.State.Active);

            var items = new List<LineAggregateItem>();

            while (true)
            {
                var page = callerService.RetrieveMultiple(query);
                if (page != null && page.Entities != null)
                {
                    foreach (var line in page.Entities)
                    {
                        var netMoney = line.GetAttributeValue<Money>(CrmSchema.OpportunityLine.NetAmount);
                        var discount = line.GetAttributeValue<decimal?>(CrmSchema.OpportunityLine.DiscountPercent);
                        if (netMoney == null || !discount.HasValue)
                        {
                            CrmError.Throw(
                                CrmError.CRM009,
                                "An Opportunity Line is missing authoritative server pricing amounts.",
                                correlationId);
                        }

                        items.Add(new LineAggregateItem(netMoney.Value, discount.Value));
                    }
                }

                if (page == null || !page.MoreRecords)
                {
                    break;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            return DomainPricing.CalculateOpportunityTotals(items, correlationId);
        }

        private static void SetOutputs(
            IPluginExecutionContext context,
            int stage,
            int statusReason,
            int commandSequence,
            Guid correlationId)
        {
            context.OutputParameters[CrmSchema.Apis.Params.Stage] = stage;
            context.OutputParameters[CrmSchema.Apis.Params.StatusReason] = statusReason;
            context.OutputParameters[CrmSchema.Apis.Params.CommandSequence] = commandSequence;
            context.OutputParameters[CrmSchema.Apis.Params.CorrelationId] = correlationId.ToString("D");
        }

        private static string GetOptionalStringInput(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(paramName))
            {
                return context.InputParameters[paramName] as string;
            }

            return null;
        }

        private static int? GetOptionalIntInput(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters == null || !context.InputParameters.Contains(paramName))
            {
                return null;
            }

            var raw = context.InputParameters[paramName];
            if (raw is int)
            {
                var val = (int)raw;
                return val == 0 ? (int?)null : val;
            }

            var opt = raw as OptionSetValue;
            if (opt != null)
            {
                return opt.Value == 0 ? (int?)null : opt.Value;
            }

            return null;
        }

        private static DateTime? GetOptionalDateTimeInput(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(paramName))
            {
                var raw = context.InputParameters[paramName];
                if (raw is DateTime)
                {
                    var dt = (DateTime)raw;
                    return dt == DateTime.MinValue ? (DateTime?)null : dt;
                }
            }

            return null;
        }

        private static int? GetOptionSetInt(Entity entity, string attributeName)
        {
            var opt = entity.GetAttributeValue<OptionSetValue>(attributeName);
            return opt != null ? (int?)opt.Value : null;
        }
    }
}
