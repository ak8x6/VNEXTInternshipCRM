using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;

namespace VNEXT.CRM.Plugins.Services
{
    public static class BpfMirrorService
    {
        private const string DefaultBpfName = "Opportunity Sales Process";
        private const int MaxBpfNameLength = 200;

        public static Guid EnsureSingleQualifyInstance(
            IOrganizationService writerService,
            Guid opportunityId,
            string opportunityName,
            Guid correlationId)
        {
            var instances = RetrieveInstances(writerService, opportunityId, correlationId);
            if (instances.Count > 1)
            {
                CrmError.Throw(
                    CrmError.CRM006,
                    "Multiple Opportunity Sales Process instances exist for the same Opportunity.",
                    correlationId);
            }

            var qualifyStageId = CrmSchema.Bpf.StageQualifyId;
            var qualifyPath = OpportunityLifecycleRules.BuildTraversedPathForStage(
                CrmSchema.Opportunity.StageValues.Qualify,
                correlationId);

            if (instances.Count == 1)
            {
                var existing = instances[0];
                var currentStageRef = existing.GetAttributeValue<EntityReference>(CrmSchema.Bpf.ActiveStageId);
                var currentPath = existing.GetAttributeValue<string>(CrmSchema.Bpf.TraversedPath);

                if (currentStageRef == null
                    || currentStageRef.Id != qualifyStageId
                    || !string.Equals(currentPath, qualifyPath, StringComparison.OrdinalIgnoreCase))
                {
                    var patch = new Entity(CrmSchema.Entities.OpportunitySalesProcess, existing.Id);
                    patch[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, qualifyStageId);
                    patch[CrmSchema.Bpf.TraversedPath] = qualifyPath;
                    writerService.Update(patch);
                }

                return existing.Id;
            }

            var bpfName = string.IsNullOrWhiteSpace(opportunityName)
                ? DefaultBpfName
                : opportunityName.Trim();
            if (bpfName.Length > MaxBpfNameLength)
            {
                bpfName = bpfName.Substring(0, MaxBpfNameLength);
            }

            var instance = new Entity(CrmSchema.Entities.OpportunitySalesProcess);
            instance[CrmSchema.Bpf.Name] = bpfName;
            instance[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, opportunityId);
            instance[CrmSchema.Bpf.ProcessId] = new EntityReference(CrmSchema.Entities.Workflow, CrmSchema.Bpf.WorkflowId);
            instance[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, qualifyStageId);
            instance[CrmSchema.Bpf.TraversedPath] = qualifyPath;

            return writerService.Create(instance);
        }

        public static void MirrorStageAdvance(
            IOrganizationService writerService,
            Guid opportunityId,
            int targetOpportunityStage,
            Guid correlationId)
        {
            var targetStageId = OpportunityLifecycleRules.GetBpfStageIdForOpportunityStage(targetOpportunityStage, correlationId);
            var traversedPath = OpportunityLifecycleRules.BuildTraversedPathForStage(targetOpportunityStage, correlationId);

            var singleInstance = RequireSingleInstance(writerService, opportunityId, correlationId);

            var patch = new Entity(CrmSchema.Entities.OpportunitySalesProcess, singleInstance.Id);
            patch[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, targetStageId);
            patch[CrmSchema.Bpf.TraversedPath] = traversedPath;
            writerService.Update(patch);
        }

        public static void MirrorClose(
            IOrganizationService writerService,
            Guid opportunityId,
            bool isWon,
            Guid correlationId)
        {
            var singleInstance = RequireSingleInstance(writerService, opportunityId, correlationId);

            var patch = new Entity(CrmSchema.Entities.OpportunitySalesProcess, singleInstance.Id);
            patch[CrmSchema.Bpf.StateCode] = new OptionSetValue(CrmSchema.Bpf.StateInactive);
            patch[CrmSchema.Bpf.StatusCode] = new OptionSetValue(
                isWon ? CrmSchema.Bpf.StatusFinished : CrmSchema.Bpf.StatusAborted);
            writerService.Update(patch);
        }

        private static Entity RequireSingleInstance(
            IOrganizationService writerService,
            Guid opportunityId,
            Guid correlationId)
        {
            var instances = RetrieveInstances(writerService, opportunityId, correlationId);
            if (instances.Count != 1)
            {
                CrmError.Throw(
                    CrmError.CRM006,
                    instances.Count == 0
                        ? "Opportunity Sales Process instance is missing."
                        : "Multiple Opportunity Sales Process instances exist for the same Opportunity.",
                    correlationId);
            }

            return instances[0];
        }

        private static DataCollection<Entity> RetrieveInstances(
            IOrganizationService writerService,
            Guid opportunityId,
            Guid correlationId)
        {
            if (writerService == null)
            {
                CrmError.Throw(CrmError.CRM006, "Scoped internal writer service is unavailable for BPF mirroring.", correlationId);
            }

            if (opportunityId == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM006, "Opportunity identifier is required for BPF mirroring.", correlationId);
            }

            var query = new QueryExpression(CrmSchema.Entities.OpportunitySalesProcess)
            {
                ColumnSet = new ColumnSet(
                    CrmSchema.Bpf.Id,
                    CrmSchema.Bpf.ActiveStageId,
                    CrmSchema.Bpf.TraversedPath,
                    CrmSchema.Bpf.StateCode,
                    CrmSchema.Bpf.StatusCode),
                TopCount = 2
            };

            query.Criteria.AddCondition(
                CrmSchema.Bpf.Opportunity,
                ConditionOperator.Equal,
                opportunityId);

            try
            {
                var result = writerService.RetrieveMultiple(query);
                return result != null && result.Entities != null
                    ? result.Entities
                    : new EntityCollection().Entities;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(
                    CrmError.CRM006,
                    "Failed to query Opportunity Sales Process instance for mirroring.",
                    correlationId);
                return null;
            }
        }
    }
}
