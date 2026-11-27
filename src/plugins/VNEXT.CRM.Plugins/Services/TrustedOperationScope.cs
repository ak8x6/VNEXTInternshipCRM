using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using VNEXT.CRM.Plugins.Core;

namespace VNEXT.CRM.Plugins.Services
{
    public sealed class TrustedApiScope
    {
        public string ApiMessage { get; }
        public string BoundEntityLogicalName { get; }
        public Guid BoundEntityId { get; }
        public string SubOperation { get; }
        public Guid InitiatingUserId { get; }
        public Guid CorrelationId { get; }

        public TrustedApiScope(
            string apiMessage,
            string boundEntityLogicalName,
            Guid boundEntityId,
            string subOperation,
            Guid initiatingUserId,
            Guid correlationId)
        {
            ApiMessage = apiMessage;
            BoundEntityLogicalName = boundEntityLogicalName;
            BoundEntityId = boundEntityId;
            SubOperation = subOperation;
            InitiatingUserId = initiatingUserId;
            CorrelationId = correlationId;
        }
    }

    public static class TrustedOperationScope
    {
        private const int MainOperationStage = 30;
        private const int MaxParentChainHops = 32;

        public static bool TryResolveFromParentChain(
            IPluginExecutionContext nestedContext,
            out TrustedApiScope scope)
        {
            scope = null;

            if (nestedContext == null
                || nestedContext.Mode != 0
                || !nestedContext.IsInTransaction
                || nestedContext.InitiatingUserId == Guid.Empty)
            {
                return false;
            }

            var ancestor = nestedContext.ParentContext;
            var hops = 0;

            while (ancestor != null && hops < MaxParentChainHops)
            {
                hops++;

                string expectedBoundEntity;
                string subOperationParamName;
                if (!TryGetRecognizedApiSpec(ancestor.MessageName, out expectedBoundEntity, out subOperationParamName))
                {
                    ancestor = ancestor.ParentContext;
                    continue;
                }

                if (ancestor.Mode != 0
                    || !ancestor.IsInTransaction
                    || ancestor.Stage != MainOperationStage)
                {
                    return false;
                }

                if (ancestor.CorrelationId != nestedContext.CorrelationId)
                {
                    return false;
                }

                if (ancestor.InitiatingUserId == Guid.Empty
                    || ancestor.InitiatingUserId != nestedContext.InitiatingUserId)
                {
                    return false;
                }

                if (ancestor.InputParameters == null
                    || !ancestor.InputParameters.Contains(CrmSchema.Apis.Params.Target))
                {
                    return false;
                }

                var targetRef = ancestor.InputParameters[CrmSchema.Apis.Params.Target] as EntityReference;
                if (targetRef == null
                    || targetRef.Id == Guid.Empty
                    || !string.Equals(targetRef.LogicalName, expectedBoundEntity, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string subOperation = null;
                if (subOperationParamName != null && ancestor.InputParameters.Contains(subOperationParamName))
                {
                    var rawSubOp = ancestor.InputParameters[subOperationParamName] as string;
                    if (!string.IsNullOrWhiteSpace(rawSubOp))
                    {
                        subOperation = rawSubOp.Trim();
                    }
                }

                scope = new TrustedApiScope(
                    ancestor.MessageName,
                    expectedBoundEntity,
                    targetRef.Id,
                    subOperation,
                    ancestor.InitiatingUserId,
                    ancestor.CorrelationId);
                return true;
            }

            return false;
        }

        public static bool IsExecutedByConfiguredInternalWriter(
            IPluginExecutionContext context,
            CrmPolicyConfig config)
        {
            if (context == null || config == null)
            {
                return false;
            }

            return config.InternalWriterUserId.HasValue
                && config.InternalWriterUserId.Value != Guid.Empty
                && context.UserId == config.InternalWriterUserId.Value
                && context.InitiatingUserId != Guid.Empty;
        }

        public static bool AreAllAttributesAllowed(Entity target, ICollection<string> allowlist)
        {
            if (target == null || allowlist == null)
            {
                return false;
            }

            var allowed = new HashSet<string>(allowlist, StringComparer.OrdinalIgnoreCase);
            foreach (var key in target.Attributes.Keys)
            {
                if (!allowed.Contains(key))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetRecognizedApiSpec(
            string messageName,
            out string expectedBoundEntity,
            out string subOperationParamName)
        {
            expectedBoundEntity = null;
            subOperationParamName = null;

            if (string.Equals(messageName, CrmSchema.Apis.QualifyLead, StringComparison.Ordinal))
            {
                expectedBoundEntity = CrmSchema.Entities.Lead;
                subOperationParamName = null;
                return true;
            }

            if (string.Equals(messageName, CrmSchema.Apis.TransitionOpportunity, StringComparison.Ordinal))
            {
                expectedBoundEntity = CrmSchema.Entities.Opportunity;
                subOperationParamName = CrmSchema.Apis.Params.Command;
                return true;
            }

            if (string.Equals(messageName, CrmSchema.Apis.EditOpportunityLine, StringComparison.Ordinal))
            {
                expectedBoundEntity = CrmSchema.Entities.Opportunity;
                subOperationParamName = CrmSchema.Apis.Params.Operation;
                return true;
            }

            if (string.Equals(messageName, CrmSchema.Apis.ManageDealApproval, StringComparison.Ordinal))
            {
                expectedBoundEntity = CrmSchema.Entities.Opportunity;
                subOperationParamName = CrmSchema.Apis.Params.Operation;
                return true;
            }

            return false;
        }
    }
}
