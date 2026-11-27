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
    public sealed class EditOpportunityLineApiPlugin : IPlugin
    {
        private const string OperationAdd = "Add";
        private const string OperationRemove = "Remove";
        private const int ObjectDoesNotExistErrorCode = -2147220969; // 0x80040217
        private const int DuplicateKeyErrorCode1 = -2147088238;      // 0x80060892
        private const int DuplicateKeyErrorCode2 = -2147220937;      // 0x80040237
        private const int LineQueryPageSize = 5000;
        private const int MaxLineNameLength = 100;

        private static readonly string[] OpportunityColumns =
        {
            CrmSchema.Opportunity.StateCode,
            CrmSchema.Opportunity.StatusCode,
            CrmSchema.Opportunity.Stage,
            CrmSchema.Opportunity.CommandSequence,
            CrmSchema.Opportunity.Total,
            CrmSchema.Opportunity.MaxDiscount,
            CrmSchema.Opportunity.CurrentApproval,
            CrmSchema.Opportunity.OwnerId,
            CrmSchema.Opportunity.TransactionCurrencyId
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
                tracing.Trace("EXT12 EditOpportunityLineApiPlugin started. CorrelationId={0:D}", correlationId);
            }

            var targetRef = context.InputParameters != null && context.InputParameters.Contains(CrmSchema.Apis.Params.Target)
                ? context.InputParameters[CrmSchema.Apis.Params.Target] as EntityReference
                : null;

            if (targetRef == null
                || !string.Equals(targetRef.LogicalName, CrmSchema.Entities.Opportunity, StringComparison.OrdinalIgnoreCase)
                || targetRef.Id == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM006, "A valid Opportunity Target reference is required for vn_EditOpportunityLine.", correlationId);
            }

            var oppId = targetRef.Id;
            var rawOperation = GetOptionalStringInput(context, CrmSchema.Apis.Params.Operation);
            var operation = rawOperation != null ? rawOperation.Trim() : string.Empty;
            if (!string.Equals(operation, OperationAdd, StringComparison.Ordinal)
                && !string.Equals(operation, OperationRemove, StringComparison.Ordinal))
            {
                CrmError.Throw(CrmError.CRM010, "Operation must be Add or Remove.", correlationId);
            }

            var rawRowVersion = GetOptionalStringInput(context, CrmSchema.Apis.Params.ExpectedRowVersion);
            var normalizedRowVersion = ConcurrencyService.NormalizeRowVersion(rawRowVersion, correlationId);

            var lineId = GetOptionalGuidInput(context, CrmSchema.Apis.Params.LineId, CrmError.CRM010, correlationId);
            var productId = GetOptionalGuidInput(context, CrmSchema.Apis.Params.ProductId, CrmError.CRM008, correlationId);
            var quantity = GetOptionalDecimalInput(context, CrmSchema.Apis.Params.Quantity);
            var discountPercent = GetOptionalDecimalInput(context, CrmSchema.Apis.Params.DiscountPercent);

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
                "Caller lacks Write permission on this Opportunity.",
                correlationId);

            var stateCode = GetOptionSetInt(opp, CrmSchema.Opportunity.StateCode) ?? -1;
            var statusCode = GetOptionSetInt(opp, CrmSchema.Opportunity.StatusCode) ?? -1;
            if (stateCode != CrmSchema.Opportunity.State.Active || statusCode != CrmSchema.Opportunity.Status.Open)
            {
                CrmError.Throw(CrmError.CRM006, "Opportunity Lines cannot be modified on a closed Opportunity.", correlationId);
            }

            // Pre-validate Scoped Internal Writer BEFORE any mutation
            var writerService = AuthorizationService.CreateInternalWriterService(factory, config, correlationId);

            if (string.Equals(operation, OperationAdd, StringComparison.Ordinal))
            {
                ExecuteAdd(
                    context,
                    callerService,
                    writerService,
                    opp,
                    oppId,
                    initiatingUserId,
                    normalizedRowVersion,
                    lineId,
                    productId,
                    quantity,
                    discountPercent,
                    config,
                    utcNow,
                    correlationId);
            }
            else
            {
                ExecuteRemove(
                    context,
                    callerService,
                    writerService,
                    opp,
                    oppId,
                    normalizedRowVersion,
                    lineId,
                    utcNow,
                    correlationId);
            }
        }

        private static void ExecuteAdd(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            IOrganizationService writerService,
            Entity opp,
            Guid oppId,
            Guid initiatingUserId,
            string normalizedRowVersion,
            Guid? lineId,
            Guid? productId,
            decimal? quantity,
            decimal? discountPercent,
            CrmPolicyConfig config,
            DateTime utcNow,
            Guid correlationId)
        {
            if (!lineId.HasValue || lineId.Value == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM010, "A valid client-generated LineId GUID is required for Add.", correlationId);
            }

            if (!productId.HasValue || productId.Value == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM008, "A valid ProductId is required to add an Opportunity Line.", correlationId);
            }

            if (!quantity.HasValue)
            {
                CrmError.Throw(CrmError.CRM008, "Quantity is required to add an Opportunity Line.", correlationId);
            }

            var effectiveDiscount = discountPercent ?? 0m;
            var currentCommandSequence = opp.GetAttributeValue<int?>(CrmSchema.Opportunity.CommandSequence) ?? 0;

            // Idempotent Add check BEFORE RowVersion equality check using required client-generated LineId
            Entity existingLine = null;
            try
            {
                existingLine = callerService.Retrieve(
                    CrmSchema.Entities.OpportunityLine,
                    lineId.Value,
                    new ColumnSet(
                        CrmSchema.OpportunityLine.Opportunity,
                        CrmSchema.OpportunityLine.Product,
                        CrmSchema.OpportunityLine.Quantity,
                        CrmSchema.OpportunityLine.DiscountPercent,
                        CrmSchema.OpportunityLine.StateCode));
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                if (ex.Detail == null || ex.Detail.ErrorCode != ObjectDoesNotExistErrorCode)
                {
                    CrmError.Throw(CrmError.CRM010, "Supplied LineId could not be verified for idempotent retry.", correlationId);
                }
            }

            if (existingLine != null)
            {
                var existingOpp = existingLine.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.Opportunity);
                var existingProduct = existingLine.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.Product);
                var existingQty = existingLine.GetAttributeValue<decimal?>(CrmSchema.OpportunityLine.Quantity);
                var existingDiscount = existingLine.GetAttributeValue<decimal?>(CrmSchema.OpportunityLine.DiscountPercent);
                var existingState = GetOptionSetInt(existingLine, CrmSchema.OpportunityLine.StateCode);

                if (existingOpp == null
                    || existingOpp.Id != oppId
                    || existingState != CrmSchema.OpportunityLine.State.Active
                    || existingProduct == null
                    || existingProduct.Id != productId.Value
                    || !existingQty.HasValue
                    || existingQty.Value != quantity.Value
                    || !existingDiscount.HasValue
                    || existingDiscount.Value != effectiveDiscount)
                {
                    CrmError.Throw(
                        CrmError.CRM010,
                        "Supplied LineId already exists with a different parent Opportunity or line specification.",
                        correlationId);
                }

                var idempotentTotals = QueryActiveLines(callerService, oppId, correlationId);
                SetOutputs(context, lineId.Value, idempotentTotals.Total, wasAlreadyApplied: true, correlationId: correlationId);
                return;
            }

            // RowVersion fast-fail
            if (!string.IsNullOrWhiteSpace(opp.RowVersion)
                && !string.Equals(opp.RowVersion, normalizedRowVersion, StringComparison.Ordinal))
            {
                CrmError.Throw(
                    CrmError.CRM021,
                    "Opportunity was modified by another operation. Refresh the record and retry.",
                    correlationId);
            }

            // Retrieve & validate Active Product snapshot
            Entity product;
            try
            {
                product = callerService.Retrieve(
                    CrmSchema.Entities.Product,
                    productId.Value,
                    new ColumnSet(
                        CrmSchema.Product.StateCode,
                        CrmSchema.Product.Name,
                        CrmSchema.Product.Sku,
                        CrmSchema.Product.UnitPrice,
                        CrmSchema.Product.TransactionCurrencyId));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM008, "Selected Product is inaccessible or does not exist.", correlationId);
                return;
            }

            if (product == null || GetOptionSetInt(product, CrmSchema.Product.StateCode) != CrmSchema.Product.State.Active)
            {
                CrmError.Throw(CrmError.CRM008, "Only an Active Product can be added to an Opportunity.", correlationId);
            }

            var rawSku = product.GetAttributeValue<string>(CrmSchema.Product.Sku);
            var rawName = product.GetAttributeValue<string>(CrmSchema.Product.Name);
            var unitPriceMoney = product.GetAttributeValue<Money>(CrmSchema.Product.UnitPrice);
            var productCurrencyRef = product.GetAttributeValue<EntityReference>(CrmSchema.Product.TransactionCurrencyId);

            if (string.IsNullOrWhiteSpace(rawSku) || string.IsNullOrWhiteSpace(rawName) || unitPriceMoney == null)
            {
                CrmError.Throw(CrmError.CRM008, "Selected Product is missing required SKU, Name, or Unit Price.", correlationId);
            }

            DomainPricing.ValidateBaseCurrency(
                productCurrencyRef != null ? (Guid?)productCurrencyRef.Id : null,
                config,
                correlationId);

            var snapshotSku = rawSku.Trim();
            var snapshotName = rawName.Trim();
            var snapshotUnitPrice = unitPriceMoney.Value;

            var pricing = DomainPricing.CalculateLine(
                quantity.Value,
                snapshotUnitPrice,
                effectiveDiscount,
                config,
                correlationId);

            // First synchronous parent coordination latch (EXT02)
            ConcurrencyService.BumpOpportunityCommandSequence(
                callerService,
                oppId,
                normalizedRowVersion,
                currentCommandSequence,
                correlationId);

            // Create immutable snapshot Line with required client-generated LineId
            var lineName = string.Format("{0} - {1}", snapshotSku, snapshotName).Trim();
            if (lineName.Length > MaxLineNameLength)
            {
                lineName = lineName.Substring(0, MaxLineNameLength);
            }

            var lineEntity = new Entity(CrmSchema.Entities.OpportunityLine, lineId.Value);

            lineEntity[CrmSchema.OpportunityLine.Name] = lineName;
            lineEntity[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, oppId);
            lineEntity[CrmSchema.OpportunityLine.Product] = new EntityReference(CrmSchema.Entities.Product, productId.Value);
            lineEntity[CrmSchema.OpportunityLine.SnapshotSku] = snapshotSku;
            lineEntity[CrmSchema.OpportunityLine.SnapshotName] = snapshotName;
            lineEntity[CrmSchema.OpportunityLine.UnitPrice] = new Money(snapshotUnitPrice);
            lineEntity[CrmSchema.OpportunityLine.Quantity] = quantity.Value;
            lineEntity[CrmSchema.OpportunityLine.DiscountPercent] = effectiveDiscount;
            lineEntity[CrmSchema.OpportunityLine.GrossAmount] = new Money(pricing.GrossAmount);
            lineEntity[CrmSchema.OpportunityLine.DiscountAmount] = new Money(pricing.DiscountAmount);
            lineEntity[CrmSchema.OpportunityLine.NetAmount] = new Money(pricing.NetAmount);
            lineEntity[CrmSchema.OpportunityLine.TransactionCurrencyId] = new EntityReference(
                CrmSchema.Entities.TransactionCurrency,
                config.BaseCurrencyId);

            Guid createdLineId;
            try
            {
                createdLineId = callerService.Create(lineEntity);
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                if (IsDuplicateIdFault(ex))
                {
                    CrmError.Throw(
                        CrmError.CRM021,
                        "Line creation conflicted with a concurrent write using the same LineId. Refresh and retry.",
                        correlationId);
                }

                throw;
            }

            var oppOwnerRef = opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OwnerId);
            if (oppOwnerRef != null && oppOwnerRef.Id != Guid.Empty && oppOwnerRef.Id != initiatingUserId)
            {
                var ownerPatch = new Entity(CrmSchema.Entities.OpportunityLine, createdLineId);
                ownerPatch[CrmSchema.OpportunityLine.OwnerId] = oppOwnerRef;
                callerService.Update(ownerPatch);
            }

            // Recalculate authoritative Opportunity totals & update parent
            var totals = QueryActiveLines(callerService, oppId, correlationId);
            var oppTotalsPatch = new Entity(CrmSchema.Entities.Opportunity, oppId);
            oppTotalsPatch[CrmSchema.Opportunity.Total] = new Money(totals.Total);
            oppTotalsPatch[CrmSchema.Opportunity.MaxDiscount] = totals.MaxDiscount;
            oppTotalsPatch[CrmSchema.Opportunity.LastProgressOn] = utcNow;
            callerService.Update(oppTotalsPatch);

            // Atomically invalidate current approval pointer (BR19 & R-02)
            var currentApprovalRef = opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.CurrentApproval);
            ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(writerService, oppId, currentApprovalRef, correlationId);

            SetOutputs(context, createdLineId, totals.Total, wasAlreadyApplied: false, correlationId: correlationId);
        }

        private static void ExecuteRemove(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            IOrganizationService writerService,
            Entity opp,
            Guid oppId,
            string normalizedRowVersion,
            Guid? lineId,
            DateTime utcNow,
            Guid correlationId)
        {
            if (!lineId.HasValue || lineId.Value == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM010, "A valid LineId is required to remove an Opportunity Line.", correlationId);
            }

            // RowVersion fast-fail
            if (!string.IsNullOrWhiteSpace(opp.RowVersion)
                && !string.Equals(opp.RowVersion, normalizedRowVersion, StringComparison.Ordinal))
            {
                CrmError.Throw(
                    CrmError.CRM021,
                    "Opportunity was modified by another operation. Refresh the record and retry.",
                    correlationId);
            }

            var currentCommandSequence = opp.GetAttributeValue<int?>(CrmSchema.Opportunity.CommandSequence) ?? 0;

            // First synchronous parent coordination latch (EXT02)
            ConcurrencyService.BumpOpportunityCommandSequence(
                callerService,
                oppId,
                normalizedRowVersion,
                currentCommandSequence,
                correlationId);

            // Verify Line ownership & delete (fail-closed)
            Entity existingLine;
            try
            {
                existingLine = callerService.Retrieve(
                    CrmSchema.Entities.OpportunityLine,
                    lineId.Value,
                    new ColumnSet(CrmSchema.OpportunityLine.Opportunity, CrmSchema.OpportunityLine.StateCode));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM010, "Opportunity Line is inaccessible or does not exist.", correlationId);
                return;
            }

            var lineOppRef = existingLine != null
                ? existingLine.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.Opportunity)
                : null;
            var lineState = existingLine != null
                ? GetOptionSetInt(existingLine, CrmSchema.OpportunityLine.StateCode)
                : null;

            if (lineOppRef == null || lineOppRef.Id != oppId || lineState != CrmSchema.OpportunityLine.State.Active)
            {
                CrmError.Throw(CrmError.CRM010, "Opportunity Line does not belong to this active Opportunity.", correlationId);
            }

            callerService.Delete(CrmSchema.Entities.OpportunityLine, lineId.Value);

            // Recalculate totals & enforce BR12 post-progression guard
            var totals = QueryActiveLines(callerService, oppId, correlationId);
            var currentStage = GetOptionSetInt(opp, CrmSchema.Opportunity.Stage) ?? CrmSchema.Opportunity.StageValues.Qualify;
            if ((currentStage == CrmSchema.Opportunity.StageValues.Propose || currentStage == CrmSchema.Opportunity.StageValues.Close)
                && (totals.LineCount < 1 || totals.Total <= 0m))
            {
                CrmError.Throw(
                    CrmError.CRM007,
                    "Cannot remove the last line from an Opportunity in Propose or Close stage.",
                    correlationId);
            }

            var oppTotalsPatch = new Entity(CrmSchema.Entities.Opportunity, oppId);
            oppTotalsPatch[CrmSchema.Opportunity.Total] = new Money(totals.Total);
            oppTotalsPatch[CrmSchema.Opportunity.MaxDiscount] = totals.MaxDiscount;
            oppTotalsPatch[CrmSchema.Opportunity.LastProgressOn] = utcNow;
            callerService.Update(oppTotalsPatch);

            // Atomically invalidate current approval pointer (BR19 & R-02)
            var currentApprovalRef = opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.CurrentApproval);
            ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(writerService, oppId, currentApprovalRef, correlationId);

            SetOutputs(context, lineId.Value, totals.Total, wasAlreadyApplied: false, correlationId: correlationId);
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
            Guid lineId,
            decimal total,
            bool wasAlreadyApplied,
            Guid correlationId)
        {
            context.OutputParameters[CrmSchema.Apis.Params.LineId] = lineId;
            context.OutputParameters[CrmSchema.Apis.Params.Total] = total;
            context.OutputParameters[CrmSchema.Apis.Params.WasAlreadyApplied] = wasAlreadyApplied;
            context.OutputParameters[CrmSchema.Apis.Params.CorrelationId] = correlationId.ToString("D");
        }

        private static bool IsDuplicateIdFault(FaultException<OrganizationServiceFault> ex)
        {
            if (ex == null || ex.Detail == null)
            {
                return false;
            }

            return ex.Detail.ErrorCode == DuplicateKeyErrorCode1
                || ex.Detail.ErrorCode == DuplicateKeyErrorCode2;
        }

        private static string GetOptionalStringInput(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(paramName))
            {
                return context.InputParameters[paramName] as string;
            }

            return null;
        }

        private static Guid? GetOptionalGuidInput(IPluginExecutionContext context, string paramName, string errorCode, Guid correlationId)
        {
            if (context.InputParameters == null || !context.InputParameters.Contains(paramName))
            {
                return null;
            }

            var raw = context.InputParameters[paramName];
            if (raw == null)
            {
                return null;
            }

            if (raw is Guid)
            {
                var g = (Guid)raw;
                return g == Guid.Empty ? (Guid?)null : g;
            }

            var text = raw as string;
            if (text != null)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                Guid parsed;
                if (Guid.TryParse(text.Trim(), out parsed))
                {
                    return parsed;
                }

                CrmError.Throw(errorCode, string.Format("Parameter '{0}' is not a valid GUID.", paramName), correlationId);
            }

            return null;
        }

        private static decimal? GetOptionalDecimalInput(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters == null || !context.InputParameters.Contains(paramName))
            {
                return null;
            }

            var raw = context.InputParameters[paramName];
            if (raw == null)
            {
                return null;
            }

            if (raw is decimal) return (decimal)raw;
            if (raw is int) return (decimal)(int)raw;
            if (raw is double) return (decimal)(double)raw;
            return null;
        }

        private static int? GetOptionSetInt(Entity entity, string attributeName)
        {
            var opt = entity.GetAttributeValue<OptionSetValue>(attributeName);
            return opt != null ? (int?)opt.Value : null;
        }
    }
}
